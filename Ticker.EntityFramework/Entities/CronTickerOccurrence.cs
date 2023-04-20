using System;
using TickerQ.Utilities.Enums;

namespace TickerQ.EntityFrameworkCore.Entities
{
    public class CronTickerOccurrence
    {
        public Guid Id { get; set; }
        public TickerStatus Status { get; set; }
        public string LockHolder { get; set; }
        public DateTimeOffset ExecutionTime { get; set; }
        public Guid CronTickerId { get; set; }
        public DateTimeOffset? LockedAt { get; set; }
        public DateTimeOffset? ExcecutedAt { get; set; }
        public CronTicker CronTicker { get; set; }
    }
}
