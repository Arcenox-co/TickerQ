using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using TickerQ.Utilities;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;

namespace TickerQ.BackgroundServices;

internal class TickerQFallbackBackgroundService :  BackgroundService
{
    private int _started;
    private readonly IInternalTickerManager _internalTickerManager;
    private readonly ITickerExecutionTaskHandler _tickerExecutionTaskHandler;
    private readonly ITickerQTaskScheduler _tickerQTaskScheduler;
    private readonly ITickerFunctionConcurrencyGate _concurrencyGate;
    private readonly TimeSpan _fallbackJobPeriod;

    public TickerQFallbackBackgroundService(IInternalTickerManager internalTickerManager, SchedulerOptionsBuilder schedulerOptions, ITickerExecutionTaskHandler tickerExecutionTaskHandler, ITickerQTaskScheduler tickerQTaskScheduler, ITickerFunctionConcurrencyGate concurrencyGate)
    {
        _internalTickerManager = internalTickerManager;
        _fallbackJobPeriod = schedulerOptions.FallbackIntervalChecker;
        _tickerExecutionTaskHandler = tickerExecutionTaskHandler;
        _tickerQTaskScheduler = tickerQTaskScheduler;
        _concurrencyGate = concurrencyGate;
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
            try
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
                            function.CachedMaxConcurrency = tickerItem.MaxConcurrency;
                        }

                        foreach (var child in function.TimeTickerChildren)
                        {
                            if (TickerFunctionProvider.TickerFunctions.TryGetValue(child.FunctionName, out var childItem))
                            {
                                child.CachedDelegate = childItem.Delegate;
                                child.CachedPriority = childItem.Priority;
                                child.CachedMaxConcurrency = childItem.MaxConcurrency;
                            }

                            foreach (var grandChild in child.TimeTickerChildren)
                            {
                                if (TickerFunctionProvider.TickerFunctions.TryGetValue(grandChild.FunctionName, out var grandChildItem))
                                {
                                    grandChild.CachedDelegate = grandChildItem.Delegate;
                                    grandChild.CachedPriority = grandChildItem.Priority;
                                    grandChild.CachedMaxConcurrency = grandChildItem.MaxConcurrency;
                                }
                            }
                        }

                        try
                        {
                            var semaphore = _concurrencyGate.GetSemaphoreOrNull(function.FunctionName, function.CachedMaxConcurrency);

                            await _tickerQTaskScheduler.QueueAsync(
                                async ct =>
                                {
                                    if (semaphore != null)
                                        await semaphore.WaitAsync(ct).ConfigureAwait(false);

                                    try
                                    {
                                        await _tickerExecutionTaskHandler.ExecuteTaskAsync(function, true, ct).ConfigureAwait(false);
                                    }
                                    finally
                                    {
                                        semaphore?.Release();
                                    }
                                },
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
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Host is shutting down – exit gracefully.
                break;
            }
            catch (Exception)
            {
                // Swallow unexpected exceptions so they don't bubble up
                // and stop the host; wait a bit before retrying.
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
