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
    private readonly ITickerExecutionTaskHandler  _taskHandler;
    private readonly ITickerFunctionConcurrencyGate _concurrencyGate;
    private readonly SemaphoreSlim _acquisitionPublicationGate = new(1, 1);
    private int _started;
    private int _stopping;
    public bool SkipFirstRun;
    public bool IsRunning => _started == 1;


    public TickerQSchedulerBackgroundService(
        TickerExecutionContext executionContext,
        ITickerExecutionTaskHandler taskHandler,
        ITickerQTaskScheduler taskScheduler,
        IInternalTickerManager  internalTickerManager,
        SchedulerOptionsBuilder schedulerOptions,
        ITickerFunctionConcurrencyGate concurrencyGate,
        ILogger<TickerQSchedulerBackgroundService> logger = null)
    {
        _executionContext = executionContext;
        _taskHandler = taskHandler;
        _taskScheduler = taskScheduler;
        _internalTickerManager = internalTickerManager ?? throw new ArgumentNullException(nameof(internalTickerManager));
        _concurrencyGate = concurrencyGate;
        _schedulerOptions = schedulerOptions;
        _logger = logger ?? NullLogger<TickerQSchedulerBackgroundService>.Instance;
        _minPollingInterval = ResolveMinPollingInterval(schedulerOptions);
        _restartThrottle = new RestartThrottleManager(() => _schedulerLoopCancellationTokenSource?.Cancel());
    }

    private readonly SchedulerOptionsBuilder _schedulerOptions;
    private readonly ILogger<TickerQSchedulerBackgroundService> _logger;
    
    public override Task StartAsync(CancellationToken ct)
    {
        if (SkipFirstRun)
        {
            _taskScheduler.Freeze();
            SkipFirstRun = false;
            return Task.CompletedTask;
        }
        
        Interlocked.Exchange(ref _stopping, 0);
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
                _executionContext.SetFunctions(null);
                _schedulerLoopCancellationTokenSource?.Dispose();
                _schedulerLoopCancellationTokenSource = null;
            }
        }
    }

    private async Task RunTickerQSchedulerAsync(CancellationToken stoppingToken, CancellationToken cancellationToken)
    {
        while (!stoppingToken.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            if (_executionContext.Functions.Length != 0)
            {
                await _acquisitionPublicationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    if (Volatile.Read(ref _stopping) != 0)
                        return;

                    var acquired = await _internalTickerManager
                        .SetTickersInProgress(_executionContext.Functions, cancellationToken)
                        .ConfigureAwait(false);

                    foreach (var function in acquired.OrderBy(x => x.CachedPriority))
                        await QueueAcquiredExecution(function, stoppingToken).ConfigureAwait(false);

                    _executionContext.SetFunctions(null);
                }
                finally
                {
                    _acquisitionPublicationGate.Release();
                }
            }
            
            var (timeRemaining, functions) =
                await _internalTickerManager.GetNextTickers(cancellationToken);

            _executionContext.SetFunctions(functions);

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

                sleepDuration = timeRemaining <= minInterval
                    ? minInterval
                    : timeRemaining;
                _executionContext.SetNextPlannedOccurrence(DateTime.UtcNow.Add(sleepDuration));
            }

            var notify = _executionContext.NotifyCoreAction;
            if (notify != null)
            {
                notify(_executionContext.GetNextPlannedOccurrence(), CoreNotifyActionType.NotifyNextOccurence);
            }

            await Task.Delay(sleepDuration, cancellationToken);
        }
    }

    /// <summary>
    /// Registers an acquired root ticker with the cancellation manager immediately (so lease renewal
    /// keeps its DB row alive while it waits in the queue and on its concurrency semaphore) and then
    /// queues its execution. The registration is visible from this point until the queued delegate's
    /// finally unregisters it; a queue-publication failure unregisters here instead so nothing leaks.
    /// </summary>
    private async Task QueueAcquiredExecution(InternalFunctionContext function, CancellationToken stoppingToken)
    {
        var semaphore = _concurrencyGate.GetSemaphoreOrNull(function.FunctionName, function.CachedMaxConcurrency);
        var registeredSource = TickerCancellationTokenManager.TryRegisterAcquired(function, isDue: false, stoppingToken);
        if (registeredSource == null)
            return;

        var lifecycle = new AcquiredExecutionLifecycle(function.TickerId, registeredSource, stoppingToken);
        if (!lifecycle.IsQueued)
            return;

        try
        {
            await _taskScheduler.QueueAsync(async _ =>
            {
                if (!lifecycle.TryBeginExecution())
                    return;

                using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(
                    stoppingToken, registeredSource.Token);

                var acquiredSemaphore = false;
                try
                {
                    if (semaphore != null)
                    {
                        await semaphore.WaitAsync(waitCts.Token).ConfigureAwait(false);
                        acquiredSemaphore = true;
                    }

                    await _taskHandler
                        .ExecuteRegisteredTaskAsync(function, false, registeredSource, waitCts.Token)
                        .ConfigureAwait(false);
                }
                finally
                {
                    if (acquiredSemaphore)
                        semaphore.Release();

                    lifecycle.CompleteExecution();
                }
            }, function.CachedPriority, CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            lifecycle.AbandonIfQueued();
            throw;
        }
    }

    private sealed class AcquiredExecutionLifecycle
    {
        private readonly Guid _tickerId;
        private readonly CancellationTokenSource _source;
        private readonly CancellationTokenRegistration _stoppingRegistration;
        private int _state; // 0 = queued, 1 = executing, 2 = completed

        internal AcquiredExecutionLifecycle(
            Guid tickerId, CancellationTokenSource source, CancellationToken stoppingToken)
        {
            _tickerId = tickerId;
            _source = source;
            _stoppingRegistration = stoppingToken.UnsafeRegister(
                static state => ((AcquiredExecutionLifecycle)state).RemoveIfQueued(), this);
        }

        internal bool IsQueued => Volatile.Read(ref _state) == 0;

        internal bool TryBeginExecution()
            => Interlocked.CompareExchange(ref _state, 1, 0) == 0;

        internal void CompleteExecution()
        {
            if (Interlocked.CompareExchange(ref _state, 2, 1) != 1)
                return;

            _stoppingRegistration.Dispose();
            TickerCancellationTokenManager.RemoveTickerCancellationToken(_tickerId, _source);
        }

        internal void AbandonIfQueued()
        {
            if (Interlocked.CompareExchange(ref _state, 2, 0) != 0)
                return;

            _stoppingRegistration.Dispose();
            TickerCancellationTokenManager.RemoveTickerCancellationToken(_tickerId, _source);
        }

        private void RemoveIfQueued()
        {
            if (Interlocked.CompareExchange(ref _state, 2, 0) == 0)
                TickerCancellationTokenManager.RemoveTickerCancellationToken(_tickerId, _source);
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

    private async Task ReleaseAllResourcesAsync(Exception ex)
    {
        if (ex != null && _executionContext.NotifyCoreAction != null)
            _executionContext.NotifyCoreAction(ex.ToString(), CoreNotifyActionType.NotifyHostExceptionMessage);

        await _internalTickerManager.ReleaseAcquiredResources(null, CancellationToken.None);
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
            _restartThrottle.RequestRestart();
            return;
        }

        var newTime = dateTime.Value;
        var threshold = TimeSpan.FromMilliseconds(500);
        var diff = nextPlannedOccurrence.Value - newTime;

        if (newTime <= now || diff > threshold)
            _restartThrottle.RequestRestart();
    }

    public void Restart()
    {
        _restartThrottle.RequestRestart();
    }
    
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        Interlocked.Exchange(ref _stopping, 1);
        await _acquisitionPublicationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _taskScheduler.Freeze();
        }
        finally
        {
            _acquisitionPublicationGate.Release();
        }
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
             _taskScheduler.ActiveExecutionCount > 0))
        {
            _logger.LogInformation(
                "Shutdown: draining {Active} in-flight and {Queued} queued ticker(s) (acquired window: {Acquired}) for up to {Timeout}s…",
                _taskScheduler.ActiveExecutionCount, _taskScheduler.TotalQueuedTasks,
                TickerCancellationTokenManager.ActiveCount, drainTimeout.TotalSeconds);

            var deadline = DateTime.UtcNow + drainTimeout;
            while (DateTime.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
            {
                if (TickerCancellationTokenManager.ActiveCount == 0 &&
                    _taskScheduler.TotalQueuedTasks == 0 &&
                    _taskScheduler.ActiveExecutionCount == 0)
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
            if (acquired > 0 || queued > 0 || active > 0)
                _logger.LogWarning(
                    "Shutdown: drain window elapsed with {Active} executing, {Queued} queued, and {Acquired} acquired/prequeue ticker(s) — they will be abandoned and healed by stale-job recovery",
                    active, queued, acquired);
            else
                _logger.LogInformation("Shutdown: all in-flight tickers finished");
        }

        await base.StopAsync(cancellationToken);
    }

    public override void Dispose()
    {
        _restartThrottle.Dispose();
        _acquisitionPublicationGate.Dispose();
        base.Dispose();
    }
}
