using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.EntityFrameworkCore.Entities;
using TickerQ.Utilities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Extensions;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models.Ticker;

namespace TickerQ.EntityFrameworkCore.Infrastructure
{
    internal class
        TickerEfCorePersistenceProvider<TDbContext, TTimeTicker, TTimeTickerEntity, TCronTicker, TCronTickerEntity> :
            BasePersistenceProvider<TDbContext, TTimeTickerEntity, TCronTickerEntity>,
            ITickerPersistenceProvider<TTimeTicker, TCronTicker>
        where TDbContext : DbContext
        where TTimeTicker : TimeTicker, new()
        where TTimeTickerEntity : TimeTickerEntity, new()
        where TCronTicker : CronTicker, new()
        where TCronTickerEntity : CronTickerEntity, new()
    {
        private readonly ITickerClock _clock;
        private readonly ICronTickerMapper<TCronTicker, TCronTickerEntity> _cronMapper;
        private readonly ITimeTickerMapper<TTimeTicker, TTimeTickerEntity> _timeMapper;
        private readonly ILogger<TickerEfCorePersistenceProvider<TDbContext, TTimeTicker, TTimeTickerEntity, TCronTicker, TCronTickerEntity>> _logger;

        public TickerEfCorePersistenceProvider(
            TDbContext dbContext,
            ITickerClock clock,
            ICronTickerMapper<TCronTicker, TCronTickerEntity> cronMapper,
            ITimeTickerMapper<TTimeTicker, TTimeTickerEntity> timeMapper,
            ILogger<TickerEfCorePersistenceProvider<TDbContext, TTimeTicker, TTimeTickerEntity, TCronTicker, TCronTickerEntity>> logger) : base(dbContext)
        {
            _clock = clock;
            _cronMapper = cronMapper;
            _timeMapper = timeMapper;
            _logger = logger;
        }

        #region Time Ticker Operations

        public async Task<TTimeTicker> GetTimeTickerById(Guid id, Action<TickerProviderOptions> options = null,
            CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();

            var timeTickerContext = GetDbSet<TTimeTickerEntity>();

            var query = optionsValue.Tracking
                ? timeTickerContext
                : timeTickerContext.AsNoTracking();

            var timeTicker = await query
                .FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
                .ConfigureAwait(false);

            return _timeMapper.ToTimeTicker(timeTicker);
        }

        public async Task<TTimeTicker[]> GetTimeTickersByIds(Guid[] ids, Action<TickerProviderOptions> options = null,
            CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();

            var timeTickerContext = GetDbSet<TTimeTickerEntity>();

            var query = optionsValue.Tracking
                ? timeTickerContext
                : timeTickerContext.AsNoTracking();

            var timeTickers = await query
                .Where(x => ids.Contains(x.Id))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return timeTickers.Select(x => _timeMapper.ToTimeTicker(x)).ToArray();
        }

        public async Task<TTimeTicker[]> GetNextTimeTickers(string lockHolder, DateTime roundedMinDate,
            Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();

            // Uses optimistic bulk locking for maximum speed within timeout windows
            return await DbContext.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                var timeTickerContext = GetDbSet<TTimeTickerEntity>();

                // Single optimized transaction with bulk processing
                using var transaction = DbContext.Database.BeginTransaction();

                try
                {
                    // Fast bulk query and update in one operation
                    var availableTickers = await timeTickerContext
                        .Where(x =>
                            ((x.LockHolder == null && x.Status == TickerStatus.Idle) ||
                             (x.LockHolder == lockHolder && x.Status == TickerStatus.Queued)) &&
                            x.ExecutionTime >= roundedMinDate.AddSeconds(-2) &&
                            x.ExecutionTime < roundedMinDate.AddSeconds(1))
                        .OrderBy(x => x.ExecutionTime)
                        .Take(100)
                        .Include(x => x.ParentJob)
                        .ToListAsync(cancellationToken)
                        .ConfigureAwait(false);

                    if (!availableTickers.Any())
                    {
                        transaction.Rollback();
                        return Array.Empty<TTimeTicker>();
                    }

                    // Bulk update all tickers at once (fastest approach)
                    var lockTime = _clock.UtcNow;
                    var successfulTickers = new List<TTimeTickerEntity>();

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

                        var result = successfulTickers.Select(x => _timeMapper.ToTimeTicker(x)).ToArray();
                        DetachAll<TTimeTickerEntity>();
                        return result;
                    }

                    transaction.Rollback();
                    return Array.Empty<TTimeTicker>();
                }
                catch (DbUpdateConcurrencyException)
                {
                    transaction.Rollback();
                    DetachAll<TTimeTickerEntity>();
                    return Array.Empty<TTimeTicker>();
                }
                catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
                {
                    transaction.Rollback();
                    DetachAll<TTimeTickerEntity>();
                    return Array.Empty<TTimeTicker>();
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

            var timeTickerContext = GetDbSet<TTimeTickerEntity>();

            var query = optionsValue.Tracking
                ? timeTickerContext
                : timeTickerContext.AsNoTracking();

            var timeTickers = await query
                .Where(x => tickerStatuses.Contains(x.Status))
                .Where(x => x.LockHolder == lockHolder)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);

            return timeTickers.Select(x => _timeMapper.ToTimeTicker(x)).ToArray();
        }

        public async Task<TTimeTicker[]> GetTimedOutTimeTickers(DateTime now,
            Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();

            var timeTickerContext = GetDbSet<TTimeTickerEntity>();

            var query = optionsValue.Tracking
                ? timeTickerContext
                : timeTickerContext.AsNoTracking();

            var timeTickers = await query
                .Where(x =>
                    (x.Status == TickerStatus.Idle && x.ExecutionTime.AddSeconds(1) < now) ||
                    (x.Status == TickerStatus.Queued && x.ExecutionTime.AddSeconds(3) < now))
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);

            return timeTickers.Select(x => _timeMapper.ToTimeTicker(x)).ToArray();
        }

        public async Task<TTimeTicker[]> GetAllTimeTickers(Action<TickerProviderOptions> options = null,
            CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();

            var timeTickerContext = GetDbSet<TTimeTickerEntity>();

            var query = optionsValue.Tracking
                ? timeTickerContext
                : timeTickerContext.AsNoTracking();

            var timeTickers = await query
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return timeTickers.Select(x => _timeMapper.ToTimeTicker(x)).ToArray();
        }

        public async Task<TTimeTicker[]> GetAllLockedTimeTickers(Action<TickerProviderOptions> options = null,
            CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();

            var timeTickerContext = GetDbSet<TTimeTickerEntity>();

            var query = optionsValue.Tracking
                ? timeTickerContext
                : timeTickerContext.AsNoTracking();

            var timeTickers = await query
                .Where(x => x.LockHolder != null)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return timeTickers.Select(x => _timeMapper.ToTimeTicker(x)).ToArray();
        }

        public async Task<TTimeTicker[]> GetTimeTickersWithin(DateTime startDate, DateTime endDate,
            Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();

            var timeTickerContext = GetDbSet<TTimeTickerEntity>();

            var query = optionsValue.Tracking
                ? timeTickerContext
                : timeTickerContext.AsNoTracking();

            var timeTickers = await query
                .Where(x => x.ExecutionTime.Date >= startDate && x.ExecutionTime.Date <= endDate)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return timeTickers.Select(x => _timeMapper.ToTimeTicker(x)).ToArray();
        }

        public async Task InsertTimeTickers(IEnumerable<TTimeTicker> tickers,
            Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var timeTickerContext = GetDbSet<TTimeTickerEntity>();
            await timeTickerContext.AddRangeAsync(tickers.Select(x => _timeMapper.ToTimeTickerEntity(x)), cancellationToken);

            await SaveAndDetachAsync<TTimeTickerEntity>(cancellationToken).ConfigureAwait(false);
        }

        public async Task UpdateTimeTickers(IEnumerable<TTimeTicker> tickers,
            Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var entities = tickers.Select(x => _timeMapper.ToTimeTickerEntity(x));

            UpsertRange(entities, x => x.Id);

            await SaveAndDetachAsync<TTimeTickerEntity>(cancellationToken).ConfigureAwait(false);
        }

        public async Task RemoveTimeTickers(IEnumerable<TTimeTicker> tickers,
            Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var entities = tickers.Select(x => _timeMapper.ToTimeTickerEntity(x));

            DeleteRange(entities, x => x.Id);

            await SaveAndDetachAsync<TTimeTickerEntity>(cancellationToken).ConfigureAwait(false);
        }

        public async Task<TTimeTicker[]> GetChildTickersByParentId(Guid parentTickerId,
            CancellationToken cancellationToken = new CancellationToken())
        {
            var timeTickerContext = GetDbSet<TTimeTickerEntity>();

            var childTickers = await timeTickerContext
                .AsNoTracking()
                .Where(x => x.BatchParent == parentTickerId)
                .ToListAsync(cancellationToken: cancellationToken);

            return childTickers.Select(x => _timeMapper.ToTimeTicker(x)).ToArray();
        }

        public async Task<byte[]> GetTimeTickerRequest(Guid tickerId, Action<TickerProviderOptions> options = null,
            CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();

            var timeTickerContext = GetDbSet<TTimeTickerEntity>();

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

            var timeTickerContext = GetDbSet<TTimeTickerEntity>();

            var query = optionsValue.Tracking
                ? timeTickerContext
                : timeTickerContext.AsNoTracking();

            var next = await query
                .Where(x => x.LockHolder == null
                            && tickerStatuses.Contains(x.Status)
                            && x.ExecutionTime >= now)
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

            var cronTickerContext = GetDbSet<TCronTickerEntity>();

            var query = optionsValue.Tracking
                ? cronTickerContext
                : cronTickerContext.AsNoTracking();

            var cronTicker = await query
                .FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
                .ConfigureAwait(false);

            return _cronMapper.ToCronTicker(cronTicker);
        }

        public async Task<TCronTicker[]> GetCronTickersByIds(Guid[] ids, Action<TickerProviderOptions> options = null,
            CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();

            var cronTickerContext = GetDbSet<TCronTickerEntity>();

            var query = optionsValue.Tracking
                ? cronTickerContext
                : cronTickerContext.AsNoTracking();

            var cronTickers = await query
                .Where(x => ids.Contains(x.Id))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return cronTickers.Select(x => _cronMapper.ToCronTicker(x)).ToArray();
        }

        public async Task<TCronTicker[]> GetNextCronTickers(string[] expressions,
            Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();

            var cronTickerContext = GetDbSet<TCronTickerEntity>();

            var query = optionsValue.Tracking
                ? cronTickerContext
                : cronTickerContext.AsNoTracking();

            var cronTickers = await query
                .Where(x => expressions.Contains(x.Expression))
                .ToArrayAsync(cancellationToken);

            return cronTickers.Select(x => _cronMapper.ToCronTicker(x)).ToArray();
        }

        public async Task<TCronTicker[]> GetAllExistingInitializedCronTickers(
            Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();

            var cronTickerContext = GetDbSet<TCronTickerEntity>();

            var query = optionsValue.Tracking
                ? cronTickerContext
                : cronTickerContext.AsNoTracking();

            var existingCronTickers = await query
                .Where(x => !string.IsNullOrEmpty(x.InitIdentifier) &&
                            x.InitIdentifier.StartsWith("MemoryTicker_Seed"))
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            return existingCronTickers.Select(x => _cronMapper.ToCronTicker(x)).ToArray();
        }

        public async Task<TCronTicker[]> GetAllCronTickers(Action<TickerProviderOptions> options = null,
            CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();

            var cronTickerContext = GetDbSet<TCronTickerEntity>();

            var query = optionsValue.Tracking
                ? cronTickerContext
                : cronTickerContext.AsNoTracking();

            var cronTickers = await query
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return cronTickers.Select(x => _cronMapper.ToCronTicker(x)).ToArray();
        }

        public async Task<Tuple<Guid, string>[]> GetAllCronTickerExpressions(
            Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();

            var cronTickerContext = GetDbSet<TCronTickerEntity>();

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
            var cronTickerContext = GetDbSet<TCronTickerEntity>();

            var entities = tickers.Select(x => _cronMapper.ToCronTickerEntity(x));

            await cronTickerContext.AddRangeAsync(entities, cancellationToken).ConfigureAwait(false);

            await SaveAndDetachAsync<TCronTickerEntity>(cancellationToken).ConfigureAwait(false);
        }

        public async Task UpdateCronTickers(IEnumerable<TCronTicker> tickers,
            Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var entities = tickers.Select(x => _cronMapper.ToCronTickerEntity(x));

            UpsertRange(entities, x => x.Id);

            await SaveAndDetachAsync<TCronTickerEntity>(cancellationToken).ConfigureAwait(false);
        }

        public async Task RemoveCronTickers(IEnumerable<TCronTicker> tickers,
            Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var context = GetDbSet<TCronTickerEntity>();

            var tickerEntities = tickers.Select(x => _cronMapper.ToCronTickerEntity(x));

            context.RemoveRange(tickerEntities);

            await SaveAndDetachAsync<TCronTickerEntity>(cancellationToken).ConfigureAwait(false);
        }

        #endregion

        #region Cron Ticker Occurrence Operations

        public async Task<CronTickerOccurrence<TCronTicker>> GetCronTickerOccurrenceById(Guid id,
            Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();

            var cronTickerOccurrenceContext = GetDbSet<CronTickerOccurrenceEntity<TCronTickerEntity>>();

            var query = optionsValue.Tracking
                ? cronTickerOccurrenceContext
                : cronTickerOccurrenceContext.AsNoTracking();

            var cronTickerOccurrence = await query
                .FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
                .ConfigureAwait(false);

            return cronTickerOccurrence.ToCronTickerOccurrence<CronTickerOccurrence<TCronTicker>, TCronTicker, TCronTickerEntity>(_cronMapper);
        }

        public async Task<CronTickerOccurrence<TCronTicker>[]> GetCronTickerOccurrencesByIds(Guid[] ids,
            Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();

            var cronTickerOccurrenceContext = GetDbSet<CronTickerOccurrenceEntity<TCronTickerEntity>>();

            var query = optionsValue.Tracking
                ? cronTickerOccurrenceContext
                : cronTickerOccurrenceContext.AsNoTracking();

            var cronTickerOccurrences = await query
                .Where(x => ids.Contains(x.Id))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return cronTickerOccurrences
                .Select(x => x.ToCronTickerOccurrence<CronTickerOccurrence<TCronTicker>, TCronTicker, TCronTickerEntity>(_cronMapper)).ToArray();
        }

        public async Task<CronTickerOccurrence<TCronTicker>[]> GetCronTickerOccurrencesByCronTickerIds(Guid[] ids,
            int? takeLimit, Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();

            var now = _clock.UtcNow;

            var cronTickerOccurrenceContext = GetDbSet<CronTickerOccurrenceEntity<TCronTickerEntity>>();

            var query = optionsValue.Tracking
                ? cronTickerOccurrenceContext
                : cronTickerOccurrenceContext.AsNoTracking();

            var cronTickerOccurrences = await query
                .Where(x => ids.Contains(x.CronTickerId))
                .Where(x => x.ExecutionTime >= now)
                .Where(x => x.Status != TickerStatus.Done && x.Status != TickerStatus.DueDone && 
                           x.Status != TickerStatus.Cancelled && x.Status != TickerStatus.Failed)
                .OrderBy(x => x.ExecutionTime)
                .ToListAsync(cancellationToken);

            return cronTickerOccurrences
                .Select(x =>
                    x.ToCronTickerOccurrence<CronTickerOccurrence<TCronTicker>, TCronTicker, TCronTickerEntity>(
                        _cronMapper)).ToArray();
        }

        public async Task<CronTickerOccurrence<TCronTicker>[]> GetNextCronTickerOccurrences(DateTime nextOccurrence,
            string lockHolder,
            Guid[] cronTickerIds, Action<TickerProviderOptions> options = null,
            CancellationToken cancellationToken = default)
        {
            // Uses optimistic bulk locking for maximum speed within timeout windows - SAME AS TIMETICKERS
            return await DbContext.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                var cronTickerOccurrenceContext = GetDbSet<CronTickerOccurrenceEntity<TCronTickerEntity>>();

                // Single optimized transaction with bulk processing
                using var transaction = DbContext.Database.BeginTransaction();

                try
                {
                    // Allow locking idle occurrences, but prevent same node from re-locking its own queued occurrences
                    var availableOccurrences = await cronTickerOccurrenceContext
                        .Where(x =>
                            cronTickerIds.Contains(x.CronTickerId) &&
                            ((x.LockHolder == null && x.Status == TickerStatus.Idle) ||
                             (x.LockHolder == lockHolder && x.Status == TickerStatus.Queued)) &&
                            x.ExecutionTime == nextOccurrence)
                        .OrderBy(x => x.ExecutionTime)
                        .ToListAsync(cancellationToken)
                        .ConfigureAwait(false);

                    if (!availableOccurrences.Any())
                    {
                        transaction.Rollback();
                        return Array.Empty<CronTickerOccurrence<TCronTicker>>();
                    }

                    // Bulk update all occurrences at once
                    var lockTime = _clock.UtcNow;
                    var successfulOccurrences = new List<CronTickerOccurrenceEntity<TCronTickerEntity>>();

                    foreach (var occurrence in availableOccurrences)
                    {
                        // Fast in-memory check and update - SIMILAR TO TIMETICKERS
                        // Lock idle occurrences or steal queued ones from other nodes, but never re-lock own queued ones
                        if ((occurrence.LockHolder == null && occurrence.Status == TickerStatus.Idle) ||
                            (occurrence.LockHolder != lockHolder && occurrence.Status == TickerStatus.Queued))
                        {
                            occurrence.Status = TickerStatus.Queued;
                            occurrence.LockHolder = lockHolder;
                            occurrence.LockedAt = lockTime;
                            successfulOccurrences.Add(occurrence);
                        }
                    }

                    if (successfulOccurrences.Any())
                    {
                        // Single bulk save operation
                        await DbContext.SaveChangesAsync(cancellationToken);
                        transaction.Commit();

                        var result = successfulOccurrences
                            .Select(x =>
                                x.ToCronTickerOccurrence<CronTickerOccurrence<TCronTicker>, TCronTicker,
                                    TCronTickerEntity>(_cronMapper))
                            .ToArray();
                        DetachAll<CronTickerOccurrenceEntity<TCronTickerEntity>>();
                        return result;
                    }

                    transaction.Rollback();
                    return Array.Empty<CronTickerOccurrence<TCronTicker>>();
                }
                catch (DbUpdateConcurrencyException)
                {
                    transaction.Rollback();
                    DetachAll<CronTickerOccurrenceEntity<TCronTickerEntity>>();
                    return Array.Empty<CronTickerOccurrence<TCronTicker>>();
                }
                catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
                {
                    transaction.Rollback();
                    DetachAll<CronTickerOccurrenceEntity<TCronTickerEntity>>();
                    return Array.Empty<CronTickerOccurrence<TCronTicker>>();
                }
                catch
                {
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

            var cronTickerOccurrenceContext = GetDbSet<CronTickerOccurrenceEntity<TCronTickerEntity>>();

            var query = optionsValue.Tracking
                ? cronTickerOccurrenceContext
                : cronTickerOccurrenceContext.AsNoTracking();

            var cronTickerOccurrences = await query
                .Where(x => tickerStatuses.Contains(x.Status))
                .Where(x => x.LockHolder == lockHolder)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);

            return cronTickerOccurrences
                .Select(x =>
                    x.ToCronTickerOccurrence<CronTickerOccurrence<TCronTicker>, TCronTicker, TCronTickerEntity>(
                        _cronMapper)).ToArray();
        }

        public async Task<CronTickerOccurrence<TCronTicker>[]> GetExistingCronTickerOccurrences(
            Guid[] cronTickerIds,
            Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();
            var cronTickerOccurrenceContext = GetDbSet<CronTickerOccurrenceEntity<TCronTickerEntity>>();

            var query = optionsValue.Tracking
                ? cronTickerOccurrenceContext
                : cronTickerOccurrenceContext.AsNoTracking();

            var occurrences = await query
                .Where(x => cronTickerIds.Contains(x.CronTickerId) &&
                            x.Status != TickerStatus.Done &&
                            x.Status != TickerStatus.DueDone &&
                            x.Status != TickerStatus.Failed &&
                            x.Status != TickerStatus.Cancelled)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return occurrences
                .Select(x =>
                    x.ToCronTickerOccurrence<CronTickerOccurrence<TCronTicker>, TCronTicker, TCronTickerEntity>(
                        _cronMapper))
                .ToArray();
        }

        public async Task<CronTickerOccurrence<TCronTicker>[]> GetTimedOutCronTickerOccurrences(DateTime now,
            Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();

            var cronTickerOccurrenceContext = GetDbSet<CronTickerOccurrenceEntity<TCronTickerEntity>>();

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
                .Select(x =>
                    x.ToCronTickerOccurrence<CronTickerOccurrence<TCronTicker>, TCronTicker, TCronTickerEntity>(
                        _cronMapper)).ToArray();
        }

        public async Task<CronTickerOccurrence<TCronTicker>[]> GetQueuedNextCronOccurrences(Guid tickerId,
            Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();

            var cronTickerOccurrenceContext = GetDbSet<CronTickerOccurrenceEntity<TCronTickerEntity>>();

            var query = optionsValue.Tracking
                ? cronTickerOccurrenceContext
                : cronTickerOccurrenceContext.AsNoTracking();

            var nextCronOccurrences = await query
                .Where(x => x.CronTickerId == tickerId)
                .Where(x => x.Status == TickerStatus.Queued)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);

            return nextCronOccurrences
                .Select(x =>
                    x.ToCronTickerOccurrence<CronTickerOccurrence<TCronTicker>, TCronTicker, TCronTickerEntity>(
                        _cronMapper)).ToArray();
        }

        public async Task<CronTickerOccurrence<TCronTicker>[]> GetCronOccurrencesByCronTickerIdAndStatusFlag(
            Guid tickerId, TickerStatus[] tickerStatuses,
            Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();

            var cronTickerOccurrenceContext = GetDbSet<CronTickerOccurrenceEntity<TCronTickerEntity>>();

            var query = optionsValue.Tracking
                ? cronTickerOccurrenceContext
                : cronTickerOccurrenceContext.AsNoTracking();

            var nextCronOccurrences = await query
                .Where(x => x.CronTickerId == tickerId)
                .Where(x => tickerStatuses.Contains(x.Status))
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);

            return nextCronOccurrences
                .Select(x =>
                    x.ToCronTickerOccurrence<CronTickerOccurrence<TCronTicker>, TCronTicker, TCronTickerEntity>(
                        _cronMapper)).ToArray();
        }

        public async Task<CronTickerOccurrence<TCronTicker>[]> GetAllCronTickerOccurrences(
            Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();

            var cronTickerOccurrenceContext = GetDbSet<CronTickerOccurrenceEntity<TCronTickerEntity>>();

            var query = optionsValue.Tracking
                ? cronTickerOccurrenceContext
                : cronTickerOccurrenceContext.AsNoTracking();

            var cronTickerOccurrences = await query
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return cronTickerOccurrences
                .Select(x =>
                    x.ToCronTickerOccurrence<CronTickerOccurrence<TCronTicker>, TCronTicker, TCronTickerEntity>(
                        _cronMapper)).ToArray();
        }

        public async Task<CronTickerOccurrence<TCronTicker>[]> GetAllLockedCronTickerOccurrences(
            Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();

            var cronTickerOccurrenceContext = GetDbSet<CronTickerOccurrenceEntity<TCronTickerEntity>>();

            var query = optionsValue.Tracking
                ? cronTickerOccurrenceContext
                : cronTickerOccurrenceContext.AsNoTracking();

            var cronTickerOccurrences = await query
                .Where(x => x.LockHolder != null)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return cronTickerOccurrences
                .Select(x =>
                    x.ToCronTickerOccurrence<CronTickerOccurrence<TCronTicker>, TCronTicker, TCronTickerEntity>(
                        _cronMapper)).ToArray();
        }

        public async Task<CronTickerOccurrence<TCronTicker>[]> GetCronTickerOccurrencesByCronTickerId(Guid cronTickerId,
            Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();

            var cronTickerOccurrenceContext = GetDbSet<CronTickerOccurrenceEntity<TCronTickerEntity>>();

            var query = optionsValue.Tracking
                ? cronTickerOccurrenceContext
                : cronTickerOccurrenceContext.AsNoTracking();

            var cronTickerOccurrences = await query
                .Where(x => x.CronTickerId == cronTickerId)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return cronTickerOccurrences
                .Select(x =>
                    x.ToCronTickerOccurrence<CronTickerOccurrence<TCronTicker>, TCronTicker, TCronTickerEntity>(
                        _cronMapper)).ToArray();
        }

        public async Task<CronTickerOccurrence<TCronTicker>[]> GetCronTickerOccurrencesWithin(DateTime startDate,
            DateTime endDate, Action<TickerProviderOptions> options = null,
            CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();

            var cronTickerOccurrenceContext = GetDbSet<CronTickerOccurrenceEntity<TCronTickerEntity>>();

            var query = optionsValue.Tracking
                ? cronTickerOccurrenceContext
                : cronTickerOccurrenceContext.AsNoTracking();

            var cronTickerOccurrences = await query
                .AsNoTracking()
                .Where(x => x.ExecutionTime.Date >= startDate && x.ExecutionTime.Date <= endDate)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return cronTickerOccurrences
                .Select(x =>
                    x.ToCronTickerOccurrence<CronTickerOccurrence<TCronTicker>, TCronTicker, TCronTickerEntity>(
                        _cronMapper)).ToArray();
        }

        public async Task<CronTickerOccurrence<TCronTicker>[]> GetCronTickerOccurrencesByCronTickerIdWithin(
            Guid cronTickerId, DateTime startDate, DateTime endDate, Action<TickerProviderOptions> options = null,
            CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();

            var cronTickerOccurrenceContext = GetDbSet<CronTickerOccurrenceEntity<TCronTickerEntity>>();

            var query = optionsValue.Tracking
                ? cronTickerOccurrenceContext
                : cronTickerOccurrenceContext.AsNoTracking();

            var cronTickerOccurrences = await query
                .Where(x => x.CronTickerId == cronTickerId)
                .Where(x => x.ExecutionTime.Date >= startDate && x.ExecutionTime.Date <= endDate)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return cronTickerOccurrences
                .Select(x =>
                    x.ToCronTickerOccurrence<CronTickerOccurrence<TCronTicker>, TCronTicker, TCronTickerEntity>(
                        _cronMapper)).ToArray();
        }

        public async Task<CronTickerOccurrence<TCronTicker>[]> GetPastCronTickerOccurrencesByCronTickerId(
            Guid cronTickerId, DateTime today, Action<TickerProviderOptions> options = null,
            CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();

            var cronTickerOccurrenceContext = GetDbSet<CronTickerOccurrenceEntity<TCronTickerEntity>>();

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
                .Select(x =>
                    x.ToCronTickerOccurrence<CronTickerOccurrence<TCronTicker>, TCronTicker, TCronTickerEntity>(
                        _cronMapper)).ToArray();
        }

        public async Task<CronTickerOccurrence<TCronTicker>[]> GetTodayCronTickerOccurrencesByCronTickerId(
            Guid cronTickerId, DateTime today, Action<TickerProviderOptions> options = null,
            CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();

            var cronTickerOccurrenceContext = GetDbSet<CronTickerOccurrenceEntity<TCronTickerEntity>>();

            var query = optionsValue.Tracking
                ? cronTickerOccurrenceContext
                : cronTickerOccurrenceContext.AsNoTracking();

            var cronTickerOccurrences = await query
                .Where(x => x.CronTicker.Id == cronTickerId)
                .Where(x => x.ExecutionTime.Date == today)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return cronTickerOccurrences
                .Select(x =>
                    x.ToCronTickerOccurrence<CronTickerOccurrence<TCronTicker>, TCronTicker, TCronTickerEntity>(
                        _cronMapper)).ToArray();
        }

        public async Task<CronTickerOccurrence<TCronTicker>[]> GetFutureCronTickerOccurrencesByCronTickerId(
            Guid cronTickerId, DateTime today, Action<TickerProviderOptions> options = null,
            CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();

            var cronTickerOccurrenceContext = GetDbSet<CronTickerOccurrenceEntity<TCronTickerEntity>>();

            var query = optionsValue.Tracking
                ? cronTickerOccurrenceContext
                : cronTickerOccurrenceContext.AsNoTracking();

            var cronTickerOccurrences = await query
                .Where(x => x.CronTicker.Id == cronTickerId)
                .Where(x => x.ExecutionTime.Date > today)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return cronTickerOccurrences
                .Select(x =>
                    x.ToCronTickerOccurrence<CronTickerOccurrence<TCronTicker>, TCronTicker, TCronTickerEntity>(
                        _cronMapper)).ToArray();
        }

        public async Task<byte[]> GetCronTickerRequestViaOccurrence(Guid tickerId,
            Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var optionsValue = options.InvokeProviderOptions();

            var cronTickerOccurrenceContext = GetDbSet<CronTickerOccurrenceEntity<TCronTickerEntity>>();

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

            var cronTickerOccurrenceContext = GetDbSet<CronTickerOccurrenceEntity<TCronTickerEntity>>();

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

        public async Task<IList<Guid>> InsertCronTickerOccurrences(
            IEnumerable<CronTickerOccurrence<TCronTicker>> cronTickerOccurrences,
            Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            _logger.LogDebug("Inserting {EntityCount} CronTickerOccurrences", cronTickerOccurrences.Count());

            var listOfSuccessfulIds = new List<Guid>();

            await DbContext.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                var cronTickerOccurrenceContext = GetDbSet<CronTickerOccurrenceEntity<TCronTickerEntity>>();

                var entities = cronTickerOccurrences.Select(x =>
                    x.ToCronTickerOccurrenceEntity(_cronMapper)).ToList();

                foreach (var entity in entities)
                {
                    using var transaction = DbContext.Database.BeginTransaction();

                    try
                    {
                        cronTickerOccurrenceContext.Add(entity);

                        await DbContext.SaveChangesAsync(cancellationToken);

                        transaction.Commit();

                        listOfSuccessfulIds.Add(entity.Id);
                        _logger.LogDebug("Successfully inserted occurrence for CronTickerId={CronTickerId}, ExecutionTime={ExecutionTime:yyyy-MM-dd HH:mm:ss}",
                            entity.CronTickerId, entity.ExecutionTime);
                    }
                    catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex, "UQ_CronTickerId_ExecutionTime"))
                    {
                        // Expected constraint violation during initialization - handle gracefully
                        _logger.LogWarning("Constraint violation handled gracefully for CronTickerId={CronTickerId}, ExecutionTime={ExecutionTime:yyyy-MM-dd HH:mm:ss}",
                            entity.CronTickerId, entity.ExecutionTime);
                        transaction.Rollback();
                        DetachAll<CronTickerOccurrenceEntity<TCronTickerEntity>>();
                        // Continue processing other occurrences (don't add to processedOccurrences)
                    }
                    catch (DbUpdateConcurrencyException)
                    {
                        // Concurrency conflict - handle gracefully
                        transaction.Rollback();
                        DetachAll<CronTickerOccurrenceEntity<TCronTickerEntity>>();
                    }
                    catch (Exception)
                    {
                        // Unexpected errors - re-throw
                        transaction.Rollback();
                        DetachAll<CronTickerOccurrenceEntity<TCronTickerEntity>>();
                        throw;
                    }
                }

                // Log summary of batch processing
                _logger.LogDebug("Batch complete: Total={TotalEntities}, Success={SuccessCount}, Failed={FailedCount}",
                    entities.Count, listOfSuccessfulIds.Count, entities.Count - listOfSuccessfulIds.Count);

                // Detach entities to prevent memory leaks
                DetachAll<CronTickerOccurrenceEntity<TCronTickerEntity>>();
            });

            return listOfSuccessfulIds;
        }

        public async Task UpdateCronTickerOccurrences(
            IEnumerable<CronTickerOccurrence<TCronTicker>> cronTickerOccurrences,
            Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var entities = cronTickerOccurrences.Select(x => x.ToCronTickerOccurrenceEntity(_cronMapper));

            UpsertRange(entities, x => x.Id);

            await SaveAndDetachAsync<TTimeTickerEntity>(cancellationToken).ConfigureAwait(false);
        }

        public async Task RemoveCronTickerOccurrences(
            IEnumerable<CronTickerOccurrence<TCronTicker>> cronTickerOccurrences,
            Action<TickerProviderOptions> options = null, CancellationToken cancellationToken = default)
        {
            var entities = cronTickerOccurrences
                .Select(x => x.ToCronTickerOccurrenceEntity(_cronMapper));

            DeleteRange(entities, x => x.Id);

            await SaveAndDetachAsync<CronTickerOccurrenceEntity<TCronTickerEntity>>(cancellationToken)
                .ConfigureAwait(false);
        }

        #endregion
    }
}
