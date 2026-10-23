using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Managers;
using TickerQ.Utilities.Models;

namespace TickerQ.Utilities.Interfaces.Managers
{
    public interface ICronTickerManager<TCronTicker> where TCronTicker : CronTickerEntity
    {
        Task<TickerResult<TCronTicker>> AddAsync(TCronTicker entity, CancellationToken cancellationToken = default);

        /// <summary>
        /// Builds and persists a recurring chain. Each cron occurrence creates a fresh durable
        /// time-ticker graph from this template.
        /// </summary>
        Task<TickerResult<TCronTicker>> AddChainAsync(
            Action<CronTickerChainBuilder<TCronTicker>> configure,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException("This manager does not support fluent recurring-chain creation.");

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

        /// <summary>Creates and admits a fresh occurrence without changing the schedule. Success means accepted, not completed.</summary>
        Task<TickerResult<CronTickerOccurrenceEntity<TCronTicker>>> RunNowAsync(Guid cronTickerId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("This manager does not support running cron tickers on demand.");

        /// <summary>
        /// Re-runs an existing occurrence in place. Standalone occurrences keep their occurrence id;
        /// recurring chains retry their existing durable graph without creating another occurrence.
        /// </summary>
        Task<TickerResult<CronTickerOccurrenceEntity<TCronTicker>>> RerunOccurrenceAsync(
            Guid occurrenceId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("This manager does not support re-running cron occurrences.");

        /// <summary>Requests cooperative cancellation of one occurrence on this host and runtime partition. Pass an occurrence ID, not a cron definition ID. AffectedRows is 1 when signalled, otherwise 0.</summary>
        Task<TickerResult<TCronTicker>> RequestCancellationAsync(Guid occurrenceId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("This manager does not support cancelling cron occurrences.");

        /// <summary>Enables or disables future scheduling. Does not cancel an active occurrence.</summary>
        Task<TickerResult<TCronTicker>> SetEnabledAsync(Guid id, bool isEnabled, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("This manager does not support changing cron enabled state.");

        /// <summary>Creates a recurring ticker for a function registered with MapTicker&lt;TFunction&gt;.</summary>
        Task<TickerResult<TCronTicker>> AddAsync<TFunction>(string expression, CancellationToken cancellationToken = default)
            where TFunction : class, ITickerFunction
            => throw new NotSupportedException("This manager does not support typed cron creation.");

        /// <summary>Creates a recurring ticker with a validated typed request.</summary>
        Task<TickerResult<TCronTicker>> AddAsync<TFunction, TRequest>(string expression, TRequest request, CancellationToken cancellationToken = default)
            where TFunction : class, ITickerFunction<TRequest>
            => throw new NotSupportedException("This manager does not support typed cron creation.");

        /// <summary>Creates a recurring ticker using a typed schedule, time zone and availability policy.</summary>
        Task<TickerResult<TCronTicker>> AddAsync<TFunction>(CronSchedule schedule,
            CancellationToken cancellationToken = default)
            where TFunction : class, ITickerFunction
            => throw new NotSupportedException("This manager does not support typed cron schedules.");

        /// <summary>Creates a recurring ticker with a typed request and schedule policy.</summary>
        Task<TickerResult<TCronTicker>> AddAsync<TFunction, TRequest>(CronSchedule schedule, TRequest request,
            CancellationToken cancellationToken = default)
            where TFunction : class, ITickerFunction<TRequest>
            => throw new NotSupportedException("This manager does not support typed cron schedules.");
        
        // Batch operations
        Task<TickerResult<List<TCronTicker>>> AddBatchAsync(List<TCronTicker> entities, CancellationToken cancellationToken = default);
        Task<TickerResult<List<TCronTicker>>> UpdateBatchAsync(List<TCronTicker> cronTickers, CancellationToken cancellationToken = default);
        Task<TickerResult<TCronTicker>> DeleteBatchAsync(List<Guid> ids, CancellationToken cancellationToken = default);
    }
}
