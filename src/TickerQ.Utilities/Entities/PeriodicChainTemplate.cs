using System.Collections.Generic;
using TickerQ.Utilities.Enums;

namespace TickerQ.Utilities.Entities
{
    /// <summary>
    /// A lightweight, serializable definition of a single step in a periodic ticker's job chain.
    /// This is a *template* (not an execution row): on every periodic fire it is materialized into a
    /// fresh <see cref="TimeTickerEntity"/> graph, which the existing TimeTicker chaining engine then runs.
    /// </summary>
    public sealed class PeriodicChainStep
    {
        /// <summary>
        /// The registered ticker function name to execute for this step.
        /// </summary>
        public string Function { get; set; }

        /// <summary>
        /// The condition (relative to the parent step's final status) under which this step runs.
        /// </summary>
        public RunCondition RunCondition { get; set; } = RunCondition.OnSuccess;

        /// <summary>
        /// Number of retry attempts for this step.
        /// </summary>
        public int Retries { get; set; }

        /// <summary>
        /// Intervals (in seconds) between retry attempts for this step.
        /// </summary>
        public int[] RetryIntervals { get; set; }

        /// <summary>
        /// Serialized request payload for this step.
        /// </summary>
        public byte[] Request { get; set; }

        /// <summary>
        /// Following steps. For a STRICTLY SEQUENTIAL chain there must be EXACTLY ONE element per level
        /// (a spine). Multiple elements at the same level form a parallel fan-out.
        /// </summary>
        public List<PeriodicChainStep> Children { get; set; } = new List<PeriodicChainStep>();
    }
}


