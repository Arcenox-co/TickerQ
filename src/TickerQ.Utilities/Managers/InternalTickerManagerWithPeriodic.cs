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
    /// <summary>
    /// Internal ticker manager that supports TimeTicker, CronTicker, and PeriodicTicker.
    /// </summary>
    internal class InternalTickerManagerWithPeriodic<TTimeTicker, TCronTicker, TPeriodicTicker> : IInternalTickerManager
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
        where TPeriodicTicker : PeriodicTickerEntity, new()
    {
        private readonly ITickerPersistenceProvider<TTimeTicker, TCronTicker> _persistenceProvider;
        private readonly IPeriodicTickerPersistenceProvider<TPeriodicTicker> _periodicPersistenceProvider;
        private readonly ITickerClock _clock;
        private readonly ITickerQNotificationHubSender _notificationHubSender;

        public InternalTickerManagerWithPeriodic(
            ITickerPersistenceProvider<TTimeTicker, TCronTicker> persistenceProvider,
            IPeriodicTickerPersistenceProvider<TPeriodicTicker> periodicPersistenceProvider,
            ITickerClock clock,
            ITickerQNotificationHubSender notificationHubSender)
        {
            _persistenceProvider = persistenceProvider;
            _periodicPersistenceProvider = periodicPersistenceProvider;
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _notificationHubSender = notificationHubSender;
        }

        public async Task<(TimeSpan TimeRemaining, InternalFunctionContext[] Functions)> GetNextTickers(CancellationToken cancellationToken = default)
        {
            var now = _clock.UtcNow;

            // Get all three types in parallel
            var minCronGroupTask = GetEarliestCronTickerGroupAsync(cancellationToken);
            var minTimeTickersTask = _persistenceProvider.GetEarliestTimeTickers(cancellationToken);
            var minPeriodicGroupTask = GetEarliestPeriodicTickerGroupAsync(cancellationToken);

            await Task.WhenAll(minCronGroupTask, minTimeTickersTask, minPeriodicGroupTask).ConfigureAwait(false);

            var minCronGroup = await minCronGroupTask.ConfigureAwait(false);
            var minTimeTickers = await minTimeTickersTask.ConfigureAwait(false);
            var minPeriodicGroup = await minPeriodicGroupTask.ConfigureAwait(false);

            var cronTime = minCronGroup?.Key;
            var timeTickerTime = minTimeTickers.Length > 0 ? minTimeTickers[0].ExecutionTime : null;
            var periodicTime = minPeriodicGroup?.Key;

            // Find the earliest time across all ticker types
            DateTime? earliestTime = null;
            if (cronTime.HasValue) earliestTime = cronTime;
            if (timeTickerTime.HasValue && (!earliestTime.HasValue || timeTickerTime < earliestTime)) earliestTime = timeTickerTime;
            if (periodicTime.HasValue && (!earliestTime.HasValue || periodicTime < earliestTime)) earliestTime = periodicTime;

            if (!earliestTime.HasValue)
                return (Timeout.InfiniteTimeSpan, []);

            var timeRemaining = SafeRemaining(earliestTime.Value, now);
            var results = new List<InternalFunctionContext>();

            // Include all ticker types that match the earliest time (within 1 second)
            var threshold = earliestTime.Value.AddSeconds(1);

            if (cronTime.HasValue && cronTime <= threshold && minCronGroup.HasValue)
            {
                var cronFunctions = await QueueNextCronTickersAsync(minCronGroup.Value, cancellationToken).ConfigureAwait(false);
                results.AddRange(cronFunctions);
            }

            if (timeTickerTime.HasValue && timeTickerTime <= threshold && minTimeTickers.Length > 0)
            {
                var timeFunctions = await QueueNextTimeTickersAsync(minTimeTickers, cancellationToken).ConfigureAwait(false);
                results.AddRange(timeFunctions);
            }

            if (periodicTime.HasValue && periodicTime <= threshold && minPeriodicGroup.HasValue)
            {
                var periodicFunctions = await QueueNextPeriodicTickersAsync(minPeriodicGroup.Value, cancellationToken).ConfigureAwait(false);
                results.AddRange(periodicFunctions);
            }

            return (timeRemaining, results.ToArray());
        }

        private static TimeSpan SafeRemaining(DateTime target, DateTime now)
        {
            var remaining = target - now;
            return remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
        }

        #region TimeTicker Methods

        private async Task<InternalFunctionContext[]> QueueNextTimeTickersAsync(TimeTickerEntity[] minTimeTickers, CancellationToken cancellationToken = default)
        {
            var results = new List<InternalFunctionContext>();

            await foreach (var updatedTimeTicker in _persistenceProvider.QueueTimeTickers(minTimeTickers, cancellationToken))
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
                        RunCondition = ch.RunCondition ?? RunCondition.OnAnyCompletedStatus
                    }).ToList()
                });

                await _notificationHubSender.UpdateTimeTickerNotifyAsync(updatedTimeTicker);
            }

            return results.ToArray();
        }

        #endregion

        #region CronTicker Methods

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

        private static (DateTime Next, InternalManagerContext[] Items)? EarliestCronTickerGroup(
            CronTickerEntity[] cronTickers, 
            DateTime now, 
            CronTickerOccurrenceEntity<TCronTicker> earliestStored)
        {
            DateTime? min = null;
            InternalManagerContext first = null;
            List<InternalManagerContext> ties = null;

            foreach (var cronTicker in cronTickers)
            {
                var next = CronScheduleCache.GetNextOccurrenceOrDefault(cronTicker.Expression, now);
                if (next is null) continue;

                if (earliestStored != null && earliestStored.ExecutionTime == next && cronTicker.Id == earliestStored.CronTickerId)
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

                if (min is null || storedTime < min.Value)
                    return (storedTime, [storedItem]);

                if (storedTime == min.Value)
                {
                    if (ties is null)
                        return (min.Value, [first, storedItem]);
                    ties.Add(storedItem);
                    return (min.Value, ties.ToArray());
                }

                var winners = ties is null ? [first] : ties.ToArray();
                return (min.Value, winners);
            }

            if (min is null)
                return null;

            var finalWinners = ties is null ? [first] : ties.ToArray();
            return (min.Value, finalWinners);
        }

        private async Task<InternalFunctionContext[]> QueueNextCronTickersAsync(
            (DateTime Key, InternalManagerContext[] Items) minCronTicker, 
            CancellationToken cancellationToken = default)
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
                else if (_notificationHubSender != null)
                    await _notificationHubSender.UpdateCronOccurrenceAsync(occurrence.CronTickerId, occurrence).ConfigureAwait(false);
            }

            return results.ToArray();
        }

        #endregion

        #region PeriodicTicker Methods

        private async Task<(DateTime Key, InternalManagerContext[] Items)?> GetEarliestPeriodicTickerGroupAsync(CancellationToken cancellationToken = default)
        {
            var now = _clock.UtcNow;

            var periodicTickers = await _periodicPersistenceProvider
                .GetAllActivePeriodicTickers(cancellationToken)
                .ConfigureAwait(false);

            if (periodicTickers.Length == 0)
                return null;

            var periodicTickerIds = periodicTickers.Select(x => x.Id).ToArray();

            var earliestAvailableOccurrence = await _periodicPersistenceProvider
                .GetEarliestAvailablePeriodicOccurrence(periodicTickerIds, cancellationToken)
                .ConfigureAwait(false);

            return EarliestPeriodicTickerGroup(periodicTickers, now, earliestAvailableOccurrence);
        }

        private (DateTime Next, InternalManagerContext[] Items)? EarliestPeriodicTickerGroup(
            PeriodicTickerEntity[] periodicTickers,
            DateTime now,
            PeriodicTickerOccurrenceEntity<TPeriodicTicker> earliestStored)
        {
            DateTime? min = null;
            InternalManagerContext first = null;
            List<InternalManagerContext> ties = null;

            foreach (var periodicTicker in periodicTickers)
            {
                var next = PeriodicTickerManager<TPeriodicTicker>.CalculateNextExecution(periodicTicker, now);
                if (next == DateTime.MaxValue) continue;

                // Skip if there's already an occurrence for this exact time
                if (earliestStored != null && 
                    earliestStored.ExecutionTime == next && 
                    periodicTicker.Id == earliestStored.PeriodicTickerId)
                    continue;

                if (min is null || next < min)
                {
                    min = next;
                    first = new InternalManagerContext(periodicTicker.Id)
                    {
                        FunctionName = periodicTicker.Function,
                        Interval = periodicTicker.Interval,
                        Retries = periodicTicker.Retries,
                        RetryIntervals = periodicTicker.RetryIntervals,
                    };
                    ties = null;
                }
                else if (next == min)
                {
                    ties ??= new List<InternalManagerContext>(2) { first };
                    ties.Add(new InternalManagerContext(periodicTicker.Id)
                    {
                        FunctionName = periodicTicker.Function,
                        Interval = periodicTicker.Interval,
                        Retries = periodicTicker.Retries,
                        RetryIntervals = periodicTicker.RetryIntervals,
                    });
                }
            }

            // Handle stored occurrence
            if (earliestStored is not null)
            {
                var storedTime = earliestStored.ExecutionTime;
                var storedItem = new InternalManagerContext(earliestStored.PeriodicTickerId)
                {
                    FunctionName = earliestStored.PeriodicTicker.Function,
                    Interval = earliestStored.PeriodicTicker.Interval,
                    Retries = earliestStored.PeriodicTicker.Retries,
                    RetryIntervals = earliestStored.PeriodicTicker.RetryIntervals,
                    NextPeriodicOccurrence = new NextPeriodicOccurrence(earliestStored.Id, earliestStored.UpdatedAt)
                };

                if (min is null || storedTime < min.Value)
                    return (storedTime, [storedItem]);

                if (storedTime == min.Value)
                {
                    if (ties is null)
                        return (min.Value, [first, storedItem]);
                    ties.Add(storedItem);
                    return (min.Value, ties.ToArray());
                }

                var winners = ties is null ? [first] : ties.ToArray();
                return (min.Value, winners);
            }

            if (min is null)
                return null;

            var finalWinners = ties is null ? [first] : ties.ToArray();
            return (min.Value, finalWinners);
        }

        private async Task<InternalFunctionContext[]> QueueNextPeriodicTickersAsync(
            (DateTime Key, InternalManagerContext[] Items) minPeriodicTicker,
            CancellationToken cancellationToken = default)
        {
            var results = new List<InternalFunctionContext>();

            await foreach (var occurrence in _periodicPersistenceProvider.QueuePeriodicTickerOccurrences(minPeriodicTicker, cancellationToken).ConfigureAwait(false))
            {
                results.Add(new InternalFunctionContext
                {
                    ParentId = occurrence.PeriodicTickerId,
                    FunctionName = occurrence.PeriodicTicker.Function,
                    TickerId = occurrence.Id,
                    Type = TickerType.PeriodicTickerOccurrence,
                    Retries = occurrence.PeriodicTicker.Retries,
                    RetryIntervals = occurrence.PeriodicTicker.RetryIntervals,
                    ExecutionTime = occurrence.ExecutionTime
                });
            }

            return results.ToArray();
        }

        #endregion

        #region Common Methods

        public async Task SetTickersInProgress(InternalFunctionContext[] resources, CancellationToken cancellationToken = default)
        {
            var unifiedFunctionContext = new InternalFunctionContext().SetProperty(x => x.Status, TickerStatus.InProgress);

            var cronTickerIds = resources.Where(x => x.Type == TickerType.CronTickerOccurrence).Select(x => x.TickerId).ToArray();
            var timeTickerIds = resources.Where(x => x.Type == TickerType.TimeTicker).Select(x => x.TickerId).ToArray();
            var periodicTickerIds = resources.Where(x => x.Type == TickerType.PeriodicTickerOccurrence).Select(x => x.TickerId).ToArray();

            var tasks = new List<Task>();

            if (cronTickerIds.Length != 0)
                tasks.Add(_persistenceProvider.UpdateCronTickerOccurrencesWithUnifiedContext(cronTickerIds, unifiedFunctionContext, cancellationToken));

            if (timeTickerIds.Length != 0)
                tasks.Add(_persistenceProvider.UpdateTimeTickersWithUnifiedContext(timeTickerIds, unifiedFunctionContext, cancellationToken));

            if (periodicTickerIds.Length != 0)
                tasks.Add(_periodicPersistenceProvider.UpdatePeriodicTickerOccurrencesWithUnifiedContext(periodicTickerIds, unifiedFunctionContext, cancellationToken));

            await Task.WhenAll(tasks).ConfigureAwait(false);

            foreach (var resource in resources)
            {
                resource.Status = TickerStatus.InProgress;

                if (resource.Type == TickerType.TimeTicker)
                    await _notificationHubSender.UpdateTimeTickerFromInternalFunctionContext<TTimeTicker>(resource).ConfigureAwait(false);
                else if (resource.Type == TickerType.CronTickerOccurrence)
                    await _notificationHubSender.UpdateCronOccurrenceFromInternalFunctionContext<TCronTicker>(resource).ConfigureAwait(false);
                // TODO: Add PeriodicTickerOccurrence notifications here (e.g., _notificationHubSender.UpdatePeriodicOccurrenceFromInternalFunctionContext) in Phase 3 - Dashboard support.
            }
        }

        public async Task ReleaseAcquiredResources(InternalFunctionContext[] resources, CancellationToken cancellationToken = default)
        {
            if (resources is null)
            {
                await Task.WhenAll(
                    _persistenceProvider.ReleaseAcquiredCronTickerOccurrences([], cancellationToken),
                    _persistenceProvider.ReleaseAcquiredTimeTickers([], cancellationToken),
                    _periodicPersistenceProvider.ReleaseAcquiredPeriodicTickerOccurrences([], cancellationToken)
                );
                return;
            }

            var cronTickerIds = resources.Where(x => x.Type == TickerType.CronTickerOccurrence).Select(x => x.TickerId).ToArray();
            var timeTickerIds = resources.Where(x => x.Type == TickerType.TimeTicker).Select(x => x.TickerId).ToArray();
            var periodicTickerIds = resources.Where(x => x.Type == TickerType.PeriodicTickerOccurrence).Select(x => x.TickerId).ToArray();

            var tasks = new List<Task>();

            if (cronTickerIds.Length != 0)
                tasks.Add(_persistenceProvider.ReleaseAcquiredCronTickerOccurrences(cronTickerIds, cancellationToken));

            if (timeTickerIds.Length != 0)
                tasks.Add(_persistenceProvider.ReleaseAcquiredTimeTickers(timeTickerIds, cancellationToken));

            if (periodicTickerIds.Length != 0)
                tasks.Add(_periodicPersistenceProvider.ReleaseAcquiredPeriodicTickerOccurrences(periodicTickerIds, cancellationToken));

            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        public async Task UpdateTickerAsync(InternalFunctionContext functionContext, CancellationToken cancellationToken = default)
        {
            if (functionContext.Type == TickerType.CronTickerOccurrence)
            {
                await _persistenceProvider.UpdateCronTickerOccurrence(functionContext, cancellationToken).ConfigureAwait(false);
                await _notificationHubSender.UpdateCronOccurrenceFromInternalFunctionContext<TCronTicker>(functionContext).ConfigureAwait(false);
            }
            else if (functionContext.Type == TickerType.PeriodicTickerOccurrence)
            {
                await _periodicPersistenceProvider.UpdatePeriodicTickerOccurrence(functionContext, cancellationToken).ConfigureAwait(false);
                
                // Update parent ticker's LastExecutedAt and ExecutionCount
                if (functionContext.ParentId.HasValue && 
                    (functionContext.Status == TickerStatus.Done || functionContext.Status == TickerStatus.DueDone))
                {
                    await _periodicPersistenceProvider.UpdatePeriodicTickerAfterExecution(
                        functionContext.ParentId.Value, 
                        functionContext.ExecutedAt, 
                        cancellationToken).ConfigureAwait(false);
                }
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
                await _persistenceProvider.UpdateTimeTickersWithUnifiedContext(
                    resources.Select(x => x.TickerId).ToArray(), 
                    unifiedFunctionContext, 
                    cancellationToken).ConfigureAwait(false);

            foreach (var resource in resources)
            {
                resource.ExecutedAt = _clock.UtcNow;
                resource.Status = TickerStatus.Skipped;
                resource.ExceptionDetails = "Rule RunCondition did not match!";
                if (resource.Type == TickerType.TimeTicker)
                    await _notificationHubSender.UpdateTimeTickerFromInternalFunctionContext<TTimeTicker>(resource).ConfigureAwait(false);
                else
                    await _notificationHubSender.UpdateCronOccurrenceFromInternalFunctionContext<TCronTicker>(resource).ConfigureAwait(false);
            }
        }

        public async Task<T> GetRequestAsync<T>(Guid tickerId, TickerType type, CancellationToken cancellationToken = default)
        {
            byte[] request;
            
            if (type == TickerType.CronTickerOccurrence)
                request = await _persistenceProvider.GetCronTickerOccurrenceRequest(tickerId, cancellationToken).ConfigureAwait(false);
            else if (type == TickerType.PeriodicTickerOccurrence)
                request = await _periodicPersistenceProvider.GetPeriodicTickerOccurrenceRequest(tickerId, cancellationToken).ConfigureAwait(false);
            else
                request = await _persistenceProvider.GetTimeTickerRequest(tickerId, cancellationToken).ConfigureAwait(false);

            return request == null ? default : TickerHelper.ReadTickerRequest<T>(request);
        }

        public async Task<InternalFunctionContext[]> RunTimedOutTickers(CancellationToken cancellationToken = default)
        {
            var results = new List<InternalFunctionContext>();

            // TimeTickers
            await foreach (var timedOutTimeTicker in _persistenceProvider.QueueTimedOutTimeTickers(cancellationToken).ConfigureAwait(false))
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
                });

                await _notificationHubSender.UpdateTimeTickerNotifyAsync(timedOutTimeTicker).ConfigureAwait(false);
            }

            // CronTickers
            await foreach (var timedOutCronTicker in _persistenceProvider.QueueTimedOutCronTickerOccurrences(cancellationToken).ConfigureAwait(false))
            {
                results.Add(new InternalFunctionContext
                {
                    FunctionName = timedOutCronTicker.CronTicker.Function,
                    TickerId = timedOutCronTicker.Id,
                    Type = TickerType.CronTickerOccurrence,
                    Retries = timedOutCronTicker.CronTicker.Retries,
                    RetryIntervals = timedOutCronTicker.CronTicker.RetryIntervals,
                    ParentId = timedOutCronTicker.CronTickerId,
                    ExecutionTime = timedOutCronTicker.ExecutionTime
                });
            }

            // PeriodicTickers
            await foreach (var timedOutPeriodic in _periodicPersistenceProvider.QueueTimedOutPeriodicTickerOccurrences(cancellationToken).ConfigureAwait(false))
            {
                results.Add(new InternalFunctionContext
                {
                    FunctionName = timedOutPeriodic.PeriodicTicker.Function,
                    TickerId = timedOutPeriodic.Id,
                    Type = TickerType.PeriodicTickerOccurrence,
                    Retries = timedOutPeriodic.PeriodicTicker.Retries,
                    RetryIntervals = timedOutPeriodic.PeriodicTicker.RetryIntervals,
                    ParentId = timedOutPeriodic.PeriodicTickerId,
                    ExecutionTime = timedOutPeriodic.ExecutionTime
                });
            }

            return results.ToArray();
        }

        public async Task MigrateDefinedCronTickers((string, string)[] cronExpressions, CancellationToken cancellationToken = default)
            => await _persistenceProvider.MigrateDefinedCronTickers(cronExpressions, cancellationToken).ConfigureAwait(false);

        public async Task DeleteTicker(Guid tickerId, TickerType type, CancellationToken cancellationToken = default)
        {
            if (type == TickerType.CronTickerOccurrence)
                await _persistenceProvider.RemoveCronTickers([tickerId], cancellationToken).ConfigureAwait(false);
            else if (type == TickerType.PeriodicTickerOccurrence)
                await _periodicPersistenceProvider.RemovePeriodicTickers([tickerId], cancellationToken).ConfigureAwait(false);
            else
                await _persistenceProvider.RemoveTimeTickers([tickerId], cancellationToken).ConfigureAwait(false);
        }

        public async Task ReleaseDeadNodeResources(string instanceIdentifier, CancellationToken cancellationToken = default)
        {
            await Task.WhenAll(
                _persistenceProvider.ReleaseDeadNodeOccurrenceResources(instanceIdentifier, cancellationToken),
                _persistenceProvider.ReleaseDeadNodeTimeTickerResources(instanceIdentifier, cancellationToken),
                _periodicPersistenceProvider.ReleaseDeadNodePeriodicOccurrenceResources(instanceIdentifier, cancellationToken)
            ).ConfigureAwait(false);
        }

        #endregion
    }
}
