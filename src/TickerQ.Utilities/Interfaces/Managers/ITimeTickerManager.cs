using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ.Utilities.Interfaces.Managers
{
    public interface ITimeTickerManager<TTimeTicker> where TTimeTicker : TimeTickerEntity<TTimeTicker>
    {
        Task<TickerResult<TTimeTicker>> AddAsync(TTimeTicker entity, CancellationToken cancellationToken = default);
        Task<TickerResult<TTimeTicker>> UpdateAsync(TTimeTicker timeTicker, CancellationToken cancellationToken = default);
        Task<TickerResult<TTimeTicker>> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

        // Batch operations
        Task<TickerResult<List<TTimeTicker>>> AddBatchAsync(List<TTimeTicker> entities, CancellationToken cancellationToken = default);
        Task<TickerResult<List<TTimeTicker>>> UpdateBatchAsync(List<TTimeTicker> timeTickers, CancellationToken cancellationToken = default);
        Task<TickerResult<TTimeTicker>> DeleteBatchAsync(List<Guid> ids, CancellationToken cancellationToken = default);

        /// <summary>
        /// Schedules a ticker function by type (registered via MapTicker&lt;T&gt;). No request payload.
        /// </summary>
        Task<TickerResult<TTimeTicker>> AddAsync<TFunction>(DateTime? executionTime = null, CancellationToken cancellationToken = default)
            where TFunction : class, ITickerFunction;

        /// <summary>
        /// Schedules a ticker function by type with a typed request payload.
        /// </summary>
        Task<TickerResult<TTimeTicker>> AddAsync<TFunction, TRequest>(DateTime? executionTime, TRequest request, CancellationToken cancellationToken = default)
            where TFunction : class, ITickerFunction<TRequest>;
    }
}
