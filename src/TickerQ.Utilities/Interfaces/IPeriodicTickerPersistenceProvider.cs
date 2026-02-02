using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Models;

namespace TickerQ.Utilities.Interfaces
{

    /// <summary>
    /// Persistence provider for periodic tickers.
    /// Separated from main ITickerPersistenceProvider to maintain backward compatibility.
    /// </summary>
    /// <typeparam name="TPeriodicTicker">The periodic ticker entity type.</typeparam>
    public interface IPeriodicTickerPersistenceProvider<TPeriodicTicker>
        where TPeriodicTicker : PeriodicTickerEntity, new()
    {
        #region Periodic_Ticker_Core_Methods
        
        /// <summary>
        /// Gets all active periodic tickers for scheduling.
        /// </summary>
        Task<PeriodicTickerEntity[]> GetAllActivePeriodicTickers(CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Gets the earliest available periodic ticker occurrence.
        /// </summary>
        Task<PeriodicTickerOccurrenceEntity<TPeriodicTicker>> GetEarliestAvailablePeriodicOccurrence(Guid[] ids, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Queues periodic ticker occurrences for execution.
        /// </summary>
        IAsyncEnumerable<PeriodicTickerOccurrenceEntity<TPeriodicTicker>> QueuePeriodicTickerOccurrences(
            (DateTime Key, InternalManagerContext[] Items) periodicTickerOccurrences, 
            CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Queues timed out periodic ticker occurrences for fallback processing.
        /// </summary>
        IAsyncEnumerable<PeriodicTickerOccurrenceEntity<TPeriodicTicker>> QueueTimedOutPeriodicTickerOccurrences(CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Updates a periodic ticker occurrence after execution.
        /// </summary>
        Task UpdatePeriodicTickerOccurrence(InternalFunctionContext functionContext, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Updates the parent periodic ticker after occurrence execution (LastExecutedAt, ExecutionCount).
        /// </summary>
        Task UpdatePeriodicTickerAfterExecution(Guid periodicTickerId, DateTime executedAt, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Releases locked periodic ticker occurrences.
        /// </summary>
        Task ReleaseAcquiredPeriodicTickerOccurrences(Guid[] occurrenceIds, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Gets the request payload for a periodic ticker occurrence.
        /// </summary>
        Task<byte[]> GetPeriodicTickerOccurrenceRequest(Guid tickerId, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Updates multiple periodic ticker occurrences with a unified context.
        /// </summary>
        Task UpdatePeriodicTickerOccurrencesWithUnifiedContext(Guid[] occurrenceIds, InternalFunctionContext functionContext, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Releases resources held by a dead node for periodic tickers.
        /// </summary>
        Task ReleaseDeadNodePeriodicOccurrenceResources(string instanceIdentifier, CancellationToken cancellationToken = default);
        
        #endregion
        
        #region Periodic_Ticker_Shared_Methods
        
        /// <summary>
        /// Gets a periodic ticker by ID.
        /// </summary>
        Task<TPeriodicTicker> GetPeriodicTickerById(Guid id, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Gets periodic tickers matching a predicate.
        /// </summary>
        Task<TPeriodicTicker[]> GetPeriodicTickers(Expression<Func<TPeriodicTicker, bool>> predicate, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Gets paginated periodic tickers.
        /// </summary>
        Task<PaginationResult<TPeriodicTicker>> GetPeriodicTickersPaginated(Expression<Func<TPeriodicTicker, bool>> predicate, int pageNumber, int pageSize, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Inserts new periodic tickers.
        /// </summary>
        Task<int> InsertPeriodicTickers(TPeriodicTicker[] tickers, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Updates existing periodic tickers.
        /// </summary>
        Task<int> UpdatePeriodicTickers(TPeriodicTicker[] tickers, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Removes periodic tickers by ID.
        /// </summary>
        Task<int> RemovePeriodicTickers(Guid[] tickerIds, CancellationToken cancellationToken = default);
        
        #endregion
        
        #region Periodic_TickerOccurrence_Shared_Methods
        
        /// <summary>
        /// Gets all periodic ticker occurrences matching a predicate.
        /// </summary>
        Task<PeriodicTickerOccurrenceEntity<TPeriodicTicker>[]> GetAllPeriodicTickerOccurrences(
            Expression<Func<PeriodicTickerOccurrenceEntity<TPeriodicTicker>, bool>> predicate, 
            CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Gets paginated periodic ticker occurrences.
        /// </summary>
        Task<PaginationResult<PeriodicTickerOccurrenceEntity<TPeriodicTicker>>> GetAllPeriodicTickerOccurrencesPaginated(
            Expression<Func<PeriodicTickerOccurrenceEntity<TPeriodicTicker>, bool>> predicate, 
            int pageNumber, int pageSize, 
            CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Inserts new periodic ticker occurrences.
        /// </summary>
        Task<int> InsertPeriodicTickerOccurrences(PeriodicTickerOccurrenceEntity<TPeriodicTicker>[] occurrences, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Removes periodic ticker occurrences by ID.
        /// </summary>
        Task<int> RemovePeriodicTickerOccurrences(Guid[] occurrenceIds, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Acquires immediate periodic occurrences for execution.
        /// </summary>
        Task<PeriodicTickerOccurrenceEntity<TPeriodicTicker>[]> AcquireImmediatePeriodicOccurrencesAsync(Guid[] occurrenceIds, CancellationToken cancellationToken = default);
        
        #endregion
    }
}
