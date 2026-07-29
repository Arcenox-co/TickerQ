using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ.RemoteExecutor;

internal enum NodeFinalizationAttemptResult
{
    Retry,
    Completed
}

/// <summary>
/// Provider-backed durable Node finalization worker. The only in-process state is a coalesced
/// wake signal and at most <see cref="DefaultWorkerCount"/> currently executing claim tasks.
/// </summary>
internal sealed class NodeFinalizationReconciler<TTimeTicker, TCronTicker> : BackgroundService,
    INodeFinalizationWakeSignal
    where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
    where TCronTicker : CronTickerEntity, new()
{
    internal const int DefaultWorkerCount = 4;
    private static readonly TimeSpan ClaimLease = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ShortDelay = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan InitialBackoff = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(5);

    private readonly ITickerPersistenceProvider<TTimeTicker, TCronTicker> _provider;
    private readonly TickerQRemoteExecutionOptions _options;
    private readonly ILogger<NodeFinalizationReconciler<TTimeTicker, TCronTicker>> _logger;
    private readonly HttpClient? _transportOverride;
    private readonly SemaphoreSlim _wake = new(0, 1);
    private int _wakePending;
    private readonly int _workerCount;
    private readonly string _workerId;

    public NodeFinalizationReconciler(
        ITickerPersistenceProvider<TTimeTicker, TCronTicker> provider,
        TickerQRemoteExecutionOptions options,
        ILogger<NodeFinalizationReconciler<TTimeTicker, TCronTicker>> logger)
        : this(provider, options, logger, null, DefaultWorkerCount)
    {
    }

    internal NodeFinalizationReconciler(
        ITickerPersistenceProvider<TTimeTicker, TCronTicker> provider,
        TickerQRemoteExecutionOptions options,
        ILogger<NodeFinalizationReconciler<TTimeTicker, TCronTicker>> logger,
        HttpClient? transportOverride,
        int workerCount)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        if (workerCount <= 0) throw new ArgumentOutOfRangeException(nameof(workerCount));
        _transportOverride = transportOverride;
        _workerCount = workerCount;
        _workerId = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";
    }

    public void Wake()
    {
        if (Interlocked.CompareExchange(ref _wakePending, 1, 0) == 0)
            _wake.Release();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_provider.SupportsDurableNodeFinalizationOutbox)
        {
            _logger.LogInformation("Durable Node finalization reconciler is disabled because the persistence provider does not advertise support.");
            return;
        }

        var inFlight = new List<Task>(_workerCount);
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                inFlight.RemoveAll(static task => task.IsCompleted);
                var freeSlots = _workerCount - inFlight.Count;
                if (freeSlots == 0)
                {
                    await Task.WhenAny(inFlight).WaitAsync(stoppingToken).ConfigureAwait(false);
                    continue;
                }

                var now = DateTime.UtcNow;
                IReadOnlyList<NodeFinalizationClaim> claims;
                try
                {
                    claims = await _provider.ClaimDueNodeFinalizationsAsync(
                        _workerId, freeSlots, now, now.Add(ClaimLease), stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Claiming durable Node finalizations failed.");
                    await WaitForWakeOrDelayAsync(IdleDelay, stoppingToken).ConfigureAwait(false);
                    continue;
                }

                foreach (var claim in claims.Take(freeSlots))
                    inFlight.Add(ProcessClaimAsync(claim, stoppingToken));

                if (claims.Count == 0)
                    await WaitForWakeOrDelayAsync(IdleDelay, stoppingToken).ConfigureAwait(false);
                else if (claims.Count < freeSlots)
                    await WaitForWakeOrDelayAsync(ShortDelay, stoppingToken).ConfigureAwait(false);
            }
        }
        finally
        {
            if (inFlight.Count > 0)
            {
                try { await Task.WhenAll(inFlight).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
                catch (Exception) { /* claims remain durable and become reclaimable at lease expiry */ }
            }
        }
    }

    private async Task ProcessClaimAsync(NodeFinalizationClaim claim, CancellationToken stoppingToken)
    {
        try
        {
            var result = await NodeCallbackExecutionDelegateFactory.TryFinalizeAsync(
                claim.Intent, () => _options.WebHookSignature, _transportOverride, stoppingToken).ConfigureAwait(false);
            if (result == NodeFinalizationAttemptResult.Completed)
            {
                if (!await _provider.CompleteNodeFinalizationAsync(claim, stoppingToken).ConfigureAwait(false))
                    _logger.LogDebug("Durable Node finalization claim {ClaimToken} became stale before completion.", claim.ClaimToken);
                return;
            }

            await RescheduleAsync(claim, "remote_retry", stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Do not widen cleanup under host shutdown. The durable lease expires and a later host reclaims it.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Durable Node finalization attempt failed for outbox {OutboxId}.", claim.Intent.OutboxId);
            try { await RescheduleAsync(claim, ClassifyError(ex), stoppingToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception persistEx)
            {
                _logger.LogWarning(persistEx, "Rescheduling durable Node finalization claim {ClaimToken} failed; its lease will expire.", claim.ClaimToken);
            }
        }
    }

    private Task<bool> RescheduleAsync(NodeFinalizationClaim claim, string errorCode, CancellationToken cancellationToken)
        => _provider.RescheduleNodeFinalizationAsync(
            claim, DateTime.UtcNow.Add(Backoff(claim)), errorCode, cancellationToken);

    private static TimeSpan Backoff(NodeFinalizationClaim claim)
    {
        var exponent = Math.Min(Math.Max(claim.AttemptCount - 1, 0), 8);
        var baseMs = Math.Min(MaxBackoff.TotalMilliseconds, InitialBackoff.TotalMilliseconds * (1 << exponent));
        var bytes = claim.Intent.OutboxId.ToByteArray();
        var jitter = 0.8 + ((bytes[0] << 8 | bytes[1]) / 65535d) * 0.4;
        return TimeSpan.FromMilliseconds(Math.Min(MaxBackoff.TotalMilliseconds, baseMs * jitter));
    }

    private static string ClassifyError(Exception ex) => ex switch
    {
        HttpRequestException => "transport_error",
        TaskCanceledException => "attempt_timeout",
        InvalidOperationException => "invalid_ack",
        _ => "attempt_error"
    };

    private async Task WaitForWakeOrDelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(delay);
        try
        {
            await _wake.WaitAsync(timeout.Token).ConfigureAwait(false);
            Interlocked.Exchange(ref _wakePending, 0);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
    }
}
