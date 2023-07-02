using Microsoft.EntityFrameworkCore;
using System;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.EntityFrameworkCore.Entities;
using TickerQ.Utilities.Models;

namespace TickerQ.EntityFrameworkCore.Src
{
    public interface ITimeTickerManager<TTimeTicker> where TTimeTicker : TimeTicker
    {
        DbSet<TTimeTicker> TimeTickers { get; }
        Task<TickerResult<TTimeTicker>> AddAsync(TTimeTicker entity, CancellationToken cancellationToken = default);
        Task<TickerResult<TTimeTicker>> UpdateAsync(TTimeTicker entity, CancellationToken cancellationToken = default);
        Task<TickerResult<TTimeTicker>> DeleteAsync(Guid Id, CancellationToken cancellationToken = default);
    }
}
