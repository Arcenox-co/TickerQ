using Microsoft.EntityFrameworkCore;
using NCrontab;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.EntityFrameworkCore.Entities;
using TickerQ.EntityFrameworkCore.Src;
using TickerQ.Utilities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;

namespace TickerQ.EntityFrameworkCore
{
    internal abstract class InternalTickerManager<TDbContext, TTimeTicker, TCronTicker> : IInternalTickerManager where TDbContext : DbContext where TTimeTicker : TimeTicker where TCronTicker : CronTicker
    {
        protected readonly TDbContext DbContext;
        protected readonly ITickerCollection TickerCollection;
        protected readonly ITickerHost TickerHost;
        protected readonly IClock Clock;
        protected readonly TickerOptionsBuilder TickerOptionsBuilder;
        private string LockHolder => TickerOptionsBuilder.InstanceIdentifier ?? Environment.MachineName;
        public DbSet<TTimeTicker> TimeTickers => DbContext.Set<TTimeTicker>();
        public DbSet<TCronTicker> CronTickers => DbContext.Set<TCronTicker>();
        public DbSet<CronTickerOccurrence<TCronTicker>> CronTickerOccurrences => DbContext.Set<CronTickerOccurrence<TCronTicker>>();

        protected InternalTickerManager(TDbContext dbContext, ITickerCollection tickerCollection, ITickerHost tickerHost, IClock clock, TickerOptionsBuilder tickerOptionsBuilder)
        {
            DbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            TickerCollection = tickerCollection ?? throw new ArgumentNullException(nameof(tickerCollection));
            TickerHost = tickerHost ?? throw new ArgumentNullException(nameof(tickerHost));
            Clock = clock ?? throw new ArgumentNullException(nameof(clock));
            TickerOptionsBuilder = tickerOptionsBuilder ?? throw new ArgumentNullException(nameof(tickerOptionsBuilder));
        }

        public async Task<(TimeSpan TimeRemaining, (string, Guid, TickerType)[] Functions)> GetNextTickers(CancellationToken cancellationToken = default)
        {
            var minCronTicker = await GetMinCronTicker(cancellationToken).ConfigureAwait(false);
            var minTimeTicker = await GetMinTimeTicker(cancellationToken).ConfigureAwait(false);

            var minTimeRemaining = GetMinTimeRemaining(minCronTicker, minTimeTicker);

            if (minTimeRemaining == Timeout.InfiniteTimeSpan)
                return (Timeout.InfiniteTimeSpan, Array.Empty<(string, Guid, TickerType)>());

            var nextTickers = await GetNextTickersAsync(minCronTicker, minTimeTicker).ConfigureAwait(false);

            return (minTimeRemaining, nextTickers);
        }

        private TimeSpan GetMinTimeRemaining(IGrouping<DateTime, string> minCronTicker, DateTime minTimeTicker)
        {
            var minTimeRemaining = minCronTicker != default && minTimeTicker != default
                ? ((minCronTicker.Key < minTimeTicker) ? minCronTicker.Key : minTimeTicker) - Clock.Now
                : (minCronTicker != default) ? minCronTicker.Key - Clock.Now
                : (minTimeTicker != default) ? minTimeTicker - Clock.Now
                : Timeout.InfiniteTimeSpan;

            return minTimeRemaining;
        }

        private async Task<(string, Guid, TickerType)[]> GetNextTickersAsync(IGrouping<DateTime, string> minCronTicker, DateTime minTimeTicker, CancellationToken cancellationToken = default)
        {
            if (minCronTicker != default && minTimeTicker != default && Math.Abs((minTimeTicker - minCronTicker.Key).TotalSeconds) <= 5)
            {
                var nextCronTickers = await GetNextCronTickersAsync(minCronTicker.ToArray(), cancellationToken).ConfigureAwait(false);
                var nextTimeTickers = await GetNextTimeTickersAsync(minTimeTicker, cancellationToken).ConfigureAwait(false);

                return nextCronTickers.Union(nextTimeTickers).ToArray();
            }
            else if (minCronTicker?.Key == default)
            {
                return await GetNextTimeTickersAsync(minTimeTicker, cancellationToken);
            }
            else if (minTimeTicker == default)
            {
                return await GetNextCronTickersAsync(minCronTicker.ToArray(), cancellationToken).ConfigureAwait(false);
            }
            else
            {
                return minTimeTicker < minCronTicker.Key
                    ? await GetNextTimeTickersAsync(minTimeTicker, cancellationToken).ConfigureAwait(false)
                    : await GetNextCronTickersAsync(minCronTicker.ToArray(), cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task<(string, Guid, TickerType)[]> GetNextTimeTickersAsync(DateTimeOffset minDate, CancellationToken cancellationToken = default)
        {
            TimeSpan tolerance = TimeSpan.FromSeconds(5);

            var timeTickers = await TimeTickers
                .Where(x => x.LockHolder == null && x.Status == TickerStatus.Idle)
                .Where(item => item.ExecutionTime >= minDate && item.ExecutionTime <= minDate + tolerance)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (var timeTicker in timeTickers)
            {
                timeTicker.Status = TickerStatus.Queued;
                timeTicker.LockHolder = LockHolder;
                timeTicker.LockedAt = Clock.OffsetNow;
            }

            DbContext.UpdateRange(timeTickers);

            await DbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return timeTickers
                .Select(x => (x.Function, x.Id, TickerType.Timer))
                .ToArray();
        }

        private async Task<(string, Guid, TickerType)[]> GetNextCronTickersAsync(string[] expressions, CancellationToken cancellationToken = default)
        {
            var now = Clock.Now;

            var nextTickerOccurrences = new List<(string, Guid)>();

            var cronTickers = await CronTickers
                .AsNoTracking()
                .Where(cronTicker => expressions.Contains(cronTicker.Expression))
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);

            var cronTickerOccurrences = await CronTickerOccurrences
                .Where(x => cronTickers.Contains(x.CronTicker))
                .ToListAsync(cancellationToken);

            foreach (var cronTicker in cronTickers)
            {
                var nextOccurrence = CrontabSchedule
                    .Parse(cronTicker.Expression)
                    .GetNextOccurrence(now);

                var existOccurrence = cronTickerOccurrences.FirstOrDefault(x => (x.CronTickerId == cronTicker.Id && x.ExecutionTime.DateTime == nextOccurrence));

                if (existOccurrence == default)
                {
                    var newOccurrence = new CronTickerOccurrence<TCronTicker>
                    {
                        Id = new Guid(),
                        Status = TickerStatus.Queued,
                        ExecutionTime = new DateTimeOffset(nextOccurrence, Clock.TimeZone.GetUtcOffset(DateTimeOffset.UtcNow)),
                        LockedAt = Clock.OffsetNow,
                        LockHolder = LockHolder,
                        CronTickerId = cronTicker.Id
                    };

                    CronTickerOccurrences.Add(newOccurrence);
                    nextTickerOccurrences.Add((cronTicker.Function, newOccurrence.Id));
                }
                else
                {
                    existOccurrence.Status = TickerStatus.Queued;
                    existOccurrence.LockHolder = LockHolder;
                    existOccurrence.LockedAt = Clock.OffsetNow;

                    CronTickerOccurrences.Update(existOccurrence);
                    nextTickerOccurrences.Add((cronTicker.Function, existOccurrence.Id));
                }
            }

            await DbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return nextTickerOccurrences
               .Select(x => (x.Item1, x.Item2, TickerType.CronExpression))
               .ToArray();
        }

        private async Task<DateTime> GetMinTimeTicker(CancellationToken cancellationToken = default)
        {
            var now = Clock.OffsetNow;

            return (await TimeTickers
               .AsNoTracking()
               .Where(x => x.LockHolder == null && x.Status == TickerStatus.Idle)
               .Select(item => item.ExecutionTime)
               .Where(executionTime => DateTimeOffset.Compare(executionTime, now) > 0)
               .DefaultIfEmpty()
               .MinAsync(cancellationToken)
               .ConfigureAwait(false))
               .DateTime;
        }

        private async Task<IGrouping<DateTime, string>> GetMinCronTicker(CancellationToken cancellationToken = default)
        {
            var now = Clock.Now;

            return (await CronTickers
                .AsNoTracking()
                .Select(x => x.Expression)
                .Distinct()
                .ToListAsync(cancellationToken).ConfigureAwait(false))
                    .GroupBy(expression => CrontabSchedule.Parse(expression).GetNextOccurrence(now))
                    .OrderBy(group => group.Key)
                    .FirstOrDefault();
        }

        public async Task SetTickersInprogress(IEnumerable<(Guid TickerId, TickerType type)> resources, CancellationToken cancellationToken = default)
        {
            if (resources.Where(x => x.type == TickerType.CronExpression).Select(x => x.TickerId).ToArray() is Guid[] cronOccurrenceIds && cronOccurrenceIds.Length != 0)
            {
                var cronOccurrences = await CronTickerOccurrences
                    .Where(x => cronOccurrenceIds.Contains(x.Id))
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                foreach (var cronOccurrence in cronOccurrences)
                {
                    cronOccurrence.Status = TickerStatus.Inprogress;
                }

                DbContext.UpdateRange(cronOccurrences);
            }

            if (resources.Where(x => x.type == TickerType.CronExpression).Select(x => x.TickerId).ToArray() is Guid[] timeOccurrenceIds && timeOccurrenceIds.Length != 0)
            {
                var timeTickers = await TimeTickers
                    .Where(x => timeOccurrenceIds.Contains(x.Id))
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                foreach (var timeTicker in timeTickers)
                {
                    timeTicker.Status = TickerStatus.Inprogress;
                }

                DbContext.UpdateRange(timeTickers);
            }

            await DbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task ReleaseAcquiredResources(IEnumerable<(Guid TickerId, TickerType type)> resources, CancellationToken cancellationToken = default)
        {
            if (resources.Where(x => x.type == TickerType.CronExpression).Select(x => x.TickerId).ToArray() is Guid[] cronOccurrenceIds && cronOccurrenceIds.Length != 0)
            {
                var cronOccurrences = await CronTickerOccurrences
                    .Where(x => cronOccurrenceIds.Contains(x.Id))
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                foreach (var cronOccurrence in cronOccurrences)
                {
                    cronOccurrence.LockHolder = string.Empty;
                    cronOccurrence.Status = TickerStatus.Idle;
                    cronOccurrence.LockedAt = default;
                }

                DbContext.UpdateRange(cronOccurrences);
            }

            if (resources.Where(x => x.type == TickerType.CronExpression).Select(x => x.TickerId).ToArray() is Guid[] timeOccurrenceIds && timeOccurrenceIds.Length != 0)
            {
                var timeTickers = await TimeTickers
                    .Where(x => timeOccurrenceIds.Contains(x.Id))
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                foreach (var timeTicker in timeTickers)
                {
                    timeTicker.LockHolder = default;
                    timeTicker.Status = TickerStatus.Idle;
                    timeTicker.LockedAt = default;
                }

                DbContext.UpdateRange(timeTickers);
            }

            await DbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task SetTickerStatus(Guid tickerId, TickerType tickerType, TickerStatus tickerStatus, CancellationToken cancellationToken = default)
        {
            if (tickerType == TickerType.CronExpression)
            {
                var cronTicker = await CronTickerOccurrences
                     .Where(x => x.Id == tickerId)
                     .FirstOrDefaultAsync()
                     .ConfigureAwait(false);

                cronTicker.Status = tickerStatus;
                cronTicker.ExcecutedAt = Clock.OffsetNow;

                DbContext.Update(cronTicker);
            }
            else
            {
                var timeTicker = await TimeTickers
                    .Where(x => x.Id == tickerId)
                    .FirstOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);

                timeTicker.Status = tickerStatus;
                timeTicker.ExcecutedAt = Clock.OffsetNow;

                DbContext.Update(timeTicker);
            }

            await DbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task<T> GetRequest<T>(Guid tickerId, TickerType type, CancellationToken cancellationToken = default)
        {
            if (type == TickerType.CronExpression)
            {
                var cronTickerRequest = await CronTickerOccurrences
                    .Include(x => x.CronTicker)
                    .Where(x => x.Id == tickerId)
                    .Select(x => x.CronTicker.Request)
                    .FirstOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);

                return TickerHelper.ReadTickerRequest<T>(cronTickerRequest);
            }
            else
            {
                var timeTickerRequest = await TimeTickers
                    .Where(x => x.Id == tickerId)
                    .Select(x => x.Request)
                    .FirstOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);

                return TickerHelper.ReadTickerRequest<T>(timeTickerRequest);
            }
        }

        public async Task<(string, Guid, TickerType)[]> GetTimeoutedFunctions(CancellationToken cancellationToken = default)
        {
            IEnumerable<(string, Guid, TickerType)> timeTickerFunctionDetails = await GetTimeoutedTimeTickers(cancellationToken).ConfigureAwait(false);
            IEnumerable<(string, Guid, TickerType)> cronTickerFunctionDetails = await GetTimeoutedCronTickerOccurrences(cancellationToken).ConfigureAwait(false);

            if(!timeTickerFunctionDetails.Any() && !cronTickerFunctionDetails.Any())
                return Array.Empty<(string, Guid, TickerType)>();

            await DbContext.SaveChangesAsync(cancellationToken);

            return timeTickerFunctionDetails.Concat(cronTickerFunctionDetails).ToArray();
        }

        private async Task<IEnumerable<(string, Guid, TickerType)>> GetTimeoutedCronTickerOccurrences(CancellationToken cancellationToken)
        {
            var cronTickerOccurrences = await CronTickerOccurrences
                            .Where(x => !x.ExcecutedAt.HasValue && x.Status != TickerStatus.Inprogress)
                            .Where(x => x.ExecutionTime.AddMinutes(TickerOptionsBuilder.TimeOutChecker.TotalMinutes) <= Clock.OffsetNow)
                            .ToArrayAsync(cancellationToken)
                            .ConfigureAwait(false);

            if (!cronTickerOccurrences.Any())
                return Enumerable.Empty<(string, Guid, TickerType)>();

            foreach (var cronTickeOccurrence in cronTickerOccurrences)
            {
                cronTickeOccurrence.Status = TickerStatus.Inprogress;
                cronTickeOccurrence.LockHolder = LockHolder;
            }

            DbContext.UpdateRange(cronTickerOccurrences);

            var cronTickerOccurennceIds = cronTickerOccurrences
                .Select(x => x.CronTickerId)
                .ToArray();

            var cronTickerFunctions = await CronTickers
                .AsNoTracking()
                .Where(x => cronTickerOccurennceIds.Contains(x.Id))
                .Select(x => new { x.Function, x.Id })
                .ToListAsync(cancellationToken);

            return cronTickerOccurrences.Select(x => (cronTickerFunctions.First(y => y.Id == x.CronTickerId).Function, x.Id, TickerType.CronExpression));
        }

        private async Task<IEnumerable<(string, Guid, TickerType)>> GetTimeoutedTimeTickers(CancellationToken cancellationToken)
        {
            var timeTickers = await TimeTickers
                .Where(x => !x.ExcecutedAt.HasValue && x.Status != TickerStatus.Inprogress)
                .Where(x => x.ExecutionTime.AddMinutes(TickerOptionsBuilder.TimeOutChecker.TotalMinutes) <= Clock.OffsetNow)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);

            if (!timeTickers.Any())
                return Enumerable.Empty<(string, Guid, TickerType)>();

            foreach (var timeTicker in timeTickers)
            {
                timeTicker.Status = TickerStatus.Inprogress;
                timeTicker.LockHolder = LockHolder;
            }

            DbContext.UpdateRange(timeTickers);

            return timeTickers.Select(x => (x.Function, x.Id, TickerType.Timer));
        }
    }
}
