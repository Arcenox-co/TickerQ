using System;
using System.Collections.Generic;
using System.Linq;

namespace TickerQ.Utilities
{
    /// <summary>
    /// Declares the cron tickers this application owns. TickerQ reconciles the declared list when a new
    /// reconciliation epoch activates: each cron is created once, updated in place when its definition
    /// changes, and retired (disabled, never deleted) after the grace period when it is removed.
    /// Restarts with the same epoch, and concurrent replicas, never create duplicates.
    /// </summary>
    public sealed class CronTickerSeedBuilder
    {
        private readonly List<CronTickerSeedDefinition> _seeds = new();

        internal IReadOnlyList<CronTickerSeedDefinition> Seeds => _seeds;

        /// <summary>
        /// Seeds a cron ticker for a registered function.
        /// </summary>
        /// <param name="functionName">Name of a registered <c>[TickerFunction]</c>.</param>
        /// <param name="expression">
        /// A 5- or 6-field cron expression in the scheduler time zone, or a configuration placeholder such as
        /// <c>%Reports:Cron%</c> that is resolved from <c>IConfiguration</c> at startup.
        /// </param>
        /// <param name="configure">Optional key, retry, and timeout settings.</param>
        public CronTickerSeedBuilder Add(string functionName, string expression,
            Action<CronTickerSeedOptions> configure = null)
        {
            if (string.IsNullOrWhiteSpace(functionName))
                throw new ArgumentException("A seeded cron ticker needs a function name.", nameof(functionName));
            if (string.IsNullOrWhiteSpace(expression))
                throw new ArgumentException(
                    $"Seeded cron ticker '{functionName}' needs a cron expression.", nameof(expression));

            var options = new CronTickerSeedOptions();
            configure?.Invoke(options);
            var definition = new CronTickerSeedDefinition(functionName.Trim(), expression.Trim(),
                options.Key ?? functionName.Trim(), options.Retries, options.RetryIntervals, options.TimeoutSeconds);

            if (_seeds.Any(seed => string.Equals(seed.Key, definition.Key, StringComparison.Ordinal)))
                throw new InvalidOperationException(
                    $"A cron ticker with key '{definition.Key}' is already seeded. Use WithKey(...) to seed the same function on several schedules.");

            _seeds.Add(definition);
            return this;
        }
    }

    /// <summary>Settings for one seeded cron ticker.</summary>
    public sealed class CronTickerSeedOptions
    {
        internal string Key { get; private set; }
        internal int Retries { get; private set; }
        internal int[] RetryIntervals { get; private set; }
        internal int? TimeoutSeconds { get; private set; }

        /// <summary>
        /// Stable identity of this cron within the application namespace. Defaults to the function name.
        /// Set it to seed one function on several schedules. Changing it later replaces the cron with a new one.
        /// </summary>
        public CronTickerSeedOptions WithKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("A seeded cron key cannot be empty.", nameof(key));
            Key = key.Trim();
            return this;
        }

        /// <summary>Retries a failed run. Intervals are seconds between attempts; the last one repeats.</summary>
        public CronTickerSeedOptions WithRetries(int retries, params int[] intervalSeconds)
        {
            if (retries < 0)
                throw new ArgumentOutOfRangeException(nameof(retries), retries, "Retries cannot be negative.");
            if (intervalSeconds?.Any(interval => interval < 0) == true)
                throw new ArgumentOutOfRangeException(nameof(intervalSeconds), "Retry intervals cannot be negative.");
            Retries = retries;
            RetryIntervals = intervalSeconds is { Length: > 0 } ? intervalSeconds.ToArray() : null;
            return this;
        }

        /// <summary>Cancels a run that takes longer than <paramref name="timeout"/>.</summary>
        public CronTickerSeedOptions WithTimeout(TimeSpan timeout)
        {
            if (timeout <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(timeout), timeout, "The timeout must be positive.");
            TimeoutSeconds = checked((int)Math.Ceiling(timeout.TotalSeconds));
            return this;
        }
    }

    internal sealed record CronTickerSeedDefinition(
        string Function,
        string Expression,
        string Key,
        int Retries,
        int[] RetryIntervals,
        int? TimeoutSeconds);
}
