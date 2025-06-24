using System;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Models;
using TickerQ.Utilities.Models.Ticker;

namespace TickerQ.Utilities.Interfaces.Managers
{
    public interface ICronTickerManager<TCronTicker> where TCronTicker : CronTicker
    {
        Task<TickerResult<TCronTicker>> AddAsync(TCronTicker entity, CancellationToken cancellationToken = default);
        Task<TickerResult<TCronTicker>> UpdateAsync(Guid id, Action<TCronTicker> updateAction, CancellationToken cancellationToken = default);
        Task<TickerResult<TCronTicker>> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    }
}