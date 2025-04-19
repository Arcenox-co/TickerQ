using Microsoft.EntityFrameworkCore;
using NCrontab;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.EntityFrameworkCore.Entities;
using TickerQ.Utilities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ.EntityFrameworkCore
{
    internal abstract class InternalTickerManager<TDbContext, TTimeTicker, TCronTicker> : IInternalTickerManager
        where TDbContext : DbContext where TTimeTicker : TimeTicker, new() where TCronTicker : CronTicker, new()
    {
        private readonly string _lockHolder;
        protected readonly TDbContext DbContext;
        protected readonly DbSet<TTimeTicker> TimeTickerContext;
        protected readonly DbSet<TCronTicker> CronTickerContext;
        protected readonly DbSet<CronTickerOccurrence<TCronTicker>> CronTickerOccurrenceContext;
        protected readonly ITickerHost TickerHost;
        protected readonly ITickerClock Clock;
        protected readonly ITickerQNotificationHubSender NotificationHubSender;

        protected InternalTickerManager(TDbContext dbContext,
            ITickerHost tickerHost, ITickerClock clock, TickerOptionsBuilder tickerOptionsBuilder,
            ITickerQNotificationHubSender notificationHubSender)
        {
            DbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            TickerHost = tickerHost ?? throw new ArgumentNullException(nameof(tickerHost));
            Clock = clock ?? throw new ArgumentNullException(nameof(clock));
            TimeTickerContext = DbContext.Set<TTimeTicker>();
            CronTickerContext = DbContext.Set<TCronTicker>();
            CronTickerOccurrenceContext = DbContext.Set<CronTickerOccurrence<TCronTicker>>();
            _lockHolder = tickerOptionsBuilder?.InstanceIdentifier ?? Environment.MachineName;
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
            var roundedMinDate =
                new DateTime(minDate.Ticks - (minDate.Ticks % TimeSpan.TicksPerSecond), DateTimeKind.Utc);

            var timeTickers = await TimeTickerContext
                .Where(x =>
                    ((x.LockHolder == null && x.Status == TickerStatus.Idle) ||
                     (x.LockHolder == _lockHolder && x.Status == TickerStatus.Queued)) &&
                    x.ExecutionTime >= roundedMinDate &&
                    x.ExecutionTime < roundedMinDate.AddSeconds(1))
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);

            var lockedAndQueuedTimeTickers = LockAndQueueTimeTickers(timeTickers).ToArray();

            if (lockedAndQueuedTimeTickers.Length > 0)
                await DbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            if (NotificationHubSender != null && lockedAndQueuedTimeTickers.Length > 0)
                await NotificationHubSender.UpdateTimeTickerNotifyAsync(lockedAndQueuedTimeTickers);
            
            foreach (var entry in DbContext.ChangeTracker.Entries())
                entry.State = EntityState.Detached;

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

            var cronTickerIds = await CronTickerContext
                .AsNoTracking()
                .Where(x => expressions.Contains(x.Expression))
                .Select(x => new { x.Id, x.Function, x.Expression, x.Retries, x.RetryIntervals })
                .ToArrayAsync(cancellationToken);

            var cronTickerIdSet = cronTickerIds.Select(t => t.Id).ToArray();

            var occurrenceList = await CronTickerOccurrenceContext
                .Where(x =>
                    cronTickerIdSet.Contains(x.CronTickerId) &&
                    ((x.LockHolder == null && x.Status == TickerStatus.Idle) ||
                     (x.LockHolder == _lockHolder && x.Status == TickerStatus.Queued)))
                .ToListAsync(cancellationToken);

            var result = new List<InternalFunctionContext>();

            foreach (var cronTicker in cronTickerIds)
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
                    
                    if(NotificationHubSender != null)
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

                    CronTickerOccurrenceContext.Add(newOccurrence);

                    result.Add(new InternalFunctionContext
                    {
                        FunctionName = cronTicker.Function,
                        TickerId = newOccurrence.Id,
                        Type = TickerType.CronExpression,
                        Retries = cronTicker.Retries,
                        RetryIntervals = cronTicker.RetryIntervals
                    });
                    
                    if(NotificationHubSender != null)
                        await NotificationHubSender.AddCronOccurrenceAsync(cronTicker.Id, newOccurrence);
                }
            }

            if (result.Count > 0)
                await DbContext.SaveChangesAsync(cancellationToken);

            foreach (var entry in DbContext.ChangeTracker.Entries())
                entry.State = EntityState.Detached;

            return result.ToArray();
        }

        private async Task<DateTime?> GetEarliestTimeTickerAsync(CancellationToken cancellationToken = default)
        {
            var now = Clock.UtcNow;

            var next = await TimeTickerContext
                .AsNoTracking()
                .Where(x => x.LockHolder  == null
                            && x.Status == TickerStatus.Idle
                            && x.ExecutionTime > now)
                .OrderBy(x => x.ExecutionTime)
                .Select(x => x.ExecutionTime)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            if (next == default(DateTime))
                return null;

            return next;
        }

        private async Task<IGrouping<DateTime, string>> GetEarliestCronTickerGroupAsync(
            CancellationToken cancellationToken = default)
        {
            var now = Clock.UtcNow;

            var expressions = await CronTickerContext
                .AsNoTracking()
                .Select(x => x.Expression)
                .Distinct()
                .ToListAsync(cancellationToken);

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

        public Task SetTickersInprogress(
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
            var timeTickers = await TimeTickerContext
                .Where(x => x.Status == TickerStatus.Queued || x.Status == TickerStatus.Inprogress)
                .Where(x => x.LockHolder == _lockHolder)
                .ToArrayAsync(cancellationToken);

            foreach (var timeTicker in timeTickers)
            {
                if (timeTicker.Status == TickerStatus.Inprogress ||
                    (terminateExpiredTickers && DateTime.Compare(timeTicker.ExecutionTime, Clock.UtcNow) == 0))
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

            TimeTickerContext.UpdateRange(timeTickers);

            var cronTickerOccurrences = await CronTickerOccurrenceContext
                .AsNoTracking()
                .Where(x => x.Status == TickerStatus.Queued || x.Status == TickerStatus.Inprogress)
                .Where(x => x.LockHolder == _lockHolder)
                .ToArrayAsync(cancellationToken);

            foreach (var cronTickerOccurrence in cronTickerOccurrences)
            {
                if (cronTickerOccurrence.Status == TickerStatus.Inprogress ||
                    (terminateExpiredTickers && DateTime.Compare(cronTickerOccurrence.ExecutionTime, Clock.UtcNow) > 0))
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

            CronTickerOccurrenceContext.UpdateRange(cronTickerOccurrences);

            await DbContext.SaveChangesAsync(cancellationToken);
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

                        if (cronTickerIds.Length == 1)
                        {
                            var cronTicker = await CronTickerContext
                                .FirstOrDefaultAsync(x => x.Id == cronTickerIds.FirstOrDefault(), cancellationToken)
                                .ConfigureAwait(false);

                            cronUpdateAction(cronTicker);
                            
                            if (NotificationHubSender != null)
                                await NotificationHubSender.UpdateCronTickerNotifyAsync(cronTicker);
                        }
                        else
                        {
                            var cronTickers = await CronTickerContext
                                .Where(x => cronTickerIds.Contains(x.Id))
                                .ToListAsync(cancellationToken)
                                .ConfigureAwait(false);

                            foreach (var cronTicker in cronTickers)
                            {
                                cronUpdateAction(cronTicker);
                                
                                if (NotificationHubSender != null)
                                    await NotificationHubSender.UpdateCronTickerNotifyAsync(cronTicker);
                            }
                        }
                    }
                    else
                    {
                        var cronOccurrenceIds = resourceType.Select(x => x.TickerId).ToArray();

                        if (cronOccurrenceIds.Length == 1)
                        {
                            var cronTickerOccurrence = await CronTickerOccurrenceContext
                                .FirstOrDefaultAsync(x => x.Id == cronOccurrenceIds.FirstOrDefault(), cancellationToken)
                                .ConfigureAwait(false);

                            cronOccurrenceUpdateAction(cronTickerOccurrence);
                            
                            if (NotificationHubSender != null)
                                await NotificationHubSender.UpdateCronOccurrenceAsync(cronTickerOccurrence.CronTickerId, cronTickerOccurrence);
                        }
                        else
                        {
                            var cronOccurrences = await CronTickerOccurrenceContext
                                .Where(x => cronOccurrenceIds.Contains(x.Id))
                                .ToListAsync(cancellationToken)
                                .ConfigureAwait(false);

                            foreach (var cronOccurrence in cronOccurrences)
                            {
                                cronOccurrenceUpdateAction(cronOccurrence);
                                
                                if (NotificationHubSender != null)
                                    await NotificationHubSender.UpdateCronOccurrenceAsync(cronOccurrence.CronTickerId, cronOccurrence);
                            }
                        }
                    }
                }
                else if (resourceType.Key == TickerType.Timer)
                {
                    var timeTickerIds = resourceType.Select(x => x.TickerId).ToArray();

                    if (timeTickerIds.Length == 1)
                    {
                        var timeTicker = await TimeTickerContext
                            .FirstOrDefaultAsync(x => x.Id == timeTickerIds.FirstOrDefault(), cancellationToken)
                            .ConfigureAwait(false);

                        timeUpdateAction(timeTicker);

                        if (NotificationHubSender != null)
                            await NotificationHubSender.UpdateTimeTickerNotifyAsync(timeTicker);
                    }
                    else
                    {
                        var timeTickers = await TimeTickerContext
                            .Where(x => timeTickerIds.Contains(x.Id))
                            .ToListAsync(cancellationToken)
                            .ConfigureAwait(false);

                        foreach (var timeTicker in timeTickers)
                        {
                            timeUpdateAction(timeTicker);
                            
                            if (NotificationHubSender != null)
                                await NotificationHubSender.UpdateTimeTickerNotifyAsync(timeTicker);
                        }
                    }
                }
            }

            await DbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            foreach (var entry in DbContext.ChangeTracker.Entries())
                entry.State = EntityState.Detached;
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
            byte[] request;
            if (type == TickerType.CronExpression)
            {
                request = await CronTickerOccurrenceContext
                    .Include(x => x.CronTicker)
                    .Where(x => x.Id == tickerId)
                    .Select(x => x.CronTicker.Request)
                    .FirstOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                request = await TimeTickerContext
                    .Where(x => x.Id == tickerId)
                    .Select(x => x.Request)
                    .FirstOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);
            }

            return request == null ? default : TickerHelper.ReadTickerRequest<T>(request);
        }

        public async Task<InternalFunctionContext[]> GetTimedOutFunctions(
            CancellationToken cancellationToken = default)
        {
            var timedOutTimeTickers = await RetrieveTimedOutTimeTickersAsync(cancellationToken).ConfigureAwait(false);
            var timedOutCronTickers = await RetrieveTimedOutCronTickersAsync(cancellationToken).ConfigureAwait(false);

            if (timedOutTimeTickers.Length == 0 && timedOutCronTickers.Length == 0)
                return Array.Empty<InternalFunctionContext>();

            await DbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return timedOutTimeTickers.Concat(timedOutCronTickers).ToArray();
        }

        private async Task<InternalFunctionContext[]> RetrieveTimedOutCronTickersAsync(
            CancellationToken cancellationToken)
        {
            var cronTickerOccurrences = await CronTickerOccurrenceContext
                .Include(x => x.CronTicker)
                .Where(x => !x.ExecutedAt.HasValue && x.Status != TickerStatus.Inprogress &&
                            x.Status != TickerStatus.Cancelled)
                .Where(x => x.ExecutionTime < Clock.UtcNow.AddSeconds(1))
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);

            if (cronTickerOccurrences.Length == 0)
                return Array.Empty<InternalFunctionContext>();

            var updatedCronTickers = UpdateCronTickerOccurrences(cronTickerOccurrences).ToArray();

            if (updatedCronTickers.Length > 0)
            {
                await DbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                foreach (var updatedOccurrence in cronTickerOccurrences)
                {
                    if(NotificationHubSender != null)
                        await NotificationHubSender.UpdateCronOccurrenceAsync(updatedOccurrence.CronTickerId, updatedOccurrence);
                }
                
                foreach (var entry in DbContext.ChangeTracker.Entries())
                    entry.State = EntityState.Detached;
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
            var now = Clock.UtcNow;

            var timeTickers = await TimeTickerContext
                .Where(x =>
                    (x.Status == TickerStatus.Idle && x.ExecutionTime.AddSeconds(1) < now) ||
                    (x.Status == TickerStatus.Queued && x.ExecutionTime.AddSeconds(3) < now))
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);

            if (timeTickers.Length == 0)
                return Array.Empty<InternalFunctionContext>();
            
            var updatedTimeTickers = UpdateTimeTickers(timeTickers).ToArray();

            if (updatedTimeTickers.Length > 0)
                await DbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            
            foreach (var entry in DbContext.ChangeTracker.Entries())
                entry.State = EntityState.Detached;
            
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

            var existingCronTickers = await CronTickerContext
                .Where(x => !string.IsNullOrEmpty(x.InitIdentifier) && x.InitIdentifier.StartsWith("MemoryTicker_Seed"))
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            var newCronTickers = new List<TCronTicker>();

            foreach (var (function, expression) in cronExpressions)
            {
                if ((existingCronTickers.FirstOrDefault(x => x.Function == function) is { } existingCronTicker))
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

            var nonExistingCronTickers = existingCronTickers.Where(x => !existingFunctions.Contains(x.Id)).ToList();

            if (nonExistingCronTickers.Any())
                CronTickerContext.RemoveRange(nonExistingCronTickers);

            if (newCronTickers.Any())
                CronTickerContext.AddRange(newCronTickers);

            await DbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task DeleteTicker(Guid tickerId, TickerType type, CancellationToken cancellationToken = default)
        {
            if (type == TickerType.CronExpression)
            {
                var cronTicker = await CronTickerContext
                    .FirstOrDefaultAsync(x => x.Id == tickerId, cancellationToken)
                    .ConfigureAwait(false);

                CronTickerContext.Remove(cronTicker);

                if (NotificationHubSender != null)
                    await NotificationHubSender.RemoveCronTickerNotifyAsync(tickerId);
                
                TickerHost.Restart();
            }
            else
            {
                var timeTicker = await TimeTickerContext
                    .FirstOrDefaultAsync(x => x.Id == tickerId, cancellationToken)
                    .ConfigureAwait(false);

                TimeTickerContext.Remove(timeTicker);

                if (NotificationHubSender != null)
                    await NotificationHubSender.RemoveTimeTickerNotifyAsync(tickerId);
                
                TickerHost.RestartIfNeeded(timeTicker.ExecutionTime);
            }

            await DbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}