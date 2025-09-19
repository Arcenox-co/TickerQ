using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using TickerQ.Base;
using TickerQ.Exceptions;
using TickerQ.Utilities;
using TickerQ.Utilities.Base;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Instrumentation;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ;

internal class TickerHost : BaseTicker, IDisposable
{
    private readonly SemaphoreSlim _semaphoreSlim = new(1, 1);

    public TickerHost(IServiceProvider serviceProvider, TickerExecutionContext executionContext, ITickerClock clock, ITickerQInstrumentation tickerQInstrumentation)
        : base(executionContext, clock, serviceProvider, tickerQInstrumentation)
    {
    }

    protected override async Task OnTimerTick(InternalFunctionContext[] functions, bool dueDone,
        CancellationToken cancellationToken)
    {
        await _semaphoreSlim.WaitAsync(cancellationToken);

        try
        {
            foreach (var context in functions)
            {
                if (context.CachedDelegate == null) continue;

                if (context.CachedPriority == TickerTaskPriority.LongRunning)
                {
                    var context1 = context;
                    _ = Task.Factory.StartNew(
                        async () => await ExecuteTaskAsync(context1, context1.CachedDelegate, dueDone,
                            cancellationToken),
                        TaskCreationOptions.LongRunning).Unwrap();
                }
                else
                {
                    var taskDetails = Task.Factory.StartNew(
                        async () => await ExecuteTaskAsync(context, context.CachedDelegate, dueDone, cancellationToken),
                        cancellationToken, TaskCreationOptions.DenyChildAttach, TickerTaskScheduler).Unwrap();

                    TickerTaskScheduler.SetQueuedTaskPriority(taskDetails.Id, context.CachedPriority);
                }
            }
            
            TickerTaskScheduler.ExecutePriorityTasks();
        }
        finally
        {
            _semaphoreSlim.Release();
        }
    }

    private async Task ExecuteTaskAsync(InternalFunctionContext context, TickerFunctionDelegate delegateFunction,
        bool isDue, CancellationToken cancellationToken = default)
    {
        TickerQInstrumentation.LogJobStarted(context.TickerId, context.FunctionName, context.Type.ToString());
        
        var stopwatch = Stopwatch.StartNew();
        var hasChildren = context.TimeTickerChildren.Count > 0;
        
        var parentTask = RunContextFunctionAsync(context, delegateFunction, isDue, cancellationToken);

        if (hasChildren)
        {
            var functionsToRunImmediately = context.TimeTickerChildren
                .Where(x => x.RunCondition == RunCondition.InProgress)
                .Select(async childContext =>
                {
                    if (childContext.CachedDelegate != null)
                        await RunContextFunctionAsync(childContext, childContext.CachedDelegate, isDue, cancellationToken, true);
                })
                .Where(x => x != null).ToList();
            
            functionsToRunImmediately.Add(parentTask);
            
            await Task.WhenAll(functionsToRunImmediately);
        }
        else
            await parentTask;
            
        stopwatch.Stop();
        TickerQInstrumentation.LogJobCompleted(context.TickerId, context.FunctionName, stopwatch.ElapsedMilliseconds, true);

        if (hasChildren)
        {
            var childExecutions = context.TimeTickerChildren
                .Where(x => x.RunCondition != RunCondition.InProgress)
                .Where(x => ShouldRunChild(x, context.Status))
                .Select(async childContext =>
                {
                    if (childContext.CachedDelegate != null) 
                        await RunContextFunctionAsync(childContext, childContext.CachedDelegate, isDue, cancellationToken, true);
                }).ToArray();
            
            if(childExecutions.Length > 0)
                await Task.WhenAll(childExecutions);
        }
    }

    private async Task RunContextFunctionAsync(InternalFunctionContext context, TickerFunctionDelegate delegateFunction,
        bool isDue, CancellationToken cancellationToken, bool isChild = false)
    {
        var cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        TickerCancellationTokenManager.AddTickerCancellationToken(cancellationTokenSource, context, isDue);
        
        try
        {
            context.SetProperty(x => x.Status, TickerStatus.InProgress);

            if (isChild)
                await InternalTickerManager.UpdateTickerAsync(context, cancellationToken);

            var stopWatch = new Stopwatch();

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
                        if (context.Type == TickerType.TimeTicker)
                            return;

                        var isRunning = context.ParentId.HasValue &&
                                        TickerCancellationTokenManager.IsParentRunning(context.ParentId.Value);

                        if (isRunning)
                            throw new TerminateExecutionException("Another CronOccurrence is already running!");
                    }
                }
            };

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
                    tickerFunctionContext.SetServiceScope(scope);
                    await delegateFunction(cancellationTokenSource.Token, scope.ServiceProvider, tickerFunctionContext);

                    success = true;
                    context.RetryCount = attempt;
                    break;
                }
                catch (TaskCanceledException ex)
                {
                    TickerQInstrumentation.LogJobCancelled(context.TickerId, context.FunctionName, ex.Message);

                    context.SetProperty(x => x.Status, TickerStatus.Cancelled)
                        .SetProperty(x => x.ExecutedAt, Clock.UtcNow)
                        .SetProperty(x => x.ElapsedTime, stopWatch.ElapsedMilliseconds)
                        .SetProperty(x => x.ExceptionDetails, ToShortExceptionMessage(context.TickerId, lastException));

                    var handler = ServiceProvider.GetService<ITickerExceptionHandler>();

                    if (handler != null)
                        await handler.HandleCanceledExceptionAsync(ex, context.TickerId, context.Type);

                    await InternalTickerManager.UpdateTickerAsync(context, cancellationToken);
                }
                catch (TerminateExecutionException ex)
                {
                    TickerQInstrumentation.LogJobSkipped(context.TickerId, context.FunctionName, ex.Message);

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
                    TickerQInstrumentation.LogJobFailed(context.TickerId, context.FunctionName, ex, attempt);
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
                    .SetProperty(x => x.ExceptionDetails, ToShortExceptionMessage(context.TickerId, lastException));

                var handler = ServiceProvider.GetService<ITickerExceptionHandler>();

                if (handler != null)
                    await handler.HandleExceptionAsync(lastException, context.TickerId, context.Type);

                await InternalTickerManager.UpdateTickerAsync(context, cancellationToken);
            }
        }
        finally
        {
            cancellationTokenSource.Dispose();
            TickerCancellationTokenManager.RemoveTickerCancellationToken(context.TickerId);
        }
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

    private static string ToShortExceptionMessage(Guid correlationId, Exception ex)
    { 
        var msg = ex.Message.Length > 500 ? ex.Message[..500] + "..." : ex.Message;
        return $"CorrelationId={correlationId} | Exception: {msg}";
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

    public void Dispose()
    {
        TickerTaskScheduler?.Dispose();
        _semaphoreSlim?.Dispose();
    }
}