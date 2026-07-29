using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TickerQ.Utilities;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Models;

namespace TickerQ.BackgroundServices;

/// <summary>
/// Periodic maintenance loop that deletes historical terminal ticker records (completed time-ticker
/// chains and cron-ticker occurrences) whose <c>ExecutedAt</c> is older than the configured per-status
/// window. Not a persisted or user-visible ticker — a plain hosted service.
/// <para>
/// Registered only when background services are enabled AND retention is configured, and after the
/// scheduler so it stops first (before the scheduler drains in-flight work). Cutoffs are computed once
/// per sweep from <see cref="ITickerClock.UtcNow"/>. Work is bounded by
/// <see cref="JobRetentionOptions.BatchSize"/> per batch and <see cref="JobRetentionOptions.MaxBatchesPerSweep"/>
/// per sweep. A failed sweep is logged and the loop continues at the next interval.
/// </para>
/// </summary>
internal sealed class TickerQRetentionBackgroundService : BackgroundService
{
    private readonly IInternalTickerManager _internalTickerManager;
    private readonly JobRetentionOptions _options;
    private readonly ITickerClock _clock;
    private readonly ILogger<TickerQRetentionBackgroundService> _logger;

    // Keyset continuation for time-chain traversal, carried across sweeps so blocked (retained) chains are
    // passed over rather than reselected — they cannot starve later eligible chains. Reset to Start when a
    // traversal reaches the end so the next sweep wraps and reconsiders records that aged into eligibility.
    private RetentionCursor _timeChainCursor = RetentionCursor.Start;
    private bool _preferTimeStream = true;

    public TickerQRetentionBackgroundService(
        IInternalTickerManager internalTickerManager,
        JobRetentionOptions options,
        ITickerClock clock,
        ILogger<TickerQRetentionBackgroundService> logger)
    {
        _internalTickerManager = internalTickerManager;
        _options = options;
        _clock = clock;
        _logger = logger;
    }

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        // Fail closed and loud: retention was explicitly configured, but the persistence provider
        // does not implement it. Surface a clear startup failure instead of silently deleting nothing.
        if (_options.IsEnabled && !_internalTickerManager.SupportsRetention)
            throw new NotSupportedException(
                "TickerQ job retention is configured (ConfigureJobRetention) but the active persistence " +
                "provider does not support retention. Remove the retention configuration or switch to a " +
                "provider that implements it (EF Core, MongoDB, Redis, or the in-memory provider).");

        return base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Defensive: the service is only registered when enabled, but never spin if it is not.
        if (!_options.IsEnabled)
            return;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_options.SweepInterval, stoppingToken);
                await RunSweepAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // A transient provider failure must never stop the host; the next interval retries.
                _logger.LogWarning(ex, "TickerQ job retention sweep failed; retrying next interval");
            }
        }
    }

    /// <summary>
    /// Runs one bounded sweep: computes cutoffs once, then issues up to
    /// <see cref="JobRetentionOptions.MaxBatchesPerSweep"/> delete batches, stopping early when no more
    /// eligible records remain. Returns the sweep totals.
    /// </summary>
    internal async Task<RetentionSweepResult> RunSweepAsync(CancellationToken cancellationToken)
    {
        var cutoffs = ComputeCutoffs(_clock.UtcNow);
        if (!cutoffs.HasAny)
            return RetentionSweepResult.Empty;

        var totalTime = 0;
        var totalCron = 0;

        // Two independent streams share one provider-call budget. Alternate them so a low cap cannot
        // starve either stream across sweeps. Time cursor progress is checkpointed immediately after
        // every successful call, before cron work can fail or cancellation can be observed.
        var cursor = _timeChainCursor;
        var timeHasMore = true;
        var cronHasMore = true;
        var calls = 0;

        while (calls < _options.MaxBatchesPerSweep && (timeHasMore || cronHasMore))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var runTime = timeHasMore && (_preferTimeStream || !cronHasMore);
            calls++;

            if (runTime)
            {
                _preferTimeStream = false;
                try
                {
                    var timeResult = await _internalTickerManager
                        .SweepTimeChainsAsync(cutoffs, _options.BatchSize, cursor, cancellationToken)
                        .ConfigureAwait(false);
                    totalTime += timeResult.Deleted;
                    cursor = timeResult.NextCursor;
                    timeHasMore = timeResult.HasMore;
                    _timeChainCursor = timeHasMore ? cursor : RetentionCursor.Start;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    timeHasMore = false;
                    _logger.LogWarning(ex,
                        "TickerQ time-chain retention stream failed; cron retention will continue this sweep");
                }
            }
            else
            {
                _preferTimeStream = true;
                try
                {
                    var cronResult = await _internalTickerManager
                        .SweepCronOccurrencesAsync(cutoffs, _options.BatchSize, cancellationToken)
                        .ConfigureAwait(false);
                    totalCron += cronResult.Deleted;
                    cronHasMore = cronResult.HasMore;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    cronHasMore = false;
                    _logger.LogWarning(ex,
                        "TickerQ cron-occurrence retention stream failed; time-chain retention will continue this sweep");
                }
            }
        }

        if (totalTime + totalCron > 0)
            _logger.LogInformation(
                "TickerQ job retention deleted {TimeTickerRows} time-ticker row(s) and {CronOccurrenceRows} cron-occurrence row(s)",
                totalTime, totalCron);

        return new RetentionSweepResult(totalTime, totalCron, timeHasMore || cronHasMore);
    }

    /// <summary>
    /// Absolute UTC cutoffs for a sweep: for each configured window, the cutoff is
    /// <paramref name="nowUtc"/> minus that window; a null window yields a null cutoff (retain forever).
    /// </summary>
    internal RetentionCutoffs ComputeCutoffs(DateTime nowUtc) => new RetentionCutoffs(
        _options.DeleteSucceededAfter is { } succeeded ? nowUtc - succeeded : (DateTime?)null,
        _options.DeleteFailedAfter is { } failed ? nowUtc - failed : (DateTime?)null,
        _options.DeleteCancelledAfter is { } cancelled ? nowUtc - cancelled : (DateTime?)null,
        _options.DeleteSkippedAfter is { } skipped ? nowUtc - skipped : (DateTime?)null,
        _options.MaxNodesPerChain);
}
