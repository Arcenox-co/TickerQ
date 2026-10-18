using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Integrations;

namespace TickerQ.Provider;

internal partial class TickerInMemoryPersistenceProvider<TTimeTicker, TCronTicker>
    where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
    where TCronTicker : CronTickerEntity, new()
{
    public Task<TickerMonitoringSnapshot> ReadMonitoringSnapshotAsync(
        string function, DateTime afterUtc, DateTime throughUtc, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(function);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ReadGraph(() =>
        {
            var ids = new HashSet<Guid>();
            // Enumerate the dictionaries directly: Values would allocate a snapshot of all rows.
            foreach (var pair in CronTickers)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (pair.Value.Function == function) ids.Add(pair.Key);
                if (ids.Count > TickerMonitoringSnapshot.MaxCronDefinitions) return TickerMonitoringCollector.TooManyDefinitions();
            }
            var collector = new TickerMonitoringCollector(afterUtc, throughUtc);
            foreach (var pair in TimeTickers)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var row = pair.Value;
                if (row.Function != function) continue;
                collector.Add(new()
                {
                    Id = row.Id, Status = row.Status, CompletedUtc = row.ExecutedAt,
                    ElapsedMilliseconds = row.ElapsedTime, Generation = row.AcquisitionToken ?? row.ChainGeneration
                });
            }
            foreach (var pair in CronOccurrences)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var row = pair.Value;
                if (!ids.Contains(row.CronTickerId) || row.ChainRootId != null) continue;
                collector.Add(new()
                {
                    Id = row.Id, IsCron = true, Status = row.Status, CompletedUtc = row.ExecutedAt,
                    ElapsedMilliseconds = row.ElapsedTime, Generation = row.AcquisitionToken
                });
            }
            return collector.Build();
        }));
    }
}
