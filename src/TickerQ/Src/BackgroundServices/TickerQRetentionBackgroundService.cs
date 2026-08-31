using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TickerQ.Utilities;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Models;

namespace TickerQ.BackgroundServices
{
    internal sealed class TickerQRetentionBackgroundService : BackgroundService
    {
        private readonly IInternalTickerManager _manager;
        private readonly JobRetentionOptions _options;
        private readonly ITickerClock _clock;
        private readonly ILogger<TickerQRetentionBackgroundService> _logger;
        private RetentionCursor _timeCursor = RetentionCursor.Start;
        private bool _preferTime = true;

        public TickerQRetentionBackgroundService(
            IInternalTickerManager manager,
            JobRetentionOptions options,
            ITickerClock clock,
            ILogger<TickerQRetentionBackgroundService> logger)
        {
            _manager = manager;
            _options = options;
            _clock = clock;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(_options.SweepInterval, stoppingToken).ConfigureAwait(false);
                    await RunSweepAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "TickerQ job retention sweep failed; retrying next interval");
                }
            }
        }

        internal async Task<RetentionSweepResult> RunSweepAsync(CancellationToken cancellationToken)
        {
            var now = _clock.UtcNow;
            var cutoffs = ComputeCutoffs(now);
            if (!cutoffs.HasAny)
                return RetentionSweepResult.Empty;

            var deletedTime = 0;
            var deletedCron = 0;
            var timeHasMore = true;
            var cronHasMore = true;
            var cursor = _timeCursor;

            for (var calls = 0; calls < _options.MaxBatchesPerSweep && (timeHasMore || cronHasMore); calls++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var sweepTime = timeHasMore && (_preferTime || !cronHasMore);
                if (sweepTime)
                {
                    _preferTime = false;
                    var result = await _manager
                        .SweepTimeChainsAsync(cutoffs, _options.BatchSize, cursor, cancellationToken)
                        .ConfigureAwait(false);
                    deletedTime += result.Deleted;
                    cursor = result.NextCursor;
                    timeHasMore = result.HasMore;
                    _timeCursor = timeHasMore ? cursor : RetentionCursor.Start;
                }
                else
                {
                    _preferTime = true;
                    var result = await _manager
                        .SweepCronOccurrencesAsync(cutoffs, _options.BatchSize, cancellationToken)
                        .ConfigureAwait(false);
                    deletedCron += result.Deleted;
                    cronHasMore = result.HasMore;
                }
            }

            if (deletedTime + deletedCron > 0)
                _logger.LogInformation(
                    "TickerQ retention deleted {TimeTickerRows} time-ticker row(s) and {CronOccurrenceRows} cron-occurrence row(s)",
                    deletedTime,
                    deletedCron);
            return new RetentionSweepResult(deletedTime, deletedCron, timeHasMore || cronHasMore);
        }

        internal RetentionCutoffs ComputeCutoffs(DateTime nowUtc) => new RetentionCutoffs(
            _options.DeleteSucceededAfter is { } succeeded ? nowUtc - succeeded : (DateTime?)null,
            _options.DeleteFailedAfter is { } failed ? nowUtc - failed : (DateTime?)null,
            _options.DeleteCancelledAfter is { } cancelled ? nowUtc - cancelled : (DateTime?)null,
            _options.DeleteSkippedAfter is { } skipped ? nowUtc - skipped : (DateTime?)null,
            _options.MaxNodesPerChain);
    }
}
