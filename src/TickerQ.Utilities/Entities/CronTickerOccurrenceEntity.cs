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
        /// <summary>
        /// Nullable marker for the current InProgress generation of this occurrence. A
        /// fresh value is minted on every transition to InProgress and cleared whenever the
        /// row is released to Idle, stale-restarted/cancelled, dead-node recovered, or
        /// terminally completed. Terminal writes and lease renewals are fenced on
        /// <see cref="LockHolder"/> + this token so a stale owner from an earlier generation
        /// cannot write after the row was re-acquired (same-node ABA).
        /// </summary>
        public virtual Guid? AcquisitionToken { get; set; }
    }
}
