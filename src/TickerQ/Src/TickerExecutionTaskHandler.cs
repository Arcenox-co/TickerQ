using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using TickerQ.Exceptions;
using TickerQ.Utilities;
using TickerQ.Utilities.Base;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Instrumentation;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Models;

namespace TickerQ;

internal class TickerExecutionTaskHandler
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ITickerClock _clock;
    private readonly ITickerQInstrumentation _tickerQInstrumentation;
    private readonly IInternalTickerManager _internalTickerManager;

    public TickerExecutionTaskHandler(IServiceProvider serviceProvider, ITickerClock clock, ITickerQInstrumentation tickerQInstrumentation, IInternalTickerManager internalTickerManager)
    {
        _serviceProvider = serviceProvider;
        _clock = clock;
        _tickerQInstrumentation = tickerQInstrumentation;
        _internalTickerManager = internalTickerManager;
    }

    public async Task ExecuteTaskAsync(InternalFunctionContext context, bool isDue, CancellationToken cancellationToken = default)
    {
        if (context.Type != TickerType.TimeTickerOccurrence)
        {
            await RunContextFunctionAsync(context, isDue, cancellationToken);
            return;
        }

        var childrenToRunAfter = new InternalFunctionContext[5];
        var tasksToRunNow = new Task[6];

        var childrenToRunAfterCount = 0;
        var tasksToRunNowCount = 0;

        var hasChildren = context.TimeTickerChildren.Count > 0;

        // Add parent task
        tasksToRunNow[tasksToRunNowCount++] = RunContextFunctionAsync(context, isDue, cancellationToken);

        if (hasChildren)
        {
            // Process children - separate InProgress from others
            for (var i = 0; i < context.TimeTickerChildren.Count; i++)
            {
                var child = context.TimeTickerChildren[i];
                
                if (child.CachedDelegate != null)
                {
                    if (child.RunCondition == RunCondition.InProgress)
                        tasksToRunNow[tasksToRunNowCount++] = SafeRecursiveExecution(child, isDue, cancellationToken);
                    else
                    {
                        childrenToRunAfter[childrenToRunAfterCount++] = child;
                    }
                }
            }
        }
        
        // Wait for concurrent tasks (parent + InProgress children)
        await Task.WhenAll(tasksToRunNow.AsSpan(0, tasksToRunNowCount).ToArray());

        // Process deferred children after parent completion
        if (childrenToRunAfterCount > 0)
        {
            var childrenToSkip = new List<InternalFunctionContext>(30); // Pre-sized for performance
            var childrenToRunAfterTask = new Task[childrenToRunAfterCount];
            
            var taskCount = 0;
            
            for (var i = 0; i < childrenToRunAfterCount; i++)
            {
                var child = childrenToRunAfter[i];
                
                if (child.CachedDelegate != null)
                {
                    if (ShouldRunChild(child, context.Status))
                    {
                        childrenToRunAfterTask[taskCount++] = SafeRecursiveExecution(child, isDue, cancellationToken);
                    }
                    else
                    {
                        _tickerQInstrumentation.LogJobSkipped(
                            child.TickerId,
                            child.FunctionName,
                            $"Condition {child.RunCondition} not met (Parent status: {context.Status})"
                        );
                        child.ParentId = context.TickerId;
                        childrenToSkip.Add(child);

                        // Recursively gather all descendants to skip
                        GatherDescendantsToSkip(child, childrenToSkip);
                    }
                }
            }

            // Bulk update skipped children
            if (childrenToSkip.Count > 0)
                await _internalTickerManager.UpdateSkipTimeTickersWithUnifiedContextAsync(
                    childrenToSkip.ToArray(), cancellationToken);
            
            // Wait for deferred tasks
            if (taskCount > 0)
                await Task.WhenAll(childrenToRunAfterTask.AsSpan(0, taskCount).ToArray());
        }
    }

    private async Task RunContextFunctionAsync(InternalFunctionContext context, bool isDue, CancellationToken cancellationToken, bool isChild = false)
    {
        // Start OpenTelemetry activity for the entire job execution
        using var jobActivity = _tickerQInstrumentation.StartJobActivity($"tickerq.job.execute.{context.Type.ToString().ToLowerInvariant()}", context);
        
        // Add additional tags to the activity
        jobActivity?.SetTag("tickerq.job.is_due", isDue);
        jobActivity?.SetTag("tickerq.job.is_child", isChild);
        
        // Log job enqueued/started (using the available method)
        _tickerQInstrumentation.LogJobEnqueued(context.Type.ToString(), context.FunctionName, context.TickerId, "ExecutionTaskHandler");
        
        context.SetProperty(x => x.Status, TickerStatus.InProgress);

        if (isChild)
            await _internalTickerManager.UpdateTickerAsync(context, cancellationToken);

        var stopWatch = new Stopwatch();
        var cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // IMPORTANT: Register the ticker FIRST, before creating the SkipIfAlreadyRunningAction callback
        // This ensures the current occurrence is properly tracked when the callback checks for siblings
        TickerCancellationTokenManager.AddTickerCancellationToken(cancellationTokenSource, context, isDue);

        var tickerFunctionContext = new TickerFunctionContext
        {
            FunctionName = context.FunctionName,
            Id = context.TickerId,
            Type = context.Type,
            IsDue = isDue,
            ScheduledFor = context.ExecutionTime,
            RequestCancelOperationAction = () => cancellationTokenSource.Cancel(),
            CronOccurrenceOperations = new CronOccurrenceOperations
            {
                SkipIfAlreadyRunningAction = () =>
                {
                    if (context.Type == TickerType.TimeTicker)
                        return;

                    // Check for other running occurrences of the same parent (excluding self)
                    // Since we're already registered, we need to exclude ourselves from the check
                    var isRunning = context.ParentId.HasValue &&
                                    TickerCancellationTokenManager.IsParentRunningExcludingSelf(context.ParentId.Value, context.TickerId);

                    if (isRunning)
                        throw new TerminateExecutionException("Another CronOccurrence is already running!");
                },
            }
        };

        Exception lastException = null;
        var success = false;

        for (var attempt = context.RetryCount; attempt <= context.Retries; attempt++)
        {
            tickerFunctionContext.RetryCount = attempt;
            
            // Update activity with current attempt information
            jobActivity?.SetTag("tickerq.job.current_attempt", attempt + 1);

            try
            {
                if (await WaitForRetry(context, cancellationToken, attempt, cancellationTokenSource)) break;

                stopWatch.Start();

                // Create service scope - will be disposed automatically via await using
                await using var scope = _serviceProvider.CreateAsyncScope();
                tickerFunctionContext.SetServiceScope(scope);
                await context.CachedDelegate(cancellationTokenSource.Token, scope.ServiceProvider, tickerFunctionContext);

                success = true;
                context.RetryCount = attempt;
                break;
            }
            catch (TaskCanceledException ex)
            {
                context.SetProperty(x => x.Status, TickerStatus.Cancelled)
                    .SetProperty(x => x.ExecutedAt, _clock.UtcNow)
                    .SetProperty(x => x.ElapsedTime, stopWatch.ElapsedMilliseconds)
                    .SetProperty(x => x.ExceptionDetails, SerializeException(ex));
                
                // Add cancellation tags to activity
                jobActivity?.SetTag("tickerq.job.final_status", context.Status.ToString());
                jobActivity?.SetTag("tickerq.job.cancellation_reason", "Task was cancelled");
                
                // Log job cancelled
                _tickerQInstrumentation.LogJobCancelled(context.TickerId, context.FunctionName, "Task was cancelled");

                if (_serviceProvider.GetService(typeof(ITickerExceptionHandler)) is ITickerExceptionHandler handler)
                    await handler.HandleCanceledExceptionAsync(ex, context.TickerId, context.Type);

                await _internalTickerManager.UpdateTickerAsync(context, cancellationToken);
                
                // Clean up and exit early on cancellation
                cancellationTokenSource?.Dispose();
                TickerCancellationTokenManager.RemoveTickerCancellationToken(context.TickerId);
                return;
            }
            catch (TerminateExecutionException ex)
            {
                context.SetProperty(x => x.Status, ex.Status)
                    .SetProperty(x => x.ExecutedAt, _clock.UtcNow)
                    .SetProperty(x => x.ElapsedTime, stopWatch.ElapsedMilliseconds);

                if (ex.InnerException != null)
                {
                    context.SetProperty(x => x.ExceptionDetails, ex.InnerException.Message);
                    jobActivity?.SetTag("tickerq.job.skip_reason", ex.InnerException.Message);
                }
                else
                {
                    context.SetProperty(x => x.ExceptionDetails, ex.Message);
                    jobActivity?.SetTag("tickerq.job.skip_reason", ex.Message);
                }
                
                // Add skip tags to activity
                jobActivity?.SetTag("tickerq.job.final_status", context.Status.ToString());
                
                // Log job skipped
                _tickerQInstrumentation.LogJobSkipped(context.TickerId, context.FunctionName, ex.Message);

                await _internalTickerManager.UpdateTickerAsync(context, cancellationToken);
                
                // Clean up and exit early on termination
                cancellationTokenSource?.Dispose();
                TickerCancellationTokenManager.RemoveTickerCancellationToken(context.TickerId);
                return;
            }
            catch (Exception ex)
            {
                lastException = ex;
            }
        }

        stopWatch.Stop();

        context.SetProperty(x => x.ElapsedTime, stopWatch.ElapsedMilliseconds)
            .SetProperty(x => x.ExecutedAt, _clock.UtcNow);

        if (success)
        {
            context.SetProperty(x => x.Status, isDue ? TickerStatus.DueDone : TickerStatus.Done);
            
            // Add success tags to activity
            jobActivity?.SetTag("tickerq.job.final_status", context.Status.ToString());
            jobActivity?.SetTag("tickerq.job.final_retry_count", context.RetryCount);
            
            // Log job completed successfully
            _tickerQInstrumentation.LogJobCompleted(context.TickerId, context.FunctionName, stopWatch.ElapsedMilliseconds, true);

            await _internalTickerManager.UpdateTickerAsync(context, cancellationToken);
        }
        else if (lastException != null)
        {
            context.SetProperty(x => x.Status, TickerStatus.Failed)
                .SetProperty(x => x.ExceptionDetails, SerializeException(lastException));
            
            // Add failure tags to activity
            jobActivity?.SetTag("tickerq.job.final_status", context.Status.ToString());
            jobActivity?.SetTag("tickerq.job.final_retry_count", context.RetryCount);
            jobActivity?.SetTag("tickerq.job.error_type", lastException.GetType().Name);
            
            // Log job failed
            _tickerQInstrumentation.LogJobFailed(context.TickerId, context.FunctionName, lastException, context.RetryCount);
            _tickerQInstrumentation.LogJobCompleted(context.TickerId, context.FunctionName, stopWatch.ElapsedMilliseconds, false);

            var handler = _serviceProvider.GetService(typeof(ITickerExceptionHandler)) as ITickerExceptionHandler;

            if (handler != null)
                await handler.HandleExceptionAsync(lastException, context.TickerId, context.Type);

            await _internalTickerManager.UpdateTickerAsync(context, cancellationToken);
        }


        // IMPORTANT: Always dispose CancellationTokenSource to prevent memory leaks
        cancellationTokenSource?.Dispose();
        TickerCancellationTokenManager.RemoveTickerCancellationToken(context.TickerId);
    }

    private async Task<bool> WaitForRetry(InternalFunctionContext context, CancellationToken cancellationToken,
        int attempt, CancellationTokenSource cancellationTokenSource)
    {
        if (attempt == 0)
            return false;

        if (attempt > context.Retries)
            return true;

        context.SetProperty(x => x.RetryCount, attempt);

        await _internalTickerManager.UpdateTickerAsync(context, cancellationToken);

        context.ResetUpdateProps();

        var retryInterval = (context.RetryIntervals?.Length > 0)
            ? (attempt - 1 < context.RetryIntervals.Length
                ? context.RetryIntervals[attempt - 1]
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
            RunCondition.OnAnyCompletedStatus => parentStatus is TickerStatus.Done or TickerStatus.DueDone
                or TickerStatus.Failed or TickerStatus.Cancelled,
            _ => false
        };
    }

    private static void GatherDescendantsToSkip(InternalFunctionContext parent, List<InternalFunctionContext> skipList)
    {
        if (parent.TimeTickerChildren == null || parent.TimeTickerChildren.Count == 0)
            return;

        foreach (var child in parent.TimeTickerChildren)
        {
            skipList.Add(child);
            
            // Recursively gather grandchildren
            GatherDescendantsToSkip(child, skipList);
        }
    }

    private Task SafeRecursiveExecution(InternalFunctionContext context, bool isDue, CancellationToken cancellationToken = default)
    {
        try
        {
            return ExecuteTaskAsync(context, isDue, cancellationToken);
        }
        catch
        {
            // ignored
        }

        return Task.CompletedTask;
    }
}
