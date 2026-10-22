using System;
using TickerQ.Utilities.Enums;

namespace TickerQ.Utilities.DashboardDtos
{
    public class TimeTickerFlatDto
    {
        public Guid Id { get; set; }
        /// <summary>Identifies the source when this DTO is used in the mixed overview feed.</summary>
        public ExecutionType Type { get; set; } = ExecutionType.TimeTicker;
        /// <summary>Parent definition for an upcoming cron occurrence; null for time tickers.</summary>
        public Guid? CronTickerId { get; set; }
        public string FunctionName { get; set; }
        public TickerStatus Status { get; set; }
        public DateTime? ScheduledFor { get; set; }
        public long ElapsedTime { get; set; }
        public double? ProgressPercent { get; set; }
        public string ProgressMessage { get; set; }
        public DateTime? ProgressUpdatedAt { get; set; }
        public int Retries { get; set; }
        public int RetryCount { get; set; }
        public TickerTaskPriority Priority { get; set; }
        public DateTime CreatedAt { get; set; }
        public int ChildCount { get; set; }
        public ChainExecutionSummaryDto ChainSummary { get; set; }
        public string ExceptionMessage { get; set; }
        public string SkippedReason { get; set; }
        public Guid? ParentId { get; set; }
        public Guid? OriginCronTickerId { get; set; }
        public ChainDeleteBehavior DeleteBehavior { get; set; }
        public RunCondition? RunCondition { get; set; }
        public DateTime? ExecutedAt { get; set; }
        public string Description { get; set; }
        /// <summary>The instance/replica that claimed this ticker (entity's LockHolder). Null until it's claimed.</summary>
        public string LockHolder { get; set; }
        /// <summary>When the ticker was claimed (LockHolder set). Null until claimed; useful for "running for Xs".</summary>
        public DateTime? LockedAt { get; set; }
        /// <summary>Per-retry delay schedule (seconds). Surfaced so edit forms can prefill the field correctly.</summary>
        public int[] RetryIntervalsSeconds { get; set; }
        /// <summary>What the stale-job watchdog does if the executing node dies mid-run.</summary>
        public StaleAction OnStale { get; set; }
        /// <summary>Max execution time per attempt (seconds); null inherits the global default.</summary>
        public int? TimeoutSeconds { get; set; }
    }
}
