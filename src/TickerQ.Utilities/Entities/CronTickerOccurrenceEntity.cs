using System;
using TickerQ.Utilities.Entities.BaseEntity;
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
        public virtual string Exception { get; set; }
        public virtual long ElapsedTime { get; set; }
        public virtual int RetryCount { get; set; }
        public virtual DateTime CreatedAt { get; set; }
        public virtual DateTime UpdatedAt { get; set; }
    }
}
