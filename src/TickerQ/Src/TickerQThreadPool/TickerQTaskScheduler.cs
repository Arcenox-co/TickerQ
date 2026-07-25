using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;

namespace TickerQ.TickerQThreadPool;

/// <summary>
/// Elastic work-stealing task scheduler.
///
/// Concurrency contract:
///  - <see cref="TotalQueuedTasks"/> settles at exactly zero and never underflows: an
///    item's queued ownership is published (counter incremented) BEFORE the item becomes
///    dequeue-visible, and publication is atomic with <see cref="Freeze"/>/<see cref="DisposeAsync"/>.
///  - At most <c>maxConcurrency</c> executions run at once. Normal and LongRunning work
///    draw from the SAME budget; a worker owns each item's full async lifecycle.
///  - Disposal cancels cooperative in-flight work through a token linked to the scheduler
///    stop token, and never disposes shared state while a worker might still touch it.
///  - Reentrant <see cref="QueueAsync"/> (from within one of this scheduler's own
///    executions) runs inline under the owning slot, so nested scheduling cannot deadlock
///    at low concurrency and cannot exceed the active-execution budget.
/// </summary>
public sealed class TickerQTaskScheduler : IAsyncDisposable, ITickerQTaskScheduler
{
    private readonly int _maxConcurrency;
    private readonly TimeSpan _idleWorkerTimeout;
    private readonly int _maxCapacityPerWorker;

    // Upper bound on how long DisposeAsync waits for in-flight executions to finish
    // before deferring shared-resource cleanup to the last worker. Injectable so tests
    // can exercise "work outlives the bounded wait" without a mandatory multi-second delay.
    private static readonly TimeSpan DefaultDisposeDrainTimeout = TimeSpan.FromSeconds(5);
    private readonly TimeSpan _disposeDrainTimeout;

    private const int DrainPollIntervalMs = 10;   // WaitForRunningTasksAsync poll cadence
    private const int WorkerPollIntervalMs = 50;   // DisposeAsync worker-exit poll cadence

    // Worker queues for work stealing
    private readonly ConcurrentQueue<WorkItem>[] _workerQueues;

    // Global state
    private volatile int _totalQueuedTasks;
    private volatile int _activeExecutions;
    private volatile int _activeWorkers;
    private volatile bool _disposed;
    private volatile bool _isFrozen;
    private volatile int _nextQueueIndex;

    // Serializes lifecycle publication (counter increment + enqueue) with Freeze/DisposeAsync
    // so no work can be published after freeze/disposal wins.
    private readonly object _stateLock = new();

    private readonly CancellationTokenSource _shutdownCts = new();
    // Cached stop token. Read this instead of _shutdownCts.Token so cancellation checks
    // never throw ObjectDisposedException after the CTS is (deferred-)disposed.
    private readonly CancellationToken _stopToken;
    private readonly SoftSchedulerNotifyDebounce _notifyDebounce;

    // One-shot latch guarding disposal of shared CTS/notification state, so DisposeAsync
    // and the last worker out can both attempt it and exactly one wins.
    private int _sharedReleased;
    private Task _disposeTask;

    // Ownership marker for reentrancy. The scope expires when its outer work item returns,
    // so detached tasks that inherited the ExecutionContext cannot later run unaccounted.
    private static readonly AsyncLocal<ExecutionScope> _currentExecutionScope = new();

    private sealed class ExecutionScope
    {
        private int _acceptingNested = 1;
        private int _nestedCount;
        private readonly SemaphoreSlim _nestedSerial = new(1, 1);
        private readonly TaskCompletionSource<bool> _nestedDrained =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal ExecutionScope(TickerQTaskScheduler owner) => Owner = owner;
        internal TickerQTaskScheduler Owner { get; }

        internal bool TryAcquireNested()
        {
            if (Volatile.Read(ref _acceptingNested) == 0)
                return false;

            Interlocked.Increment(ref _nestedCount);
            if (Volatile.Read(ref _acceptingNested) != 0)
                return true;

            ReleaseNested();
            return false;
        }

        internal void ReleaseNested()
        {
            if (Interlocked.Decrement(ref _nestedCount) == 0 &&
                Volatile.Read(ref _acceptingNested) == 0)
                _nestedDrained.TrySetResult(true);
        }

        internal Task WaitForNestedTurnAsync(CancellationToken cancellationToken)
            => _nestedSerial.WaitAsync(cancellationToken);

        internal void ReleaseNestedTurn() => _nestedSerial.Release();

        internal static void Observe(Task task)
        {
            _ = task.ContinueWith(
                static faulted => _ = faulted.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        internal Task CloseAndWaitAsync()
        {
            Volatile.Write(ref _acceptingNested, 0);
            if (Volatile.Read(ref _nestedCount) == 0)
                _nestedDrained.TrySetResult(true);
            return _nestedDrained.Task;
        }
    }

    // Thread-local flag to detect if we're on a TickerQ worker thread (used by the
    // synchronization context for continuation routing).
    [ThreadStatic] public static bool IsTickerQWorkerThread;
    [ThreadStatic] private static int _threadWorkerIndex = -1;

    public TickerQTaskScheduler(
        int maxConcurrency,
        TimeSpan? idleWorkerTimeout = null,
        SoftSchedulerNotifyDebounce notifyDebounce = null)
        : this(maxConcurrency, idleWorkerTimeout, notifyDebounce, DefaultDisposeDrainTimeout)
    {
    }

    // Test-friendly constructor: allows the dispose drain timeout to be shortened so the
    // "execution outlives bounded disposal" path can be exercised deterministically.
    internal TickerQTaskScheduler(
        int maxConcurrency,
        TimeSpan? idleWorkerTimeout,
        SoftSchedulerNotifyDebounce notifyDebounce,
        TimeSpan disposeDrainTimeout)
    {
        if (maxConcurrency <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxConcurrency), "Must be greater than zero");

        _maxConcurrency = maxConcurrency;
        _idleWorkerTimeout = idleWorkerTimeout ?? TimeSpan.FromSeconds(60);
        _disposeDrainTimeout = disposeDrainTimeout;
        _maxCapacityPerWorker = 1024; // Fixed optimal capacity
        _notifyDebounce = notifyDebounce ?? new SoftSchedulerNotifyDebounce(_ => { });
        _stopToken = _shutdownCts.Token;

        // Initialize all worker queues upfront for simplicity
        _workerQueues = new ConcurrentQueue<WorkItem>[maxConcurrency];
        for (int i = 0; i < maxConcurrency; i++)
        {
            _workerQueues[i] = new ConcurrentQueue<WorkItem>();
        }

        // Start at least one worker immediately to handle incoming tasks
        TryStartWorker();
    }

    /// <summary>
    /// Queues work to be executed by the scheduler.
    /// The priority parameter only selects the execution thread type (LongRunning uses a
    /// dedicated long-running thread); it does not change the shared concurrency budget.
    /// </summary>
    public ValueTask QueueAsync(
        Func<CancellationToken, Task> work,
        TickerTaskPriority priority,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return QueueAsyncDispatch(work, priority, cancellationToken);
        }
        catch (Exception ex)
        {
            // Preserve the original async API behavior for synchronous validation/lifecycle errors.
            return ValueTask.FromException(ex);
        }
    }

    private ValueTask QueueAsyncDispatch(
        Func<CancellationToken, Task> work,
        TickerTaskPriority priority,
        CancellationToken cancellationToken)
    {
        if (work == null)
            throw new ArgumentNullException(nameof(work));

        // Reentrancy: acquire nested ownership atomically with Freeze/DisposeAsync. Sibling
        // nested delegates are serialized by their owning scope; each child receives a new
        // scope for deeper recursion.
        ExecutionScope nestedScope = null;
        lock (_stateLock)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(TickerQTaskScheduler));
            if (_isFrozen)
                throw new InvalidOperationException("Scheduler is frozen");

            var currentScope = _currentExecutionScope.Value;
            if (ReferenceEquals(currentScope?.Owner, this) && currentScope.TryAcquireNested())
                nestedScope = currentScope;
        }

        if (nestedScope != null)
        {
            var nestedTask = ExecuteNestedAsync(nestedScope, work, priority, cancellationToken);
            ExecutionScope.Observe(nestedTask);
            return new ValueTask(nestedTask);
        }

        return QueueCoreAsync(work, priority, cancellationToken);
    }

    private async ValueTask QueueCoreAsync(
        Func<CancellationToken, Task> work,
        TickerTaskPriority priority,
        CancellationToken cancellationToken)
    {
        // Round-robin distribution across worker queues. LongRunning work shares the same
        // queue and active-execution budget as normal work; its priority is carried on the
        // WorkItem so the worker can honor the dedicated long-running thread on execution.
        var queueIndex = GetNextQueueIndex();
        var targetQueue = _workerQueues[queueIndex];

        // Asynchronous capacity backpressure. This can suspend, so Freeze/DisposeAsync may
        // win in the meantime; the publication below re-checks state under _stateLock.
        await WaitForCapacityAsync(targetQueue, cancellationToken).ConfigureAwait(false);

        var workItem = new WorkItem(work, cancellationToken, priority);

        // Publish atomically with Freeze/DisposeAsync. Increment the queued counter BEFORE
        // the item becomes dequeue-visible, so a worker can never dequeue-and-decrement an
        // item whose ownership has not yet been published (no underflow / false drain).
        lock (_stateLock)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(TickerQTaskScheduler));

            if (_isFrozen)
                throw new InvalidOperationException("Scheduler is frozen");

            Interlocked.Increment(ref _totalQueuedTasks);
            targetQueue.Enqueue(workItem);
        }

        // Worker management runs outside the lock (it may invoke the external notify
        // callback and start threads). Safe: if disposal raced in, the item is already
        // enqueued and will be drained, and TryStartWorker no-ops during shutdown.
        EnsureWorkerAvailable();
    }

    private async Task ExecuteNestedAsync(
        ExecutionScope parentScope,
        Func<CancellationToken, Task> work,
        TickerTaskPriority priority,
        CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stopToken);
        var ownsTurn = false;
        try
        {
            if (linked.IsCancellationRequested)
                return;

            try
            {
                await parentScope.WaitForNestedTurnAsync(linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (linked.IsCancellationRequested)
            {
                return;
            }
            ownsTurn = true;

            if (linked.IsCancellationRequested)
                return;

            if (priority == TickerTaskPriority.LongRunning)
            {
                await Task.Factory.StartNew(
                    () => ExecuteInlineAsync(work, linked.Token).AsTask(),
                    CancellationToken.None,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default).Unwrap().ConfigureAwait(false);
            }
            else
            {
                await ExecuteInlineAsync(work, linked.Token).ConfigureAwait(false);
            }
        }
        finally
        {
            if (ownsTurn)
                parentScope.ReleaseNestedTurn();
            parentScope.ReleaseNested();
        }
    }

    /// <summary>
    /// Executes reentrant work inline under the caller's already-counted execution slot.
    /// The work receives a token linked to both the caller token and the scheduler stop
    /// token. Does not touch the queued/active counters (the parent slot already owns them).
    /// </summary>
    private async ValueTask ExecuteInlineAsync(Func<CancellationToken, Task> work, CancellationToken cancellationToken)
    {
        // Reentrancy implies a worker of this scheduler is running, so _shutdownCts is alive.
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stopToken);

        // Give every inline execution its own expiring scope. Its parent scope owns this
        // method until completion, while deeper nested work belongs to this child scope.
        // That keeps fire-and-forget inline children reentrant after their parent delegate
        // returns without letting unrelated detached ExecutionContext copies remain owners.
        var previousScope = _currentExecutionScope.Value;
        var executionScope = new ExecutionScope(this);
        _currentExecutionScope.Value = executionScope;
        try
        {
            var task = work(linked.Token);
            if (task != null)
                await task.ConfigureAwait(false);
        }
        finally
        {
            await executionScope.CloseAndWaitAsync().ConfigureAwait(false);
            _currentExecutionScope.Value = previousScope;
        }
    }

    private int GetNextQueueIndex()
    {
        // Simple round-robin without complex CAS loop
        var index = Interlocked.Increment(ref _nextQueueIndex);
        return Math.Abs(index) % _maxConcurrency;
    }

    private async ValueTask WaitForCapacityAsync(
        ConcurrentQueue<WorkItem> queue,
        CancellationToken cancellationToken)
    {
        var waitCount = 0;
        while (queue.Count >= _maxCapacityPerWorker
               && !cancellationToken.IsCancellationRequested
               && !_stopToken.IsCancellationRequested
               && !_disposed)
        {
            if (++waitCount > 100) // After ~1 second, nudge a worker in case we're stuck
            {
                EnsureWorkerAvailable();
                waitCount = 0;
            }

            try
            {
                await Task.Delay(10, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break; // publication re-check throws the appropriate exception
            }
        }
    }

    private void EnsureWorkerAvailable()
    {
        // Always try to maintain at least one worker
        if (_activeWorkers == 0)
        {
            TryStartWorker();
            return;
        }

        // If there is queued work and we still have capacity, start another worker
        var totalQueued = _totalQueuedTasks;
        var activeWorkers = _activeWorkers;

        if (totalQueued > 0 && activeWorkers < _maxConcurrency)
        {
            TryStartWorker();
        }
    }

    private void TryStartWorker()
    {
        if (_stopToken.IsCancellationRequested || _disposed)
            return;

        // Try to increment active workers
        var currentWorkers = _activeWorkers;
        if (currentWorkers >= _maxConcurrency)
            return;

        if (Interlocked.CompareExchange(ref _activeWorkers, currentWorkers + 1, currentWorkers) == currentWorkers)
        {
            // Successfully reserved a worker slot
            var workerId = currentWorkers; // Use the slot we just reserved
            var thread = new Thread(() => WorkerLoop(workerId))
            {
                IsBackground = true,
                Name = $"TickerQ.Worker-{workerId}"
            };
            thread.Start();

            _notifyDebounce.NotifySafely(_activeWorkers);
        }
    }

    private void WorkerLoop(int workerId)
    {
        // Set thread-local state
        _threadWorkerIndex = workerId;
        IsTickerQWorkerThread = true;

        // Set a simple synchronization context if needed for continuations
        var originalContext = SynchronizationContext.Current;
        var tickerQContext = new TickerQSynchronizationContext(this);
        SynchronizationContext.SetSynchronizationContext(tickerQContext);

        try
        {
            // Run the async worker loop
            Task.Run(async () => await WorkerLoopCoreAsync(workerId)).GetAwaiter().GetResult();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);

            var remaining = Interlocked.Decrement(ref _activeWorkers);
            if (remaining == 0 && _disposed)
            {
                // Last worker out during disposal: release shared resources that
                // DisposeAsync deferred because an execution outlived its bounded wait.
                ReleaseSharedResourcesOnce();
            }
            else
            {
                // While workers remain, the notification state is guaranteed alive.
                _notifyDebounce.NotifySafely(remaining);
            }
        }
    }

    private async Task WorkerLoopCoreAsync(int workerId)
    {
        var lastWorkTime = DateTime.UtcNow;
        var localQueue = _workerQueues[workerId];
        var consecutiveStealFailures = 0;

        // Cached stop token only — never read _shutdownCts.Token here, which can be
        // disposed by the time a straggler execution loops back.
        while (!_stopToken.IsCancellationRequested)
        {
            WorkItem workItem = default;
            bool foundWork = false;

            // 1. Try local queue first (fastest path)
            if (localQueue.TryDequeue(out workItem))
            {
                foundWork = true;
                consecutiveStealFailures = 0;
            }
            // 2. Try work stealing if local queue is empty
            else if (TryStealWork(workerId, out workItem))
            {
                foundWork = true;
                consecutiveStealFailures = 0;
            }
            else
            {
                consecutiveStealFailures++;
            }

            if (foundWork)
            {
                lastWorkTime = DateTime.UtcNow;
                await ExecuteWorkAsync(workItem);
            }
            else
            {
                // No work found - check if we should exit
                if (DateTime.UtcNow - lastWorkTime > _idleWorkerTimeout)
                {
                    // Check ALL queues for any remaining work before exiting
                    bool anyWorkRemaining = false;
                    for (int i = 0; i < _maxConcurrency; i++)
                    {
                        if (_workerQueues[i].Count > 0)
                        {
                            anyWorkRemaining = true;
                            break;
                        }
                    }

                    // Only exit if there's really no work and we have more than the minimum
                    // worker. The last worker never idle-exits, so it stays until shutdown.
                    if (!anyWorkRemaining && _totalQueuedTasks == 0 && _activeWorkers > 1)
                    {
                        break; // Exit this worker
                    }

                    // Reset timer if we need to stay
                    lastWorkTime = DateTime.UtcNow;
                }

                // Brief sleep to avoid spinning
                if (consecutiveStealFailures > 3)
                {
                    await Task.Delay(Math.Min(consecutiveStealFailures * 2, 50));
                }
                else
                {
                    await Task.Yield();
                }
            }
        }
    }

    private bool TryStealWork(int thiefWorkerId, out WorkItem workItem)
    {
        workItem = default;

        // Try to steal from other workers
        // Start from a different position each time to avoid patterns
        var startIndex = (thiefWorkerId + 1) % _maxConcurrency;

        // First pass: steal from queues with multiple items
        for (int i = 0; i < _maxConcurrency - 1; i++)
        {
            var victimIndex = (startIndex + i) % _maxConcurrency;
            if (victimIndex == thiefWorkerId)
                continue; // Don't steal from ourselves

            var victimQueue = _workerQueues[victimIndex];

            // Only steal if victim has multiple items (leave at least one)
            if (victimQueue.Count > 1 && victimQueue.TryDequeue(out workItem))
            {
                return true;
            }
        }

        // Second pass: steal even single items if we're desperate
        for (int i = 0; i < _maxConcurrency - 1; i++)
        {
            var victimIndex = (startIndex + i) % _maxConcurrency;
            if (victimIndex == thiefWorkerId)
                continue;

            if (_workerQueues[victimIndex].TryDequeue(out workItem))
            {
                return true;
            }
        }

        return false;
    }

    private async Task ExecuteWorkAsync(WorkItem workItem)
    {
        // The item has been dequeued but is still counted as queued at this point. We keep
        // the queued ownership until we know whether the work is accepted, so a concurrent
        // WaitForRunningTasksAsync stays conservative (it never observes drained while a
        // dequeued item is still being decided).

        // Cancelled before acceptance: drop queued ownership only, never take an active
        // slot, so the counters settle at queued-1 / active+0.
        if (workItem.UserToken.IsCancellationRequested || _stopToken.IsCancellationRequested)
        {
            Interlocked.Decrement(ref _totalQueuedTasks);
            return;
        }

        // Accepted work: hand ownership from the queued counter to the active-execution
        // counter with a deliberate temporary overlap. We increment active BEFORE
        // decrementing queued so that at no instant can a concurrent
        // WaitForRunningTasksAsync observe queued==0 && active==0 for work that is about to
        // run. The brief queued+active double-count is intentional and conservative — it
        // can only delay a drain signal, never report drained early.
        //
        // The worker owns this task's full lifecycle: it awaits completion before dequeuing
        // the next item, which is what bounds concurrent executions to maxConcurrency.
        Interlocked.Increment(ref _activeExecutions);
        Interlocked.Decrement(ref _totalQueuedTasks);

        // Cooperative cancellation: the running delegate gets a token linked to BOTH the
        // caller token and the scheduler stop token, so disposal can cancel active work.
        // _stopToken's source is alive for the whole execution — it is only disposed after
        // _activeWorkers reaches 0, which cannot happen while this execution is in flight.
        var linked = CancellationTokenSource.CreateLinkedTokenSource(workItem.UserToken, _stopToken);

        // Mark this async flow as executing inside one of our slots so reentrant QueueAsync
        // runs inline instead of deadlocking on the queue. The expiring scope flows across
        // async continuations but cannot be reused by detached tasks after this item closes.
        var previousScope = _currentExecutionScope.Value;
        var executionScope = new ExecutionScope(this);
        _currentExecutionScope.Value = executionScope;
        try
        {
            if (workItem.Priority == TickerTaskPriority.LongRunning)
            {
                // Preserve the dedicated long-running thread, but own its lifecycle: start
                // it, unwrap the inner task, and await full completion here rather than
                // firing and forgetting — so it consumes the same budget as normal work.
                var task = Task.Factory.StartNew(
                    () => workItem.Work(linked.Token) ?? Task.CompletedTask,
                    CancellationToken.None,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default).Unwrap();

                await task.ConfigureAwait(false);
            }
            else
            {
                var task = workItem.Work(linked.Token);
                if (task != null)
                    await task.ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected - work cancelled cooperatively (caller token or shutdown).
        }
        catch (Exception)
        {
            // Swallow exceptions so one failed work item never kills the worker.
        }
        finally
        {
            // Stop detached ExecutionContext copies from acquiring this slot, then keep the
            // slot active until every inline child (including an ignored returned ValueTask)
            // has completed.
            await executionScope.CloseAndWaitAsync().ConfigureAwait(false);
            _currentExecutionScope.Value = previousScope;
            linked.Dispose();
            // Release the active-execution slot only after full async completion.
            Interlocked.Decrement(ref _activeExecutions);
        }
    }

    /// <summary>
    /// Posts a continuation work item to the scheduler.
    /// Used by TickerQSynchronizationContext.
    /// </summary>
    internal void PostContinuation(SendOrPostCallback callback, object state)
    {
        if (_disposed || _stopToken.IsCancellationRequested)
            return;

        // Continuations get queued to the current worker's queue if possible
        var queueIndex = _threadWorkerIndex >= 0 ? _threadWorkerIndex : GetNextQueueIndex();
        var targetQueue = _workerQueues[queueIndex];

        var workItem = new WorkItem(
            ct =>
            {
                try
                {
                    callback(state);
                }
                catch
                {
                    // Swallow exceptions in continuations
                }
                return Task.CompletedTask;
            },
            CancellationToken.None);

        // Publish atomically with disposal, counter before visibility (same as QueueAsync).
        lock (_stateLock)
        {
            if (_disposed || _stopToken.IsCancellationRequested)
                return;

            Interlocked.Increment(ref _totalQueuedTasks);
            targetQueue.Enqueue(workItem);
        }

        EnsureWorkerAvailable();
    }

    /// <summary>
    /// Freezes the scheduler - prevents new tasks from being queued.
    /// </summary>
    public void Freeze()
    {
        lock (_stateLock)
        {
            _isFrozen = true;
        }
    }

    /// <summary>
    /// Resumes the scheduler - allows new tasks to be queued again.
    /// </summary>
    public void Resume()
    {
        lock (_stateLock)
        {
            _isFrozen = false;
        }
    }

    /// <summary>
    /// Gets whether the scheduler is currently frozen.
    /// </summary>
    public bool IsFrozen => _isFrozen;

    /// <summary>
    /// Gets the current number of active worker threads.
    /// </summary>
    public int ActiveWorkers => _activeWorkers;

    /// <summary>
    /// Gets the current number of in-flight async executions (work items that have been
    /// dequeued and invoked but not yet fully completed). Distinct from the number of
    /// worker threads.
    /// </summary>
    public int ActiveExecutionCount => _activeExecutions;

    /// <summary>
    /// Gets the current total number of queued tasks.
    /// </summary>
    public int TotalQueuedTasks => _totalQueuedTasks;

    /// <summary>
    /// Gets whether the scheduler has been disposed.
    /// </summary>
    public bool IsDisposed => _disposed;

    /// <summary>
    /// Gets diagnostic information about the scheduler state.
    /// </summary>
    public string GetDiagnostics()
    {
        var text = $"=== TickerQ Work-Stealing Scheduler ===\n";
        text += $"Status: {(_isFrozen ? "FROZEN" : (_disposed ? "DISPOSED" : "ACTIVE"))}\n";
        text += $"Workers: {_activeWorkers}/{_maxConcurrency}\n";
        text += $"Active Executions: {_activeExecutions}\n";
        text += $"Total Queued (counter): {_totalQueuedTasks}\n\n";
        text += "Queue Distribution:\n";

        int totalInQueues = 0;
        for (int i = 0; i < _maxConcurrency; i++)
        {
            var count = _workerQueues[i].Count;
            totalInQueues += count;
            if (count > 0)
            {
                text += $"  Queue[{i}]: {count} tasks\n";
            }
        }

        if (totalInQueues == 0)
        {
            text += "  All queues empty\n";
        }
        else
        {
            text += $"  Total in queues: {totalInQueues}\n";
        }

        // The queued counter and a live queue snapshot are intentionally NOT atomic: an
        // item is counted while it is dequeued-but-not-yet-transitioned to active, and a
        // just-published item is counted an instant before it becomes visible. A transient
        // difference here is expected and not a defect.
        if (totalInQueues != _totalQueuedTasks)
        {
            text += $"\nNote: counter ({_totalQueuedTasks}) differs from live queue snapshot ({totalInQueues}); " +
                    "expected transiently during the queued→active handoff.\n";
        }

        return text;
    }

    /// <summary>
    /// Waits until there is no queued and no in-flight work. Original timeout-only overload,
    /// preserved for source/binary compatibility; forwards to the cancellation-aware overload.
    /// </summary>
    public Task<bool> WaitForRunningTasksAsync(TimeSpan? timeout = null)
        => WaitForRunningTasksAsync(timeout, CancellationToken.None);

    /// <summary>
    /// Waits until there is no queued and no in-flight work. Completes true only when both
    /// the queued counter and the active-execution counter reach zero — worker thread
    /// liveness is deliberately ignored, since idle workers are not pending work.
    ///
    /// Returns false (without throwing) if the timeout elapses or the token is cancelled.
    /// A pre-cancelled token returns false even when already idle, and the timeout is
    /// strict: once the deadline passes, work finishing afterward cannot still yield true.
    /// </summary>
    public async Task<bool> WaitForRunningTasksAsync(TimeSpan? timeout, CancellationToken cancellationToken)
    {
        // Pre-cancelled cancellation returns false even if already idle.
        if (cancellationToken.IsCancellationRequested)
            return false;

        // Already drained: return true immediately (even for a zero/elapsed timeout).
        if (_totalQueuedTasks == 0 && _activeExecutions == 0)
            return true;

        // Not drained and no time to wait: strict false.
        if (timeout.HasValue && timeout.Value <= TimeSpan.Zero)
            return false;

        var deadline = timeout.HasValue ? DateTime.UtcNow.Add(timeout.Value) : (DateTime?)null;

        while (true)
        {
            try
            {
                await Task.Delay(DrainPollIntervalMs, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return false;
            }

            if (cancellationToken.IsCancellationRequested)
                return false;

            // Strict timeout: after the delay, re-check the deadline BEFORE the counters so
            // work that finishes after the deadline cannot still produce true.
            if (deadline.HasValue && DateTime.UtcNow >= deadline.Value)
                return false;

            if (_totalQueuedTasks == 0 && _activeExecutions == 0)
                return true;
        }
    }

    public ValueTask DisposeAsync()
    {
        TaskCompletionSource<bool> completion = null;
        Task disposeTask;
        lock (_stateLock)
        {
            if (_disposeTask != null)
                return new ValueTask(_disposeTask);

            _disposed = true;
            _isFrozen = true; // no new work may be published past this point
            completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _disposeTask = completion.Task;
            disposeTask = _disposeTask;
        }

        // Start outside _stateLock: CancellationTokenSource.Cancel invokes callbacks
        // synchronously, and user callbacks must never run while the lifecycle lock is held.
        _ = DisposeCoreAndCompleteAsync(completion);
        return new ValueTask(disposeTask);
    }

    private async Task DisposeCoreAndCompleteAsync(TaskCompletionSource<bool> completion)
    {
        try
        {
            await DisposeCoreAsync().ConfigureAwait(false);
            completion.TrySetResult(true);
        }
        catch (Exception ex)
        {
            completion.TrySetException(ex);
        }
    }

    private async Task DisposeCoreAsync()
    {
        // Signal cooperative shutdown. Safe: _shutdownCts is only disposed by
        // ReleaseSharedResourcesOnce, which runs after _activeWorkers reaches 0, and no
        // worker can reach 0 before observing this cancellation.
        try
        {
            _shutdownCts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Already released by the last worker out; nothing to signal.
        }

        // Release the ownership of not-yet-started items exactly once so the queued counter
        // can settle at zero. Items a worker already dequeued are released by that worker's
        // queued->active transition; ConcurrentQueue hands each item to exactly one taker,
        // so there is no double count.
        DrainQueuedItems();

        // Bounded wait for in-flight executions to finish. Cooperative work is cancelled
        // above and should exit promptly.
        var deadline = DateTime.UtcNow + _disposeDrainTimeout;
        while (_activeWorkers > 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(WorkerPollIntervalMs).ConfigureAwait(false);
        }

        // If every worker exited, release shared resources now. Otherwise a non-cooperative
        // execution outlived the bounded wait; the LAST worker to exit releases them, so we
        // must NOT dispose shared CTS/notification state here while a worker may still use it.
        if (_activeWorkers == 0)
            ReleaseSharedResourcesOnce();
    }

    private void DrainQueuedItems()
    {
        for (int i = 0; i < _maxConcurrency; i++)
        {
            while (_workerQueues[i].TryDequeue(out _))
            {
                Interlocked.Decrement(ref _totalQueuedTasks);
            }
        }
    }

    private void ReleaseSharedResourcesOnce()
    {
        if (Interlocked.Exchange(ref _sharedReleased, 1) == 1)
            return;

        _notifyDebounce?.Dispose();
        _shutdownCts?.Dispose();
    }
}
