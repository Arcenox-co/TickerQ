namespace TickerQ.Utilities;

using System;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using NCrontab;

internal static partial class CronScheduleCache
{
    public static TimeZoneInfo TimeZoneInfo { get; internal set; } = TimeZoneInfo.Local;
    
    private static readonly ConcurrentDictionary<string, CrontabSchedule> Cache = new(StringComparer.Ordinal);

    private static readonly CrontabSchedule.ParseOptions Opts = new()
    {
        IncludingSeconds = true
    };

    private static string Normalize(string expr)
    {
        ArgumentNullException.ThrowIfNull(expr);
        
        return ReplaceRegex().Replace(expr.Trim(), " "); 
    }

    public static CrontabSchedule Get(string expression)
    {
        var key = Normalize(expression);

        return Cache.GetOrAdd(key, exp => CrontabSchedule.TryParse(exp, Opts));
    }

    public static DateTime? GetNextOccurrenceOrDefault(string expression, DateTime dateTime)
    {
        var parsed = Get(expression);

        if (parsed == null)
            return null;

        var localTime = TimeZoneInfo.ConvertTimeFromUtc(dateTime, TimeZoneInfo);

        var nextOccurrence = parsed.GetNextOccurrence(localTime);

        try
        {
            var utcDateTime = TimeZoneInfo.ConvertTimeToUtc(nextOccurrence, TimeZoneInfo);
            return utcDateTime;
        }
        catch (ArgumentException)
        {
            // DST gap: the local time produced by NCrontab doesn't exist
            // (e.g., spring-forward skips 2:00→3:00). Advance past the gap
            // by finding the next valid UTC instant after the invalid time,
            // then ask NCrontab for the next occurrence from that point.
            // Loop handles the (unlikely) case of consecutive gaps.
            var candidate = nextOccurrence;
            for (var i = 0; i < 24; i++)
            {
                candidate = candidate.AddHours(1);
                try
                {
                    var utc = TimeZoneInfo.ConvertTimeToUtc(candidate, TimeZoneInfo);
                    // candidate is valid — get the real next occurrence from here
                    var local = TimeZoneInfo.ConvertTimeFromUtc(utc, TimeZoneInfo);
                    var retryOccurrence = parsed.GetNextOccurrence(local);
                    return TimeZoneInfo.ConvertTimeToUtc(retryOccurrence, TimeZoneInfo);
                }
                catch (ArgumentException)
                {
                    // Still in a gap — keep advancing
                }
            }

            // Exhausted retries — should never happen in practice
            return null;
        }
    }

    public static bool Invalidate(string expression) =>
        Cache.TryRemove(Normalize(expression), out _);
    
    [GeneratedRegex(@"\s+")]
    private static partial Regex ReplaceRegex();
}