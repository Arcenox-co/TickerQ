using System;
using TickerQ.Utilities.Enums;

namespace TickerQ.Utilities.DashboardDtos
{
    public class TimeTickerFlatDto
    {
        public Guid Id { get; set; }
        public string FunctionName { get; set; }
        public TickerStatus Status { get; set; }
        public DateTime? ScheduledFor { get; set; }
        public long ElapsedTime { get; set; }
        public int Retries { get; set; }
        public int RetryCount { get; set; }
        public TickerTaskPriority Priority { get; set; }
        public DateTime CreatedAt { get; set; }
        public int ChildCount { get; set; }
        public string ExceptionMessage { get; set; }
        public string SkippedReason { get; set; }
        public Guid? ParentId { get; set; }
        public RunCondition? RunCondition { get; set; }
        public DateTime? ExecutedAt { get; set; }
        public string Description { get; set; }
    }
}
