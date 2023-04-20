using System;
using TickerQ.EntityFrameworkCore.Entities.BaseEntity;
using TickerQ.Utilities.Enums;

namespace TickerQ.EntityFrameworkCore.Entities
{
    public class TimeTicker : BaseTickerEntity
    {
        public TickerStatus Status { get; set; }
        public string LockHolder { get; set; }
        public byte[] Request { get; set; }
        public DateTimeOffset ExecutionTime { get; set; }
        public DateTimeOffset? LockedAt { get; set; }
        public DateTimeOffset? ExcecutedAt { get; set; }
    }
}
