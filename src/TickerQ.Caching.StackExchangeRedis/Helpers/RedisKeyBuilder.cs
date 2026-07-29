#nullable disable
using System;
using System.Collections.Generic;
using StackExchange.Redis;
using TickerQ.Utilities.Enums;

namespace TickerQ.Caching.StackExchangeRedis.Helpers;

internal static class RedisKeyBuilder
{
    internal const string Prefix = "tq";
    internal const string TimeTickerIdsKey = $"{Prefix}:tt:ids";
    internal const string TimeTickerPendingKey = $"{Prefix}:tt:pending";
    internal const string CronIdsKey = $"{Prefix}:cron:ids";
    internal const string CronOccurrenceIdsKey = $"{Prefix}:co:ids";
    internal const string CronOccurrencePendingKey = $"{Prefix}:co:pending";
    internal const string TimeTickerRetentionSucceededKey = $"{Prefix}:tt:retention:succeeded";
    internal const string TimeTickerRetentionFailedKey = $"{Prefix}:tt:retention:failed";
    internal const string TimeTickerRetentionCancelledKey = $"{Prefix}:tt:retention:cancelled";
    internal const string TimeTickerRetentionSkippedKey = $"{Prefix}:tt:retention:skipped";
    internal const string CronOccurrenceRetentionSucceededKey = $"{Prefix}:co:retention:succeeded";
    internal const string CronOccurrenceRetentionFailedKey = $"{Prefix}:co:retention:failed";
    internal const string CronOccurrenceRetentionCancelledKey = $"{Prefix}:co:retention:cancelled";
    internal const string CronOccurrenceRetentionSkippedKey = $"{Prefix}:co:retention:skipped";
    internal const string RetentionReconciliationPhaseKey = $"{Prefix}:retention:reconcile:phase";
    internal const string RetentionReconciliationCursorKey = $"{Prefix}:retention:reconcile:cursor";
    internal const string RetentionReconciliationPendingKey = $"{Prefix}:retention:reconcile:pending";
    // These intentionally remain additive legacy-style keys. Because ticker and result keys also
    // lack a shared hash tag, the durable outbox capability is advertised only on standalone Redis.
    internal const string NodeFinalizationRecordsKey = $"{Prefix}:node-finalize:records";
    internal const string NodeFinalizationDueKey = $"{Prefix}:node-finalize:due";

    internal static string TimeTickerKey(Guid id) => $"{Prefix}:tt:{id}";
    internal static string TimeTickerResultKey(Guid id) => $"{Prefix}:tt:{id}:result";
    internal static string CronKey(Guid id) => $"{Prefix}:cron:{id}";
    internal static string CronOccurrenceKey(Guid id) => $"{Prefix}:co:{id}";
    internal static string CronOccurrenceResultKey(Guid id) => $"{Prefix}:co:{id}:result";
    internal static string CronOccurrencesByCronKey(Guid cronId) => $"{Prefix}:cron:{cronId}:occurrences";

    internal static double ToScore(DateTime utc) => utc.ToUniversalTime().Ticks;

    internal static bool CanAcquire(TickerStatus status, string currentHolder, string lockHolder)
    {
        return status is TickerStatus.Idle or TickerStatus.Queued &&
               (string.IsNullOrEmpty(currentHolder) || string.Equals(currentHolder, lockHolder, StringComparison.Ordinal));
    }

    internal static Guid[] ParseGuidSet(RedisValue[] members)
    {
        var result = new List<Guid>(members.Length);
        foreach (var member in members)
        {
            if (Guid.TryParse(member.ToString(), out var id))
                result.Add(id);
        }
        return result.ToArray();
    }
}
