using System;
using TickerQ.Utilities.Enums;

namespace TickerQ.Utilities.DashboardDtos
{
    public class TimeTickerDto : BaseTickerDto
    {
        public string Description { get; set; }
        public TickerStatus Status { get; set; }
        public string LockHolder { get; set; }
        public DateTime ExecutionTime { get; set; }
        public DateTime? LockedAt { get; set; }
        public DateTime? ExecutedAt { get; set; }
        public string RequestType { get; set; }
        public string Exception { get; set; }
        public long ElapsedTime { get; set; }
        public int Retries { get; set; }
        public int RetryCount { get; set; }
        public int[] RetryIntervals { get; set; }
        public string InitIdentifier { get; set; }
        
        public Guid? BatchParent { get; set; }
        public BatchRunCondition? BatchRunCondition { get; set; }
    }
}