using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Models;

namespace TickerQ.Utilities.Interfaces.Managers
{
    /// <summary>
    /// Manager for periodic tickers - tasks that execute at regular intervals.
    /// </summary>
    /// <typeparam name="TPeriodicTicker">The periodic ticker entity type.</typeparam>
    public interface IPeriodicTickerManager<TPeriodicTicker> where TPeriodicTicker : PeriodicTickerEntity
    {
        /// <summary>
        /// Adds a new periodic ticker.
        /// </summary>
        /// <param name="entity">The periodic ticker to add.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Result containing the added ticker.</returns>
        Task<TickerResult<TPeriodicTicker>> AddAsync(TPeriodicTicker entity, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Updates an existing periodic ticker.
        /// </summary>
        /// <param name="periodicTicker">The periodic ticker to update.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Result containing the updated ticker.</returns>
        Task<TickerResult<TPeriodicTicker>> UpdateAsync(TPeriodicTicker periodicTicker, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Deletes a periodic ticker by ID.
        /// </summary>
        /// <param name="id">The ID of the ticker to delete.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Result of the delete operation.</returns>
        Task<TickerResult<TPeriodicTicker>> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Pauses a periodic ticker (sets IsActive to false).
        /// </summary>
        /// <param name="id">The ID of the ticker to pause.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Result of the pause operation.</returns>
        Task<TickerResult<TPeriodicTicker>> PauseAsync(Guid id, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Resumes a paused periodic ticker (sets IsActive to true).
        /// </summary>
        /// <param name="id">The ID of the ticker to resume.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Result of the resume operation.</returns>
        Task<TickerResult<TPeriodicTicker>> ResumeAsync(Guid id, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Adds multiple periodic tickers in batch.
        /// </summary>
        /// <param name="entities">The tickers to add.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Result containing the added tickers.</returns>
        Task<TickerResult<List<TPeriodicTicker>>> AddBatchAsync(List<TPeriodicTicker> entities, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Updates multiple periodic tickers in batch.
        /// </summary>
        /// <param name="periodicTickers">The tickers to update.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Result containing the updated tickers.</returns>
        Task<TickerResult<List<TPeriodicTicker>>> UpdateBatchAsync(List<TPeriodicTicker> periodicTickers, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Deletes multiple periodic tickers in batch.
        /// </summary>
        /// <param name="ids">The IDs of tickers to delete.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Result of the delete operation.</returns>
        Task<TickerResult<TPeriodicTicker>> DeleteBatchAsync(List<Guid> ids, CancellationToken cancellationToken = default);
    }
}
