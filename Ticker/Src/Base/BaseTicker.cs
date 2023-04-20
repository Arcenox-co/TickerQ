using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;

namespace TickerQ.Base
{
    internal abstract class BaseTicker : ITickerHost
    {
        protected readonly TickerCollection TickerCollection;
        protected readonly IServiceProvider ServiceProvider;
        protected readonly ILogger<TickerHost> Logger;
        protected readonly IClock Clock;

        protected TickerOptionsBuilder TickerOptionsBuilder { get; }
        private CancellationTokenSource CtsTickerChecker { get; set; }
        private CancellationTokenSource CtsTickerCheckerDelayAwaiter { get; set; }
        private CancellationTokenSource CtsTickerTimeoutChecker { get; set; }
        public DateTimeOffset? NextPlannedOccurrence { get; private set; }

        protected abstract void OnTimerTick((string FunctionName, Guid TickerId, TickerType type)[] functions, CancellationToken cancellationToken = default, bool dueDone = false);

        protected BaseTicker(TickerOptionsBuilder tickerOptionsBuilder, TickerCollection tickerCollection, IServiceProvider serviceProvider, ILogger<TickerHost> logger, IClock clock)
        {
            TickerOptionsBuilder = tickerOptionsBuilder ?? throw new ArgumentNullException(nameof(tickerOptionsBuilder));
            TickerCollection = tickerCollection ?? throw new ArgumentNullException(nameof(tickerCollection));
            ServiceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            Logger = logger ?? throw new ArgumentNullException(nameof(logger));
            Clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        public void Run()
        {
            if (TickerCollection.TickerFunctionsDelegate.Count == 0 || CtsTickerChecker != default)
                return;

            Task.Run(async () =>
            {
                try
                {
                    CtsTickerChecker = new CancellationTokenSource();

                    CtsTickerCheckerDelayAwaiter = CancellationTokenSource.CreateLinkedTokenSource(CtsTickerChecker.Token);

                    CtsTickerTimeoutChecker = TickerOptionsBuilder.UseEfCore
                        ? new CancellationTokenSource()
                        : default;

                    var tickerTask = (CtsTickerChecker != default)
                        ? StartTickerCheckingLoop(CtsTickerChecker.Token)
                        : Task.CompletedTask;

                    var timeoutTask = (CtsTickerTimeoutChecker != default)
                        ? StartTimeoutTickerCheckingLoop(CtsTickerTimeoutChecker.Token)
                        : Task.CompletedTask;

                    await Task.WhenAll(tickerTask, timeoutTask).ConfigureAwait(false);
                }
                catch (TaskCanceledException)
                {
                    CtsTickerChecker?.Dispose();
                    CtsTickerTimeoutChecker?.Dispose();
                    CtsTickerCheckerDelayAwaiter?.Dispose();
                }
            }).ConfigureAwait(false);

        }

        private async Task StartTickerCheckingLoop(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                TimeSpan timeRemaining;

                (string FunctionName, Guid TickerId, TickerType type)[] functions = default;

                try
                {
                    (timeRemaining, functions) = TickerOptionsBuilder.UseEfCore
                      ? await TickerHelper.GetNearestOccurrenceFromDbAsync(ServiceProvider).ConfigureAwait(false)
                      : TickerHelper.GetNearestMemoryCronExpressions(TickerCollection.MemoryCronExpressions, Clock.Now);

                    if (timeRemaining == Timeout.InfiniteTimeSpan)
                        NextPlannedOccurrence = null;
                    else
                        NextPlannedOccurrence = Clock.OffsetNow.Add(timeRemaining);

                    await Task.Delay(timeRemaining, CtsTickerCheckerDelayAwaiter.Token).ConfigureAwait(false);

                    if (TickerOptionsBuilder.UseEfCore && functions != default)
                        await TickerHelper.SetTickersInprogress(ServiceProvider, functions).ConfigureAwait(false);

                    if (functions != default)
                        OnTimerTick(functions, cancellationToken);
                }
                catch (TaskCanceledException)
                {
                    NextPlannedOccurrence = default;

                    if (TickerOptionsBuilder.UseEfCore && functions != default)
                        await TickerHelper.ReleaseAcquiredResourcesAsync(ServiceProvider, functions).ConfigureAwait(false);

                    if (cancellationToken.IsCancellationRequested)
                        return;

                    CtsTickerCheckerDelayAwaiter?.Dispose();
                    CtsTickerCheckerDelayAwaiter = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Critical error in Ticker, stopping application...");

                    CtsTickerChecker?.Cancel();
                    CtsTickerTimeoutChecker?.Cancel();
                }
            }
        }

        private async Task StartTimeoutTickerCheckingLoop(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {

                if (TickerOptionsBuilder.TimeOutChecker == Timeout.InfiniteTimeSpan)
                    return;

                await Task.Delay(TickerOptionsBuilder.TimeOutChecker, cancellationToken);

                var functions = await TickerHelper.GetTimeoutedFunctions(ServiceProvider, cancellationToken).ConfigureAwait(false);

                if (functions.Length != 0)
                    OnTimerTick(functions, cancellationToken, true);
            }
        }

        public void Stop()
        {
            CtsTickerChecker?.Cancel();
            CtsTickerTimeoutChecker?.Cancel();
        }

        public void RestartIfNeeded(DateTime newOccurrence)
        {
            if (NextPlannedOccurrence == null)
                Restart();

            else if (Math.Abs((newOccurrence - NextPlannedOccurrence.Value.DateTime).TotalSeconds) <= 3)
                Restart();
        }

        public void Restart()
        {
            CtsTickerCheckerDelayAwaiter?.Cancel();
        }
    }
}
