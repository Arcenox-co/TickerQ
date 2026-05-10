using System;
using TickerQ.Utilities.Enums;

namespace TickerQ.Utilities.DashboardDtos
{
    public enum ExecutionType
    {
        TimeTicker = 0,
        CronOccurrence = 1
    }

    public class ExecutionFlatDto
    {
        public Guid Id { get; set; }
        public ExecutionType Type { get; set; }
        public string FunctionName { get; set; }
        public TickerStatus Status { get; set; }
        public DateTime ScheduledFor { get; set; }
        public long ElapsedTime { get; set; }
        public int RetryCount { get; set; }
        /// <summary>
        /// Max retries configured on the parent ticker / cron. The dashboard
        /// renders Retries as "RetryCount/Retries" — without the max we can
        /// only show the count, which loses the "out of how many" framing the
        /// time-ticker / cron-detail tables already provide.
        /// </summary>
        public int Retries { get; set; }
        public string ExceptionMessage { get; set; }
        /// <summary>
        /// Reason the run was Skipped (e.g. "SDK offline"). Persisted on the
        /// row separate from <see cref="ExceptionMessage"/> because the
        /// scheduler routes Skip details there. Surfaces in the dashboard's
        /// log panel as a synthetic fallback line when the run produced no
        /// captured logs (common for skip-before-dispatch cases).
        /// </summary>
        public string SkippedReason { get; set; }
        public DateTime? ExecutedAt { get; set; }
        /// <summary>
        /// For time tickers: the number of direct chain children. Lets the
        /// executions table render a chain icon next to root rows so the user
        /// can spot which executions kicked off a workflow vs ran standalone.
        /// Always 0 for cron occurrences (cron tickers don't chain).
        /// </summary>
        public int ChildCount { get; set; }
    }
}
