using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using TickerQ.TickerQThreadPool;
using TickerQ.Utilities;
using TickerQ.Utilities.Interfaces.Managers;

namespace TickerQ.BackgroundServices;

internal class TickerQFallbackBackgroundService :  BackgroundService
{
    private int _started;
    private PeriodicTimer _tickerFallbackJobPeriodicTimer;
    private readonly IInternalTickerManager _internalTickerManager;
    private readonly TickerExecutionTaskHandler _tickerExecutionTaskHandler;
    private readonly TickerQTaskScheduler _tickerQTaskScheduler;
    private readonly TimeSpan _fallbackJobPeriod;

    public TickerQFallbackBackgroundService(IInternalTickerManager internalTickerManager, SchedulerOptionsBuilder schedulerOptions, TickerExecutionTaskHandler tickerExecutionTaskHandler, TickerQTaskScheduler tickerQTaskScheduler)
    {
        _internalTickerManager = internalTickerManager;
        _fallbackJobPeriod = schedulerOptions.FallbackIntervalChecker;
        _tickerExecutionTaskHandler = tickerExecutionTaskHandler;
        _tickerQTaskScheduler = tickerQTaskScheduler;
    }

    public override Task StartAsync(CancellationToken ct)
    {
        return Interlocked.CompareExchange(ref _started, 1, 0) != 0 
            ? Task.CompletedTask : base.StartAsync(ct);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _tickerFallbackJobPeriodicTimer = new PeriodicTimer(TimeSpan.FromMilliseconds(10));
        await RunTickerQFallbackAsync(stoppingToken);
    }
    
    private async Task RunTickerQFallbackAsync(CancellationToken stoppingToken)
    {
        while (await _tickerFallbackJobPeriodicTimer.WaitForNextTickAsync(stoppingToken))
        {
            var oldPeriod = _tickerFallbackJobPeriodicTimer.Period;
            
            var functions = await _internalTickerManager.RunTimedOutTickers(stoppingToken);

            if (functions.Length != 0)
            {
                foreach (var function in functions)
                {
                    if (TickerFunctionProvider.TickerFunctions.TryGetValue(function.FunctionName, out var tickerItem))
                    {
                        function.CachedDelegate = tickerItem.Delegate;
                        function.CachedPriority = tickerItem.Priority;
                    }
                    await _tickerQTaskScheduler.QueueAsync(ct => _tickerExecutionTaskHandler.ExecuteTaskAsync(function, true, ct), function.CachedPriority, stoppingToken);
                }

                _tickerFallbackJobPeriodicTimer.Period = TimeSpan.FromMilliseconds(10);
            }
            else
                _tickerFallbackJobPeriodicTimer.Period = _fallbackJobPeriod;
            
            if(oldPeriod != _fallbackJobPeriod)
                await _tickerFallbackJobPeriodicTimer.WaitForNextTickAsync(stoppingToken);
        }
    }
    
        
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        Interlocked.Exchange(ref _started, 0);
        await base.StopAsync(cancellationToken);
    }
}