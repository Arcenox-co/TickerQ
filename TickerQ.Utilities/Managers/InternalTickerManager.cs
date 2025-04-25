using NCrontab;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Models;
using TickerQ.Utilities.Models.Ticker;

namespace TickerQ.Utilities.Managers
{
    internal abstract class InternalTickerManager<TTimeTicker, TCronTicker> : IInternalTickerManager
    where TTimeTicker : TimeTicker, new() where TCronTicker : CronTicker, new()
    {
        private readonly string _lockHolder;
        protected readonly ITickerPersistenceProvider<TTimeTicker, TCronTicker> PersistenceProvider;
        protected readonly ITickerHost TickerHost;
        protected readonly ITickerClock Clock;
        protected readonly ITickerQNotificationHubSender NotificationHubSender;

        protected InternalTickerManager(ITickerPersistenceProvider<TTimeTicker, TCronTicker> persistenceProvider,
            ITickerHost tickerHost, ITickerClock clock, TickerOptionsBuilder tickerOptionsBuilder,
            ITickerQNotificationHubSender notificationHubSender)
        {
            _lockHolder = tickerOptionsBuilder?.InstanceIdentifier ?? Environment.MachineName;
            PersistenceProvider = persistenceProvider;
            TickerHost = tickerHost ?? throw new ArgumentNullException(nameof(tickerHost));
            Clock = clock ?? throw new ArgumentNullException(nameof(clock));
            NotificationHubSender = notificationHubSender;
        }

        public async Task<(TimeSpan TimeRemaining, InternalFunctionContext[] Functions)> GetNextTickers(
            CancellationToken cancellationToken = default)
        {
            var minCronGroup = await GetEarliestCronTickerGroupAsync(cancellationToken).ConfigureAwait(false);
            var minTimeTicker = await GetEarliestTimeTickerAsync(cancellationToken).ConfigureAwait(false);

            var minTimeRemaining = CalculateMinTimeRemaining(minCronGroup, minTimeTicker ?? default);

            if (minTimeRemaining == Timeout.InfiniteTimeSpan)
                return (Timeout.InfiniteTimeSpan, Array.Empty<InternalFunctionContext>());

            var nextTickers = await RetrieveEligibleTickersAsync(minCronGroup, minTimeTicker ?? default, cancellationToken)
                .ConfigureAwait(false);

            return (minTimeRemaining, nextTickers);
        }

        private TimeSpan CalculateMinTimeRemaining(IGrouping<DateTime, string> minCronTicker, DateTime minTimeTicker)
        {
            var now = Clock.UtcNow;
            var minTimeRemaining = minCronTicker != null && minTimeTicker != default
                ? (minCronTicker.Key < minTimeTicker ? minCronTicker.Key : minTimeTicker) - now
                : minCronTicker != null
                    ? minCronTicker.Key - now
                    : minTimeTicker != default
                        ? minTimeTicker - now
                        : Timeout.InfiniteTimeSpan;

            return minTimeRemaining;
        }

        private async Task<InternalFunctionContext[]> RetrieveEligibleTickersAsync(
            IGrouping<DateTime, string> minCronTicker, DateTime minTimeTicker,
            CancellationToken cancellationToken = default)
        {
            var hasValidCronTicker = minCronTicker != null;
            var hasValidTimeTicker = minTimeTicker != default;

            var areCloseInTime = hasValidCronTicker && hasValidTimeTicker
                                                    && Math.Abs((minTimeTicker - minCronTicker.Key).TotalSeconds) == 0;

            if (areCloseInTime)
            {
                var nextCronTickers = await RetrieveNextCronTickersAsync(minCronTicker.ToArray(), cancellationToken)
                    .ConfigureAwait(false);
                var nextTimeTickers = await RetrieveNextTimeTickersAsync(minTimeTicker, cancellationToken)
                    .ConfigureAwait(false);
                return nextCronTickers.Union(nextTimeTickers).ToArray();
            }

            if (!hasValidCronTicker)
                return await RetrieveNextTimeTickersAsync(minTimeTicker, cancellationToken).ConfigureAwait(false);

            if (!hasValidTimeTicker)
                return await RetrieveNextCronTickersAsync(minCronTicker.ToArray(), cancellationToken)
                    .ConfigureAwait(false);

            return minTimeTicker < minCronTicker.Key
                ? await RetrieveNextTimeTickersAsync(minTimeTicker, cancellationToken).ConfigureAwait(false)
                : await RetrieveNextCronTickersAsync(minCronTicker.ToArray(), cancellationToken).ConfigureAwait(false);
        }

        private async Task<InternalFunctionContext[]> RetrieveNextTimeTickersAsync(DateTime minDate,
            CancellationToken cancellationToken = default)
        {
            var roundedMinDate = new DateTime(minDate.Ticks - minDate.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);

            var timeTickers = await PersistenceProvider.RetrieveNextTimeTickers(_lockHolder, roundedMinDate, cancellationToken).ConfigureAwait(false);

            var lockedAndQueuedTimeTickers = LockAndQueueTimeTickers(timeTickers).ToArray();

            if (lockedAndQueuedTimeTickers.Length > 0)
                await PersistenceProvider.UpdateTimeTickers(timeTickers, cancellationToken).ConfigureAwait(false);

            if (NotificationHubSender != null && lockedAndQueuedTimeTickers.Length > 0)
                await NotificationHubSender.UpdateTimeTickerNotifyAsync(lockedAndQueuedTimeTickers);

            return lockedAndQueuedTimeTickers;

            IEnumerable<InternalFunctionContext> LockAndQueueTimeTickers(TTimeTicker[] scopeTimeTickers)
            {
                var now = Clock.UtcNow;

                foreach (var timeTicker in scopeTimeTickers)
                {
                    timeTicker.Status = TickerStatus.Queued;
                    timeTicker.LockHolder = _lockHolder;
                    timeTicker.LockedAt = now;

                    yield return new InternalFunctionContext()
                    {
                        FunctionName = timeTicker.Function,
                        TickerId = timeTicker.Id,
                        Type = TickerType.Timer,
                        Retries = timeTicker.Retries,
                        RetryIntervals = timeTicker.RetryIntervals
                    };
                }
            }
        }

        private async Task<InternalFunctionContext[]> RetrieveNextCronTickersAsync(string[] expressions,
            CancellationToken cancellationToken = default)
        {
            var now = Clock.UtcNow;

            var cronTickers = await PersistenceProvider.RetrieveNextCronTickers(expressions, cancellationToken).ConfigureAwait(false);

            var cronTickerIdSet = cronTickers.Select(t => t.Id).ToArray();

            var occurrenceList = await PersistenceProvider.RetrieveNextCronTickerOccurences(_lockHolder, cronTickerIdSet, cancellationToken).ConfigureAwait(false);

            var result = new List<InternalFunctionContext>();

            var newOccurrences = new List<CronTickerOccurrence<TCronTicker>>();

            foreach (var cronTicker in cronTickers)
            {
                var nextOccurrence = CrontabSchedule
                    .Parse(cronTicker.Expression)
                    .GetNextOccurrence(now);

                var existing = occurrenceList.FirstOrDefault(x =>
                    x.CronTickerId == cronTicker.Id &&
                    x.ExecutionTime == nextOccurrence);

                if (existing != null)
                {
                    existing.Status = TickerStatus.Queued;
                    existing.LockHolder = _lockHolder;
                    existing.LockedAt = now;

                    result.Add(new InternalFunctionContext
                    {
                        FunctionName = cronTicker.Function,
                        TickerId = existing.Id,
                        Type = TickerType.CronExpression,
                        Retries = cronTicker.Retries,
                        RetryIntervals = cronTicker.RetryIntervals
                    });

                    if (NotificationHubSender != null)
                        await NotificationHubSender.UpdateCronOccurrenceAsync(cronTicker.Id, existing);
                }
                else
                {
                    var newOccurrence = new CronTickerOccurrence<TCronTicker>
                    {
                        Id = Guid.NewGuid(),
                        Status = TickerStatus.Queued,
                        ExecutionTime = nextOccurrence,
                        LockedAt = now,
                        LockHolder = _lockHolder,
                        CronTickerId = cronTicker.Id
                    };

                    newOccurrences.Add(newOccurrence);

                    result.Add(new InternalFunctionContext
                    {
                        FunctionName = cronTicker.Function,
                        TickerId = newOccurrence.Id,
                        Type = TickerType.CronExpression,
                        Retries = cronTicker.Retries,
                        RetryIntervals = cronTicker.RetryIntervals
                    });

                    if (NotificationHubSender != null)
                        await NotificationHubSender.AddCronOccurrenceAsync(cronTicker.Id, newOccurrence);
                }
            }

            await PersistenceProvider.UpdateCronTickerOccurences(occurrenceList, cancellationToken).ConfigureAwait(false);

            if (result.Count > 0)
                await PersistenceProvider.InsertCronTickerOccurences(newOccurrences, cancellationToken).ConfigureAwait(false);

            return result.ToArray();
        }

        private async Task<DateTime?> GetEarliestTimeTickerAsync(CancellationToken cancellationToken = default)
        {
            return await PersistenceProvider.GetEarliestTimeTickerTime(Clock.UtcNow, cancellationToken).ConfigureAwait(false);
        }

        private async Task<IGrouping<DateTime, string>> GetEarliestCronTickerGroupAsync(
            CancellationToken cancellationToken = default)
        {
            var now = Clock.UtcNow;

            var expressions = await PersistenceProvider.GetAllCronTickerExpressions(cancellationToken).ConfigureAwait(false);

            var withNext = expressions
                .Select(expr => new
                {
                    Expression = expr,
                    Next = CrontabSchedule.TryParse(expr)?.GetNextOccurrence(now)
                })
                .Where(x => x.Next != null)
                .GroupBy(x => x.Next!.Value)
                .OrderBy(x => x.Key)
                .FirstOrDefault();

            return withNext?.Select(x => x.Expression).GroupBy(_ => withNext.Key).FirstOrDefault();
        }

        public Task SetTickersInProgress(
            InternalFunctionContext[] resources,
            CancellationToken cancellationToken = default)
        {
            return UpdateTickersAsync(resources,
                cronOccurrence => cronOccurrence.Status = TickerStatus.Inprogress,
                timeTicker => timeTicker.Status = TickerStatus.Inprogress, null,
                cancellationToken);
        }

        public Task ReleaseAcquiredResources(InternalFunctionContext[] resources,
            CancellationToken cancellationToken = default)
        {
            return UpdateTickersAsync(resources,
                cronOccurrence =>
                {
                    cronOccurrence.LockHolder = null;
                    cronOccurrence.Status = TickerStatus.Idle;
                    cronOccurrence.LockedAt = null;
                },
                timeTicker =>
                {
                    timeTicker.LockHolder = null;
                    timeTicker.Status = TickerStatus.Idle;
                    timeTicker.LockedAt = null;
                }, null, cancellationToken);
        }


        public async Task ReleaseOrCancelAllAcquiredResources(bool terminateExpiredTickers,
            CancellationToken cancellationToken = default)
        {
            var timeTickers = await PersistenceProvider.RetrieveLockedTimeTickers(_lockHolder, cancellationToken).ConfigureAwait(false);

            foreach (var timeTicker in timeTickers)
            {
                if (timeTicker.Status == TickerStatus.Inprogress ||
                    terminateExpiredTickers && DateTime.Compare(timeTicker.ExecutionTime, Clock.UtcNow) == 0)
                {
                    timeTicker.Status = TickerStatus.Cancelled;
                    timeTicker.LockedAt = Clock.UtcNow;
                    timeTicker.LockHolder = _lockHolder;
                }
                else
                {
                    timeTicker.Status = TickerStatus.Idle;
                    timeTicker.LockedAt = null;
                    timeTicker.LockHolder = null;
                }
            }

            await PersistenceProvider.UpdateTimeTickers(timeTickers, cancellationToken).ConfigureAwait(false);

            var cronTickerOccurrences = await PersistenceProvider.RetrieveLockedCronTickerOccurences(_lockHolder, cancellationToken).ConfigureAwait(false);

            foreach (var cronTickerOccurrence in cronTickerOccurrences)
            {
                if (cronTickerOccurrence.Status == TickerStatus.Inprogress ||
                    terminateExpiredTickers && DateTime.Compare(cronTickerOccurrence.ExecutionTime, Clock.UtcNow) > 0)
                {
                    cronTickerOccurrence.Status = TickerStatus.Cancelled;
                    cronTickerOccurrence.LockedAt = Clock.UtcNow;
                    cronTickerOccurrence.LockHolder = _lockHolder;
                }
                else
                {
                    cronTickerOccurrence.Status = TickerStatus.Idle;
                    cronTickerOccurrence.LockedAt = null;
                    cronTickerOccurrence.LockHolder = null;
                }
            }

            await PersistenceProvider.UpdateCronTickerOccurences(cronTickerOccurrences, cancellationToken).ConfigureAwait(false);
        }

        private async Task UpdateTickersAsync(
            InternalFunctionContext[] resources,
            Action<CronTickerOccurrence<TCronTicker>> cronOccurrenceUpdateAction,
            Action<TimeTicker> timeUpdateAction,
            Action<CronTicker> cronUpdateAction,
            CancellationToken cancellationToken = default)
        {
            var resourcesByType = resources.GroupBy(x => x.Type);

            foreach (var resourceType in resourcesByType)
            {
                if (resourceType.Key == TickerType.CronExpression)
                {
                    if (cronUpdateAction != null)
                    {
                        var cronTickerIds = resourceType.Select(x => x.TickerId).ToArray();

                        var cronTickers = await PersistenceProvider.GetCronTickersByIds(cronTickerIds, cancellationToken).ConfigureAwait(false);

                        foreach (var cronTicker in cronTickers)
                        {
                            cronUpdateAction(cronTicker);

                            if (NotificationHubSender != null)
                                await NotificationHubSender.UpdateCronTickerNotifyAsync(cronTicker);
                        }

                        await PersistenceProvider.UpdateCronTickers(cronTickers, cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        var cronOccurrenceIds = resourceType.Select(x => x.TickerId).ToArray();

                        var cronTickerOccurrences = await PersistenceProvider.GetCronTickerOccurencesByIds(cronOccurrenceIds, cancellationToken).ConfigureAwait(false);

                        foreach (var cronTickerOccurrence in cronTickerOccurrences)
                        {
                            cronOccurrenceUpdateAction(cronTickerOccurrence);

                            if (NotificationHubSender != null)
                                await NotificationHubSender.UpdateCronOccurrenceAsync(cronTickerOccurrence.CronTickerId, cronTickerOccurrence);
                        }

                        await PersistenceProvider.UpdateCronTickerOccurences(cronTickerOccurrences, cancellationToken).ConfigureAwait(false);
                    }
                }
                else if (resourceType.Key == TickerType.Timer)
                {
                    var timeTickerIds = resourceType.Select(x => x.TickerId).ToArray();

                    var timeTickers = await PersistenceProvider.GetTimeTickersByIds(timeTickerIds, cancellationToken).ConfigureAwait(false);

                    foreach (var timeTicker in timeTickers)
                    {
                        timeUpdateAction(timeTicker);

                        if (NotificationHubSender != null)
                            await NotificationHubSender.UpdateTimeTickerNotifyAsync(timeTicker);
                    }

                    await PersistenceProvider.UpdateTimeTickers(timeTickers, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        public Task SetTickerStatus(InternalFunctionContext context, CancellationToken cancellationToken = default)
        {
            return UpdateTickersAsync(new[] { context },
                cronOccurrence =>
                {
                    cronOccurrence.Status = context.Status;
                    cronOccurrence.ExecutedAt = Clock.UtcNow;

                    if (!string.IsNullOrEmpty(context.ExceptionDetails))
                        cronOccurrence.Exception = context.ExceptionDetails;

                    if (context.ElapsedTime > 0)
                        cronOccurrence.ElapsedTime = context.ElapsedTime;

                    if (context.RetryCount > 0)
                        cronOccurrence.RetryCount = context.RetryCount;
                },
                timeTicker =>
                {
                    if (!string.IsNullOrEmpty(context.ExceptionDetails))
                        timeTicker.Exception = context.ExceptionDetails;

                    if (context.ElapsedTime > 0)
                        timeTicker.ElapsedTime = context.ElapsedTime;

                    if (context.RetryCount > 0)
                        timeTicker.RetryCount = context.RetryCount;

                    timeTicker.Status = context.Status;
                    timeTicker.ExecutedAt = Clock.UtcNow;
                },
                null, cancellationToken);
        }

        public async Task<T> GetRequestAsync<T>(Guid tickerId, TickerType type,
            CancellationToken cancellationToken = default)
        {
            byte[] request = type == TickerType.CronExpression
                ? await PersistenceProvider.GetCronTickerRequestViaOccurence(tickerId, cancellationToken).ConfigureAwait(false)
                : await PersistenceProvider.GetTimeTickerRequest(tickerId, cancellationToken).ConfigureAwait(false);

            return request == null ? default : TickerHelper.ReadTickerRequest<T>(request);
        }

        public async Task<InternalFunctionContext[]> GetTimedOutFunctions(
            CancellationToken cancellationToken = default)
        {
            var timedOutTimeTickers = await RetrieveTimedOutTimeTickersAsync(cancellationToken).ConfigureAwait(false);
            var timedOutCronTickers = await RetrieveTimedOutCronTickersAsync(cancellationToken).ConfigureAwait(false);

            if (timedOutTimeTickers.Length == 0 && timedOutCronTickers.Length == 0)
                return Array.Empty<InternalFunctionContext>();

            return timedOutTimeTickers.Concat(timedOutCronTickers).ToArray();
        }

        private async Task<InternalFunctionContext[]> RetrieveTimedOutCronTickersAsync(
            CancellationToken cancellationToken)
        {
            var cronTickerOccurrences = await PersistenceProvider.RetrieveTimedOutCronTickerOccurrences(Clock.UtcNow, cancellationToken).ConfigureAwait(false);

            if (cronTickerOccurrences.Length == 0)
                return Array.Empty<InternalFunctionContext>();

            var updatedCronTickers = UpdateCronTickerOccurrences(cronTickerOccurrences).ToArray();

            if (updatedCronTickers.Length > 0)
            {
                await PersistenceProvider.UpdateCronTickerOccurences(cronTickerOccurrences, cancellationToken).ConfigureAwait(false);

                foreach (var updatedOccurrence in cronTickerOccurrences)
                {
                    if (NotificationHubSender != null)
                        await NotificationHubSender.UpdateCronOccurrenceAsync(updatedOccurrence.CronTickerId, updatedOccurrence);
                }
            }

            return updatedCronTickers;

            IEnumerable<InternalFunctionContext> UpdateCronTickerOccurrences(
                CronTickerOccurrence<TCronTicker>[] scopeCronTickerOccurrences)
            {
                foreach (var cronTickerOccurrence in scopeCronTickerOccurrences)
                {
                    cronTickerOccurrence.Status = TickerStatus.Inprogress;
                    cronTickerOccurrence.LockHolder = _lockHolder;

                    yield return new InternalFunctionContext()
                    {
                        FunctionName = cronTickerOccurrence.CronTicker.Function,
                        TickerId = cronTickerOccurrence.Id,
                        Type = TickerType.CronExpression,
                        Retries = cronTickerOccurrence.CronTicker.Retries,
                        RetryIntervals = cronTickerOccurrence.CronTicker.RetryIntervals
                    };
                }
            }
        }

        private async Task<InternalFunctionContext[]> RetrieveTimedOutTimeTickersAsync(
            CancellationToken cancellationToken)
        {
            var timeTickers = await PersistenceProvider.RetrieveTimedOutTimeTickers(Clock.UtcNow, cancellationToken).ConfigureAwait(false);

            if (timeTickers.Length == 0)
                return Array.Empty<InternalFunctionContext>();

            var updatedTimeTickers = UpdateTimeTickers(timeTickers).ToArray();

            if (updatedTimeTickers.Length > 0)
                await PersistenceProvider.UpdateTimeTickers(timeTickers, cancellationToken).ConfigureAwait(false);

            return updatedTimeTickers;

            IEnumerable<InternalFunctionContext> UpdateTimeTickers(TTimeTicker[] scopeTimeTickers)
            {
                foreach (var timeTicker in scopeTimeTickers)
                {
                    timeTicker.Status = TickerStatus.Inprogress;
                    timeTicker.LockHolder = _lockHolder;

                    yield return new InternalFunctionContext
                    {
                        FunctionName = timeTicker.Function,
                        TickerId = timeTicker.Id,
                        Type = TickerType.Timer,
                        Retries = timeTicker.Retries,
                        RetryIntervals = timeTicker.RetryIntervals
                    };
                }
            }
        }

        public Task UpdateTickerRetries(InternalFunctionContext context, CancellationToken cancellationToken = default)
        {
            return UpdateTickersAsync(new[] { context },
                cronTickerOccurrence => { cronTickerOccurrence.RetryCount = context.RetryCount; },
                timeTicker => { timeTicker.RetryCount = context.RetryCount; },
                null, cancellationToken);
        }

        public async Task SyncWithDbMemoryCronTickers(IList<(string, string)> cronExpressions,
            CancellationToken cancellationToken = default)
        {
            var existingFunctions = new HashSet<Guid>();

            var existingCronTickers = await PersistenceProvider.RetrieveAllExistingInitializedCronTickers(cancellationToken).ConfigureAwait(false);

            var newCronTickers = new List<TCronTicker>();

            foreach (var (function, expression) in cronExpressions)
            {
                if (existingCronTickers.FirstOrDefault(x => x.Function == function) is { } existingCronTicker)
                {
                    existingFunctions.Add(existingCronTicker.Id);

                    if (existingCronTicker.Expression == expression)
                        continue;

                    existingCronTicker.Expression = expression;
                    existingCronTicker.UpdatedAt = DateTime.UtcNow;
                }
                else
                {
                    newCronTickers.Add(new TCronTicker
                    {
                        Id = Guid.NewGuid(),
                        Function = function,
                        Expression = expression,
                        InitIdentifier = $"MemoryTicker_Seeded_{Guid.NewGuid()}",
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    });
                }
            }

            await PersistenceProvider.UpdateCronTickers(existingCronTickers, cancellationToken).ConfigureAwait(false);

            var nonExistingCronTickers = existingCronTickers.Where(x => !existingFunctions.Contains(x.Id)).ToList();

            if (nonExistingCronTickers.Any())
                await PersistenceProvider.RemoveCronTickers(nonExistingCronTickers, cancellationToken).ConfigureAwait(false);

            if (newCronTickers.Any())
                await PersistenceProvider.InsertCronTickers(newCronTickers, cancellationToken).ConfigureAwait(false);
        }

        public async Task DeleteTicker(Guid tickerId, TickerType type, CancellationToken cancellationToken = default)
        {
            if (type == TickerType.CronExpression)
            {
                var cronTicker = await PersistenceProvider.GetCronTickerById(tickerId, cancellationToken).ConfigureAwait(false);

                if (cronTicker is null)
                    return;

                await PersistenceProvider.RemoveCronTickers(new[] { cronTicker }, cancellationToken).ConfigureAwait(false);

                if (NotificationHubSender != null)
                    await NotificationHubSender.RemoveCronTickerNotifyAsync(tickerId);

                TickerHost.Restart();
            }
            else
            {
                var timeTicker = await PersistenceProvider.GetTimeTickerById(tickerId, cancellationToken).ConfigureAwait(false);

                if (timeTicker is null)
                    return;

                await PersistenceProvider.RemoveTimeTickers(new[] { timeTicker }, cancellationToken).ConfigureAwait(false);

                if (NotificationHubSender != null)
                    await NotificationHubSender.RemoveTimeTickerNotifyAsync(tickerId);

                TickerHost.RestartIfNeeded(timeTicker.ExecutionTime);
            }
        }
    }
}