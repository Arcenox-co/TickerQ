using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Models;

namespace TickerQ.Utilities.Interfaces.Managers
{
    public interface ICronTickerManager<TCronTicker> where TCronTicker : CronTickerEntity
    {
        Task<TickerResult<TCronTicker>> AddAsync(TCronTicker entity, CancellationToken cancellationToken = default);

        /// <summary>
        /// Validates and tracks a new recurring schedule in the scoped application DbContext
        /// configured by UseApplicationDbContext. The caller must save and commit that context.
        /// Schedule-cache invalidation and scheduler/dashboard notifications happen after commit.
        /// Throws on validation/configuration errors; the returned entity is not yet persisted.
        /// </summary>
        Task<TCronTicker> StageAsync(TCronTicker entity, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException(
                "StageAsync requires the EF Core UseApplicationDbContext<TContext> integration and a scoped manager. " +
                "Use AddAsync for independent scheduling.");

        Task<TickerResult<TCronTicker>> UpdateAsync(TCronTicker cronTicker,
            CancellationToken cancellationToken = default);
        Task<TickerResult<TCronTicker>> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
        
        // Batch operations
        Task<TickerResult<List<TCronTicker>>> AddBatchAsync(List<TCronTicker> entities, CancellationToken cancellationToken = default);
        Task<TickerResult<List<TCronTicker>>> UpdateBatchAsync(List<TCronTicker> cronTickers, CancellationToken cancellationToken = default);
        Task<TickerResult<TCronTicker>> DeleteBatchAsync(List<Guid> ids, CancellationToken cancellationToken = default);
    }
}
