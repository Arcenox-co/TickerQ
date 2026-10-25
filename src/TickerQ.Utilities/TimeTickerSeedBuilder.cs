using System;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Managers;

namespace TickerQ.Utilities
{
    /// <summary>
    /// Declares one-time work this application owns, using the same builder as
    /// <c>ITimeTickerManager.AddChainAsync</c>. When a new reconciliation epoch activates, TickerQ creates
    /// each declared ticker once per key. A key that already has a ticker is left untouched, so restarts,
    /// epoch bumps, and concurrent replicas never create duplicates. To run the work again, change the key
    /// (for example <c>import-v2</c>).
    /// </summary>
    public sealed class TimeTickerSeedBuilder<TTimeTicker> where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
    {
        internal const string Kind = "time ticker";

        internal TickerSeedEntries<TimeTickerChainBuilder<TTimeTicker>> Entries { get; } = new();

        /// <summary>
        /// Seeds a single run of a registered function. The function name is the ticker's key.
        /// </summary>
        /// <param name="functionName">Name of a registered function.</param>
        /// <param name="executionTime">When to run; <see langword="null"/> runs as soon as the scheduler starts.</param>
        public TimeTickerSeedBuilder<TTimeTicker> Add(string functionName, DateTime? executionTime = null)
        {
            if (string.IsNullOrWhiteSpace(functionName))
                throw new ArgumentException("A seeded time ticker needs a function name.", nameof(functionName));
            var function = functionName.Trim();
            return Add(function, ticker =>
            {
                if (executionTime.HasValue) ticker.RunAt(executionTime.Value);
                ticker.StartWith(function);
            });
        }

        /// <summary>
        /// Seeds a ticker configured with the chain builder: run time, schedule policy, request, retries,
        /// late-start rules, and chain steps. The root function name is the ticker's key.
        /// </summary>
        public TimeTickerSeedBuilder<TTimeTicker> Add(Action<TimeTickerChainBuilder<TTimeTicker>> configure)
        {
            Entries.Add(null, configure, Kind);
            return this;
        }

        /// <summary>
        /// Seeds a ticker with an explicit stable key, for example to run one function more than once or to
        /// version one-time work. A new key creates a new ticker; the old one is kept.
        /// </summary>
        public TimeTickerSeedBuilder<TTimeTicker> Add(string key, Action<TimeTickerChainBuilder<TTimeTicker>> configure)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            Entries.Add(key, configure, Kind);
            return this;
        }
    }
}
