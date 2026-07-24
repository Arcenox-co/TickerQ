using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Models;

namespace TickerQ.Utilities.Interfaces
{
    public interface ITickerPersistenceProvider<TTimeTicker, TCronTicker>
    where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
    where TCronTicker : CronTickerEntity, new()
    {
        #region Time_Ticker_Core_Methods
        IAsyncEnumerable<TimeTickerEntity> QueueTimeTickers(TimeTickerEntity[] timeTickers, CancellationToken cancellationToken = default);
        IAsyncEnumerable<TimeTickerEntity> QueueTimedOutTimeTickers(CancellationToken cancellationToken = default);
        Task ReleaseAcquiredTimeTickers(Guid[] timeTickerIds, CancellationToken cancellationToken = default);
        Task<TimeTickerEntity[]> GetEarliestTimeTickers(CancellationToken cancellationToken = default);
        Task<int> UpdateTimeTicker(InternalFunctionContext functionContext, CancellationToken cancellationToken = default);
        Task<byte[]> GetTimeTickerRequest(Guid id, CancellationToken cancellationToken);
        Task UpdateTimeTickersWithUnifiedContext(Guid[] timeTickerIds, InternalFunctionContext functionContext, CancellationToken cancellationToken = default);
        Task<TimeTickerEntity[]> AcquireImmediateTimeTickersAsync(Guid[] ids, CancellationToken cancellationToken = default);
        #endregion
        
        #region Cron_Ticker_Core_Methods
        Task MigrateDefinedCronTickers((string Function, string Expression)[] cronTickers, CancellationToken cancellationToken = default);
        Task<CronTickerEntity[]> GetAllCronTickerExpressions(CancellationToken cancellationToken);
        Task ReleaseDeadNodeTimeTickerResources(string instanceIdentifier, CancellationToken cancellationToken = default);
        #endregion
        
        #region Cron_TickerOccurrence_Core_Methods
        Task<CronTickerOccurrenceEntity<TCronTicker>> GetEarliestAvailableCronOccurrence(Guid[] ids, CancellationToken cancellationToken = default);
        IAsyncEnumerable<CronTickerOccurrenceEntity<TCronTicker>> QueueCronTickerOccurrences((DateTime Key, InternalManagerContext[] Items) cronTickerOccurrences, CancellationToken cancellationToken = default);
        IAsyncEnumerable<CronTickerOccurrenceEntity<TCronTicker>> QueueTimedOutCronTickerOccurrences(CancellationToken cancellationToken = default);
        Task UpdateCronTickerOccurrence(InternalFunctionContext functionContext, CancellationToken cancellationToken = default);
        Task ReleaseAcquiredCronTickerOccurrences(Guid[] occurrenceIds, CancellationToken cancellationToken = default);
        Task<byte[]> GetCronTickerOccurrenceRequest(Guid tickerId, CancellationToken cancellationToken = default);
        Task UpdateCronTickerOccurrencesWithUnifiedContext(Guid[] timeTickerIds, InternalFunctionContext functionContext, CancellationToken cancellationToken = default);
        Task ReleaseDeadNodeOccurrenceResources(string instanceIdentifier, CancellationToken cancellationToken = default);
        Task<int> SkipStaleCronOccurrencesAsync(TimeSpan staleThreshold, CancellationToken cancellationToken = default) => Task.FromResult(0);
        #endregion

        #region Stale_Job_Recovery
        // Default implementations keep external providers (e.g. Redis) compiling;
        // they opt in by overriding. Renewals return the number of rows actually
        // renewed so the caller can detect lost leases (fewer than requested).
        Task<int> RenewTimeTickerLeases(Guid[] timeTickerIds, DateTime leaseUntil, CancellationToken cancellationToken = default)
            => Task.FromResult(timeTickerIds?.Length ?? 0);
        Task<int> RenewCronTickerOccurrenceLeases(Guid[] occurrenceIds, DateTime leaseUntil, CancellationToken cancellationToken = default)
            => Task.FromResult(occurrenceIds?.Length ?? 0);
        /// <summary>Of the given ids, returns those still held (InProgress + locked) by this node.</summary>
        Task<Guid[]> GetStillHeldTickerIds(Guid[] timeTickerIds, Guid[] occurrenceIds, CancellationToken cancellationToken = default)
        {
            var all = new List<Guid>((timeTickerIds?.Length ?? 0) + (occurrenceIds?.Length ?? 0));
            if (timeTickerIds != null) all.AddRange(timeTickerIds);
            if (occurrenceIds != null) all.AddRange(occurrenceIds);
            return Task.FromResult(all.ToArray());
        }
        /// <summary>
        /// Applies the OnStale policy to InProgress tickers whose lease expired:
        /// Restart (bounded by <paramref name="maxStaleRestarts"/>) resets them to
        /// Idle for re-acquisition; Cancel (or exhausted restarts) marks them
        /// Cancelled with a stale reason.
        /// </summary>
        Task<StaleTickerRecoveryResult> RecoverStaleTickers(int maxStaleRestarts, CancellationToken cancellationToken = default)
            => Task.FromResult(new StaleTickerRecoveryResult());
        #endregion
        
        #region Queryable
        ITickerQueryable<TTimeTicker> TimeTickersQuery();
        ITickerQueryable<TCronTicker> CronTickersQuery();
        ITickerQueryable<CronTickerOccurrenceEntity<TCronTicker>> CronTickerOccurrencesQuery();
        #endregion

        #region Time_Ticker_Shared_Methods
        Task<TTimeTicker> GetTimeTickerById(Guid id, CancellationToken cancellationToken = default);
        Task<TTimeTicker[]> GetTimeTickers(Expression<Func<TTimeTicker, bool>> predicate, CancellationToken cancellationToken = default);
        Task<PaginationResult<TTimeTicker>> GetTimeTickersPaginated(Expression<Func<TTimeTicker, bool>> predicate, int pageNumber, int pageSize, CancellationToken cancellationToken = default);
        Task<int> AddTimeTickers(TTimeTicker[] tickers, CancellationToken cancellationToken = default);
        Task<int> UpdateTimeTickers(TTimeTicker[] tickers, CancellationToken cancellationToken = default);
        Task<int> RemoveTimeTickers(Guid[] tickerIds, CancellationToken cancellationToken = default);
        #endregion

        #region Cron_Ticker_Shared_Methods
        Task<TCronTicker> GetCronTickerById(Guid id, CancellationToken cancellationToken);
        Task<TCronTicker[]> GetCronTickers(Expression<Func<TCronTicker, bool>> predicate, CancellationToken cancellationToken);
        Task<PaginationResult<TCronTicker>> GetCronTickersPaginated(Expression<Func<TCronTicker, bool>> predicate, int pageNumber, int pageSize, CancellationToken cancellationToken = default);
        Task<int> InsertCronTickers(TCronTicker[] tickers, CancellationToken cancellationToken);
        Task<int> UpdateCronTickers(TCronTicker[] cronTicker, CancellationToken cancellationToken);
        Task<int> RemoveCronTickers(Guid[] cronTickerIds, CancellationToken cancellationToken);
        #endregion
        
        #region Cron_TickerOccurrence_Shared_Methods
        Task<CronTickerOccurrenceEntity<TCronTicker>[]> GetAllCronTickerOccurrences(Expression<Func<CronTickerOccurrenceEntity<TCronTicker>, bool>> predicate, CancellationToken cancellationToken = default);
        Task<PaginationResult<CronTickerOccurrenceEntity<TCronTicker>>> GetAllCronTickerOccurrencesPaginated(Expression<Func<CronTickerOccurrenceEntity<TCronTicker>, bool>> predicate, int pageNumber, int pageSize, CancellationToken cancellationToken = default);
        Task<int> InsertCronTickerOccurrences(CronTickerOccurrenceEntity<TCronTicker>[] cronTickerOccurrences, CancellationToken cancellationToken);
        Task<int> RemoveCronTickerOccurrences(Guid[] cronTickerOccurrences, CancellationToken cancellationToken);
        Task<CronTickerOccurrenceEntity<TCronTicker>[]> AcquireImmediateCronOccurrencesAsync(Guid[] occurrenceIds, CancellationToken cancellationToken = default);
        #endregion
    }
}
