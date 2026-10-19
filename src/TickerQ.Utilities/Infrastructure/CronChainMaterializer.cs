using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Models;

namespace TickerQ.Utilities.Infrastructure;

[JsonSerializable(typeof(CronChainTemplate))]
internal partial class CronChainJsonContext : JsonSerializerContext { }

public static class CronChainMaterializer
{
    public const string OverlapSkippedReason = "A previous chain for this cron schedule still has unfinished jobs.";
    public static string Serialize(CronChainTemplate value) => value == null ? null :
        JsonSerializer.Serialize(value, CronChainJsonContext.Default.CronChainTemplate);
    public static CronChainTemplate Deserialize(string value) => value == null ? null :
        JsonSerializer.Deserialize(value, CronChainJsonContext.Default.CronChainTemplate);

    public static bool MatchesPartition(InternalFunctionContext context, string partition) =>
        context != null && context.Type == TickerType.CronTickerOccurrence && StringComparer.Ordinal.Equals(context.RuntimePartitionKey ?? TickerQRuntimePartition.LegacyGlobal.StorageKey, partition);

    public static bool IsUnfinished(TickerStatus status) =>
        status is TickerStatus.Idle or TickerStatus.Queued or TickerStatus.InProgress;

    // Bounded and cycle checked before serialization or recursive graph construction.
    public static void ValidateShape(CronChainTemplate template)
    {
        if (template == null) return;
        if (!Enum.IsDefined(template.DeleteBehavior))
            throw new ArgumentException("Unknown recurring chain policy.");
        if (template.Children == null || template.Children.Count == 0)
            throw new ArgumentException("A recurring chain needs at least one child.");
        var visited = new HashSet<CronChainNode>();
        var pending = new Stack<(CronChainNode Node, int Depth)>();
        foreach (var child in template.Children) pending.Push((child, 1));
        while (pending.TryPop(out var item))
        {
            var node = item.Node;
            if (node == null || !visited.Add(node))
                throw new ArgumentException("Recurring chain nodes must be non-null and cannot be shared or cyclic.");
            if (item.Depth > 20 || visited.Count >= 500)
                throw new ArgumentException("Recurring chains support up to 20 child levels and 500 jobs including the root.");
            if (string.IsNullOrWhiteSpace(node.Function) || !Enum.IsDefined(node.RunCondition) ||
                !Enum.IsDefined(node.OnStale) || node.Retries < 0 ||
                (node.RetryIntervals?.Any(x => x < 0) ?? false))
                throw new ArgumentException("Each chain job needs a function and valid run condition, retries and stale policy.");
            foreach (var child in node.Children ?? new()) pending.Push((child, item.Depth + 1));
        }
    }

    public static TTimeTicker Build<TTimeTicker>(CronTickerEntity cron, Guid occurrenceId, DateTime now)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
    {
        ValidateShape(cron.Chain);
        if (cron.Chain == null) throw new ArgumentException("No recurring chain is configured.");
        var root = new TTimeTicker
        {
            Id = occurrenceId, ChainRootId = occurrenceId, OriginCronTickerId = cron.Id,
            ApplicationNamespaceKey = cron.ApplicationNamespaceKey,
            InitIdentifier = "cron-chain/" + occurrenceId.ToString("N"),
            Function = cron.Function, Description = cron.Description, Request = cron.Request?.ToArray(),
            RequestContractVersion = cron.RequestContractVersion,
            RequestContractFingerprint = cron.RequestContractFingerprint,
            Retries = cron.Retries, RetryIntervals = cron.RetryIntervals?.ToArray(),
            TimeoutSeconds = cron.TimeoutSeconds, OnStale = cron.OnStale,
            ExecutionTime = now, CreatedAt = now, UpdatedAt = now,
            DeleteBehavior = cron.Chain.DeleteBehavior
        };
        void AddChildren(TTimeTicker parent, IEnumerable<CronChainNode> nodes)
        {
            foreach (var node in nodes ?? Array.Empty<CronChainNode>())
            {
                var child = new TTimeTicker
                {
                    Id = Guid.NewGuid(), ParentId = parent.Id, ChainRootId = root.Id,
                    OriginCronTickerId = cron.Id, ApplicationNamespaceKey = cron.ApplicationNamespaceKey,
                    Function = node.Function, Description = node.Description, Request = node.Request?.ToArray(),
                    RequestContractVersion = node.RequestContractVersion,
                    RequestContractFingerprint = node.RequestContractFingerprint,
                    Retries = node.Retries, RetryIntervals = node.RetryIntervals?.ToArray(),
                    RunCondition = node.RunCondition, TimeoutSeconds = node.TimeoutSeconds, OnStale = node.OnStale,
                    CreatedAt = now, UpdatedAt = now
                };
                parent.Children.Add(child);
                AddChildren(child, node.Children);
            }
        }
        AddChildren(root, cron.Chain.Children);
        return root;
    }

    public static void Complete<TCronTicker>(CronTickerOccurrenceEntity<TCronTicker> occurrence,
        bool skipped, bool isDue, DateTime now) where TCronTicker : CronTickerEntity
    {
        occurrence.ChainRootId = skipped ? null : occurrence.Id;
        occurrence.Status = skipped ? TickerStatus.Skipped : isDue ? TickerStatus.DueDone : TickerStatus.Done;
        occurrence.SkippedReason = skipped ? OverlapSkippedReason : null;
        occurrence.ExecutedAt = now;
        occurrence.UpdatedAt = now;
        occurrence.LockHolder = null;
        occurrence.LockedAt = null;
        occurrence.LeaseUntil = null;
        occurrence.AcquisitionToken = null;
    }
}
