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
    internal class InternalTickerManager<TTimeTicker, TCronTicker> : IInternalTickerManager
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        private readonly ITickerPersistenceProvider<TTimeTicker, TCronTicker> _persistenceProvider;
        private readonly ITickerClock _clock;
        private readonly ITickerQNotificationHubSender _notificationHubSender;

        public InternalTickerManager(
            ITickerPersistenceProvider<TTimeTicker, TCronTicker> persistenceProvider,
            ITickerClock clock,
            ITickerQNotificationHubSender notificationHubSender)
        {
            _persistenceProvider = persistenceProvider;
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _notificationHubSender = notificationHubSender;
        }
        
        public async Task<(TimeSpan TimeRemaining, InternalFunctionContext[] Functions)> GetNextTickers(CancellationToken cancellationToken = default)
        {
            var now = _clock.UtcNow;

            var minCronGroupTask = GetEarliestCronTickerGroupAsync(cancellationToken);
            var minTimeTickersTask = _persistenceProvider.GetEarliestTimeTickers(cancellationToken);

            await Task.WhenAll(minCronGroupTask, minTimeTickersTask).ConfigureAwait(false);

            var minCronGroup = await minCronGroupTask.ConfigureAwait(false);
            var minTimeTickers = await minTimeTickersTask.ConfigureAwait(false);

            var cronTime = minCronGroup?.Key;
            var timeTickerTime = minTimeTickers.Length > 0
                ? minTimeTickers[0].ExecutionTime
                : null;

            if (cronTime is null && timeTickerTime is null)
                return (Timeout.InfiniteTimeSpan, []);

            TimeSpan timeRemaining;
            bool includeCron = false;
            bool includeTimeTickers = false;

            if (cronTime is null)
            {
                includeTimeTickers = true;
                timeRemaining = SafeRemaining(timeTickerTime!.Value, now);
            }
            else if (timeTickerTime is null)
            {
                includeCron = true;
                timeRemaining = SafeRemaining(cronTime.Value, now);
            }
            else
            {
                var cronSecond = new DateTime(cronTime.Value.Year, cronTime.Value.Month, cronTime.Value.Day,
                    cronTime.Value.Hour, cronTime.Value.Minute, cronTime.Value.Second);
                var timeSecond = new DateTime(timeTickerTime.Value.Year, timeTickerTime.Value.Month, timeTickerTime.Value.Day,
                    timeTickerTime.Value.Hour, timeTickerTime.Value.Minute, timeTickerTime.Value.Second);

                if (cronSecond == timeSecond)
                {
                    includeCron = true;
                    includeTimeTickers = true;
                    var earliest = cronTime < timeTickerTime ? cronTime.Value : timeTickerTime.Value;
                    timeRemaining = SafeRemaining(earliest, now);
                }
                else if (cronTime < timeTickerTime)
                {
                    includeCron = true;
                    timeRemaining = SafeRemaining(cronTime.Value, now);
                }
                else
                {
                    includeTimeTickers = true;
                    timeRemaining = SafeRemaining(timeTickerTime.Value, now);
                }
            }

            if (!includeCron && !includeTimeTickers)
                return (Timeout.InfiniteTimeSpan, []);

            InternalFunctionContext[] cronFunctions = [];
            InternalFunctionContext[] timeFunctions = [];

            if (includeCron && minCronGroup is not null)
                cronFunctions = await QueueNextCronTickersAsync(minCronGroup.Value, cancellationToken).ConfigureAwait(false);

            if (includeTimeTickers && minTimeTickers.Length > 0)
                timeFunctions = await QueueNextTimeTickersAsync(minTimeTickers, cancellationToken).ConfigureAwait(false);

            if (cronFunctions.Length == 0 && timeFunctions.Length == 0)
                return (timeRemaining, []);

            if (cronFunctions.Length == 0)
                return (timeRemaining, timeFunctions);

            if (timeFunctions.Length == 0)
                return (timeRemaining, cronFunctions);

            var merged = new InternalFunctionContext[cronFunctions.Length + timeFunctions.Length];
            cronFunctions.AsSpan().CopyTo(merged.AsSpan(0, cronFunctions.Length));
            timeFunctions.AsSpan().CopyTo(merged.AsSpan(cronFunctions.Length, timeFunctions.Length));

            return (timeRemaining, merged);
        }

        private static TimeSpan SafeRemaining(DateTime target, DateTime now)
        {
            var remaining = target - now;
            return remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
        }

        private async Task<InternalFunctionContext[]> QueueNextTimeTickersAsync(TimeTickerEntity[] minTimeTickers, CancellationToken cancellationToken = default)
        {
            var results = new List<InternalFunctionContext>();
            
            await foreach(var updatedTimeTicker in _persistenceProvider.QueueTimeTickers(minTimeTickers, cancellationToken))
            {
                results.Add(new InternalFunctionContext
                {
                    FunctionName = updatedTimeTicker.Function,
                    TickerId = updatedTimeTicker.Id,
                    Type = TickerType.TimeTicker,
                    Retries = updatedTimeTicker.Retries,
                    RetryIntervals = updatedTimeTicker.RetryIntervals,
                    ParentId = updatedTimeTicker.ParentId,
                    ExecutionTime = updatedTimeTicker.ExecutionTime ?? _clock.UtcNow,
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
                
                await _notificationHubSender.UpdateTimeTickerNotifyAsync(updatedTimeTicker);
            }
           
            return results.ToArray();
        }

        private async Task<InternalFunctionContext[]> QueueNextCronTickersAsync((DateTime Key, InternalManagerContext[] Items) minCronTicker, CancellationToken cancellationToken = default)
        {
            var results = new List<InternalFunctionContext>();
            
            await foreach (var occurrence in _persistenceProvider.QueueCronTickerOccurrences(minCronTicker, cancellationToken).ConfigureAwait(false))
            {
                results.Add(new InternalFunctionContext
                {
                    ParentId = occurrence.CronTickerId,
                    FunctionName = occurrence.CronTicker.Function,
                    TickerId = occurrence.Id,
                    Type = TickerType.CronTickerOccurrence,
                    Retries = occurrence.CronTicker.Retries,
                    RetryIntervals = occurrence.CronTicker.RetryIntervals,
                    ExecutionTime = occurrence.ExecutionTime
                });
                
                if (occurrence.CreatedAt == occurrence.UpdatedAt && _notificationHubSender != null)
                    await _notificationHubSender.AddCronOccurrenceAsync(occurrence.CronTickerId, occurrence).ConfigureAwait(false);
                else if(_notificationHubSender != null)
                    await _notificationHubSender.UpdateCronOccurrenceAsync(occurrence.CronTickerId, occurrence).ConfigureAwait(false);
            }
            
            return results.ToArray();
        }
        
        private async Task<(DateTime Key, InternalManagerContext[] Items)?> GetEarliestCronTickerGroupAsync(CancellationToken cancellationToken = default)
        {
            var now = _clock.UtcNow;

            var cronTickers = await _persistenceProvider
                .GetAllCronTickerExpressions(cancellationToken)
                .ConfigureAwait(false);

            var cronTickerIds = cronTickers.Select(x => x.Id).ToArray();

            var earliestAvailableCronOccurrence = await _persistenceProvider
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
                var updateCronTickerOccurrencesTask = _persistenceProvider.UpdateCronTickerOccurrencesWithUnifiedContext(cronTickerIds, unifiedFunctionContext, cancellationToken);
                var updateTimeTickersTask = _persistenceProvider.UpdateTimeTickersWithUnifiedContext(timeTickerIds, unifiedFunctionContext, cancellationToken);
                await Task.WhenAll(updateCronTickerOccurrencesTask, updateTimeTickersTask).ConfigureAwait(false);
            }
            else
            {
                if (cronTickerIds.Length != 0)                 
                    await _persistenceProvider.UpdateCronTickerOccurrencesWithUnifiedContext(cronTickerIds, unifiedFunctionContext, cancellationToken).ConfigureAwait(false);
            
                if (timeTickerIds.Length != 0)
                    await _persistenceProvider.UpdateTimeTickersWithUnifiedContext(timeTickerIds, unifiedFunctionContext, cancellationToken).ConfigureAwait(false);
            }
            
            foreach (var resource in resources)
            {
                resource.Status = TickerStatus.InProgress;
                
                if(resource.Type == TickerType.TimeTicker)
                    await _notificationHubSender.UpdateTimeTickerFromInternalFunctionContext<TTimeTicker>(resource).ConfigureAwait(false);
                else
                    await _notificationHubSender.UpdateCronOccurrenceFromInternalFunctionContext<TCronTicker>(resource).ConfigureAwait(false);
            }
        }

        public async Task ReleaseAcquiredResources(InternalFunctionContext[] resources, CancellationToken cancellationToken = default)
        {
            if (resources is null)
            {
                await Task.WhenAll(
                    _persistenceProvider.ReleaseAcquiredCronTickerOccurrences([], cancellationToken),
                    _persistenceProvider.ReleaseAcquiredTimeTickers([], cancellationToken)
                    );
                return;
            }
            
            var cronTickerIds = resources.Length == 0 
                ? [] 
                : resources.Where(x => x.Type == TickerType.CronTickerOccurrence).Select(x => x.TickerId).ToArray();
            
            if(cronTickerIds.Length != 0)
                await _persistenceProvider.ReleaseAcquiredCronTickerOccurrences(cronTickerIds, cancellationToken).ConfigureAwait(false);
            
            var timeTickerIds = resources.Length == 0
                ? []
                : resources.Where(x => x.Type == TickerType.TimeTicker).Select(x => x.TickerId).ToArray();
            
            if (timeTickerIds.Length != 0)
                await _persistenceProvider.ReleaseAcquiredTimeTickers(timeTickerIds, cancellationToken).ConfigureAwait(false);
        }
        
        public async Task UpdateTickerAsync(InternalFunctionContext functionContext, CancellationToken cancellationToken = default)
        {
            if (functionContext.Type == TickerType.CronTickerOccurrence)
            {
                await _persistenceProvider.UpdateCronTickerOccurrence(functionContext, cancellationToken).ConfigureAwait(false);
                await _notificationHubSender.UpdateCronOccurrenceFromInternalFunctionContext<TCronTicker>(functionContext).ConfigureAwait(false);
            }
            else
            {
                await _persistenceProvider.UpdateTimeTicker(functionContext, cancellationToken).ConfigureAwait(false);
                await _notificationHubSender.UpdateTimeTickerFromInternalFunctionContext<TTimeTicker>(functionContext).ConfigureAwait(false);
            }
        }
        
        public async Task UpdateSkipTimeTickersWithUnifiedContextAsync(InternalFunctionContext[] resources, CancellationToken cancellationToken = default)
        {
            var unifiedFunctionContext = new InternalFunctionContext()
                .SetProperty(x => x.Status, TickerStatus.Skipped)
                .SetProperty(x => x.ExecutedAt, _clock.UtcNow)
                .SetProperty(x => x.ExceptionDetails, "Rule RunCondition did not match!");
            
            if (resources.Length != 0)
                await _persistenceProvider.UpdateTimeTickersWithUnifiedContext(resources.Select(x => x.TickerId).ToArray(), unifiedFunctionContext, cancellationToken).ConfigureAwait(false);
            
            foreach (var resource in resources)
            {
                resource.ExecutedAt = _clock.UtcNow;
                resource.Status = TickerStatus.Skipped;
                resource.ExceptionDetails = "Rule RunCondition did not match!";
                if(resource.Type == TickerType.TimeTicker)
                    await _notificationHubSender.UpdateTimeTickerFromInternalFunctionContext<TTimeTicker>(resource).ConfigureAwait(false);
                else
                    await _notificationHubSender.UpdateCronOccurrenceFromInternalFunctionContext<TCronTicker>(resource).ConfigureAwait(false);
            }
        }

        public async Task<T> GetRequestAsync<T>(Guid tickerId, TickerType type, CancellationToken cancellationToken = default)
        {
            var request = type == TickerType.CronTickerOccurrence
                ? await _persistenceProvider.GetCronTickerOccurrenceRequest(tickerId, cancellationToken: cancellationToken).ConfigureAwait(false)
                : await _persistenceProvider.GetTimeTickerRequest(tickerId, cancellationToken: cancellationToken).ConfigureAwait(false);

            return request == null || request.Length == 0
                ? default
                : TickerHelper.ReadTickerRequest<T>(request);
        }

        public async Task<InternalFunctionContext[]> RunTimedOutTickers(CancellationToken cancellationToken = default)
        {
            var results = new List<InternalFunctionContext>();
            
            await foreach(var timedOutTimeTicker in _persistenceProvider.QueueTimedOutTimeTickers(cancellationToken).ConfigureAwait(false))
            {
                results.Add(new InternalFunctionContext
                {
                    FunctionName = timedOutTimeTicker.Function,
                    TickerId = timedOutTimeTicker.Id,
                    Type = TickerType.TimeTicker,
                    Retries = timedOutTimeTicker.Retries,
                    RetryIntervals = timedOutTimeTicker.RetryIntervals,
                    ParentId = timedOutTimeTicker.ParentId,
                    ExecutionTime = timedOutTimeTicker.ExecutionTime ?? _clock.UtcNow,
                    TimeTickerChildren = timedOutTimeTicker.Children.Select(ch => new InternalFunctionContext
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
                
                await _notificationHubSender.UpdateTimeTickerNotifyAsync(timedOutTimeTicker).ConfigureAwait(false);
            }

            await foreach (var timedOutCronTicker in _persistenceProvider.QueueTimedOutCronTickerOccurrences(cancellationToken).ConfigureAwait(false))
            {
                var functionContext = new InternalFunctionContext
                {
                    FunctionName = timedOutCronTicker.CronTicker.Function,
                    TickerId = timedOutCronTicker.Id,
                    Type = TickerType.CronTickerOccurrence,
                    Retries = timedOutCronTicker.CronTicker.Retries,
                    RetryIntervals = timedOutCronTicker.CronTicker.RetryIntervals,
                    ParentId = timedOutCronTicker.CronTickerId,
                    ExecutionTime = timedOutCronTicker.ExecutionTime
                };
                
                results.Add(functionContext);
                await _notificationHubSender.UpdateCronOccurrenceFromInternalFunctionContext<TCronTicker>(functionContext).ConfigureAwait(false);
            }
            
            return results.ToArray();
        }
        
        public async Task MigrateDefinedCronTickers((string, string)[] cronExpressions, CancellationToken cancellationToken = default)
            => await _persistenceProvider.MigrateDefinedCronTickers(cronExpressions, cancellationToken).ConfigureAwait(false);

        public async Task DeleteTicker(Guid tickerId, TickerType type, CancellationToken cancellationToken = default)
        {
            if (type == TickerType.CronTickerOccurrence)
                await _persistenceProvider.RemoveCronTickers([tickerId], cancellationToken).ConfigureAwait(false);
            else
                await _persistenceProvider.RemoveTimeTickers([tickerId], cancellationToken).ConfigureAwait(false);
        }

        public async Task ReleaseDeadNodeResources(string instanceIdentifier, CancellationToken cancellationToken = default)
        {
            var cronOccurrence = _persistenceProvider.ReleaseDeadNodeOccurrenceResources(instanceIdentifier, cancellationToken);
            
            var timeTickers = _persistenceProvider.ReleaseDeadNodeTimeTickerResources(instanceIdentifier, cancellationToken);
            
            await Task.WhenAll(cronOccurrence, timeTickers).ConfigureAwait(false);
        }
    }
}
