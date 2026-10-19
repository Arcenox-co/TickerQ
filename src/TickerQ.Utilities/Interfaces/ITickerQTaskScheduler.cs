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

        /// <summary>Live logical consumers, including idle consumers; not a native-thread count.</summary>
        int ActiveWorkers { get; }

        /// <summary>
        /// Number of executing attempts, including asynchronous work awaiting I/O.
        /// Default interface member so pre-existing third-party implementers keep compiling
        /// and running; the built-in scheduler overrides it with the real value.
        /// </summary>
        int ActiveExecutionCount => 0;

        /// <summary>Accepted job workflows, including chains, function waits, and retries.</summary>
        int ActiveOrchestrationCount => 0;

        /// <summary>Job attempts waiting for their per-function concurrency permit.</summary>
        int WaitingForFunctionCount => 0;

        /// <summary>Jobs currently waiting between retry attempts.</summary>
        int RetryingCount => 0;

        int TotalQueuedTasks { get; }

        bool IsDisposed { get; }

        string GetDiagnostics();

        /// <summary>
        /// Waits until queued work, executing attempts, and accepted workflows finish,
        /// bounded by <paramref name="timeout"/>.
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
