using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

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
                .Include(x => x.Children)
                .FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task<TTimeTicker[]> GetTimeTickers(Expression<Func<TTimeTicker, bool>> predicate, CancellationToken cancellationToken)
        {
            await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);;

            var baseQuery = dbContext.Set<TTimeTicker>()
                .Include(x => x.Children)
                .ThenInclude(x => x.Children)
                .AsNoTracking();
            
            if (predicate != null)
                baseQuery = baseQuery.Where(predicate);
            
            return await baseQuery
                .Where(x => x.ParentId == null)
                .OrderByDescending(x => x.ExecutionTime)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        
        public async Task<PaginationResult<TTimeTicker>> GetTimeTickersPaginated(
            Expression<Func<TTimeTicker, bool>> predicate, 
            int pageNumber, 
            int pageSize, 
            CancellationToken cancellationToken)
        {
            await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

            var baseQuery = dbContext.Set<TTimeTicker>()
                .Include(x => x.Children)
                .ThenInclude(x => x.Children)
                .AsNoTracking();
            
            if (predicate != null)
                baseQuery = baseQuery.Where(predicate);
            
            baseQuery = baseQuery
                .Where(x => x.ParentId == null)
                .OrderByDescending(x => x.ExecutionTime);
            
            return await baseQuery.ToPaginatedListAsync(pageNumber, pageSize, cancellationToken).ConfigureAwait(false);
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
            
            // Load the entities to be deleted (including children for cascade delete)
            var idList = timeTickerIds.ToList();
            var tickersToDelete = await dbContext.Set<TTimeTicker>()
                .Include(x => x.Children)
                .ThenInclude(x => x.Children) // Include grandchildren if needed
                .Where(x => EF.Constant(idList).Contains(x.Id))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            // Remove using Entity Framework (respects cascade delete configuration)
            dbContext.Set<TTimeTicker>().RemoveRange(tickersToDelete);
            
            return await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
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
        
        public async Task<PaginationResult<TCronTicker>> GetCronTickersPaginated(
            Expression<Func<TCronTicker, bool>> predicate, 
            int pageNumber, 
            int pageSize, 
            CancellationToken cancellationToken)
        {
            await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

            var baseQuery = dbContext.Set<TCronTicker>()
                .AsNoTracking();
            
            if (predicate != null)
                baseQuery = baseQuery.Where(predicate);
            
            baseQuery = baseQuery.OrderByDescending(x => x.CreatedAt);
            
            return await baseQuery.ToPaginatedListAsync(pageNumber, pageSize, cancellationToken).ConfigureAwait(false);
        }

        public async Task<int> InsertCronTickers(TCronTicker[] tickers, CancellationToken cancellationToken)
        {
            await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);;

            await dbContext.Set<TCronTicker>().AddRangeAsync(tickers, cancellationToken).ConfigureAwait(false);
            
            var result = await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            
            if(RedisContext.HasRedisConnection)
                await RedisContext.DistributedCache.RemoveAsync("cron:expressions", cancellationToken).ConfigureAwait(false);
            
            return result;
        }

        public async Task<int> UpdateCronTickers(TCronTicker[] cronTickers, CancellationToken cancellationToken = default)
        {
            await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);;

            dbContext.Set<TCronTicker>().UpdateRange(cronTickers);

            var result =  await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            
            if(RedisContext.HasRedisConnection)
                await RedisContext.DistributedCache.RemoveAsync("cron:expressions", cancellationToken).ConfigureAwait(false);
            
            return result;
        }

        public async Task<int> RemoveCronTickers(Guid[] cronTickerIds, CancellationToken cancellationToken)
        {
            await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var idList = cronTickerIds.ToList();
            var result = await dbContext.Set<TCronTicker>().Where(x => EF.Constant(idList).Contains(x.Id))
                .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            
            if(RedisContext.HasRedisConnection)
                await RedisContext.DistributedCache.RemoveAsync("cron:expressions", cancellationToken).ConfigureAwait(false);
            
            return result;
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
        
        public async Task<PaginationResult<CronTickerOccurrenceEntity<TCronTicker>>> GetAllCronTickerOccurrencesPaginated(
            Expression<Func<CronTickerOccurrenceEntity<TCronTicker>, bool>> predicate, 
            int pageNumber, 
            int pageSize, 
            CancellationToken cancellationToken)
        {
            await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            
            var baseQuery = dbContext.Set<CronTickerOccurrenceEntity<TCronTicker>>()
                .Include(x => x.CronTicker)
                .AsNoTracking();

            if (predicate != null)
                baseQuery = baseQuery.Where(predicate);
            
            baseQuery = baseQuery.OrderByDescending(x => x.ExecutionTime);
            
            return await baseQuery.ToPaginatedListAsync(pageNumber, pageSize, cancellationToken).ConfigureAwait(false);
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
            var idList = cronTickerOccurrences.ToList();
            return await dbContext.Set<CronTickerOccurrenceEntity<TCronTicker>>()
                .Where(x => EF.Constant(idList).Contains(x.Id))
                .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task<CronTickerOccurrenceEntity<TCronTicker>[]> AcquireImmediateCronOccurrencesAsync(Guid[] occurrenceIds, CancellationToken cancellationToken = default)
        {
            if (occurrenceIds == null || occurrenceIds.Length == 0)
                return Array.Empty<CronTickerOccurrenceEntity<TCronTicker>>();

            await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var now = _clock.UtcNow;
            var idList = occurrenceIds.ToList();

            // Only acquire occurrences that are acquirable (Idle/Queued and not locked by another node)
            var query = dbContext.Set<CronTickerOccurrenceEntity<TCronTicker>>()
                .Where(x => EF.Constant(idList).Contains(x.Id))
                .WhereCanAcquire(_lockHolder);

            // Lock and mark InProgress
            var affected = await query
                .ExecuteUpdateAsync(setter => setter
                    .SetProperty(x => x.LockHolder, _lockHolder)
                    .SetProperty(x => x.LockedAt, now)
                    .SetProperty(x => x.Status, TickerStatus.InProgress)
                    .SetProperty(x => x.UpdatedAt, now), cancellationToken)
                .ConfigureAwait(false);

            if (affected == 0)
                return Array.Empty<CronTickerOccurrenceEntity<TCronTicker>>();

            // Return acquired occurrences with CronTicker populated
            return await dbContext.Set<CronTickerOccurrenceEntity<TCronTicker>>()
                .AsNoTracking()
                .Where(x => EF.Constant(idList).Contains(x.Id) && x.LockHolder == _lockHolder && x.Status == TickerStatus.InProgress)
                .Include(x => x.CronTicker)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        #endregion
    }
}
