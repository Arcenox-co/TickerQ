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

        /// <summary>
        /// Requests cooperative cancellation of one running occurrence in this runtime partition. Pass an
        /// occurrence ID, not a cron definition ID. The occurrence is signalled directly when it runs on
        /// this host; otherwise, when the persistence provider supports cluster control, a cancellation
        /// command is stored for the owning node, which applies it if it still holds the same attempt.
        /// Commands expire after about 30 seconds. AffectedRows is 1 when the occurrence was signalled or
        /// the command was stored, otherwise 0; neither proves that the function has already stopped.
        /// </summary>
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
        
        /// <summary>
        /// Lists the next <paramref name="count"/> run times of a cron ticker, in UTC, after the current
        /// time, applying its time zone and schedule policy. The list is empty while the cron is disabled or
        /// paused. <paramref name="count"/> must be between 1 and 1000.
        /// </summary>
        Task<TickerResult<IReadOnlyList<DateTime>>> GetNextOccurrencesAsync(Guid cronTickerId, int count,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException("This manager does not support listing cron occurrences.");

        /// <summary>
        /// Reads the stored status of a cron occurrence (for example one returned by RunNowAsync) and, once
        /// it has succeeded, the result it published. Returns null when the occurrence does not exist.
        /// </summary>
        Task<TickerOutcome> GetOutcomeAsync(Guid occurrenceId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("This manager does not support reading occurrence outcomes.");

        /// <summary>
        /// Waits until the cron occurrence finishes or <paramref name="timeout"/> elapses, then returns its
        /// outcome. Check IsCompleted: after a timeout the outcome reports the last stored status. Returns null
        /// when the occurrence does not exist. The store is polled, so this works across replicas.
        /// </summary>
        Task<TickerOutcome> WaitForCompletionAsync(Guid occurrenceId, TimeSpan timeout,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException("This manager does not support waiting for occurrence completion.");

        // Batch operations
        Task<TickerResult<List<TCronTicker>>> AddBatchAsync(List<TCronTicker> entities, CancellationToken cancellationToken = default);
        Task<TickerResult<List<TCronTicker>>> UpdateBatchAsync(List<TCronTicker> cronTickers, CancellationToken cancellationToken = default);
        Task<TickerResult<TCronTicker>> DeleteBatchAsync(List<Guid> ids, CancellationToken cancellationToken = default);
    }
}
