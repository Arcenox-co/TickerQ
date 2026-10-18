using System.Collections.Generic;
using System.Linq;
using TickerQ.Utilities.Enums;

namespace TickerQ.Utilities.DashboardDtos;

public enum ChainExecutionStatus
{
    Waiting,
    Running,
    Completed,
    Failed,
    Cancelled,
    Unavailable
}

/// <summary>A display-only summary of the root and every descendant, independent of the root's job status.</summary>
public sealed class ChainExecutionSummaryDto
{
    public ChainExecutionStatus Status { get; set; }
    public int TotalCount { get; set; }
    public int FinishedCount { get; set; }
    public int FailedCount { get; set; }
    public int CancelledCount { get; set; }
    public int SkippedCount { get; set; }

    internal static ChainExecutionSummaryDto FromStatuses(IEnumerable<TickerStatus> statuses)
    {
        var jobs = statuses.ToArray();
        var summary = new ChainExecutionSummaryDto
        {
            TotalCount = jobs.Length,
            FinishedCount = jobs.Count(s => s is TickerStatus.Done or TickerStatus.DueDone or
                TickerStatus.Failed or TickerStatus.Cancelled or TickerStatus.Skipped),
            FailedCount = jobs.Count(s => s == TickerStatus.Failed),
            CancelledCount = jobs.Count(s => s == TickerStatus.Cancelled),
            SkippedCount = jobs.Count(s => s == TickerStatus.Skipped)
        };
        summary.Status = jobs.Length == 0 ? ChainExecutionStatus.Unavailable
            : jobs.Contains(TickerStatus.InProgress) ? ChainExecutionStatus.Running
            : summary.FinishedCount < summary.TotalCount ? ChainExecutionStatus.Waiting
            : summary.FailedCount > 0 ? ChainExecutionStatus.Failed
            : summary.CancelledCount > 0 ? ChainExecutionStatus.Cancelled
            : ChainExecutionStatus.Completed;
        return summary;
    }
}
