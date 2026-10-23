using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Managers;
using TickerQ.Utilities.Models;

namespace TickerQ.Utilities.Interfaces.Managers
{
    public interface ITimeTickerManager<TTimeTicker> where TTimeTicker : TimeTickerEntity<TTimeTicker>
    {
        Task<TickerResult<TTimeTicker>> AddAsync(TTimeTicker entity, CancellationToken cancellationToken = default);

        /// <summary>
        /// Builds and persists a durable job graph. Use Then for a linear path and ThenMany for
        /// sibling branches. Every step is stored and executed as a normal time ticker.
        /// </summary>
        Task<TickerResult<TTimeTicker>> AddChainAsync(
            Action<TimeTickerChainBuilder<TTimeTicker>> configure,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException("This manager does not support fluent chain creation.");

        /// <summary>
        /// Validates and tracks a new ticker in the scoped application DbContext configured by
        /// UseApplicationDbContext. The caller must save and commit that context. Nothing is
        /// dispatched before commit. Throws on validation/configuration errors; the returned
        /// entity is staged, not yet persisted. Unsupported providers fail immediately.
        /// </summary>
        Task<TTimeTicker> StageAsync(TTimeTicker entity, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException(
                "StageAsync requires the EF Core UseApplicationDbContext<TContext> integration and a scoped manager. " +
                "Use AddAsync for independent scheduling.");

        /// <summary>
        /// Idempotent add keyed by <paramref name="initIdentifier"/>: creates the
        /// ticker only if no time ticker with that identifier exists yet, otherwise
        /// returns the existing one untouched. Made for startup seeding
        /// (<c>UseTickerSeeder</c>) so re-running the seeder on every app start
        /// doesn't create duplicate one-off tickers. Built-in durable providers converge concurrent
        /// first-time seeding on a deterministic identity and return the committed winner.
        /// </summary>
        Task<TickerResult<TTimeTicker>> AddOnceAsync(string initIdentifier, TTimeTicker entity, CancellationToken cancellationToken = default);
        Task<TickerResult<TTimeTicker>> UpdateAsync(TTimeTicker timeTicker, CancellationToken cancellationToken = default);
        Task<TickerResult<TTimeTicker>> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
        /// <summary>Atomically admits a root ticker to run now. An active ticker or chain child cannot be acquired. Success means accepted, not completed; AffectedRows is 1.</summary>
        Task<TickerResult<TTimeTicker>> RunNowAsync(Guid id, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("This manager does not support running time tickers on demand.");
        /// <summary>
        /// Retries the selected job in its existing chain. Completed ancestors and unaffected grouped
        /// branches are reused, while the selected job and every affected downstream condition are
        /// evaluated again under a fresh execution generation. No standalone ticker is created.
        /// </summary>
        Task<TickerResult<TTimeTicker>> RetryFromAsync(Guid id, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("This manager does not support retrying a chain from a selected job.");
        /// <summary>Requests cooperative cancellation of one running execution on this host and runtime partition. Use CancelAsync for a pending job. AffectedRows is 1 when signalled, otherwise 0.</summary>
        Task<TickerResult<TTimeTicker>> RequestCancellationAsync(Guid id, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("This manager does not support cancelling time tickers.");
        /// <summary>Changes only the root's deletion policy, including for completed chains.</summary>
        Task<TickerResult<TTimeTicker>> SetDeleteBehaviorAsync(Guid rootId, ChainDeleteBehavior behavior, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("This manager does not support updating chain deletion policies.");

        /// <summary>
        /// Cancels a persisted Idle or Queued ticker and its pending descendants atomically.
        /// Running and terminal tickers are not changed. AffectedRows is the number cancelled;
        /// zero means the selected ticker was missing or no longer pending. Records are retained.
        /// This is an independent store operation, not a staged DbContext mutation.
        /// </summary>
        Task<TickerResult<TTimeTicker>> CancelAsync(Guid id, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("This manager does not support pending ticker cancellation.");

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

        /// <summary>Schedules a function using a typed one-time schedule and optional availability policy.</summary>
        Task<TickerResult<TTimeTicker>> AddAsync<TFunction>(TimeSchedule schedule,
            CancellationToken cancellationToken = default)
            where TFunction : class, ITickerFunction
            => throw new NotSupportedException("This manager does not support typed time schedules.");

        /// <summary>Schedules a typed request using a typed one-time schedule and optional availability policy.</summary>
        Task<TickerResult<TTimeTicker>> AddAsync<TFunction, TRequest>(TimeSchedule schedule, TRequest request,
            CancellationToken cancellationToken = default)
            where TFunction : class, ITickerFunction<TRequest>
            => throw new NotSupportedException("This manager does not support typed time schedules.");
    }
}
