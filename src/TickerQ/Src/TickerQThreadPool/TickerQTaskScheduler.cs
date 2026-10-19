using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;

namespace TickerQ.TickerQThreadPool;

/// <summary>
/// Bounded, signaled asynchronous worker queue. A worker owns the entire async lifetime
/// of a work item; normal and LongRunning work share the same concurrency budget.
/// High, Normal and Low queues are FIFO and receive weighted fair service (4:2:1).
/// LongRunning shares Normal admission priority and starts on a dedicated thread.
/// </summary>
public sealed class TickerQTaskScheduler : IAsyncDisposable, ITickerQTaskScheduler, ITickerQExecutionScheduler
{
    private static readonly TimeSpan DefaultDisposeDrainTimeout = TimeSpan.FromSeconds(5);
    private static readonly int[] PrioritySchedule = [0, 0, 0, 0, 1, 1, 2];
    private readonly int _maxConcurrency;
    private readonly int _queueCapacity;
    private readonly int _orchestrationCapacity;
    private readonly TimeSpan _disposeDrainTimeout;
    private readonly object _stateLock = new();
    private readonly Queue<WorkItem>[] _queues = [new(), new(), new()];
    private readonly SemaphoreSlim _availableItems = new(0);
    private readonly CancellationTokenSource _shutdownCts = new();
    private readonly CancellationToken _stopToken;
    private readonly SoftSchedulerNotifyDebounce _notifyDebounce;
    private readonly Task _workersCompleted;
    private readonly TaskCompletionSource<bool> _allStopped = NewSignal();
    private volatile int _activeOrchestrations;
    private int _waitingForFunction;
    private int _retrying;
    private TaskCompletionSource<bool> _capacityChanged;
    private TaskCompletionSource<bool> _drained = NewSignal();
    private Task _disposeTask;
    private int _priorityCursor;
    private long _admissionGeneration;
    private volatile int _totalQueuedTasks;
    private volatile int _activeExecutions;
    private volatile int _activeWorkers;
    private volatile bool _disposed;
    private volatile bool _isFrozen;
    private int _shutdownSignalled;
    private int _shutdownCallbacksFinished;
    private int _sharedReleased;

    // Direct reentrant QueueAsync calls keep the legacy inline/await-completion contract.
    // Scopes expire and retain their children, preventing detached tasks from retaining
    // an execution slot after its owning work item has completed.
    private static readonly AsyncLocal<ExecutionScope> _currentExecutionScope = new();
    private static readonly AsyncLocal<ExecutionScope> _currentOrchestrationScope = new();

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

    /// <summary>
    /// Creates a queue with 1024 waiting items per configured execution slot.
    /// Idle asynchronous workers wait for a signal without occupying an operating-system thread.
    /// </summary>
    public TickerQTaskScheduler(int maxConcurrency, SoftSchedulerNotifyDebounce notifyDebounce = null)
        : this(maxConcurrency, notifyDebounce, DefaultDisposeDrainTimeout)
    {
    }

    /// <summary>Creates a scheduler with an explicit total waiting-queue capacity.</summary>
    internal TickerQTaskScheduler(int maxConcurrency, int queueCapacity,
        SoftSchedulerNotifyDebounce notifyDebounce = null)
        : this(maxConcurrency, notifyDebounce, DefaultDisposeDrainTimeout, queueCapacity)
    {
    }

    internal TickerQTaskScheduler(int maxConcurrency,
        SoftSchedulerNotifyDebounce notifyDebounce, TimeSpan disposeDrainTimeout)
        : this(maxConcurrency, notifyDebounce, disposeDrainTimeout,
            (int)Math.Min((long)maxConcurrency * 1024, int.MaxValue))
    {
    }

    internal TickerQTaskScheduler(int maxConcurrency, int queueCapacity, int orchestrationCapacity)
        : this(maxConcurrency, null, DefaultDisposeDrainTimeout, queueCapacity, orchestrationCapacity)
    {
    }

    private TickerQTaskScheduler(int maxConcurrency,
        SoftSchedulerNotifyDebounce notifyDebounce, TimeSpan disposeDrainTimeout, int queueCapacity, int? orchestrationCapacity = null)
    {
        if (maxConcurrency <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxConcurrency), "Must be greater than zero");
        if (queueCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(queueCapacity), "Must be greater than zero");
        if (disposeDrainTimeout < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(disposeDrainTimeout));

        _maxConcurrency = maxConcurrency;
        if (orchestrationCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(orchestrationCapacity));
        _queueCapacity = queueCapacity;
        _orchestrationCapacity = orchestrationCapacity ?? (int)Math.Min((long)queueCapacity + maxConcurrency, int.MaxValue);
        _disposeDrainTimeout = disposeDrainTimeout;
        _notifyDebounce = notifyDebounce ?? new SoftSchedulerNotifyDebounce(_ => { });
        _stopToken = _shutdownCts.Token;
        _drained.TrySetResult(true);
        _activeWorkers = maxConcurrency;
        var workers = new Task[maxConcurrency];
        // The scheduler can be resolved inside an application request/job scope. Worker
        // consumers must not retain that caller's AsyncLocals or reentrancy ownership.
        if (ExecutionContext.IsFlowSuppressed())
            StartWorkers();
        else
        {
            using (ExecutionContext.SuppressFlow())
                StartWorkers();
        }
        _workersCompleted = Task.WhenAll(workers);
        ExecutionScope.Observe(_workersCompleted);
        NotifyWorkers(maxConcurrency);

        void StartWorkers()
        {
            for (var i = 0; i < workers.Length; i++)
                workers[i] = Task.Run(WorkerLoopAsync);
        }
    }

    public int QueueCapacity => _queueCapacity;

    /// <summary>
    /// Admits a work item, waiting asynchronously for capacity. Existing direct nested
    /// calls execute within their owning scope and await completion for compatibility.
    /// A cancelled admission completes without executing, as in earlier versions.
    /// </summary>
    public ValueTask QueueAsync(Func<CancellationToken, Task> work, TickerTaskPriority priority,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (work == null) throw new ArgumentNullException(nameof(work));
            ExecutionScope nestedScope = null;
            long generation;
            lock (_stateLock)
            {
                ThrowIfUnavailable();
                generation = _admissionGeneration;
                var currentScope = _currentExecutionScope.Value;
                if (ReferenceEquals(currentScope?.Owner, this) && currentScope.TryAcquireNested())
                    nestedScope = currentScope;
            }
            if (nestedScope != null)
            {
                var task = ExecuteNestedAsync(nestedScope, work, priority, cancellationToken);
                ExecutionScope.Observe(task);
                return new ValueTask(task);
            }
            return QueueCoreAsync(work, priority, cancellationToken, generation);
        }
        catch (Exception ex)
        {
            return ValueTask.FromException(ex);
        }
    }

    private async ValueTask QueueCoreAsync(Func<CancellationToken, Task> work,
        TickerTaskPriority priority, CancellationToken cancellationToken, long generation,
        bool acceptedOrchestration = false, WorkItemCompletion completion = null, ExecutionContext executionContext = null)
    {
        while (true)
        {
            Task capacity;
            lock (_stateLock)
            {
                if (_disposed && completion != null)
                {
                    completion.CancelBeforeStart(_stopToken);
                    return;
                }
                ThrowIfUnavailable(generation, acceptedOrchestration);
                if (cancellationToken.IsCancellationRequested)
                {
                    completion?.CancelBeforeStart(cancellationToken);
                    return;
                }
                if (_totalQueuedTasks < _queueCapacity)
                {
                    if (_totalQueuedTasks == 0 && _activeExecutions == 0 && _activeOrchestrations == 0)
                        _drained = NewSignal();
                    // Capacity reservation, publication and lifecycle checks are atomic.
                    // Count before signaling, so a consumer cannot observe an unowned item.
                    _totalQueuedTasks++;
                    _queues[GetPriorityQueue(priority)].Enqueue(new WorkItem(work, cancellationToken, priority, completion, executionContext));
                    _availableItems.Release();
                    return;
                }
                capacity = (_capacityChanged ??= NewSignal()).Task;
            }
            try
            {
                await capacity.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Freeze/disposal wins over cancellation, preserving lifecycle rejection.
                lock (_stateLock)
                {
                    if (_disposed && completion != null)
                    {
                        completion.CancelBeforeStart(_stopToken);
                        return;
                    }
                    ThrowIfUnavailable(generation, acceptedOrchestration);
                }
                completion?.CancelBeforeStart(cancellationToken);
                return;
            }
        }
    }

    private void ThrowIfUnavailable(long? generation = null, bool acceptedOrchestration = false)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(TickerQTaskScheduler));
        if (!acceptedOrchestration && (_isFrozen || (generation.HasValue && generation.Value != _admissionGeneration)))
            throw new InvalidOperationException("Scheduler is frozen");
    }

    private static int GetPriorityQueue(TickerTaskPriority priority)
        => priority == TickerTaskPriority.High ? 0 : priority == TickerTaskPriority.Low ? 2 : 1;

    private static TaskCompletionSource<bool> NewSignal()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    // Must be called under _stateLock; continuations always run asynchronously.
    private void SignalCapacityChanged()
    {
        var previous = _capacityChanged;
        _capacityChanged = null;
        previous?.TrySetResult(true);
    }

    private void SignalDrainedIfIdle()
    {
        if (_totalQueuedTasks == 0 && _activeExecutions == 0 && _activeOrchestrations == 0)
            _drained.TrySetResult(true);
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

    private async Task WorkerLoopAsync()
    {
        try
        {
            while (true)
            {
                await _availableItems.WaitAsync(_stopToken).ConfigureAwait(false);
                WorkItem item;
                lock (_stateLock)
                {
                    if (_disposed) return;
                    item = DequeueFairly();
                    // Publish active ownership before removing queued ownership so drain
                    // observers never see a false zero during the handoff.
                    _activeExecutions++;
                    _totalQueuedTasks--;
                    SignalCapacityChanged();
                }
                try
                {
                    if (item.Completion != null && !item.Completion.TryBegin()) continue;
                    if (!item.UserToken.IsCancellationRequested && !_stopToken.IsCancellationRequested)
                    {
                        if (item.ExecutionContext == null)
                            await ExecuteWorkAsync(item).ConfigureAwait(false);
                        else
                        {
                            Task execution = null;
                            ExecutionContext.Run(item.ExecutionContext, _ => execution = ExecuteWorkAsync(item), null);
                            await execution.ConfigureAwait(false);
                        }
                    }
                    else
                        item.Completion?.CompleteCancellation(item.UserToken.IsCancellationRequested ? item.UserToken : _stopToken);
                }
                finally
                {
                    lock (_stateLock)
                    {
                        _activeExecutions--;
                        SignalDrainedIfIdle();
                    }
                    item = default; // Do not retain the last job/request/ExecutionContext while idle.
                }
            }
        }
        catch (OperationCanceledException) when (_stopToken.IsCancellationRequested)
        {
        }
        finally
        {
            var remaining = Interlocked.Decrement(ref _activeWorkers);
            NotifyWorkers(remaining);
            CompleteStopIfIdle();
        }
    }

    // All callers hold _stateLock. The finite cyclic schedule guarantees service to
    // every nonempty priority lane, even under a continuously replenished High queue.
    private WorkItem DequeueFairly()
    {
        for (var i = 0; i < PrioritySchedule.Length; i++)
        {
            var queue = _queues[PrioritySchedule[_priorityCursor]];
            _priorityCursor = (_priorityCursor + 1) % PrioritySchedule.Length;
            if (queue.Count > 0) return queue.Dequeue();
        }
        throw new InvalidOperationException("A signaled scheduler item was not present in its queue.");
    }

    private async Task ExecuteWorkAsync(WorkItem workItem)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(workItem.UserToken, _stopToken);
        var previousScope = _currentExecutionScope.Value;
        var executionScope = new ExecutionScope(this);
        _currentExecutionScope.Value = executionScope;
        Exception failure = null;
        try
        {
            if (workItem.Priority == TickerTaskPriority.LongRunning)
            {
                await Task.Factory.StartNew(
                    () => workItem.Work(linked.Token) ?? Task.CompletedTask,
                    CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)
                    .Unwrap().ConfigureAwait(false);
            }
            else
            {
                var task = workItem.Work(linked.Token);
                if (task != null) await task.ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            // Direct QueueAsync preserves fire-and-forget behavior; ExecuteAsync callers
            // receive the original result after the attempt releases its complete lifetime.
            failure = ex;
        }
        finally
        {
            await executionScope.CloseAndWaitAsync().ConfigureAwait(false);
            _currentExecutionScope.Value = previousScope;
        }
        if (failure != null)
            // Preserve an original OperationCanceledException too. Converting it into a
            // generic TaskCanceledException changes the handler's cancellation/retry
            // classification when the job token itself was never cancelled.
            workItem.Completion?.CompleteFailure(failure);
        else
            workItem.Completion?.CompleteSuccess();
    }

    /// <summary>Rejects new work and wakes pending capacity waiters. Accepted work drains.</summary>
    public void Freeze()
    {
        lock (_stateLock)
        {
            if (!_isFrozen) _admissionGeneration++;
            _isFrozen = true;
            SignalCapacityChanged();
        }
    }

    public void Resume()
    {
        lock (_stateLock)
        {
            if (!_disposed) _isFrozen = false;
        }
    }

    public bool IsFrozen => _isFrozen;
    /// <summary>Live asynchronous queue consumers, not dedicated operating-system threads.</summary>
    public int ActiveWorkers => _activeWorkers;
    /// <summary>Dequeued work items whose complete asynchronous lifetime is still active.</summary>
    public int ActiveExecutionCount => _activeExecutions;
    public int ActiveOrchestrationCount => _activeOrchestrations;
    public int WaitingForFunctionCount => Volatile.Read(ref _waitingForFunction);
    public int RetryingCount => Volatile.Read(ref _retrying);
    public int TotalQueuedTasks => _totalQueuedTasks;
    public bool IsDisposed => _disposed;

    public string GetDiagnostics()
    {
        lock (_stateLock)
            return $"=== TickerQ Async Scheduler ===\n" +
                $"Status: {(_disposed ? "DISPOSED" : _isFrozen ? "FROZEN" : "ACTIVE")}\n" +
                $"Workers: {_activeWorkers}/{_maxConcurrency} (asynchronous consumers)\n" +
                $"Active Executions: {_activeExecutions}\n" +
                $"Active Orchestrations: {_activeOrchestrations}/{_orchestrationCapacity}\n" +
                $"Waiting For Function: {WaitingForFunctionCount}; Retrying: {RetryingCount}\n" +
                $"Total Queued (counter): {_totalQueuedTasks}/{_queueCapacity}\n" +
                $"Priority Queues: High={_queues[0].Count}, Normal={_queues[1].Count}, Low={_queues[2].Count}\n";
    }

    public Task<bool> WaitForRunningTasksAsync(TimeSpan? timeout = null)
        => WaitForRunningTasksAsync(timeout, CancellationToken.None);

    public async Task<bool> WaitForRunningTasksAsync(TimeSpan? timeout, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested) return false;
        Task drained;
        lock (_stateLock)
        {
            if (_totalQueuedTasks == 0 && _activeExecutions == 0 && _activeOrchestrations == 0) return true;
            drained = _drained.Task;
        }
        if (timeout.HasValue && timeout.Value <= TimeSpan.Zero) return false;
        var elapsed = Stopwatch.StartNew();
        try
        {
            if (timeout.HasValue)
                await drained.WaitAsync(timeout.Value, cancellationToken).ConfigureAwait(false);
            else
                await drained.WaitAsync(cancellationToken).ConfigureAwait(false);
            return !cancellationToken.IsCancellationRequested && (!timeout.HasValue || elapsed.Elapsed < timeout.Value);
        }
        catch (TimeoutException) { return false; }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return false; }
    }

    public ValueTask DisposeAsync()
    {
        TaskCompletionSource<bool> completion;
        lock (_stateLock)
        {
            if (_disposeTask != null) return new ValueTask(_disposeTask);
            _disposed = true;
            _isFrozen = true;
            _admissionGeneration++;
            SignalCapacityChanged();
            completion = NewSignal();
            _disposeTask = completion.Task;
        }
        _ = DisposeCoreAndCompleteAsync(completion);
        return new ValueTask(completion.Task);
    }

    private async Task DisposeCoreAndCompleteAsync(TaskCompletionSource<bool> completion)
    {
        try
        {
            // Cancellation is observable immediately, but callbacks execute asynchronously.
            // A user callback that blocks must not prevent draining the queue or starting
            // the bounded shutdown wait. Shared resources stay alive until callbacks finish.
            Task callbacks = Task.CompletedTask;
            try { callbacks = _shutdownCts.CancelAsync(); }
            catch (ObjectDisposedException) { }
            finally
            {
                lock (_stateLock)
                {
                    foreach (var queue in _queues)
                    {
                        while (queue.Count > 0) queue.Dequeue().Completion?.CancelBeforeStart(_stopToken);
                    }
                    _totalQueuedTasks = 0;
                    SignalCapacityChanged();
                    SignalDrainedIfIdle();
                }
                Volatile.Write(ref _shutdownSignalled, 1);
            }

            _ = ObserveShutdownCallbacksAsync(callbacks);
            CompleteStopIfIdle();
            try { await _allStopped.Task.WaitAsync(_disposeDrainTimeout).ConfigureAwait(false); }
            catch (TimeoutException) { /* Non-cooperative work releases its resources when its last worker exits. */ }
            CompleteStopIfIdle();
            completion.TrySetResult(true);
        }
        catch (Exception ex)
        {
            CompleteStopIfIdle();
            completion.TrySetException(ex);
        }
    }

    bool ITickerQExecutionScheduler.TryQueueOrchestration(Func<CancellationToken, Task> work, CancellationToken admissionToken)
    {
        if (work == null) throw new ArgumentNullException(nameof(work));
        lock (_stateLock)
        {
            if (_disposed || _isFrozen || admissionToken.IsCancellationRequested || _activeOrchestrations >= _orchestrationCapacity)
                return false;
            if (_activeOrchestrations == 0 && _activeExecutions == 0 && _totalQueuedTasks == 0)
                _drained = NewSignal();
            _activeOrchestrations++;
        }
        // Never inherit an executing parent's inline QueueAsync scope. The new root has
        // its own accepted lifetime, even when admitted by a job at concurrency one.
        Task task;
        if (ExecutionContext.IsFlowSuppressed())
            task = Task.Run(() => RunOrchestrationAsync(work));
        else
        {
            using (ExecutionContext.SuppressFlow())
                task = Task.Run(() => RunOrchestrationAsync(work));
        }
        ExecutionScope.Observe(task);
        return true;
    }

    private async Task RunOrchestrationAsync(Func<CancellationToken, Task> work)
    {
        var scope = new ExecutionScope(this);
        _currentOrchestrationScope.Value = scope;
        try
        {
            // Always enter the accepted callback, even when already cancelled: acquired
            // persistence generations need their execution/cleanup boundary to run.
            // Admission cancellation belongs to the acquired-generation lifecycle until
            // it begins; a scanner/request ending must not cancel a running job.
            var task = work(_stopToken);
            if (task != null) await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_stopToken.IsCancellationRequested) { }
        finally
        {
            await scope.CloseAndWaitAsync().ConfigureAwait(false);
            _currentOrchestrationScope.Value = null;
            lock (_stateLock)
            {
                _activeOrchestrations--;
                SignalDrainedIfIdle();
            }
            CompleteStopIfIdle();
        }
    }

    async Task ITickerQExecutionScheduler.ExecuteAsync(Func<CancellationToken, Task> work,
        TickerTaskPriority priority, CancellationToken token)
    {
        if (work == null) throw new ArgumentNullException(nameof(work));
        ExecutionScope owner = null;
        long generation;
        lock (_stateLock)
        {
            if (_disposed) throw new TaskCanceledException("Scheduler is stopping.");
            var scope = _currentOrchestrationScope.Value;
            if (ReferenceEquals(scope?.Owner, this) && scope.TryAcquireNested()) owner = scope;
            try { ThrowIfUnavailable(acceptedOrchestration: owner != null); }
            catch { owner?.ReleaseNested(); throw; }
            generation = _admissionGeneration;
        }
        try
        {
            var completion = new WorkItemCompletion();
            using var userRegistration = token.Register(() => completion.CancelBeforeStart(token));
            using var stopRegistration = _stopToken.Register(() => completion.CancelBeforeStart(_stopToken));
            await QueueCoreAsync(work, priority, token, generation, owner != null, completion, ExecutionContext.Capture()).ConfigureAwait(false);
            await completion.Task.ConfigureAwait(false);
        }
        finally { owner?.ReleaseNested(); }
    }

    IDisposable ITickerQExecutionScheduler.BeginFunctionWait() => new CounterScope(this, retry: false);
    IDisposable ITickerQExecutionScheduler.BeginRetryDelay() => new CounterScope(this, retry: true);

    private sealed class CounterScope : IDisposable
    {
        private TickerQTaskScheduler _owner;
        private readonly bool _retry;
        internal CounterScope(TickerQTaskScheduler owner, bool retry)
        {
            _owner = owner;
            _retry = retry;
            if (retry) Interlocked.Increment(ref owner._retrying);
            else Interlocked.Increment(ref owner._waitingForFunction);
        }
        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            if (owner == null) return;
            if (_retry) Interlocked.Decrement(ref owner._retrying);
            else Interlocked.Decrement(ref owner._waitingForFunction);
        }
    }

    private async Task ObserveShutdownCallbacksAsync(Task callbacks)
    {
        try { await callbacks.ConfigureAwait(false); }
        catch (Exception ex) { Trace.TraceError("TickerQ cancellation callback failed during shutdown: {0}", ex); }
        finally
        {
            Volatile.Write(ref _shutdownCallbacksFinished, 1);
            CompleteStopIfIdle();
        }
    }

    private void CompleteStopIfIdle()
    {
        if (!_disposed || Volatile.Read(ref _shutdownSignalled) == 0 ||
            Volatile.Read(ref _shutdownCallbacksFinished) == 0 || _activeWorkers != 0 || _activeOrchestrations != 0)
            return;
        ReleaseSharedResourcesOnce();
    }

    private void NotifyWorkers(int count)
    {
        try { _notifyDebounce.NotifySafely(count); }
        catch (Exception ex) { Trace.TraceError("TickerQ worker notification failed: {0}", ex); }
    }

    private void ReleaseSharedResourcesOnce()
    {
        if (Interlocked.Exchange(ref _sharedReleased, 1) != 0) return;
        _notifyDebounce.Dispose();
        _availableItems.Dispose();
        _shutdownCts.Dispose();
        _allStopped.TrySetResult(true);
    }
}
