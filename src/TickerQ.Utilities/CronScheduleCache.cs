namespace TickerQ.Utilities;

using System;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using NCrontab;

public static partial class CronScheduleCache
{
    // Cache: cron expression (normalized) -> parsed schedule (created once)
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
        => Get(Normalize(expression))?.GetNextOccurrence(dateTime);
    

    public static bool Invalidate(string expression) =>
        Cache.TryRemove(Normalize(expression), out _);

    public static void Clear() => Cache.Clear();
    
    [GeneratedRegex(@"\s+")]
    private static partial Regex ReplaceRegex();
}