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

    internal static string TimeTickerKey(Guid id) => $"{Prefix}:tt:{id}";
    internal static string CronKey(Guid id) => $"{Prefix}:cron:{id}";
    internal static string CronOccurrenceKey(Guid id) => $"{Prefix}:co:{id}";
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
