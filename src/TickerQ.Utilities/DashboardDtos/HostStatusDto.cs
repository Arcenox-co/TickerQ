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
        public GraphBucketCountDto[] Counts { get; set; }
    }

    // A named class, not a value tuple: tuples expose Item1/Item2 as FIELDS,
    // which System.Text.Json skips — the counts would serialize as "{}".
    public class GraphBucketCountDto
    {
        public TickerStatus Status { get; set; }
        public int Count { get; set; }
    }
}
