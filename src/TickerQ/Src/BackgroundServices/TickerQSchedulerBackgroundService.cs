using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TickerQ.TickerQThreadPool;
using TickerQ.Utilities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Licensing;
using TickerQ.Utilities.Models;

namespace TickerQ.BackgroundServices;

internal class TickerQSchedulerBackgroundService : BackgroundService, ITickerQHostScheduler
{
    private readonly RestartThrottleManager _restartThrottle;
    private readonly IInternalTickerManager _internalTickerManager;
    private readonly TickerExecutionContext _executionContext;
    private readonly TimeSpan _minPollingInterval;
    private SafeCancellationTokenSource _schedulerLoopCancellationTokenSource;
    private readonly ITickerQTaskScheduler  _taskScheduler;
    private readonly AcquiredExecutionQueue _acquiredQueue;
    private readonly bool _ownsAcquiredQueue;
    private readonly ITickerQActivationGate _activationGate;
    private readonly TickerQLicenseStateProvider _licenseState;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _acquisitionPublicationGate = new(1, 1);
    private int _started;
    private int _stopping;
    public bool SkipFirstRun;
    public bool IsRunning => Volatile.Read(ref _started) == 1;
    private DateTime _nextFailureWarning;


    public TickerQSchedulerBackgroundService(
        TickerExecutionContext executionContext,
        ITickerExecutionTaskHandler taskHandler,
        ITickerQTaskScheduler taskScheduler,
        IInternalTickerManager  internalTickerManager,
        SchedulerOptionsBuilder schedulerOptions,
        TickerQLicenseStateProvider licenseState,
        ILogger<TickerQSchedulerBackgroundService> logger = null,
        ITickerQActivationGate activationGate = null, AcquiredExecutionQueue acquiredQueue = null)
        : this(TimeProvider.System, executionContext, taskHandler, taskScheduler, internalTickerManager,
            schedulerOptions, licenseState, logger, activationGate, acquiredQueue)
    {
    }

    // The operational store uses UTC through ITickerClock. An unrelated TimeProvider
    // registration in the application must not replace the scheduler's production clock.
    // Only deterministic tests select a different timer/clock through this overload.
    internal TickerQSchedulerBackgroundService(
        TimeProvider timeProvider,
        TickerExecutionContext executionContext,
        ITickerExecutionTaskHandler taskHandler,
        ITickerQTaskScheduler taskScheduler,
        IInternalTickerManager internalTickerManager,
        SchedulerOptionsBuilder schedulerOptions,
        TickerQLicenseStateProvider licenseState,
        ILogger<TickerQSchedulerBackgroundService> logger = null,
        ITickerQActivationGate activationGate = null, AcquiredExecutionQueue acquiredQueue = null)
    {
        _executionContext = executionContext;
        _taskScheduler = taskScheduler;
        _internalTickerManager = internalTickerManager ?? throw new ArgumentNullException(nameof(internalTickerManager));
        _licenseState = licenseState;
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _activationGate = activationGate;
        _schedulerOptions = schedulerOptions;
        _logger = logger ?? NullLogger<TickerQSchedulerBackgroundService>.Instance;
        _minPollingInterval = ResolveMinPollingInterval(schedulerOptions);
        _ownsAcquiredQueue = acquiredQueue == null;
        _acquiredQueue = acquiredQueue ?? new AcquiredExecutionQueue(internalTickerManager, taskHandler, taskScheduler, _logger, licenseState);
        _restartThrottle = new RestartThrottleManager(() => _schedulerLoopCancellationTokenSource?.Cancel());
    }

    private readonly SchedulerOptionsBuilder _schedulerOptions;
    private readonly ILogger<TickerQSchedulerBackgroundService> _logger;
    
    public override Task StartAsync(CancellationToken ct)
    {
        if (!_licenseState.ExecutionAllowed)
            return Task.CompletedTask;

        if (SkipFirstRun)
        {
            _taskScheduler.Freeze();
            SkipFirstRun = false;
            return Task.CompletedTask;
        }
        
        if (Interlocked.CompareExchange(ref _started, 1, 0) != 0)
            return Task.CompletedTask;

        Interlocked.Exchange(ref _stopping, 0);
        _acquiredQueue.Resume();
        _taskScheduler.Resume();
        if (_schedulerOptions.DetailedLoggingEnabled)
        {
            _logger.LogInformation(
                "TickerQ detailed logging enabled. Node {NodeIdentifier}; namespace {ApplicationNamespace}; " +
                "max concurrency {MaxConcurrency}; acquisition batch {AcquisitionBatchSize}; queue capacity {MaxQueuedExecutions}; " +
                "workflow capacity {MaxActiveWorkflows}; minimum wait {MinPollingInterval}",
                _schedulerOptions.NodeIdentifier,
                _schedulerOptions.ApplicationNamespace ?? "legacy-global",
                _schedulerOptions.MaxConcurrency,
                _schedulerOptions.AcquisitionBatchSize,
                _schedulerOptions.MaxQueuedExecutions,
                _schedulerOptions.MaxActiveWorkflows,
                _minPollingInterval);
        }
        return base.StartAsync(ct);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            if (!_licenseState.ExecutionAllowed)
                return;

            if (_activationGate != null)
                await _activationGate.WaitForActivationAsync(stoppingToken).ConfigureAwait(false);

            if (!_licenseState.ExecutionAllowed)
                return;

            while (!stoppingToken.IsCancellationRequested && _licenseState.ExecutionAllowed)
            {
                _schedulerLoopCancellationTokenSource = SafeCancellationTokenSource.CreateLinked(stoppingToken);

                try
                {
                    await RunTickerQSchedulerAsync(stoppingToken, _schedulerLoopCancellationTokenSource.Token);
                }
                catch (OperationCanceledException) when (_schedulerLoopCancellationTokenSource.Token.IsCancellationRequested && !stoppingToken.IsCancellationRequested)
                {
                    // This is a restart request - release resources and continue loop
                    if (_schedulerOptions.DetailedLoggingEnabled)
                        _logger.LogInformation("TickerQ scheduler loop is restarting after an earlier execution was scheduled");
                    await ReleaseAllResourcesAsync(null);
                    // Small delay to allow resources to be released
                    await Task.Delay(100, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    // Application is shutting down - release resources and exit
                    await ReleaseAllResourcesAsync(null);
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
                    _executionContext.SetFunctions(null);
                    _schedulerLoopCancellationTokenSource?.Dispose();
                    _schedulerLoopCancellationTokenSource = null;
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally { Interlocked.Exchange(ref _started, 0); }
    }

    private async Task RunTickerQSchedulerAsync(CancellationToken stoppingToken, CancellationToken cancellationToken)
    {
        while (!stoppingToken.IsCancellationRequested
               && !cancellationToken.IsCancellationRequested
               && _licenseState.ExecutionAllowed)
        {
            if (_executionContext.Functions.Length != 0)
            {
                // Timer delays are relative and round to milliseconds. Waking from the
                // acquisition delay does not prove that this batch's absolute due time
                // has arrived. Keep its leases queued until every acquired job is due.
                var dueAt = DateTime.MinValue;
                foreach (var function in _executionContext.Functions)
                    if (function.ExecutionTime > dueAt)
                        dueAt = function.ExecutionTime;
                await WaitUntilDueAsync(dueAt, _timeProvider, cancellationToken).ConfigureAwait(false);

                await _acquisitionPublicationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    if (Volatile.Read(ref _stopping) != 0)
                        return;

                    var candidateCount = _executionContext.Functions.Length;
                    var acquired = await _internalTickerManager
                        .SetTickersInProgress(_executionContext.Functions, cancellationToken)
                        .ConfigureAwait(false);

                    var pendingPublication = acquired.OrderBy(x => x.CachedPriority).ToArray();
                    _executionContext.SetFunctions(pendingPublication);

                    if (_schedulerOptions.DetailedLoggingEnabled)
                    {
                        _logger.LogInformation(
                            "TickerQ acquired {AcquiredCount} of {CandidateCount} selected executions for publication: {Executions}",
                            pendingPublication.Length, candidateCount,
                            DescribeBatch(pendingPublication));
                    }

                    if (!_licenseState.ExecutionAllowed)
                    {
                        await _internalTickerManager.ReleaseAcquiredResources(
                                pendingPublication, CancellationToken.None)
                            .ConfigureAwait(false);
                        _executionContext.SetFunctions(null);
                        return;
                    }

                    if (Volatile.Read(ref _stopping) != 0)
                    {
                        await _internalTickerManager.ReleaseAcquiredResources(
                                pendingPublication, CancellationToken.None)
                            .ConfigureAwait(false);
                        return;
                    }

                    // Transfer ownership of the entire batch to the common dispatcher. It releases
                    // only unpublished generations if admission fails; active jobs retain their leases.
                    _executionContext.SetFunctions(null);
                    var published = await _acquiredQueue.PublishAsync(pendingPublication, false, stoppingToken,
                        waitForCapacity: true).ConfigureAwait(false);
                    if (_schedulerOptions.DetailedLoggingEnabled)
                    {
                        _logger.LogInformation(
                            published
                                ? "TickerQ published {ExecutionCount} acquired executions to the host queue"
                                : "TickerQ could not publish {ExecutionCount} acquired executions; their unstarted generations were released",
                            pendingPublication.Length);
                    }
                    if (!_licenseState.ExecutionAllowed) return;
                    if (!published)
                    {
                        // Failure/freeze is not backlog progress. Released generations
                        // must not create a tight reacquisition loop.
                        await Task.Delay(_minPollingInterval > TimeSpan.Zero
                            ? _minPollingInterval : TimeSpan.FromMilliseconds(10), cancellationToken);
                    }
                }
                finally
                {
                    _acquisitionPublicationGate.Release();
                }
            }
            
            var (timeRemaining, functions) =
                await _internalTickerManager.GetNextTickers(cancellationToken);

            _executionContext.SetFunctions(functions);

            if (_schedulerOptions.DetailedLoggingEnabled && functions.Length != 0)
            {
                _logger.LogInformation(
                    "TickerQ selected {ExecutionCount} executions; next due in {TimeRemaining}: {Executions}",
                    functions.Length, timeRemaining, DescribeBatch(functions));
            }

            TimeSpan sleepDuration;
            if (timeRemaining == Timeout.InfiniteTimeSpan || timeRemaining > TimeSpan.FromDays(1))
            {
                sleepDuration = TimeSpan.FromDays(1);
                _executionContext.SetNextPlannedOccurrence(null);
                _executionContext.SetFunctions(null);
            }
            else
            {
                var minInterval = _minPollingInterval < TimeSpan.Zero
                    ? TimeSpan.Zero
                    : _minPollingInterval;

                // A successfully acquired due batch is work, not an idle poll. Keep
                // draining bounded batches through the dispatcher's capacity wait.
                // Empty acquisition results still back off (e.g. another node won).
                sleepDuration = functions.Length > 0 && timeRemaining <= TimeSpan.Zero
                    ? TimeSpan.Zero
                    : timeRemaining <= minInterval ? minInterval : timeRemaining;
                _executionContext.SetNextPlannedOccurrence(DateTime.UtcNow.Add(sleepDuration));
            }

            NotifySafely(_executionContext.GetNextPlannedOccurrence(), CoreNotifyActionType.NotifyNextOccurence);

            if (sleepDuration == TimeSpan.Zero)
            {
                // In-memory acquisition and admission can both finish synchronously.
                // Yield between batches so workers and cancellation can make progress.
                await Task.Yield();
            }
            else
            {
                await Task.Delay(sleepDuration, cancellationToken);
            }
        }
    }

    internal static async ValueTask WaitUntilDueAsync(
        DateTime dueAt, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var remaining = dueAt - timeProvider.GetUtcNow().UtcDateTime;
            if (remaining <= TimeSpan.Zero)
                return;

            // Round UP: Task.Delay(TimeSpan) truncates fractional milliseconds.
            // Recheck after each wake, including early timers and backward clock changes.
            // A bounded async wait avoids spinning or overflowing the timer's duration.
            var ticks = Math.Min(remaining.Ticks, TimeSpan.TicksPerDay);
            var milliseconds = (ticks + TimeSpan.TicksPerMillisecond - 1) / TimeSpan.TicksPerMillisecond;
            await Task.Delay(TimeSpan.FromMilliseconds(milliseconds), timeProvider, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static TimeSpan ResolveMinPollingInterval(SchedulerOptionsBuilder schedulerOptions)
    {
        if (schedulerOptions == null)
            return TimeSpan.FromSeconds(1);

        var prop = schedulerOptions.GetType().GetProperty("MinPollingInterval");
        if (prop?.PropertyType == typeof(TimeSpan))
            return (TimeSpan)prop.GetValue(schedulerOptions);

        return TimeSpan.FromSeconds(1);
    }

    private static string DescribeBatch(InternalFunctionContext[] functions)
    {
        const int maximum = 5;
        var sample = functions.Take(maximum)
            .Select(context => $"{context.Type}:{context.FunctionName}({context.TickerId})");
        var description = string.Join(", ", sample);
        return functions.Length > maximum
            ? $"{description}, +{functions.Length - maximum} more"
            : description;
    }

    private async Task ReleaseAllResourcesAsync(Exception ex)
    {
        if (ex != null)
        {
            if (DateTime.UtcNow >= _nextFailureWarning)
            {
                _nextFailureWarning = DateTime.UtcNow.AddMinutes(1);
                _logger.LogWarning(ex, "TickerQ scheduler cycle failed; retrying. Repeated failures are logged at most once per minute");
            }
            NotifySafely(ex.ToString(), CoreNotifyActionType.NotifyHostExceptionMessage);
        }

        try
        {
            await _internalTickerManager.ReleaseAcquiredResources(
                _executionContext.Functions, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception releaseException)
        {
            // Never widen a failed generation-fenced cleanup into an ID-only node-wide release.
            // The persistence lease remains authoritative and stale-job recovery can safely heal it.
            _logger.LogError(releaseException,
                "Failed to release the scheduler's current acquired generations after an execution-loop fault");
        }
    }

    private void NotifySafely(object value, CoreNotifyActionType action)
    {
        try { _executionContext.NotifyCoreAction?.Invoke(value, action); }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "TickerQ scheduler notification failed; scheduler execution continues");
        }
    }

    public void RestartIfNeeded(DateTime? dateTime)
    {
        if (!dateTime.HasValue)
            return;
        
        var now = DateTime.UtcNow;
        var nextPlannedOccurrence = _executionContext.GetNextPlannedOccurrence();
        
        // Restart if:
        // 1. No tasks are currently planned, OR
        // 2. The new task should execute at least 500ms earlier than the currently planned task, OR
        // 3. The new task is already due/overdue (ExecutionTime <= now)
        if (nextPlannedOccurrence == null)
        {
            if (_schedulerOptions.DetailedLoggingEnabled)
                _logger.LogInformation("TickerQ scheduler restart requested for {ExecutionTime}; no occurrence is currently planned", dateTime.Value);
            _restartThrottle.RequestRestart();
            return;
        }

        var newTime = dateTime.Value;
        var threshold = TimeSpan.FromMilliseconds(500);
        var diff = nextPlannedOccurrence.Value - newTime;

        if (newTime <= now || diff > threshold)
        {
            if (_schedulerOptions.DetailedLoggingEnabled)
                _logger.LogInformation(
                    "TickerQ scheduler restart requested for {ExecutionTime}; current wake is {NextPlannedOccurrence}",
                    newTime, nextPlannedOccurrence.Value);
            _restartThrottle.RequestRestart();
        }
    }

    public void Restart()
    {
        _restartThrottle.RequestRestart();
    }
    
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        Interlocked.Exchange(ref _stopping, 1);
        // Freeze immediately rather than waiting unboundedly for a persistence transition. Any
        // transition already in flight re-checks _stopping before publication and fenced-releases
        // its winners. base.StopAsync remains bounded by the host's cancellation token.
        _taskScheduler.Freeze();
        Interlocked.Exchange(ref _started, 0);

        // Graceful drain: give in-flight executions (and anything already queued
        // on the worker pool) a bounded chance to finish before the host exits,
        // so routine deploys don't rely on stale-job recovery to heal abandoned
        // rows. Bounded by both ShutdownDrainTimeout and the host's own shutdown
        // token (HostOptions.ShutdownTimeout).
        var drainTimeout = _schedulerOptions.ShutdownDrainTimeout;
        if (drainTimeout > TimeSpan.Zero &&
            (TickerCancellationTokenManager.ActiveCount > 0 ||
             _taskScheduler.TotalQueuedTasks > 0 ||
             _taskScheduler.ActiveExecutionCount > 0 ||
             _taskScheduler.ActiveOrchestrationCount > 0))
        {
            _logger.LogInformation(
                "Shutdown: draining {Active} in-flight and {Queued} queued attempt(s), {Orchestrations} job orchestration(s) (acquired window: {Acquired}) for up to {Timeout}s…",
                _taskScheduler.ActiveExecutionCount, _taskScheduler.TotalQueuedTasks,
                _taskScheduler.ActiveOrchestrationCount,
                TickerCancellationTokenManager.ActiveCount, drainTimeout.TotalSeconds);

            var deadline = DateTime.UtcNow + drainTimeout;
            while (DateTime.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
            {
                if (TickerCancellationTokenManager.ActiveCount == 0 &&
                    _taskScheduler.TotalQueuedTasks == 0 &&
                    _taskScheduler.ActiveExecutionCount == 0 &&
                    _taskScheduler.ActiveOrchestrationCount == 0)
                    break;

                try
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            var acquired = TickerCancellationTokenManager.ActiveCount;
            var queued = _taskScheduler.TotalQueuedTasks;
            var active = _taskScheduler.ActiveExecutionCount;
            var orchestrations = _taskScheduler.ActiveOrchestrationCount;
            if (acquired > 0 || queued > 0 || active > 0 || orchestrations > 0)
                _logger.LogWarning(
                    "Shutdown: drain window elapsed with {Active} executing, {Queued} queued attempt(s), {Orchestrations} job orchestration(s), and {Acquired} acquired/prequeue ticker(s) — they will be abandoned and healed by stale-job recovery",
                    active, queued, orchestrations, acquired);
            else
                _logger.LogInformation("Shutdown: all in-flight tickers finished");
        }

        await _acquiredQueue.StopAsync(cancellationToken).ConfigureAwait(false);
        await base.StopAsync(cancellationToken);
    }

    public override void Dispose()
    {
        Interlocked.Exchange(ref _stopping, 1);
        _taskScheduler.Freeze();
        _restartThrottle.Dispose();
        if (_ownsAcquiredQueue) _acquiredQueue.Dispose();
        // BackgroundService.Dispose cancels but does not join ExecuteAsync, so an active
        // acquisition may still need to release the admission semaphore.
        base.Dispose();
    }
}
