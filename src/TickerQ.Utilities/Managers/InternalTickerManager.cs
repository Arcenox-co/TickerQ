using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization.Metadata;
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
        private readonly SchedulerOptionsBuilder _schedulerOptions;

        public InternalTickerManager(
            ITickerPersistenceProvider<TTimeTicker, TCronTicker> persistenceProvider,
            ITickerClock clock,
            ITickerQNotificationHubSender notificationHubSender,
            SchedulerOptionsBuilder schedulerOptions)
        {
            _persistenceProvider = persistenceProvider;
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _notificationHubSender = notificationHubSender;
            _schedulerOptions = schedulerOptions;
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

        private static InternalFunctionContext BuildTimeTickerContext(TimeTickerEntity ticker)
        {
            return new InternalFunctionContext
            {
                FunctionName = ticker.Function,
                TickerId = ticker.Id,
                Type = TickerType.TimeTicker,
                Retries = ticker.Retries,
                RetryIntervals = ticker.RetryIntervals,
                TimeoutSeconds = ticker.TimeoutSeconds,
                ParentId = ticker.ParentId,
                AcquisitionToken = ticker.AcquisitionToken,
                RunCondition = ticker.RunCondition ?? RunCondition.OnAnyCompletedStatus,
                TimeTickerChildren = ticker.Children?
                    .Select(BuildTimeTickerContext)
                    .ToList() ?? []
            };
        }

        private async Task<InternalFunctionContext[]> QueueNextTimeTickersAsync(TimeTickerEntity[] minTimeTickers, CancellationToken cancellationToken = default)
        {
            var results = new List<InternalFunctionContext>();
            
            await foreach(var updatedTimeTicker in _persistenceProvider.QueueTimeTickers(minTimeTickers, cancellationToken))
            {
                var context = BuildTimeTickerContext(updatedTimeTicker);
                context.ExecutionTime = updatedTimeTicker.ExecutionTime ?? _clock.UtcNow;
                results.Add(context);

                await _notificationHubSender.UpdateTimeTickerNotifyAsync(updatedTimeTicker.Id);
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
                    TimeoutSeconds = occurrence.CronTicker.TimeoutSeconds,
                    AcquisitionToken = occurrence.AcquisitionToken,
                    ExecutionTime = occurrence.ExecutionTime
                });
                
                if (occurrence.CreatedAt == occurrence.UpdatedAt && _notificationHubSender != null)
                    await _notificationHubSender.AddCronOccurrenceAsync(occurrence.CronTickerId, occurrence.Id).ConfigureAwait(false);
                else if(_notificationHubSender != null)
                    await _notificationHubSender.UpdateCronOccurrenceAsync(occurrence.CronTickerId, occurrence.Id).ConfigureAwait(false);
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
                        TimeoutSeconds = cronTicker.TimeoutSeconds,
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
                        TimeoutSeconds = cronTicker.TimeoutSeconds,
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
                    TimeoutSeconds = earliestStored.CronTicker.TimeoutSeconds,
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

        public async Task<InternalFunctionContext[]> SetTickersInProgress(InternalFunctionContext[] resources, CancellationToken cancellationToken = default)
        {
            var cronResources = resources.Where(x => x.Type == TickerType.CronTickerOccurrence).ToArray();
            var timeResources = resources.Where(x => x.Type == TickerType.TimeTicker).ToArray();
            var cronTask = cronResources.Length == 0
                ? Task.FromResult(Array.Empty<Guid>())
                : _persistenceProvider.TransitionQueuedCronOccurrencesToInProgressAsync(
                    cronResources.Select(x => new AcquisitionLease(x.TickerId, x.AcquisitionToken)).ToArray(), cancellationToken);
            var timeTask = timeResources.Length == 0
                ? Task.FromResult(Array.Empty<Guid>())
                : _persistenceProvider.TransitionQueuedTimeTickersToInProgressAsync(
                    timeResources.Select(x => new AcquisitionLease(x.TickerId, x.AcquisitionToken)).ToArray(), cancellationToken);
            await Task.WhenAll(cronTask, timeTask).ConfigureAwait(false);

            var winningCronIds = new HashSet<Guid>(cronTask.Result);
            var winningTimeIds = new HashSet<Guid>(timeTask.Result);
            var winners = resources.Where(x => x.Type == TickerType.CronTickerOccurrence
                ? winningCronIds.Contains(x.TickerId)
                : winningTimeIds.Contains(x.TickerId)).ToArray();
            foreach (var resource in winners)
            {
                resource.Status = TickerStatus.InProgress;

                if(resource.Type == TickerType.TimeTicker)
                    await _notificationHubSender.UpdateTimeTickerFromInternalFunctionContext<TTimeTicker>(resource).ConfigureAwait(false);
                else
                    await _notificationHubSender.UpdateCronOccurrenceFromInternalFunctionContext<TCronTicker>(resource).ConfigureAwait(false);
            }

            return winners;
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
            foreach (var resource in resources)
            {
                resource.ResetUpdateProps()
                    .SetProperty(x => x.Status, TickerStatus.Idle)
                    .SetProperty(x => x.ReleaseLock, true);

                if (resource.Type == TickerType.CronTickerOccurrence)
                    await _persistenceProvider.UpdateCronTickerOccurrence(resource, cancellationToken)
                        .ConfigureAwait(false);
                else
                    await _persistenceProvider.UpdateTimeTicker(resource, cancellationToken)
                        .ConfigureAwait(false);
            }
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

        public async Task<T> GetRequestAsync<T>(Guid tickerId, TickerType type, JsonTypeInfo<T> typeInfo, CancellationToken cancellationToken = default)
        {
            var request = type == TickerType.CronTickerOccurrence
                ? await _persistenceProvider.GetCronTickerOccurrenceRequest(tickerId, cancellationToken: cancellationToken).ConfigureAwait(false)
                : await _persistenceProvider.GetTimeTickerRequest(tickerId, cancellationToken: cancellationToken).ConfigureAwait(false);

            return request == null || request.Length == 0
                ? default
                : TickerHelper.ReadTickerRequest(request, typeInfo);
        }

        public async Task<InternalFunctionContext[]> RunTimedOutTickers(CancellationToken cancellationToken = default)
        {
            var results = new List<InternalFunctionContext>();
            
            await foreach(var timedOutTimeTicker in _persistenceProvider.QueueTimedOutTimeTickers(cancellationToken).ConfigureAwait(false))
            {
                var context = BuildTimeTickerContext(timedOutTimeTicker);
                context.ExecutionTime = timedOutTimeTicker.ExecutionTime ?? _clock.UtcNow;
                results.Add(context);

                await _notificationHubSender.UpdateTimeTickerNotifyAsync(timedOutTimeTicker.Id).ConfigureAwait(false);
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
                    TimeoutSeconds = timedOutCronTicker.CronTicker.TimeoutSeconds,
                    ParentId = timedOutCronTicker.CronTickerId,
                    AcquisitionToken = timedOutCronTicker.AcquisitionToken,
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

        public async Task<int> SkipStaleCronOccurrencesAsync(TimeSpan staleThreshold, CancellationToken cancellationToken = default)
            => await _persistenceProvider.SkipStaleCronOccurrencesAsync(staleThreshold, cancellationToken).ConfigureAwait(false);

        public async Task ReleaseDeadNodeResources(string instanceIdentifier, CancellationToken cancellationToken = default)
        {
            var cronOccurrence = _persistenceProvider.ReleaseDeadNodeOccurrenceResources(instanceIdentifier, cancellationToken);

            var timeTickers = _persistenceProvider.ReleaseDeadNodeTimeTickerResources(instanceIdentifier, cancellationToken);

            await Task.WhenAll(cronOccurrence, timeTickers).ConfigureAwait(false);
        }

        public bool SupportsLeaseBasedRecovery => _persistenceProvider.SupportsLeaseBasedRecovery;

        public async Task<int> RenewActiveTickerLeasesAsync(Guid[] timeTickerIds, Guid[] occurrenceIds, CancellationToken cancellationToken = default)
        {
            var leaseUntil = _clock.UtcNow.Add(_schedulerOptions.LeaseDuration);
            var renewed = 0;

            if (timeTickerIds is { Length: > 0 })
                renewed += await _persistenceProvider.RenewTimeTickerLeases(timeTickerIds, leaseUntil, cancellationToken).ConfigureAwait(false);

            if (occurrenceIds is { Length: > 0 })
                renewed += await _persistenceProvider.RenewCronTickerOccurrenceLeases(occurrenceIds, leaseUntil, cancellationToken).ConfigureAwait(false);

            return renewed;
        }

        public async Task<Guid[]> GetLostLeaseTickerIdsAsync(Guid[] timeTickerIds, Guid[] occurrenceIds, CancellationToken cancellationToken = default)
        {
            var held = await _persistenceProvider.GetStillHeldTickerIds(timeTickerIds, occurrenceIds, cancellationToken).ConfigureAwait(false);
            var heldSet = new HashSet<Guid>(held);

            var lost = new List<Guid>();
            if (timeTickerIds != null)
                lost.AddRange(timeTickerIds.Where(id => !heldSet.Contains(id)));
            if (occurrenceIds != null)
                lost.AddRange(occurrenceIds.Where(id => !heldSet.Contains(id)));

            return lost.ToArray();
        }

        public async Task<int> RenewActiveTickerLeasesAsync(IReadOnlyCollection<AcquisitionLease> timeTickerLeases, IReadOnlyCollection<AcquisitionLease> occurrenceLeases, CancellationToken cancellationToken = default)
        {
            var leaseUntil = _clock.UtcNow.Add(_schedulerOptions.LeaseDuration);
            var renewed = 0;

            if (timeTickerLeases is { Count: > 0 })
                renewed += await _persistenceProvider.RenewTimeTickerLeases(timeTickerLeases, leaseUntil, cancellationToken).ConfigureAwait(false);

            if (occurrenceLeases is { Count: > 0 })
                renewed += await _persistenceProvider.RenewCronTickerOccurrenceLeases(occurrenceLeases, leaseUntil, cancellationToken).ConfigureAwait(false);

            return renewed;
        }

        public async Task<TickerExecutionLease[]> GetLostLeaseTickerIdsAsync(
            IReadOnlyCollection<AcquisitionLease> timeTickerLeases,
            IReadOnlyCollection<AcquisitionLease> occurrenceLeases,
            CancellationToken cancellationToken = default)
        {
            timeTickerLeases ??= Array.Empty<AcquisitionLease>();
            occurrenceLeases ??= Array.Empty<AcquisitionLease>();
            var heldTimeTask = _persistenceProvider.GetStillHeldTickerIds(
                timeTickerLeases, Array.Empty<AcquisitionLease>(), cancellationToken);
            var heldCronTask = _persistenceProvider.GetStillHeldTickerIds(
                Array.Empty<AcquisitionLease>(), occurrenceLeases, cancellationToken);
            await Task.WhenAll(heldTimeTask, heldCronTask).ConfigureAwait(false);

            var heldTime = new HashSet<Guid>(heldTimeTask.Result);
            var heldCron = new HashSet<Guid>(heldCronTask.Result);
            var lost = new List<TickerExecutionLease>();
            lost.AddRange(timeTickerLeases
                .Where(lease => !heldTime.Contains(lease.TickerId))
                .Select(lease => new TickerExecutionLease(
                    TickerType.TimeTicker, lease.TickerId, lease.AcquisitionToken)));
            lost.AddRange(occurrenceLeases
                .Where(lease => !heldCron.Contains(lease.TickerId))
                .Select(lease => new TickerExecutionLease(
                    TickerType.CronTickerOccurrence, lease.TickerId, lease.AcquisitionToken)));

            return lost.ToArray();
        }

        public async Task<StaleTickerRecoveryResult> RecoverStaleTickersAsync(CancellationToken cancellationToken = default)
            => await _persistenceProvider.RecoverStaleTickers(_schedulerOptions.MaxStaleRestarts, cancellationToken).ConfigureAwait(false);
    }
}
