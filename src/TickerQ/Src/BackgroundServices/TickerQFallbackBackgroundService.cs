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
        while (!stoppingToken.IsCancellationRequested)
        {
            // If the scheduler is frozen or disposed (e.g., manual start mode or shutdown),
            // skip queuing fallback work to avoid throwing and stopping the host.
            if (_tickerQTaskScheduler.IsFrozen || _tickerQTaskScheduler.IsDisposed)
            {
                await Task.Delay(_fallbackJobPeriod, stoppingToken);
                continue;
            }

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

                    foreach (var child in function.TimeTickerChildren)
                    {
                        if (TickerFunctionProvider.TickerFunctions.TryGetValue(child.FunctionName, out var childItem))
                        {
                            child.CachedDelegate = childItem.Delegate;
                            child.CachedPriority = childItem.Priority;
                        }

                        foreach (var grandChild in child.TimeTickerChildren)
                        {
                            if (TickerFunctionProvider.TickerFunctions.TryGetValue(grandChild.FunctionName, out var grandChildItem))
                            {
                                grandChild.CachedDelegate = grandChildItem.Delegate;
                                grandChild.CachedPriority = grandChildItem.Priority;
                            }
                        }
                    }

                    try
                    {
                        await _tickerQTaskScheduler.QueueAsync(
                            ct => _tickerExecutionTaskHandler.ExecuteTaskAsync(function, true, ct),
                            function.CachedPriority,
                            stoppingToken);
                    }
                    catch (InvalidOperationException) when (_tickerQTaskScheduler.IsFrozen || _tickerQTaskScheduler.IsDisposed)
                    {
                        // Scheduler is frozen/disposed – ignore and let loop delay
                        break;
                    }
                }

                await Task.Delay(TimeSpan.FromMilliseconds(10), stoppingToken);
            }
            else
            {
                await Task.Delay(_fallbackJobPeriod, stoppingToken);
            }
        }
    }
    
        
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        Interlocked.Exchange(ref _started, 0);
        await base.StopAsync(cancellationToken);
    }
}
