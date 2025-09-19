using NCrontab;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Exceptions;
using TickerQ.Utilities.Instrumentation;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Models;

namespace TickerQ.Utilities.Managers
{
    internal class
        TickerManager<TTimeTicker, TCronTicker> :
        InternalTickerManager<TTimeTicker, TCronTicker>, ICronTickerManager<TCronTicker>,
        ITimeTickerManager<TTimeTicker>
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        private readonly ITickerHost _tickerHost;
        private readonly TickerExecutionContext _executionContext;
        private readonly ITickerQInstrumentation _tickerQInstrumentation;
        public TickerManager(ITickerPersistenceProvider<TTimeTicker, TCronTicker> persistenceProvider,
            ITickerHost tickerHost, ITickerClock clock, ITickerQNotificationHubSender notificationHubSender, TickerExecutionContext executionContext, ITickerQInstrumentation tickerQInstrumentation)
            : base(persistenceProvider, clock, notificationHubSender)
        {
            _tickerHost = tickerHost ?? throw new ArgumentNullException(nameof(tickerHost));
            _executionContext = executionContext ?? throw new ArgumentNullException(nameof(executionContext));
            _tickerQInstrumentation = tickerQInstrumentation ?? throw new ArgumentNullException(nameof(tickerQInstrumentation));
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

            _tickerQInstrumentation.LogJobEnqueued("TimeTicker", entity.Function, entity.Id, "API");

            if (TickerFunctionProvider.TickerFunctions.All(x => x.Key != entity?.Function))
                return new TickerResult<TTimeTicker>(
                    new TickerValidatorException($"Cannot find TickerFunction with name {entity?.Function}"));
            
            if (entity.ExecutionTime == null)
                return new TickerResult<TTimeTicker>(new TickerValidatorException("Invalid ExecutionTime!"));
            
            entity.ExecutionTime ??= Clock.UtcNow;
            
            entity.ExecutionTime = entity.ExecutionTime.Value.Kind == DateTimeKind.Utc
                ? entity.ExecutionTime 
                : entity.ExecutionTime.Value.ToUniversalTime();
            
            try
            {
                await PersistenceProvider.AddTimeTickers([entity], cancellationToken: cancellationToken);

                _tickerHost.RestartIfNeeded(entity.ExecutionTime.Value);

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

            _tickerQInstrumentation.LogJobEnqueued("CronTicker", entity.Function, entity.Id, "API");

            if (TickerFunctionProvider.TickerFunctions.All(x => x.Key != entity?.Function))
                return new TickerResult<TCronTicker>(
                    new TickerValidatorException($"Cannot find TickerFunction with name {entity?.Function}"));

            if (CrontabSchedule.TryParse(entity.Expression) is not { } crontabSchedule)
                return new TickerResult<TCronTicker>(
                    new TickerValidatorException($"Cannot parse expression {entity.Expression}"));

            var nextOccurrence = crontabSchedule.GetNextOccurrence(Clock.UtcNow);
            
            entity.CreatedAt = Clock.UtcNow;
            entity.UpdatedAt = Clock.UtcNow;
            
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
            if (timeTicker is null)
                return new TickerResult<TTimeTicker>(
                    new TickerValidatorException($"Ticker must not be null!"));
            
            if(timeTicker.ExecutionTime == null)
                return new TickerResult<TTimeTicker>(
                    new TickerValidatorException($"Ticker ExecutionTime must not be null!"));
            
            timeTicker.UpdatedAt = Clock.UtcNow;
            
            timeTicker.ExecutionTime = timeTicker.ExecutionTime.Value.Kind == DateTimeKind.Utc
                ? timeTicker.ExecutionTime.Value
                : timeTicker.ExecutionTime.Value.ToUniversalTime();
            
            try {
                var affectedRows = await PersistenceProvider.UpdateTimeTickers([timeTicker], cancellationToken: cancellationToken).ConfigureAwait(false);
                
                if (_executionContext.Functions.Any(x => x.TickerId == timeTicker.Id))
                    _tickerHost.Restart();
                else
                    _tickerHost.RestartIfNeeded(timeTicker.ExecutionTime!.Value);
                
                return new TickerResult<TTimeTicker>(timeTicker, affectedRows);
            }
            catch (Exception e)
            {
                return new TickerResult<TTimeTicker>(e);
            }
        }

        private async Task<TickerResult<TCronTicker>> UpdateCronTickerAsync(TCronTicker cronTicker, CancellationToken cancellationToken = default)
        {
            if (cronTicker is null)
                return new TickerResult<TCronTicker>(new Exception($"Cron ticker must not be null!"));

            if (TickerFunctionProvider.TickerFunctions.All(x => x.Key != cronTicker?.Function))
                return new TickerResult<TCronTicker>(
                    new TickerValidatorException($"Cannot find TickerFunction with name {cronTicker.Function}"));


            if (CrontabSchedule.TryParse(cronTicker.Expression) is not { } crontabSchedule)
                return new TickerResult<TCronTicker>(
                    new TickerValidatorException($"Cannot parse expression {cronTicker.Expression}"));
            
            var nextOccurrence = crontabSchedule.GetNextOccurrence(Clock.UtcNow);

            try
            {
                cronTicker.UpdatedAt = Clock.UtcNow;

                var affectedRows = await PersistenceProvider.UpdateCronTickers([cronTicker], cancellationToken: cancellationToken);

                if (_executionContext.Functions.FirstOrDefault(x => x.ParentId == cronTicker.Id) is { } internalFunction)
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
            
            if(affectedRows > 0 && _executionContext.Functions.Any(x => x.ParentId == id))
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