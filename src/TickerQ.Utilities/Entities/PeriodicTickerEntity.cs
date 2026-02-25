using System;
using TickerQ.Utilities.Entities.BaseEntity;

namespace TickerQ.Utilities.Entities
{
    /// <summary>
    /// A ticker that executes periodically based on a TimeSpan interval.
    /// Unlike CronTicker which uses cron expressions, PeriodicTicker uses a simple interval.
    /// </summary>
    public class PeriodicTickerEntity : BaseTickerEntity
    {
        /// <summary>
        /// The interval between executions.
        /// </summary>
        public virtual TimeSpan Interval { get; set; }
        
        /// <summary>
        /// The serialized request payload for the ticker function.
        /// </summary>
        public virtual byte[] Request { get; set; }
        
        /// <summary>
        /// Number of retry attempts if execution fails.
        /// </summary>
        public virtual int Retries { get; set; }
        
        /// <summary>
        /// Intervals (in seconds) between retry attempts.
        /// </summary>
        public virtual int[] RetryIntervals { get; set; }
        
        /// <summary>
        /// Whether the ticker is active and should be scheduled.
        /// </summary>
        public virtual bool IsActive { get; set; } = true;
        
        /// <summary>
        /// Optional: The time when this periodic ticker should start.
        /// If null, starts immediately after creation.
        /// </summary>
        public virtual DateTime? StartTime { get; set; }
        
        /// <summary>
        /// Optional: The time when this periodic ticker should stop.
        /// If null, runs indefinitely.
        /// </summary>
        public virtual DateTime? EndTime { get; set; }
        
        /// <summary>
        /// The last time this ticker was executed.
        /// Used to calculate the next execution time.
        /// </summary>
        public virtual DateTime? LastExecutedAt { get; internal set; }
        
        /// <summary>
        /// Total number of times this ticker has been executed.
        /// </summary>
        public virtual long ExecutionCount { get; internal set; }
    }
}
