using System.Collections.Generic;
using System.Diagnostics.Metrics;

namespace TickerQ.Utilities.Instrumentation
{
    /// <summary>
    /// OpenTelemetry-compatible metrics for TickerQ, emitted alongside the
    /// existing activity tracing. Subscribe by adding the meter name to your
    /// metrics pipeline: <c>builder.AddMeter(TickerQMetrics.MeterName)</c>
    /// (or <c>dotnet-counters monitor --counters TickerQ</c>).
    /// </summary>
    public static class TickerQMetrics
    {
        public const string MeterName = "TickerQ";

        private static readonly Meter Meter = new(MeterName, "1.0");

        internal static readonly Counter<long> JobsEnqueued =
            Meter.CreateCounter<long>("tickerq.jobs.enqueued", "{job}", "Jobs handed to the execution pipeline");

        internal static readonly Counter<long> JobsCompleted =
            Meter.CreateCounter<long>("tickerq.jobs.completed", "{job}", "Jobs that finished executing (success tag distinguishes outcome)");

        internal static readonly Counter<long> JobsFailed =
            Meter.CreateCounter<long>("tickerq.jobs.failed", "{job}", "Jobs that exhausted retries and landed Failed");

        internal static readonly Counter<long> JobsRetried =
            Meter.CreateCounter<long>("tickerq.jobs.retried", "{retry}", "Retry attempts scheduled after a failed attempt");

        internal static readonly Counter<long> JobsCancelled =
            Meter.CreateCounter<long>("tickerq.jobs.cancelled", "{job}", "Jobs cancelled (user, execution timeout, or stale recovery)");

        internal static readonly Counter<long> JobsSkipped =
            Meter.CreateCounter<long>("tickerq.jobs.skipped", "{job}", "Jobs skipped by policy (already running, SDK offline, missed)");

        internal static readonly Counter<long> JobsStaleRecovered =
            Meter.CreateCounter<long>("tickerq.jobs.stale_recovered", "{job}", "Expired-lease jobs recovered by the stale watchdog (action tag: restart/cancel)");

        internal static readonly Histogram<double> JobDuration =
            Meter.CreateHistogram<double>("tickerq.job.duration", "ms", "Wall-clock execution time per job, including retry waits");

        // Held to keep the observable instrument alive for the Meter's lifetime.
        private static readonly ObservableGauge<int> ActiveJobs =
            Meter.CreateObservableGauge("tickerq.jobs.active",
                () => TickerCancellationTokenManager.ActiveCount,
                "{job}", "Ticker executions currently in flight on this node");

        internal static KeyValuePair<string, object> FunctionTag(string functionName)
            => new("tickerq.function", functionName ?? string.Empty);
    }
}
