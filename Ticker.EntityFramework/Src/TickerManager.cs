using Microsoft.EntityFrameworkCore;
using NCrontab;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.EntityFrameworkCore.Entities;
using TickerQ.EntityFrameworkCore.Entities.BaseEntity;
using TickerQ.Utilities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Exceptios;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ.EntityFrameworkCore
{
    internal class TickerManager<TDbContext> : InternalTickerManager<TDbContext>, ITickerManager where TDbContext : DbContext
    {
        public TickerManager(TDbContext dbContext, ITickerCollection tickerCollection, ITickerHost tickerHost, IClock clock, TickerOptionsBuilder tickerOptions)
            : base(dbContext, tickerCollection, tickerHost, clock, tickerOptions) { }

        public async Task<TickerResult<TEntity>> AddTickerAsync<TEntity>(TEntity entity, CancellationToken cancellationToken = default) where TEntity : BaseTickerEntity
        {
            var nextOccurrence = ValidateAndGetNextOccurrenceTicker(entity, out Exception exception);

            if (exception != default)
                return new TickerResult<TEntity>(exception);

            try
            {
                entity.CreatedAt = Clock.OffsetNow;
                entity.UpdatedAt = Clock.OffsetNow;

                var result = DbContext.Add(entity);

                await DbContext.SaveChangesAsync(cancellationToken)
                    .ConfigureAwait(false);

                TickerHost.RestartIfNeeded(nextOccurrence.Value);

                return new TickerResult<TEntity>(result.Entity);
            }
            catch (Exception e)
            {
                return new TickerResult<TEntity>(e);
            }
        }

        public async Task<TickerResult<TEntity>> DeleteTickerAsync<TEntity>(Guid id, CancellationToken cancellationToken = default) where TEntity : BaseTickerEntity
        {
            var originalEntity = await DbContext.Set<TEntity>().FirstOrDefaultAsync(x => x.Id == id, cancellationToken).ConfigureAwait(false);

            if (originalEntity == default)
                return new TickerResult<TEntity>(new TickerValidatorException($"Cannot find enitity with id {id}!"));

            try
            {
                bool mustRestart = false;

                if (originalEntity is CronTicker originalCronTickerEntity)
                    mustRestart = await GetQueuedNextCronOccurrences(originalCronTickerEntity, cancellationToken)
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

        public async Task<TickerResult<TEntity>> UpdateTickerAsync<TEntity>(TEntity entity, CancellationToken cancellationToken = default) where TEntity : BaseTickerEntity
        {
            var nextOccurrence = ValidateAndGetNextOccurrenceTicker(entity, out Exception exception);

            if (exception != default)
                return new TickerResult<TEntity>(exception);

            var originalEntity = await DbContext.Set<TEntity>().FirstOrDefaultAsync(x => x.Id == entity.Id, cancellationToken).ConfigureAwait(false);

            if (originalEntity == default)
                exception = new TickerValidatorException($"Cannot find enitity with id {entity.Id}!");

            try
            {
                bool mustRestart = false;

                if (originalEntity is CronTicker originalCronTickerEntity && entity is CronTicker newCronTickerEntity)
                {
                    originalCronTickerEntity.Expression = newCronTickerEntity.Expression;
                    originalCronTickerEntity.Request = newCronTickerEntity.Request;
                    originalCronTickerEntity.Function = newCronTickerEntity.Function;

                    var queuedNextOccurrences = await GetQueuedNextCronOccurrences(originalCronTickerEntity, cancellationToken)
                        .ToListAsync(cancellationToken)
                        .ConfigureAwait(false);

                    if (queuedNextOccurrences.Count > 0)
                    {
                        DbContext.RemoveRange(queuedNextOccurrences);
                        mustRestart = true;
                    }
                }
                else if (originalEntity is TimeTicker originalTimeTickerEntity && entity is TimeTicker newTimeTickerEntity)
                {
                    originalTimeTickerEntity.Function = newTimeTickerEntity.Function;
                    originalTimeTickerEntity.ExecutionTime = newTimeTickerEntity.ExecutionTime;
                    originalTimeTickerEntity.Request = newTimeTickerEntity.Request;
                    originalTimeTickerEntity.LockHolder = newTimeTickerEntity.LockHolder;
                    originalTimeTickerEntity.Status = newTimeTickerEntity.Status;
                    originalTimeTickerEntity.LockedAt = newTimeTickerEntity.LockedAt;

                    mustRestart = originalTimeTickerEntity.Status == TickerStatus.Queued;
                }

                originalEntity.UpdatedAt = Clock.OffsetNow;

                var result = DbContext.Update(originalEntity);

                await DbContext.SaveChangesAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (mustRestart)
                    TickerHost.Restart();
                else
                    TickerHost.RestartIfNeeded(nextOccurrence.Value);

                return new TickerResult<TEntity>(result.Entity);
            }
            catch (Exception e)
            {
                return new TickerResult<TEntity>(e);
            }
        }

        private IQueryable<CronTickerOccurrence> GetQueuedNextCronOccurrences(CronTicker cronTicker, CancellationToken cancellationToken)
        {
            var nextCronOccurrences = CronTickerOccurrences
                .Where(x => x.CronTickerId == cronTicker.Id)
                .Where(x => x.Status == TickerStatus.Queued);

            return nextCronOccurrences;
        }

        private DateTime? ValidateAndGetNextOccurrenceTicker<TEntity>(TEntity entity, out Exception exception) where TEntity : BaseTickerEntity
        {
            exception = default;

            DateTime? nextOccurrence = null;

            if (entity == default)
                exception = new TickerValidatorException($"No such entity is known in Ticker!");

            if (!TickerCollection.ExistFunction(entity.Function))
                exception = new TickerValidatorException($"Cannot find ticker with name {entity.Function}");

            if (entity is CronTicker cronTicker)
            {
                if (CrontabSchedule.TryParse(cronTicker.Expression) is CrontabSchedule crontabSchedule)
                    nextOccurrence = crontabSchedule.GetNextOccurrence(Clock.Now);
                else
                    exception = new TickerValidatorException($"Cannot parse expression {cronTicker.Expression}");
            }

            else if (entity is TimeTicker timeTicker)
            {
                if (timeTicker.ExecutionTime == default)
                    exception = new TickerValidatorException($"Invalid ExecutionTime!");
                else
                    nextOccurrence = timeTicker.ExecutionTime.DateTime;
            }

            return nextOccurrence;
        }
    }
}
