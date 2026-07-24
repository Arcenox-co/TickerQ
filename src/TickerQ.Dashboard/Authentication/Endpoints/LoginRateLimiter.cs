using System;
using System.Collections.Concurrent;

namespace TickerQ.Dashboard.Authentication.Endpoints;

/// <summary>
/// Fixed-window in-memory brake on <c>/api/auth/login</c>: after
/// <see cref="MaxFailures"/> failed attempts from one client IP the endpoint
/// answers 429 until the window rolls over. A successful login clears the
/// counter. In-memory by design — each instance enforces the limit
/// independently, which is acceptable for a brute-force brake (N instances
/// just means an N× attempt budget), and avoids dragging a distributed cache
/// into the dashboard package.
/// </summary>
internal static class LoginRateLimiter
{
    internal const int MaxFailures = 5;
    internal static readonly TimeSpan Window = TimeSpan.FromMinutes(5);

    // Prune when the map grows past this to keep memory bounded under
    // spoofed-IP floods.
    private const int PruneThreshold = 10_000;

    private static readonly ConcurrentDictionary<string, Entry> Failures = new();

    private sealed class Entry
    {
        public int Count;
        public DateTime WindowStart;
    }

    public static bool IsBlocked(string clientKey, out TimeSpan retryAfter)
    {
        retryAfter = TimeSpan.Zero;
        if (!Failures.TryGetValue(clientKey, out var entry)) return false;

        var now = DateTime.UtcNow;
        if (now - entry.WindowStart >= Window)
        {
            Failures.TryRemove(clientKey, out _);
            return false;
        }

        if (entry.Count < MaxFailures) return false;

        retryAfter = entry.WindowStart + Window - now;
        return true;
    }

    public static void RecordFailure(string clientKey)
    {
        var now = DateTime.UtcNow;
        Failures.AddOrUpdate(
            clientKey,
            _ => new Entry { Count = 1, WindowStart = now },
            (_, entry) =>
            {
                if (now - entry.WindowStart >= Window)
                {
                    entry.Count = 1;
                    entry.WindowStart = now;
                }
                else
                {
                    entry.Count++;
                }
                return entry;
            });

        if (Failures.Count > PruneThreshold)
            Prune(now);
    }

    public static void RecordSuccess(string clientKey)
        => Failures.TryRemove(clientKey, out _);

    private static void Prune(DateTime now)
    {
        foreach (var kvp in Failures)
        {
            if (now - kvp.Value.WindowStart >= Window)
                Failures.TryRemove(kvp.Key, out _);
        }
    }
}
