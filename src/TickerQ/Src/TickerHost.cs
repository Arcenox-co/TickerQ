using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using TickerQ.Base;
using TickerQ.Exceptions;
using TickerQ.Utilities;
using TickerQ.Utilities.Base;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ;

internal class TickerHost : BaseTicker
{
    private readonly SemaphoreSlim _semaphoreSlim = new(1, 1);

    public TickerHost(IServiceProvider serviceProvider, TickerExecutionContext executionContext, ILogger<TickerHost> logger, ITickerClock clock)
        : base(executionContext, logger, clock, serviceProvider)
    {
    }

    protected override async Task OnTimerTick(InternalFunctionContext[] functions, bool dueDone,
        CancellationToken cancellationToken)
    {
        await _semaphoreSlim.WaitAsync(cancellationToken);

        foreach (var context in functions)
        {
            if (!TickerFunctionProvider.TickerFunctions.TryGetValue(context.FunctionName, out var tickerItem))
                continue;

            if (tickerItem.Priority == TickerTaskPriority.LongRunning)
                _ = Task.Factory.StartNew(
                    async () => await ExecuteTaskAsync(context, tickerItem.Delegate, dueDone, cancellationToken),
                    TaskCreationOptions.LongRunning).Unwrap();
            else
            {
                var taskDetails = Task.Factory.StartNew(
                    async () => await ExecuteTaskAsync(context, tickerItem.Delegate, dueDone, cancellationToken),
                    cancellationToken, TaskCreationOptions.DenyChildAttach, TickerTaskScheduler).Unwrap();
                    
                TickerTaskScheduler.SetQueuedTaskPriority(taskDetails.Id, tickerItem.Priority);
            }
        }

        TickerTaskScheduler.ExecutePriorityTasks();
        _semaphoreSlim.Release();
    }

    private async Task ExecuteTaskAsync(InternalFunctionContext context, TickerFunctionDelegate delegateFunction,
        bool isDue, CancellationToken cancellationToken = default)
    {
        var hasChildren = context.TimeTickerChildren.Count > 0;
        
        var parentTask = RunContextFunctionAsync(context, delegateFunction, isDue, cancellationToken);

        if (hasChildren)
        {
            var functionsToRunImmediately = context.TimeTickerChildren
                .Where(x => x.RunCondition == RunCondition.InProgress)
                .Select(async childContext =>
                {
                    try
                    {
                        if (TickerFunctionProvider.TickerFunctions.TryGetValue(childContext.FunctionName, out var childTickerItem))
                            await RunContextFunctionAsync(childContext, childTickerItem.Delegate, isDue, cancellationToken, true);
                    }
                    catch (Exception)
                    {
                        // Don't rethrow - let other children continue
                    }
                })
                .Where(x => x != null).ToList();
            
            functionsToRunImmediately.Add(parentTask);
            
            await Task.WhenAll(functionsToRunImmediately);
        }
        else
            await parentTask;

        if (hasChildren)
        {
            var childExecutions = context.TimeTickerChildren
                .Where(x => x.RunCondition != RunCondition.InProgress)
                .Where(x => ShouldRunChild(x, context.Status))
                .Select(async childContext =>
                {
                    try
                    {
                        if (TickerFunctionProvider.TickerFunctions.TryGetValue(childContext.FunctionName, out var childTickerItem))
                            await RunContextFunctionAsync(childContext, childTickerItem.Delegate, isDue, cancellationToken, true);
                    }
                    catch (Exception)
                    {
                        // Don't rethrow - let other children continue
                    }
                }).ToArray();
            
            if(childExecutions.Length > 0)
                await Task.WhenAll(childExecutions);
        }
    }

    private async Task RunContextFunctionAsync(InternalFunctionContext context, TickerFunctionDelegate delegateFunction,
        bool isDue, CancellationToken cancellationToken, bool isChild = false)
    {
        context.SetProperty(x => x.Status, TickerStatus.InProgress);
        
        if(isChild)
            await InternalTickerManager.UpdateTickerAsync(context, cancellationToken);
        
        var stopWatch = new Stopwatch();
        var cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        
        var tickerFunctionContext = new TickerFunctionContext
        {
            Id = context.TickerId,
            Type = context.Type,
            IsDue = isDue,
            CancelOperationAction = () => cancellationTokenSource.Cancel(),
            CronOccurrenceOperations = new CronOccurrenceOperations
            {
                SkipIfAlreadyRunningAction = () =>
                {
                    if(context.Type == TickerType.TimeTicker)
                        return;
                    
                    var isRunning = TickerCancellationTokenManager.TickerCancellationTokens.Any(x => x.Value.ParentId == context.ParentId);
                    
                    if(isRunning)
                        throw new TerminateExecutionException("Another CronOccurrence is already running!");
                },
            }
        };

        TickerCancellationTokenManager.AddTickerCancellationToken(cancellationTokenSource, context.FunctionName, context.TickerId, context.Type, isDue);

        Exception lastException = null;
        var success = false;

        for (var attempt = context.RetryCount; attempt <= context.Retries; attempt++)
        {
            tickerFunctionContext.RetryCount = context.RetryCount;
            
            try
            {
                if (await WaitForRetry(context, cancellationToken, attempt, cancellationTokenSource)) break;

                stopWatch.Start();
                    
                await using var scope = ServiceProvider.CreateAsyncScope();

                await delegateFunction(cancellationTokenSource.Token, scope.ServiceProvider, tickerFunctionContext);

                success = true;
                context.RetryCount = attempt;
                break;
            }
            catch (TaskCanceledException ex)
            {
                context.SetProperty(x => x.Status, TickerStatus.Cancelled)
                    .SetProperty(x => x.ExecutedAt, Clock.UtcNow)
                    .SetProperty(x => x.ElapsedTime, stopWatch.ElapsedMilliseconds)
                    .SetProperty(x => x.ExceptionDetails, SerializeException(lastException));

                var handler = ServiceProvider.GetService<ITickerExceptionHandler>();

                if (handler != null)
                    await handler.HandleCanceledExceptionAsync(ex, context.TickerId, context.Type);

                await InternalTickerManager.UpdateTickerAsync(context, cancellationToken);
            }
            catch (TerminateExecutionException ex)
            {
                context.SetProperty(x => x.Status, TickerStatus.Skipped)
                    .SetProperty(x => x.ExecutedAt, Clock.UtcNow)
                    .SetProperty(x => x.ElapsedTime, stopWatch.ElapsedMilliseconds)
                    .SetProperty(x => x.ExceptionDetails, ex.Message);
                
                await InternalTickerManager.UpdateTickerAsync(context, cancellationToken);
                return;
            }
            catch (Exception ex)
            {
                lastException = ex;
            }
        }

        stopWatch.Stop();
            
        context.SetProperty(x => x.ElapsedTime, stopWatch.ElapsedMilliseconds)
            .SetProperty(x => x.ExecutedAt, Clock.UtcNow);
            
        if (success)
        {
            context.SetProperty(x => x.Status, isDue ? TickerStatus.DueDone : TickerStatus.Done);
                
            await InternalTickerManager.UpdateTickerAsync(context, cancellationToken);
        }
        else if (lastException != null)
        {
            context.SetProperty(x => x.Status, TickerStatus.Failed)
                .SetProperty(x => x.ExceptionDetails, SerializeException(lastException));

            var handler = ServiceProvider.GetService<ITickerExceptionHandler>();
                
            if (handler != null)
                await handler.HandleExceptionAsync(lastException, context.TickerId, context.Type);
                
            await InternalTickerManager.UpdateTickerAsync(context, cancellationToken);
        }
            
            
        cancellationTokenSource.Dispose();
        TickerCancellationTokenManager.RemoveTickerCancellationToken(context.TickerId);
    }

    private async Task<bool> WaitForRetry(InternalFunctionContext context, CancellationToken cancellationToken,
        int attempt, CancellationTokenSource cancellationTokenSource)
    {
        if (attempt == 0) 
            return false;

        if (attempt >= context.Retries)
            return true;

        context.SetProperty(x => x.RetryCount, attempt + 1);
            
        await InternalTickerManager.UpdateTickerAsync(context, cancellationToken);

        context.ResetUpdateProps();
            
        var retryInterval = (context.RetryIntervals?.Length > 0)
            ? (attempt < context.RetryIntervals.Length
                ? context.RetryIntervals[attempt]
                : context.RetryIntervals[^1])
            : 30;

        await Task.Delay(TimeSpan.FromSeconds(retryInterval), cancellationTokenSource.Token);

        return false;
    }

    private static Exception GetRootException(Exception ex)
    {
        while (ex.InnerException != null)
            ex = ex.InnerException;
        return ex;
    }

    private static string SerializeException(Exception ex)
    {
        var rootException = GetRootException(ex);
        var stackTrace = new StackTrace(rootException, true);
        var frame = stackTrace.GetFrame(0);

        return JsonSerializer.Serialize(new ExceptionDetailClassForSerialization
        {
            Message = ex.Message,
            StackTrace = frame?.ToString() ?? rootException.StackTrace
        });
    }
    
    private static bool ShouldRunChild(InternalFunctionContext childContext, TickerStatus parentStatus)
    {
        return childContext.RunCondition switch
        {
            RunCondition.InProgress => parentStatus == TickerStatus.InProgress,
            RunCondition.OnSuccess => parentStatus is TickerStatus.Done or TickerStatus.DueDone,
            RunCondition.OnFailure => parentStatus == TickerStatus.Failed,
            RunCondition.OnCancelled => parentStatus == TickerStatus.Cancelled,
            RunCondition.OnFailureOrCancelled => parentStatus is TickerStatus.Failed or TickerStatus.Cancelled,
            RunCondition.OnAnyCompletedStatus => parentStatus is TickerStatus.Done or TickerStatus.DueDone or TickerStatus.Failed or TickerStatus.Cancelled,
            _ => false
        };
    }
}