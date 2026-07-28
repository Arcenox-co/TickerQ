using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using TickerQ.Exceptions;
using TickerQ.Utilities;
using TickerQ.Utilities.Exceptions;
using TickerQ.Utilities.Base;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Infrastructure;
using TickerQ.Utilities.Instrumentation;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Models;

namespace TickerQ;

internal class TickerExecutionTaskHandler : ITickerExecutionTaskHandler
{
    private enum ExecutionMode
    {
        Scheduler,
        Worker
    }

    // Cached lowercase TickerType names to avoid per-call ToString + ToLowerInvariant allocations
    private static readonly Dictionary<TickerType, string> TickerTypeNamesLower =
        Enum.GetValues<TickerType>().ToDictionary(t => t, t => t.ToString().ToLowerInvariant());

    private readonly IServiceProvider _serviceProvider;
    private readonly ITickerClock _clock;
    private readonly ITickerQInstrumentation _tickerQInstrumentation;
    private readonly IInternalTickerManager _internalTickerManager;
    private readonly SchedulerOptionsBuilder _schedulerOptions;
    private readonly ITickerQFailureNotifier _failureNotifier;
    private readonly Func<string, TickerFunctionDescriptor> _descriptorResolver;

    public TickerExecutionTaskHandler(IServiceProvider serviceProvider, ITickerClock clock, ITickerQInstrumentation tickerQInstrumentation, IInternalTickerManager internalTickerManager, SchedulerOptionsBuilder schedulerOptions, ITickerQFailureNotifier failureNotifier)
        : this(
            serviceProvider, clock, tickerQInstrumentation, internalTickerManager, schedulerOptions, failureNotifier,
            functionName => TickerFunctionProvider.TickerFunctionDescriptors.TryGetValue(functionName, out var descriptor)
                ? descriptor
                : null)
    {
    }

    internal TickerExecutionTaskHandler(
        IServiceProvider serviceProvider,
        ITickerClock clock,
        ITickerQInstrumentation tickerQInstrumentation,
        IInternalTickerManager internalTickerManager,
        SchedulerOptionsBuilder schedulerOptions,
        ITickerQFailureNotifier failureNotifier,
        Func<string, TickerFunctionDescriptor> descriptorResolver)
    {
        _serviceProvider = serviceProvider;
        _clock = clock;
        _tickerQInstrumentation = tickerQInstrumentation;
        _internalTickerManager = internalTickerManager;
        _schedulerOptions = schedulerOptions;
        _failureNotifier = failureNotifier;
        _descriptorResolver = descriptorResolver ?? throw new ArgumentNullException(nameof(descriptorResolver));
    }

    private void NotifyFailure(InternalFunctionContext context, string kind, string reason)
        => _failureNotifier.Notify(new TickerFailureEvent
        {
            Kind = kind,
            TickerId = context.TickerId,
            Function = context.FunctionName,
            TickerType = context.Type.ToString(),
            Reason = reason,
            RetryCount = context.RetryCount,
            Retries = context.Retries,
            OccurredAtUtc = _clock.UtcNow,
            Node = _schedulerOptions.NodeIdentifier,
        });

    private void TryNotifyFailure(InternalFunctionContext context, string kind, string reason)
    {
        try
        {
            NotifyFailure(context, kind, reason);
        }
        catch (Exception ex)
        {
            _tickerQInstrumentation.LogJobFailed(
                context.TickerId, context.FunctionName, ex, context.RetryCount);
        }
    }

    /// <summary>
    /// Effective per-attempt timeout for a ticker: its own TimeoutSeconds wins
    /// (&lt;= 0 disables explicitly), otherwise the global default applies.
    /// </summary>
    private TimeSpan? GetEffectiveTimeout(InternalFunctionContext context)
    {
        if (context.TimeoutSeconds is { } seconds)
            return seconds > 0 ? TimeSpan.FromSeconds(seconds) : (TimeSpan?)null;

        return _schedulerOptions.DefaultExecutionTimeout is { } fallback && fallback > TimeSpan.Zero
            ? fallback
            : (TimeSpan?)null;
    }

    public Task ExecuteTaskAsync(InternalFunctionContext context, bool isDue, CancellationToken cancellationToken = default)
        => ExecuteRegisteredTaskAsync(context, isDue, registeredSource: null, cancellationToken);

    public async Task<TickerWorkerExecutionResult> ExecuteWorkerTaskAsync(
        InternalFunctionContext context, bool isDue, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.ChainRootId = context.TickerId;
        await RunContextFunctionAsync(
            context, isDue, cancellationToken, isChild: false,
            preRegisteredSource: null, mode: ExecutionMode.Worker).ConfigureAwait(false);
        return new TickerWorkerExecutionResult(
            context.Status,
            context.Status is TickerStatus.Done or TickerStatus.DueDone ? null : context.ExceptionDetails,
            context.ResultEnvelope);
    }

    public async Task ExecuteRegisteredTaskAsync(InternalFunctionContext context, bool isDue,
        CancellationTokenSource registeredSource, CancellationToken cancellationToken = default)
        => await ExecuteTreeAsync(context, isDue, registeredSource, cancellationToken, isChild: false);

    private async Task ExecuteTreeAsync(
        InternalFunctionContext context,
        bool isDue,
        CancellationTokenSource registeredSource,
        CancellationToken cancellationToken,
        bool isChild,
        Guid? chainRootId = null)
    {
        context.ChainRootId = chainRootId ?? context.TickerId;

        if (context.Type == TickerType.CronTickerOccurrence)
        {
            // Root occurrence: reuse the acquisition-time registration if the caller supplied one.
            await RunContextFunctionAsync(
                context, isDue, cancellationToken, isChild, preRegisteredSource: registeredSource);
            return;
        }

        var childCount = context.TimeTickerChildren.Count;
        var childrenToRunAfter = new InternalFunctionContext[childCount];
        var tasksToRunNow = new Task[childCount + 1];

        var childrenToRunAfterCount = 0;
        var tasksToRunNowCount = 0;

        var hasChildren = context.TimeTickerChildren.Count > 0;

        // Add parent (root) task — reuses the acquisition-time registration when supplied; children
        // below always self-register (preRegisteredSource stays null for them).
        tasksToRunNow[tasksToRunNowCount++] =
            RunContextFunctionAsync(
                context, isDue, cancellationToken, isChild, preRegisteredSource: registeredSource);

        if (hasChildren)
        {
            // Process children - separate InProgress from others
            for (var i = 0; i < context.TimeTickerChildren.Count; i++)
            {
                var child = context.TimeTickerChildren[i];
                
                if (child.CachedDelegate != null)
                {
                    if (child.RunCondition == RunCondition.InProgress)
                        tasksToRunNow[tasksToRunNowCount++] = SafeRecursiveExecution(
                            child, context.ChainRootId.Value, isDue, cancellationToken);
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
                        childrenToRunAfterTask[taskCount++] = SafeRecursiveExecution(
                            child, context.ChainRootId.Value, isDue, cancellationToken);
                    }
                    else
                    {
                        _tickerQInstrumentation.LogJobSkipped(
                            child.TickerId,
                            child.FunctionName,
                            $"Condition {child.RunCondition} not met (Parent status: {context.Status})"
                        );
                        child.ParentId = context.TickerId;
                        child.ChainRootId = context.ChainRootId;
                        childrenToSkip.Add(child);

                        // Recursively gather all descendants to skip
                        GatherDescendantsToSkip(child, context.ChainRootId.Value, childrenToSkip);
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

    private async Task RunContextFunctionAsync(
        InternalFunctionContext context,
        bool isDue,
        CancellationToken cancellationToken,
        bool isChild = false,
        CancellationTokenSource preRegisteredSource = null,
        ExecutionMode mode = ExecutionMode.Scheduler)
    {
        if (preRegisteredSource != null)
        {
            // Root registered at acquisition time by the scheduler: reuse its source and leave
            // removal/disposal to the owner (the scheduler delegate's finally) — running it here too
            // would double-remove and risk disposing the source out from under the owner.
            await RunContextFunctionCoreAsync(
                context, isDue, cancellationToken, preRegisteredSource, isChild, mode).ConfigureAwait(false);
            return;
        }

        // Legacy/direct call (and every child): self-register atomically and own the single cleanup.
        var cancellationTokenSource = TickerCancellationTokenManager.TryRegisterAcquired(
            context, isDue, out var generationConflict, cancellationToken);
        if (cancellationTokenSource == null)
        {
            if (generationConflict && mode == ExecutionMode.Scheduler)
                await _internalTickerManager.ReleaseAcquiredResources([context], CancellationToken.None)
                    .ConfigureAwait(false);
            return; // duplicate generation stays with its existing owner; conflicts retry from persistence
        }

        try
        {
            await RunContextFunctionCoreAsync(
                context, isDue, cancellationToken, cancellationTokenSource, isChild, mode).ConfigureAwait(false);
        }
        finally
        {
            TickerCancellationTokenManager.RemoveTickerCancellationToken(
                new TickerExecutionKey(context.Type, context.TickerId), cancellationTokenSource);
        }
    }

    private async Task RunContextFunctionCoreAsync(
        InternalFunctionContext context,
        bool isDue,
        CancellationToken cancellationToken,
        CancellationTokenSource cancellationTokenSource,
        bool isChild,
        ExecutionMode mode)
    {
        var typeName = TickerTypeNamesLower.GetValueOrDefault(context.Type, context.Type.ToString().ToLowerInvariant());

        using var jobActivity = _tickerQInstrumentation.StartJobActivity($"tickerq.job.execute.{typeName}", context);

        jobActivity?.SetTag("tickerq.job.is_due", isDue);
        jobActivity?.SetTag("tickerq.job.is_child", isChild);

        _tickerQInstrumentation.LogJobEnqueued(typeName, context.FunctionName, context.TickerId, "ExecutionTaskHandler");
        
        context.SetProperty(x => x.Status, TickerStatus.InProgress);

        if (isChild && mode == ExecutionMode.Scheduler)
            await _internalTickerManager.UpdateTickerAsync(context, CancellationToken.None);

        if (TryGetContractDriftReason(context, out var driftReason))
        {
            var exception = new TickerValidatorException(driftReason);
            context.SetProperty(x => x.Status, TickerStatus.Failed)
                .SetProperty(x => x.ExecutedAt, _clock.UtcNow)
                .SetProperty(x => x.ElapsedTime, 0L)
                .SetProperty(x => x.ExceptionDetails, SerializeException(exception));

            _tickerQInstrumentation.LogJobFailed(
                context.TickerId, context.FunctionName, exception, context.RetryCount);
            TryNotifyFailure(context, "contract_drift", driftReason);
            if (mode == ExecutionMode.Scheduler)
                await _internalTickerManager.UpdateTickerAsync(context, CancellationToken.None).ConfigureAwait(false);
            return;
        }

        var stopWatch = new Stopwatch();
        // Total wall-clock from first attempt start through the final outcome,
        // including retry wait intervals. Persisted as ElapsedTime so the
        // dashboard's Duration column reflects the user's lived time, not just
        // the last attempt's CPU time.
        var totalStopWatch = Stopwatch.StartNew();

        // Fetch the direct parent's committed result (if any) before invoking the function so the
        // body can read it. Roots (no ParentId) and parents that published none yield null. A cron
        // occurrence's parent is the cron definition, which never publishes a result.
        var parentResult = context.ParentResultEnvelope ?? (context.ParentId.HasValue
            ? await _internalTickerManager
                .GetParentResultAsync(context.ParentId.Value, context.Type, CancellationToken.None)
                .ConfigureAwait(false)
            : null);

        var tickerFunctionContext = new TickerFunctionContext
        {
            FunctionName = context.FunctionName,
            Id = context.TickerId,
            ParentId = context.ParentId,
            Type = context.Type,
            IsDue = isDue,
            ScheduledFor = context.ExecutionTime,
            // Runtime-owned result sink the success path reads; shared by reference with the typed
            // context wrapper the source generator builds.
            ResultSink = new TickerResultSink(),
            ParentResultEnvelope = parentResult,
            // Forward retry config so delegates that defer execution to a different
            // process (e.g. the remote-dispatch delegate that ships work to the SDK)
            // can pass it along — without these the SDK would see Retries=0.
            Retries = context.Retries,
            RetryIntervals = context.RetryIntervals,
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

        var effectiveTimeout = GetEffectiveTimeout(context);
        // Qualified remote functions execute their retry policy inside the SDK. Core still
        // forwards Retries/RetryIntervals through TickerFunctionContext, but must dispatch
        // the remote delegate only once or both layers multiply the configured retries.
        var usesRemoteRetryPolicy = !string.IsNullOrEmpty(context.FunctionName) &&
                                    context.FunctionName.Contains('@');
        var finalAttempt = usesRemoteRetryPolicy ? context.RetryCount : context.Retries;

        for (var attempt = context.RetryCount; attempt <= finalAttempt; attempt++)
        {
            tickerFunctionContext.RetryCount = attempt;

            // Update activity with current attempt information
            jobActivity?.SetTag("tickerq.job.current_attempt", attempt + 1);

            CancellationTokenSource attemptCts = null;
            try
            {
                if (!usesRemoteRetryPolicy &&
                    await WaitForRetry(context, cancellationToken, attempt, cancellationTokenSource, mode)) break;

                stopWatch.Restart();

                // Discard any result an earlier failed attempt published so only the successful
                // attempt's result can be committed — intermediate retry outputs never leak.
                tickerFunctionContext.ResultSink.Reset();

                if (context.CachedDelegate is null)
                {
                    // Qualified function name (`name@nodeName`) with no
                    // matching delegate almost always means the SDK that
                    // owns it is currently offline — its functions get
                    // unregistered from TickerFunctionProvider when the
                    // worker stream drops. Surface as SdkOfflineSkipException
                    // so the run lands as Skipped (with the SDK-offline
                    // reason persisted) instead of burning user retries on
                    // something that can't run until the node is back.
                    if (!string.IsNullOrEmpty(context.FunctionName) && context.FunctionName.Contains('@'))
                    {
                        var node = context.FunctionName[(context.FunctionName.IndexOf('@') + 1)..];
                        throw new SdkOfflineSkipException(
                            $"SDK node '{node}' is offline (function '{context.FunctionName}' is not currently registered).");
                    }
                    throw new InvalidOperationException(
                        $"Ticker function '{context.FunctionName}' was not found in the registered functions. " +
                        "Ensure the function is properly decorated with [TickerFunction] attribute and the containing class is registered.");
                }

                // Create service scope - will be disposed automatically via await using
                await using var scope = _serviceProvider.CreateAsyncScope();
                tickerFunctionContext.SetServiceScope(scope);

                // Per-attempt cancellation: linked to the job's CTS (user cancel /
                // shutdown) and additionally fired by the execution timeout.
                attemptCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationTokenSource.Token);
                if (effectiveTimeout is { } timeout)
                    attemptCts.CancelAfter(timeout);

                // Ambient log scope: ILogger calls flowing on the function body's async
                // stack land in the in-memory log store the dashboard's log tail reads.
                using (TickerExecutionLogScope.Push(context.TickerId, context.FunctionName))
                {
                    var execTask = context.CachedDelegate(attemptCts.Token, scope.ServiceProvider, tickerFunctionContext);
                    var timeoutPending = false;

                    if (effectiveTimeout is { } t)
                    {
                        // Fire cooperative cancellation at timeout. If the delegate is still running
                        // after grace, report the critical pending state but retain its registration,
                        // renewable lease, and DI scope until the delegate actually exits.
                        var grace = _schedulerOptions.TimeoutGracePeriod < TimeSpan.Zero
                            ? TimeSpan.Zero
                            : _schedulerOptions.TimeoutGracePeriod;
                        using var graceDelayCts = new CancellationTokenSource();
                        var winner = await Task.WhenAny(execTask, Task.Delay(t + grace, graceDelayCts.Token));
                        if (winner != execTask)
                        {
                            timeoutPending = true;
                            _tickerQInstrumentation.LogJobTimeoutPending(
                                context.TickerId, context.FunctionName, t, grace);
                            jobActivity?.SetTag("tickerq.job.timeout_pending", true);
                        }
                        else
                        {
                            graceDelayCts.Cancel();
                        }
                    }

                    var timeoutElapsed = effectiveTimeout != null &&
                                         attemptCts.IsCancellationRequested &&
                                         !cancellationTokenSource.IsCancellationRequested;

                    try
                    {
                        await execTask;
                    }
                    catch (Exception) when (timeoutPending || timeoutElapsed)
                    {
                        // The delegate has now exited. Its eventual exception is observed, but timeout
                        // remains the authoritative terminal outcome and must not consume retry budget.
                    }

                    if (timeoutPending || timeoutElapsed)
                    {
                        throw new TickerExecutionTimeoutException(
                            $"Exceeded execution timeout of {effectiveTimeout.Value.TotalSeconds:0.###}s; " +
                            "the function has now exited after timeout cancellation.");
                    }
                }

                success = true;
                context.RetryCount = attempt;
                break;
            }
            catch (TickerExecutionTimeoutException ex)
            {
                await HandleExecutionTimeoutAsync(context, jobActivity, ex.Message, stopWatch, cancellationToken, mode);
                return;
            }
            catch (OperationCanceledException) when (attemptCts is { IsCancellationRequested: true } &&
                                                     !cancellationTokenSource.IsCancellationRequested)
            {
                // Only the CancelAfter could have fired the attempt token without the
                // parent — the function honored cancellation within the grace window.
                await HandleExecutionTimeoutAsync(context, jobActivity,
                    $"Exceeded execution timeout of {effectiveTimeout?.TotalSeconds ?? 0:0}s.",
                    stopWatch, cancellationToken, mode);
                return;
            }
            catch (TaskCanceledException ex)
            {
                context.SetProperty(x => x.Status, TickerStatus.Cancelled)
                    .SetProperty(x => x.ExecutedAt, _clock.UtcNow)
                    .SetProperty(x => x.ElapsedTime, stopWatch.ElapsedMilliseconds)
                    .SetProperty(x => x.ExceptionDetails, SerializeException(ex));
                
                // Record only the non-sensitive terminal status on the activity.
                jobActivity?.SetTag("tickerq.job.final_status", context.Status.ToString());
                
                // Log job cancelled
                _tickerQInstrumentation.LogJobCancelled(context.TickerId, context.FunctionName, "Task was cancelled");

                if (_serviceProvider.GetService(typeof(ITickerExceptionHandler)) is ITickerExceptionHandler handler)
                    await handler.HandleCanceledExceptionAsync(ex, context.TickerId, context.Type);

                if (mode == ExecutionMode.Scheduler)
                    await _internalTickerManager.UpdateTickerAsync(context, CancellationToken.None);
                
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

                }
                else
                {
                    context.SetProperty(x => x.ExceptionDetails, ex.Message);

                }

                // Add skip tags to activity
                jobActivity?.SetTag("tickerq.job.final_status", context.Status.ToString());

                // Log job skipped
                _tickerQInstrumentation.LogJobSkipped(context.TickerId, context.FunctionName, ex.Message);

                if (mode == ExecutionMode.Scheduler)
                    await _internalTickerManager.UpdateTickerAsync(context, CancellationToken.None);

                return;
            }
            catch (SdkOfflineSkipException ex)
            {
                // SDK node is offline and the transport-retry window
                // expired. Don't burn the user's Retries budget — retrying
                // is pointless while the node is down. Land on Skipped with
                // the reason persisted so the dashboard can distinguish
                // "would have run but SDK was offline" from "ran and broke".
                context.SetProperty(x => x.Status, TickerStatus.Skipped)
                    .SetProperty(x => x.ExecutedAt, _clock.UtcNow)
                    .SetProperty(x => x.ElapsedTime, stopWatch.ElapsedMilliseconds)
                    .SetProperty(x => x.ExceptionDetails, ex.Message);

                jobActivity?.SetTag("tickerq.job.final_status", context.Status.ToString());

                _tickerQInstrumentation.LogJobSkipped(context.TickerId, context.FunctionName, ex.Message);

                if (mode == ExecutionMode.Scheduler)
                    await _internalTickerManager.UpdateTickerAsync(context, CancellationToken.None);
                return;
            }
            catch (Exception ex)
            {
                lastException = ex;

                context.SetProperty(x => x.ExceptionDetails, SerializeException(ex));

                if (_serviceProvider.GetService(typeof(ITickerExceptionHandler)) is ITickerExceptionHandler handler)
                    await handler.HandleExceptionAsync(ex, context.TickerId, context.Type);

                // Per-attempt failure log so retries are visible in trace/logs.
                // The terminal failure (last attempt that exhausts retries) is logged
                // by the post-loop block below as Error — guard with `attempt < Retries`
                // so non-terminal attempts log as Warning instead and the final attempt
                // isn't double-logged.
                if (attempt < finalAttempt)
                    _tickerQInstrumentation.LogJobAttemptFailed(
                        context.TickerId, context.FunctionName, attempt, context.Retries, stopWatch.ElapsedMilliseconds, ex);

                // Persist retry progress only for scheduler-owned local retries. Remote retries are
                // completed inside the worker and their immutable terminal outcome is committed once
                // by the scheduler below; worker mode never mutates durable scheduler state.
                if (mode == ExecutionMode.Scheduler && !usesRemoteRetryPolicy)
                    await _internalTickerManager.UpdateTickerAsync(context, CancellationToken.None);

                context.ResetUpdateProps();
            }
            finally
            {
                attemptCts?.Dispose();
            }
        }

        stopWatch.Stop();
        totalStopWatch.Stop();

        // Persist the *total* time (first attempt start → final outcome, includes
        // retry waits). The per-attempt stopWatch is still used for instrumentation
        // ("attempt X failed in 200ms") but doesn't reflect user-lived duration.
        context.SetProperty(x => x.ElapsedTime, totalStopWatch.ElapsedMilliseconds)
            .SetProperty(x => x.ExecutedAt, _clock.UtcNow);

        if (success)
        {
            context.SetProperty(x => x.Status, isDue ? TickerStatus.DueDone : TickerStatus.Done);

            // Attach the published result ONLY on the successful terminal write so it commits
            // atomically with the Done/DueDone status, before any children are released/queued.
            // Always stage the optional envelope on success. Null means absence and deliberately clears
            // any result from an earlier successful run; a present JSON "null" remains an envelope.
            context.SetProperty(x => x.ResultEnvelope,
                tickerFunctionContext.ResultSink.HasResult ? tickerFunctionContext.ResultSink.Envelope : null);

            // Add success tags to activity
            jobActivity?.SetTag("tickerq.job.final_status", context.Status.ToString());
            jobActivity?.SetTag("tickerq.job.final_retry_count", context.RetryCount);
            jobActivity?.SetStatus(ActivityStatusCode.Ok);
            
            // Log job completed successfully
            _tickerQInstrumentation.LogJobCompleted(context.TickerId, context.FunctionName, totalStopWatch.ElapsedMilliseconds, true);

            if (mode == ExecutionMode.Scheduler)
                await _internalTickerManager.UpdateTickerAsync(context, CancellationToken.None);
        }
        else if (lastException != null)
        {
            context.SetProperty(x => x.Status, TickerStatus.Failed)
                .SetProperty(x => x.ExceptionDetails, SerializeException(lastException));
            
            // Add failure tags to activity
            jobActivity?.SetTag("tickerq.job.final_status", context.Status.ToString());
            jobActivity?.SetTag("tickerq.job.final_retry_count", context.RetryCount);
            jobActivity?.SetTag("tickerq.job.error_type", lastException.GetType().Name);
            jobActivity?.SetStatus(ActivityStatusCode.Error);
            
            // Log job failed
            _tickerQInstrumentation.LogJobFailed(context.TickerId, context.FunctionName, lastException, context.RetryCount);
            _tickerQInstrumentation.LogJobCompleted(context.TickerId, context.FunctionName, totalStopWatch.ElapsedMilliseconds, false);

            if (mode == ExecutionMode.Scheduler)
                await _internalTickerManager.UpdateTickerAsync(context, CancellationToken.None);

            TryNotifyFailure(context, "failed", lastException.Message);
        }
    }

    /// <summary>
    /// Terminal outcome for a timed-out execution: Cancelled with the timeout
    /// reason persisted. Timeouts don't consume the retry budget — a job that
    /// hung once is likely to hang again; opt into re-runs explicitly.
    /// </summary>
    private async Task HandleExecutionTimeoutAsync(InternalFunctionContext context, Activity jobActivity,
        string reason, Stopwatch stopWatch, CancellationToken cancellationToken, ExecutionMode mode)
    {
        _ = cancellationToken; // deliberately unused — see CancellationToken.None below

        try
        {
            context.SetProperty(x => x.Status, TickerStatus.Cancelled)
                .SetProperty(x => x.ExecutedAt, _clock.UtcNow)
                .SetProperty(x => x.ElapsedTime, stopWatch.ElapsedMilliseconds)
                .SetProperty(x => x.ExceptionDetails, reason);

            jobActivity?.SetTag("tickerq.job.final_status", context.Status.ToString());
            jobActivity?.SetStatus(ActivityStatusCode.Error);

            _tickerQInstrumentation.LogJobCancelled(context.TickerId, context.FunctionName, reason);

            // Scheduler mode owns the terminal write. Worker mode only returns this outcome so
            // the scheduler can apply it to the original acquired context and fence the commit.
            if (mode == ExecutionMode.Scheduler)
                await _internalTickerManager.UpdateTickerAsync(context, CancellationToken.None);

            // Notifications and user hooks are best-effort side effects. Neither may prevent
            // the authoritative terminal row from being persisted, nor suppress the other.
            TryNotifyFailure(context, "timeout_cancelled", reason);

            if (_serviceProvider.GetService(typeof(ITickerExceptionHandler)) is ITickerExceptionHandler handler)
            {
                try
                {
                    await handler.HandleCanceledExceptionAsync(
                        new TaskCanceledException(reason), context.TickerId, context.Type);
                }
                catch (Exception ex)
                {
                    _tickerQInstrumentation.LogJobFailed(context.TickerId, context.FunctionName, ex, context.RetryCount);
                }
            }
        }
        catch (Exception ex)
        {
            _tickerQInstrumentation.LogJobFailed(context.TickerId, context.FunctionName, ex, context.RetryCount);
        }
    }

    private bool TryGetContractDriftReason(InternalFunctionContext context, out string reason)
    {
        reason = null;

        // Rows created before contract identity persistence remain executable for backward compatibility.
        if (context.RequestContractVersion == null && context.RequestContractFingerprint == null)
            return false;

        var descriptor = _descriptorResolver(context.FunctionName);
        if (descriptor == null)
        {
            reason =
                $"Request contract drift detected for TickerFunction '{context.FunctionName}': " +
                "the persisted ticker has contract identity, but the current descriptor is missing. " +
                "Restore the descriptor or update the ticker before executing it.";
            return true;
        }

        var currentFingerprint = descriptor.Request?.Fingerprint;
        if (context.RequestContractVersion == descriptor.ContractVersion
            && string.Equals(
                context.RequestContractFingerprint,
                currentFingerprint,
                StringComparison.Ordinal))
            return false;

        var fingerprintChanged = !string.Equals(
            context.RequestContractFingerprint,
            currentFingerprint,
            StringComparison.Ordinal);
        reason =
            $"Request contract drift detected for TickerFunction '{context.FunctionName}': " +
            $"persisted version {context.RequestContractVersion?.ToString() ?? "<missing>"}, " +
            $"current version {descriptor.ContractVersion}, fingerprint changed={fingerprintChanged}. " +
            "Update the ticker payload against the current contract before executing it.";
        return true;
    }

    private async Task<bool> WaitForRetry(InternalFunctionContext context, CancellationToken cancellationToken,
        int attempt, CancellationTokenSource cancellationTokenSource, ExecutionMode mode)
    {
        if (attempt == 0)
            return false;

        if (attempt > context.Retries)
            return true;

        context.SetProperty(x => x.RetryCount, attempt);

        if (mode == ExecutionMode.Scheduler)
            await _internalTickerManager.UpdateTickerAsync(context, CancellationToken.None);

        context.ResetUpdateProps();

        var retryInterval = (context.RetryIntervals?.Length > 0)
            ? (attempt - 1 < context.RetryIntervals.Length
                ? context.RetryIntervals[attempt - 1]
                : context.RetryIntervals[^1])
            : 30;

        // Announce the upcoming retry. attempt is the 1-based retry number that's
        // about to run, matching the dashboard's "N/M" Retries column ("retries done / max").
        _tickerQInstrumentation.LogJobRetryScheduled(
            context.TickerId, context.FunctionName, attempt, context.Retries, retryInterval);

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
        }, TickerQInternalJsonContext.Default.ExceptionDetailClassForSerialization);
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

    private static void GatherDescendantsToSkip(
        InternalFunctionContext parent,
        Guid chainRootId,
        List<InternalFunctionContext> skipList)
    {
        if (parent.TimeTickerChildren == null || parent.TimeTickerChildren.Count == 0)
            return;

        foreach (var child in parent.TimeTickerChildren)
        {
            child.ParentId = parent.TickerId;
            child.ChainRootId = chainRootId;
            skipList.Add(child);

            // Recursively gather grandchildren
            GatherDescendantsToSkip(child, chainRootId, skipList);
        }
    }

    private Task SafeRecursiveExecution(
        InternalFunctionContext context,
        Guid chainRootId,
        bool isDue,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return ExecuteTreeAsync(
                context, isDue, registeredSource: null, cancellationToken, isChild: true,
                chainRootId: chainRootId);
        }
        catch
        {
            // ignored
        }

        return Task.CompletedTask;
    }
}
