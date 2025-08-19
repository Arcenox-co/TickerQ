using Microsoft.Extensions.Logging;
using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using TickerQ.Utilities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Exceptions;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;
using TickerQ.Utilities.Interfaces.Managers;

namespace TickerQ.Base
{
    internal abstract class BaseTicker : ITickerHost
    {
        private readonly ITickerClock _clock;
        protected readonly IServiceProvider ServiceProvider;
        private TickerOptionsBuilder TickerOptionsBuilder { get; }
        private SafeCancellationTokenSource CtsTickerChecker { get; set; }
        private SafeCancellationTokenSource CtsTickerDelayAwaiter { get; set; }
        private SafeCancellationTokenSource CtsTickerTimeoutChecker { get; set; }
        private SafeCancellationTokenSource CtsTickerTimeoutDelayAwaiter { get; set; }
        public DateTime? NextPlannedOccurrence { get; private set; }
        private readonly RestartThrottleManager _restartThrottle;
        protected abstract Task OnTimerTick(InternalFunctionContext[] functions,
            CancellationToken cancellationToken = default, bool dueDone = false);

        protected BaseTicker(TickerOptionsBuilder tickerOptionsBuilder,
            IServiceProvider serviceProvider, ILogger<TickerHost> logger, ITickerClock clock)
        {
            TickerOptionsBuilder =
                tickerOptionsBuilder ?? throw new ArgumentNullException(nameof(tickerOptionsBuilder));
            ServiceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _restartThrottle = new RestartThrottleManager(SoftNotifyDelayChange);
        }

        private void Run()
        {
            TickerOptionsBuilder.HostExceptionMessageFunc(string.Empty);

            if (TickerFunctionProvider.TickerFunctions.Count == 0)
                return;
            
            Task.Run(async () =>
            {
                try
                {
                    CtsTickerChecker = new SafeCancellationTokenSource();

                    CtsTickerTimeoutChecker = new SafeCancellationTokenSource();

                    var tickerTask = (CtsTickerChecker?.IsDisposed == false)
                        ? StartTickerCheckingLoop()
                        : Task.CompletedTask;

                    var timeoutTask = (CtsTickerTimeoutChecker?.IsDisposed == false)
                        ? StartTimeoutTickerCheckingLoop()
                        : Task.CompletedTask;

                    await Task.WhenAll(tickerTask, timeoutTask).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    TickerOptionsBuilder.HostExceptionMessageFunc(ex.StackTrace);
                    TickerOptionsBuilder.NotifyNextOccurenceFunc(null);
                }
                finally
                {
                    CtsTickerChecker?.Dispose();
                    CtsTickerTimeoutChecker?.Dispose();
                    using var scope = ServiceProvider.CreateScope();
                    var internalTickerManager = scope.ServiceProvider.GetRequiredService<IInternalTickerManager>();
                    await internalTickerManager.ReleaseAllAcquiredResources(ReleaseAcquiredTermination.ToIdle);
                }
            });
        }

        private async Task StartTickerCheckingLoop()
        {
            CtsTickerDelayAwaiter = SafeCancellationTokenSource.CreateLinked(CtsTickerChecker.Token);

            using var scope = ServiceProvider.CreateScope();

            var internalTickerManager = scope.ServiceProvider.GetRequiredService<IInternalTickerManager>();

            while (!CtsTickerChecker.Token.IsCancellationRequested)
            {
                TickerOptionsBuilder.HostExceptionMessageFunc(string.Empty);

                var functions = Array.Empty<InternalFunctionContext>();
                try
                {
                    TimeSpan timeRemaining;

                    (timeRemaining, functions) = await internalTickerManager.GetNextTickers(CtsTickerChecker.Token)
                        .ConfigureAwait(false);

                    if (timeRemaining == Timeout.InfiniteTimeSpan)
                    {
                        NextPlannedOccurrence = null;
                        TickerOptionsBuilder.NotifyNextOccurenceFunc(NextPlannedOccurrence);
                        await Task.Delay(Timeout.InfiniteTimeSpan, CtsTickerDelayAwaiter.Token).ConfigureAwait(false);
                    }
                    else
                    {
                        NextPlannedOccurrence = _clock.UtcNow.Add(timeRemaining);
                        TickerOptionsBuilder.NotifyNextOccurenceFunc(NextPlannedOccurrence);

                        var sleepDuration = timeRemaining > TimeSpan.FromDays(1)
                            ? TimeSpan.FromDays(1)
                            : timeRemaining;

                        if (CtsTickerDelayAwaiter.IsCancellationRequested)
                            ResetCtsTickerDelayAwaiter();

                        if (sleepDuration > TimeSpan.Zero && sleepDuration < TimeSpan.FromMilliseconds(500))
                            await Task.Delay(sleepDuration, CancellationToken.None).ConfigureAwait(false);
                        else if (sleepDuration > TimeSpan.Zero)
                            await Task.Delay(sleepDuration, CtsTickerDelayAwaiter.Token).ConfigureAwait(false);

                        if (functions?.Length != 0)
                            await internalTickerManager.SetTickersInProgress(functions, CtsTickerChecker.Token)
                                .ConfigureAwait(false);

                        if (functions?.Length != 0)
                            await OnTimerTick(functions, CtsTickerChecker.Token);

                        if (CtsTickerDelayAwaiter.IsCancellationRequested)
                            ResetCtsTickerDelayAwaiter();
                    }
                }
                catch (Exception) when (CtsTickerDelayAwaiter.IsCancellationRequested)
                {
                    if (functions?.Length != 0)
                        await internalTickerManager.ReleaseAcquiredResources(functions, CancellationToken.None)
                            .ConfigureAwait(false);

                    if (CtsTickerChecker.IsCancellationRequested)
                        return;

                    CtsTickerDelayAwaiter?.Dispose();
                    CtsTickerDelayAwaiter = SafeCancellationTokenSource.CreateLinked(CtsTickerChecker.Token);
                }
                catch (CronOccurrenceAlreadyExistsException)
                {
                    await Task.Delay(TimeSpan.FromSeconds(new Random().Next(12, 32)));
                }
                catch (DBConcurrencyException)
                {
                    await Task.Delay(TimeSpan.FromSeconds(new Random().Next(12, 32)));
                }
                catch (Exception ex)
                {
                    TickerOptionsBuilder.HostExceptionMessageFunc(ex.StackTrace);
                    TickerOptionsBuilder.NotifyHostStatusFunc(false);
                    TickerOptionsBuilder.NotifyNextOccurenceFunc(null);

                    CtsTickerChecker?.Cancel();
                }
            }
        }

        private async Task StartTimeoutTickerCheckingLoop()
        {
            CtsTickerTimeoutDelayAwaiter = SafeCancellationTokenSource.CreateLinked(CtsTickerTimeoutChecker.Token);

            using var scope = ServiceProvider.CreateScope();
            var internalTickerManager = scope.ServiceProvider.GetRequiredService<IInternalTickerManager>();

            if (TickerOptionsBuilder.TimeOutChecker == Timeout.InfiniteTimeSpan)
                return;

            var delayAwaiter = TickerOptionsBuilder.TimeOutChecker;
            while (!CtsTickerTimeoutChecker.Token.IsCancellationRequested)
            {
                try
                {
                    var functions = await internalTickerManager.GetTimedOutFunctions(CtsTickerTimeoutChecker.Token)
                        .ConfigureAwait(false);

                    if (functions.Length == 0)
                    {
                        await Task.Delay(delayAwaiter, CtsTickerTimeoutDelayAwaiter.Token).ConfigureAwait(false);
                        delayAwaiter = TickerOptionsBuilder.TimeOutChecker;
                        continue;
                    }

                    await OnTimerTick(functions, CtsTickerTimeoutChecker.Token, true);
                }
                catch (Exception) when (CtsTickerTimeoutDelayAwaiter.IsCancellationRequested)
                {
                    if (CtsTickerTimeoutChecker.IsCancellationRequested)
                        return;

                    delayAwaiter = TimeSpan.FromSeconds(1);

                    CtsTickerTimeoutDelayAwaiter?.Dispose();
                    CtsTickerTimeoutDelayAwaiter =
                        SafeCancellationTokenSource.CreateLinked(CtsTickerTimeoutChecker.Token);
                }
            }
        }

        public void Stop()
        {
            TickerOptionsBuilder.HostExceptionMessageFunc(string.Empty);

            if (CtsTickerTimeoutChecker?.IsDisposed == false)
                CtsTickerTimeoutChecker?.Cancel();

            if (CtsTickerChecker?.IsDisposed == false)
                CtsTickerChecker?.Cancel();

            TickerCancellationTokenManager.CleanUpTickerCancellationTokens();
            NextPlannedOccurrence = null;
            TickerOptionsBuilder.NotifyNextOccurenceFunc(NextPlannedOccurrence);
            TickerOptionsBuilder.NotifyHostStatusFunc(false);
        }
        
        private void SoftNotifyDelayChange()
        {
            if (CtsTickerDelayAwaiter?.IsDisposed == false)
                CtsTickerDelayAwaiter.Cancel();
        }

        public void RestartIfNeeded(DateTime nextPlannedOccurrence)
        {
            if (NextPlannedOccurrence == null ||
                (NextPlannedOccurrence.Value - nextPlannedOccurrence).TotalSeconds >= 1)
            {
                _restartThrottle.RequestRestart();
            }
        }
        public void Restart()
        {
            if (CtsTickerDelayAwaiter?.IsDisposed == false)
                CtsTickerDelayAwaiter.Cancel();

            TickerOptionsBuilder.NotifyHostStatusFunc((CtsTickerChecker?.IsDisposed == false));
        }

        public void Start()
        {
            Stop();
            Run();
            TickerOptionsBuilder.NotifyHostStatusFunc(true);
        }

        public bool IsRunning()
        {
            return (CtsTickerChecker?.IsDisposed == false);
        }
        
        private void ResetCtsTickerDelayAwaiter()
        {
            CtsTickerDelayAwaiter?.Dispose();
            CtsTickerDelayAwaiter = SafeCancellationTokenSource.CreateLinked(CtsTickerChecker.Token);
        }
    }
}