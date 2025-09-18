using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using TickerQ.Utilities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;
using TickerQ.Utilities.Interfaces.Managers;
namespace TickerQ.Base
{
    internal abstract class BaseTicker : ITickerHost
    {
        protected readonly ITickerClock Clock;
        private readonly TickerExecutionContext _executionContext;
        private SafeCancellationTokenSource CtsTickerChecker { get; set; }
        private SafeCancellationTokenSource CtsTickerDelayAwaiter { get; set; }
        private SafeCancellationTokenSource CtsTickerTimeoutChecker { get; set; }
        private SafeCancellationTokenSource CtsTickerTimeoutDelayAwaiter { get; set; }
        private readonly RestartThrottleManager _restartThrottle;
        protected readonly TickerTaskScheduler TickerTaskScheduler;
        protected IInternalTickerManager InternalTickerManager;
        protected readonly IServiceProvider ServiceProvider;
        protected abstract Task OnTimerTick(InternalFunctionContext[] functions, bool dueDone, CancellationToken cancellationToken);

        protected BaseTicker(TickerExecutionContext executionContext, ILogger<TickerHost> logger, ITickerClock clock, IServiceProvider serviceProvider)
        {
            ServiceProvider = serviceProvider;
            Clock = clock ?? throw new ArgumentNullException(nameof(clock));
            TickerTaskScheduler = new TickerTaskScheduler(executionContext);
            _executionContext = executionContext ?? throw new ArgumentNullException(nameof(executionContext));
            _restartThrottle = new RestartThrottleManager(SoftNotifyDelayChange);
        }

        public void Run()
        {
            InternalTickerManager ??= ServiceProvider.GetService<IInternalTickerManager>();
            
            Stop();
            
            _executionContext.NotifyCoreAction(string.Empty, CoreNotifyActionType.NotifyHostExceptionMessage);

            if (TickerFunctionProvider.TickerFunctions.Count == 0)
                return;

            Task.Run(async () =>
            {
                try
                {
                    CtsTickerChecker = new SafeCancellationTokenSource();
                    CtsTickerTimeoutChecker = new SafeCancellationTokenSource();

                    var tickerTask  = (CtsTickerChecker?.IsDisposed == false)
                        ? StartTickerCheckingLoop()
                        : Task.CompletedTask;

                    var timeoutTask = (CtsTickerTimeoutChecker?.IsDisposed == false)
                        ? StartTimeoutTickerCheckingLoop()
                        : Task.CompletedTask;

                    await Task.WhenAll(tickerTask, timeoutTask).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (
                    (CtsTickerChecker?.IsCancellationRequested ?? false) ||
                    (CtsTickerTimeoutChecker?.IsCancellationRequested ?? false))
                {
                    // normal shutdown
                }
                catch (Exception ex)
                {
                    _executionContext.NotifyCoreAction(ex.ToString(), CoreNotifyActionType.NotifyHostExceptionMessage);
                    _executionContext.NotifyCoreAction(null, CoreNotifyActionType.NotifyNextOccurence);
                }
                finally
                {
                    _executionContext.SetFunctions([]);

                    CtsTickerChecker?.Dispose();
                    CtsTickerTimeoutChecker?.Dispose();
                    await InternalTickerManager.ReleaseAcquiredResources([]).ConfigureAwait(false);
                }
            });
        }

        private async Task StartTickerCheckingLoop()
        {
            CtsTickerDelayAwaiter = SafeCancellationTokenSource.CreateLinked(CtsTickerChecker.Token);
            
            while (!CtsTickerChecker!.Token.IsCancellationRequested)
            {
                _executionContext.NotifyCoreAction(string.Empty, CoreNotifyActionType.NotifyHostExceptionMessage);

                InternalFunctionContext[] functions = [];
                
                try
                {
                    (var timeRemaining, functions) = await InternalTickerManager.GetNextTickers(CtsTickerChecker.Token).ConfigureAwait(false);
                    
                    _executionContext.SetFunctions(functions);
                    if (timeRemaining == Timeout.InfiniteTimeSpan)
                    {
                        _executionContext.SetNextPlannedOccurrence(null);
                        _executionContext.NotifyCoreAction(_executionContext.NextPlannedOccurrence, CoreNotifyActionType.NotifyNextOccurence);
                        await Task.Delay(Timeout.InfiniteTimeSpan, CtsTickerDelayAwaiter.Token).ConfigureAwait(false);
                    }
                    else
                    {
                        _executionContext.SetNextPlannedOccurrence(Clock.UtcNow.Add(timeRemaining));
                        _executionContext.NotifyCoreAction(_executionContext.NextPlannedOccurrence, CoreNotifyActionType.NotifyNextOccurence);

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
                            await InternalTickerManager.SetTickersInProgress(functions, CtsTickerChecker.Token).ConfigureAwait(false);

                        if (functions?.Length != 0)
                        {
                            await OnTimerTick(functions, dueDone: false, cancellationToken: CtsTickerChecker.Token).ConfigureAwait(false);
                            _executionContext.SetFunctions([]);
                        }

                        if (CtsTickerDelayAwaiter.IsCancellationRequested)
                            ResetCtsTickerDelayAwaiter();
                    }
                }
                catch (Exception) when (CtsTickerDelayAwaiter.IsCancellationRequested)
                {
                    if (functions?.Length != 0)
                    {
                        await InternalTickerManager.ReleaseAcquiredResources(functions, CancellationToken.None);

                        _executionContext.SetFunctions([]);
                    }

                    if (CtsTickerChecker.IsCancellationRequested)
                        return;

                    CtsTickerDelayAwaiter?.Dispose();
                    CtsTickerDelayAwaiter = SafeCancellationTokenSource.CreateLinked(CtsTickerChecker.Token);
                }
                catch (Exception ex)
                {
                    _executionContext.SetFunctions([]);
                    _executionContext.NotifyCoreAction(ex.StackTrace, CoreNotifyActionType.NotifyHostExceptionMessage);
                    _executionContext.NotifyCoreAction(false, CoreNotifyActionType.NotifyHostStatus);
                    _executionContext.NotifyCoreAction(null, CoreNotifyActionType.NotifyNextOccurence);
                    CtsTickerChecker?.Cancel();
                }
            }
        }

        private async Task StartTimeoutTickerCheckingLoop()
        {
            CtsTickerTimeoutDelayAwaiter = SafeCancellationTokenSource.CreateLinked(CtsTickerTimeoutChecker.Token);

            if (_executionContext.TimeOutChecker == Timeout.InfiniteTimeSpan)
                return;

            var delayAwaiter = _executionContext.TimeOutChecker;
            while (!CtsTickerTimeoutChecker.Token.IsCancellationRequested)
            {
                try
                {
                    var functions = await InternalTickerManager.RunTimedOutTickers(CtsTickerTimeoutChecker.Token);
                
                    if (functions.Length == 0)
                    {
                        await Task.Delay(delayAwaiter, CtsTickerTimeoutDelayAwaiter.Token);
                        delayAwaiter = _executionContext.TimeOutChecker;
                        continue;
                    }
                
                    await OnTimerTick(functions, dueDone: true, cancellationToken: CtsTickerTimeoutChecker.Token);
                }
                catch (Exception) when (CtsTickerTimeoutDelayAwaiter.IsCancellationRequested)
                {
                    if (CtsTickerTimeoutChecker.IsCancellationRequested)
                        return;
                
                    delayAwaiter = TimeSpan.FromSeconds(1);
                
                    CtsTickerTimeoutDelayAwaiter?.Dispose();
                    CtsTickerTimeoutDelayAwaiter = SafeCancellationTokenSource.CreateLinked(CtsTickerTimeoutChecker.Token);
                }
                catch (Exception)
                {
                    //
                }
            }
        }


        public void Stop()
        {
            _executionContext.NotifyCoreAction(string.Empty, CoreNotifyActionType.NotifyHostExceptionMessage);

            if (CtsTickerTimeoutChecker?.IsDisposed == false)
                CtsTickerTimeoutChecker.Cancel();

            if (CtsTickerChecker?.IsDisposed == false)
                CtsTickerChecker.Cancel();

            TickerCancellationTokenManager.CleanUpTickerCancellationTokens();
            _executionContext.SetNextPlannedOccurrence(null);
            _executionContext.NotifyCoreAction(_executionContext.NextPlannedOccurrence, CoreNotifyActionType.NotifyNextOccurence);
            _executionContext.NotifyCoreAction(false, CoreNotifyActionType.NotifyHostStatus);
        }
        
        private void SoftNotifyDelayChange()
        {
            if (CtsTickerDelayAwaiter?.IsDisposed == false)
                CtsTickerDelayAwaiter.Cancel();
        }

        public void RestartIfNeeded(DateTime nextPlannedOccurrence)
        {
            if (_executionContext.NextPlannedOccurrence == null || (_executionContext.NextPlannedOccurrence.Value - nextPlannedOccurrence).TotalSeconds >= 1)
                _restartThrottle.RequestRestart();
        }
        
        public void Restart()
        {
            if (CtsTickerDelayAwaiter?.IsDisposed == false)
                CtsTickerDelayAwaiter.Cancel();

            _executionContext.NotifyCoreAction((CtsTickerChecker?.IsDisposed == false), CoreNotifyActionType.NotifyHostStatus);
        }

        public bool IsRunning()
            => (CtsTickerChecker?.IsDisposed == false);

        private void ResetCtsTickerDelayAwaiter()
        {
            CtsTickerDelayAwaiter?.Dispose();
            CtsTickerDelayAwaiter = SafeCancellationTokenSource.CreateLinked(CtsTickerChecker.Token);
        }
    }
}