using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
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
    private readonly IInternalTickerManager _internalTickerManager;
    private readonly TickerExecutionContext _executionContext;
    private SafeCancellationTokenSource _schedulerLoopCancellationTokenSource;
    private readonly TickerQTaskScheduler  _taskScheduler;
    private readonly TickerExecutionTaskHandler  _taskHandler;
    private int _started;
    public bool SkipFirstRun;
    public bool IsRunning => _started == 1;

    
    public TickerQSchedulerBackgroundService(
        TickerExecutionContext executionContext,
        TickerExecutionTaskHandler taskHandler, 
        TickerQTaskScheduler taskScheduler, 
        IInternalTickerManager  internalTickerManager)
    {
        _executionContext = executionContext;
        _taskHandler = taskHandler;
        _taskScheduler = taskScheduler;
        _internalTickerManager = internalTickerManager ?? throw new ArgumentNullException(nameof(internalTickerManager));
        _restartThrottle = new RestartThrottleManager(() => _schedulerLoopCancellationTokenSource?.Cancel());
    }
    
    public override Task StartAsync(CancellationToken ct)
    {
        if (SkipFirstRun)
        {
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
                // This is a restart request - release resources and continue loop
                await _internalTickerManager.ReleaseAcquiredResources(_executionContext.Functions, stoppingToken);
                // Small delay to allow resources to be released
                await Task.Delay(100, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Application is shutting down - release resources and exit
                await _internalTickerManager.ReleaseAcquiredResources(_executionContext.Functions, CancellationToken.None);
                break;
            }
            catch (Exception ex)
            {
                await ReleaseAllResourcesAsync(ex);
                // Continue running - don't exit the scheduler loop on exceptions
                // Add a small delay to prevent tight loop if errors persist
                await Task.Delay(1000, stoppingToken);
            }
            finally
            {
                // CRITICAL: Must dispose PeriodicTimer to prevent memory leak
                _tickerJobPeriodicTimer?.Dispose();
                _tickerJobPeriodicTimer = null;
                
                _schedulerLoopCancellationTokenSource?.Dispose();
                _schedulerLoopCancellationTokenSource = null;
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

            var sleepDuration = timeRemaining > TimeSpan.FromDays(1) || timeRemaining == Timeout.InfiniteTimeSpan
                ? TimeSpan.FromDays(1)
                : timeRemaining;

            if (sleepDuration <= TimeSpan.Zero)
                sleepDuration = TimeSpan.FromMilliseconds(1);

            _tickerJobPeriodicTimer.Period = sleepDuration;
            
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
        
        // Restart if:
        // 1. No tasks are currently planned, OR
        // 2. The new task should execute BEFORE the currently planned task
        if (nextPlannedOccurrence == null || dateTime.Value < nextPlannedOccurrence.Value)
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