using System;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Enums;

namespace TickerQ.Utilities.Interfaces
{
    /// <summary>
    /// Abstraction for the internal TickerQ task scheduler.
    /// </summary>
    public interface ITickerQTaskScheduler
    {
        ValueTask QueueAsync(
            Func<CancellationToken, Task> work,
            TickerTaskPriority priority,
            CancellationToken cancellationToken = default);

        void Freeze();

        void Resume();

        bool IsFrozen { get; }

        int ActiveWorkers { get; }

        /// <summary>
        /// Number of in-flight async executions, distinct from worker-thread count.
        /// Default interface member so pre-existing third-party implementers keep compiling
        /// and running; the built-in scheduler overrides it with the real value.
        /// </summary>
        int ActiveExecutionCount => 0;

        int TotalQueuedTasks { get; }

        bool IsDisposed { get; }

        string GetDiagnostics();

        /// <summary>
        /// Waits until there is no queued and no in-flight work, bounded by <paramref name="timeout"/>.
        /// Original overload, preserved for source/binary compatibility.
        /// </summary>
        Task<bool> WaitForRunningTasksAsync(TimeSpan? timeout = null);

        /// <summary>
        /// Cancellation-aware wait. Default interface implementation forwards to the
        /// timeout-only overload (ignoring the token), so third-party implementers are not
        /// forced to add it; implementations that support cooperative cancellation override this.
        /// </summary>
        Task<bool> WaitForRunningTasksAsync(TimeSpan? timeout, CancellationToken cancellationToken)
            => WaitForRunningTasksAsync(timeout);
    }
}
