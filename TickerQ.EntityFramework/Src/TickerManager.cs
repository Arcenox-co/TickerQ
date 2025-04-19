using Microsoft.EntityFrameworkCore;
using NCrontab;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.EntityFrameworkCore.Entities;
using TickerQ.EntityFrameworkCore.Entities.BaseEntity;
using TickerQ.Utilities;
using TickerQ.Utilities.DashboardDtos;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Exceptios;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ.EntityFrameworkCore
{
    internal class
        TickerManager<TDbContext, TTimeTicker, TCronTicker> :
        InternalTickerManager<TDbContext, TTimeTicker, TCronTicker>, ICronTickerManager<TCronTicker>,
        ITimeTickerManager<TTimeTicker> where TDbContext : DbContext
        where TTimeTicker : TimeTicker, new()
        where TCronTicker : CronTicker, new()
    {
        public TickerManager(TDbContext dbContext, ITickerHost tickerHost, ITickerClock clock,
            TickerOptionsBuilder tickerOptions, ITickerQNotificationHubSender notificationHubSender)
            : base(dbContext, tickerHost, clock, tickerOptions, notificationHubSender)
        {
        }

        Task<TickerResult<TCronTicker>> ICronTickerManager<TCronTicker>.AddAsync(TCronTicker entity,
            CancellationToken cancellationToken)
            => AddCronTickerAsync(entity, cancellationToken);

        Task<TickerResult<TTimeTicker>> ITimeTickerManager<TTimeTicker>.AddAsync(TTimeTicker entity,
            CancellationToken cancellationToken)
            => AddTimeTickerAsync(entity, cancellationToken);

        Task<TickerResult<TCronTicker>> ICronTickerManager<TCronTicker>.UpdateAsync(TCronTicker entity,
            CancellationToken cancellationToken)
            => UpdateAsync(entity, cancellationToken);

        Task<TickerResult<TTimeTicker>> ITimeTickerManager<TTimeTicker>.UpdateAsync(Guid id, Action<TTimeTicker> action,
            CancellationToken cancellationToken)
            => UpdateTimeTickerAsync(id, action, cancellationToken);

        Task<TickerResult<TCronTicker>> ICronTickerManager<TCronTicker>.DeleteAsync(Guid id,
            CancellationToken cancellationToken)
            => DeleteAsync<TCronTicker>(id, cancellationToken);

        Task<TickerResult<TTimeTicker>> ITimeTickerManager<TTimeTicker>.DeleteAsync(Guid id,
            CancellationToken cancellationToken)
            => DeleteAsync<TTimeTicker>(id, cancellationToken);

        private async Task<TickerResult<TTimeTicker>> AddTimeTickerAsync(TTimeTicker entity,
            CancellationToken cancellationToken)
        {
            try
            {
                if (entity.ExecutionTime == default)
                    return new TickerResult<TTimeTicker>(new TickerValidatorException("Invalid ExecutionTime!"));

                entity.CreatedAt = Clock.UtcNow;
                entity.UpdatedAt = Clock.UtcNow;
                entity.ExecutionTime = entity.ExecutionTime.ToUniversalTime();

                TimeTickerContext.Add(entity);

                await DbContext.SaveChangesAsync(cancellationToken)
                    .ConfigureAwait(false);

                TickerHost.RestartIfNeeded(entity.ExecutionTime);
                
                if(NotificationHubSender != null)
                    await NotificationHubSender.AddTimeTickerNotifyAsync(new TimeTickerDto
                    {
                        Id = entity.Id,
                        Function = entity.Function,
                        ExecutionTime = entity.ExecutionTime,
                        Status = entity.Status,
                        Exception = entity.Exception,
                        ElapsedTime = entity.ElapsedTime,
                        CreatedAt = entity.CreatedAt,
                        UpdatedAt = entity.UpdatedAt,
                        LockedAt = entity.LockedAt,
                        LockHolder = entity.LockHolder,
                        Description = entity.Description,
                        Retries = entity.Retries,
                        RetryIntervals = entity.RetryIntervals,
                        ExecutedAt = entity.ExecutedAt,
                        RetryCount = entity.RetryCount,
                    });

                return new TickerResult<TTimeTicker>(entity);
            }
            catch (Exception e)
            {
                return new TickerResult<TTimeTicker>(e);
            }
        }

        private async Task<TickerResult<TCronTicker>> AddCronTickerAsync(TCronTicker entity,
            CancellationToken cancellationToken)
        {
            try
            {
                if (!(CrontabSchedule.TryParse(entity.Expression) is { } crontabSchedule))
                    return new TickerResult<TCronTicker>(
                        new TickerValidatorException($"Cannot parse expression {entity.Expression}"));

                var nextOccurrence = crontabSchedule.GetNextOccurrence(Clock.UtcNow);

                entity.CreatedAt = Clock.UtcNow;
                entity.UpdatedAt = Clock.UtcNow;

                CronTickerContext.Add(entity);
                
                await DbContext.SaveChangesAsync(cancellationToken)
                    .ConfigureAwait(false);

                TickerHost.RestartIfNeeded(nextOccurrence);

                if (NotificationHubSender != null)
                    await NotificationHubSender.AddCronTickerNotifyAsync(new CronTickerDto
                    {
                        Function = entity.Function,
                        Expression = entity.Expression,
                        UpdatedAt = entity.UpdatedAt,
                        CreatedAt = entity.CreatedAt,
                        Retries = entity.Retries,
                        RetryIntervals = entity.RetryIntervals,
                        Id = entity.Id
                    });

                return new TickerResult<TCronTicker>(entity);
            }
            catch (Exception e)
            {
                return new TickerResult<TCronTicker>(e);
            }
        }

        private async Task<TickerResult<TTimeTicker>> UpdateTimeTickerAsync(Guid id, Action<TTimeTicker> updateAction,
            CancellationToken cancellationToken)
        {
            try
            {
                var timeTicker = await TimeTickerContext
                    .FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
                    .ConfigureAwait(false);

                if (timeTicker == null)
                    return new TickerResult<TTimeTicker>(
                        new TickerValidatorException($"Cannot find Entity with id {id}!"));

                updateAction(timeTicker);

                timeTicker.UpdatedAt = Clock.UtcNow;
                timeTicker.ExecutionTime = timeTicker.ExecutionTime.ToUniversalTime();

                if (timeTicker.Status == TickerStatus.Queued)
                    TickerHost.Restart();
                else
                    TickerHost.RestartIfNeeded(timeTicker.ExecutionTime);

                TimeTickerContext.Update(timeTicker);

                await DbContext.SaveChangesAsync(cancellationToken)
                    .ConfigureAwait(false);

                return new TickerResult<TTimeTicker>(timeTicker);
            }
            catch (Exception e)
            {
                return new TickerResult<TTimeTicker>(e);
            }
        }

        private async Task<TickerResult<TEntity>> UpdateAsync<TEntity>(TEntity entity,
            CancellationToken cancellationToken = default) where TEntity : BaseTickerEntity
        {
            var nextOccurrence = ValidateAndGetNextOccurrenceTicker(entity, out var exception);

            if (exception != null)
                return new TickerResult<TEntity>(exception);

            var originalEntity = await DbContext.Set<TEntity>()
                .FirstOrDefaultAsync(x => x.Id == entity.Id, cancellationToken).ConfigureAwait(false);

            if (originalEntity == null)
                return new TickerResult<TEntity>(new Exception("$Cannot find Entity with id {entity.Id}!"));

            try
            {
                var mustRestart = false;

                if (originalEntity is CronTicker originalCronTickerEntity && entity is CronTicker newCronTickerEntity)
                {
                    originalCronTickerEntity.Expression = newCronTickerEntity.Expression;
                    originalCronTickerEntity.Request = newCronTickerEntity.Request;
                    originalCronTickerEntity.Function = newCronTickerEntity.Function;

                    var queuedNextOccurrences = await GetQueuedNextCronOccurrences(originalCronTickerEntity)
                        .ToListAsync(cancellationToken)
                        .ConfigureAwait(false);

                    if (queuedNextOccurrences.Count > 0)
                    {
                        CronTickerOccurrenceContext.RemoveRange(queuedNextOccurrences);
                        mustRestart = true;
                    }
                }
                else if (originalEntity is TimeTicker originalTimeTickerEntity &&
                         entity is TimeTicker newTimeTickerEntity)
                {
                    originalTimeTickerEntity.Function = newTimeTickerEntity.Function;
                    originalTimeTickerEntity.ExecutionTime = newTimeTickerEntity.ExecutionTime;
                    originalTimeTickerEntity.Request = newTimeTickerEntity.Request;
                    originalTimeTickerEntity.LockHolder = newTimeTickerEntity.LockHolder;
                    originalTimeTickerEntity.Status = newTimeTickerEntity.Status;
                    originalTimeTickerEntity.LockedAt = newTimeTickerEntity.LockedAt;

                    mustRestart = originalTimeTickerEntity.Status == TickerStatus.Queued;
                }

                originalEntity.UpdatedAt = Clock.UtcNow;

                var result = DbContext.Update(originalEntity);

                await DbContext.SaveChangesAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (mustRestart)
                    TickerHost.Restart();
                else
                    TickerHost.RestartIfNeeded(nextOccurrence);

                return new TickerResult<TEntity>(result.Entity);
            }
            catch (Exception e)
            {
                return new TickerResult<TEntity>(e);
            }
        }

        private async Task<TickerResult<TEntity>> DeleteAsync<TEntity>(Guid id,
            CancellationToken cancellationToken = default) where TEntity : BaseTickerEntity
        {
            var originalEntity = await DbContext.Set<TEntity>().FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
                .ConfigureAwait(false);

            if (originalEntity == null)
                return new TickerResult<TEntity>(new TickerValidatorException($"Cannot find Entity with id {id}!"));

            try
            {
                var mustRestart = false;

                if (originalEntity is CronTicker originalCronTickerEntity)
                    mustRestart = await GetQueuedNextCronOccurrences(originalCronTickerEntity)
                        .AnyAsync(cancellationToken)
                        .ConfigureAwait(false);

                else if (originalEntity is TimeTicker originalTimeTickerEntity)
                    mustRestart = originalTimeTickerEntity.Status == TickerStatus.Queued;

                DbContext.Remove(originalEntity);

                await DbContext.SaveChangesAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (mustRestart)
                    TickerHost.Restart();

                return new TickerResult<TEntity>(originalEntity);
            }
            catch (Exception e)
            {
                return new TickerResult<TEntity>(e);
            }
        }

        private IQueryable<CronTickerOccurrence<TCronTicker>> GetQueuedNextCronOccurrences(CronTicker cronTicker)
        {
            var nextCronOccurrences = CronTickerOccurrenceContext
                .Where(x => x.CronTickerId == cronTicker.Id)
                .Where(x => x.Status == TickerStatus.Queued);

            return nextCronOccurrences;
        }

        private DateTime ValidateAndGetNextOccurrenceTicker<TEntity>(TEntity entity, out Exception exception)
            where TEntity : BaseTickerEntity
        {
            exception = null;

            DateTime nextOccurrence = default;

            if (entity == null)
            {
                exception = new TickerValidatorException($"No such entity is known in Ticker!");
            }

            if (TickerFunctionProvider.TickerFunctions.All(x => x.Key != entity?.Function))
                exception = new TickerValidatorException($"Cannot find Ticker with name {entity?.Function}");

            if (entity is CronTicker cronTicker)
            {
                if (CrontabSchedule.TryParse(cronTicker.Expression) is { } crontabSchedule)
                    nextOccurrence = crontabSchedule.GetNextOccurrence(Clock.UtcNow);
                else
                    exception = new TickerValidatorException($"Cannot parse expression {cronTicker.Expression}");
            }

            else if (entity is TimeTicker timeTicker)
            {
                if (timeTicker.ExecutionTime == default)
                    exception = new TickerValidatorException($"Invalid ExecutionTime!");
                else
                    nextOccurrence = timeTicker.ExecutionTime;
            }

            return nextOccurrence;
        }
    }
}