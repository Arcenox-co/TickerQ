using System;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.TickerQThreadPool;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ.Dispatcher
{
    internal class TickerQDispatcher : ITickerQDispatcher
    {
        private readonly TickerQTaskScheduler _taskScheduler;
        private readonly TickerExecutionTaskHandler _taskHandler;

        public TickerQDispatcher(TickerQTaskScheduler taskScheduler, TickerExecutionTaskHandler taskHandler)
        {
            _taskScheduler = taskScheduler ?? throw new ArgumentNullException(nameof(taskScheduler));
            _taskHandler = taskHandler ?? throw new ArgumentNullException(nameof(taskHandler));
        }

        public async Task DispatchAsync(InternalFunctionContext[] contexts, CancellationToken cancellationToken = default)
        {
            if (contexts == null || contexts.Length == 0)
                return;

            foreach (var context in contexts)
            {
                await _taskScheduler.QueueAsync(
                    async ct => await _taskHandler.ExecuteTaskAsync(context, false, ct).ConfigureAwait(false),
                    context.CachedPriority,
                    cancellationToken).ConfigureAwait(false);
            }
        }
    }
}

