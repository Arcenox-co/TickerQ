using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TickerQ.Utilities;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Models;

namespace TickerQ.BackgroundServices;

/// <summary>
/// Two halves of runtime stale-job recovery, one loop per node:
/// 1. Lease renewal — every <see cref="SchedulerOptionsBuilder.LeaseRenewalInterval"/>,
///    one batched update pushes <c>LeaseUntil</c> forward for everything this node is
///    executing right now (no-op when idle). A renewal that comes back short means the
///    watchdog on another node already recovered a job we still think we're running —
///    the local execution gets its cancellation token fired so a paused-then-resumed
///    node stops doing work whose result would be fenced away anyway.
/// 2. Watchdog sweep — InProgress rows whose lease expired belong to a dead node;
///    their per-job <c>OnStale</c> policy is applied (Restart bounded by
///    <see cref="SchedulerOptionsBuilder.MaxStaleRestarts"/>, else Cancel).
/// </summary>
internal class TickerQStaleJobRecoveryBackgroundService : BackgroundService
{
    private readonly IInternalTickerManager _internalTickerManager;
    private readonly SchedulerOptionsBuilder _schedulerOptions;
    private readonly ILogger<TickerQStaleJobRecoveryBackgroundService> _logger;
    private readonly Utilities.Interfaces.ITickerQFailureNotifier _notifier;

    public TickerQStaleJobRecoveryBackgroundService(
        IInternalTickerManager internalTickerManager,
        SchedulerOptionsBuilder schedulerOptions,
        ILogger<TickerQStaleJobRecoveryBackgroundService> logger,
        Utilities.Interfaces.ITickerQFailureNotifier notifier)
    {
        _internalTickerManager = internalTickerManager;
        _schedulerOptions = schedulerOptions;
        _logger = logger;
        _notifier = notifier;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_internalTickerManager.SupportsLeaseBasedRecovery)
        {
            // Fail closed: providers that have not implemented the full lease/recovery
            // contract would otherwise report fake renewal success and never surface lost
            // leases. Skip both loops entirely and warn once at startup instead of spinning.
            _logger.LogWarning(
                "Persistence provider does not support lease-based stale-job recovery; lease renewal and the stale-job watchdog are disabled for this node");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_schedulerOptions.LeaseRenewalInterval, stoppingToken);

                await RenewLeasesAsync(stoppingToken);
                await SweepStaleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Never let a transient DB failure stop the host — a missed renewal
                // is absorbed by the lease being a multiple of the renewal interval.
                _logger.LogWarning(ex, "Stale-job recovery cycle failed; retrying next interval");
            }
        }
    }

    private async Task RenewLeasesAsync(CancellationToken ct)
    {
        var timeTickerLeases = new List<AcquisitionLease>();
        var occurrenceLeases = new List<AcquisitionLease>();
        TickerCancellationTokenManager.SnapshotRunningForLeaseRenewal(timeTickerLeases, occurrenceLeases);

        if (timeTickerLeases.Count == 0 && occurrenceLeases.Count == 0)
            return;

        // Generation-aware renewal: rows are matched on id + AcquisitionToken, so a row this
        // node re-acquired under a newer generation (same-node ABA) or one another node recovered
        // is not renewed under this stale snapshot — it falls into the lost set below instead.
        var renewed = await _internalTickerManager.RenewActiveTickerLeasesAsync(timeTickerLeases, occurrenceLeases, ct);
        if (renewed >= timeTickerLeases.Count + occurrenceLeases.Count)
            return;

        // Short renewal: either a job finished between snapshot and update (benign —
        // the id is gone from the token manager by now) or we lost the lease. Cancel
        // whatever is genuinely still running locally without a row backing it.
        var lost = await _internalTickerManager.GetLostLeaseTickerIdsAsync(timeTickerLeases, occurrenceLeases, ct);
        foreach (var key in lost)
        {
            if (TickerCancellationTokenManager.RequestTickerCancellation(key))
                _logger.LogWarning(
                    "{TickerType} {TickerId} lost its lease (recovered by another node while this one was unresponsive); cancelling the local execution — its result would be discarded by fencing",
                    key.Type, key.TickerId);
        }
    }

    private async Task SweepStaleAsync(CancellationToken ct)
    {
        var recovered = await _internalTickerManager.RecoverStaleTickersAsync(ct);
        if (recovered.Total == 0)
            return;

        var restarted = recovered.RestartedTimeTickers + recovered.RestartedCronOccurrences;
        var cancelled = recovered.CancelledTimeTickers + recovered.CancelledCronOccurrences;
        if (restarted > 0)
            Utilities.Instrumentation.TickerQMetrics.JobsStaleRecovered.Add(restarted,
                new KeyValuePair<string, object>("tickerq.action", "restart"));
        if (cancelled > 0)
            Utilities.Instrumentation.TickerQMetrics.JobsStaleRecovered.Add(cancelled,
                new KeyValuePair<string, object>("tickerq.action", "cancel"));

        _notifier.Notify(new Utilities.Models.TickerFailureEvent
        {
            Kind = "stale_recovered",
            Reason = $"{restarted} restarted, {cancelled} cancelled after their executing node stopped renewing its lease",
            OccurredAtUtc = DateTime.UtcNow,
            Node = _schedulerOptions.NodeIdentifier,
        });

        _logger.LogWarning(
            "Stale-job watchdog recovered {Total} ticker(s) from dead nodes: {RestartedTime} time ticker(s) restarted, {CancelledTime} cancelled; {RestartedCron} cron occurrence(s) restarted, {CancelledCron} cancelled",
            recovered.Total,
            recovered.RestartedTimeTickers,
            recovered.CancelledTimeTickers,
            recovered.RestartedCronOccurrences,
            recovered.CancelledCronOccurrences);
    }
}
