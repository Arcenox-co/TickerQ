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
        public string NodeKey { get; set; }
        public string NodeName { get; set; }
        public string NodeIdentifier { get; set; }
        public string DashboardUrl { get; set; }
        public string Version { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? LastHeartbeat { get; set; }
        public NodeHealthStatus HealthStatus { get; set; }
        public bool IsRunning { get; set; }
        public int FunctionCount { get; set; }
        public int ActiveJobs { get; set; }
        public int MaxConcurrency { get; set; }
        public int QueuedJobs { get; set; }
        public int ActiveWorkflows { get; set; }
        public int WaitingForFunction { get; set; }
        public int RetryingJobs { get; set; }
    }
}
