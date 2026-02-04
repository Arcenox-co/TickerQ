using System;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ.Dispatcher
{
    internal class TickerQDispatcher : ITickerQDispatcher
    {
        private readonly ITickerQTaskScheduler _taskScheduler;
        private readonly ITickerExecutionTaskHandler _taskHandler;

        public bool IsEnabled => true;

        public TickerQDispatcher(ITickerQTaskScheduler taskScheduler, ITickerExecutionTaskHandler taskHandler)
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
