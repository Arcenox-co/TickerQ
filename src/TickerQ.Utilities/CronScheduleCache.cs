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
        var parsed = Get(Normalize(expression));
        
        if (parsed == null)
            return null;
        
        var localTime = TimeZoneInfo.ConvertTimeFromUtc(dateTime, TimeZoneInfo);
        
        var nextOccurrence = parsed.GetNextOccurrence(localTime);
        
        var utcDateTime = TimeZoneInfo.ConvertTimeToUtc(nextOccurrence, TimeZoneInfo);
        
        return utcDateTime;
    }

    public static bool Invalidate(string expression) =>
        Cache.TryRemove(Normalize(expression), out _);
    
    [GeneratedRegex(@"\s+")]
    private static partial Regex ReplaceRegex();
}