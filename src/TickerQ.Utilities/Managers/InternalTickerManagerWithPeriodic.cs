using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ.Utilities.Managers
{
    /// <summary>
    /// Internal scheduler that adds <see cref="PeriodicTickerEntity"/> support on top of the
    /// time/cron implementation provided by <see cref="InternalTickerManager{TTimeTicker, TCronTicker}"/>.
    /// All shared scheduling logic is inherited; this class only contributes the periodic branch
    /// to <see cref="GetNextTickers"/> and merges periodic ids into the existing
    /// SetInProgress / Release / UpdateTicker / RunTimedOut / DeleteTicker / ReleaseDeadNode paths.
    /// Selected via opt-in <c>EnablePeriodic&lt;T&gt;()</c> on <see cref="TickerOptionsBuilder{TTime,TCron}"/>.
    /// </summary>
    internal sealed class InternalTickerManagerWithPeriodic<TTimeTicker, TCronTicker, TPeriodicTicker>
        : InternalTickerManager<TTimeTicker, TCronTicker>
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
        where TPeriodicTicker : PeriodicTickerEntity, new()
    {
        private readonly IPeriodicTickerPersistenceProvider<TPeriodicTicker> _periodicProvider;

        public InternalTickerManagerWithPeriodic(
            ITickerPersistenceProvider<TTimeTicker, TCronTicker> persistenceProvider,
            IPeriodicTickerPersistenceProvider<TPeriodicTicker> periodicProvider,
            ITickerClock clock,
            ITickerQNotificationHubSender notificationHubSender)
            : base(persistenceProvider, clock, notificationHubSender)
        {
            _periodicProvider = periodicProvider ?? throw new ArgumentNullException(nameof(periodicProvider));
        }

        // ---------------------------------------------------------------------
        // Scheduling: extend GetNextTickers with the periodic branch.
        // Strategy: query time/cron/periodic earliest in parallel, pick the
        // earliest, queue every source whose due time is within a small
        // co-firing window so co-scheduled jobs run together.
        // ---------------------------------------------------------------------
        public override async Task<(TimeSpan TimeRemaining, InternalFunctionContext[] Functions)> GetNextTickers(CancellationToken cancellationToken = default)
        {
            var now = Clock.UtcNow;

            var minCronGroupTask = GetEarliestCronTickerGroupAsync(cancellationToken);
            var minTimeTickersTask = PersistenceProvider.GetEarliestTimeTickers(cancellationToken);
            var minPeriodicGroupTask = GetEarliestPeriodicTickerGroupAsync(cancellationToken);

            await Task.WhenAll(minCronGroupTask, minTimeTickersTask, minPeriodicGroupTask).ConfigureAwait(false);

            var minCronGroup = await minCronGroupTask.ConfigureAwait(false);
            var minTimeTickers = await minTimeTickersTask.ConfigureAwait(false);
            var minPeriodicGroup = await minPeriodicGroupTask.ConfigureAwait(false);

            DateTime? cronTime = minCronGroup?.Key;
            DateTime? timeTickerTime = minTimeTickers.Length > 0 ? minTimeTickers[0].ExecutionTime : null;
            DateTime? periodicTime = minPeriodicGroup?.Key;

            if (cronTime is null && timeTickerTime is null && periodicTime is null)
                return (Timeout.InfiniteTimeSpan, []);

            // Earliest among the three sources.
            DateTime earliest = DateTime.MaxValue;
            if (cronTime.HasValue && cronTime.Value < earliest) earliest = cronTime.Value;
            if (timeTickerTime.HasValue && timeTickerTime.Value < earliest) earliest = timeTickerTime.Value;
            if (periodicTime.HasValue && periodicTime.Value < earliest) earliest = periodicTime.Value;

            // Co-firing window: fire any source whose earliest entry is in the
            // same wall-clock second as the global minimum. Matches the
            // existing time/cron logic in the base implementation.
            var earliestSecond = TruncateToSecond(earliest);

            bool includeCron = cronTime.HasValue && TruncateToSecond(cronTime.Value) == earliestSecond;
            bool includeTime = timeTickerTime.HasValue && TruncateToSecond(timeTickerTime.Value) == earliestSecond;
            bool includePeriodic = periodicTime.HasValue && TruncateToSecond(periodicTime.Value) == earliestSecond;

            if (!includeCron && !includeTime && !includePeriodic)
                return (Timeout.InfiniteTimeSpan, []);

            var timeRemaining = SafeRemaining(earliest, now);

            InternalFunctionContext[] cronFunctions = [];
            InternalFunctionContext[] timeFunctions = [];
            InternalFunctionContext[] periodicFunctions = [];

            if (includeCron && minCronGroup is not null)
                cronFunctions = await QueueNextCronTickersAsync(minCronGroup.Value, cancellationToken).ConfigureAwait(false);

            if (includeTime && minTimeTickers.Length > 0)
                timeFunctions = await QueueNextTimeTickersAsync(minTimeTickers, cancellationToken).ConfigureAwait(false);

            if (includePeriodic && minPeriodicGroup is not null)
                periodicFunctions = await QueueNextPeriodicTickersAsync(minPeriodicGroup.Value, cancellationToken).ConfigureAwait(false);

            var totalLen = cronFunctions.Length + timeFunctions.Length + periodicFunctions.Length;
            if (totalLen == 0)
                return (timeRemaining, []);

            var merged = new InternalFunctionContext[totalLen];
            var offset = 0;
            if (cronFunctions.Length > 0)
            {
                cronFunctions.AsSpan().CopyTo(merged.AsSpan(offset, cronFunctions.Length));
                offset += cronFunctions.Length;
            }
            if (timeFunctions.Length > 0)
            {
                timeFunctions.AsSpan().CopyTo(merged.AsSpan(offset, timeFunctions.Length));
                offset += timeFunctions.Length;
            }
            if (periodicFunctions.Length > 0)
            {
                periodicFunctions.AsSpan().CopyTo(merged.AsSpan(offset, periodicFunctions.Length));
            }

            return (timeRemaining, merged);
        }

        // Helper kept private to avoid changing the protected surface of the base type.
        private static DateTime TruncateToSecond(DateTime dt)
            => new DateTime(dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, dt.Second, dt.Kind);

        // ---------------------------------------------------------------------
        // Periodic earliest selection. Mirrors the cron earliest-group logic
        // (in-memory candidate vs stored occurrence merge).
        // ---------------------------------------------------------------------
        private async Task<(DateTime Key, InternalManagerContext[] Items)?> GetEarliestPeriodicTickerGroupAsync(CancellationToken cancellationToken)
        {
            var now = Clock.UtcNow;

            var periodicTickers = await _periodicProvider
                .GetAllActivePeriodicTickers(cancellationToken)
                .ConfigureAwait(false);

            var ids = periodicTickers.Select(x => x.Id).ToArray();

            var earliestStored = await _periodicProvider
                .GetEarliestAvailablePeriodicOccurrence(ids, cancellationToken)
                .ConfigureAwait(false);

            return EarliestPeriodicTickerGroup(periodicTickers, now, earliestStored);
        }

        private static (DateTime Next, InternalManagerContext[] Items)? EarliestPeriodicTickerGroup(
            PeriodicTickerEntity[] periodicTickers,
            DateTime now,
            PeriodicTickerOccurrenceEntity<TPeriodicTicker> earliestStored)
        {
            DateTime? min = null;
            InternalManagerContext first = null;
            List<InternalManagerContext> ties = null;

            foreach (var p in periodicTickers)
            {
                var next = PeriodicTickerManager<TPeriodicTicker>.CalculateNextExecution(p, now);
                if (next == DateTime.MaxValue) continue;

                // Skip the in-memory candidate if a stored occurrence already covers it
                if (earliestStored != null && earliestStored.PeriodicTickerId == p.Id && earliestStored.ExecutionTime == next)
                    continue;

                if (min is null || next < min)
                {
                    min = next;
                    first = new InternalManagerContext(p.Id)
                    {
                        FunctionName = p.Function,
                        Interval = p.Interval,
                        Retries = p.Retries,
                        RetryIntervals = p.RetryIntervals,
                    };
                    ties = null;
                }
                else if (next == min)
                {
                    ties ??= new List<InternalManagerContext>(2) { first };
                    ties.Add(new InternalManagerContext(p.Id)
                    {
                        FunctionName = p.Function,
                        Interval = p.Interval,
                        Retries = p.Retries,
                        RetryIntervals = p.RetryIntervals,
                    });
                }
            }

            if (earliestStored is not null)
            {
                var storedTime = earliestStored.ExecutionTime;
                var storedItem = new InternalManagerContext(earliestStored.PeriodicTickerId)
                {
                    FunctionName = earliestStored.PeriodicTicker?.Function,
                    Interval = earliestStored.PeriodicTicker?.Interval ?? TimeSpan.Zero,
                    Retries = earliestStored.PeriodicTicker?.Retries ?? 0,
                    RetryIntervals = earliestStored.PeriodicTicker?.RetryIntervals,
                    NextPeriodicOccurrence = new NextPeriodicOccurrence(earliestStored.Id, earliestStored.UpdatedAt)
                };

                if (min is null || storedTime < min.Value)
                    return (storedTime, [storedItem]);

                if (storedTime == min.Value)
                {
                    if (ties is null) return (min.Value, [first, storedItem]);
                    ties.Add(storedItem);
                    return (min.Value, ties.ToArray());
                }

                var winners = ties is null ? [first] : ties.ToArray();
                return (min.Value, winners);
            }

            if (min is null) return null;

            var finalWinners = ties is null ? [first] : ties.ToArray();
            return (min.Value, finalWinners);
        }

        private async Task<InternalFunctionContext[]> QueueNextPeriodicTickersAsync(
            (DateTime Key, InternalManagerContext[] Items) minPeriodic,
            CancellationToken cancellationToken)
        {
            var results = new List<InternalFunctionContext>();

            await foreach (var occurrence in _periodicProvider.QueuePeriodicTickerOccurrences(minPeriodic, cancellationToken).ConfigureAwait(false))
            {
                results.Add(new InternalFunctionContext
                {
                    ParentId = occurrence.PeriodicTickerId,
                    FunctionName = occurrence.PeriodicTicker?.Function,
                    TickerId = occurrence.Id,
                    Type = TickerType.PeriodicTickerOccurrence,
                    Retries = occurrence.PeriodicTicker?.Retries ?? 0,
                    RetryIntervals = occurrence.PeriodicTicker?.RetryIntervals,
                    ExecutionTime = occurrence.ExecutionTime
                });

                if (NotificationHubSender != null)
                {
                    if (occurrence.CreatedAt == occurrence.UpdatedAt)
                        await NotificationHubSender.AddPeriodicOccurrenceAsync(occurrence.PeriodicTickerId, occurrence).ConfigureAwait(false);
                    else
                        await NotificationHubSender.UpdatePeriodicOccurrenceAsync(occurrence.PeriodicTickerId, occurrence).ConfigureAwait(false);
                }
            }

            return results.ToArray();
        }

        // ---------------------------------------------------------------------
        // SetTickersInProgress: call base to update time/cron, then add periodic.
        // Notification fan-out for periodic items is done here.
        // ---------------------------------------------------------------------
        public override async Task SetTickersInProgress(InternalFunctionContext[] resources, CancellationToken cancellationToken = default)
        {
            await base.SetTickersInProgress(
                resources.Where(r => r.Type != TickerType.PeriodicTickerOccurrence).ToArray(),
                cancellationToken).ConfigureAwait(false);

            var periodicIds = resources.Where(r => r.Type == TickerType.PeriodicTickerOccurrence).Select(r => r.TickerId).ToArray();
            if (periodicIds.Length == 0) return;

            var unified = new InternalFunctionContext().SetProperty(x => x.Status, TickerStatus.InProgress);
            await _periodicProvider.UpdatePeriodicTickerOccurrencesWithUnifiedContext(periodicIds, unified, cancellationToken).ConfigureAwait(false);

            foreach (var r in resources.Where(r => r.Type == TickerType.PeriodicTickerOccurrence))
            {
                r.Status = TickerStatus.InProgress;
                if (NotificationHubSender != null)
                    await NotificationHubSender.UpdatePeriodicOccurrenceFromInternalFunctionContext<TPeriodicTicker>(r).ConfigureAwait(false);
            }
        }

        public override async Task ReleaseAcquiredResources(InternalFunctionContext[] resources, CancellationToken cancellationToken = default)
        {
            if (resources is null)
            {
                // null contract: release everything
                await base.ReleaseAcquiredResources(null, cancellationToken).ConfigureAwait(false);
                await _periodicProvider.ReleaseAcquiredPeriodicTickerOccurrences([], cancellationToken).ConfigureAwait(false);
                return;
            }

            await base.ReleaseAcquiredResources(
                resources.Where(r => r.Type != TickerType.PeriodicTickerOccurrence).ToArray(),
                cancellationToken).ConfigureAwait(false);

            var periodicIds = resources.Where(r => r.Type == TickerType.PeriodicTickerOccurrence).Select(r => r.TickerId).ToArray();
            if (periodicIds.Length > 0)
                await _periodicProvider.ReleaseAcquiredPeriodicTickerOccurrences(periodicIds, cancellationToken).ConfigureAwait(false);
        }

        public override async Task UpdateTickerAsync(InternalFunctionContext functionContext, CancellationToken cancellationToken = default)
        {
            if (functionContext.Type != TickerType.PeriodicTickerOccurrence)
            {
                await base.UpdateTickerAsync(functionContext, cancellationToken).ConfigureAwait(false);
                return;
            }

            await _periodicProvider.UpdatePeriodicTickerOccurrence(functionContext, cancellationToken).ConfigureAwait(false);

            // On terminal-success status, advance LastExecutedAt / ExecutionCount on the parent so the
            // next interval is calculated from the actual execution moment rather than the previous one.
            if (functionContext.Status is TickerStatus.Done or TickerStatus.DueDone && functionContext.ParentId.HasValue)
            {
                var executedAt = functionContext.ExecutedAt == default ? Clock.UtcNow : functionContext.ExecutedAt;
                await _periodicProvider.UpdatePeriodicTickerAfterExecution(functionContext.ParentId.Value, executedAt, cancellationToken).ConfigureAwait(false);
            }

            if (NotificationHubSender != null)
                await NotificationHubSender.UpdatePeriodicOccurrenceFromInternalFunctionContext<TPeriodicTicker>(functionContext).ConfigureAwait(false);
        }

        // The base method only walks resources of type TimeTicker. Periodic-typed
        // skipped resources should fan out a periodic notification, not a cron one
        // (this was the bug in the previous duplicated implementation).
        public override async Task UpdateSkipTimeTickersWithUnifiedContextAsync(InternalFunctionContext[] resources, CancellationToken cancellationToken = default)
        {
            // Base handles TimeTicker (its persistence + notifications). Filter to time-only
            // to keep base's invariants intact.
            var timeOnly = resources.Where(r => r.Type == TickerType.TimeTicker).ToArray();
            if (timeOnly.Length > 0)
                await base.UpdateSkipTimeTickersWithUnifiedContextAsync(timeOnly, cancellationToken).ConfigureAwait(false);

            // Cron and periodic skipped notifications are not part of this method's contract
            // for the base type (it's "skip time tickers"). For periodic children we still
            // want a status notification; do not touch persistence here — this method
            // historically only performed a unified time-ticker update.
            if (NotificationHubSender == null) return;

            foreach (var r in resources.Where(r => r.Type == TickerType.PeriodicTickerOccurrence))
            {
                r.ExecutedAt = Clock.UtcNow;
                r.Status = TickerStatus.Skipped;
                r.ExceptionDetails = "Rule RunCondition did not match!";
                await NotificationHubSender.UpdatePeriodicOccurrenceFromInternalFunctionContext<TPeriodicTicker>(r).ConfigureAwait(false);
            }
        }

        public override async Task<T> GetRequestAsync<T>(Guid tickerId, TickerType type, CancellationToken cancellationToken = default)
        {
            if (type != TickerType.PeriodicTickerOccurrence)
                return await base.GetRequestAsync<T>(tickerId, type, cancellationToken).ConfigureAwait(false);

            var request = await _periodicProvider.GetPeriodicTickerOccurrenceRequest(tickerId, cancellationToken).ConfigureAwait(false);
            return request == null || request.Length == 0
                ? default
                : TickerHelper.ReadTickerRequest<T>(request);
        }

        public override async Task<T> GetRequestAsync<T>(Guid tickerId, TickerType type, JsonTypeInfo<T> typeInfo, CancellationToken cancellationToken = default)
        {
            if (type != TickerType.PeriodicTickerOccurrence)
                return await base.GetRequestAsync(tickerId, type, typeInfo, cancellationToken).ConfigureAwait(false);

            var request = await _periodicProvider.GetPeriodicTickerOccurrenceRequest(tickerId, cancellationToken).ConfigureAwait(false);
            return request == null || request.Length == 0
                ? default
                : TickerHelper.ReadTickerRequest(request, typeInfo);
        }

        public override async Task<InternalFunctionContext[]> RunTimedOutTickers(CancellationToken cancellationToken = default)
        {
            var baseResults = await base.RunTimedOutTickers(cancellationToken).ConfigureAwait(false);

            var periodicResults = new List<InternalFunctionContext>();
            await foreach (var timedOut in _periodicProvider.QueueTimedOutPeriodicTickerOccurrences(cancellationToken).ConfigureAwait(false))
            {
                var ctx = new InternalFunctionContext
                {
                    FunctionName = timedOut.PeriodicTicker?.Function,
                    TickerId = timedOut.Id,
                    Type = TickerType.PeriodicTickerOccurrence,
                    Retries = timedOut.PeriodicTicker?.Retries ?? 0,
                    RetryIntervals = timedOut.PeriodicTicker?.RetryIntervals,
                    ParentId = timedOut.PeriodicTickerId,
                    ExecutionTime = timedOut.ExecutionTime
                };

                periodicResults.Add(ctx);
                if (NotificationHubSender != null)
                    await NotificationHubSender.UpdatePeriodicOccurrenceFromInternalFunctionContext<TPeriodicTicker>(ctx).ConfigureAwait(false);
            }

            if (periodicResults.Count == 0)
                return baseResults;

            var merged = new InternalFunctionContext[baseResults.Length + periodicResults.Count];
            baseResults.AsSpan().CopyTo(merged.AsSpan(0, baseResults.Length));
            for (var i = 0; i < periodicResults.Count; i++)
                merged[baseResults.Length + i] = periodicResults[i];
            return merged;
        }

        public override async Task DeleteTicker(Guid tickerId, TickerType type, CancellationToken cancellationToken = default)
        {
            if (type == TickerType.PeriodicTickerOccurrence)
            {
                await _periodicProvider.RemovePeriodicTickers([tickerId], cancellationToken).ConfigureAwait(false);
                return;
            }

            await base.DeleteTicker(tickerId, type, cancellationToken).ConfigureAwait(false);
        }

        public override async Task ReleaseDeadNodeResources(string instanceIdentifier, CancellationToken cancellationToken = default)
        {
            var baseTask = base.ReleaseDeadNodeResources(instanceIdentifier, cancellationToken);
            var periodicTask = _periodicProvider.ReleaseDeadNodePeriodicOccurrenceResources(instanceIdentifier, cancellationToken);
            await Task.WhenAll(baseTask, periodicTask).ConfigureAwait(false);
        }
    }
}


