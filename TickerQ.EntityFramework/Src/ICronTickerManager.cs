using System;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.EntityFrameworkCore.Entities;
using TickerQ.Utilities.Models;

namespace TickerQ.EntityFrameworkCore
{
    public interface ICronTickerManager<TCronTicker> where TCronTicker : CronTicker
    {
        Task<TickerResult<TCronTicker>> AddAsync(TCronTicker entity, CancellationToken cancellationToken = default);
        Task<TickerResult<TCronTicker>> UpdateAsync(TCronTicker entity, CancellationToken cancellationToken = default);
        Task<TickerResult<TCronTicker>> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    }
}
