using System;
using TickerQ.Utilities.Enums;

namespace TickerQ.Utilities.DashboardDtos
{
    public class CronOccurrenceFlatDto
    {
        public Guid Id { get; set; }
        public Guid CronTickerId { get; set; }
        public string FunctionName { get; set; }
        public TickerStatus Status { get; set; }
        public DateTime ScheduledFor { get; set; }
        public long ElapsedTime { get; set; }
        public int RetryCount { get; set; }
        public string ExceptionMessage { get; set; }
        public string SkippedReason { get; set; }
        public DateTime? ExecutedAt { get; set; }
        public DateTime CreatedAt { get; set; }
        /// <summary>The instance/replica that claimed this occurrence (entity's LockHolder). Null until claimed.</summary>
        public string LockHolder { get; set; }
        /// <summary>When the occurrence was claimed (LockHolder set). Null until claimed.</summary>
        public DateTime? LockedAt { get; set; }
    }
}
