using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Base;
using TickerQ.Utilities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;

namespace TickerQ
{
    internal class TickerHost : BaseTicker
    {
        private readonly TickerTaskScheduler _tickerTaskScheduler;
        private SemaphoreSlim _semaphoreSlim = new SemaphoreSlim(1, 1);
        public TickerHost(IServiceProvider serviceProvider, TickerOptionsBuilder tickerOptionsBuilder, TickerCollection tickerCollection, ILogger<TickerHost> logger, IClock clock)
            : base(tickerOptionsBuilder, tickerCollection, serviceProvider, logger, clock)
        {
            _tickerTaskScheduler = new TickerTaskScheduler(5);
        }

        protected override void OnTimerTick((string FunctionName, Guid TickerId, TickerType type)[] functions, CancellationToken cancellationToken = default, bool dueDone = false)
        {
            _semaphoreSlim.Wait();

            foreach (var (functionName, tickerId, type) in functions)
            {
                if (TickerCollection.TickerFunctionsDelegate.TryGetValue(functionName, out var tickerItem))
                {
                    if (tickerItem.Priority == TickerTaskPriority.LongRunning)
                        _ = Task.Factory.StartNew(async () => await tickerItem.Delegate(ServiceProvider, tickerId, type, CancellationToken.None, dueDone), TaskCreationOptions.LongRunning).ConfigureAwait(false);
                    else
                    {
                        var taskDetails = Task.Factory.StartNew(async () => await tickerItem.Delegate(ServiceProvider, tickerId, type, cancellationToken, dueDone), cancellationToken, TaskCreationOptions.HideScheduler, _tickerTaskScheduler);

                        _tickerTaskScheduler.SetQueuedTaskPriority(taskDetails.Id, tickerItem.Priority);
                    }
                }
            }

            _tickerTaskScheduler.RunQueuedTaskWithPriority();

            _semaphoreSlim.Release();
        }
    }
}
