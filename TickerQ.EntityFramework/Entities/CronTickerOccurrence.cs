using System;
using TickerQ.Utilities.Enums;

namespace TickerQ.EntityFrameworkCore.Entities
{
    public class CronTickerOccurrence<TCronTicker> where TCronTicker : CronTicker
    {
        public Guid Id { get; set; }
        public TickerStatus Status { get; set; }
        public string LockHolder { get; set; }
        public DateTime ExecutionTime { get; set; }
        public Guid CronTickerId { get; set; }
        public DateTime? LockedAt { get; set; }
        public DateTime? ExecutedAt { get; set; }
        public TCronTicker CronTicker { get; set; }
        public string Exception { get; set; }
        public long ElapsedTime { get; set; }
        public int RetryCount { get; set; }
    }
}
