using System;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Enums;

namespace TickerQ.TickerQThreadPool;

/// <summary>
/// Simple work item structure for the scheduler
/// </summary>
public readonly struct WorkItem
{
    public readonly Func<CancellationToken, Task> Work;
    public readonly CancellationToken UserToken;
    public readonly TickerTaskPriority Priority;

    public WorkItem(Func<CancellationToken, Task> work, CancellationToken userToken)
        : this(work, userToken, TickerTaskPriority.Normal)
    {
    }

    public WorkItem(Func<CancellationToken, Task> work, CancellationToken userToken, TickerTaskPriority priority)
    {
        Work = work ?? throw new ArgumentNullException(nameof(work));
        UserToken = userToken;
        Priority = priority;
    }
}
