using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces;

namespace TickerQ.EntityFrameworkCore.Infrastructure
{
    internal class TickerEfCorePersistenceProvider<TDbContext, TTimeTicker, TCronTicker> :
        BasePersistenceProvider<TDbContext, TTimeTicker, TCronTicker>,
        ITickerPersistenceProvider<TTimeTicker, TCronTicker>
        where TDbContext : DbContext
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        public TickerEfCorePersistenceProvider(IDbContextFactory<TDbContext> dbContextFactory, ITickerClock clock, SchedulerOptionsBuilder optionsBuilder, ITickerQRedisContext  redisContext) 
            :  base(dbContextFactory, clock, optionsBuilder, redisContext) { }
        
        #region Time_Ticker_Implementations

        public async Task<TTimeTicker> GetTimeTickerById(Guid id, CancellationToken cancellationToken = default)
        {
            await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);;

            return await dbContext.Set<TTimeTicker>()
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task<TTimeTicker[]> GetTimeTickers(Expression<Func<TTimeTicker, bool>> predicate, CancellationToken cancellationToken)
        {
            await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);;

            var baseQuery = dbContext.Set<TTimeTicker>()
                .AsNoTracking();
            
            if (predicate != null)
                baseQuery = baseQuery.Where(predicate);
            
            return await baseQuery
                .OrderByDescending(x => x.ExecutionTime)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task<int> AddTimeTickers(TTimeTicker[] tickers, CancellationToken cancellationToken)
        {
            await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);;
            
            await dbContext.Set<TTimeTicker>()
                .AddRangeAsync(tickers, cancellationToken);
            
            return await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task<int> UpdateTimeTickers(TTimeTicker[] timeTickers, CancellationToken cancellationToken = default)
        {
            await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);;

            dbContext.Set<TTimeTicker>().UpdateRange(timeTickers);
             
            return await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task<int> RemoveTimeTickers(Guid[] timeTickerIds, CancellationToken cancellationToken)
        {
            await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);;
            return await dbContext.Set<TTimeTicker>()
                .Where(x => timeTickerIds.Contains(x.Id))
                .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        }
        #endregion

        #region Cron_Ticker_Implementations

        public async Task<TCronTicker> GetCronTickerById(Guid id, CancellationToken cancellationToken)
        {
            await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);;
            return await dbContext.Set<TCronTicker>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken).ConfigureAwait(false);;
        }

        public async Task<TCronTicker[]> GetCronTickers(Expression<Func<TCronTicker, bool>> predicate,
            CancellationToken cancellationToken)
        {
            await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);;

            var baseQuery = dbContext.Set<TCronTicker>()
                .AsNoTracking();
            
            if (predicate != null)
                baseQuery = baseQuery.Where(predicate);
            
            return await baseQuery
                .OrderByDescending(x => x.CreatedAt)
                .ToArrayAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task<int> InsertCronTickers(TCronTicker[] tickers, CancellationToken cancellationToken)
        {
            await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);;

            await dbContext.Set<TCronTicker>().AddRangeAsync(tickers, cancellationToken).ConfigureAwait(false);
            
            return await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task<int> UpdateCronTickers(TCronTicker[] cronTickers, CancellationToken cancellationToken = default)
        {
            await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);;

            dbContext.Set<TCronTicker>().UpdateRange(cronTickers);
            
            return await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task<int> RemoveCronTickers(Guid[] cronTickerIds, CancellationToken cancellationToken)
        {
            await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            return await dbContext.Set<TCronTicker>().Where(x => cronTickerIds.Contains(x.Id))
                .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        }

        #endregion

        #region Cron_TickerOccurrence_Implementations
        public async Task<CronTickerOccurrenceEntity<TCronTicker>[]> GetAllCronTickerOccurrences(Expression<Func<CronTickerOccurrenceEntity<TCronTicker>, bool>> predicate, CancellationToken cancellationToken = default)
        {
            await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var cronTickerOccurrenceContext = dbContext.Set<CronTickerOccurrenceEntity<TCronTicker>>()
                .AsNoTracking();

            var query = predicate == null
                ? cronTickerOccurrenceContext.Include(x => x.CronTicker)
                : cronTickerOccurrenceContext.Include(x => x.CronTicker).Where(predicate);
            
            return await query.OrderByDescending(x => x.ExecutionTime).ToArrayAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task<int> InsertCronTickerOccurrences(CronTickerOccurrenceEntity<TCronTicker>[] cronTickerOccurrences, CancellationToken cancellationToken = default)
        {
            await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

            await dbContext.Set<CronTickerOccurrenceEntity<TCronTicker>>().AddRangeAsync(cronTickerOccurrences, cancellationToken).ConfigureAwait(false);

            return await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task<int> RemoveCronTickerOccurrences(Guid[] cronTickerOccurrences, CancellationToken cancellationToken = default)
        {
            await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            return await dbContext.Set<CronTickerOccurrenceEntity<TCronTicker>>()
                .Where(x => cronTickerOccurrences.Contains(x.Id))
                .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        }

        #endregion
    }
}