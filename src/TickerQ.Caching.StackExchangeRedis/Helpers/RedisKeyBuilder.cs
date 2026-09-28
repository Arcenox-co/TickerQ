#nullable disable
using System;
using System.Collections.Generic;
using StackExchange.Redis;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Models;

namespace TickerQ.Caching.StackExchangeRedis.Helpers;

internal sealed class RedisKeyBuilder
{
    internal const string Prefix = "tq";
    private static readonly RedisKeyBuilder Legacy = new(TickerQRuntimePartition.LegacyGlobal);
    private readonly string _prefix;

    internal RedisKeyBuilder(TickerQRuntimePartition partition)
    {
        Partition = partition ?? throw new ArgumentNullException(nameof(partition));
        HashTag = $"{{{partition.StorageKey}}}";
        _prefix = $"{Prefix}:{HashTag}";
    }

    internal TickerQRuntimePartition Partition { get; }
    internal string HashTag { get; }
    internal string PartitionPrefix => _prefix;
    private string Key(string suffix) => $"{_prefix}:{suffix}";

    internal string TimeTickerIds => Key("tt:ids");
    internal string TimeTickerPending => Key("tt:pending");
    internal string CronIds => Key("cron:ids");
    internal string CronRepairPhase => Key("cron:repair:phase");
    internal string CronRepairCursor => Key("cron:repair:cursor");
    internal string CronRepairPending => Key("cron:repair:pending");
    internal string CronRepairQuarantine => Key("cron:repair:quarantine");
    internal string CronOccurrenceRepairQuarantine => Key("co:repair:quarantine");
    internal string ReconciliationActivationMetadata => Key("reconciliation:activation");
    internal string ReconciliationActivationMetadataForScope(string scopeKey)
        => Key($"reconciliation:activation:scope:{scopeKey}");
    internal string CronOccurrenceIds => Key("co:ids");
    internal string CronOccurrencePending => Key("co:pending");
    internal string TimeTickerRetentionSucceeded => Key("tt:retention:succeeded");
    internal string TimeTickerRetentionFailed => Key("tt:retention:failed");
    internal string TimeTickerRetentionCancelled => Key("tt:retention:cancelled");
    internal string TimeTickerRetentionSkipped => Key("tt:retention:skipped");
    internal string CronOccurrenceRetentionSucceeded => Key("co:retention:succeeded");
    internal string CronOccurrenceRetentionFailed => Key("co:retention:failed");
    internal string CronOccurrenceRetentionCancelled => Key("co:retention:cancelled");
    internal string CronOccurrenceRetentionSkipped => Key("co:retention:skipped");
    internal string RetentionReconciliationPhase => Key("retention:reconcile:phase");
    internal string RetentionReconciliationCursor => Key("retention:reconcile:cursor");
    internal string RetentionReconciliationPending => Key("retention:reconcile:pending");
    internal string NodeFinalizationRecords => Key("node-finalize:records");
    internal string NodeFinalizationDue => Key("node-finalize:due");
    internal string TerminalMutationEvidence => Key("terminal:evidence");
    internal string NodesRegistry => Key("nodes:registry");
    internal string Heartbeat(string owner) => Key($"hb:{owner}");
    internal string TimeTicker(Guid id) => Key($"tt:{id}");
    internal string TimeTickerResult(Guid id) => Key($"tt:{id}:result");
    internal string Cron(Guid id) => Key($"cron:{id}");
    internal string CronOccurrence(Guid id) => Key($"co:{id}");
    internal string CronOccurrenceResult(Guid id) => Key($"co:{id}:result");
    internal string CronOccurrencesByCron(Guid cronId) => Key($"cron:{cronId}:occurrences");
    internal string CronOccurrenceSlot(Guid cronId, DateTime executionTime)
        => Key($"cron:{cronId}:slot:{executionTime.ToUniversalTime().Ticks}");
    internal string CronDocumentPrefix => Key("cron:");
    internal string CronOccurrenceDocumentPrefix => Key("co:");
    internal string CronSlotPrefix(Guid cronId) => Key($"cron:{cronId}:slot:");

    // Compatibility accessors represent only the explicit namespace-less LegacyGlobal partition.
    internal static string TimeTickerIdsKey => Legacy.TimeTickerIds;
    internal static string TimeTickerPendingKey => Legacy.TimeTickerPending;
    internal static string CronIdsKey => Legacy.CronIds;
    internal static string CronRepairPhaseKey => Legacy.CronRepairPhase;
    internal static string CronRepairCursorKey => Legacy.CronRepairCursor;
    internal static string CronRepairPendingKey => Legacy.CronRepairPending;
    internal static string CronRepairQuarantineKey => Legacy.CronRepairQuarantine;
    internal static string CronOccurrenceRepairQuarantineKey => Legacy.CronOccurrenceRepairQuarantine;
    internal static string ReconciliationActivationMetadataKey => Legacy.ReconciliationActivationMetadata;
    internal static string ReconciliationActivationMetadataKeyForScope(string scopeKey)
        => Legacy.ReconciliationActivationMetadataForScope(scopeKey);
    internal static string CronOccurrenceIdsKey => Legacy.CronOccurrenceIds;
    internal static string CronOccurrencePendingKey => Legacy.CronOccurrencePending;
    internal static string TimeTickerRetentionSucceededKey => Legacy.TimeTickerRetentionSucceeded;
    internal static string TimeTickerRetentionFailedKey => Legacy.TimeTickerRetentionFailed;
    internal static string TimeTickerRetentionCancelledKey => Legacy.TimeTickerRetentionCancelled;
    internal static string TimeTickerRetentionSkippedKey => Legacy.TimeTickerRetentionSkipped;
    internal static string CronOccurrenceRetentionSucceededKey => Legacy.CronOccurrenceRetentionSucceeded;
    internal static string CronOccurrenceRetentionFailedKey => Legacy.CronOccurrenceRetentionFailed;
    internal static string CronOccurrenceRetentionCancelledKey => Legacy.CronOccurrenceRetentionCancelled;
    internal static string CronOccurrenceRetentionSkippedKey => Legacy.CronOccurrenceRetentionSkipped;
    internal static string RetentionReconciliationPhaseKey => Legacy.RetentionReconciliationPhase;
    internal static string RetentionReconciliationCursorKey => Legacy.RetentionReconciliationCursor;
    internal static string RetentionReconciliationPendingKey => Legacy.RetentionReconciliationPending;
    internal static string NodeFinalizationRecordsKey => Legacy.NodeFinalizationRecords;
    internal static string NodeFinalizationDueKey => Legacy.NodeFinalizationDue;
    internal static string TerminalMutationEvidenceKey => Legacy.TerminalMutationEvidence;
    internal static string TimeTickerKey(Guid id) => Legacy.TimeTicker(id);
    internal static string TimeTickerResultKey(Guid id) => Legacy.TimeTickerResult(id);
    internal static string CronKey(Guid id) => Legacy.Cron(id);
    internal static string CronOccurrenceKey(Guid id) => Legacy.CronOccurrence(id);
    internal static string CronOccurrenceResultKey(Guid id) => Legacy.CronOccurrenceResult(id);
    internal static string CronOccurrencesByCronKey(Guid cronId) => Legacy.CronOccurrencesByCron(cronId);
    internal static string CronOccurrenceSlotKey(Guid cronId, DateTime executionTime)
        => Legacy.CronOccurrenceSlot(cronId, executionTime);

    internal static double ToScore(DateTime utc) => utc.ToUniversalTime().Ticks;
    internal static bool CanAcquire(TickerStatus status, string currentHolder, string lockHolder)
        => status is TickerStatus.Idle or TickerStatus.Queued &&
           (string.IsNullOrEmpty(currentHolder) || string.Equals(currentHolder, lockHolder, StringComparison.Ordinal));

    internal static Guid[] ParseGuidSet(RedisValue[] members)
    {
        var result = new List<Guid>(members.Length);
        foreach (var member in members)
            if (Guid.TryParse(member.ToString(), out var id)) result.Add(id);
        return result.ToArray();
    }
}
