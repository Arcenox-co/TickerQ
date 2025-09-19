using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Models;

namespace TickerQ.Utilities.Managers
{
    internal abstract class InternalTickerManager<TTimeTicker, TCronTicker>(
        ITickerPersistenceProvider<TTimeTicker, TCronTicker> persistenceProvider,
        ITickerClock clock,
        ITickerQNotificationHubSender notificationHubSender)
        : IInternalTickerManager
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        protected readonly ITickerPersistenceProvider<TTimeTicker, TCronTicker> PersistenceProvider = persistenceProvider;
        protected readonly ITickerClock Clock = clock ?? throw new ArgumentNullException(nameof(clock));
        protected readonly ITickerQNotificationHubSender NotificationHubSender = notificationHubSender;
        
        public async Task<(TimeSpan TimeRemaining, InternalFunctionContext[] Functions)> GetNextTickers(CancellationToken cancellationToken = default)
        {
            while (true)
            {
                var minCronGroupTask =  GetEarliestCronTickerGroupAsync(cancellationToken);
                var minTimeTickersTask =  PersistenceProvider.GetEarliestTimeTickers(cancellationToken);
                
                await Task.WhenAll(minCronGroupTask, minTimeTickersTask).ConfigureAwait(false);
                
                var (minCronGroup, minTimeTickers) = (await minCronGroupTask, await minTimeTickersTask);
                
                var minTimeTickerTime = minTimeTickers.Length != 0
                    ? minTimeTickers[0].ExecutionTime ?? default
                    : default;

                var minTimeRemaining = CalculateMinTimeRemaining(minCronGroup, minTimeTickerTime, out var typesToQueue);

                if (minTimeRemaining == Timeout.InfiniteTimeSpan) 
                    return (Timeout.InfiniteTimeSpan, []);

                var nextTickers = await RetrieveEligibleTickersAsync(minCronGroup, minTimeTickers, typesToQueue, cancellationToken).ConfigureAwait(false);

                if (nextTickers.Length != 0) 
                    return (minTimeRemaining, nextTickers);

                await Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.Next(50, 201)), cancellationToken).ConfigureAwait(false);
            }
        }

        private TimeSpan CalculateMinTimeRemaining(
            (DateTime Key, InternalManagerContext[] Items)? minCronTicker,
            DateTime minTimeTicker,
            out TickerType[] sources)
        {
            var now = Clock.UtcNow;

            DateTime? cron = minCronTicker?.Key;
            DateTime? time = minTimeTicker == default ? null : minTimeTicker;

            // no values
            if (cron is null && time is null)
            {
                sources = [];
                return Timeout.InfiniteTimeSpan;
            }

            // only cron
            if (time is null)
            {
                sources = [TickerType.CronTickerOccurrence];
                return cron.Value - now;
            }

            // only time
            if (cron is null)
            {
                sources = [TickerType.TimeTicker];
                return time.Value - now;
            }

            // both present
            var cronSec = TruncateToSecond(cron.Value);
            var timeSec = TruncateToSecond(time.Value);

            // same second → both tickers win
            if (cronSec == timeSec)
            {
                sources = [TickerType.CronTickerOccurrence, TickerType.TimeTicker];
                var earliest = cron < time ? cron.Value : time.Value; // with ms
                return earliest - now;
            }

            // different seconds → only earliest ticker wins
            if (cron < time)
            {
                sources = [TickerType.CronTickerOccurrence];
                return cron.Value - now;
            }

            sources = [TickerType.TimeTicker];
            return time.Value - now;

            DateTime TruncateToSecond(DateTime dt) 
                => new(dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, dt.Second, dt.Kind);
        }

        private async Task<InternalFunctionContext[]>RetrieveEligibleTickersAsync(
            (DateTime Key, InternalManagerContext[] Items)? minCronTicker,
            TimeTickerEntity[] minTimeTicker,
            TickerType[] typesToQueue,
            CancellationToken cancellationToken = default)
        {
            
            if (typesToQueue.Contains(TickerType.CronTickerOccurrence) && typesToQueue.Contains(TickerType.TimeTicker))
            {
                var nextCronTickersTask = QueueNextCronTickersAsync(minCronTicker!.Value, cancellationToken);
                var nextTimeTickersTask = QueueNextTimeTickersAsync(minTimeTicker, cancellationToken);

                await Task.WhenAll(nextCronTickersTask, nextTimeTickersTask).ConfigureAwait(false);
                
                var (nextCronTickers, nextTimeTickers) = (await nextCronTickersTask, await nextTimeTickersTask);
                
                // Safety check for extremely large datasets
                var totalLength = nextCronTickers.Length + nextTimeTickers.Length;
         
    
                var merged = new InternalFunctionContext[totalLength];
                nextCronTickers.AsSpan().CopyTo(merged.AsSpan(0, nextCronTickers.Length));
                nextTimeTickers.AsSpan().CopyTo(merged.AsSpan(nextCronTickers.Length, nextTimeTickers.Length));
                return merged;
            }

            if (typesToQueue.Contains(TickerType.TimeTicker))
                return await QueueNextTimeTickersAsync(minTimeTicker, cancellationToken).ConfigureAwait(false);
            else
                return await QueueNextCronTickersAsync(minCronTicker!.Value, cancellationToken).ConfigureAwait(false);
        }

        private async Task<InternalFunctionContext[]> QueueNextTimeTickersAsync(TimeTickerEntity[] minTimeTickers, CancellationToken cancellationToken = default)
        {
            var results = new List<InternalFunctionContext>();
            
            await foreach(var updatedTimeTicker in PersistenceProvider.QueueTimeTickers(minTimeTickers, cancellationToken))
            {
                results.Add(new InternalFunctionContext
                {
                    FunctionName = updatedTimeTicker.Function,
                    TickerId = updatedTimeTicker.Id,
                    Type = TickerType.TimeTicker,
                    Retries = updatedTimeTicker.Retries,
                    RetryIntervals = updatedTimeTicker.RetryIntervals,
                    ParentId = updatedTimeTicker.ParentId,
                    TimeTickerChildren = updatedTimeTicker.Children.Select(ch => new InternalFunctionContext
                    {
                        FunctionName = ch.Function,
                        TickerId = ch.Id,
                        Type = TickerType.TimeTicker,
                        Retries = ch.Retries,
                        RetryIntervals = ch.RetryIntervals,
                        ParentId = ch.ParentId,
                        RunCondition = ch.RunCondition ?? RunCondition.OnAnyCompletedStatus,
                        TimeTickerChildren = ch.Children.Select(gch => new InternalFunctionContext
                        {
                            FunctionName = gch.Function,
                            TickerId = gch.Id,
                            Type = TickerType.TimeTicker,
                            Retries = gch.Retries,
                            RetryIntervals = gch.RetryIntervals,
                            ParentId = gch.ParentId,
                            RunCondition = ch.RunCondition ?? RunCondition.OnAnyCompletedStatus
                        }).ToList()
                    }).ToList()
                });
                
                await NotificationHubSender.UpdateTimeTickerNotifyAsync(updatedTimeTicker);
            }
           
            return results.ToArray();
        }

        private async Task<InternalFunctionContext[]> QueueNextCronTickersAsync((DateTime Key, InternalManagerContext[] Items) minCronTicker, CancellationToken cancellationToken = default)
        {
            var results = new List<InternalFunctionContext>();
            
            await foreach (var occurrence in PersistenceProvider.QueueCronTickerOccurrences(minCronTicker, cancellationToken).ConfigureAwait(false))
            {
                results.Add(new InternalFunctionContext
                {
                    ParentId = occurrence.CronTickerId,
                    FunctionName = occurrence.CronTicker.Function,
                    TickerId = occurrence.Id,
                    Type = TickerType.CronTickerOccurrence,
                    Retries = occurrence.CronTicker.Retries,
                    RetryIntervals = occurrence.CronTicker.RetryIntervals
                });
                
                if (occurrence.CreatedAt == occurrence.UpdatedAt && NotificationHubSender != null)
                    await NotificationHubSender.AddCronOccurrenceAsync(occurrence.CronTickerId, occurrence).ConfigureAwait(false);
                else if(NotificationHubSender != null)
                    await NotificationHubSender.UpdateCronOccurrenceAsync(occurrence.CronTickerId, occurrence).ConfigureAwait(false);
            }
            
            return results.ToArray();
        }
        
        private async Task<(DateTime Key, InternalManagerContext[] Items)?> GetEarliestCronTickerGroupAsync(CancellationToken cancellationToken = default)
        {
            var now = Clock.UtcNow;

            var cronTickers = await PersistenceProvider
                .GetAllCronTickerExpressions(cancellationToken)
                .ConfigureAwait(false);

            var cronTickerIds = cronTickers.Select(x => x.Id).ToArray();

            var earliestAvailableCronOccurrence = await PersistenceProvider
                .GetEarliestAvailableCronOccurrence(cronTickerIds, cancellationToken)
                .ConfigureAwait(false);

            return EarliestCronTickerGroup(cronTickers, now, earliestAvailableCronOccurrence);
        }

        private static (DateTime Next, InternalManagerContext[] Items)? EarliestCronTickerGroup(CronTickerEntity[] cronTickers, DateTime now, CronTickerOccurrenceEntity<TCronTicker> earliestStored)
        {
            DateTime? min = null;
            InternalManagerContext first = null;
            List<InternalManagerContext> ties = null;

            foreach (var cronTicker in cronTickers)
            {
                var next = CronScheduleCache.GetNextOccurrenceOrDefault(cronTicker.Expression, now);
                if (next is null) continue;
                
                if(earliestStored != null && earliestStored.ExecutionTime == next && cronTicker.Id == earliestStored.CronTickerId)
                    continue;
                
                var n = next.Value;
                if (min is null || n < min)
                {
                    min = n;
                    first = new InternalManagerContext(cronTicker.Id)
                    {
                        FunctionName = cronTicker.Function,
                        Expression = cronTicker.Expression,
                        Retries = cronTicker.Retries,
                        RetryIntervals = cronTicker.RetryIntervals,
                    };
                    
                    ties = null;
                }
                else if (n == min)
                {
                    ties ??= new List<InternalManagerContext>(2) { first };
                    ties.Add(new InternalManagerContext(cronTicker.Id)
                    {
                        FunctionName = cronTicker.Function,
                        Expression = cronTicker.Expression,
                        Retries = cronTicker.Retries,
                        RetryIntervals = cronTicker.RetryIntervals,
                    });
                }
            }

            // If we have a stored occurrence, compare/merge
            if (earliestStored is not null)
            {
                var storedTime = earliestStored.ExecutionTime;
                var storedItem = new InternalManagerContext(earliestStored.CronTickerId)
                {
                    FunctionName = earliestStored.CronTicker.Function,
                    Expression = earliestStored.CronTicker.Expression,
                    Retries = earliestStored.CronTicker.Retries,
                    RetryIntervals = earliestStored.CronTicker.RetryIntervals,
                    NextCronOccurrence = new NextCronOccurrence(earliestStored.Id, earliestStored.CreatedAt)
                };

                // If no in-memory occurrences or stored is earlier, return stored only
                if (min is null || storedTime < min.Value)
                    return (storedTime, [storedItem]);

                // If stored time equals the earliest in-memory time, aggregate them
                if (storedTime == min.Value)
                {
                    if (ties is null)
                        return (min.Value, [first, storedItem]);

                    ties.Add(storedItem);
                    return (min.Value, ties.ToArray());
                }

                // Stored is later than min, return in-memory winners only
                var winners = ties is null ? [first] : ties.ToArray();
                return (min.Value, winners);
            }

            // No stored occurrence - return in-memory winners or null if none
            if (min is null)
                return null;

            var finalWinners = ties is null ? [first] : ties.ToArray();
            return (min.Value, finalWinners);
        }

        public async Task SetTickersInProgress(InternalFunctionContext[] resources, CancellationToken cancellationToken = default)
        {
            var unifiedFunctionContext = new InternalFunctionContext().SetProperty(x => x.Status, TickerStatus.InProgress);
            
            var cronTickerIds = resources.Where(x => x.Type == TickerType.CronTickerOccurrence).Select(x => x.TickerId).ToArray();
            var timeTickerIds = resources.Where(x => x.Type == TickerType.TimeTicker).Select(x => x.TickerId).ToArray();

            if (cronTickerIds.Length != 0 && timeTickerIds.Length != 0)
            {
                var updateCronTickerOccurrencesTask = PersistenceProvider.UpdateCronTickerOccurrencesWithUnifiedContext(cronTickerIds, unifiedFunctionContext, cancellationToken);
                var updateTimeTickersTask = PersistenceProvider.UpdateTimeTickersWithUnifiedContext(timeTickerIds, unifiedFunctionContext, cancellationToken);
                await Task.WhenAll(updateCronTickerOccurrencesTask, updateTimeTickersTask).ConfigureAwait(false);
            }
            else
            {
                if (cronTickerIds.Length != 0)                 
                    await PersistenceProvider.UpdateCronTickerOccurrencesWithUnifiedContext(cronTickerIds, unifiedFunctionContext, cancellationToken).ConfigureAwait(false);
            
                if (timeTickerIds.Length != 0)
                    await PersistenceProvider.UpdateTimeTickersWithUnifiedContext(timeTickerIds, unifiedFunctionContext, cancellationToken).ConfigureAwait(false);
            }
            
            foreach (var resource in resources)
            {
                resource.Status = TickerStatus.InProgress;
                
                if(resource.Type == TickerType.TimeTicker)
                    await NotificationHubSender.UpdateTimeTickerFromInternalFunctionContext<TTimeTicker>(resource).ConfigureAwait(false);
                else
                    await NotificationHubSender.UpdateCronOccurrenceFromInternalFunctionContext<TCronTicker>(resource).ConfigureAwait(false);
            }
        }

        public async Task ReleaseAcquiredResources(InternalFunctionContext[] resources, CancellationToken cancellationToken = default)
        {
            if (resources is null)
            {
                await Task.WhenAll(
                    PersistenceProvider.ReleaseAcquiredCronTickerOccurrences([], cancellationToken),
                    PersistenceProvider.ReleaseAcquiredTimeTickers([], cancellationToken)
                    );
                return;
            }
            
            var cronTickerIds = resources.Length == 0 
                ? [] 
                : resources.Where(x => x.Type == TickerType.CronTickerOccurrence).Select(x => x.TickerId).ToArray();
            
            if(cronTickerIds.Length != 0)
                await PersistenceProvider.ReleaseAcquiredCronTickerOccurrences(cronTickerIds, cancellationToken).ConfigureAwait(false);
            
            var timeTickerIds = resources.Length == 0
                ? []
                : resources.Where(x => x.Type == TickerType.TimeTicker).Select(x => x.TickerId).ToArray();
            
            if (timeTickerIds.Length != 0)
                await PersistenceProvider.ReleaseAcquiredTimeTickers(timeTickerIds, cancellationToken).ConfigureAwait(false);
        }
        
        public async Task UpdateTickerAsync(InternalFunctionContext functionContext, CancellationToken cancellationToken = default)
        {
            if (functionContext.Type == TickerType.CronTickerOccurrence)
            {
                await PersistenceProvider.UpdateCronTickerOccurrence(functionContext, cancellationToken).ConfigureAwait(false);
                await NotificationHubSender.UpdateCronOccurrenceFromInternalFunctionContext<TCronTicker>(functionContext).ConfigureAwait(false);
            }
            else
            {
                await PersistenceProvider.UpdateTimeTicker(functionContext, cancellationToken).ConfigureAwait(false);
                await NotificationHubSender.UpdateTimeTickerFromInternalFunctionContext<TTimeTicker>(functionContext).ConfigureAwait(false);
            }
        }

        public async Task<T> GetRequestAsync<T>(Guid tickerId, TickerType type, CancellationToken cancellationToken = default)
        {
            var request = type == TickerType.CronTickerOccurrence
                ? await PersistenceProvider.GetCronTickerOccurrenceRequest(tickerId, cancellationToken: cancellationToken).ConfigureAwait(false)
                : await PersistenceProvider.GetTimeTickerRequest(tickerId, cancellationToken: cancellationToken).ConfigureAwait(false);

            return request == null ? default : TickerHelper.ReadTickerRequest<T>(request);
        }

        public async Task<InternalFunctionContext[]> RunTimedOutTickers(CancellationToken cancellationToken = default)
        {
            var results = new List<InternalFunctionContext>();
            
            await foreach(var timedOutTimeTicker in PersistenceProvider.QueueTimedOutTimeTickers(cancellationToken).ConfigureAwait(false))
            {
                var functionContext = new InternalFunctionContext
                {
                    FunctionName = timedOutTimeTicker.Function,
                    TickerId = timedOutTimeTicker.Id,
                    Type = TickerType.TimeTicker,
                    Retries = timedOutTimeTicker.Retries,
                    RetryIntervals = timedOutTimeTicker.RetryIntervals
                };
                results.Add(functionContext);
                await NotificationHubSender.UpdateTimeTickerFromInternalFunctionContext<TTimeTicker>(functionContext).ConfigureAwait(false);
            }

            await foreach (var timedOutCronTicker in PersistenceProvider.QueueTimedOutCronTickerOccurrences(cancellationToken).ConfigureAwait(false))
            {
                var functionContext = new InternalFunctionContext
                {
                    FunctionName = timedOutCronTicker.CronTicker.Function,
                    TickerId = timedOutCronTicker.Id,
                    Type = TickerType.TimeTicker,
                    Retries = timedOutCronTicker.CronTicker.Retries,
                    RetryIntervals = timedOutCronTicker.CronTicker.RetryIntervals,
                    ParentId = timedOutCronTicker.CronTickerId
                };
                
                results.Add(functionContext);
                await NotificationHubSender.UpdateCronOccurrenceFromInternalFunctionContext<TCronTicker>(functionContext).ConfigureAwait(false);
            }
            
            return results.ToArray();
        }
        
        public async Task MigrateDefinedCronTickers((string, string)[] cronExpressions, CancellationToken cancellationToken = default)
            => await PersistenceProvider.MigrateDefinedCronTickers(cronExpressions, cancellationToken).ConfigureAwait(false);

        public async Task DeleteTicker(Guid tickerId, TickerType type, CancellationToken cancellationToken = default)
        {
            if (type == TickerType.CronTickerOccurrence)
               await PersistenceProvider.RemoveTimeTickers([tickerId], cancellationToken).ConfigureAwait(false);
            else
                await PersistenceProvider.RemoveCronTickers([tickerId], cancellationToken).ConfigureAwait(false);
        }

        public async Task CascadeBatchUpdate(Guid parentTickerId, TickerStatus currentStatus, CancellationToken cancellationToken = default)
        {
            // var childTickers = await PersistenceProvider.GetChildTickersByParentId(parentTickerId, cancellationToken);
            //
            // foreach (var child in childTickers)
            // {
            //     child.Status = child.BatchRunCondition switch
            //     {
            //         BatchRunCondition.OnSuccess when _successConditions.Contains(currentStatus) => TickerStatus
            //             .Idle,
            //         BatchRunCondition.OnAnyCompletedStatus when _completedStatuses.Contains(currentStatus) =>
            //             TickerStatus.Idle,
            //         _ => child.Status
            //     };
            // }

            //TODO
            // await PersistenceProvider.UpdateTimeTickers(childTickers, cancellationToken: cancellationToken);
        }
    }
}