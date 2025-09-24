using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ.EntityFrameworkCore.Infrastructure;

internal abstract class BasePersistenceProvider<TDbContext, TTimeTicker, TCronTicker>
    where TDbContext : DbContext
    where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
    where TCronTicker : CronTickerEntity, new()
{
    public BasePersistenceProvider(IDbContextFactory<TDbContext> dbContextFactory, ITickerClock clock, SchedulerOptionsBuilder optionsBuilder, ITickerQRedisContext redisContext)
    {
        _clock = clock;
        RedisContext = redisContext;
        DbContextFactory = dbContextFactory;
        _lockHolder = optionsBuilder.NodeIdentifier;
    }
    
    protected readonly IDbContextFactory<TDbContext>  DbContextFactory;
    private readonly string _lockHolder;
    private readonly ITickerClock _clock;
    protected readonly ITickerQRedisContext RedisContext;
    
    #region Core_Time_Ticker_Methods
    public async IAsyncEnumerable<TimeTickerEntity> QueueTimeTickers(TimeTickerEntity[] timeTickers, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);;
        var context = dbContext.Set<TTimeTicker>();
        var now = _clock.UtcNow;
        
        foreach (var timeTicker in timeTickers)
        {
            cancellationToken.ThrowIfCancellationRequested();
                
            var updatedTicker = await context
                .Where(x => x.Id == timeTicker.Id)
                .Where(x => x.UpdatedAt == timeTicker.UpdatedAt)
                .ExecuteUpdateAsync(prop => prop
                    .SetProperty(x => x.LockHolder, _lockHolder)
                    .SetProperty(x => x.LockedAt, now)
                    .SetProperty(x => x.UpdatedAt, now)
                    .SetProperty(x => x.Status, TickerStatus.Queued), cancellationToken);

            if (updatedTicker <= 0) 
                continue;
                
            timeTicker.UpdatedAt = now;
            timeTicker.LockHolder = _lockHolder;
            timeTicker.LockedAt = now;
            timeTicker.Status = TickerStatus.Queued;
                
            yield return timeTicker;
        }
    }

    public async IAsyncEnumerable<TimeTickerEntity> QueueTimedOutTimeTickers([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);;
        var context = dbContext.Set<TTimeTicker>();
        var nowShifted = _clock.UtcNow.AddSeconds(-1);
        var now = _clock.UtcNow;
            
        var timeTickersToUpdate =  await context
            .AsNoTracking()
            .Where(x => x.ExecutionTime != null)
            .Where(x => x.Status == TickerStatus.Idle || x.Status == TickerStatus.Queued)
            .Where(x => x.ExecutionTime <= nowShifted)
            .Include(x => x.Children.Where(y => y.ExecutionTime == null))
            .Select(MappingExtensions.ForQueueTimeTickers<TTimeTicker>())
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);;

        foreach (var timeTicker in timeTickersToUpdate)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var affected = await context
                .Where(x => x.Id == timeTicker.Id && x.UpdatedAt <= timeTicker.UpdatedAt)
                .ExecuteUpdateAsync(setter => setter
                    .SetProperty(x => x.LockHolder, _lockHolder)
                    .SetProperty(x => x.LockedAt, now)
                    .SetProperty(x => x.UpdatedAt, now)
                    .SetProperty(x => x.Status, TickerStatus.InProgress), cancellationToken).ConfigureAwait(false);
                
            if(affected <= 0)
                continue;

            yield return timeTicker;
        }
    }

    public async Task ReleaseAcquiredTimeTickers(Guid[] timeTickerIds, CancellationToken cancellationToken)
    {
        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);;
        var now = _clock.UtcNow;
            
        var baseQuery = timeTickerIds.Length == 0
            ? dbContext.Set<TTimeTicker>()
            : dbContext.Set<TTimeTicker>().Where(x => timeTickerIds.Contains(x.Id));
            
        await baseQuery
            .WhereCanAcquire(_lockHolder)
            .ExecuteUpdateAsync(setter => setter
                .SetProperty(x => x.LockHolder, _ => null)
                .SetProperty(x => x.LockedAt, _ => null)
                .SetProperty(x => x.Status, _ => TickerStatus.Idle)
                .SetProperty(x => x.UpdatedAt, _ => now), cancellationToken).ConfigureAwait(false);;
    }
        
    public async Task<int> UpdateTimeTicker(InternalFunctionContext functionContexts, CancellationToken cancellationToken)
    {
        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await dbContext.Set<TTimeTicker>()
            .Where(x => x.Id == functionContexts.TickerId)
            .ExecuteUpdateAsync(MappingExtensions.UpdateTimeTicker<TTimeTicker>(functionContexts, _clock.UtcNow), cancellationToken).ConfigureAwait(false);
    }
        
    public async Task UpdateTimeTickersWithUnifiedContext(Guid[] timeTickerIds, InternalFunctionContext functionContext, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);;
        await dbContext.Set<TTimeTicker>()
            .Where(x => timeTickerIds.Contains(x.Id))
            .ExecuteUpdateAsync(MappingExtensions.UpdateTimeTicker<TTimeTicker>(functionContext, _clock.UtcNow), cancellationToken).ConfigureAwait(false);
    }
        
    public async Task<TimeTickerEntity[]> GetEarliestTimeTickers(CancellationToken cancellationToken)
    {
        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var now = _clock.UtcNow;
            
        var baseQuery = dbContext.Set<TTimeTicker>()
            .AsNoTracking()
            .Where(x => x.ExecutionTime != null)
            .WhereCanAcquire(_lockHolder)
            .Where(x => x.ExecutionTime >= now);
        
        var minExecutionTime = await baseQuery
            .OrderBy(x => x.ExecutionTime)
            .Select(x => x.ExecutionTime)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        if (minExecutionTime == null)
            return [];
        
        var startOfSecond = new DateTime(
            minExecutionTime.Value.Year,
            minExecutionTime.Value.Month,
            minExecutionTime.Value.Day,
            minExecutionTime.Value.Hour, 
            minExecutionTime.Value.Minute,
            minExecutionTime.Value.Second, DateTimeKind.Utc);
    
        var endOfSecond = startOfSecond.AddSeconds(1);
    
        return await baseQuery
            .Include(x => x.Children.Where(y => y.ExecutionTime == null))
            .Where(x => x.ExecutionTime.Value >= startOfSecond && x.ExecutionTime.Value < endOfSecond)
            .Select(MappingExtensions.ForQueueTimeTickers<TTimeTicker>())
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
    }
    
    public async Task<byte[]> GetTimeTickerRequest(Guid tickerId, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);;
        return await dbContext.Set<TTimeTicker>()
            .AsNoTracking()
            .Where(x => x.Id == tickerId)
            .Select(x => x.Request)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
    }
    
    public async Task ReleaseDeadNodeTimeTickerResources(string instanceIdentifier, CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        
        await dbContext.Set<TimeTickerEntity>()
            .WhereCanAcquire(instanceIdentifier)
            .ExecuteUpdateAsync(setter => setter
                .SetProperty(x => x.LockHolder, _ => null)
                .SetProperty(x => x.LockedAt, _ => null)
                .SetProperty(x => x.Status, TickerStatus.Idle)
                .SetProperty(x => x.UpdatedAt, now), cancellationToken)
            .ConfigureAwait(false);
        
        await dbContext.Set<TimeTickerEntity>()
            .Where(x => x.LockHolder == instanceIdentifier && x.Status == TickerStatus.InProgress)
            .ExecuteUpdateAsync(setter => setter
                .SetProperty(x => x.Status, TickerStatus.Skipped)
                .SetProperty(x => x.SkippedReason, "Node is not alive!")
                .SetProperty(x => x.ExecutedAt, now)
                .SetProperty(x => x.UpdatedAt, now), cancellationToken)
            .ConfigureAwait(false);
    }
    #endregion
        
    #region Core_Cron_Ticker_Methods
    public async Task MigrateDefinedCronTickers((string Function, string Expression)[] cronTickers, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var now = _clock.UtcNow;

        var entitiesToUpsert = cronTickers.Select(x =>
        {
            var id = Guid.NewGuid();
            return new TCronTicker
            {
                Id = id,
                Function = x.Function,
                Expression = x.Expression,
                InitIdentifier = $"MemoryTicker_Seeded_{id}",
                CreatedAt = now,
                UpdatedAt = now,
                Request = []
            };
        }).ToList();

        await dbContext.Set<TCronTicker>()
            .UpsertRange(entitiesToUpsert)
            .On(v => new { v.Function, v.Expression, v.Request })
            .NoUpdate()
            .RunAsync(cancellationToken).ConfigureAwait(false);
    }
        
    public async Task<CronTickerEntity[]> GetAllCronTickerExpressions(CancellationToken cancellationToken = default)
    {
        var result = await RedisContext.GetOrSetArrayAsync(
            cacheKey: "cron:expressions",
            factory: async (ct) =>
            {
                await using var dbContext = await DbContextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
                return await dbContext.Set<TCronTicker>()
                    .AsNoTracking()
                    .Select(MappingExtensions.ForCronTickerExpressions<CronTickerEntity>())
                    .ToArrayAsync(ct)
                    .ConfigureAwait(false);
            },
            expiration: TimeSpan.FromMinutes(10),
            cancellationToken: cancellationToken);
        
        
        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);;
        return await dbContext.Set<TCronTicker>()
            .AsNoTracking()
            .Select(MappingExtensions.ForCronTickerExpressions<CronTickerEntity>())
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
    }
    #endregion

    #region Core_Cron_TickerOccurrence_Methods
    public async Task UpdateCronTickerOccurrence(InternalFunctionContext functionContext, CancellationToken cancellationToken)
    {
        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);;
        await dbContext.Set<CronTickerOccurrenceEntity<TCronTicker>>()
            .Where(x => x.Id == functionContext.TickerId)
            .ExecuteUpdateAsync(MappingExtensions.UpdateCronTickerOccurrence<TCronTicker>(functionContext), cancellationToken)
            .ConfigureAwait(false);
    }
    
    public async IAsyncEnumerable<CronTickerOccurrenceEntity<TCronTicker>> QueueTimedOutCronTickerOccurrences([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var nowShifted = _clock.UtcNow.AddSeconds(-1);
        var now = _clock.UtcNow;
        
        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);;
        var context = dbContext.Set<CronTickerOccurrenceEntity<TCronTicker>>();
            
        var cronTickersToUpdate = await context
            .AsNoTracking()
            .Include(x => x.CronTicker)
            .Where(x => x.Status == TickerStatus.Idle || x.Status == TickerStatus.Queued)
            .Where(x => x.ExecutionTime <= nowShifted)
            .Select(MappingExtensions.ForQueueCronTickerOccurrence<CronTickerOccurrenceEntity<TCronTicker>, TCronTicker>())
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);

        foreach (var cronTickerOccurrence in cronTickersToUpdate)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var affected = await context
                .Where(x => x.Id == cronTickerOccurrence.Id && x.UpdatedAt == cronTickerOccurrence.UpdatedAt)
                .ExecuteUpdateAsync(setter => setter
                    .SetProperty(x => x.LockHolder, _lockHolder)
                    .SetProperty(x => x.LockedAt, now)
                    .SetProperty(x => x.UpdatedAt,  now)
                    .SetProperty(x => x.Status, TickerStatus.InProgress), cancellationToken)
                .ConfigureAwait(false);
                
            if(affected <= 0)
                continue;

            yield return cronTickerOccurrence;
        }
    }
    
    public async Task ReleaseDeadNodeOccurrenceResources(string instanceIdentifier, CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
           
        await dbContext.Set<CronTickerOccurrenceEntity<TCronTicker>>()
            .WhereCanAcquire(instanceIdentifier)
            .ExecuteUpdateAsync(setter => setter
                .SetProperty(x => x.LockHolder, _ => null)
                .SetProperty(x => x.LockedAt, _ => null)
                .SetProperty(x => x.Status, TickerStatus.Idle)
                .SetProperty(x => x.UpdatedAt, now), cancellationToken)
            .ConfigureAwait(false);
        
        await dbContext.Set<CronTickerOccurrenceEntity<TCronTicker>>()
            .Where(x => x.LockHolder == instanceIdentifier && x.Status == TickerStatus.InProgress)
            .ExecuteUpdateAsync(setter => setter
                .SetProperty(x => x.Status, TickerStatus.Skipped)
                .SetProperty(x => x.SkippedReason, "Node is not alive!")
                .SetProperty(x => x.ExecutedAt, now)
                .SetProperty(x => x.UpdatedAt, now), cancellationToken)
            .ConfigureAwait(false);
    }
    
    public async Task ReleaseAcquiredCronTickerOccurrences(Guid[] occurrenceIds, CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);;
            
        var baseQuery = occurrenceIds.Length == 0 
            ? dbContext.Set<CronTickerOccurrenceEntity<TCronTicker>>() 
            : dbContext.Set<CronTickerOccurrenceEntity<TCronTicker>>().Where(x => occurrenceIds.Contains(x.Id));
           
        await baseQuery.Where(x => occurrenceIds.Contains(x.Id))
            .WhereCanAcquire(_lockHolder)
            .ExecuteUpdateAsync(setter => setter
                .SetProperty(x => x.LockHolder, _ => null)
                .SetProperty(x => x.LockedAt, _ => null)
                .SetProperty(x => x.Status, TickerStatus.Idle)
                .SetProperty(x => x.UpdatedAt, now), cancellationToken)
            .ConfigureAwait(false);
    }
    
    public async IAsyncEnumerable<CronTickerOccurrenceEntity<TCronTicker>> QueueCronTickerOccurrences((DateTime Key, InternalManagerContext[] Items) cronTickerOccurrences, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        var executionTime = cronTickerOccurrences.Key;
        
        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);;
        var context = dbContext.Set<CronTickerOccurrenceEntity<TCronTicker>>();
        
        foreach (var item in cronTickerOccurrences.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (item.NextCronOccurrence is null)
            {
                var itemToAdd = new CronTickerOccurrenceEntity<TCronTicker>
                {
                    Id = Guid.NewGuid(),
                    Status = TickerStatus.Queued,
                    LockHolder = _lockHolder,
                    ExecutionTime = executionTime,
                    CronTickerId = item.Id,
                    LockedAt = now,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                
                var affectAdded = await context.Upsert(itemToAdd)
                    .On(x => new { x.ExecutionTime, x.CronTickerId })
                    .NoUpdate()
                    .RunAsync(cancellationToken).ConfigureAwait(false);;

                if (affectAdded <= 0)
                    continue;
                
                itemToAdd.CronTicker = new TCronTicker
                {
                    Id = item.Id,
                    Function = item.FunctionName,
                    InitIdentifier = _lockHolder,
                    Expression = item.Expression,
                    Retries = item.Retries,
                    RetryIntervals = item.RetryIntervals
                };
                yield return itemToAdd;
            }
            else
            {
                var affectedUpdate = await context
                    .Where(x => x.Id == item.NextCronOccurrence.Id)
                    .Where(x => x.ExecutionTime == executionTime)
                    .WhereCanAcquire(_lockHolder)
                    .ExecuteUpdateAsync(prop => prop
                            .SetProperty(y => y.LockHolder, _lockHolder)
                            .SetProperty(y => y.LockedAt, now)
                            .SetProperty(y => y.UpdatedAt, now)
                            .SetProperty(y => y.Status, TickerStatus.Queued),
                        cancellationToken)
                    .ConfigureAwait(false);

                if (affectedUpdate <= 0)
                    continue;
                
                yield return new CronTickerOccurrenceEntity<TCronTicker>
                {
                    Id = item.NextCronOccurrence.Id,
                    CronTickerId = item.Id,
                    ExecutionTime = now,
                    Status = TickerStatus.Queued,
                    LockHolder = _lockHolder,
                    LockedAt = now,
                    UpdatedAt = now,
                    CreatedAt = item.NextCronOccurrence.CreatedAt,
                    CronTicker = new TCronTicker
                    {
                        Id = item.Id,
                        Function = item.FunctionName,
                        InitIdentifier = _lockHolder,
                        Expression = item.Expression,
                        Retries = item.Retries,
                        RetryIntervals = item.RetryIntervals
                    }
                };
            }
        }
    }
    
    public async Task<CronTickerOccurrenceEntity<TCronTicker>> GetEarliestAvailableCronOccurrence(Guid[] ids, CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);;
        return await dbContext.Set<CronTickerOccurrenceEntity<TCronTicker>>()
            .AsNoTracking()
            .Include(x => x.CronTicker)
            .Where(x => ids.Contains(x.CronTickerId))
            .Where(x => x.ExecutionTime >= now)
            .WhereCanAcquire(_lockHolder)
            .OrderBy(x => x.ExecutionTime)
            .Select(MappingExtensions.ForLatestQueuedCronTickerOccurrence<CronTickerOccurrenceEntity<TCronTicker>, TCronTicker>())
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }
    
    public async Task<byte[]> GetCronTickerOccurrenceRequest(Guid tickerId, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);;
        return await dbContext.Set<CronTickerOccurrenceEntity<TCronTicker>>()
            .AsNoTracking()
            .Include(x => x.CronTicker)
            .Where(x => x.Id == tickerId)
            .Select(x => x.CronTicker.Request)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }
    
    public async Task UpdateCronTickerOccurrencesWithUnifiedContext(Guid[] timeTickerIds, InternalFunctionContext functionContext,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);;
        await dbContext.Set<CronTickerOccurrenceEntity<TCronTicker>>()
            .Where(x => timeTickerIds.Contains(x.CronTickerId))
            .ExecuteUpdateAsync(MappingExtensions.UpdateCronTickerOccurrence<TCronTicker>(functionContext), cancellationToken)
            .ConfigureAwait(false);
    }
    
    #endregion
}