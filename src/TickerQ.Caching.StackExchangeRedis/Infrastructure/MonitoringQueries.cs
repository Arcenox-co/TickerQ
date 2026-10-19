using StackExchange.Redis;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Integrations;

namespace TickerQ.Caching.StackExchangeRedis.Infrastructure;

internal sealed partial class TickerRedisPersistenceProvider<TTimeTicker, TCronTicker>
    where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
    where TCronTicker : CronTickerEntity, new()
{
    public async Task<TickerMonitoringSnapshot> ReadMonitoringSnapshotAsync(
        string function, DateTime afterUtc, DateTime throughUtc, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(function);
        cancellationToken.ThrowIfCancellationRequested();
        if (!await IsActivationPublicationVisibleAsync(cancellationToken).ConfigureAwait(false)) return new();
        var ids = new HashSet<Guid>();
        await foreach (var id in Db.SetScanAsync(CronIdsKey, pageSize: 128).WithCancellation(cancellationToken))
        {
            if (!Guid.TryParse(id.ToString(), out var guid)) continue;
            var value = await Db.StringGetAsync(CronKey(guid)).WaitAsync(cancellationToken).ConfigureAwait(false);
            if (!value.IsNull) RedisMonitoringReader.Visit((byte[])value, function, false, row =>
            {
                if (row.FunctionMatches) ids.Add(row.Id);
            }, cancellationToken);
            if (ids.Count > TickerMonitoringSnapshot.MaxCronDefinitions) return TickerMonitoringCollector.TooManyDefinitions();
        }

        var collector = new TickerMonitoringCollector(afterUtc, throughUtc);
        // Redis stores complete JSON aggregates. Read one at a time and skip payload fields;
        // never materialize all IDs, all JSON values, or full entity/child graphs.
        await foreach (var id in Db.SetScanAsync(TimeTickerIdsKey, pageSize: 128).WithCancellation(cancellationToken))
        {
            if (!Guid.TryParse(id.ToString(), out var guid)) continue;
            var value = await Db.StringGetAsync(TimeTickerKey(guid)).WaitAsync(cancellationToken).ConfigureAwait(false);
            if (!value.IsNull) RedisMonitoringReader.Visit((byte[])value, function, true, row =>
            {
                if (row.FunctionMatches) collector.Add(row.Execution(false));
            }, cancellationToken);
        }
        if (ids.Count > 0)
        {
            await foreach (var id in Db.SetScanAsync(CronOccurrenceIdsKey, pageSize: 128).WithCancellation(cancellationToken))
            {
                if (!Guid.TryParse(id.ToString(), out var guid)) continue;
                var value = await Db.StringGetAsync(CronOccurrenceKey(guid)).WaitAsync(cancellationToken).ConfigureAwait(false);
                if (!value.IsNull) RedisMonitoringReader.Visit((byte[])value, function, false, row =>
                {
                    if (ids.Contains(row.CronTickerId) && row.ChainRootId == null) collector.Add(row.Execution(true));
                }, cancellationToken);
            }
        }
        return collector.Build();
    }
}
