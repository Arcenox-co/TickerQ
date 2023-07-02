using Microsoft.EntityFrameworkCore;
using System;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.EntityFrameworkCore.Entities;
using TickerQ.Utilities.Models;

namespace TickerQ.EntityFrameworkCore
{
    public interface ICronTickerManager<TCronTicker> where TCronTicker : CronTicker
    {
        DbSet<TCronTicker> CronTickers { get; }
        DbSet<CronTickerOccurrence<TCronTicker>> CronTickerOccurrences { get; }
        Task<TickerResult<TCronTicker>> AddAsync(TCronTicker entity, CancellationToken cancellationToken = default);
        Task<TickerResult<TCronTicker>> UpdateAsync(TCronTicker entity, CancellationToken cancellationToken = default);
        Task<TickerResult<TCronTicker>> DeleteAsync(Guid Id, CancellationToken cancellationToken = default);
    }
}
