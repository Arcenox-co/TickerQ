using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities;
using TickerQ.Utilities.Enums;

namespace TickerQ
{
    internal sealed class TickerTaskScheduler : TaskScheduler, IDisposable
    {
        /// <summary>Cancellation token used for disposal.</summary>
        private readonly CancellationTokenSource _disposeCancellation = new CancellationTokenSource();

        /// <summary>Whether we're processing tasks on the current thread.</summary>
        private static readonly ThreadLocal<bool> TaskProcessingThread = new ThreadLocal<bool>();

        /// <summary>The collection of tasks to be executed on our custom threads.</summary>
        private readonly BlockingCollection<Task> _blockingTaskQueue;

        private readonly ConcurrentDictionary<int, TaskWithPriority> _taskDict =
            new ConcurrentDictionary<int, TaskWithPriority>();
        
        private const string DefaultThreadNameFormat = "Ticker thread ({0})";

        public TickerTaskScheduler(int threadCount)
        {
            MaximumConcurrencyLevel = threadCount;

            _blockingTaskQueue = new BlockingCollection<Task>();
            
            CreateAndStartThreads(threadCount);
        }

        private void CreateAndStartThreads(int concurrencyLevel)
        {
            var threads = new Thread[concurrencyLevel];
            for (var i = 0; i < concurrencyLevel; i++)
            {
                threads[i] = new Thread(ThreadBasedDispatchLoop)
                {
                    IsBackground = true,
                    Name = string.Format(DefaultThreadNameFormat, i)
                };
            }

            foreach (var thread in threads)
            {
                thread.Start();
            }
        }

        private void ThreadBasedDispatchLoop()
        {
            TaskProcessingThread.Value = true;
            try
            {
                while (true)
                {
                    try
                    {
                        foreach (var task in _blockingTaskQueue.GetConsumingEnumerable(_disposeCancellation.Token))
                        {
                            SoftSchedulerNotifyDebounce.NotifySafely(Interlocked.Increment(ref TickerOptionsBuilder.ActiveThreads));
                            
                            try
                            {
                                TryExecuteTask(task);
                            }
                            finally
                            {
                                SoftSchedulerNotifyDebounce.NotifySafely(Interlocked.Decrement(ref TickerOptionsBuilder.ActiveThreads));
                            }
                        }
                    }
                    catch (ThreadAbortException)
                    {
                        if (!Environment.HasShutdownStarted && !AppDomain.CurrentDomain.IsFinalizingForUnload())
                        {
                            Thread.ResetAbort();
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                TaskProcessingThread.Value = false;
            }
        }

        protected override void QueueTask(Task task)
        {
            if (_disposeCancellation.IsCancellationRequested)
            {
                throw new ObjectDisposedException(nameof(TickerTaskScheduler),
                    "Cannot queue tasks after the scheduler is disposed.");
            }

            if (task.CreationOptions == TaskCreationOptions.HideScheduler)
            {
                _taskDict.TryAdd(task.Id, new TaskWithPriority(task, TickerTaskPriority.Normal));
            }
            else
            {
                _blockingTaskQueue.Add(task);
            }
        }

        public void SetQueuedTaskPriority(int taskId, TickerTaskPriority tickerTaskPriority)
        {
            if (_taskDict.TryGetValue(taskId, out var priority))
            {
                _taskDict[taskId] = new TaskWithPriority(priority.Task, tickerTaskPriority);
            }
        }

        public void ExecutePriorityTasks()
        {
            TaskWithPriority[] tasksSnapshot;
            lock (_taskDict) 
            {
                tasksSnapshot = _taskDict
                    .Select(x => x.Value)
                    .OrderBy(x => x.Priority)
                    .ToArray();
            }

            foreach (var task in tasksSnapshot)
            {
                try
                {
                    _blockingTaskQueue.Add(task.Task);
                }
                catch (InvalidOperationException ex)
                {
                    Console.WriteLine($"Failed to queue task {task.Task.Id}: {ex.Message}");
                }
            }

            _taskDict.Clear();
        }

        protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued)
        {
            return TaskProcessingThread.Value && TryExecuteTask(task);
        }

        protected override IEnumerable<Task> GetScheduledTasks()
        {
            return _blockingTaskQueue.ToList();
        }

        public override int MaximumConcurrencyLevel { get; }

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
}