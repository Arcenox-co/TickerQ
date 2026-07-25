using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace TickerQ.Dashboard.Authentication.Endpoints;

/// <summary>
/// Bounded fixed-window in-memory brute-force brake. Callers use independent
/// buckets for both client IP and normalized username so address rotation does
/// not bypass account protection.
/// </summary>
internal static class LoginRateLimiter
{
    internal const int MaxFailures = 5;
    internal static readonly TimeSpan Window = TimeSpan.FromMinutes(5);
    internal const int Capacity = 10_000;

    private sealed record Entry(int Count, DateTime WindowStart);
    private static readonly ConcurrentDictionary<string, Entry> Failures = new();

    public static bool IsBlocked(string key, out TimeSpan retryAfter)
    {
        retryAfter = TimeSpan.Zero;
        if (!Failures.TryGetValue(key, out var entry)) return false;

        var now = DateTime.UtcNow;
        if (now - entry.WindowStart >= Window)
        {
            Failures.TryRemove(new KeyValuePair<string, Entry>(key, entry));
            return false;
        }

        if (entry.Count < MaxFailures) return false;
        retryAfter = entry.WindowStart + Window - now;
        return true;
    }

    public static void RecordFailure(string key)
    {
        var now = DateTime.UtcNow;
        Failures.AddOrUpdate(
            key,
            _ => new Entry(1, now),
            (_, entry) => now - entry.WindowStart >= Window
                ? new Entry(1, now)
                : entry with { Count = entry.Count + 1 });

        if (Failures.Count > Capacity)
            PruneAndBound(now);
    }

    public static void RecordSuccess(string key) => Failures.TryRemove(key, out _);

    public static string AccountKey(string username)
    {
        var normalized = (username ?? string.Empty).Trim().ToUpperInvariant();
        return "user:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
    }

    private static void PruneAndBound(DateTime now)
    {
        foreach (var pair in Failures)
        {
            if (now - pair.Value.WindowStart >= Window)
                Failures.TryRemove(new KeyValuePair<string, Entry>(pair.Key, pair.Value));
        }

        var excess = Failures.Count - Capacity;
        if (excess <= 0) return;
        foreach (var pair in Failures.OrderBy(x => x.Value.WindowStart).Take(excess))
            Failures.TryRemove(new KeyValuePair<string, Entry>(pair.Key, pair.Value));
    }

    internal static int CurrentCount => Failures.Count;
    internal static void ResetForTests() => Failures.Clear();
}
