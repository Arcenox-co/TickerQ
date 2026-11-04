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
    /// <summary>The caller's token is already cancelled whenever the scheduler stops.</summary>
    internal readonly bool TokenObservesStop;

    public WorkItem(Func<CancellationToken, Task> work, CancellationToken userToken)
        : this(work, userToken, TickerTaskPriority.Normal)
    {
    }

    public WorkItem(Func<CancellationToken, Task> work, CancellationToken userToken, TickerTaskPriority priority)
        : this(work, userToken, priority, null)
    {
    }

    internal WorkItem(Func<CancellationToken, Task> work, CancellationToken userToken,
        TickerTaskPriority priority, WorkItemCompletion completion, ExecutionContext executionContext = null,
        bool tokenObservesStop = false)
    {
        Completion = completion;
        ExecutionContext = executionContext;
        TokenObservesStop = tokenObservesStop;
        Work = work ?? throw new ArgumentNullException(nameof(work));
        UserToken = userToken;
        Priority = priority;
    }
}

// Cancellation can finish a queued attempt immediately, but once the delegate starts
// its completion owns the actual lifetime. A non-cooperative running job must not free
// its execution slot merely because its caller's token was cancelled.
internal sealed class WorkItemCompletion : TaskCompletionSource<bool>
{
    private int _state;
    internal WorkItemCompletion() : base(TaskCreationOptions.RunContinuationsAsynchronously) { }
    internal new Task Task => base.Task;
    internal bool TryBegin() => Interlocked.CompareExchange(ref _state, 1, 0) == 0;
    internal void CancelBeforeStart(CancellationToken token)
    {
        if (Interlocked.CompareExchange(ref _state, 2, 0) == 0) TrySetCanceled(token);
    }

    /// <summary>Cancels a not-yet-started attempt when <paramref name="token"/> fires; no closure.</summary>
    internal CancellationTokenRegistration CancelBeforeStartOn(CancellationToken token)
        => token.CanBeCanceled
            ? token.UnsafeRegister(static (state, cancelled) => ((WorkItemCompletion)state!).CancelBeforeStart(cancelled), this)
            : default;
    internal void CompleteCancellation(CancellationToken token)
    {
        Interlocked.Exchange(ref _state, 2);
        TrySetCanceled(token);
    }
    internal void CompleteFailure(Exception exception)
    {
        Interlocked.Exchange(ref _state, 2);
        TrySetException(exception);
    }
    internal void CompleteSuccess()
    {
        Interlocked.Exchange(ref _state, 2);
        TrySetResult(true);
    }
}
