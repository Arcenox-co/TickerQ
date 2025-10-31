using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Enums;

namespace TickerQ.TickerQThreadPool;

/// <summary>
/// Process-lifetime singleton task scheduler with optimized memory usage and elastic workers.
/// Fire-and-forget execution with strict priority ordering and work stealing.
/// </summary>
public sealed class TickerQTaskScheduler : IAsyncDisposable
{
    private readonly int _maxConcurrency;
    private readonly TimeSpan _idleWorkerTimeout;
    private readonly int _maxTotalCapacity;
    private readonly int _maxCapacityPerWorker;
    
    // Single priority queue per worker (lazy initialized)
    private readonly ConcurrentQueue<PriorityTask>[] _workerQueues;
    private ulong _workerInitialized; // Bitfield for up to 64 workers (use Interlocked for thread safety)
    
    // Global capacity tracking
    private volatile int _totalQueuedTasks;
    
    // Worker assignment
    private volatile int _nextWorkerId;
    private volatile int _randomSeed;
    
    private readonly CancellationTokenSource _shutdownCts = new();
    private volatile int _activeWorkers;
    private volatile bool _disposed;
    private volatile bool _isFrozen;
    private readonly SoftSchedulerNotifyDebounce _notifyDebounce;
    // Thread-local temp array for priority dequeuing (avoids allocations)
    [ThreadStatic] private static PriorityTask[] _tempTasks;
    
    // Thread-local flag to detect if we're on a TickerQ worker thread
    [ThreadStatic] public static bool IsTickerQWorkerThread;
    

    public TickerQTaskScheduler(int maxConcurrency, TimeSpan? idleWorkerTimeout = null, SoftSchedulerNotifyDebounce notifyDebounce = null)
    {
        if (maxConcurrency <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxConcurrency), maxConcurrency, "Must be greater than zero");
        if (maxConcurrency > 64)
            throw new ArgumentOutOfRangeException(nameof(maxConcurrency), maxConcurrency, "Maximum 64 workers supported due to bitfield limitations");

        // Validate idle worker timeout
        var timeout = idleWorkerTimeout ?? TimeSpan.FromSeconds(60);
        if (timeout < TimeSpan.FromSeconds(1))
            throw new ArgumentOutOfRangeException(nameof(idleWorkerTimeout), timeout, "Idle timeout must be at least 1 second");
        if (timeout > TimeSpan.FromHours(24))
            throw new ArgumentOutOfRangeException(nameof(idleWorkerTimeout), timeout, "Idle timeout cannot exceed 24 hours");

        _maxConcurrency = maxConcurrency;
        _idleWorkerTimeout = timeout;
        _maxCapacityPerWorker = 1024; // Fixed optimal capacity per worker
        _maxTotalCapacity = maxConcurrency * 1024; // Auto-calculated: queues × 1024
        _notifyDebounce = notifyDebounce ?? new SoftSchedulerNotifyDebounce(_ => { }); // Default no-op notifier
        
        // Lazy initialization - only create queues when needed
        _workerQueues = new ConcurrentQueue<PriorityTask>[maxConcurrency];
        _workerInitialized = 0; // Bitfield initialized to 0
    }


    public async ValueTask QueueAsync(Func<CancellationToken, Task> work, TickerTaskPriority priority, CancellationToken userCancellationToken = default)
    {
        if (work == null)
            throw new ArgumentNullException(nameof(work));
            
        if (_disposed)
            throw new ObjectDisposedException(nameof(TickerQTaskScheduler));
        
        if (_isFrozen)
            throw new InvalidOperationException("Scheduler is frozen - no new tasks can be queued");

        if (priority == TickerTaskPriority.LongRunning)
        {
            // Bypass pool on default scheduler with LongRunning; use user token for task cancellation.
            _ = Task.Factory.StartNew(
                async () => { try { await work(userCancellationToken); } catch { /* swallow */ } },
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default).Unwrap();
            return;
        }

        // Get worker using thread-safe round-robin
        var workerId = GetNextWorkerId();
        var workerQueue = GetOrCreateWorkerQueue(workerId);

        // Wait for capacity (both global and per-worker limits)
        await WaitForCapacity(workerQueue, userCancellationToken);

        // Create optimized priority task (no wrapper needed)
        var priorityTask = new PriorityTask(priority, work, userCancellationToken, shouldDecrementTotal: true);

        Interlocked.Increment(ref _totalQueuedTasks);
        workerQueue.Enqueue(priorityTask);
        TryStartWorker();
    }

    private async ValueTask WaitForCapacity(ConcurrentQueue<PriorityTask> targetQueue, CancellationToken cancellationToken)
    {
        var queueCount = targetQueue.Count;
        var waitCount = 0;

        while (queueCount >= _maxCapacityPerWorker || _totalQueuedTasks >= _maxTotalCapacity)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Adaptive backpressure mechanism
            // Start with small delays and increase as we wait longer
            int delayMs;
            if (waitCount < 5)
            {
                delayMs = 1; // First 5 attempts: 1ms
            }
            else if (waitCount < 20)
            {
                delayMs = 5; // Next 15 attempts: 5ms
            }
            else if (waitCount < 100)
            {
                delayMs = 10; // Next 80 attempts: 10ms
            }
            else
            {
                delayMs = 25; // After 100 attempts: 25ms (significant backpressure)
            }

            await Task.Delay(delayMs, cancellationToken);
            waitCount++;

            // Re-check queue count after delay
            queueCount = targetQueue.Count;
            
            // If we've been waiting too long, try to spawn another worker
            // This helps prevent deadlocks when all workers are busy
            if (waitCount == 50 && _activeWorkers < _maxConcurrency)
            {
                TryStartWorker();
            }
        }
    }

    private ConcurrentQueue<PriorityTask> GetOrCreateWorkerQueue(int workerId)
    {
        if (workerId < 0 || workerId >= 64)
            throw new ArgumentOutOfRangeException(nameof(workerId), workerId, "Worker ID must be between 0 and 63");
            
        ulong workerBit = 1UL << workerId;
        
        // Fast path: Check if already initialized using atomic read
        var initialized = Interlocked.Read(ref _workerInitialized);
        if ((initialized & workerBit) != 0)
        {
            // Queue already initialized - return it
            // This is safe because queues are never removed once created
            return _workerQueues[workerId];
        }
        
        // Slow path: Need to initialize the queue
        lock (_workerQueues)
        {
            // Double-check pattern: Re-read inside lock to handle race conditions
            initialized = Interlocked.Read(ref _workerInitialized);
            if ((initialized & workerBit) == 0)
            {
                // Create the queue
                _workerQueues[workerId] = new ConcurrentQueue<PriorityTask>();
                
                // Atomically set the bit to indicate this worker queue is initialized
                // Use CAS loop to ensure thread-safe bitfield update
                ulong currentValue, newValue;
                do
                {
                    currentValue = Interlocked.Read(ref _workerInitialized);
                    newValue = currentValue | workerBit;
                } while (Interlocked.CompareExchange(ref _workerInitialized, newValue, currentValue) != currentValue);
            }
        }
        
        return _workerQueues[workerId];
    }

    /// <summary>
    /// Thread-safe method to get the next worker ID using round-robin with proper overflow handling.
    /// </summary>
    private int GetNextWorkerId()
    {
        uint nextId;
        uint currentId;
        
        // Use unsigned arithmetic to handle overflow gracefully
        do
        {
            currentId = (uint)_nextWorkerId;
            nextId = currentId + 1;
            
            // Reset to 0 if we've exceeded a reasonable limit (prevent int overflow issues)
            if (nextId > 1_000_000_000)
                nextId = 0;
                
        } while (Interlocked.CompareExchange(ref _nextWorkerId, (int)nextId, (int)currentId) != (int)currentId);
        
        return (int)(nextId % (uint)_maxConcurrency);
    }

    private void TryStartWorker(bool allowOversubscriptionForContinuations = false)
    {
        if (_shutdownCts.IsCancellationRequested || _disposed) return;

        // CAS loop to avoid oversubscription under bursts
        int newWorkerCount;
        while (true)
        {
            var current = _activeWorkers;
            
            // For continuations, allow temporary oversubscription to prevent deadlocks
            // when all workers are busy with tasks that have continuations
            var maxAllowed = allowOversubscriptionForContinuations 
                ? _maxConcurrency + (_maxConcurrency / 2) // Allow 50% oversubscription for continuations
                : _maxConcurrency;
            
            if (current >= maxAllowed) return;
            
            newWorkerCount = current + 1;
            if (Interlocked.CompareExchange(ref _activeWorkers, newWorkerCount, current) == current)
                break;
            
            _notifyDebounce.NotifySafely(_activeWorkers);
        }

        var workerId = (newWorkerCount - 1) % _maxConcurrency;
        
        var threadName = $"TickerQ.Worker-{workerId}";
            
        var thread = new Thread(() => WorkerLoop(workerId)) { IsBackground = true, Name = threadName };
        thread.Start();
    }

    private void WorkerLoop(int workerId)
    {
        try
        {
            WorkerLoopAsync(workerId).GetAwaiter().GetResult();
        }
        catch
        {
            // Worker thread will exit, exception is logged by instrumentation if available
            // Silently exit to prevent console spam in production
        }
    }

    private async Task WorkerLoopAsync(int workerId)
    {
        // Mark this thread as a TickerQ worker thread
        IsTickerQWorkerThread = true;

        // Set custom SynchronizationContext to keep all async continuations on TickerQ threads
        // Continuations can now run on any available TickerQ worker for better performance
        var originalContext = SynchronizationContext.Current;
        var tickerQContext = new TickerQSynchronizationContext(this);
        SynchronizationContext.SetSynchronizationContext(tickerQContext);

        try
        {
            var lastWorkTime = DateTime.UtcNow;
            var consecutiveNoWorkCount = 0;

            while (true)
            {
                var work = TryGetWork(workerId);

                if (work != null)
                {
                    // Update work time when we actually execute work
                    lastWorkTime = DateTime.UtcNow;
                    consecutiveNoWorkCount = 0;

                    try
                    {
                        // Check for cancellation before executing task
                        _shutdownCts.Token.ThrowIfCancellationRequested();

                        await work(); // No ConfigureAwait(false) - stay on TickerQ thread

                        // Check for disposal after task execution
                        if (_disposed)
                            break;
                    }
                    catch (OperationCanceledException)
                    {
                        // Task was cancelled, exit worker
                        break;
                    }
                    catch
                    {
                        // swallow other exceptions by design
                    }
                }
                else
                {
                    // No work available - check if should exit due to idle timeout
                    // This allows elastic scaling: workers exit when idle to free resources
                    var idleTime = DateTime.UtcNow - lastWorkTime;
                    if (idleTime > _idleWorkerTimeout)
                    {
                        break; // Exit worker due to idle timeout - elastic scaling
                    }

                    // Adaptive delay strategy to balance responsiveness vs CPU usage
                    consecutiveNoWorkCount++;
                    
                    if (consecutiveNoWorkCount < 10)
                    {
                        // First 10 cycles: yield to scheduler only
                        Thread.Yield(); // Yield CPU to other threads without blocking
                    }
                    else if (consecutiveNoWorkCount < 100)
                    {
                        // Next 90 cycles: minimal sleep
                        Thread.Sleep(0); // Sleep(0) yields to equal/higher priority threads
                    }
                    else if (consecutiveNoWorkCount < 1000)
                    {
                        // Next 900 cycles: 1ms delay
                        Thread.Sleep(1); // 1ms delay
                    }
                    else if (consecutiveNoWorkCount < 10000)
                    {
                        // Next 9000 cycles: 10ms delay
                        Thread.Sleep(10); // 10ms delay
                    }
                    else
                    {
                        // After 10000 cycles: longer delay to reduce CPU significantly
                        Thread.Sleep(50); // 50ms delay - worker will likely exit soon anyway
                    }
                }

                // Exit if frozen, shutdown, or disposed
                if (_isFrozen || _shutdownCts.IsCancellationRequested || _disposed)
                    break;
            }
        }
        finally
        {
            // Restore original SynchronizationContext
            SynchronizationContext.SetSynchronizationContext(originalContext);
            
            // Clean up thread-local storage to prevent memory leaks in long-running apps
            _tempTasks = null;
            IsTickerQWorkerThread = false;
            _cachedNow = default;
            _lastCacheUpdateTicks = 0;
            
            Interlocked.Decrement(ref _activeWorkers);
            _notifyDebounce.NotifySafely(_activeWorkers);
        }
    }

    private Func<Task> TryGetWork(int workerId)
    {
        // Try own queue first (local work) - get the highest priority available
        var ownQueue = _workerQueues[workerId];
        if (ownQueue != null && TryDequeueByPriority(ownQueue, out var work))
            return work;

        // No local work - try stealing from other workers
        // Use two-phase approach: first look for overloaded queues, then do random stealing
        
        // Phase 1: Look for heavily loaded queues (more than 2x average load)
        var averageLoad = Math.Max(1, _totalQueuedTasks / _maxConcurrency);
        var overloadThreshold = averageLoad * 2;
        
        for (int i = 0; i < _maxConcurrency; i++)
        {
            if (i == workerId || _workerQueues[i] == null) continue;
            
            // Check if this queue is overloaded
            if (_workerQueues[i].Count > overloadThreshold)
            {
                if (TryDequeueByPriority(_workerQueues[i], out work))
                    return work;
            }
        }
        
        // Phase 2: Random stealing for better load distribution
        var startWorker = (uint)Interlocked.Increment(ref _randomSeed) % (uint)_maxConcurrency;
        for (uint offset = 1; offset < _maxConcurrency; offset++)
        {
            var i = (int)((startWorker + offset) % (uint)_maxConcurrency);
            if (i == workerId || _workerQueues[i] == null) continue;
            
            if (TryDequeueByPriority(_workerQueues[i], out work))
                return work;
        }

        return null; // No work available anywhere
    }

    private bool TryDequeueByPriority(ConcurrentQueue<PriorityTask> queue, out Func<Task> work)
    {
        work = null;
        
        // Initialize thread-local temp array if needed (increased size for better coverage)
        const int maxExamineCount = 128; // Examine more tasks for better priority handling
        
        // IMPORTANT: Check if we need to resize or recreate the array
        // This prevents unbounded growth if someone changes maxExamineCount
        if (_tempTasks == null || _tempTasks.Length != maxExamineCount)
        {
            _tempTasks = new PriorityTask[maxExamineCount];
        }
        
        // Look for highest priority task in queue
        PriorityTask? bestTask = null;
        int tempCount = 0;
        int examinedCount = 0;
        
        // First pass: find if there are any high priority tasks
        // This prevents examining too many tasks if we have urgent work
        var queueSnapshot = queue.Count;
        var maxToExamine = Math.Min(queueSnapshot, maxExamineCount);
        
        // Use a List to handle overflow cases without losing tasks
        List<PriorityTask> overflowTasks = null;
        
        // Dequeue tasks to find the highest priority (considering age)
        while (queue.TryDequeue(out var task) && examinedCount < maxToExamine)
        {
            examinedCount++;
            var effectivePriority = GetEffectivePriority(task);
            
            // Early exit optimization: If we find a High priority task, use it immediately
            if (effectivePriority == TickerTaskPriority.High)
            {
                // Re-enqueue any tasks we've already dequeued
                if (tempCount > 0)
                {
                    var earlyExitTasks = _tempTasks.AsSpan(0, tempCount);
                    foreach (var tempTask in earlyExitTasks)
                    {
                        queue.Enqueue(tempTask);
                    }
                }
                if (bestTask.HasValue)
                {
                    queue.Enqueue(bestTask.Value);
                }
                // Re-enqueue overflow tasks if any
                if (overflowTasks != null)
                {
                    foreach (var overflowTask in overflowTasks)
                    {
                        queue.Enqueue(overflowTask);
                    }
                }
                
                bestTask = task;
                break;
            }
            
            // Compare priorities and keep the best one
            if (!bestTask.HasValue)
            {
                bestTask = task;
            }
            else
            {
                var bestEffectivePriority = GetEffectivePriority(bestTask.Value);
                
                if (effectivePriority < bestEffectivePriority || 
                    (effectivePriority == bestEffectivePriority && task.QueueTime < bestTask.Value.QueueTime))
                {
                    // New task is better - store old best task
                    if (tempCount < maxExamineCount)
                    {
                        _tempTasks[tempCount++] = bestTask.Value;
                    }
                    else
                    {
                        // Buffer full - store in overflow list instead of immediate re-queue
                        overflowTasks ??= new List<PriorityTask>();
                        overflowTasks.Add(bestTask.Value);
                    }
                    bestTask = task;
                }
                else
                {
                    // Current best is still better
                    if (tempCount < maxExamineCount)
                    {
                        _tempTasks[tempCount++] = task;
                    }
                    else
                    {
                        // Buffer full - store in overflow list instead of immediate re-queue
                        overflowTasks ??= new List<PriorityTask>();
                        overflowTasks.Add(task);
                    }
                }
            }
        }
        
        // Re-enqueue ALL tasks we didn't take (including overflow)
        if (tempCount > 0)
        {
            var tasksToRequeue = _tempTasks.AsSpan(0, tempCount);
            foreach (var task in tasksToRequeue)
            {
                queue.Enqueue(task);
            }
        }
        
        // Re-enqueue overflow tasks
        if (overflowTasks != null)
        {
            foreach (var task in overflowTasks)
            {
                queue.Enqueue(task);
            }
        }
        
        if (bestTask.HasValue)
        {
            work = async () =>
            {
                try
                {
                    await bestTask.Value.Work(bestTask.Value.UserToken); // No ConfigureAwait(false) - stay on TickerQ thread
                }
                catch
                {
                    /* swallow */
                }
                finally
                {
                    if (bestTask.Value.ShouldDecrementTotal)
                    {
                        Interlocked.Decrement(ref _totalQueuedTasks);
                    }
                }
            };
            return true;
        }
        
        return false;
    }

    // Thread-local cached time to avoid repeated DateTime.UtcNow calls
    [ThreadStatic] private static DateTime _cachedNow;
    [ThreadStatic] private static long _lastCacheUpdateTicks;
    
    private TickerTaskPriority GetEffectivePriority(PriorityTask task)
    {
        // Cache DateTime.UtcNow per batch to avoid repeated system calls
        // Use Environment.TickCount64 which is monotonic and doesn't wrap in practice
        var currentTicks = Environment.TickCount64;
        
        // Refresh cache every 10ms or if we detect time going backwards (shouldn't happen with TickCount64)
        var ticksDiff = currentTicks - _lastCacheUpdateTicks;
        if (ticksDiff > 10 || ticksDiff < 0)
        {
            _cachedNow = DateTime.UtcNow;
            _lastCacheUpdateTicks = currentTicks;
        }
        
        var age = _cachedNow - task.QueueTime;
        var priority = task.Priority;
        
        // Age-based priority promotion with starvation prevention
        // Tasks get promoted based on how long they've been waiting
        if (priority == TickerTaskPriority.Low)
        {
            if (age.TotalMinutes > 5)
                return TickerTaskPriority.High; // Very old low priority tasks become high
            if (age.TotalMinutes > 2)
                return TickerTaskPriority.Normal; // Old low priority tasks become normal
        }
        else if (priority == TickerTaskPriority.Normal)
        {
            if (age.TotalMinutes > 10)
                return TickerTaskPriority.High; // Very old normal tasks become high
        }
        
        return priority;
    }

    /// <summary>
    /// Queues an async continuation to any available TickerQ worker thread.
    /// Uses round-robin distribution for optimal load balancing and performance.
    /// </summary>
    internal void QueueContinuation(Action continuation)
    {
        if (_disposed || _isFrozen) return;
        
        // Create a high-priority task for the continuation
        var continuationTask = new PriorityTask(
            TickerTaskPriority.High, // Continuations get highest priority to maintain execution flow
            ct =>
            {
                try
                {
                    continuation();
                }
                catch
                {
                    /* swallow continuation exceptions */
                }
                return Task.CompletedTask; // Direct return - no async overhead
            },
            CancellationToken.None, // Continuations don't need user cancellation
            shouldDecrementTotal: false // Continuations should not decrement the total count
        );

        // Use round-robin to distribute continuations across all workers for better performance
        var workerId = GetNextWorkerId();
        var workerQueue = GetOrCreateWorkerQueue(workerId);
        workerQueue.Enqueue(continuationTask);
        
        // CRITICAL: For continuations, we may need to temporarily exceed maxConcurrency
        // to prevent deadlocks when all workers are busy with tasks that have continuations
        TryStartWorker(allowOversubscriptionForContinuations: true);
        
        // Don't increment _totalQueuedTasks for continuations as they're internal overhead
        // and shouldn't count against user's capacity limits
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        // Cancel the shutdown token first to signal workers to exit
        await _shutdownCts.CancelAsync();

        // Wait for workers to finish (they should exit quickly due to _disposed check)
        var startTime = DateTime.UtcNow;
        while (_activeWorkers > 0)
        {
            await Task.Delay(10);

            // Safety timeout - if workers don't exit after 5 seconds, force continue
            if ((DateTime.UtcNow - startTime).TotalSeconds > 5)
            {
                break;
            }
        }

        // Clean up resources
        _notifyDebounce.Dispose();
        _shutdownCts.Dispose();
    }

    /// <summary>
    /// Freezes the scheduler - prevents new tasks from being queued.
    /// Currently running tasks will continue to completion.
    /// Workers will exit after completing current tasks.
    /// </summary>
    public void Freeze()
    {
        _isFrozen = true;
    }

    /// <summary>
    /// Resumes the scheduler - allows new tasks to be queued again.
    /// Workers will be spawned as needed when new tasks arrive.
    /// </summary>
    public void Resume()
    {
        _isFrozen = false;
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
    /// Gets the current total number of queued tasks (excluding continuations).
    /// </summary>
    public int TotalQueuedTasks => _totalQueuedTasks;

    /// <summary>
    /// Gets whether the scheduler has been disposed.
    /// </summary>
    public bool IsDisposed => _disposed;

    /// <summary>
    /// Waits for all currently running tasks to complete.
    /// Does not prevent new tasks from being queued unless scheduler is frozen.
    /// </summary>
    /// <param name="timeout">Maximum time to wait for tasks to complete</param>
    /// <returns>True if all tasks completed within timeout, false if timeout occurred</returns>
    public async Task<bool> WaitForRunningTasksAsync(TimeSpan? timeout = null)
    {
        var deadline = timeout.HasValue ? DateTime.UtcNow.Add(timeout.Value) : DateTime.MaxValue;
        
        while (_activeWorkers > 0)
        {
            if (DateTime.UtcNow > deadline)
                return false; // Timeout

            await Task.Delay(10);
        }

        return true; // All tasks completed
    }
}