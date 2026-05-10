using System;

namespace TickerQ.Utilities.DashboardDtos
{
    public enum NodeHealthStatus
    {
        Healthy = 0,
        Degraded = 1,
        Down = 2
    }

    public class NodeDto
    {
        public string NodeName { get; set; }
        public DateTime? LastHeartbeat { get; set; }
        public NodeHealthStatus HealthStatus { get; set; }
        public int FunctionCount { get; set; }
        public int ActiveJobs { get; set; }
    }
}
