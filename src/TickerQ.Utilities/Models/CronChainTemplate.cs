using System.Collections.Generic;
using TickerQ.Utilities.Enums;

namespace TickerQ.Utilities.Models;

/// <summary>A recurring chain. The cron function is its root; these children are copied for each occurrence.</summary>
public sealed class CronChainTemplate
{
    public ChainDeleteBehavior DeleteBehavior { get; set; } = ChainDeleteBehavior.Restrict;
    public List<CronChainNode> Children { get; set; } = new();
}

public sealed class CronChainNode
{
    public string Function { get; set; }
    public string Description { get; set; }
    public byte[] Request { get; set; }
    public int Retries { get; set; }
    public int[] RetryIntervals { get; set; }
    public RunCondition RunCondition { get; set; } = RunCondition.OnSuccess;
    public StaleAction OnStale { get; set; }
    public int? TimeoutSeconds { get; set; }
    public int? RequestContractVersion { get; set; }
    public string RequestContractFingerprint { get; set; }
    public List<CronChainNode> Children { get; set; } = new();
}
