using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using TickerQ.Utilities.Entities;

namespace TickerQ.Utilities.Models;

public enum TimeTickerChainMalformedKind
{
    Orphan,
    Cycle
}

public sealed class TimeTickerChainRepairException : InvalidOperationException
{
    public TimeTickerChainMalformedKind Kind { get; }
    public Guid TickerId { get; }
    public Guid? ParentId { get; }

    internal TimeTickerChainRepairException(
        TimeTickerChainMalformedKind kind, Guid tickerId, Guid? parentId)
        : base(kind == TimeTickerChainMalformedKind.Orphan
            ? $"TickerQ TimeTicker '{tickerId}' references missing ParentId '{parentId}'. Restore the parent or correct ParentId before retrying chain repair."
            : $"TickerQ TimeTicker graph contains a ParentId cycle reachable from ticker '{tickerId}'. Break the cycle before retrying chain repair.")
    {
        Kind = kind;
        TickerId = tickerId;
        ParentId = parentId;
    }
}

public sealed record TimeTickerChainRepairResult(
    int ScannedRows,
    int RepairedRows,
    int UnchangedRows)
{
    public static TimeTickerChainRepairResult Empty { get; } = new(0, 0, 0);
}

internal sealed record TimeTickerChainRepairUpdate<TTimeTicker>(
    TTimeTicker Row,
    Guid ChainRootId,
    Guid? ChainGeneration)
    where TTimeTicker : TimeTickerEntity<TTimeTicker>;

internal sealed record TimeTickerChainRepairPlan<TTimeTicker>(
    TimeTickerChainRepairResult Result,
    IReadOnlyList<TimeTickerChainRepairUpdate<TTimeTicker>> Updates,
    TimeTickerChainRepairException Malformed)
    where TTimeTicker : TimeTickerEntity<TTimeTicker>
{
    internal void ThrowIfMalformed()
    {
        if (Malformed != null) throw Malformed;
    }
}

internal static class TimeTickerChainRepairPlanner
{
    internal static TimeTickerChainRepairPlan<TTimeTicker> Create<TTimeTicker>(
        IReadOnlyCollection<TTimeTicker> rows,
        CancellationToken cancellationToken)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>
    {
        var byId = rows.ToDictionary(x => x.Id);
        var resolved = new Dictionary<Guid, (Guid RootId, Guid? Generation)>();
        TimeTickerChainRepairException malformed = null;

        (Guid RootId, Guid? Generation) Resolve(TTimeTicker start)
        {
            if (resolved.TryGetValue(start.Id, out var known)) return known;
            var path = new List<TTimeTicker>();
            var pathIds = new HashSet<Guid>();
            var current = start;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (resolved.TryGetValue(current.Id, out known)) break;
                if (!pathIds.Add(current.Id))
                {
                    malformed ??= new TimeTickerChainRepairException(
                        TimeTickerChainMalformedKind.Cycle, current.Id, current.ParentId);
                    return default;
                }
                path.Add(current);
                if (!current.ParentId.HasValue)
                {
                    known = (current.Id, current.ChainGeneration);
                    break;
                }
                if (!byId.TryGetValue(current.ParentId.Value, out current))
                {
                    malformed ??= new TimeTickerChainRepairException(
                        TimeTickerChainMalformedKind.Orphan, path[^1].Id, path[^1].ParentId);
                    return default;
                }
            }

            foreach (var node in path) resolved[node.Id] = known;
            return known;
        }

        foreach (var row in rows.OrderBy(x => x.Id))
        {
            Resolve(row);
            if (malformed != null)
                return new TimeTickerChainRepairPlan<TTimeTicker>(
                    new TimeTickerChainRepairResult(rows.Count, 0, 0),
                    Array.Empty<TimeTickerChainRepairUpdate<TTimeTicker>>(), malformed);
        }

        var updates = rows
            .Where(row => row.ChainRootId != resolved[row.Id].RootId ||
                          row.ChainGeneration != resolved[row.Id].Generation)
            .Select(row => new TimeTickerChainRepairUpdate<TTimeTicker>(
                row, resolved[row.Id].RootId, resolved[row.Id].Generation))
            .ToArray();
        return new TimeTickerChainRepairPlan<TTimeTicker>(
            new TimeTickerChainRepairResult(rows.Count, updates.Length, rows.Count - updates.Length),
            updates, null);
    }
}
