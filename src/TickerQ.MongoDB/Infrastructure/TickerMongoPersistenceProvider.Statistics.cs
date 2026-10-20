using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MongoDB.Driver;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;

namespace TickerQ.MongoDB.Infrastructure;

internal sealed partial class TickerMongoPersistenceProvider<TTimeTicker, TCronTicker> : ITickerDashboardStatisticsProvider
{
    public async Task<IReadOnlyDictionary<TickerStatus, int>> GetOverallJobStatusCountsAsync(CancellationToken cancellationToken = default)
    {
        var time = await _context.TimeTickers.Aggregate()
            .Match(InPartition(Builders<TTimeTicker>.Filter.Empty))
            .Group(row => row.Status, group => new { Status = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var occurrences = await _context.CronTickerOccurrences.Aggregate()
            .Match(InPartition(Builders<CronTickerOccurrenceEntity<TCronTicker>>.Filter.Empty))
            .Group(row => row.Status, group => new { Status = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return time.Concat(occurrences).GroupBy(row => row.Status)
            .ToDictionary(group => group.Key, group => group.Sum(row => row.Count));
    }
}
