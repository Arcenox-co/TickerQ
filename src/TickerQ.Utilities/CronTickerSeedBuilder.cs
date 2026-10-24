using System;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Managers;

namespace TickerQ.Utilities
{
    /// <summary>
    /// Declares the cron tickers this application owns, using the same builder as
    /// <c>ICronTickerManager.AddChainAsync</c>. TickerQ reconciles the declared list when a new
    /// reconciliation epoch activates: each cron is created once, updated in place when its definition
    /// changes, and retired (disabled, never deleted) after the grace period when it is removed.
    /// Restarts with the same epoch, and concurrent replicas, never create duplicates.
    /// </summary>
    public sealed class CronTickerSeedBuilder<TCronTicker> where TCronTicker : CronTickerEntity, new()
    {
        internal const string Kind = "cron ticker";

        internal TickerSeedEntries<CronTickerChainBuilder<TCronTicker>> Entries { get; } = new();

        /// <summary>
        /// Seeds a plain cron for a registered function. The function name is the cron's key.
        /// </summary>
        /// <param name="functionName">Name of a registered function.</param>
        /// <param name="expression">
        /// A 5- or 6-field cron expression in the scheduler time zone, or a configuration placeholder such
        /// as <c>%Reports:Cron%</c> resolved from <c>IConfiguration</c> at startup.
        /// </param>
        public CronTickerSeedBuilder<TCronTicker> Add(string functionName, string expression)
        {
            if (string.IsNullOrWhiteSpace(functionName))
                throw new ArgumentException("A seeded cron ticker needs a function name.", nameof(functionName));
            if (string.IsNullOrWhiteSpace(expression))
                throw new ArgumentException(
                    $"Seeded cron ticker '{functionName}' needs a cron expression.", nameof(expression));
            var function = functionName.Trim();
            return Add(function, cron => cron.WithExpression(expression).StartWith(function));
        }

        /// <summary>
        /// Seeds a cron configured with the recurring-chain builder: expression, time zone, schedule
        /// policy, request, retries, late-start and catch-up rules, overlap, and chain steps. The root
        /// function name is the cron's key.
        /// </summary>
        public CronTickerSeedBuilder<TCronTicker> Add(Action<CronTickerChainBuilder<TCronTicker>> configure)
        {
            Entries.Add(null, configure, Kind);
            return this;
        }

        /// <summary>
        /// Seeds a cron with an explicit stable key, for example to run one function on several
        /// schedules. Changing the key later replaces the cron with a new one.
        /// </summary>
        public CronTickerSeedBuilder<TCronTicker> Add(string key, Action<CronTickerChainBuilder<TCronTicker>> configure)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            Entries.Add(key, configure, Kind);
            return this;
        }
    }
}
