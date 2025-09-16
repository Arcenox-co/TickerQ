using System;
using TickerQ.Utilities.Entities.BaseEntity;
using TickerQ.Utilities.Enums;

namespace TickerQ.Utilities.Entities
{
    public class TimeTickerEntity : BaseTickerEntity
    {
        public virtual TickerStatus Status { get; internal set; }
        public virtual string LockHolder { get; internal set; }
        public virtual byte[] Request { get; set; }
        public virtual DateTime ExecutionTime { get; set; }
        public virtual DateTime? LockedAt { get; internal set; }
        public virtual DateTime? ExecutedAt { get; internal set; }
        public virtual string Exception { get; set; }
        public virtual long ElapsedTime { get; internal set; }
        public virtual int Retries { get; set; }
        public virtual int RetryCount { get; internal set; }
        public virtual int[] RetryIntervals { get; set; }
        public virtual Guid? ParentId { get; set; }
        public virtual RunCondition? RunCondition { get; set; }
    }
}