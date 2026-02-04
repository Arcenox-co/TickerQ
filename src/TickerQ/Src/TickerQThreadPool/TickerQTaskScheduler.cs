using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;

namespace TickerQ.TickerQThreadPool;

/// <summary>
/// Elastic work-stealing task scheduler.
/// </summary>
public sealed class TickerQTaskScheduler : IAsyncDisposable, ITickerQTaskScheduler
{
    private readonly int _maxConcurrency;
    private readonly TimeSpan _idleWorkerTimeout;
    private readonly int _maxCapacityPerWorker;
    
    // Worker queues for work stealing
    private readonly ConcurrentQueue<WorkItem>[] _workerQueues;
    
    // Global state
    private volatile int _totalQueuedTasks;
    private volatile int _activeWorkers;
    private volatile bool _disposed;
    private volatile bool _isFrozen;
    private volatile int _nextQueueIndex;
    
    private readonly CancellationTokenSource _shutdownCts = new();
    private readonly SoftSchedulerNotifyDebounce _notifyDebounce;
    
    // Thread-local flag to detect if we're on a TickerQ worker thread
    [ThreadStatic] public static bool IsTickerQWorkerThread;
    [ThreadStatic] private static int _threadWorkerIndex = -1;
    
    public TickerQTaskScheduler(
        int maxConcurrency, 
        TimeSpan? idleWorkerTimeout = null,
        SoftSchedulerNotifyDebounce notifyDebounce = null)
    {
        if (maxConcurrency <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxConcurrency), "Must be greater than zero");
        
        _maxConcurrency = maxConcurrency;
        _idleWorkerTimeout = idleWorkerTimeout ?? TimeSpan.FromSeconds(60);
        _maxCapacityPerWorker = 1024; // Fixed optimal capacity
        _notifyDebounce = notifyDebounce ?? new SoftSchedulerNotifyDebounce(_ => { });
        
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
    /// The priority parameter is ignored in this simplified version.
    /// </summary>
    public async ValueTask QueueAsync(
        Func<CancellationToken, Task> work,
        TickerTaskPriority priority, // Kept for backward compatibility but ignored
        CancellationToken cancellationToken = default)
    {
        if (work == null)
            throw new ArgumentNullException(nameof(work));
            
        if (_disposed)
            throw new ObjectDisposedException(nameof(TickerQTaskScheduler));
            
        if (_isFrozen)
            throw new InvalidOperationException("Scheduler is frozen");
        
        // Handle long-running tasks specially
        if (priority == TickerTaskPriority.LongRunning)
        {
            // Bypass pool for long-running tasks
            _ = Task.Factory.StartNew(
                async () => 
                {
                    try 
                    { 
                        await work(cancellationToken); 
                    } 
                    catch 
                    { 
                        /* swallow */ 
                    }
                },
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default).Unwrap();
            return;
        }
        
        // Round-robin distribution across worker queues
        var queueIndex = GetNextQueueIndex();
        var targetQueue = _workerQueues[queueIndex];
        
        // Wait for capacity if needed
        await WaitForCapacityAsync(targetQueue, cancellationToken);
        
        // Enqueue work
        var workItem = new WorkItem(work, cancellationToken);
        targetQueue.Enqueue(workItem);
        var newTotal = Interlocked.Increment(ref _totalQueuedTasks);
        
        // Ensure we have workers to process the work
        // Check both queue count and total to avoid race conditions
        if (newTotal > 0 || targetQueue.Count > 0)
        {
            EnsureWorkerAvailable();
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
        while (queue.Count >= _maxCapacityPerWorker && !cancellationToken.IsCancellationRequested)
        {
            if (++waitCount > 100) // After 1 second, check if we're stuck
            {
                // Force start a worker if we're waiting too long
                EnsureWorkerAvailable();
                waitCount = 0;
            }
            await Task.Delay(10, cancellationToken);
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
        
        // Start more workers if we have queued tasks
        var totalQueued = _totalQueuedTasks;
        var activeWorkers = _activeWorkers;
        
        // If we have tasks but not enough workers, start more
        if (totalQueued > 0 && activeWorkers < _maxConcurrency)
        {
            // Start workers proportional to load
            var desiredWorkers = Math.Min(totalQueued, _maxConcurrency);
            if (desiredWorkers > activeWorkers)
            {
                TryStartWorker();
            }
        }
    }
    
    private void TryStartWorker()
    {
        if (_shutdownCts.IsCancellationRequested || _disposed)
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
            Interlocked.Decrement(ref _activeWorkers);
            _notifyDebounce.NotifySafely(_activeWorkers);
        }
    }
    
    private async Task WorkerLoopCoreAsync(int workerId)
    {
        var lastWorkTime = DateTime.UtcNow;
        var localQueue = _workerQueues[workerId];
        var consecutiveStealFailures = 0;
        
        while (!_shutdownCts.Token.IsCancellationRequested && !_disposed)
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
                    
                    // Only exit if there's really no work and we have minimum workers
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
        // Decrement immediately after dequeue to keep counter in sync
        Interlocked.Decrement(ref _totalQueuedTasks);
        
        try
        {
            // Check cancellation before executing
            if (!workItem.UserToken.IsCancellationRequested && !_shutdownCts.Token.IsCancellationRequested)
            {
                // Start the work without awaiting it so this worker
                // can continue processing other items while the task awaits.
                var task = workItem.Work(workItem.UserToken);
                
                if (task == null)
                    return;

                if (!task.IsCompleted)
                {
                    // Observe completion and exceptions without blocking the worker loop
                    _ = task.ContinueWith(t =>
                    {
                        try
                        {
                            if (t.IsFaulted)
                            {
                                _ = t.Exception;
                            }
                        }
                        catch
                        {
                            // Swallow continuation exceptions
                        }
                    }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                }
                else
                {
                    // Task already completed synchronously – observe any exception
                    try
                    {
                        await task;
                    }
                    catch
                    {
                        // Swallow exceptions to keep worker alive
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected - task was cancelled
        }
        catch (Exception)
        {
            // Log error if needed, but don't crash the worker
            // Errors are swallowed to prevent worker thread crashes
        }
    }
    
    /// <summary>
    /// Posts a continuation work item to the scheduler.
    /// Used by TickerQSynchronizationContext.
    /// </summary>
    internal void PostContinuation(SendOrPostCallback callback, object state)
    {
        if (_disposed || _shutdownCts.Token.IsCancellationRequested)
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
        
        targetQueue.Enqueue(workItem);
        // Count continuations to keep counter accurate
        Interlocked.Increment(ref _totalQueuedTasks);
        
        // Ensure worker is available
        EnsureWorkerAvailable();
    }
    
    /// <summary>
    /// Freezes the scheduler - prevents new tasks from being queued.
    /// </summary>
    public void Freeze() => _isFrozen = true;
    
    /// <summary>
    /// Resumes the scheduler - allows new tasks to be queued again.
    /// </summary>
    public void Resume() => _isFrozen = false;
    
    /// <summary>
    /// Gets whether the scheduler is currently frozen.
    /// </summary>
    public bool IsFrozen => _isFrozen;
    
    /// <summary>
    /// Gets the current number of active worker threads.
    /// </summary>
    public int ActiveWorkers => _activeWorkers;
    
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
        
        // Discrepancy check
        if (totalInQueues != _totalQueuedTasks)
        {
            text += $"\n⚠️ DISCREPANCY: Counter shows {_totalQueuedTasks} but queues have {totalInQueues} tasks!\n";
        }
        
        return text;
    }
    
    /// <summary>
    /// Waits for all currently running tasks to complete.
    /// </summary>
    public async Task<bool> WaitForRunningTasksAsync(TimeSpan? timeout = null)
    {
        var deadline = timeout.HasValue ? DateTime.UtcNow.Add(timeout.Value) : DateTime.MaxValue;
        
        while (_totalQueuedTasks > 0 || _activeWorkers > 0)
        {
            if (DateTime.UtcNow > deadline)
                return false; // Timeout
            
            await Task.Delay(10);
        }
        
        return true;
    }
    
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        
        _disposed = true;
        _isFrozen = true; // Prevent new tasks
        _shutdownCts.Cancel();
        
        // Wait for workers to exit gracefully
        var timeout = DateTime.UtcNow.AddSeconds(5);
        while (_activeWorkers > 0 && DateTime.UtcNow < timeout)
        {
            await Task.Delay(100);
        }
        
        _notifyDebounce?.Dispose();
        _shutdownCts?.Dispose();
    }
}
