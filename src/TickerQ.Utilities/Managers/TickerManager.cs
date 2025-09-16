using NCrontab;
using System;
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
    internal class
        TickerManager<TTimeTicker, TCronTicker> :
        InternalTickerManager<TTimeTicker, TCronTicker>, ICronTickerManager<TCronTicker>,
        ITimeTickerManager<TTimeTicker>
        where TTimeTicker : TimeTickerEntity, new()
        where TCronTicker : CronTickerEntity, new()
    {
        private readonly ITickerHost _tickerHost;
        private readonly TickerExecutionContext _executionContext;
        public TickerManager(ITickerPersistenceProvider<TTimeTicker, TCronTicker> persistenceProvider,
            ITickerHost tickerHost, ITickerClock clock, ITickerQNotificationHubSender notificationHubSender, TickerExecutionContext executionContext)
            : base(persistenceProvider, clock, notificationHubSender)
        {
            _tickerHost = tickerHost ?? throw new ArgumentNullException(nameof(tickerHost));
            _executionContext = executionContext ?? throw new ArgumentNullException(nameof(executionContext));
        }

        Task<TickerResult<TCronTicker>> ICronTickerManager<TCronTicker>.AddAsync(TCronTicker entity, CancellationToken cancellationToken)
            => AddCronTickerAsync(entity, cancellationToken);

        Task<TickerResult<TTimeTicker>> ITimeTickerManager<TTimeTicker>.AddAsync(TTimeTicker entity, CancellationToken cancellationToken)
            => AddTimeTickerAsync(entity, cancellationToken);

        Task<TickerResult<TCronTicker>> ICronTickerManager<TCronTicker>.UpdateAsync(TCronTicker cronTicker, CancellationToken cancellationToken)
            => UpdateCronTickerAsync(cronTicker, cancellationToken);

        Task<TickerResult<TTimeTicker>> ITimeTickerManager<TTimeTicker>.UpdateAsync(TTimeTicker timeTicker, CancellationToken cancellationToken)
            => UpdateTimeTickerAsync(timeTicker, cancellationToken);

        Task<TickerResult<TCronTicker>> ICronTickerManager<TCronTicker>.DeleteAsync(Guid id, CancellationToken cancellationToken)
            => DeleteCronTickerAsync(id, cancellationToken);

        Task<TickerResult<TTimeTicker>> ITimeTickerManager<TTimeTicker>.DeleteAsync(Guid id, CancellationToken cancellationToken)
            => DeleteTimeTickerAsync(id, cancellationToken);

        private async Task<TickerResult<TTimeTicker>> AddTimeTickerAsync(TTimeTicker entity, CancellationToken cancellationToken)
        {
            if (entity.Id == Guid.Empty)
                entity.Id = Guid.NewGuid();

            if (TickerFunctionProvider.TickerFunctions.All(x => x.Key != entity?.Function))
                return new TickerResult<TTimeTicker>(
                    new TickerValidatorException($"Cannot find TickerFunction with name {entity?.Function}"));
            
            if (entity.ExecutionTime == default)
                return new TickerResult<TTimeTicker>(new TickerValidatorException("Invalid ExecutionTime!"));
            
            entity.CreatedAt = Clock.Now;
            entity.UpdatedAt = Clock.Now;
            
            entity.ExecutionTime = entity.ExecutionTime.Kind == DateTimeKind.Utc
                ? entity.ExecutionTime 
                : entity.ExecutionTime.ToUniversalTime();
            
            try
            {
                await PersistenceProvider.AddTimeTickers([entity], cancellationToken: cancellationToken);

                _tickerHost.RestartIfNeeded(entity.ExecutionTime);

                if (NotificationHubSender != null)
                    await NotificationHubSender.AddTimeTickerNotifyAsync(entity).ConfigureAwait(false);

                return new TickerResult<TTimeTicker>(entity);
            }
            catch (Exception e)
            {
                return new TickerResult<TTimeTicker>(e);
            }
        }

        private async Task<TickerResult<TCronTicker>> AddCronTickerAsync(TCronTicker entity, CancellationToken cancellationToken)
        {
           
            if (entity.Id == Guid.Empty)
                entity.Id = Guid.NewGuid();

            if (TickerFunctionProvider.TickerFunctions.All(x => x.Key != entity?.Function))
                return new TickerResult<TCronTicker>(
                    new TickerValidatorException($"Cannot find TickerFunction with name {entity?.Function}"));

            if (CrontabSchedule.TryParse(entity.Expression) is not { } crontabSchedule)
                return new TickerResult<TCronTicker>(
                    new TickerValidatorException($"Cannot parse expression {entity.Expression}"));

            var nextOccurrence = crontabSchedule.GetNextOccurrence(Clock.Now);
            
            entity.CreatedAt = Clock.Now;
            entity.UpdatedAt = Clock.Now;
            
            try
            {
                await PersistenceProvider.InsertCronTickers([entity], cancellationToken: cancellationToken);

                _tickerHost.RestartIfNeeded(nextOccurrence);

                if (NotificationHubSender != null)
                    await NotificationHubSender.AddCronTickerNotifyAsync(entity);

                return new TickerResult<TCronTicker>(entity);
            }
            catch (Exception e)
            {
                return new TickerResult<TCronTicker>(e);
            }
        }

        private async Task<TickerResult<TTimeTicker>> UpdateTimeTickerAsync(TTimeTicker timeTicker, CancellationToken cancellationToken)
        {
            if (timeTicker == null)
                return new TickerResult<TTimeTicker>(
                    new TickerValidatorException($"Ticker must not be null!"));

            timeTicker.UpdatedAt = Clock.Now;
      
            timeTicker.ExecutionTime = timeTicker.ExecutionTime.Kind == DateTimeKind.Utc
                ? timeTicker.ExecutionTime 
                : timeTicker.ExecutionTime.ToUniversalTime();
            
            try {
                
                var affectedRows = await PersistenceProvider.UpdateTimeTickers([timeTicker], cancellationToken: cancellationToken).ConfigureAwait(false);
                
                if (_executionContext.Functions.Any(x => x.TickerId == timeTicker.Id))
                    _tickerHost.Restart();
                else
                    _tickerHost.RestartIfNeeded(timeTicker.ExecutionTime);
                
                return new TickerResult<TTimeTicker>(timeTicker, affectedRows);
            }
            catch (Exception e)
            {
                return new TickerResult<TTimeTicker>(e);
            }
        }

        private async Task<TickerResult<TCronTicker>> UpdateCronTickerAsync(TCronTicker cronTicker, CancellationToken cancellationToken = default)
        {
            if (cronTicker == null)
                return new TickerResult<TCronTicker>(new Exception($"Cron ticker must not be null!"));

            if (TickerFunctionProvider.TickerFunctions.All(x => x.Key != cronTicker?.Function))
                return new TickerResult<TCronTicker>(
                    new TickerValidatorException($"Cannot find TickerFunction with name {cronTicker.Function}"));


            if (CrontabSchedule.TryParse(cronTicker.Expression) is not { } crontabSchedule)
                return new TickerResult<TCronTicker>(
                    new TickerValidatorException($"Cannot parse expression {cronTicker.Expression}"));
            
            var nextOccurrence = crontabSchedule.GetNextOccurrence(Clock.Now);

            try
            {
                cronTicker.UpdatedAt = Clock.Now;

                var affectedRows = await PersistenceProvider.UpdateCronTickers([cronTicker], cancellationToken: cancellationToken);

                if (_executionContext.Functions.FirstOrDefault(x => x.CronTickerId == cronTicker.Id) is { } internalFunction)
                {
                    internalFunction.ResetUpdateProps()
                        .SetProperty(x => x.ExecutionTime, nextOccurrence);
                    
                    await PersistenceProvider.UpdateCronTickerOccurrence(internalFunction, cancellationToken: cancellationToken).ConfigureAwait(false);
                    
                    _tickerHost.Restart();
                }
                
                _tickerHost.RestartIfNeeded(nextOccurrence);

                return new TickerResult<TCronTicker>(cronTicker, affectedRows);
            }
            catch (Exception e)
            {
                return new TickerResult<TCronTicker>(e);
            }
        }

        private async Task<TickerResult<TCronTicker>> DeleteCronTickerAsync(Guid id, CancellationToken cancellationToken = default)
        {
            var affectedRows = await PersistenceProvider.RemoveCronTickers([id], cancellationToken: cancellationToken);
            
            if(affectedRows > 0 && _executionContext.Functions.Any(x => x.CronTickerId == id))
                _tickerHost.Restart();

            return new TickerResult<TCronTicker>(affectedRows);
        }


        private async Task<TickerResult<TTimeTicker>> DeleteTimeTickerAsync(Guid id, CancellationToken cancellationToken = default)
        {
            var affectedRows = await PersistenceProvider.RemoveTimeTickers([id], cancellationToken: cancellationToken);
            
            if(affectedRows > 0 && _executionContext.Functions.Any(x => x.TickerId == id))
                _tickerHost.Restart();

            return new TickerResult<TTimeTicker>(affectedRows);
        }
    }
}