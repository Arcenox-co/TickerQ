using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.DashboardDtos;

namespace TickerQ.Utilities.Infrastructure;

public sealed partial class TickerDashboardDataService<TTimeTicker, TCronTicker>
{
    private sealed class ChainSnapshot
    {
        public TTimeTicker Root { get; init; }
        public Dictionary<Guid, TTimeTicker> Jobs { get; } = new();
        public bool Complete { get; set; } = true;
        public int ChildCount => Jobs.Values.Count(x => x.ParentId == Root.Id);
        public ChainExecutionSummaryDto Summary => Complete
            ? ChainExecutionSummaryDto.FromStatuses(Jobs.Values
                .Where(x => !TickerChainInfrastructure.IsParallelGroup(x.Function))
                .Select(x => x.Status))
            : new ChainExecutionSummaryDto { Status = ChainExecutionStatus.Unavailable };
    }

    // Load only the roots on the displayed page and their descendants. One batched
    // query per level avoids a query per row/job. Redis keeps children embedded in
    // the root, while relational/Mongo providers return separate child rows.
    private async Task<Dictionary<Guid, ChainSnapshot>> ReadChainSnapshotsAsync(
        IEnumerable<Guid> ids, CancellationToken cancellationToken)
    {
        var rootIds = ids.Distinct().ToArray();
        var snapshots = new Dictionary<Guid, ChainSnapshot>();
        if (rootIds.Length == 0) return snapshots;
        var roots = await ChainMetadataQuery().AsNoTracking()
            .Where(x => Enumerable.Contains(rootIds, x.Id) && x.ParentId == null)
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
        var owners = new Dictionary<Guid, ChainSnapshot>();
        foreach (var root in roots)
        {
            var snapshot = new ChainSnapshot { Root = root };
            snapshot.Jobs.Add(root.Id, root);
            snapshots.Add(root.Id, snapshot);
            owners.Add(root.Id, snapshot);
        }

        var frontier = roots;
        for (var depth = 0; frontier.Length > 0; depth++)
        {
            var parentIds = frontier.Select(x => x.Id).ToArray();
            var limit = snapshots.Count * 500 + 1;
            var stored = await ChainMetadataQuery().AsNoTracking()
                .Where(x => x.ParentId.HasValue && Enumerable.Contains(parentIds, x.ParentId.Value))
                .OrderBy(x => x.Id).Take(limit).ToArrayAsync(cancellationToken).ConfigureAwait(false);
            if (stored.Length == limit)
            {
                foreach (var parent in frontier) owners[parent.Id].Complete = false;
                break;
            }
            var children = frontier.SelectMany(x => x.Children ?? Array.Empty<TTimeTicker>())
                .Concat(stored).GroupBy(x => x.Id).Select(x => x.Last());
            var next = new List<TTimeTicker>();
            foreach (var child in children)
            {
                if (!child.ParentId.HasValue || !owners.TryGetValue(child.ParentId.Value, out var owner)) continue;
                if (!owner.Complete) continue;
                if (depth >= 20 || owner.Jobs.Count >= 500 || owners.ContainsKey(child.Id))
                {
                    owner.Complete = false;
                    continue;
                }
                owner.Jobs.Add(child.Id, child);
                owners.Add(child.Id, owner);
                next.Add(child);
            }
            frontier = next.Where(x => owners[x.Id].Complete).ToArray();
        }
        return snapshots;
    }

    private async Task PopulateTimeChainSummariesAsync(IEnumerable<TimeTickerFlatDto> rows, CancellationToken ct)
    {
        var all = rows.ToArray();
        await PopulateDirectChildCountsAsync(all.Where(x => x.ParentId.HasValue), ct).ConfigureAwait(false);
        var items = all.Where(x => x.Type == ExecutionType.TimeTicker && x.ParentId == null).ToArray();
        var snapshots = await ReadChainSnapshotsAsync(items.Select(x => x.Id), ct).ConfigureAwait(false);
        foreach (var item in items)
            if (snapshots.TryGetValue(item.Id, out var chain))
            {
                item.ChildCount = chain.ChildCount;
                if (chain.Jobs.Count > 1 || !chain.Complete) item.ChainSummary = chain.Summary;
            }
    }

    private async Task PopulateDirectChildCountsAsync(IEnumerable<TimeTickerFlatDto> rows, CancellationToken ct)
    {
        var items = rows.ToArray();
        if (items.Length == 0) return;
        var ids = items.Select(x => x.Id).ToArray();
        var children = await ChainMetadataQuery()
            .Where(x => x.ParentId.HasValue && Enumerable.Contains(ids, x.ParentId.Value))
            .ToArrayAsync(ct).ConfigureAwait(false);
        var counts = children.GroupBy(x => x.ParentId.Value).ToDictionary(x => x.Key, x => x.Count());
        foreach (var item in items) item.ChildCount = Math.Max(item.ChildCount, counts.GetValueOrDefault(item.Id));
    }

    private async Task PopulateExecutionChainSummariesAsync(IEnumerable<ExecutionFlatDto> rows, CancellationToken ct)
    {
        var items = rows.ToArray();
        var snapshots = await ReadChainSnapshotsAsync(items
            .Where(x => x.Type == ExecutionType.TimeTicker || x.ChainRootId.HasValue)
            .Select(x => x.ChainRootId ?? x.Id), ct).ConfigureAwait(false);
        foreach (var item in items)
            if (snapshots.TryGetValue(item.ChainRootId ?? item.Id, out var chain) &&
                (item.Type == ExecutionType.TimeTicker || chain.Root.OriginCronTickerId == item.CronTickerId))
            {
                item.ChildCount = chain.ChildCount;
                if (item.ChainRootId.HasValue || chain.Jobs.Count > 1 || !chain.Complete)
                    item.ChainSummary = chain.Summary;
            }
    }

    private async Task PopulateCronChainSummariesAsync(IEnumerable<CronTickerFlatDto> rows, CancellationToken ct)
    {
        var items = rows.Where(x => x.LastRunChainRootId.HasValue).ToArray();
        var snapshots = await ReadChainSnapshotsAsync(items.Select(x => x.LastRunChainRootId.Value), ct).ConfigureAwait(false);
        foreach (var item in items)
            if (snapshots.TryGetValue(item.LastRunChainRootId.Value, out var chain) && chain.Root.OriginCronTickerId == item.Id)
                item.LastRunChainSummary = chain.Summary;
    }
}
