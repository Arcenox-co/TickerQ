using System;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Licensing;
using TickerQ.Utilities.Models;

namespace TickerQ.Dispatcher
{
    internal class TickerQDispatcher : ITickerQDispatcher
    {
        private readonly ITickerQTaskScheduler _taskScheduler;
        private readonly ITickerExecutionTaskHandler _taskHandler;
        private readonly ITickerFunctionConcurrencyGate _concurrencyGate;
        private readonly TickerQLicenseStateProvider _licenseState;

        // Direct dispatch is only enabled while the offline license permits execution.
        public bool IsEnabled => _licenseState.ExecutionAllowed;

        public TickerQDispatcher(ITickerQTaskScheduler taskScheduler, ITickerExecutionTaskHandler taskHandler, ITickerFunctionConcurrencyGate concurrencyGate, TickerQLicenseStateProvider licenseState)
        {
            _taskScheduler = taskScheduler ?? throw new ArgumentNullException(nameof(taskScheduler));
            _taskHandler = taskHandler ?? throw new ArgumentNullException(nameof(taskHandler));
            _concurrencyGate = concurrencyGate ?? throw new ArgumentNullException(nameof(concurrencyGate));
            _licenseState = licenseState ?? throw new ArgumentNullException(nameof(licenseState));
        }

        public async Task DispatchAsync(InternalFunctionContext[] contexts, CancellationToken cancellationToken = default)
        {
            if (contexts == null || contexts.Length == 0)
                return;

            // Fail closed before any queue or semaphore work: the offline license must permit execution.
            var license = _licenseState.Current;
            if (!license.ExecutionAllowed)
                throw new InvalidOperationException(license.Message);

            foreach (var context in contexts)
            {
                var semaphore = _concurrencyGate.GetSemaphoreOrNull(context.FunctionName, context.CachedMaxConcurrency);

                await _taskScheduler.QueueAsync(
                    async ct =>
                    {
                        if (semaphore != null)
                            await semaphore.WaitAsync(ct).ConfigureAwait(false);

                        try
                        {
                            await _taskHandler.ExecuteTaskAsync(context, false, ct).ConfigureAwait(false);
                        }
                        finally
                        {
                            semaphore?.Release();
                        }
                    },
                    context.CachedPriority,
                    cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
