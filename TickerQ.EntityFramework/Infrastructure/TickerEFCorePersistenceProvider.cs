using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.EntityFrameworkCore.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models.Ticker;

namespace TickerQ.EntityFrameworkCore.Infrastructure
{
    internal class TickerEFCorePersistenceProvider<TDbContext, TTimeTicker, TCronTicker> : ITickerPersistenceProvider<TTimeTicker, TCronTicker>
        where TDbContext : DbContext
        where TTimeTicker : TimeTicker, new()
        where TCronTicker : CronTicker, new()
    {
        private readonly TDbContext _dbContext;

        public TickerEFCorePersistenceProvider(TDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        #region Time Ticker Operations

        public async Task<TTimeTicker> GetTimeTickerById(Guid id, CancellationToken cancellationToken = default)
        {
            var timeTickerContext = GetDbSet<TimeTickerEntity>();

            var timeTicker = await timeTickerContext
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
                .ConfigureAwait(false);

            return timeTicker?.ToTimeTicker<TTimeTicker>();
        }

        public async Task<TTimeTicker[]> GetTimeTickersByIds(Guid[] ids, CancellationToken cancellationToken = default)
        {
            var timeTickerContext = GetDbSet<TimeTickerEntity>();

            var timeTickers = await timeTickerContext
                .AsNoTracking()
                .Where(x => ids.Contains(x.Id))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return timeTickers.Select(x => x.ToTimeTicker<TTimeTicker>()).ToArray();
        }

        public async Task<TTimeTicker[]> GetNextTimeTickers(string lockHolder, DateTime roundedMinDate, CancellationToken cancellationToken = default)
        {
            var timeTickerContext = GetDbSet<TimeTickerEntity>();

            var timeTickers = await timeTickerContext
                .AsNoTracking()
                .Where(x =>
                    ((x.LockHolder == null && x.Status == TickerStatus.Idle) ||
                     (x.LockHolder == lockHolder && x.Status == TickerStatus.Queued)) &&
                    x.ExecutionTime >= roundedMinDate &&
                    x.ExecutionTime < roundedMinDate.AddSeconds(1))
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);

            return timeTickers.Select(x => x.ToTimeTicker<TTimeTicker>()).ToArray();
        }

        public async Task<TTimeTicker[]> GetLockedTimeTickers(string lockHolder, TickerStatus[] tickerStatuses, CancellationToken cancellationToken = default)
        {
            var timeTickerContext = GetDbSet<TimeTickerEntity>();

            var timeTickers = await timeTickerContext
                .AsNoTracking()
                .Where(x => tickerStatuses.Contains(x.Status))
                .Where(x => x.LockHolder == lockHolder)
                .ToArrayAsync(cancellationToken);

            return timeTickers.Select(x => x.ToTimeTicker<TTimeTicker>()).ToArray();
        }

        public async Task<TTimeTicker[]> GetTimedOutTimeTickers(DateTime now, CancellationToken cancellationToken = default)
        {
            var timeTickerContext = GetDbSet<TimeTickerEntity>();

            var timeTickers = await timeTickerContext
                .AsNoTracking()
                .Where(x =>
                    (x.Status == TickerStatus.Idle && x.ExecutionTime.AddSeconds(1) < now) ||
                    (x.Status == TickerStatus.Queued && x.ExecutionTime.AddSeconds(3) < now))
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);

            return timeTickers.Select(x => x.ToTimeTicker<TTimeTicker>()).ToArray();
        }

        public async Task InsertTimeTickers(IEnumerable<TTimeTicker> tickers, CancellationToken cancellationToken = default)
        {
            var timeTickerContext = GetDbSet<TimeTickerEntity>();
            await timeTickerContext.AddRangeAsync(tickers.Select(x => x.ToTimeTickerEntity()));
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        public async Task UpdateTimeTickers(IEnumerable<TTimeTicker> tickers, CancellationToken cancellationToken = default)
        {
            var timeTickerContext = GetDbSet<TimeTickerEntity>();
            timeTickerContext.UpdateRange(tickers.Select(x => x.ToTimeTickerEntity()));
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        public async Task RemoveTimeTickers(IEnumerable<TTimeTicker> tickers, CancellationToken cancellationToken = default)
        {
            var timeTickerContext = GetDbSet<TimeTickerEntity>();
            timeTickerContext.RemoveRange(tickers.Select(x => x.ToTimeTickerEntity()));
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        public async Task<byte[]> GetTimeTickerRequest(Guid tickerId, CancellationToken cancellationToken = default)
        {
            var timeTickerContext = GetDbSet<TimeTickerEntity>();

            var request = await timeTickerContext
                .AsNoTracking()
                .Where(x => x.Id == tickerId)
                .Select(x => x.Request)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            return request;
        }

        public async Task<DateTime?> GetEarliestTimeTickerTime(DateTime now, TickerStatus[] tickerStatuses, CancellationToken cancellationToken = default)
        {
            var timeTickerContext = GetDbSet<TimeTickerEntity>();

            var next = await timeTickerContext
                .AsNoTracking()
                .Where(x => x.LockHolder == null
                            && tickerStatuses.Contains(x.Status)
                            && x.ExecutionTime > now)
                .OrderBy(x => x.ExecutionTime)
                .Select(x => x.ExecutionTime)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            return next;
        }

        #endregion

        #region Cron Ticker Operations

        public async Task<TCronTicker> GetCronTickerById(Guid id, CancellationToken cancellationToken = default)
        {
            var cronTickerContext = GetDbSet<CronTickerEntity>();

            var cronTicker = await cronTickerContext
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
                .ConfigureAwait(false);

            return cronTicker?.ToCronTicker<TCronTicker>();
        }

        public async Task<TCronTicker[]> GetCronTickersByIds(Guid[] ids, CancellationToken cancellationToken = default)
        {
            var cronTickerContext = GetDbSet<CronTickerEntity>();

            var cronTickers = await cronTickerContext
                .AsNoTracking()
                .Where(x => ids.Contains(x.Id))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return cronTickers.Select(x => x.ToCronTicker<TCronTicker>()).ToArray();
        }

        public async Task<TCronTicker[]> GetNextCronTickers(string[] expressions, CancellationToken cancellationToken = default)
        {
            var cronTickerContext = GetDbSet<CronTickerEntity>();

            var cronTickers = await cronTickerContext
                .AsNoTracking()
                .Where(x => expressions.Contains(x.Expression))
                .ToArrayAsync(cancellationToken);

            return cronTickers.Select(x => x.ToCronTicker<TCronTicker>()).ToArray();
        }

        public async Task<TCronTicker[]> GetAllExistingInitializedCronTickers(CancellationToken cancellationToken = default)
        {
            var cronTickerContext = GetDbSet<CronTickerEntity>();

            var existingCronTickers = await cronTickerContext
                .AsNoTracking()
                .Where(x => !string.IsNullOrEmpty(x.InitIdentifier) && x.InitIdentifier.StartsWith("MemoryTicker_Seed"))
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            return existingCronTickers.Select(x => x.ToCronTicker<TCronTicker>()).ToArray();
        }

        public async Task<string[]> GetAllCronTickerExpressions(CancellationToken cancellationToken = default)
        {
            var cronTickerContext = GetDbSet<CronTickerEntity>();

            var expressions = await cronTickerContext
                .AsNoTracking()
                .Select(x => x.Expression)
                .Distinct()
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);

            return expressions;
        }

        public async Task InsertCronTickers(IEnumerable<TCronTicker> tickers, CancellationToken cancellationToken = default)
        {
            var cronTickerContext = GetDbSet<CronTickerEntity>();
            await cronTickerContext.AddRangeAsync(tickers.Select(x => x.ToCronTickerEntity()));
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        public async Task UpdateCronTickers(IEnumerable<TCronTicker> tickers, CancellationToken cancellationToken = default)
        {
            var cronTickerContext = GetDbSet<CronTickerEntity>();
            cronTickerContext.UpdateRange(tickers.Select(x => x.ToCronTickerEntity()));
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        public async Task RemoveCronTickers(IEnumerable<TCronTicker> tickers, CancellationToken cancellationToken = default)
        {
            var cronTickerContext = GetDbSet<CronTickerEntity>();
            cronTickerContext.RemoveRange(tickers.Select(x => x.ToCronTickerEntity()));
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        #endregion

        #region Cron Ticker Occurrence Operations

        public async Task<CronTickerOccurrence<TCronTicker>> GetCronTickerOccurenceById(Guid id, CancellationToken cancellationToken = default)
        {
            var cronTickerOccurrenceContext = GetDbSet<CronTickerOccurrenceEntity<CronTickerEntity>>();

            var cronTickerOccurrence = await cronTickerOccurrenceContext
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
                .ConfigureAwait(false);

            return cronTickerOccurrence.ToCronTickerOccurrence<CronTickerOccurrence<TCronTicker>, TCronTicker>();
        }

        public async Task<CronTickerOccurrence<TCronTicker>[]> GetCronTickerOccurencesByIds(Guid[] ids, CancellationToken cancellationToken = default)
        {
            var cronTickerOccurrenceContext = GetDbSet<CronTickerOccurrenceEntity<CronTickerEntity>>();

            var cronTickerOccurrences = await cronTickerOccurrenceContext
                .AsNoTracking()
                .Where(x => ids.Contains(x.Id))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return cronTickerOccurrences.Select(x => x.ToCronTickerOccurrence<CronTickerOccurrence<TCronTicker>, TCronTicker>()).ToArray();
        }

        public async Task<CronTickerOccurrence<TCronTicker>[]> GetNextCronTickerOccurences(string lockHolder, Guid[] cronTickerIds, CancellationToken cancellationToken = default)
        {
            var cronTickerOccurrenceContext = GetDbSet<CronTickerOccurrenceEntity<CronTickerEntity>>();

            var occurrenceList = await cronTickerOccurrenceContext
                .AsNoTracking()
                .Where(x =>
                    cronTickerIds.Contains(x.CronTickerId) &&
                    ((x.LockHolder == null && x.Status == TickerStatus.Idle) ||
                     (x.LockHolder == lockHolder && x.Status == TickerStatus.Queued)))
                .ToListAsync(cancellationToken);

            return occurrenceList.Select(x => x.ToCronTickerOccurrence<CronTickerOccurrence<TCronTicker>, TCronTicker>()).ToArray();
        }

        public async Task<CronTickerOccurrence<TCronTicker>[]> GetLockedCronTickerOccurences(string lockHolder, TickerStatus[] tickerStatuses, CancellationToken cancellationToken = default)
        {
            var cronTickerOccurrenceContext = GetDbSet<CronTickerOccurrenceEntity<CronTickerEntity>>();

            var cronTickerOccurrences = await cronTickerOccurrenceContext
                .AsNoTracking()
                .Where(x => tickerStatuses.Contains(x.Status))
                .Where(x => x.LockHolder == lockHolder)
                .ToArrayAsync(cancellationToken);

            return cronTickerOccurrences.Select(x => x.ToCronTickerOccurrence<CronTickerOccurrence<TCronTicker>, TCronTicker>()).ToArray();
        }

        public async Task<CronTickerOccurrence<TCronTicker>[]> GetTimedOutCronTickerOccurrences(DateTime now, CancellationToken cancellationToken = default)
        {
            var cronTickerOccurrenceContext = GetDbSet<CronTickerOccurrenceEntity<CronTickerEntity>>();

            var cronTickerOccurrences = await cronTickerOccurrenceContext
                .AsNoTracking()
                .Include(x => x.CronTicker)
                .Where(x => !x.ExecutedAt.HasValue && x.Status != TickerStatus.Inprogress &&
                            x.Status != TickerStatus.Cancelled)
                .Where(x => x.ExecutionTime < now.AddSeconds(1))
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);

            return cronTickerOccurrences.Select(x => x.ToCronTickerOccurrence<CronTickerOccurrence<TCronTicker>, TCronTicker>()).ToArray();
        }

        public async Task<CronTickerOccurrence<TCronTicker>[]> GetQueuedNextCronOccurrences(Guid tickerId, CancellationToken cancellationToken = default)
        {
            var cronTickerOccurrenceContext = GetDbSet<CronTickerOccurrenceEntity<CronTickerEntity>>();

            var nextCronOccurrences = await cronTickerOccurrenceContext
                .AsNoTracking()
                .Where(x => x.CronTickerId == tickerId)
                .Where(x => x.Status == TickerStatus.Queued)
                .ToArrayAsync(cancellationToken).ConfigureAwait(false);

            return nextCronOccurrences.Select(x => x.ToCronTickerOccurrence<CronTickerOccurrence<TCronTicker>, TCronTicker>()).ToArray();
        }

        public async Task<byte[]> GetCronTickerRequestViaOccurence(Guid tickerId, CancellationToken cancellationToken = default)
        {
            var cronTickerOccurrenceContext = GetDbSet<CronTickerOccurrenceEntity<CronTickerEntity>>();

            var request = await cronTickerOccurrenceContext
                .AsNoTracking()
                .Where(x => x.Id == tickerId)
                .Select(x => x.CronTicker.Request)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            return request;
        }

        public async Task InsertCronTickerOccurences(IEnumerable<CronTickerOccurrence<TCronTicker>> cronTickerOccurrences, CancellationToken cancellationToken = default)
        {
            var cronTickerOccurrenceContext = GetDbSet<CronTickerOccurrenceEntity<CronTickerEntity>>();
            await cronTickerOccurrenceContext.AddRangeAsync(cronTickerOccurrences.Select(x => x.ToCronTickerOccurrenceEntity<TCronTicker, CronTickerOccurrence<TCronTicker>>()));
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        public async Task UpdateCronTickerOccurences(IEnumerable<CronTickerOccurrence<TCronTicker>> cronTickerOccurrences, CancellationToken cancellationToken = default)
        {
            var cronTickerOccurrenceContext = GetDbSet<CronTickerOccurrenceEntity<CronTickerEntity>>();
            cronTickerOccurrenceContext.UpdateRange(cronTickerOccurrences.Select(x => x.ToCronTickerOccurrenceEntity<TCronTicker, CronTickerOccurrence<TCronTicker>>()));
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        public async Task RemoveCronTickerOccurences(IEnumerable<CronTickerOccurrence<TCronTicker>> cronTickerOccurrences, CancellationToken cancellationToken = default)
        {
            var cronTickerOccurrenceContext = GetDbSet<CronTickerOccurrenceEntity<CronTickerEntity>>();
            cronTickerOccurrenceContext.RemoveRange(cronTickerOccurrences.Select(x => x.ToCronTickerOccurrenceEntity<TCronTicker, CronTickerOccurrence<TCronTicker>>()));
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        #endregion

        #region Helpers

        private DbSet<T> GetDbSet<T>() where T : class => _dbContext.Set<T>();

        #endregion
    }
}
