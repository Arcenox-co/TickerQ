using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Entities;
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
    }
}
