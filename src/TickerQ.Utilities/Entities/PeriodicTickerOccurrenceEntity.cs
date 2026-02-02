using System;
using TickerQ.Utilities.Enums;

namespace TickerQ.Utilities.Entities
{
    /// <summary>
    /// Represents a single execution instance of a PeriodicTicker.
    /// Similar to CronTickerOccurrenceEntity but for periodic tickers.
    /// </summary>
    public class PeriodicTickerOccurrenceEntity<TPeriodicTicker> where TPeriodicTicker : PeriodicTickerEntity
    {
        /// <summary>
        /// Unique identifier for this occurrence.
        /// </summary>
        public virtual Guid Id { get; set; }
        
        /// <summary>
        /// Current status of this occurrence execution.
        /// </summary>
        public virtual TickerStatus Status { get; set; }
        
        /// <summary>
        /// Identifier of the node that has locked this occurrence for execution.
        /// </summary>
        public virtual string LockHolder { get; set; }
        
        /// <summary>
        /// The scheduled execution time for this occurrence.
        /// </summary>
        public virtual DateTime ExecutionTime { get; set; }
        
        /// <summary>
        /// Reference to the parent PeriodicTicker.
        /// </summary>
        public virtual Guid PeriodicTickerId { get; set; }
        
        /// <summary>
        /// Navigation property to the parent PeriodicTicker.
        /// </summary>
        public virtual TPeriodicTicker PeriodicTicker { get; set; }
        
        /// <summary>
        /// Time when this occurrence was locked for execution.
        /// </summary>
        public virtual DateTime? LockedAt { get; set; }
        
        /// <summary>
        /// Time when this occurrence finished execution.
        /// </summary>
        public virtual DateTime? ExecutedAt { get; set; }
        
        /// <summary>
        /// Exception message if execution failed.
        /// </summary>
        public virtual string ExceptionMessage { get; set; }
        
        /// <summary>
        /// Reason if this occurrence was skipped.
        /// </summary>
        public virtual string SkippedReason { get; set; }
        
        /// <summary>
        /// Execution time in milliseconds.
        /// </summary>
        public virtual long ElapsedTime { get; set; }
        
        /// <summary>
        /// Number of retry attempts made for this occurrence.
        /// </summary>
        public virtual int RetryCount { get; set; }
        
        /// <summary>
        /// Timestamp when this occurrence was created.
        /// </summary>
        public virtual DateTime CreatedAt { get; set; }
        
        /// <summary>
        /// Timestamp when this occurrence was last updated.
        /// </summary>
        public virtual DateTime UpdatedAt { get; set; }
    }
}
