using System;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Integrations;

namespace TickerQ.MongoDB.Infrastructure;

internal sealed partial class TickerMongoPersistenceProvider<TTimeTicker, TCronTicker>
    where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
    where TCronTicker : CronTickerEntity, new()
{
    private static readonly Expression<Func<TTimeTicker, TickerMonitoringExecution>> MonitoringTimeProjection = x => new()
    {
        Id = x.Id, Status = x.Status, CompletedUtc = x.ExecutedAt,
        ElapsedMilliseconds = x.ElapsedTime, Generation = x.AcquisitionToken ?? x.ChainGeneration
    };
    private static readonly Expression<Func<CronTickerOccurrenceEntity<TCronTicker>, TickerMonitoringExecution>> MonitoringCronProjection = x => new()
    {
        Id = x.Id, IsCron = true, Status = x.Status, CompletedUtc = x.ExecutedAt,
        ElapsedMilliseconds = x.ElapsedTime, Generation = x.AcquisitionToken
    };

    public async Task<TickerMonitoringSnapshot> ReadMonitoringSnapshotAsync(
        string function, DateTime afterUtc, DateTime throughUtc, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(function);
        cancellationToken.ThrowIfCancellationRequested();
        var ids = await _context.CronTickers.AsQueryable()
            .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey && x.Function == function)
            .Select(x => x.Id).Take(TickerMonitoringSnapshot.MaxCronDefinitions + 1)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        if (ids.Count > TickerMonitoringSnapshot.MaxCronDefinitions) return TickerMonitoringCollector.TooManyDefinitions();

        var time = _context.TimeTickers.AsQueryable()
            .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey && x.Function == function);
        var cron = _context.CronTickerOccurrences.AsQueryable()
            .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey && ids.Contains(x.CronTickerId) && x.ChainRootId == null);
        var collector = new TickerMonitoringCollector(afterUtc, throughUtc);
        var completedTime = await time
            .Where(x => (x.Status == TickerStatus.Done || x.Status == TickerStatus.DueDone || x.Status == TickerStatus.Failed || x.Status == TickerStatus.Cancelled)
                && x.ExecutedAt > afterUtc && x.ExecutedAt <= throughUtc)
            .OrderBy(x => x.ExecutedAt).Take(TickerMonitoringSnapshot.PageSize + 1)
            .Select(MonitoringTimeProjection).ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var row in completedTime) collector.Add(row);
        if (ids.Count > 0)
        {
            var completedCron = await cron
                .Where(x => (x.Status == TickerStatus.Done || x.Status == TickerStatus.DueDone || x.Status == TickerStatus.Failed || x.Status == TickerStatus.Cancelled)
                    && x.ExecutedAt > afterUtc && x.ExecutedAt <= throughUtc)
                .OrderBy(x => x.ExecutedAt).Take(TickerMonitoringSnapshot.PageSize + 1)
                .Select(MonitoringCronProjection).ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (var row in completedCron) collector.Add(row);
        }
        var page = collector.Build();
        if (page.Completed.Length > TickerMonitoringSnapshot.PageSize) return page;

        var runningTime = await time.Where(x => x.Status == TickerStatus.InProgress)
            .Take(TickerMonitoringSnapshot.MaxRunning + 1).Select(MonitoringTimeProjection)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var row in runningTime) collector.Add(row);
        if (ids.Count > 0)
        {
            var runningCron = await cron.Where(x => x.Status == TickerStatus.InProgress)
                .Take(TickerMonitoringSnapshot.MaxRunning + 1).Select(MonitoringCronProjection)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (var row in runningCron) collector.Add(row);
        }
        return collector.Build();
    }
}
