using Microsoft.EntityFrameworkCore;
using System;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.EntityFrameworkCore.Entities;
using TickerQ.EntityFrameworkCore.Entities.BaseEntity;
using TickerQ.Utilities.Models;

namespace TickerQ.EntityFrameworkCore
{
    public interface ITickerManager
    {
        DbSet<TimeTicker> TimeTickers { get; }
        DbSet<CronTicker> CronTickers { get; }
        DbSet<CronTickerOccurrence> CronTickerOccurrences { get; }
        Task<TickerResult<TEntity>> AddTickerAsync<TEntity>(TEntity entity, CancellationToken cancellationToken = default) where TEntity : BaseTickerEntity;
        Task<TickerResult<TEntity>> UpdateTickerAsync<TEntity>(TEntity entity, CancellationToken cancellationToken = default) where TEntity : BaseTickerEntity;
        Task<TickerResult<TEntity>> DeleteTickerAsync<TEntity>(Guid Id, CancellationToken cancellationToken = default) where TEntity : BaseTickerEntity;
    }
}
