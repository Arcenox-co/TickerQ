using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;

namespace TickerQ.EntityFrameworkCore.Infrastructure;

internal partial class TickerEfCorePersistenceProvider<TDbContext, TTimeTicker, TCronTicker> : ITickerDashboardStatisticsProvider
{
    public async Task<IReadOnlyDictionary<TickerStatus, int>> GetOverallJobStatusCountsAsync(CancellationToken cancellationToken = default)
    {
        using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var time = await session.Context.Set<TTimeTicker>()
            .Where(row => row.ApplicationNamespaceKey == _runtimePartitionKey)
            .GroupBy(row => row.Status).Select(group => new { Status = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.Status, row => row.Count, cancellationToken).ConfigureAwait(false);
        var occurrences = await session.Context.Set<CronTickerOccurrenceEntity<TCronTicker>>()
            .Where(row => row.ApplicationNamespaceKey == _runtimePartitionKey)
            .GroupBy(row => row.Status).Select(group => new { Status = group.Key, Count = group.Count() })
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
        foreach (var row in occurrences) time[row.Status] = time.GetValueOrDefault(row.Status) + row.Count;
        return time;
    }
}
