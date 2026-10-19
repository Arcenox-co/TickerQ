using System;

namespace TickerQ.Utilities.Models
{
    /// <summary>One terminal failure, delivered to <c>ITickerQFailureNotifier</c>.</summary>
    public class TickerFailureEvent
    {
        /// <summary>"failed" | "timeout_cancelled" | "stale_recovered".</summary>
        public string Kind { get; set; }
        public Guid TickerId { get; set; }
        public string Function { get; set; }
        /// <summary>"TimeTicker" | "CronTickerOccurrence" — empty for sweep summaries.</summary>
        public string TickerType { get; set; }
        public string Reason { get; set; }
        public int RetryCount { get; set; }
        public int Retries { get; set; }
        public DateTime OccurredAtUtc { get; set; }
        public string Node { get; set; }
    }
}
