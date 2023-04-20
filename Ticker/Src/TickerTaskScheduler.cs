using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Enums;

internal sealed class TickerTaskScheduler : TaskScheduler, IDisposable
{
    /// <summary>Cancellation token used for disposal.</summary>
    private readonly CancellationTokenSource _disposeCancellation = new CancellationTokenSource();

    /// <summary>
    /// The maximum allowed concurrency level of this scheduler.  If custom threads are
    /// used, this represents the number of created threads.
    /// </summary>
    private readonly int _concurrencyLevel;

    /// <summary>Whether we're processing tasks on the current thread.</summary>
    private static readonly ThreadLocal<bool> _taskProcessingThread = new ThreadLocal<bool>();

    /// <summary>The threads used by the scheduler to process work.</summary>
    private readonly Thread[] _threads;

    /// <summary>The collection of tasks to be executed on our custom threads.</summary>
    private readonly BlockingCollection<Task> _blockingTaskQueue;

    private readonly ConcurrentDictionary<int, TaskWithPriority> _taskDict = new ConcurrentDictionary<int, TaskWithPriority>();

    /// <summary>Initializes the scheduler.</summary>
    /// <param name="threadCount">The number of threads to create and use for processing work items.</param>

    /// <summary>Initializes the scheduler.</summary>
    /// <param name="threadCount">The number of threads to create and use for processing work items.</param>
    /// <param name="threadName">The name to use for each of the created threads.</param>
    /// <param name="useForegroundThreads">A Boolean value that indicates whether to use foreground threads instead of background.</param>
    /// <param name="threadPriority">The priority to assign to each thread.</param>
    public TickerTaskScheduler(
        int threadCount)
    {
        // Validates arguments (some validation is left up to the Thread type itself).
        // If the thread count is 0, default to the number of logical processors.
        if (threadCount < 0)
        {
            throw new Exception("Cannot run scheduler with 0 threads!");
        }
        else if (threadCount == 0)
        {
            _concurrencyLevel = Environment.ProcessorCount;
        }
        else
        {
            _concurrencyLevel = threadCount;
        }

        // Initialize the queue used for storing tasks
        _blockingTaskQueue = new BlockingCollection<Task>();

        // Create all of the threads
        _threads = new Thread[_concurrencyLevel];

        for (int i = 0; i < _concurrencyLevel; i++)
        {
            _threads[i] = new Thread(() => ThreadBasedDispatchLoop())
            {
                IsBackground = true,
                Name = $"Ticker thread ({i})"
            };
        }

        // Start all of the threads
        foreach (var thread in _threads)
        {
            thread.Start();
        }
    }

    /// <summary>The dispatch loop run by all threads in this scheduler.</summary>
    private void ThreadBasedDispatchLoop()
    {
        _taskProcessingThread.Value = true;

        try
        {
            // If a thread abort occurs, we'll try to reset it and continue running.
            while (true)
            {
                try
                {
                    // For each task queued to the scheduler, try to execute it.
                    foreach (var task in _blockingTaskQueue.GetConsumingEnumerable(_disposeCancellation.Token))
                    {
                        TryExecuteTask(task);
                    }
                }
                catch (ThreadAbortException)
                {
                    // If we received a thread abort, and that thread abort was due to shutting down
                    // or unloading, let it pass through.  Otherwise, reset the abort so we can
                    // continue processing work items.
                    if (!Environment.HasShutdownStarted && !AppDomain.CurrentDomain.IsFinalizingForUnload())
                    {
                        Thread.ResetAbort();
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // If the scheduler is disposed, the cancellation token will be set and
            // we'll receive an OperationCanceledException.  That OCE should not crash the process.
        }
        finally
        {
            _taskProcessingThread.Value = false;
        }
    }

    /// <summary>Queues a task to the scheduler.</summary>
    /// <param name="task">The task to be queued.</param>
    protected override void QueueTask(Task task)
    {
        // If we've been disposed, no one should be queueing
        if (_disposeCancellation.IsCancellationRequested)
        {
            throw new Exception("");
        }

        if (task.CreationOptions == TaskCreationOptions.HideScheduler)
            _taskDict.TryAdd(task.Id, new TaskWithPriority(task, TickerTaskPriority.Normal));
        else
            _blockingTaskQueue.Add(task);
    }

    public void SetQueuedTaskPriority(int taskId, TickerTaskPriority tickerTaskPriority)
    {
        _taskDict.TryGetValue(taskId, out var priority);

        _taskDict[taskId] = new TaskWithPriority(priority.Task, tickerTaskPriority);
    }

    public void RunQueuedTaskWithPriority()
    {
        var tasks = _taskDict
            .Select(x => x.Value)
            .OrderBy(x => x.Priority)
            .Select(x => x.Task).ToArray();

        foreach (var task in tasks)
        {
            _blockingTaskQueue.Add(task);
        }

        _taskDict.Clear();
    }

    /// <summary>Tries to execute a task synchronously on the current thread.</summary>
    /// <param name="task">The task to execute.</param>
    /// <param name="taskWasPreviouslyQueued">Whether the task was previously queued.</param>
    /// <returns>true if the task was executed; otherwise, false.</returns>
    protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued)
    {
        // If we're already running tasks on this threads, enable inlining
        return _taskProcessingThread.Value && TryExecuteTask(task);
    }

    /// <summary>Gets the tasks scheduled to this scheduler.</summary>
    /// <returns>An enumerable of all tasks queued to this scheduler.</returns>
    /// <remarks>This does not include the tasks on sub-schedulers.  Those will be retrieved by the debugger separately.</remarks>
    protected override IEnumerable<Task> GetScheduledTasks()
    {
        // Get the tasks from the blocking queue.
        return _blockingTaskQueue.ToList();
    }

    /// <summary>Gets the maximum concurrency level to use when processing tasks.</summary>
    public override int MaximumConcurrencyLevel
    {
        get { return _concurrencyLevel; }
    }

    /// <summary>Initiates shutdown of the scheduler.</summary>
    public void Dispose()
    {
        _disposeCancellation.Cancel();
    }

    private readonly struct TaskWithPriority
    {
        public readonly TickerTaskPriority Priority;
        public readonly Task Task;

        public TaskWithPriority(Task task, TickerTaskPriority priority)
        {
            Task = task;
            Priority = priority;
        }
    }
}