using System;
using TickerQ.Utilities.Enums;

namespace TickerQ.Utilities.Models.Ticker
{
    public class TimeTicker : BaseTicker
    {
        public TickerStatus Status { get; internal set; }
        public string LockHolder { get; internal set; }
        public byte[] Request { get; set; }
        public DateTime ExecutionTime { get; set; }
        public DateTime? LockedAt { get; internal set; }
        public DateTime? ExecutedAt { get; internal set; }
        public string Exception { get; set; }
        public long ElapsedTime { get; internal set; }
        public int Retries { get; set; }
        public int RetryCount { get; internal set; }
        public int[] RetryIntervals { get; set; }

        public Guid? BatchParent { get; set; }

        public BatchRunCondition? BatchRunCondition { get; set; }
    }
}