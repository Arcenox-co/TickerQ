using System;
using TickerQ.Utilities.Enums;

namespace TickerQ.Utilities.DashboardDtos
{
    public class HostStatusDto
    {
        public bool IsRunning { get; set; }
        public int ActiveThreads { get; set; }
        public int MaxConcurrency { get; set; }
    }

    public class NextTickerDto
    {
        public Guid? Id { get; set; }
        public string FunctionName { get; set; }
        public DateTime? ScheduledFor { get; set; }
        public ExecutionType Type { get; set; }
    }

    public class GraphBucketDto
    {
        public DateTime Date { get; set; }
        public (TickerStatus Status, int Count)[] Counts { get; set; }
    }
}
