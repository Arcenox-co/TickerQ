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

        /// <summary>
        /// Idempotent add keyed by <paramref name="initIdentifier"/>: creates the
        /// ticker only if no time ticker with that identifier exists yet, otherwise
        /// returns the existing one untouched. Made for startup seeding
        /// (<c>UseTickerSeeder</c>) so re-running the seeder on every app start
        /// doesn't create duplicate one-off tickers. Best-effort check-then-insert —
        /// concurrent first-time seeding from multiple nodes can still race.
        /// </summary>
        Task<TickerResult<TTimeTicker>> AddOnceAsync(string initIdentifier, TTimeTicker entity, CancellationToken cancellationToken = default);
        Task<TickerResult<TTimeTicker>> UpdateAsync(TTimeTicker timeTicker, CancellationToken cancellationToken = default);
        Task<TickerResult<TTimeTicker>> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

        /// <summary>
        /// Atomically replaces the chain aggregate rooted at <paramref name="oldRootId"/> with
        /// <paramref name="newRoot"/> (root plus its whole descendant tree). The complete
        /// replacement is validated and persisted before the original is removed, all within a
        /// single transaction: on any validation or persistence failure nothing is deleted and
        /// the original aggregate is left fully intact. This exists so a chain edit never has to
        /// delete-then-create, which loses the original if the recreate fails.
        /// </summary>
        Task<TickerResult<TTimeTicker>> ReplaceChainAsync(Guid oldRootId, TTimeTicker newRoot, CancellationToken cancellationToken = default);

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
