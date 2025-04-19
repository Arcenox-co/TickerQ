using System;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.EntityFrameworkCore.Entities;
using TickerQ.Utilities.Models;

namespace TickerQ.EntityFrameworkCore
{
    public interface ITimeTickerManager<TTimeTicker> where TTimeTicker : TimeTicker
    {
        Task<TickerResult<TTimeTicker>> AddAsync(TTimeTicker entity, CancellationToken cancellationToken = default);
        Task<TickerResult<TTimeTicker>> UpdateAsync(Guid id, Action<TTimeTicker> updateAction, CancellationToken cancellationToken = default);
        Task<TickerResult<TTimeTicker>> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    }
}
