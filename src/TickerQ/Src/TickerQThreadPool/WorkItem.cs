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
    internal readonly WorkItemCompletion Completion;
    internal readonly ExecutionContext ExecutionContext;

    public WorkItem(Func<CancellationToken, Task> work, CancellationToken userToken)
        : this(work, userToken, TickerTaskPriority.Normal)
    {
    }

    public WorkItem(Func<CancellationToken, Task> work, CancellationToken userToken, TickerTaskPriority priority)
        : this(work, userToken, priority, null)
    {
    }

    internal WorkItem(Func<CancellationToken, Task> work, CancellationToken userToken,
        TickerTaskPriority priority, WorkItemCompletion completion, ExecutionContext executionContext = null)
    {
        Completion = completion;
        ExecutionContext = executionContext;
        Work = work ?? throw new ArgumentNullException(nameof(work));
        UserToken = userToken;
        Priority = priority;
    }
}

// Cancellation can finish a queued attempt immediately, but once the delegate starts
// its completion owns the actual lifetime. A non-cooperative running job must not free
// its execution slot merely because its caller's token was cancelled.
internal sealed class WorkItemCompletion
{
    private readonly TaskCompletionSource<bool> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _state;
    internal Task Task => _completion.Task;
    internal bool TryBegin() => Interlocked.CompareExchange(ref _state, 1, 0) == 0;
    internal void CancelBeforeStart(CancellationToken token)
    {
        if (Interlocked.CompareExchange(ref _state, 2, 0) == 0) _completion.TrySetCanceled(token);
    }
    internal void CompleteCancellation(CancellationToken token)
    {
        Interlocked.Exchange(ref _state, 2);
        _completion.TrySetCanceled(token);
    }
    internal void CompleteFailure(Exception exception)
    {
        Interlocked.Exchange(ref _state, 2);
        _completion.TrySetException(exception);
    }
    internal void CompleteSuccess()
    {
        Interlocked.Exchange(ref _state, 2);
        _completion.TrySetResult(true);
    }
}
