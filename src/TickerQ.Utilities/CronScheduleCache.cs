namespace TickerQ.Utilities;

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NCrontab;

internal static partial class CronScheduleCache
{
    public static TimeZoneInfo TimeZoneInfo { get; internal set; } = TimeZoneInfo.Local;
    
    // Bound both entry count and retained key length; unusual long expressions still parse.
    private static readonly BoundedScheduleCache Cache = new(4096, 1024);

    private static readonly CrontabSchedule.ParseOptions Opts = new()
    {
        IncludingSeconds = true
    };

    private static string Normalize(string expr)
    {
        ArgumentNullException.ThrowIfNull(expr);

        var collapsed = ReplaceRegex().Replace(expr.Trim(), " ");

        // Standard 5-part cron (no seconds) — prepend "0" so NCrontab's
        // IncludingSeconds parser accepts it. Keeps 5- and 6-part spellings
        // of the same schedule on one cache entry.
        return CountParts(collapsed) == 5 ? "0 " + collapsed : collapsed;
    }

    private static int CountParts(string expr)
    {
        if (expr.Length == 0)
            return 0;

        var parts = 1;
        foreach (var ch in expr)
            if (ch == ' ')
                parts++;
        return parts;
    }

    public static CrontabSchedule Get(string expression)
    {
        var key = Normalize(expression);

        return Cache.Get(key);
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
        Cache.Invalidate(Normalize(expression));

    internal sealed class BoundedScheduleCache(int capacity, int maximumKeyLength)
    {
        private readonly object _gate = new();
        private readonly Dictionary<string, LinkedListNode<(string Key, CrontabSchedule Schedule)>> _entries = new(StringComparer.Ordinal);
        private readonly LinkedList<(string Key, CrontabSchedule Schedule)> _recency = new();
        internal int Count { get { lock (_gate) return _entries.Count; } }

        internal CrontabSchedule Get(string key)
        {
            lock (_gate)
                if (_entries.TryGetValue(key, out var hit))
                {
                    _recency.Remove(hit);
                    _recency.AddFirst(hit);
                    return hit.Value.Schedule;
                }
            var parsed = CrontabSchedule.TryParse(key, Opts);
            // Invalid input and oversized keys do not displace frequently used schedules.
            if (parsed == null || key.Length > maximumKeyLength || capacity <= 0) return parsed;
            lock (_gate)
            {
                if (_entries.TryGetValue(key, out var raced)) return raced.Value.Schedule;
                if (_entries.Count >= capacity)
                {
                    _entries.Remove(_recency.Last.Value.Key);
                    _recency.RemoveLast();
                }
                _entries.Add(key, _recency.AddFirst((key, parsed)));
                return parsed;
            }
        }

        internal bool Invalidate(string key)
        {
            lock (_gate)
            {
                if (!_entries.Remove(key, out var node)) return false;
                _recency.Remove(node);
                return true;
            }
        }
    }
    
    [GeneratedRegex(@"\s+")]
    private static partial Regex ReplaceRegex();
}
