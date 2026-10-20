using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TickerQ.Exceptions;
using TickerQ.TickerQThreadPool;
using TickerQ.Utilities;
using TickerQ.Utilities.Exceptions;
using TickerQ.Utilities.Base;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Infrastructure;
using TickerQ.Utilities.Instrumentation;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Licensing;
using TickerQ.Utilities.Models;

namespace TickerQ;

internal partial class TickerExecutionTaskHandler : ITickerExecutionTaskHandler
{
    private enum ExecutionMode
    {
        Scheduler,
        Worker
    }

    // Cached lowercase TickerType names to avoid per-call ToString + ToLowerInvariant allocations
    private static readonly Dictionary<TickerType, string> TickerTypeNamesLower =
        Enum.GetValues<TickerType>().ToDictionary(t => t, t => t.ToString().ToLowerInvariant());

    // Track admitted executions, not queued cancellation registrations. Otherwise two
    // occurrences acquired together can both see a sibling and both skip.
    private readonly object _cronAdmissionLock = new();
    private readonly Dictionary<(string Partition, Guid CronId), int> _activeCronExecutions = new();

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TickerExecutionTaskHandler> _logger;
    private readonly TickerLifecycleLogger _lifecycle;
    private readonly ITickerClock _clock;
    private readonly ITickerQInstrumentation _tickerQInstrumentation;
    private readonly IInternalTickerManager _internalTickerManager;
    private readonly SchedulerOptionsBuilder _schedulerOptions;
    private readonly ITickerQFailureNotifier _failureNotifier;
    private readonly TickerQLicenseStateProvider _licenseState;
    private readonly ITickerFunctionConcurrencyGate _concurrencyGate;
    private readonly ITickerQExecutionScheduler _executionScheduler;
    private readonly Func<string, TickerFunctionDescriptor> _descriptorResolver;

    public TickerExecutionTaskHandler(IServiceProvider serviceProvider, ITickerClock clock, ITickerQInstrumentation tickerQInstrumentation, IInternalTickerManager internalTickerManager, SchedulerOptionsBuilder schedulerOptions, ITickerQFailureNotifier failureNotifier, TickerQLicenseStateProvider licenseState, ITickerFunctionConcurrencyGate concurrencyGate)
        : this(
            serviceProvider, clock, tickerQInstrumentation, internalTickerManager, schedulerOptions, failureNotifier, licenseState, concurrencyGate,
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
        TickerQLicenseStateProvider licenseState,
        ITickerFunctionConcurrencyGate concurrencyGate,
        Func<string, TickerFunctionDescriptor> descriptorResolver)
    {
        _serviceProvider = serviceProvider;
        _logger = serviceProvider.GetService<ILogger<TickerExecutionTaskHandler>>()
            ?? NullLogger<TickerExecutionTaskHandler>.Instance;
        _lifecycle = serviceProvider.GetService<TickerLifecycleLogger>();
        _clock = clock;
        _tickerQInstrumentation = tickerQInstrumentation;
        _internalTickerManager = internalTickerManager;
        _schedulerOptions = schedulerOptions;
        _failureNotifier = failureNotifier;
        _licenseState = licenseState ?? throw new ArgumentNullException(nameof(licenseState));
        _concurrencyGate = concurrencyGate ?? throw new ArgumentNullException(nameof(concurrencyGate));
        _executionScheduler = serviceProvider.GetService<ITickerQTaskScheduler>() as ITickerQExecutionScheduler;
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
            _logger.LogWarning(ex, "TickerQ failure notifier failed for {TickerType} {TickerId}",
                context.Type, context.TickerId);
        }
    }

    /// <summary>
    /// Effective per-attempt timeout for a ticker: its own TimeoutSeconds wins
    /// (&lt;= 0 disables explicitly), otherwise the global default applies.
    /// </summary>
    private TimeSpan? GetEffectiveTimeout(InternalFunctionContext context)
    {
        TimeSpan? configured;
        if (context.TimeoutSeconds is { } seconds)
            configured = seconds > 0 ? TimeSpan.FromSeconds(seconds) : (TimeSpan?)null;
        else
            configured = _schedulerOptions.DefaultExecutionTimeout is { } fallback && fallback > TimeSpan.Zero
                ? fallback
                : (TimeSpan?)null;

        return configured > DefinedCronExecutionLimits.MaxTimeout
            ? DefinedCronExecutionLimits.MaxTimeout
            : configured;
    }

    private void EnsureLicensedForExecution()
    {
        var license = _licenseState.Current;
        if (!license.ExecutionAllowed)
            throw new InvalidOperationException(license.Message);
    }

    public Task ExecuteTaskAsync(InternalFunctionContext context, bool isDue, CancellationToken cancellationToken = default)
    {
        EnsureLicensedForExecution();
        return ExecuteRegisteredTaskAsync(context, isDue, registeredSource: null, cancellationToken);
    }

    public async Task<TickerWorkerExecutionResult> ExecuteWorkerTaskAsync(
        InternalFunctionContext context, bool isDue, CancellationToken cancellationToken = default)
    {
        EnsureLicensedForExecution();
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
    {
        if (registeredSource?.IsCancellationRequested == true || cancellationToken.IsCancellationRequested)
        {
            // Finalizing an acquired cancellation is cleanup, not a new execution. Do it
            // before license checks, contract validation, cron-chain creation, or user hooks.
            await CancelBeforeExecutionAsync(context).ConfigureAwait(false);
            return;
        }
        EnsureLicensedForExecution();
        await ExecuteTreeAsync(context, isDue, registeredSource, cancellationToken, isChild: false);
    }

    private async Task CancelBeforeExecutionAsync(InternalFunctionContext context)
    {
        const string reason = "Cancelled before execution.";
        context.ChainRootId ??= context.TickerId;
        context.ChainGeneration ??= context.AcquisitionToken;
        var pending = new Stack<(InternalFunctionContext Context, bool Finalize)>();
        var visited = new HashSet<Guid>();
        pending.Push((context, false));
        while (pending.TryPop(out var item))
        {
            var current = item.Context;
            if (!item.Finalize)
            {
                if (!visited.Add(current.TickerId)) continue;
                pending.Push((current, true));
                // Cron occurrences own a definition, not an already materialized time tree.
                if (current.Type != TickerType.CronTickerOccurrence)
                    foreach (var child in current.TimeTickerChildren)
                    {
                        child.ParentId = current.TickerId;
                        child.ChainRootId = context.ChainRootId;
                        child.ChainGeneration = context.ChainGeneration;
                        child.AcquisitionToken = context.AcquisitionToken;
                        pending.Push((child, false));
                    }
                continue;
            }
            if (current.Status is not (TickerStatus.Idle or TickerStatus.Queued or TickerStatus.InProgress))
                continue;

            current.SetStatus(TickerStatus.Cancelled)
                .SetExecutedAt(_clock.UtcNow)
                .SetElapsedTime(0L)
                .SetExceptionDetails(reason)
                .SetResultEnvelope(null);
            // Preserve the acquired generation until its descendants have been finalized.
            // The provider fences every write; the cancellation token must not cancel cleanup.
            await _internalTickerManager.UpdateTickerAsync(current, CancellationToken.None).ConfigureAwait(false);
            try { _tickerQInstrumentation.LogJobCancelled(current.TickerId, current.FunctionName, reason); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "TickerQ cancellation logging failed for {TickerType} {TickerId}",
                    current.Type, current.TickerId);
            }
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
            await RunCronGuardedFunctionAsync(
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
            await RunCronGuardedFunctionAsync(
                context, isDue, cancellationToken, cancellationTokenSource, isChild, mode).ConfigureAwait(false);
        }
        finally
        {
            TickerCancellationTokenManager.RemoveTickerCancellationToken(
                new TickerExecutionKey(
                    context.RuntimePartitionKey ?? TickerQRuntimePartition.LegacyGlobal.StorageKey,
                    context.Type, context.TickerId), cancellationTokenSource);
        }
    }

    private async Task RunCronGuardedFunctionAsync(
        InternalFunctionContext context, bool isDue, CancellationToken cancellationToken,
        CancellationTokenSource cancellationTokenSource, bool isChild, ExecutionMode mode)
    {
        if (mode != ExecutionMode.Scheduler || context.Type != TickerType.CronTickerOccurrence)
        {
            await RunContextFunctionCoreAsync(context, isDue, cancellationToken,
                cancellationTokenSource, isChild, mode).ConfigureAwait(false);
            return;
        }
        if (!context.ParentId.HasValue)
        {
            if (context.SkipIfAlreadyRunning)
                throw new InvalidOperationException("A cron overlap policy requires the owning cron ID.");
            await RunContextFunctionCoreAsync(context, isDue, cancellationToken,
                cancellationTokenSource, isChild, mode).ConfigureAwait(false);
            return;
        }

        var key = (context.RuntimePartitionKey ?? TickerQRuntimePartition.LegacyGlobal.StorageKey, context.ParentId.Value);
        bool skip;
        lock (_cronAdmissionLock)
        {
            _activeCronExecutions.TryGetValue(key, out var active);
            skip = context.SkipIfAlreadyRunning && active > 0;
            if (!skip) _activeCronExecutions[key] = active + 1;
        }
        if (skip)
        {
            const string reason = "Another execution of this cron job is already running on this scheduler host.";
            context.ResultEnvelope = null;
            context.SetStatus(TickerStatus.Skipped)
                .SetExecutedAt(_clock.UtcNow)
                .SetElapsedTime(0L)
                .SetExceptionDetails(reason);
            _tickerQInstrumentation.LogJobSkipped(context.TickerId, context.FunctionName, reason);
            await _internalTickerManager.UpdateTickerAsync(context, CancellationToken.None).ConfigureAwait(false);
            return;
        }
        try
        {
            // Includes retry waits and cooperative timeout cleanup, until the body has truly exited.
            await RunContextFunctionCoreAsync(context, isDue, cancellationToken,
                cancellationTokenSource, isChild, mode).ConfigureAwait(false);
        }
        finally
        {
            lock (_cronAdmissionLock)
            {
                var remaining = _activeCronExecutions[key] - 1;
                if (remaining == 0) _activeCronExecutions.Remove(key);
                else _activeCronExecutions[key] = remaining;
            }
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
        var typeName = TickerTypeNamesLower.TryGetValue(context.Type, out var cachedTypeName)
            ? cachedTypeName
            : context.Type.ToString().ToLowerInvariant();

        using var jobActivity = _tickerQInstrumentation.StartJobActivity($"tickerq.job.execute.{typeName}", context);

        jobActivity?.SetTag("tickerq.job.is_due", isDue);
        jobActivity?.SetTag("tickerq.job.is_child", isChild);

        _tickerQInstrumentation.LogJobEnqueued(typeName, context.FunctionName, context.TickerId, "ExecutionTaskHandler");
        
        context.SetStatus(TickerStatus.InProgress);

        if (TryGetContractDriftReason(context, out var driftReason))
        {
            var exception = new TickerValidatorException(driftReason);
            context.SetStatus(TickerStatus.Failed)
                .SetExecutedAt(_clock.UtcNow)
                .SetElapsedTime(0L)
                .SetExceptionDetails(SerializeException(exception));

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
            AcquisitionToken = context.AcquisitionToken,
            RuntimePartitionKey = context.RuntimePartitionKey,
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
            RequestCancelOperationAction = () => cancellationTokenSource.Cancel()
        };

        Exception lastException = null;
        var success = false;

        var effectiveTimeout = GetEffectiveTimeout(context);
        // Qualified remote functions execute their retry policy inside the SDK. Core still
        // forwards Retries/RetryIntervals through TickerFunctionContext, but must dispatch
        // the remote delegate only once or both layers multiply the configured retries.
        var usesRemoteRetryPolicy = !string.IsNullOrEmpty(context.FunctionName) &&
                                    context.FunctionName.Contains('@');
        var finalAttempt = Math.Min(
            usesRemoteRetryPolicy ? context.RetryCount : context.Retries,
            DefinedCronExecutionLimits.MaxRetries);

        // Gate individual function attempts, never the whole tree: roots and descendants
        // share the same host-wide permit, released before traversing children or retrying.
        // SDK worker mode already acquires this gate at its transport execution boundary.
        for (var attempt = context.RetryCount; attempt <= finalAttempt; attempt++)
        {
            tickerFunctionContext.RetryCount = attempt;

            // Update activity with current attempt information
            jobActivity?.SetTag("tickerq.job.current_attempt", attempt + 1);

            CancellationTokenSource attemptCts = null;
            CancellationToken executionAttemptToken = default;
            IDisposable functionPermit = null;
            try
            {
                if (!usesRemoteRetryPolicy &&
                    await WaitForRetry(context, cancellationToken, attempt, cancellationTokenSource, mode)) break;

                if (mode == ExecutionMode.Scheduler && context.CachedMaxConcurrency > 0)
                {
                    var acquisition = _concurrencyGate.AcquireAsync(context.FunctionName,
                        context.CachedMaxConcurrency, cancellationTokenSource.Token);
                    if (acquisition.IsCompletedSuccessfully)
                        functionPermit = acquisition.Result;
                    else
                    {
                        using var waiting = _executionScheduler?.BeginFunctionWait();
                        functionPermit = await acquisition.ConfigureAwait(false);
                    }
                }
                cancellationTokenSource.Token.ThrowIfCancellationRequested();

                // Orchestration, retry delays, and function-gate waits do not own a host
                // execution slot. Only an actual attempt enters the shared execution queue.
                async Task InvokeAttemptAsync(CancellationToken executionToken)
                {
                    executionAttemptToken = executionToken;
                    executionToken.ThrowIfCancellationRequested();
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
                    attemptCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationTokenSource.Token, executionToken);
                    if (effectiveTimeout is { } timeout)
                        attemptCts.CancelAfter(timeout);

                    // Ambient log scope: ILogger calls flowing on the function body's async
                    // stack land in the in-memory log store the dashboard's log tail reads.
                    using (TickerExecutionLogScope.Push(context.TickerId, context.FunctionName))
                    {
                        // Re-evaluate at the final invocation boundary. A run can wait behind retry delays,
                        // concurrency, or graph orchestration after the entry gate; no local or remote
                        // delegate may begin once the shared offline authority becomes blocking.
                        EnsureLicensedForExecution();
                        _lifecycle?.Started(context, attempt);
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
                            var remainingTimerDelay = DefinedCronExecutionLimits.MaxTimerDelay - t;
                            if (grace > remainingTimerDelay)
                                grace = remainingTimerDelay;
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
                                             !cancellationTokenSource.IsCancellationRequested &&
                                             !executionToken.IsCancellationRequested;

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
                }

                if (mode == ExecutionMode.Scheduler && _executionScheduler != null)
                    await _executionScheduler.ExecuteAsync(
                        InvokeAttemptAsync, context.CachedPriority, cancellationTokenSource.Token).ConfigureAwait(false);
                else
                    await InvokeAttemptAsync(cancellationTokenSource.Token).ConfigureAwait(false);

                success = true;
                context.RetryCount = attempt;
                break;
            }
            catch (TickerExecutionTimeoutException ex)
            {
                await HandleExecutionTimeoutAsync(context, tickerFunctionContext, jobActivity,
                    ex.Message, stopWatch, cancellationToken, mode);
                return;
            }
            catch (OperationCanceledException) when (effectiveTimeout.HasValue &&
                                                     attemptCts is { IsCancellationRequested: true } &&
                                                     !cancellationTokenSource.IsCancellationRequested &&
                                                     !executionAttemptToken.IsCancellationRequested)
            {
                // Only the CancelAfter could have fired the attempt token without the
                // parent — the function honored cancellation within the grace window.
                await HandleExecutionTimeoutAsync(context, tickerFunctionContext, jobActivity,
                    $"Exceeded execution timeout of {effectiveTimeout?.TotalSeconds ?? 0:0}s.",
                    stopWatch, cancellationToken, mode);
                return;
            }
            catch (OperationCanceledException ex) when (
                cancellationTokenSource.IsCancellationRequested || executionAttemptToken.IsCancellationRequested || ex is TaskCanceledException)
            {
                context.SetStatus(TickerStatus.Cancelled)
                    .SetExecutedAt(_clock.UtcNow)
                    .SetElapsedTime(stopWatch.ElapsedMilliseconds)
                    .SetExceptionDetails(SerializeException(ex));
                
                // Record only the non-sensitive terminal status on the activity.
                jobActivity?.SetTag("tickerq.job.final_status", context.Status.ToString());
                
                // Log job cancelled
                _tickerQInstrumentation.LogJobCancelled(context.TickerId, context.FunctionName, "Task was cancelled");

                if (mode == ExecutionMode.Scheduler)
                    await PersistExecutionUpdateAsync(context, tickerFunctionContext);

                // A user notification hook must not leave a stopped job persisted as running.
                if (_serviceProvider.GetService(typeof(ITickerExceptionHandler)) is ITickerExceptionHandler handler)
                {
                    try { await handler.HandleCanceledExceptionAsync(ex, context.TickerId, context.Type); }
                    catch (Exception hookError)
                    {
                        _logger.LogWarning(hookError, "TickerQ cancellation exception handler failed for {TickerType} {TickerId}",
                            context.Type, context.TickerId);
                    }
                }
                
                return;
            }
            catch (TerminateExecutionException ex)
            {
                context.SetStatus(ex.Status)
                    .SetExecutedAt(_clock.UtcNow)
                    .SetElapsedTime(stopWatch.ElapsedMilliseconds);

                if (ex.InnerException != null)
                {
                    context.SetExceptionDetails(ex.InnerException.Message);

                }
                else
                {
                    context.SetExceptionDetails(ex.Message);

                }

                // Add skip tags to activity
                jobActivity?.SetTag("tickerq.job.final_status", context.Status.ToString());

                // Log job skipped
                _tickerQInstrumentation.LogJobSkipped(context.TickerId, context.FunctionName, ex.Message);

                if (mode == ExecutionMode.Scheduler)
                    await PersistExecutionUpdateAsync(context, tickerFunctionContext);

                return;
            }
            catch (RemoteExecutionNotStartedException ex) when (
                mode == ExecutionMode.Scheduler && ex.HasAuthenticatedNotStartedProof)
            {
                // The signed remote endpoint proved this exact generation never entered user code.
                // Skipped is terminal, consumes no user retry budget, and releases no failure/cancel descendants.
                context.SetStatus(TickerStatus.Skipped)
                    .SetExecutedAt(_clock.UtcNow)
                    .SetElapsedTime(stopWatch.ElapsedMilliseconds)
                    .SetExceptionDetails(ex.Message);
                jobActivity?.SetTag("tickerq.job.final_status", context.Status.ToString());
                _tickerQInstrumentation.LogJobSkipped(context.TickerId, context.FunctionName, ex.Message);
                tickerFunctionContext.IsRemoteCallbackExecution = true;
                await PersistExecutionUpdateAsync(context, tickerFunctionContext);
                return;
            }
            catch (SdkOfflineSkipException ex)
            {
                // SDK node is offline and the transport-retry window
                // expired. Don't burn the user's Retries budget — retrying
                // is pointless while the node is down. Land on Skipped with
                // the reason persisted so the dashboard can distinguish
                // "would have run but SDK was offline" from "ran and broke".
                context.SetStatus(TickerStatus.Skipped)
                    .SetExecutedAt(_clock.UtcNow)
                    .SetElapsedTime(stopWatch.ElapsedMilliseconds)
                    .SetExceptionDetails(ex.Message);

                jobActivity?.SetTag("tickerq.job.final_status", context.Status.ToString());

                _tickerQInstrumentation.LogJobSkipped(context.TickerId, context.FunctionName, ex.Message);

                if (mode == ExecutionMode.Scheduler)
                    await PersistExecutionUpdateAsync(context, tickerFunctionContext);
                return;
            }
            catch (Exception ex)
            {
                lastException = ex;

                context.SetExceptionDetails(SerializeException(ex));

                // Reporting failures must not replace the original user exception or skip
                // the remaining retries and terminal persistence.
                try
                {
                    if (_serviceProvider.GetService(typeof(ITickerExceptionHandler)) is ITickerExceptionHandler handler)
                        await handler.HandleExceptionAsync(ex, context.TickerId, context.Type).ConfigureAwait(false);
                }
                catch (Exception hookError)
                {
                    _logger.LogWarning(hookError, "TickerQ exception handler failed for {TickerType} {TickerId}",
                        context.Type, context.TickerId);
                }

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
                functionPermit?.Dispose();
            }
        }

        stopWatch.Stop();
        totalStopWatch.Stop();

        // Persist the *total* time (first attempt start → final outcome, includes
        // retry waits). The per-attempt stopWatch is still used for instrumentation
        // ("attempt X failed in 200ms") but doesn't reflect user-lived duration.
        context.SetElapsedTime(totalStopWatch.ElapsedMilliseconds)
            .SetExecutedAt(_clock.UtcNow);

        if (success)
        {
            context.SetStatus(isDue ? TickerStatus.DueDone : TickerStatus.Done);

            // Attach the published result ONLY on the successful terminal write so it commits
            // atomically with the Done/DueDone status, before any children are released/queued.
            // Always stage the optional envelope on success. Null means absence and deliberately clears
            // any result from an earlier successful run; a present JSON "null" remains an envelope.
            context.SetResultEnvelope(
                tickerFunctionContext.ResultSink.HasResult ? tickerFunctionContext.ResultSink.Envelope : null);

            // Add success tags to activity
            jobActivity?.SetTag("tickerq.job.final_status", context.Status.ToString());
            jobActivity?.SetTag("tickerq.job.final_retry_count", context.RetryCount);
            jobActivity?.SetStatus(ActivityStatusCode.Ok);
            
            // Log job completed successfully
            _tickerQInstrumentation.LogJobCompleted(context.TickerId, context.FunctionName, totalStopWatch.ElapsedMilliseconds, true);

            if (mode == ExecutionMode.Scheduler)
                await PersistExecutionUpdateAsync(context, tickerFunctionContext);
        }
        else if (lastException != null)
        {
            context.SetStatus(TickerStatus.Failed)
                .SetExceptionDetails(SerializeException(lastException));
            
            // Add failure tags to activity
            jobActivity?.SetTag("tickerq.job.final_status", context.Status.ToString());
            jobActivity?.SetTag("tickerq.job.final_retry_count", context.RetryCount);
            jobActivity?.SetTag("tickerq.job.error_type", lastException.GetType().Name);
            jobActivity?.SetStatus(ActivityStatusCode.Error);
            
            // Log job failed
            _tickerQInstrumentation.LogJobFailed(context.TickerId, context.FunctionName, lastException, context.RetryCount);
            _tickerQInstrumentation.LogJobCompleted(context.TickerId, context.FunctionName, totalStopWatch.ElapsedMilliseconds, false);

            if (mode == ExecutionMode.Scheduler)
                await PersistExecutionUpdateAsync(context, tickerFunctionContext);

            TryNotifyFailure(context, "failed", lastException.Message);
        }
    }

    /// <summary>
    /// Terminal outcome for a timed-out execution: Cancelled with the timeout
    /// reason persisted. Timeouts don't consume the retry budget — a job that
    /// hung once is likely to hang again; opt into re-runs explicitly.
    /// </summary>
    private async Task HandleExecutionTimeoutAsync(InternalFunctionContext context,
        TickerFunctionContext tickerFunctionContext, Activity jobActivity,
        string reason, Stopwatch stopWatch, CancellationToken cancellationToken, ExecutionMode mode)
    {
        _ = cancellationToken; // deliberately unused — see CancellationToken.None below
        var terminalPersisted = mode != ExecutionMode.Scheduler;

        try
        {
            context.SetStatus(TickerStatus.Cancelled)
                .SetExecutedAt(_clock.UtcNow)
                .SetElapsedTime(stopWatch.ElapsedMilliseconds)
                .SetExceptionDetails(reason);

            jobActivity?.SetTag("tickerq.job.final_status", context.Status.ToString());
            jobActivity?.SetStatus(ActivityStatusCode.Error);

            _tickerQInstrumentation.LogJobCancelled(context.TickerId, context.FunctionName, reason);

            // Scheduler mode owns the terminal write. Worker mode only returns this outcome so
            // the scheduler can apply it to the original acquired context and fence the commit.
            if (mode == ExecutionMode.Scheduler)
            {
                // The terminal write must ignore the surrounding cancelled token and must be
                // durably acknowledged before notification or remote finalization.
                await PersistExecutionUpdateAsync(context, tickerFunctionContext);
                terminalPersisted = true;
            }

            // Notifications and user hooks are best-effort side effects after persistence.
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
                    _logger.LogWarning(ex, "TickerQ cancellation exception handler failed for {TickerType} {TickerId}",
                        context.Type, context.TickerId);
                }
            }
        }
        catch (Exception) when (!terminalPersisted)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "TickerQ timeout reporting failed after persisting {TickerType} {TickerId}",
                context.Type, context.TickerId);
        }
    }

    private async Task PersistExecutionUpdateAsync(
        InternalFunctionContext context,
        TickerFunctionContext executionContext)
    {
        var terminal = context.GetPropsToUpdate().Contains(nameof(InternalFunctionContext.Status)) &&
                       context.Status is TickerStatus.Done or TickerStatus.DueDone or TickerStatus.Failed
                           or TickerStatus.Cancelled or TickerStatus.Skipped;
        if (!executionContext.IsRemoteCallbackExecution || !terminal)
        {
            await _internalTickerManager.UpdateTickerAsync(context, CancellationToken.None).ConfigureAwait(false);
            return;
        }

        var intent = executionContext.RemoteFinalizationIntent;
        if (intent is null)
        {
            if (context.Status != TickerStatus.Skipped)
                throw new InvalidOperationException(
                    "An authenticated remote callback terminal outcome did not carry a durable Node finalization intent.");

            // An authenticated no-start proof has no retained Node execution record to finalize,
            // but its local skipped mutation is still generation-fenced and acknowledged.
            await _internalTickerManager.UpdateTickerFromRemoteAsync(context, CancellationToken.None)
                .ConfigureAwait(false);
            return;
        }

        await _internalTickerManager.UpdateTickerFromRemoteAsync(context, intent, CancellationToken.None)
            .ConfigureAwait(false);
        (_serviceProvider.GetService(typeof(INodeFinalizationWakeSignal)) as INodeFinalizationWakeSignal)?.Wake();
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

        context.SetRetryCount(attempt);

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

        var executableRetryInterval = Math.Min(
            retryInterval, DefinedCronExecutionLimits.MaxRetryIntervalSeconds);
        _lifecycle?.RetryScheduled(context, attempt, executableRetryInterval);
        using var retrying = mode == ExecutionMode.Scheduler && executableRetryInterval > 0
            ? _executionScheduler?.BeginRetryDelay()
            : null;
        await Task.Delay(TimeSpan.FromSeconds(executableRetryInterval), cancellationTokenSource.Token);

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
        Guid? chainGeneration,
        List<InternalFunctionContext> skipList)
    {
        if (parent.TimeTickerChildren == null || parent.TimeTickerChildren.Count == 0)
            return;

        // Keep the same depth-first preorder without consuming the call stack for
        // every generation of a skipped subtree. One frame per depth also avoids
        // copying a wide sibling list into a second work queue.
        var pending = new Stack<(InternalFunctionContext Parent, int NextChild)>();
        pending.Push((parent, 0));
        while (pending.TryPop(out var frame))
        {
            var children = frame.Parent.TimeTickerChildren;
            if (children == null || frame.NextChild >= children.Count)
                continue;
            var child = children[frame.NextChild];
            pending.Push((frame.Parent, frame.NextChild + 1));
            child.ParentId = frame.Parent.TickerId;
            child.ChainRootId = chainRootId;
            child.ChainGeneration = chainGeneration;
            skipList.Add(child);
            if (child.TimeTickerChildren is { Count: > 0 })
                pending.Push((child, 0));
        }
    }

}
