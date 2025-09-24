using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TickerQ.Base;
using TickerQ.TickerQThreadPool;
using TickerQ.Utilities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;

namespace TickerQ.BackgroundServices;

internal class TickerQSchedulerBackgroundService : BackgroundService, ITickerQHostScheduler
{
    private PeriodicTimer _tickerJobPeriodicTimer;
    private readonly RestartThrottleManager _restartThrottle;
    private IInternalTickerManager _internalTickerManager;
    private readonly IServiceProvider _serviceProvider;
    private readonly TickerExecutionContext _executionContext;
    private SafeCancellationTokenSource _schedulerLoopCancellationTokenSource;
    private readonly TickerQTaskScheduler  _taskScheduler;
    private readonly TickerExecutionTaskHandler  _taskHandler;
    private int _started;
    public bool SkipFirstRun;

    
    public TickerQSchedulerBackgroundService(
        TickerExecutionContext executionContext,
        TickerExecutionTaskHandler taskHandler, 
        TickerQTaskScheduler taskScheduler, 
        IServiceProvider serviceProvider)
    {
        _executionContext = executionContext;
        _taskHandler = taskHandler;
        _taskScheduler = taskScheduler;
        _serviceProvider = serviceProvider;
        _restartThrottle = new RestartThrottleManager(() => _schedulerLoopCancellationTokenSource?.Cancel());
    }
    
    public override Task StartAsync(CancellationToken ct)
    {
        if (SkipFirstRun)
        {
            Console.WriteLine("Skip first run");
            _taskScheduler.Freeze();
            SkipFirstRun = false;
            return Task.CompletedTask;
        }
        
        _taskScheduler.Resume();
        return Interlocked.CompareExchange(ref _started, 1, 0) != 0 
            ? Task.CompletedTask : base.StartAsync(ct);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _internalTickerManager ??= _serviceProvider.GetService<IInternalTickerManager>();
        
        while (!stoppingToken.IsCancellationRequested)
        {
            _schedulerLoopCancellationTokenSource = SafeCancellationTokenSource.CreateLinked(stoppingToken);

            try
            {
                _tickerJobPeriodicTimer = new PeriodicTimer(TimeSpan.FromMilliseconds(1));
                await RunTickerQSchedulerAsync(stoppingToken, _schedulerLoopCancellationTokenSource.Token);
            }
            catch (OperationCanceledException) when (_schedulerLoopCancellationTokenSource.Token.IsCancellationRequested && !stoppingToken.IsCancellationRequested)
            {
                await _internalTickerManager.ReleaseAcquiredResources(_executionContext.Functions, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                await _internalTickerManager.ReleaseAcquiredResources(_executionContext.Functions, stoppingToken);
                break;
            }
            catch (Exception ex)
            {
                await ReleaseAllResourcesAsync(ex);
                break;
            }
            finally
            {
                _schedulerLoopCancellationTokenSource?.Dispose();
            }
        }
    }

    private async Task RunTickerQSchedulerAsync(CancellationToken stoppingToken, CancellationToken cancellationToken)
    {
        while (await _tickerJobPeriodicTimer.WaitForNextTickAsync(cancellationToken))
        {
            var oldPeriod = _tickerJobPeriodicTimer.Period;
            
            if (_executionContext.Functions.Length != 0)
            {
                await _internalTickerManager.SetTickersInProgress(_executionContext.Functions, cancellationToken);

                foreach (var function in _executionContext.Functions)
                    await _taskScheduler.QueueAsync(async ct => await _taskHandler.ExecuteTaskAsync(function,false, ct), function.CachedPriority, stoppingToken);
            }
            
            var (timeRemaining, functions) =
                await _internalTickerManager.GetNextTickers(cancellationToken);

            _executionContext.SetFunctions(functions);

            var sleepDuration = timeRemaining > TimeSpan.FromDays(1)
                ? TimeSpan.FromDays(1)
                : timeRemaining;

            _tickerJobPeriodicTimer.Period = sleepDuration == TimeSpan.Zero
                ? TimeSpan.FromMilliseconds(5)
                : sleepDuration;
            
            if (timeRemaining == Timeout.InfiniteTimeSpan)
                _executionContext.SetNextPlannedOccurrence(null);
            else
                _executionContext.SetNextPlannedOccurrence(DateTime.UtcNow.Add(sleepDuration));
            
            _executionContext.NotifyCoreAction(_executionContext.GetNextPlannedOccurrence(), CoreNotifyActionType.NotifyNextOccurence);
            
            if(oldPeriod != _tickerJobPeriodicTimer.Period)
                await _tickerJobPeriodicTimer.WaitForNextTickAsync(cancellationToken);
        }
    }

    private async Task ReleaseAllResourcesAsync(Exception ex)
    {
        if (ex != null)
            _executionContext.NotifyCoreAction(ex.ToString(), CoreNotifyActionType.NotifyHostExceptionMessage);

        await _internalTickerManager.ReleaseAcquiredResources([], CancellationToken.None);
    }

    public void RestartIfNeeded(DateTime? dateTime)
    {
        if (!dateTime.HasValue)
            return;
        
        var nextPlannedOccurrence = _executionContext.GetNextPlannedOccurrence();
        
        if (nextPlannedOccurrence == null || (nextPlannedOccurrence.Value - dateTime.Value).TotalSeconds >= 1)
            _restartThrottle.RequestRestart();
    }

    public void Restart()
    {
        _restartThrottle.RequestRestart();
    }
    
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _taskScheduler.Freeze();
        Interlocked.Exchange(ref _started, 0);
        await base.StopAsync(cancellationToken);
    }
}