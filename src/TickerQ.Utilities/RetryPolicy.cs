using System;

namespace TickerQ.Utilities
{
    /// <summary>
    /// Generators for the <c>RetryIntervals</c> arrays tickers carry, so callers
    /// don't hand-roll <c>[5, 10, 20, 40]</c>. Purely a convenience — the output
    /// is a plain seconds array usable anywhere intervals are accepted (entities,
    /// dashboard API, chain nodes).
    /// </summary>
    public static class RetryPolicy
    {
        /// <summary>Same delay before every retry: <c>Fixed(30, 3)</c> → [30, 30, 30].</summary>
        public static int[] Fixed(int intervalSeconds, int retries)
        {
            if (intervalSeconds < 1) throw new ArgumentOutOfRangeException(nameof(intervalSeconds), "Interval must be at least 1 second.");
            if (retries < 1) throw new ArgumentOutOfRangeException(nameof(retries), "Retries must be at least 1.");

            var intervals = new int[retries];
            Array.Fill(intervals, intervalSeconds);
            return intervals;
        }

        /// <summary>
        /// Doubling backoff capped at <paramref name="maxSeconds"/>:
        /// <c>Exponential(5, 5)</c> → [5, 10, 20, 40, 80]. With
        /// <paramref name="jitter"/>, each interval is randomized ±20% so a burst
        /// of failures doesn't retry in lockstep (thundering herd).
        /// </summary>
        public static int[] Exponential(int baseSeconds, int retries, int maxSeconds = 3600, bool jitter = false)
        {
            if (baseSeconds < 1) throw new ArgumentOutOfRangeException(nameof(baseSeconds), "Base must be at least 1 second.");
            if (retries < 1) throw new ArgumentOutOfRangeException(nameof(retries), "Retries must be at least 1.");
            if (maxSeconds < baseSeconds) throw new ArgumentOutOfRangeException(nameof(maxSeconds), "Cap must be >= the base interval.");

            var intervals = new int[retries];
            var current = (double)baseSeconds;

            for (var i = 0; i < retries; i++)
            {
                var seconds = Math.Min(current, maxSeconds);

                if (jitter)
                {
                    // ±20%, but never below 1s and never above the cap.
                    var factor = 0.8 + Random.Shared.NextDouble() * 0.4;
                    seconds = Math.Min(seconds * factor, maxSeconds);
                }

                intervals[i] = Math.Max(1, (int)Math.Round(seconds));
                current *= 2;
            }

            return intervals;
        }
    }
}
