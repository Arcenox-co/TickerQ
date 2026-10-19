using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TickerQ.TickerQThreadPool;
using TickerQ.Utilities;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Models;
using TickerQ.Utilities.Licensing;

namespace TickerQ.BackgroundServices;

/// <summary>Owns acquired generations from admission through queue wait, execution and cleanup.</summary>
internal sealed class AcquiredExecutionQueue : IDisposable
{
    private readonly IInternalTickerManager _manager;
    private readonly ITickerExecutionTaskHandler _handler;
    private readonly ITickerQTaskScheduler _scheduler;
    private readonly ILogger _logger;
    private CancellationTokenSource _executionStop = new();
    private readonly TickerQLicenseStateProvider _licenseState;
    private int _disposed;
    private readonly ConcurrentDictionary<Task, byte> _pendingReleases = new();

    internal AcquiredExecutionQueue(IInternalTickerManager manager, ITickerExecutionTaskHandler handler,
        ITickerQTaskScheduler scheduler, ILogger logger, TickerQLicenseStateProvider licenseState = null)
    { _manager = manager; _handler = handler; _scheduler = scheduler; _logger = logger; _licenseState = licenseState; }

    internal void Resume()
    {
        if (!_executionStop.IsCancellationRequested) return;
        _executionStop.Dispose();
        _executionStop = new CancellationTokenSource();
    }

    internal void Stop()
    {
        try { _executionStop.Cancel(); }
        catch (AggregateException ex) { _logger.LogWarning(ex, "TickerQ job cancellation callback failed during shutdown"); }
    }
    internal async Task StopAsync(CancellationToken cancellationToken)
    {
        Stop(); // Cancellation callbacks synchronously register their cleanup tasks.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        try { await Task.WhenAll(_pendingReleases.Keys.ToArray()).WaitAsync(deadline.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        { _logger.LogWarning("TickerQ acquired-job cleanup exceeded the shutdown deadline; stale recovery may be needed"); }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Stop();
        _executionStop.Dispose();
    }

    internal async Task<bool> PublishAsync(InternalFunctionContext[] functions, bool isDue, CancellationToken admissionToken)
    {
        // Register the whole acquired batch before any queue-capacity wait. Executing work uses
        // the scheduler's drain lifetime, not the fallback scanner's earlier shutdown token.
        var pending = new List<(InternalFunctionContext Function, CancellationTokenSource Source, AcquiredExecutionLifecycle Life)>();
        try
        {
            foreach (var function in functions)
            {
                var source = TickerCancellationTokenManager.TryRegisterAcquired(function, isDue,
                    out var conflict, _executionStop.Token);
                if (source == null)
                {
                    if (conflict) await ReleaseAsync(function).ConfigureAwait(false);
                    continue;
                }
                var life = new AcquiredExecutionLifecycle(
                    new TickerExecutionKey(function.RuntimePartitionKey ?? TickerQRuntimePartition.LegacyGlobal.StorageKey,
                        function.Type, function.TickerId), source, _executionStop.Token, admissionToken,
                    owner => TrackCancellationReleaseAsync(owner, function));
                pending.Add((function, source, life));
            }

            foreach (var item in pending)
            {
                admissionToken.ThrowIfCancellationRequested();
                if (_licenseState != null && !_licenseState.ExecutionAllowed) return false;
                if (!item.Life.IsQueued) continue;
                try
                {
                    // Queue cancellation only interrupts admission. A successfully published callback
                    // has cancellation cleanup registered even if the scanner stops before dequeue.
                    async Task ExecuteAsync(CancellationToken schedulerToken)
                    {
                        // Accepted orchestrations are invoked even when disposal races with
                        // dispatch. Return their exact generation to persistence if not started.
                        if (schedulerToken.IsCancellationRequested)
                        {
                            if (item.Life.AbandonIfQueued()) await ReleaseAsync(item.Function).ConfigureAwait(false);
                            return;
                        }
                        if (!item.Life.TryBeginExecution()) return;
                        try
                        {
                            // The per-job source drives function gates, retry waits, children,
                            // and attempts. Include scheduler disposal in that same lifetime.
                            using var schedulerCancellation = schedulerToken.UnsafeRegister(_ =>
                            {
                                try { item.Source.Cancel(); }
                                catch (AggregateException ex)
                                {
                                    _logger.LogWarning(ex,
                                        "TickerQ job cancellation callback failed during scheduler shutdown");
                                }
                            }, null);
                            await _handler.ExecuteRegisteredTaskAsync(item.Function, isDue, item.Source,
                                item.Source.Token).ConfigureAwait(false);
                        }
                        finally { item.Life.CompleteExecution(); }
                    }

                    if (_scheduler is ITickerQExecutionScheduler executionScheduler)
                    {
                        if (!executionScheduler.TryQueueOrchestration(ExecuteAsync, admissionToken))
                            throw new InvalidOperationException("The scheduler cannot admit more work right now.");
                    }
                    else
                    {
                        // Legacy schedulers combine admission and stop in one token. Preserve
                        // their existing drain behavior: the acquired source owns execution.
                        await _scheduler.QueueAsync(_ => ExecuteAsync(CancellationToken.None), item.Function.CachedPriority, admissionToken)
                            .ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    if (item.Life.AbandonIfQueued())
                    {
                        await ReleaseAsync(item.Function).ConfigureAwait(false);
                        _logger.LogWarning(ex, "Failed to publish {TickerType} {TickerId}; its acquired generation was released or left for stale recovery",
                            item.Function.Type, item.Function.TickerId);
                    }
                    else if (item.Life.HasBegunExecution)
                        _logger.LogWarning(ex, "Queue reported a fault after {TickerType} {TickerId} began; execution retains ownership",
                            item.Function.Type, item.Function.TickerId);
                    // Release the remaining unpublished batch on cancellation/freeze/failure.
                    return false;
                }
            }
            pending.Clear(); // Ownership transferred to callbacks / cancellation releases.
            return true;
        }
        finally
        {
            foreach (var item in pending)
                if (item.Life.AbandonIfQueued()) await ReleaseAsync(item.Function).ConfigureAwait(false);
        }
    }

    private Task TrackCancellationReleaseAsync(AcquiredExecutionLifecycle owner, InternalFunctionContext function)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingReleases.TryAdd(completion.Task, 0);
        _ = CompleteAsync();
        return completion.Task;

        async Task CompleteAsync()
        {
            try { await ReleaseAsync(function).ConfigureAwait(false); }
            finally
            {
                owner.CompleteCancellationRelease();
                _pendingReleases.TryRemove(completion.Task, out _);
                completion.TrySetResult();
            }
        }
    }

    private async Task ReleaseAsync(InternalFunctionContext function)
    {
        // Retry only this exact acquisition generation. Never widen cleanup to earlier
        // publications, which may already be running on another worker.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                await _manager.ReleaseAcquiredResources([function], CancellationToken.None).ConfigureAwait(false);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to release acquired {TickerType} {TickerId} (attempt {Attempt}); stale recovery must heal it if retry fails",
                    function.Type, function.TickerId, attempt + 1);
            }
        }
    }

    private sealed class AcquiredExecutionLifecycle
    {
        private readonly TickerExecutionKey _key;
        private readonly CancellationTokenSource _source;
        private readonly CancellationTokenRegistration _stoppingRegistration;
        private readonly CancellationTokenRegistration _admissionRegistration;
        private readonly Func<AcquiredExecutionLifecycle, Task> _releaseQueuedExecution;
        private int _state; // 0 = queued, 1 = executing, 2 = completed, 3 = cancellation release pending

        internal AcquiredExecutionLifecycle(
            TickerExecutionKey key,
            CancellationTokenSource source,
            CancellationToken stoppingToken,
            CancellationToken admissionToken,
            Func<AcquiredExecutionLifecycle, Task> releaseQueuedExecution)
        {
            _key = key;
            _source = source;
            _releaseQueuedExecution = releaseQueuedExecution;
            _stoppingRegistration = stoppingToken.UnsafeRegister(
                static state => ((AcquiredExecutionLifecycle)state).ReleaseIfQueued(), this);
            _admissionRegistration = admissionToken.UnsafeRegister(
                static state => ((AcquiredExecutionLifecycle)state).ReleaseIfQueued(), this);
        }

        internal bool IsQueued => Volatile.Read(ref _state) == 0;
        internal bool HasBegunExecution => Volatile.Read(ref _state) == 1;
        internal bool IsCancellationReleasePending => Volatile.Read(ref _state) == 3;

        internal bool TryBeginExecution()
        {
            if (Interlocked.CompareExchange(ref _state, 1, 0) != 0)
                return false;

            _stoppingRegistration.Dispose();
            _admissionRegistration.Dispose();
            return true;
        }

        internal void CompleteExecution()
        {
            if (Interlocked.CompareExchange(ref _state, 2, 1) != 1)
                return;

            TickerCancellationTokenManager.RemoveTickerCancellationToken(_key, _source);
        }

        internal bool AbandonIfQueued()
        {
            if (Interlocked.CompareExchange(ref _state, 2, 0) != 0)
                return false;

            _stoppingRegistration.Dispose();
            _admissionRegistration.Dispose();
            TickerCancellationTokenManager.RemoveTickerCancellationToken(_key, _source);
            return true;
        }

        internal void CompleteCancellationRelease()
        {
            if (Interlocked.CompareExchange(ref _state, 2, 3) == 3)
            {
                _stoppingRegistration.Unregister();
                _admissionRegistration.Unregister();
                TickerCancellationTokenManager.RemoveTickerCancellationToken(_key, _source);
            }
        }

        private void ReleaseIfQueued()
        {
            if (Interlocked.CompareExchange(ref _state, 3, 0) == 0)
                _ = _releaseQueuedExecution(this);
        }
    }

}
