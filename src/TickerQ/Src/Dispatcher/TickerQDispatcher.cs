using System;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.BackgroundServices;
using TickerQ.TickerQThreadPool;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Licensing;
using TickerQ.Utilities.Models;

namespace TickerQ.Dispatcher
{
    internal class TickerQDispatcher : ITickerQDispatcher
    {
        private readonly ITickerQTaskScheduler _taskScheduler;
        private readonly ITickerExecutionTaskHandler _taskHandler;
        private readonly TickerQLicenseStateProvider _licenseState;
        private readonly AcquiredExecutionQueue _acquiredQueue;

        // Direct dispatch is only enabled while the offline license permits execution.
        public bool IsEnabled => _licenseState.ExecutionAllowed && !_taskScheduler.IsFrozen && !_taskScheduler.IsDisposed;

        public TickerQDispatcher(ITickerQTaskScheduler taskScheduler, ITickerExecutionTaskHandler taskHandler, TickerQLicenseStateProvider licenseState)
            : this(taskScheduler, taskHandler, licenseState, null)
        {
        }

        public TickerQDispatcher(ITickerQTaskScheduler taskScheduler, ITickerExecutionTaskHandler taskHandler,
            TickerQLicenseStateProvider licenseState, AcquiredExecutionQueue acquiredQueue)
        {
            _taskScheduler = taskScheduler ?? throw new ArgumentNullException(nameof(taskScheduler));
            _taskHandler = taskHandler ?? throw new ArgumentNullException(nameof(taskHandler));
            _licenseState = licenseState ?? throw new ArgumentNullException(nameof(licenseState));
            _acquiredQueue = acquiredQueue;
        }

        public async Task DispatchAsync(InternalFunctionContext[] contexts, CancellationToken cancellationToken = default)
        {
            if (contexts == null || contexts.Length == 0)
                return;

            if (_acquiredQueue != null)
            {
                // Manual/immediate jobs need the same lease renewal and shutdown cleanup
                // as scheduled work, including while queue admission waits for capacity.
                if (!await _acquiredQueue.PublishAsync(contexts, false, cancellationToken).ConfigureAwait(false))
                    throw new InvalidOperationException(_licenseState.ExecutionAllowed
                        ? "The immediate execution could not be queued. Check the scheduler status and logs."
                        : _licenseState.Current.Message);
                return;
            }

            // Fail closed before any queue or semaphore work: the offline license must permit execution.
            var license = _licenseState.Current;
            if (!license.ExecutionAllowed)
                throw new InvalidOperationException(license.Message);

            foreach (var context in contexts)
            {
                if (_taskScheduler is ITickerQExecutionScheduler executionScheduler)
                {
                    // A manager call made inside a function must return after admission, not
                    // await another invocation that could need the caller's function permit.
                    if (!executionScheduler.TryQueueOrchestration(
                            ct => _taskHandler.ExecuteTaskAsync(context, false, ct), cancellationToken))
                        throw new InvalidOperationException("The scheduler cannot admit more work right now.");
                    continue;
                }

                await _taskScheduler.QueueAsync(
                    ct => _taskHandler.ExecuteTaskAsync(context, false, ct),
                    context.CachedPriority,
                    cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
