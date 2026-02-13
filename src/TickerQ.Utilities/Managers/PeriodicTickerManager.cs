using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Exceptions;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Models;

namespace TickerQ.Utilities.Managers
{
    internal class PeriodicTickerManager<TPeriodicTicker> : IPeriodicTickerManager<TPeriodicTicker>
        where TPeriodicTicker : PeriodicTickerEntity, new()
    {
        private readonly IPeriodicTickerPersistenceProvider<TPeriodicTicker> _persistenceProvider;
        private readonly ITickerQHostScheduler _tickerQHostScheduler;
        private readonly ITickerClock _clock;
        private readonly ITickerQNotificationHubSender _notificationHubSender;

        public PeriodicTickerManager(
            IPeriodicTickerPersistenceProvider<TPeriodicTicker> persistenceProvider,
            ITickerQHostScheduler tickerQHostScheduler,
            ITickerClock clock,
            ITickerQNotificationHubSender notificationHubSender)
        {
            _persistenceProvider = persistenceProvider;
            _tickerQHostScheduler = tickerQHostScheduler;
            _clock = clock;
            _notificationHubSender = notificationHubSender;
        }

        public async Task<TickerResult<TPeriodicTicker>> AddAsync(TPeriodicTicker entity, CancellationToken cancellationToken = default)
        {
            if (entity.Id == Guid.Empty)
                entity.Id = Guid.NewGuid();

            if (TickerFunctionProvider.TickerFunctions.All(x => x.Key != entity.Function))
                return new TickerResult<TPeriodicTicker>(
                    new TickerValidatorException($"Cannot find TickerFunction with name {entity.Function}"));

            if (entity.Interval <= TimeSpan.Zero)
                return new TickerResult<TPeriodicTicker>(
                    new TickerValidatorException("Interval must be greater than zero"));

            var now = _clock.UtcNow;
            entity.CreatedAt = now;
            entity.UpdatedAt = now;

            try
            {
                await _persistenceProvider.InsertPeriodicTickers([entity], cancellationToken);

                var nextExecution = CalculateNextExecution(entity, now);
                _tickerQHostScheduler.RestartIfNeeded(nextExecution);

                return new TickerResult<TPeriodicTicker>(entity);
            }
            catch (Exception e)
            {
                return new TickerResult<TPeriodicTicker>(e);
            }
        }

        public async Task<TickerResult<TPeriodicTicker>> UpdateAsync(TPeriodicTicker periodicTicker, CancellationToken cancellationToken = default)
        {
            if (periodicTicker is null)
                return new TickerResult<TPeriodicTicker>(
                    new TickerValidatorException("Periodic ticker must not be null!"));

            if (periodicTicker.Interval <= TimeSpan.Zero)
                return new TickerResult<TPeriodicTicker>(
                    new TickerValidatorException("Interval must be greater than zero"));

            periodicTicker.UpdatedAt = _clock.UtcNow;

            try
            {
                var affectedRows = await _persistenceProvider.UpdatePeriodicTickers([periodicTicker], cancellationToken);

                var nextExecution = CalculateNextExecution(periodicTicker, _clock.UtcNow);
                _tickerQHostScheduler.RestartIfNeeded(nextExecution);

                return new TickerResult<TPeriodicTicker>(periodicTicker, affectedRows);
            }
            catch (Exception e)
            {
                return new TickerResult<TPeriodicTicker>(e);
            }
        }

        public async Task<TickerResult<TPeriodicTicker>> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        {
            var affectedRows = await _persistenceProvider.RemovePeriodicTickers([id], cancellationToken);
            return new TickerResult<TPeriodicTicker>(affectedRows);
        }

        public async Task<TickerResult<TPeriodicTicker>> PauseAsync(Guid id, CancellationToken cancellationToken = default)
        {
            var ticker = await _persistenceProvider.GetPeriodicTickerById(id, cancellationToken);
            if (ticker is null)
                return new TickerResult<TPeriodicTicker>(
                    new TickerValidatorException($"Periodic ticker with id {id} not found"));

            ticker.IsActive = false;
            ticker.UpdatedAt = _clock.UtcNow;

            var affectedRows = await _persistenceProvider.UpdatePeriodicTickers([ticker], cancellationToken);
            return new TickerResult<TPeriodicTicker>(ticker, affectedRows);
        }

        public async Task<TickerResult<TPeriodicTicker>> ResumeAsync(Guid id, CancellationToken cancellationToken = default)
        {
            var ticker = await _persistenceProvider.GetPeriodicTickerById(id, cancellationToken);
            if (ticker is null)
                return new TickerResult<TPeriodicTicker>(
                    new TickerValidatorException($"Periodic ticker with id {id} not found"));

            ticker.IsActive = true;
            ticker.UpdatedAt = _clock.UtcNow;

            var affectedRows = await _persistenceProvider.UpdatePeriodicTickers([ticker], cancellationToken);

            var nextExecution = CalculateNextExecution(ticker, _clock.UtcNow);
            _tickerQHostScheduler.RestartIfNeeded(nextExecution);

            return new TickerResult<TPeriodicTicker>(ticker, affectedRows);
        }

        public async Task<TickerResult<List<TPeriodicTicker>>> AddBatchAsync(List<TPeriodicTicker> entities, CancellationToken cancellationToken = default)
        {
            if (entities == null || entities.Count == 0)
                return new TickerResult<List<TPeriodicTicker>>(entities ?? new List<TPeriodicTicker>());

            var now = _clock.UtcNow;
            DateTime? earliestExecution = null;

            foreach (var entity in entities)
            {
                if (entity.Id == Guid.Empty)
                    entity.Id = Guid.NewGuid();

                if (TickerFunctionProvider.TickerFunctions.All(x => x.Key != entity.Function))
                    return new TickerResult<List<TPeriodicTicker>>(
                        new TickerValidatorException($"Cannot find TickerFunction with name {entity.Function}"));

                if (entity.Interval <= TimeSpan.Zero)
                    return new TickerResult<List<TPeriodicTicker>>(
                        new TickerValidatorException("Interval must be greater than zero"));

                entity.CreatedAt = now;
                entity.UpdatedAt = now;

                var nextExec = CalculateNextExecution(entity, now);
                if (earliestExecution == null || nextExec < earliestExecution)
                    earliestExecution = nextExec;
            }

            try
            {
                await _persistenceProvider.InsertPeriodicTickers(entities.ToArray(), cancellationToken);

                if (earliestExecution.HasValue)
                    _tickerQHostScheduler.RestartIfNeeded(earliestExecution.Value);

                return new TickerResult<List<TPeriodicTicker>>(entities);
            }
            catch (Exception e)
            {
                return new TickerResult<List<TPeriodicTicker>>(e);
            }
        }

        public async Task<TickerResult<List<TPeriodicTicker>>> UpdateBatchAsync(List<TPeriodicTicker> periodicTickers, CancellationToken cancellationToken = default)
        {
            if (periodicTickers == null || periodicTickers.Count == 0)
                return new TickerResult<List<TPeriodicTicker>>(periodicTickers ?? new List<TPeriodicTicker>());

            var now = _clock.UtcNow;
            DateTime? earliestExecution = null;

            foreach (var ticker in periodicTickers)
            {
                if (ticker.Interval <= TimeSpan.Zero)
                    return new TickerResult<List<TPeriodicTicker>>(
                        new TickerValidatorException("Interval must be greater than zero"));

                ticker.UpdatedAt = now;

                if (ticker.IsActive)
                {
                    var nextExec = CalculateNextExecution(ticker, now);
                    if (earliestExecution == null || nextExec < earliestExecution)
                        earliestExecution = nextExec;
                }
            }

            try
            {
                var affectedRows = await _persistenceProvider.UpdatePeriodicTickers(periodicTickers.ToArray(), cancellationToken);

                if (earliestExecution.HasValue)
                    _tickerQHostScheduler.RestartIfNeeded(earliestExecution.Value);

                return new TickerResult<List<TPeriodicTicker>>(periodicTickers, affectedRows);
            }
            catch (Exception e)
            {
                return new TickerResult<List<TPeriodicTicker>>(e);
            }
        }

        public async Task<TickerResult<TPeriodicTicker>> DeleteBatchAsync(List<Guid> ids, CancellationToken cancellationToken = default)
        {
            var affectedRows = await _persistenceProvider.RemovePeriodicTickers(ids.ToArray(), cancellationToken);
            return new TickerResult<TPeriodicTicker>(affectedRows);
        }

        /// <summary>
        /// Calculates the next execution time for a periodic ticker.
        /// </summary>
        internal static DateTime CalculateNextExecution(PeriodicTickerEntity ticker, DateTime now)
        {
            // If start time is in the future, use that
            if (ticker.StartTime.HasValue && ticker.StartTime.Value > now)
                return ticker.StartTime.Value;

            // If never executed, start now (or at StartTime if set)
            if (!ticker.LastExecutedAt.HasValue)
                return ticker.StartTime ?? now;

            // Calculate next based on last execution + interval
            var nextExecution = ticker.LastExecutedAt.Value + ticker.Interval;

            // If we're past the calculated time, align to now + interval
            if (nextExecution <= now)
            {
                // Calculate how many intervals have passed
                var elapsed = now - ticker.LastExecutedAt.Value;
                var intervalsPassed = (long)(elapsed.TotalMilliseconds / ticker.Interval.TotalMilliseconds);
                nextExecution = ticker.LastExecutedAt.Value + TimeSpan.FromMilliseconds((intervalsPassed + 1) * ticker.Interval.TotalMilliseconds);
            }

            // Check if past end time
            if (ticker.EndTime.HasValue && nextExecution > ticker.EndTime.Value)
                return DateTime.MaxValue; // No more executions

            return nextExecution;
        }
    }
}
