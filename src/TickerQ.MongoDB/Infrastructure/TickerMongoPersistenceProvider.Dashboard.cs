using System;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using MongoDB.Driver;

namespace TickerQ.MongoDB.Infrastructure;

internal sealed partial class TickerMongoPersistenceProvider<TTimeTicker, TCronTicker>
{
    private async Task<Expression<Func<TTimeTicker, bool>>> BuildDashboardChildPredicateAsync(
        bool hasChildren, CancellationToken cancellationToken)
    {
        // Children is an ignored navigation in Mongo. Query distinct parent ids only,
        // so the filter/count/page never requires hydrating child payloads or subtrees.
        using var cursor = await _context.TimeTickers.DistinctAsync(x => x.ParentId,
            InPartition(Builders<TTimeTicker>.Filter.Ne(x => x.ParentId, null)),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        var parentIds = (await cursor.ToListAsync(cancellationToken).ConfigureAwait(false))
            .Where(x => x.HasValue).Select(x => x.Value).ToArray();
        return hasChildren ? row => parentIds.Contains(row.Id) : row => !parentIds.Contains(row.Id);
    }
}
