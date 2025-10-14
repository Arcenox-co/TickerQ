using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Enums;
using System.Runtime.CompilerServices;

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
            throw new ArgumentOutOfRangeException(nameof(maxConcurrency));
        if (maxConcurrency > 64)
            throw new ArgumentOutOfRangeException(nameof(maxConcurrency), "Maximum 64 workers supported due to bitfield limitations");

        _maxConcurrency = maxConcurrency;
        // Default to 60 seconds idle timeout for elastic worker management
        // Workers will exit after 60s of inactivity to free resources
        _idleWorkerTimeout = idleWorkerTimeout ?? TimeSpan.FromSeconds(60);
        _maxCapacityPerWorker = 1024; // Fixed optimal capacity per worker
        _maxTotalCapacity = maxConcurrency * 1024; // Auto-calculated: queues × 1024
        _notifyDebounce =  notifyDebounce ?? new SoftSchedulerNotifyDebounce(_ => { }); // Default no-op notifier
        // Lazy initialization - only create queues when needed
        _workerQueues = new ConcurrentQueue<PriorityTask>[maxConcurrency];
        _workerInitialized = 0; // Bitfield initialized to 0
    }


    public async ValueTask QueueAsync(Func<CancellationToken, Task> work, TickerTaskPriority priority, CancellationToken userCancellationToken = default)
    {
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

        // Get worker using round-robin with overflow protection
        var nextId = Interlocked.Increment(ref _nextWorkerId);
        // Prevent overflow by resetting when approaching int.MaxValue
        if (nextId > 1_000_000_000)
        {
            Interlocked.CompareExchange(ref _nextWorkerId, 0, nextId);
        }
        var workerId = Math.Abs(nextId) % _maxConcurrency;
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

        while (queueCount >= _maxCapacityPerWorker || _totalQueuedTasks >= _maxTotalCapacity)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // CRITICAL: Always add a delay to prevent busy-wait CPU spike
            // This is a backpressure mechanism - if we're at capacity, slow down
            await Task.Delay(5, cancellationToken); // 5ms delay when at capacity

            // Re-check queue count after delay
            queueCount = targetQueue.Count;
        }
    }

    private ConcurrentQueue<PriorityTask> GetOrCreateWorkerQueue(int workerId)
    {
        if (workerId >= 64)
            throw new ArgumentOutOfRangeException(nameof(workerId), "Maximum 64 workers supported");
            
        ulong workerBit = 1UL << workerId;
        
        // Optimization: Cache first read to avoid duplicate atomic operations
        var initialized = Interlocked.Read(ref _workerInitialized);
        if ((initialized & workerBit) == 0)
        {
            lock (_workerQueues)
            {
                // Re-check with fresh read inside lock
                initialized = Interlocked.Read(ref _workerInitialized);
                if ((initialized & workerBit) == 0)
                {
                    _workerQueues[workerId] = new ConcurrentQueue<PriorityTask>();
                    
                    // Use Interlocked to set the bit atomically
                    ulong currentValue, newValue;
                    do
                    {
                        currentValue = initialized;
                        newValue = currentValue | workerBit;
                        initialized = Interlocked.CompareExchange(ref _workerInitialized, newValue, currentValue);
                    } while (initialized != currentValue);
                }
            }
        }
        return _workerQueues[workerId];
    }

    private void TryStartWorker(bool allowOversubscriptionForContinuations = false)
    {
        if (_shutdownCts.IsCancellationRequested || _disposed) return;

        // CAS loop to avoid oversubscription under bursts
        int newWorkerCount;
        while (true)
        {
            var current = _activeWorkers;
            
            // For continuations, allow temporary +1 oversubscription to prevent deadlocks
            var maxAllowed = allowOversubscriptionForContinuations ? _maxConcurrency + 1 : _maxConcurrency;
            if (current >= maxAllowed) return;
            
            newWorkerCount = current + 1;
            if (Interlocked.CompareExchange(ref _activeWorkers, newWorkerCount, current) == current)
                break;
            
            _notifyDebounce.NotifySafely(_activeWorkers);
        }

        // For continuation workers, use existing worker IDs to reuse queues
        // This prevents IndexOutOfRangeException and enables proper work stealing
        var workerId = (newWorkerCount - 1) % _maxConcurrency;
        
        var threadName = allowOversubscriptionForContinuations ? 
            $"TickerQ.ContinuationWorker-{workerId}" : 
            $"TickerQ.Worker-{workerId}";
            
        var thread = new Thread(() => WorkerLoop(workerId)) { IsBackground = true, Name = threadName };
        thread.Start();
    }

    private void WorkerLoop(int workerId)
    {
        try
        {
            WorkerLoopAsync(workerId).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Worker {workerId} crashed: {ex}");
            // Worker thread will exit, but we log the error
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
                        Console.WriteLine($"[DEBUG] Worker {workerId} exiting due to idle timeout ({idleTime.TotalSeconds:F1}s > {_idleWorkerTimeout.TotalSeconds:F1}s)");
                        break; // Exit worker due to idle timeout - elastic scaling
                    }

                    // Adaptive delay strategy to balance responsiveness vs CPU usage
                    consecutiveNoWorkCount++;
                    
                    if (consecutiveNoWorkCount < 100)
                    {
                        // First 100 cycles: tight loop for immediate responsiveness
                        // No delay - just continue loop
                    }
                    else if (consecutiveNoWorkCount < 1000)
                    {
                        // Next 900 cycles: start adding minimal delay
                        Thread.Sleep(1); // 1ms delay
                    }
                    else
                    {
                        // After 1000 cycles: longer delay to reduce CPU
                        Thread.Sleep(5); // 5ms delay - worker will likely exit soon anyway
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

        // No local work - try stealing from other workers with randomized order for fairness
        var startWorker = Interlocked.Increment(ref _randomSeed) % _maxConcurrency;
        for (int offset = 1; offset < _maxConcurrency; offset++)
        {
            var i = (startWorker + offset) % _maxConcurrency;
            if (i == workerId || _workerQueues[i] == null) continue;
            
            if (TryDequeueByPriority(_workerQueues[i], out work))
                return work;
        }

        return null; // No work available anywhere
    }

    private bool TryDequeueByPriority(ConcurrentQueue<PriorityTask> queue, out Func<Task> work)
    {
        work = null;
        
        // Initialize thread-local temp array if needed (reuse existing if possible)
        _tempTasks ??= new PriorityTask[32];
        
        // Look for highest priority task in queue using pooled array
        PriorityTask? bestTask = null;
        int tempCount = 0;
        
        // Dequeue tasks to find the highest priority (considering age)
        while (queue.TryDequeue(out var task) && tempCount < 32)
        {
            var effectivePriority = GetEffectivePriority(task);
            
            // Early exit optimization: If we find a High priority task, use it immediately
            if (effectivePriority == TickerTaskPriority.High)
            {
                // Re-enqueue any tasks we've already dequeued - use Span for better performance
                var earlyExitTasks = _tempTasks.AsSpan(0, tempCount);
                foreach (var tempTask in earlyExitTasks)
                {
                    queue.Enqueue(tempTask);
                }
                if (bestTask.HasValue)
                {
                    queue.Enqueue(bestTask.Value);
                }
                
                bestTask = task;
                break;
            }
            
            var bestEffectivePriority = bestTask.HasValue ? GetEffectivePriority(bestTask.Value) : TickerTaskPriority.Low;
            
            if (bestTask == null || effectivePriority < bestEffectivePriority)
            {
                if (bestTask.HasValue)
                    _tempTasks[tempCount++] = bestTask.Value;
                bestTask = task;
            }
            else
            {
                _tempTasks[tempCount++] = task;
            }
        }
        
        // Re-enqueue the tasks we didn't take - use Span for better performance
        var tasksToRequeue = _tempTasks.AsSpan(0, tempCount);
        foreach (var task in tasksToRequeue)
        {
            queue.Enqueue(task);
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
    [ThreadStatic] private static long _lastCacheTicks;
    
    private TickerTaskPriority GetEffectivePriority(PriorityTask task)
    {
        // Cache DateTime.UtcNow per batch to avoid repeated system calls
        var nowTicks = Environment.TickCount64;
        if (nowTicks - _lastCacheTicks > 10) // Refresh cache every 10ms
        {
            _cachedNow = DateTime.UtcNow;
            _lastCacheTicks = nowTicks;
        }
        
        var age = _cachedNow - task.QueueTime;
        var priority = task.Priority;
        
        // Age-based priority promotion
        if (age.TotalMinutes > 2 && priority == TickerTaskPriority.Low)
            return TickerTaskPriority.Normal;
        if (age.TotalMinutes > 5 && priority == TickerTaskPriority.Normal)
            return TickerTaskPriority.High;
            
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
        var nextId = Interlocked.Increment(ref _nextWorkerId);
        if (nextId > 1_000_000_000)
        {
            Interlocked.CompareExchange(ref _nextWorkerId, 0, nextId);
        }
        var workerId = Math.Abs(nextId) % _maxConcurrency;
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

        Console.WriteLine($"[DEBUG] Disposing scheduler, signaling {_activeWorkers} workers to exit");

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
                Console.WriteLine($"[WARNING] Workers not exiting cleanly after 5 seconds, forcing disposal");
                break;
            }
        }

        Console.WriteLine($"[DEBUG] All workers exited, cleaning up resources");

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