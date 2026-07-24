using System;
using TickerQ.Utilities.Enums;

namespace TickerQ.Utilities.Entities
{
    public class CronTickerOccurrenceEntity<TCronTicker> where TCronTicker : CronTickerEntity
    {
        public virtual Guid Id { get; set; }
        public virtual TickerStatus Status { get; set; }
        public virtual string LockHolder { get; set; }
        public virtual DateTime ExecutionTime { get; set; }
        public virtual Guid CronTickerId { get; set; }
        public virtual DateTime? LockedAt { get; set; }
        public virtual DateTime? ExecutedAt { get; set; }
        public virtual TCronTicker CronTicker { get; set; }
        public virtual string ExceptionMessage { get; set; }
        public virtual string SkippedReason { get; set; }
        public virtual long ElapsedTime { get; set; }
        public virtual int RetryCount { get; set; }
        public virtual DateTime CreatedAt { get; set; }
        public virtual DateTime UpdatedAt { get; set; }
        /// <summary>
        /// Lease expiry for the node currently executing this occurrence. Stamped
        /// when the occurrence goes InProgress and renewed periodically by the
        /// running node; an InProgress occurrence whose lease is in the past is
        /// considered stale. The stale action for occurrences comes from the
        /// parent <see cref="CronTickerEntity.OnStale"/>.
        /// </summary>
        public virtual DateTime? LeaseUntil { get; set; }
        public virtual int StaleRestartCount { get; set; }
    }
}
