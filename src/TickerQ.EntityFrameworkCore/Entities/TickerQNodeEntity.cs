using System;

namespace TickerQ.EntityFrameworkCore.Entities;

public sealed class TickerQNodeEntity
{
    public string ApplicationNamespaceKey { get; set; }
    public string NodeKey { get; set; }
    public string ExecutionOwnerId { get; set; }
    public string NodeIdentifier { get; set; }
    public string DisplayName { get; set; }
    public string DashboardUrl { get; set; }
    public string Version { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime LastSeenAtUtc { get; set; }
    public bool IsRunning { get; set; }
    public int MaxConcurrency { get; set; }
    public int FunctionCount { get; set; }
    public int ActiveExecutions { get; set; }
    public int QueuedExecutions { get; set; }
    public int ActiveWorkflows { get; set; }
    public int WaitingForFunction { get; set; }
    public int RetryingExecutions { get; set; }
    public long ActivationEpoch { get; set; }
}
