using NCrontab;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.DashboardDtos;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Exceptios;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Models;
using TickerQ.Utilities.Models.Ticker;

namespace TickerQ.Utilities.Managers
{
    internal class
        TickerManager<TTimeTicker, TCronTicker> :
        InternalTickerManager<TTimeTicker, TCronTicker>, ICronTickerManager<TCronTicker>,
        ITimeTickerManager<TTimeTicker>
        where TTimeTicker : TimeTicker, new() where TCronTicker : CronTicker, new()
    {
        public TickerManager(ITickerPersistenceProvider<TTimeTicker, TCronTicker> persistenceProvider, ITickerHost tickerHost, ITickerClock clock,
            TickerOptionsBuilder tickerOptions, ITickerQNotificationHubSender notificationHubSender)
            : base(persistenceProvider, tickerHost, clock, tickerOptions, notificationHubSender)
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

                await PersistenceProvider.InsertTimeTickers(new[] { entity }, cancellationToken).ConfigureAwait(false);

                TickerHost.RestartIfNeeded(entity.ExecutionTime);

                if (NotificationHubSender != null)
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

                await PersistenceProvider.InsertCronTickers(new[] { entity }, cancellationToken).ConfigureAwait(false);

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
                var timeTicker = await PersistenceProvider.GetTimeTickerById(id, cancellationToken).ConfigureAwait(false);

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

                await PersistenceProvider.UpdateTimeTickers(new[] { timeTicker }, cancellationToken).ConfigureAwait(false);

                return new TickerResult<TTimeTicker>(timeTicker);
            }
            catch (Exception e)
            {
                return new TickerResult<TTimeTicker>(e);
            }
        }

        private async Task<TickerResult<TTicker>> UpdateAsync<TTicker>(TTicker entity,
            CancellationToken cancellationToken = default) where TTicker : BaseTicker
        {
            var nextOccurrence = ValidateAndGetNextOccurrenceTicker(entity, out var exception);

            if (exception != null)
                return new TickerResult<TTicker>(exception);

            var originalTicker = await GetOriginalTicker(entity, cancellationToken);

            if (originalTicker is null)
                return new TickerResult<TTicker>(new Exception($"Cannot find Entity with id {entity.Id}!"));

            try
            {
                var mustRestart = false;

                switch (originalTicker)
                {
                    case TCronTicker originalCron when entity is TCronTicker newCron:
                        MapCronTickerChanges(originalCron, newCron);
                        mustRestart = await HandleCronRescheduling(originalCron, cancellationToken);
                        await PersistenceProvider.UpdateCronTickers(new[] { originalCron }, cancellationToken);
                        break;

                    case TTimeTicker originalTime when entity is TTimeTicker newTime:
                        MapTimeTickerChanges(originalTime, newTime);
                        mustRestart = originalTime.Status == TickerStatus.Queued;
                        await PersistenceProvider.UpdateTimeTickers(new[] { originalTime }, cancellationToken);
                        break;
                }

                RestartTickerHost(mustRestart, nextOccurrence);

                return new TickerResult<TTicker>(originalTicker);
            }
            catch (Exception e)
            {
                return new TickerResult<TTicker>(e);
            }

            void MapCronTickerChanges(TCronTicker original, TCronTicker updated)
            {
                original.Expression = updated.Expression;
                original.Request = updated.Request;
                original.Function = updated.Function;
                original.UpdatedAt = Clock.UtcNow;
            }

            void MapTimeTickerChanges(TTimeTicker original, TTimeTicker updated)
            {
                original.Function = updated.Function;
                original.ExecutionTime = updated.ExecutionTime;
                original.Request = updated.Request;
                original.LockHolder = updated.LockHolder;
                original.Status = updated.Status;
                original.LockedAt = updated.LockedAt;
                original.UpdatedAt = Clock.UtcNow;
            }

            async Task<bool> HandleCronRescheduling(TCronTicker ticker, CancellationToken cancellationToken)
            {
                var queuedOccurrences = await GetQueuedNextCronOccurrences(ticker, cancellationToken).ConfigureAwait(false);

                if (queuedOccurrences.Length > 0)
                {
                    await PersistenceProvider.RemoveCronTickerOccurences(queuedOccurrences, cancellationToken).ConfigureAwait(false);
                    return true;
                }

                return false;
            }

            void RestartTickerHost(bool mustRestart, DateTime nextOccurrence)
            {
                if (mustRestart)
                    TickerHost.Restart();
                else
                    TickerHost.RestartIfNeeded(nextOccurrence);
            }

            async Task<TTicker> GetOriginalTicker(TTicker entity, CancellationToken cancellationToken)
            {
                return entity switch
                {
                    CronTicker _ => (TTicker)(BaseTicker)(await PersistenceProvider.GetCronTickerById(entity.Id, cancellationToken).ConfigureAwait(false)),
                    TimeTicker _ => (TTicker)(BaseTicker)(await PersistenceProvider.GetTimeTickerById(entity.Id, cancellationToken).ConfigureAwait(false)),
                    _ => default,
                };
            }
        }

        private async Task<TickerResult<TTicker>> DeleteAsync<TTicker>(Guid id,
            CancellationToken cancellationToken = default) where TTicker : BaseTicker
        {
            var originalTicker = await GetOriginalTicker(id, cancellationToken);

            if (originalTicker == null)
                return new TickerResult<TTicker>(new TickerValidatorException($"Cannot find Entity with id {id}!"));

            try
            {
                var mustRestart = false;


                switch (originalTicker)
                {
                    case TCronTicker cron:
                        var queued = await GetQueuedNextCronOccurrences(cron, cancellationToken).ConfigureAwait(false);
                        mustRestart = queued.Any();
                        await PersistenceProvider.RemoveCronTickers(new[] { cron }, cancellationToken).ConfigureAwait(false);
                        break;

                    case TTimeTicker time:
                        mustRestart = time.Status == TickerStatus.Queued;
                        await PersistenceProvider.RemoveTimeTickers(new[] { time }, cancellationToken).ConfigureAwait(false);
                        break;
                }

                if (mustRestart)
                    TickerHost.Restart();

                return new TickerResult<TTicker>(originalTicker);
            }
            catch (Exception e)
            {
                return new TickerResult<TTicker>(e);
            }

            async Task<TTicker> GetOriginalTicker(Guid id, CancellationToken cancellationToken)
            {
                return typeof(TTicker) switch
                {
                    Type t when t == typeof(CronTicker) => (TTicker)(BaseTicker)(await PersistenceProvider.GetCronTickerById(id, cancellationToken).ConfigureAwait(false)),
                    Type t when t == typeof(TimeTicker) => (TTicker)(BaseTicker)(await PersistenceProvider.GetTimeTickerById(id, cancellationToken).ConfigureAwait(false)),
                    _ => default,
                };
            }
        }

        private async Task<CronTickerOccurrence<TCronTicker>[]> GetQueuedNextCronOccurrences(CronTicker cronTicker, CancellationToken cancellationToken)
        {
            return await PersistenceProvider.RetrieveQueuedNextCronOccurrences(cronTicker.Id, cancellationToken).ConfigureAwait(false);
        }

        private DateTime ValidateAndGetNextOccurrenceTicker<TTicker>(TTicker ticker, out Exception exception)
            where TTicker : BaseTicker
        {
            exception = null;

            DateTime nextOccurrence = default;

            if (ticker == null)
            {
                exception = new TickerValidatorException($"No such entity is known in Ticker!");
            }

            if (TickerFunctionProvider.TickerFunctions.All(x => x.Key != ticker?.Function))
                exception = new TickerValidatorException($"Cannot find Ticker with name {ticker?.Function}");

            if (ticker is CronTicker cronTicker)
            {
                if (CrontabSchedule.TryParse(cronTicker.Expression) is { } crontabSchedule)
                    nextOccurrence = crontabSchedule.GetNextOccurrence(Clock.UtcNow);
                else
                    exception = new TickerValidatorException($"Cannot parse expression {cronTicker.Expression}");
            }

            else if (ticker is TimeTicker timeTicker)
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