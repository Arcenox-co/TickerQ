using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.EntityFrameworkCore.Entities;
using TickerQ.Utilities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Exceptions;
using TickerQ.Utilities.Extensions;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models.Ticker;

namespace TickerQ.EntityFrameworkCore.Infrastructure
{
    internal class
        TickerEFCorePersistenceProvider<TDbContext, TTimeTicker, TCronTicker> : BasePersistenceProvider<TDbContext>,
        ITickerPersistenceProvider<TTimeTicker,
            TCronTicker>
        where TDbContext : DbContext
        where TTimeTicker : TimeTicker, new()
        where TCronTicker : CronTicker, new()
    {
        private readonly ITickerClock _clock;

        public TickerEFCorePersistenceProvider(TDbContext dbContext, ITickerClock clock) : base(dbContext)
        {
            _clock = clock;
        }

        #region Time Ticker Operations

        public async Task<TTimeTicker> GetTimeTickerById(Guid id, Action<TickerProviderOptions> options = null,
            CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();

            var timeTickerContext = GetDbSet<TimeTickerEntity>();

            var query = optionsValue.Tracking
                ? timeTickerContext
                : timeTickerContext.AsNoTracking();

            var timeTicker = await query
                .FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
                .ConfigureAwait(false);

            return timeTicker?.ToTimeTicker<TTimeTicker>();
        }

        public async Task<TTimeTicker[]> GetTimeTickersByIds(Guid[] ids, Action<TickerProviderOptions> options = null,
            CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();

            var timeTickerContext = GetDbSet<TimeTickerEntity>();

            var query = optionsValue.Tracking
                ? timeTickerContext
                : timeTickerContext.AsNoTracking();

            var timeTickers = await query
                .Where(x => ids.Contains(x.Id))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return timeTickers.Select(x => x.ToTimeTicker<TTimeTicker>()).ToArray();
        }

                public async Task<TTimeTicker[]> GetNextTimeTickers(string lockHolder, DateTime roundedMinDate,
Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();

            // Ultra-fast approach optimized for strict timeout constraints (1-3 seconds)
            // Uses optimistic bulk locking for maximum speed within timeout windows
            return await DbContext.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                var timeTickerContext = GetDbSet<TimeTickerEntity>();
                
                // Single optimized transaction with bulk processing
                using var transaction = DbContext.Database.BeginTransaction();
                
                try
                {
                    // Fast bulk query and update in one operation
                    var availableTickers = await timeTickerContext
                        .Where(x =>
                            ((x.LockHolder == null && x.Status == TickerStatus.Idle) ||
                             (x.LockHolder == lockHolder && x.Status == TickerStatus.Queued)) &&
                            x.ExecutionTime >= roundedMinDate &&
                            x.ExecutionTime < roundedMinDate.AddSeconds(1))
                        .OrderBy(x => x.ExecutionTime)
                        .Take(100)
                        .Include(x => x.ParentJob)
                        .ToListAsync(cancellationToken)
                        .ConfigureAwait(false);

                    if (!availableTickers.Any())
                    {
                        transaction.Rollback();
                        return new TTimeTicker[0];
                    }

                    // Bulk update all tickers at once (fastest approach)
                    var lockTime = _clock.UtcNow;
                    var successfulTickers = new List<TimeTickerEntity>();

                    foreach (var ticker in availableTickers)
                    {
                        // Fast in-memory check and update
                        if ((ticker.LockHolder == null && ticker.Status == TickerStatus.Idle) ||
                            (ticker.LockHolder == lockHolder && ticker.Status == TickerStatus.Queued))
                        {
                            ticker.Status = TickerStatus.Queued;
                            ticker.LockHolder = lockHolder;
                            ticker.LockedAt = lockTime;
                            successfulTickers.Add(ticker);
                        }
                    }

                    if (successfulTickers.Any())
                    {
                        // Single bulk save operation
                        await DbContext.SaveChangesAsync(cancellationToken);
                        transaction.Commit();

                        var result = successfulTickers.Select(x => x.ToTimeTicker<TTimeTicker>()).ToArray();
                        DetachAll<TimeTickerEntity>();
                        return result;
                    }
                    else
                    {
                        transaction.Rollback();
                        return new TTimeTicker[0];
                    }
                }
                catch (DbUpdateConcurrencyException)
                {
                    transaction.Rollback();
                    DetachAll<TimeTickerEntity>();
                    return new TTimeTicker[0];
                }
                catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
                {
                    transaction.Rollback();
                    DetachAll<TimeTickerEntity>();
                    return new TTimeTicker[0];
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            });
        }





        public async Task<TTimeTicker[]> GetLockedTimeTickers(string lockHolder, TickerStatus[] tickerStatuses,
            Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();

            var timeTickerContext = GetDbSet<TimeTickerEntity>();

            var query = optionsValue.Tracking
                ? timeTickerContext
                : timeTickerContext.AsNoTracking();

            var timeTickers = await query
                .Where(x => tickerStatuses.Contains(x.Status))
                .Where(x => x.LockHolder == lockHolder)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);

            return timeTickers.Select(x => x.ToTimeTicker<TTimeTicker>()).ToArray();
        }

        public async Task<TTimeTicker[]> GetTimedOutTimeTickers(DateTime now,
            Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();

            var timeTickerContext = GetDbSet<TimeTickerEntity>();

            var query = optionsValue.Tracking
                ? timeTickerContext
                : timeTickerContext.AsNoTracking();

            var timeTickers = await query
                .Where(x =>
                    (x.Status == TickerStatus.Idle && x.ExecutionTime.AddSeconds(1) < now) ||
                    (x.Status == TickerStatus.Queued && x.ExecutionTime.AddSeconds(3) < now))
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);

            return timeTickers.Select(x => x.ToTimeTicker<TTimeTicker>()).ToArray();
        }

        public async Task<TTimeTicker[]> GetAllTimeTickers(Action<TickerProviderOptions> options = null,
            CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();

            var timeTickerContext = GetDbSet<TimeTickerEntity>();

            var query = optionsValue.Tracking
                ? timeTickerContext
                : timeTickerContext.AsNoTracking();

            var timeTickers = await query
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return timeTickers.Select(x => x.ToTimeTicker<TTimeTicker>()).ToArray();
        }

        public async Task<TTimeTicker[]> GetAllLockedTimeTickers(Action<TickerProviderOptions> options = null,
            CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();

            var timeTickerContext = GetDbSet<TimeTickerEntity>();

            var query = optionsValue.Tracking
                ? timeTickerContext
                : timeTickerContext.AsNoTracking();

            var timeTickers = await query
                .Where(x => x.LockHolder != null)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return timeTickers.Select(x => x.ToTimeTicker<TTimeTicker>()).ToArray();
        }

        public async Task<TTimeTicker[]> GetTimeTickersWithin(DateTime startDate, DateTime endDate,
            Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();

            var timeTickerContext = GetDbSet<TimeTickerEntity>();

            var query = optionsValue.Tracking
                ? timeTickerContext
                : timeTickerContext.AsNoTracking();

            var timeTickers = await query
                .Where(x => x.ExecutionTime.Date >= startDate && x.ExecutionTime.Date <= endDate)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return timeTickers.Select(x => x.ToTimeTicker<TTimeTicker>()).ToArray();
        }

        public async Task InsertTimeTickers(IEnumerable<TTimeTicker> tickers,
            Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var timeTickerContext = GetDbSet<TimeTickerEntity>();
            await timeTickerContext.AddRangeAsync(tickers.Select(x => x.ToTimeTickerEntity()), cancellationToken);

            await SaveAndDetachAsync<TimeTickerEntity>(cancellationToken).ConfigureAwait(false);
        }

        public async Task UpdateTimeTickers(IEnumerable<TTimeTicker> tickers,
            Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var entities = tickers.Select(x => x.ToTimeTickerEntity());

            UpsertRange(entities, x => x.Id);

            await SaveAndDetachAsync<TimeTickerEntity>(cancellationToken).ConfigureAwait(false);
        }

        public async Task RemoveTimeTickers(IEnumerable<TTimeTicker> tickers,
            Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var entities = tickers.Select(x => x.ToTimeTickerEntity());

            DeleteRange(entities, x => x.Id);

            await SaveAndDetachAsync<TimeTickerEntity>(cancellationToken).ConfigureAwait(false);
        }

        public async Task<TTimeTicker[]> GetChildTickersByParentId(Guid parentTickerId,
            CancellationToken cancellationToken = new CancellationToken())
        {
            var timeTickerContext = GetDbSet<TimeTickerEntity>();

            var childTickers = await timeTickerContext
                .AsNoTracking()
                .Where(x => x.BatchParent == parentTickerId)
                .ToListAsync(cancellationToken: cancellationToken);

            return childTickers.Select(x => x.ToTimeTicker<TTimeTicker>()).ToArray();
        }

        public async Task<byte[]> GetTimeTickerRequest(Guid tickerId, Action<TickerProviderOptions> options = null,
            CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();

            var timeTickerContext = GetDbSet<TimeTickerEntity>();

            var query = optionsValue.Tracking
                ? timeTickerContext
                : timeTickerContext.AsNoTracking();

            var request = await query
                .Where(x => x.Id == tickerId)
                .Select(x => x.Request)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            return request;
        }

        public async Task<DateTime?> GetEarliestTimeTickerTime(DateTime now, TickerStatus[] tickerStatuses,
            Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();

            var timeTickerContext = GetDbSet<TimeTickerEntity>();

            var query = optionsValue.Tracking
                ? timeTickerContext
                : timeTickerContext.AsNoTracking();

            var next = await query
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

        public async Task<TCronTicker> GetCronTickerById(Guid id, Action<TickerProviderOptions> options = null,
            CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();

            var cronTickerContext = GetDbSet<CronTickerEntity>();

            var query = optionsValue.Tracking
                ? cronTickerContext
                : cronTickerContext.AsNoTracking();

            var cronTicker = await query
                .FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
                .ConfigureAwait(false);

            return cronTicker?.ToCronTicker<TCronTicker>();
        }

        public async Task<TCronTicker[]> GetCronTickersByIds(Guid[] ids, Action<TickerProviderOptions> options = null,
            CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();

            var cronTickerContext = GetDbSet<CronTickerEntity>();

            var query = optionsValue.Tracking
                ? cronTickerContext
                : cronTickerContext.AsNoTracking();

            var cronTickers = await query
                .Where(x => ids.Contains(x.Id))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return cronTickers.Select(x => x.ToCronTicker<TCronTicker>()).ToArray();
        }

        public async Task<TCronTicker[]> GetNextCronTickers(string[] expressions,
            Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();

            var cronTickerContext = GetDbSet<CronTickerEntity>();

            var query = optionsValue.Tracking
                ? cronTickerContext
                : cronTickerContext.AsNoTracking();

            var cronTickers = await query
                .Where(x => expressions.Contains(x.Expression))
                .ToArrayAsync(cancellationToken);

            return cronTickers.Select(x => x.ToCronTicker<TCronTicker>()).ToArray();
        }

        public async Task<TCronTicker[]> GetAllExistingInitializedCronTickers(
            Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();

            var cronTickerContext = GetDbSet<CronTickerEntity>();

            var query = optionsValue.Tracking
                ? cronTickerContext
                : cronTickerContext.AsNoTracking();

            var existingCronTickers = await query
                .Where(x => !string.IsNullOrEmpty(x.InitIdentifier) &&
                            x.InitIdentifier.StartsWith("MemoryTicker_Seed"))
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            return existingCronTickers.Select(x => x.ToCronTicker<TCronTicker>()).ToArray();
        }

        public async Task<TCronTicker[]> GetAllCronTickers(Action<TickerProviderOptions> options = null,
            CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();

            var cronTickerContext = GetDbSet<CronTickerEntity>();

            var query = optionsValue.Tracking
                ? cronTickerContext
                : cronTickerContext.AsNoTracking();

            var cronTickers = await query
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return cronTickers.Select(x => x.ToCronTicker<TCronTicker>()).ToArray();
        }

        public async Task<Tuple<Guid, string>[]> GetAllCronTickerExpressions(
            Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();

            var cronTickerContext = GetDbSet<CronTickerEntity>();

            var query = optionsValue.Tracking
                ? cronTickerContext
                : cronTickerContext.AsNoTracking();

            var expressions = await query
                .Select(x => Tuple.Create(x.Id, x.Expression))
                .Distinct()
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);

            return expressions;
        }

        public async Task InsertCronTickers(IEnumerable<TCronTicker> tickers,
            Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var cronTickerContext = GetDbSet<CronTickerEntity>();

            var entities = tickers.Select(x => x.ToCronTickerEntity());

            await cronTickerContext.AddRangeAsync(entities, cancellationToken).ConfigureAwait(false);

            await SaveAndDetachAsync<CronTickerEntity>(cancellationToken).ConfigureAwait(false);
        }

        public async Task UpdateCronTickers(IEnumerable<TCronTicker> tickers,
            Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var entities = tickers.Select(x => x.ToCronTickerEntity());

            UpsertRange(entities, x => x.Id);

            await SaveAndDetachAsync<CronTickerEntity>(cancellationToken).ConfigureAwait(false);
        }

        public async Task RemoveCronTickers(IEnumerable<TCronTicker> tickers,
            Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var context = GetDbSet<CronTickerEntity>();

            var tickerEntities = tickers.Select(x => x.ToCronTickerEntity());

            context.RemoveRange(tickerEntities);

            await SaveAndDetachAsync<CronTickerEntity>(cancellationToken).ConfigureAwait(false);
        }

        #endregion

        #region Cron Ticker Occurrence Operations

        public async Task<CronTickerOccurrence<TCronTicker>> GetCronTickerOccurrenceById(Guid id,
            Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();

            var cronTickerOccurrenceContext = GetDbSet<CronTickerOccurrenceEntity<CronTickerEntity>>();

            var query = optionsValue.Tracking
                ? cronTickerOccurrenceContext
                : cronTickerOccurrenceContext.AsNoTracking();

            var cronTickerOccurrence = await query
                .FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
                .ConfigureAwait(false);

            return cronTickerOccurrence.ToCronTickerOccurrence<CronTickerOccurrence<TCronTicker>, TCronTicker>();
        }

        public async Task<CronTickerOccurrence<TCronTicker>[]> GetCronTickerOccurrencesByIds(Guid[] ids,
            Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();

            var cronTickerOccurrenceContext = GetDbSet<CronTickerOccurrenceEntity<CronTickerEntity>>();

            var query = optionsValue.Tracking
                ? cronTickerOccurrenceContext
                : cronTickerOccurrenceContext.AsNoTracking();

            var cronTickerOccurrences = await query
                .Where(x => ids.Contains(x.Id))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return cronTickerOccurrences
                .Select(x => x.ToCronTickerOccurrence<CronTickerOccurrence<TCronTicker>, TCronTicker>()).ToArray();
        }

        public async Task<CronTickerOccurrence<TCronTicker>[]> GetCronTickerOccurrencesByCronTickerIds(Guid[] ids,
            int? takeLimit, Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();

            var now = _clock.UtcNow;

            var cronTickerOccurrenceContext = GetDbSet<CronTickerOccurrenceEntity<CronTickerEntity>>();

            var query = optionsValue.Tracking
                ? cronTickerOccurrenceContext
                : cronTickerOccurrenceContext.AsNoTracking();

            var cronTickerOccurrences = await query
                .Where(x => ids.Contains(x.CronTickerId))
                .Where(x => x.ExecutionTime >= now)
                .OrderBy(x => x.ExecutionTime)
                .ToListAsync(cancellationToken);

            cronTickerOccurrences = cronTickerOccurrences.DistinctBy(x => x.CronTickerId)
                .Take(takeLimit ?? 1)
                .ToList();

            return cronTickerOccurrences
                .Select(x => x.ToCronTickerOccurrence<CronTickerOccurrence<TCronTicker>, TCronTicker>()).ToArray();
        }

        public async Task<CronTickerOccurrence<TCronTicker>[]> GetNextCronTickerOccurrences(string lockHolder,
            Guid[] cronTickerIds, Action<TickerProviderOptions> options = null,
            CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();

            // Use execution strategy to handle retry logic and transaction management
            return await DbContext.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                using var transaction = DbContext.Database.BeginTransaction();
                
                try
                {
                    var cronTickerOccurrenceContext = GetDbSet<CronTickerOccurrenceEntity<CronTickerEntity>>();

                    // First, get the IDs of available occurrences with a simple query
                    var availableOccurrenceIds = await cronTickerOccurrenceContext
                        .Where(x =>
                            cronTickerIds.Contains(x.CronTickerId) &&
                            ((x.LockHolder == null && x.Status == TickerStatus.Idle) ||
                             (x.LockHolder == lockHolder && x.Status == TickerStatus.Queued)))
                        .Select(x => x.Id)
                        .ToArrayAsync(cancellationToken)
                        .ConfigureAwait(false);

                    if (availableOccurrenceIds.Length == 0)
                    {
                        transaction.Commit();
                        return new CronTickerOccurrence<TCronTicker>[0];
                    }

                    // Now fetch the full entities
                    var occurrenceList = await cronTickerOccurrenceContext
                        .Where(x => availableOccurrenceIds.Contains(x.Id))
                        .ToArrayAsync(cancellationToken)
                        .ConfigureAwait(false);

                    // Immediately update the lock holder to prevent race conditions
                    foreach (var occurrence in occurrenceList)
                    {
                        occurrence.Status = TickerStatus.Queued;
                        occurrence.LockHolder = lockHolder;
                        occurrence.LockedAt = _clock.UtcNow;
                    }

                    // Save changes within the transaction
                    await DbContext.SaveChangesAsync(cancellationToken);
                    
                    // Commit the transaction
                    transaction.Commit();

                    return occurrenceList
                        .Select(x => x.ToCronTickerOccurrence<CronTickerOccurrence<TCronTicker>, TCronTicker>()).ToArray();
                }
                catch
                {
                    // Rollback on any error
                    transaction.Rollback();
                    throw;
                }
            });
        }

        public async Task<CronTickerOccurrence<TCronTicker>[]> GetLockedCronTickerOccurrences(string lockHolder,
            TickerStatus[] tickerStatuses, Action<TickerProviderOptions> options = null,
            CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();

            var cronTickerOccurrenceContext = GetDbSet<CronTickerOccurrenceEntity<CronTickerEntity>>();

            var query = optionsValue.Tracking
                ? cronTickerOccurrenceContext
                : cronTickerOccurrenceContext.AsNoTracking();

            var cronTickerOccurrences = await query
                .Where(x => tickerStatuses.Contains(x.Status))
                .Where(x => x.LockHolder == lockHolder)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);

            return cronTickerOccurrences
                .Select(x => x.ToCronTickerOccurrence<CronTickerOccurrence<TCronTicker>, TCronTicker>()).ToArray();
        }

        public async Task<CronTickerOccurrence<TCronTicker>[]> GetTimedOutCronTickerOccurrences(DateTime now,
            Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();

            var cronTickerOccurrenceContext = GetDbSet<CronTickerOccurrenceEntity<CronTickerEntity>>();

            var query = optionsValue.Tracking
                ? cronTickerOccurrenceContext
                : cronTickerOccurrenceContext.AsNoTracking();

            var cronTickerOccurrences = await query
                .Include(x => x.CronTicker)
                .Where(x => !x.ExecutedAt.HasValue && x.Status != TickerStatus.Inprogress &&
                            x.Status != TickerStatus.Cancelled)
                .Where(x => x.ExecutionTime < now.AddSeconds(1))
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);

            return cronTickerOccurrences
                .Select(x => x.ToCronTickerOccurrence<CronTickerOccurrence<TCronTicker>, TCronTicker>()).ToArray();
        }

        public async Task<CronTickerOccurrence<TCronTicker>[]> GetQueuedNextCronOccurrences(Guid tickerId,
            Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();

            var cronTickerOccurrenceContext = GetDbSet<CronTickerOccurrenceEntity<CronTickerEntity>>();

            var query = optionsValue.Tracking
                ? cronTickerOccurrenceContext
                : cronTickerOccurrenceContext.AsNoTracking();

            var nextCronOccurrences = await query
                .Where(x => x.CronTickerId == tickerId)
                .Where(x => x.Status == TickerStatus.Queued)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);

            return nextCronOccurrences
                .Select(x => x.ToCronTickerOccurrence<CronTickerOccurrence<TCronTicker>, TCronTicker>()).ToArray();
        }

        public async Task<CronTickerOccurrence<TCronTicker>[]> GetCronOccurrencesByCronTickerIdAndStatusFlag(
            Guid tickerId, TickerStatus[] tickerStatuses,
            Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();

            var cronTickerOccurrenceContext = GetDbSet<CronTickerOccurrenceEntity<CronTickerEntity>>();

            var query = optionsValue.Tracking
                ? cronTickerOccurrenceContext
                : cronTickerOccurrenceContext.AsNoTracking();

            var nextCronOccurrences = await query
                .Where(x => x.CronTickerId == tickerId)
                .Where(x => tickerStatuses.Contains(x.Status))
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);

            return nextCronOccurrences
                .Select(x => x.ToCronTickerOccurrence<CronTickerOccurrence<TCronTicker>, TCronTicker>()).ToArray();
        }

        public async Task<CronTickerOccurrence<TCronTicker>[]> GetAllCronTickerOccurrences(
            Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();

            var cronTickerOccurrenceContext = GetDbSet<CronTickerOccurrenceEntity<CronTickerEntity>>();

            var query = optionsValue.Tracking
                ? cronTickerOccurrenceContext
                : cronTickerOccurrenceContext.AsNoTracking();

            var cronTickerOccurrences = await query
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return cronTickerOccurrences
                .Select(x => x.ToCronTickerOccurrence<CronTickerOccurrence<TCronTicker>, TCronTicker>()).ToArray();
        }

        public async Task<CronTickerOccurrence<TCronTicker>[]> GetAllLockedCronTickerOccurrences(
            Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();

            var cronTickerOccurrenceContext = GetDbSet<CronTickerOccurrenceEntity<CronTickerEntity>>();

            var query = optionsValue.Tracking
                ? cronTickerOccurrenceContext
                : cronTickerOccurrenceContext.AsNoTracking();

            var cronTickerOccurrences = await query
                .Where(x => x.LockHolder != null)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return cronTickerOccurrences
                .Select(x => x.ToCronTickerOccurrence<CronTickerOccurrence<TCronTicker>, TCronTicker>()).ToArray();
        }

        public async Task<CronTickerOccurrence<TCronTicker>[]> GetCronTickerOccurrencesByCronTickerId(Guid cronTickerId,
            Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();

            var cronTickerOccurrenceContext = GetDbSet<CronTickerOccurrenceEntity<CronTickerEntity>>();

            var query = optionsValue.Tracking
                ? cronTickerOccurrenceContext
                : cronTickerOccurrenceContext.AsNoTracking();

            var cronTickerOccurrences = await query
                .Where(x => x.CronTickerId == cronTickerId)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return cronTickerOccurrences
                .Select(x => x.ToCronTickerOccurrence<CronTickerOccurrence<TCronTicker>, TCronTicker>()).ToArray();
        }

        public async Task<CronTickerOccurrence<TCronTicker>[]> GetCronTickerOccurrencesWithin(DateTime startDate,
            DateTime endDate, Action<TickerProviderOptions> options = null,
            CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();

            var cronTickerOccurrenceContext = GetDbSet<CronTickerOccurrenceEntity<CronTickerEntity>>();

            var query = optionsValue.Tracking
                ? cronTickerOccurrenceContext
                : cronTickerOccurrenceContext.AsNoTracking();

            var cronTickerOccurrences = await query
                .AsNoTracking()
                .Where(x => x.ExecutionTime.Date >= startDate && x.ExecutionTime.Date <= endDate)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return cronTickerOccurrences
                .Select(x => x.ToCronTickerOccurrence<CronTickerOccurrence<TCronTicker>, TCronTicker>()).ToArray();
        }

        public async Task<CronTickerOccurrence<TCronTicker>[]> GetCronTickerOccurrencesByCronTickerIdWithin(
            Guid cronTickerId, DateTime startDate, DateTime endDate, Action<TickerProviderOptions> options = null,
            CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();

            var cronTickerOccurrenceContext = GetDbSet<CronTickerOccurrenceEntity<CronTickerEntity>>();

            var query = optionsValue.Tracking
                ? cronTickerOccurrenceContext
                : cronTickerOccurrenceContext.AsNoTracking();

            var cronTickerOccurrences = await query
                .Where(x => x.CronTickerId == cronTickerId)
                .Where(x => x.ExecutionTime.Date >= startDate && x.ExecutionTime.Date <= endDate)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return cronTickerOccurrences
                .Select(x => x.ToCronTickerOccurrence<CronTickerOccurrence<TCronTicker>, TCronTicker>()).ToArray();
        }

        public async Task<CronTickerOccurrence<TCronTicker>[]> GetPastCronTickerOccurrencesByCronTickerId(
            Guid cronTickerId, DateTime today, Action<TickerProviderOptions> options = null,
            CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();

            var cronTickerOccurrenceContext = GetDbSet<CronTickerOccurrenceEntity<CronTickerEntity>>();

            var query = optionsValue.Tracking
                ? cronTickerOccurrenceContext
                : cronTickerOccurrenceContext.AsNoTracking();

            var cronTickerOccurrences = await query
                .Include(x => x.CronTicker)
                .Where(x => x.CronTicker.Id == cronTickerId)
                .Where(x => x.ExecutionTime.Date < today)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return cronTickerOccurrences
                .Select(x => x.ToCronTickerOccurrence<CronTickerOccurrence<TCronTicker>, TCronTicker>()).ToArray();
        }

        public async Task<CronTickerOccurrence<TCronTicker>[]> GetTodayCronTickerOccurrencesByCronTickerId(
            Guid cronTickerId, DateTime today, Action<TickerProviderOptions> options = null,
            CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();

            var cronTickerOccurrenceContext = GetDbSet<CronTickerOccurrenceEntity<CronTickerEntity>>();

            var query = optionsValue.Tracking
                ? cronTickerOccurrenceContext
                : cronTickerOccurrenceContext.AsNoTracking();

            var cronTickerOccurrences = await query
                .Where(x => x.CronTicker.Id == cronTickerId)
                .Where(x => x.ExecutionTime.Date == today)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return cronTickerOccurrences
                .Select(x => x.ToCronTickerOccurrence<CronTickerOccurrence<TCronTicker>, TCronTicker>()).ToArray();
        }

        public async Task<CronTickerOccurrence<TCronTicker>[]> GetFutureCronTickerOccurrencesByCronTickerId(
            Guid cronTickerId, DateTime today, Action<TickerProviderOptions> options = null,
            CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();

            var cronTickerOccurrenceContext = GetDbSet<CronTickerOccurrenceEntity<CronTickerEntity>>();

            var query = optionsValue.Tracking
                ? cronTickerOccurrenceContext
                : cronTickerOccurrenceContext.AsNoTracking();

            var cronTickerOccurrences = await query
                .Where(x => x.CronTicker.Id == cronTickerId)
                .Where(x => x.ExecutionTime.Date > today)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return cronTickerOccurrences
                .Select(x => x.ToCronTickerOccurrence<CronTickerOccurrence<TCronTicker>, TCronTicker>()).ToArray();
        }

        public async Task<byte[]> GetCronTickerRequestViaOccurrence(Guid tickerId,
            Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();

            var cronTickerOccurrenceContext = GetDbSet<CronTickerOccurrenceEntity<CronTickerEntity>>();

            var query = optionsValue.Tracking
                ? cronTickerOccurrenceContext
                : cronTickerOccurrenceContext.AsNoTracking();

            var request = await query
                .Where(x => x.Id == tickerId)
                .Select(x => x.CronTicker.Request)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            return request;
        }

        public async Task<DateTime> GetEarliestCronTickerOccurrenceById(Guid id, TickerStatus[] tickerStatuses,
            Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();

            var cronTickerOccurrenceContext = GetDbSet<CronTickerOccurrenceEntity<CronTickerEntity>>();

            var query = optionsValue.Tracking
                ? cronTickerOccurrenceContext
                : cronTickerOccurrenceContext.AsNoTracking();

            var earliestCronTickerOccurrence = await query
                .AsNoTracking()
                .Where(x => x.Id == id)
                .Where(x => tickerStatuses.Contains(x.Status))
                .MinAsync(x => x.ExecutionTime, cancellationToken)
                .ConfigureAwait(false);

            return earliestCronTickerOccurrence;
        }

        public async Task InsertCronTickerOccurrences(
            IEnumerable<CronTickerOccurrence<TCronTicker>> cronTickerOccurrences,
            Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();
            
            // Use execution strategy to handle retry logic and transaction management
            await DbContext.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                using var transaction = DbContext.Database.BeginTransaction();
                
                try
                {
                    var cronTickerOccurrenceContext = GetDbSet<CronTickerOccurrenceEntity<CronTickerEntity>>();
                    
                    // Convert to entities
                    var entities = cronTickerOccurrences.Select(x =>
                        x.ToCronTickerOccurrenceEntity<TCronTicker, CronTickerOccurrence<TCronTicker>>()).ToList();
                    
                    // Efficiently check for existing occurrences to prevent duplicates
                    var existingKeys = await GetExistingOccurrenceKeysAsync(cronTickerOccurrences, cancellationToken);
                    
                    var newEntities = new List<CronTickerOccurrenceEntity<CronTickerEntity>>();
                    
                    foreach (var entity in entities)
                    {
                        var key = (entity.CronTickerId, entity.ExecutionTime);
                        if (!existingKeys.Contains(key))
                        {
                            newEntities.Add(entity);
                        }
                    }
                    
                    // Only add and save if there are new entities
                    if (newEntities.Any())
                    {
                        await cronTickerOccurrenceContext.AddRangeAsync(newEntities, cancellationToken);
                        await DbContext.SaveChangesAsync(cancellationToken);
                    }
                    
                    // Commit the transaction
                    transaction.Commit();
                    
                    // Detach entities to prevent memory leaks
                    DetachAll<CronTickerOccurrenceEntity<CronTickerEntity>>();
                }
                catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex, "UQ_CronTickerId_ExecutionTime"))
                {
                    // Rollback on constraint violation
                    transaction.Rollback();
                    
                    // Detach all entities to prevent memory leaks
                    DetachAll<CronTickerOccurrenceEntity<CronTickerEntity>>();
                    
                    // Log the constraint violation but don't throw - this is expected in multi-node scenarios
                    // The occurrence already exists, which is fine
                    return;
                }
                catch (DbUpdateConcurrencyException)
                {
                    // Handle EF Core concurrency conflicts
                    transaction.Rollback();
                    
                    // Detach all entities to prevent memory leaks
                    DetachAll<CronTickerOccurrenceEntity<CronTickerEntity>>();
                    
                    // This is expected in multi-node scenarios where multiple nodes try to insert the same occurrence
                    return;
                }
                catch (Exception ex) when (ex.Message.Contains("concurrency") || ex.Message.Contains("Concurrency") || 
                                         ex.GetType().Name.Contains("Concurrency") || 
                                         ex.GetType().Name.Contains("AggregateUpdateConcurrency"))
                {
                    // Handle any other concurrency-related exceptions (including PostgreSQL specific ones)
                    transaction.Rollback();
                    
                    // Detach all entities to prevent memory leaks
                    DetachAll<CronTickerOccurrenceEntity<CronTickerEntity>>();
                    
                    // This is expected in multi-node scenarios
                    return;
                }
                catch (Exception)
                {
                    transaction.Rollback();
                    throw;
                }
            });
        }

        public async Task UpdateCronTickerOccurrences(
            IEnumerable<CronTickerOccurrence<TCronTicker>> cronTickerOccurrences,
            Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();
            
            // Use execution strategy to handle retry logic and transaction management
            await DbContext.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                using var transaction = await DbContext.Database.BeginTransactionAsync(cancellationToken);
                
                try
                {
                    var cronTickerOccurrenceContext = GetDbSet<CronTickerOccurrenceEntity<CronTickerEntity>>();
                    
                    // Get existing occurrences to check for duplicates
                    var existingKeys = await GetExistingOccurrenceKeysAsync(cronTickerOccurrences, cancellationToken);
                    
                    // Filter out occurrences that don't exist (can't update what doesn't exist)
                    var validOccurrences = cronTickerOccurrences
                        .Where(x => existingKeys.Contains((x.CronTickerId, x.ExecutionTime)))
                        .ToList();
                    
                    if (!validOccurrences.Any())
                    {
                        // No valid occurrences to update, commit transaction and return
                        transaction.Commit();
                        return;
                    }
                    
                    // First, detach any existing tracked entities to prevent conflicts
                    DetachAll<CronTickerOccurrenceEntity<CronTickerEntity>>();
                    
                    // Update only valid occurrences using proper EF Core approach
                    foreach (var occurrence in validOccurrences)
                    {
                        // Check if entity is already tracked
                        var existingEntity = cronTickerOccurrenceContext
                            .Local
                            .FirstOrDefault(x => x.Id == occurrence.Id);
                        
                        if (existingEntity != null)
                        {
                            // Update existing tracked entity
                            existingEntity.CronTickerId = occurrence.CronTickerId;
                            existingEntity.ExecutionTime = occurrence.ExecutionTime;
                            existingEntity.ExecutedAt = occurrence.ExecutedAt;
                            existingEntity.ElapsedTime = occurrence.ElapsedTime;
                            existingEntity.Status = occurrence.Status;
                            existingEntity.Exception = occurrence.Exception;
                            existingEntity.RetryCount = occurrence.RetryCount;
                            existingEntity.LockHolder = occurrence.LockHolder;
                            existingEntity.LockedAt = occurrence.LockedAt;
                        }
                        else
                        {
                            // Create new entity and attach it
                            var entity = new CronTickerOccurrenceEntity<CronTickerEntity>
                            {
                                Id = occurrence.Id,
                                CronTickerId = occurrence.CronTickerId,
                                ExecutionTime = occurrence.ExecutionTime,
                                ExecutedAt = occurrence.ExecutedAt,
                                ElapsedTime = occurrence.ElapsedTime,
                                Status = occurrence.Status,
                                Exception = occurrence.Exception,
                                RetryCount = occurrence.RetryCount,
                                LockHolder = occurrence.LockHolder,
                                LockedAt = occurrence.LockedAt
                            };
                            
                            cronTickerOccurrenceContext.Attach(entity);
                            DbContext.Entry(entity).State = EntityState.Modified;
                        }
                    }
                    
                    await DbContext.SaveChangesAsync(cancellationToken);
                    transaction.Commit();
                    
                    // Detach all entities to prevent memory leaks
                    DetachAll<CronTickerOccurrenceEntity<CronTickerEntity>>();
                }
                catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
                {
                    // Handle duplicate constraint violations gracefully
                    // This can happen in multi-node scenarios where multiple nodes try to update the same occurrence
                    transaction.Rollback();
                    
                    // Detach all entities to prevent memory leaks
                    DetachAll<CronTickerOccurrenceEntity<CronTickerEntity>>();
                    
                    // This is expected in multi-node scenarios
                }
                catch (DbUpdateConcurrencyException)
                {
                    // Handle EF Core concurrency conflicts
                    transaction.Rollback();
                    
                    // Detach all entities to prevent memory leaks
                    DetachAll<CronTickerOccurrenceEntity<CronTickerEntity>>();
                    
                    // This is expected in multi-node scenarios where multiple nodes try to update the same occurrence
                }
                catch (Exception ex) when (ex.Message.Contains("concurrency") || ex.Message.Contains("Concurrency") || 
                                           ex.GetType().Name.Contains("Concurrency") || 
                                           ex.GetType().Name.Contains("AggregateUpdateConcurrency"))
                {
                    // Handle any other concurrency-related exceptions (including PostgreSQL specific ones)
                    transaction.Rollback();
                    
                    // Detach all entities to prevent memory leaks
                    DetachAll<CronTickerOccurrenceEntity<CronTickerEntity>>();
                    
                    // This is expected in multi-node scenarios
                }
                catch (Exception)
                {
                    transaction.Rollback();
                    throw;
                }
            }).ConfigureAwait(false);
        }

        public async Task RemoveCronTickerOccurrences(
            IEnumerable<CronTickerOccurrence<TCronTicker>> cronTickerOccurrences,
            Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var entities = cronTickerOccurrences
                .Select(x => x.ToCronTickerOccurrenceEntity<TCronTicker, CronTickerOccurrence<TCronTicker>>());

            DeleteRange(entities, x => x.Id);

            await SaveAndDetachAsync<CronTickerOccurrenceEntity<CronTickerEntity>>(cancellationToken)
                .ConfigureAwait(false);
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Checks if cron ticker occurrences already exist for the given cron ticker IDs and execution times
        /// </summary>
        private async Task<HashSet<(Guid CronTickerId, DateTime ExecutionTime)>> GetExistingOccurrenceKeysAsync(
            IEnumerable<CronTickerOccurrence<TCronTicker>> occurrences, 
            CancellationToken cancellationToken)
        {
            var cronTickerOccurrenceContext = GetDbSet<CronTickerOccurrenceEntity<CronTickerEntity>>();
            
            var keys = occurrences.Select(x => (x.CronTickerId, x.ExecutionTime)).Distinct().ToList();
            
            if (!keys.Any())
                return new HashSet<(Guid, DateTime)>();
            
            // Optimize by using bulk query instead of N individual queries
            // Extract unique CronTickerIds and ExecutionTimes for efficient filtering
            var cronTickerIds = keys.Select(x => x.CronTickerId).Distinct().ToList();
            var executionTimes = keys.Select(x => x.ExecutionTime).Distinct().ToList();
            
            // Single query to get all existing occurrences that match any of our keys
            var existingOccurrences = await cronTickerOccurrenceContext
                .AsNoTracking()
                .Where(x => cronTickerIds.Contains(x.CronTickerId) && executionTimes.Contains(x.ExecutionTime))
                .Select(x => new { x.CronTickerId, x.ExecutionTime })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            
            // Convert to HashSet for O(1) lookup performance
            var existingKeys = existingOccurrences
                .Select(x => (x.CronTickerId, x.ExecutionTime))
                .ToHashSet();
                
            return existingKeys;
        }

        #endregion
    }
}