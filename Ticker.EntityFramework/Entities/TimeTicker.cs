using System;
using TickerQ.EntityFrameworkCore.Entities.BaseEntity;
using TickerQ.Utilities.Enums;

namespace TickerQ.EntityFrameworkCore.Entities
{
    public class TimeTicker : BaseTickerEntity
    {
        public virtual TickerStatus Status { get; set; }
        public virtual string LockHolder { get; set; }
        public virtual byte[] Request { get; set; }
        public virtual DateTimeOffset ExecutionTime { get; set; }
        public virtual DateTimeOffset? LockedAt { get; set; }
        public virtual DateTimeOffset? ExcecutedAt { get; set; }
    }
}
