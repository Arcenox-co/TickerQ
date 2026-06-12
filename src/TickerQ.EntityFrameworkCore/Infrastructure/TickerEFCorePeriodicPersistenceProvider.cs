using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TickerQ.EntityFrameworkCore.DbContextFactory;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ.EntityFrameworkCore.Infrastructure
{
    /// <summary>
    /// EF Core implementation of <see cref="IPeriodicTickerPersistenceProvider{TPeriodicTicker}"/>.
    /// Uses the same DbContext-pool / lease pattern as the time/cron provider so that periodic
    /// scheduling participates in the same connection lifecycle and pooling guarantees.
    /// </summary>
    internal class TickerEfCorePeriodicPersistenceProvider<TDbContext, TPeriodicTicker>
        : IPeriodicTickerPersistenceProvider<TPeriodicTicker>
        where TDbContext : DbContext
        where TPeriodicTicker : PeriodicTickerEntity, new()
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ITickerClock _clock;
        private readonly string _lockHolder;

        public TickerEfCorePeriodicPersistenceProvider(
            IServiceProvider serviceProvider,
            ITickerClock clock,
            SchedulerOptionsBuilder optionsBuilder)
        {
            _serviceProvider = serviceProvider;
            _clock = clock;
            _lockHolder = optionsBuilder.NodeIdentifier;
        }

        private Task<DbContextLease<TDbContext>> CreateDbContextAsync(CancellationToken cancellationToken)
            => DbContextLease<TDbContext>.CreateAsync(_serviceProvider, cancellationToken);

        // -------- Core --------

        public async Task<PeriodicTickerEntity[]> GetAllActivePeriodicTickers(CancellationToken cancellationToken = default)
        {
            using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var dbContext = session.Context;
            var now = _clock.UtcNow;

            return await dbContext.Set<TPeriodicTicker>()
                .AsNoTracking()
                .Where(x => x.IsActive)
                .Where(x => x.StartTime == null || x.StartTime <= now)
                .Where(x => x.EndTime == null || x.EndTime > now)
                .Select(x => new PeriodicTickerEntity
                {
                    Id = x.Id,
                    Function = x.Function,
                    Description = x.Description,
                    Interval = x.Interval,
                    Retries = x.Retries,
                    RetryIntervals = x.RetryIntervals,
                    IsActive = x.IsActive,
                    StartTime = x.StartTime,
                    EndTime = x.EndTime,
                    LastExecutedAt = x.LastExecutedAt,
                    ExecutionCount = x.ExecutionCount,
                    CreatedAt = x.CreatedAt,
                    UpdatedAt = x.UpdatedAt
                })
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task<PeriodicTickerOccurrenceEntity<TPeriodicTicker>> GetEarliestAvailablePeriodicOccurrence(Guid[] ids, CancellationToken cancellationToken = default)
        {
            if (ids == null || ids.Length == 0) return null;

            var idList = ids.ToList();
            var now = _clock.UtcNow;
            var mainSchedulerThreshold = now.AddSeconds(-1);

            using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var dbContext = session.Context;

            return await dbContext.Set<PeriodicTickerOccurrenceEntity<TPeriodicTicker>>()
                .AsNoTracking()
                .Include(x => x.PeriodicTicker)
                .Where(x => idList.Contains(x.PeriodicTickerId))
                .Where(x => x.ExecutionTime >= mainSchedulerThreshold)
                .Where(x => (x.Status == TickerStatus.Idle || x.Status == TickerStatus.Queued)
                            && (x.LockHolder == null || x.LockHolder == _lockHolder))
                .OrderBy(x => x.ExecutionTime)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        public async IAsyncEnumerable<PeriodicTickerOccurrenceEntity<TPeriodicTicker>> QueuePeriodicTickerOccurrences(
            (DateTime Key, InternalManagerContext[] Items) periodicTickerOccurrences,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var now = _clock.UtcNow;
            var executionTime = periodicTickerOccurrences.Key;

            using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var dbContext = session.Context;
            var occSet = dbContext.Set<PeriodicTickerOccurrenceEntity<TPeriodicTicker>>();

            foreach (var item in periodicTickerOccurrences.Items)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (item.NextPeriodicOccurrence is null)
                {
                    // Insert new occurrence; rely on the unique (PeriodicTickerId, ExecutionTime)
                    // index to dedupe across nodes. SaveChanges throws on conflict, swallowed.
                    var newRow = new PeriodicTickerOccurrenceEntity<TPeriodicTicker>
                    {
                        Id = Guid.NewGuid(),
                        Status = TickerStatus.Queued,
                        LockHolder = _lockHolder,
                        ExecutionTime = executionTime,
                        PeriodicTickerId = item.Id,
                        LockedAt = now,
                        CreatedAt = now,
                        UpdatedAt = now
                    };

                    int affected;
                    try
                    {
                        await occSet.AddAsync(newRow, cancellationToken).ConfigureAwait(false);
                        affected = await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    }
                    catch (DbUpdateException)
                    {
                        // Another node won the insert race
                        dbContext.Entry(newRow).State = EntityState.Detached;
                        continue;
                    }

                    if (affected <= 0) continue;

                    // Hydrate parent ticker for downstream code (function name, retries)
                    var parent = await dbContext.Set<TPeriodicTicker>()
                        .AsNoTracking()
                        .FirstOrDefaultAsync(p => p.Id == item.Id, cancellationToken)
                        .ConfigureAwait(false);
                    newRow.PeriodicTicker = parent;
                    yield return newRow;
                }
                else
                {
                    var occId = item.NextPeriodicOccurrence.Id;
                    var prevUpdatedAt = item.NextPeriodicOccurrence.UpdatedAt;

                    var affectedUpdate = await occSet
                        .Where(x => x.Id == occId)
                        .Where(x => x.UpdatedAt == prevUpdatedAt)
                        .Where(x => x.ExecutionTime == executionTime)
                        .Where(x => (x.Status == TickerStatus.Idle || x.Status == TickerStatus.Queued)
                                    && (x.LockHolder == null || x.LockHolder == _lockHolder))
                        .ExecuteUpdateAsync(prop => prop
                            .SetProperty(y => y.LockHolder, _lockHolder)
                            .SetProperty(y => y.LockedAt, now)
                            .SetProperty(y => y.UpdatedAt, now)
                            .SetProperty(y => y.Status, TickerStatus.Queued), cancellationToken)
                        .ConfigureAwait(false);

                    if (affectedUpdate <= 0) continue;

                    var parent = await dbContext.Set<TPeriodicTicker>()
                        .AsNoTracking()
                        .FirstOrDefaultAsync(p => p.Id == item.Id, cancellationToken)
                        .ConfigureAwait(false);

                    yield return new PeriodicTickerOccurrenceEntity<TPeriodicTicker>
                    {
                        Id = occId,
                        PeriodicTickerId = item.Id,
                        ExecutionTime = executionTime,
                        Status = TickerStatus.Queued,
                        LockHolder = _lockHolder,
                        LockedAt = now,
                        UpdatedAt = now,
                        PeriodicTicker = parent
                    };
                }
            }
        }

        public async IAsyncEnumerable<PeriodicTickerOccurrenceEntity<TPeriodicTicker>> QueueTimedOutPeriodicTickerOccurrences(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var now = _clock.UtcNow;
            var fallbackThreshold = now.AddSeconds(-1);

            using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var dbContext = session.Context;
            var occSet = dbContext.Set<PeriodicTickerOccurrenceEntity<TPeriodicTicker>>();

            var rows = await occSet
                .AsNoTracking()
                .Include(x => x.PeriodicTicker)
                .Where(x => x.Status == TickerStatus.Idle || x.Status == TickerStatus.Queued)
                .Where(x => x.ExecutionTime <= fallbackThreshold)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (var row in rows)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var affected = await occSet
                    .Where(x => x.Id == row.Id && x.UpdatedAt == row.UpdatedAt)
                    .ExecuteUpdateAsync(setter => setter
                        .SetProperty(x => x.LockHolder, _lockHolder)
                        .SetProperty(x => x.LockedAt, now)
                        .SetProperty(x => x.UpdatedAt, now)
                        .SetProperty(x => x.Status, TickerStatus.InProgress), cancellationToken)
                    .ConfigureAwait(false);

                if (affected <= 0) continue;

                yield return row;
            }
        }

        public async Task UpdatePeriodicTickerOccurrence(InternalFunctionContext functionContext, CancellationToken cancellationToken = default)
        {
            using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var dbContext = session.Context;
            var now = _clock.UtcNow;

            await dbContext.Set<PeriodicTickerOccurrenceEntity<TPeriodicTicker>>()
                .Where(x => x.Id == functionContext.TickerId)
                .ExecuteUpdateAsync(setter => setter
                    .SetProperty(x => x.Status, functionContext.Status)
                    .SetProperty(x => x.ElapsedTime, functionContext.ElapsedTime)
                    .SetProperty(x => x.ExceptionMessage, functionContext.ExceptionDetails)
                    .SetProperty(x => x.ExecutedAt, functionContext.ExecutedAt == default ? (DateTime?)null : functionContext.ExecutedAt)
                    .SetProperty(x => x.RetryCount, functionContext.RetryCount)
                    .SetProperty(x => x.UpdatedAt, now), cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task UpdatePeriodicTickerAfterExecution(Guid periodicTickerId, DateTime executedAt, bool succeeded = true, CancellationToken cancellationToken = default)
        {
            using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var dbContext = session.Context;
            var now = _clock.UtcNow;

            var query = dbContext.Set<TPeriodicTicker>().Where(x => x.Id == periodicTickerId);

            // LastExecutedAt always advances (success or terminal failure) so the schedule moves forward;
            // ExecutionCount only counts successful runs. Two branches keep each setter expression translatable.
            if (succeeded)
            {
                await query.ExecuteUpdateAsync(setter => setter
                        .SetProperty(x => x.LastExecutedAt, executedAt)
                        .SetProperty(x => x.ExecutionCount, x => x.ExecutionCount + 1)
                        .SetProperty(x => x.UpdatedAt, now), cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                await query.ExecuteUpdateAsync(setter => setter
                        .SetProperty(x => x.LastExecutedAt, executedAt)
                        .SetProperty(x => x.UpdatedAt, now), cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        public async Task ReleaseAcquiredPeriodicTickerOccurrences(Guid[] occurrenceIds, CancellationToken cancellationToken = default)
        {
            using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var dbContext = session.Context;
            var now = _clock.UtcNow;

            var idList = (occurrenceIds ?? Array.Empty<Guid>()).ToList();
            var baseQuery = idList.Count == 0
                ? dbContext.Set<PeriodicTickerOccurrenceEntity<TPeriodicTicker>>()
                : dbContext.Set<PeriodicTickerOccurrenceEntity<TPeriodicTicker>>().Where(x => idList.Contains(x.Id));

            await baseQuery
                .Where(x => x.LockHolder == _lockHolder
                            && (x.Status == TickerStatus.Idle || x.Status == TickerStatus.Queued))
                .ExecuteUpdateAsync(setter => setter
                    .SetProperty(x => x.LockHolder, _ => null)
                    .SetProperty(x => x.LockedAt, _ => (DateTime?)null)
                    .SetProperty(x => x.Status, TickerStatus.Idle)
                    .SetProperty(x => x.UpdatedAt, now), cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task<byte[]> GetPeriodicTickerOccurrenceRequest(Guid tickerId, CancellationToken cancellationToken = default)
        {
            using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var dbContext = session.Context;
            return await dbContext.Set<PeriodicTickerOccurrenceEntity<TPeriodicTicker>>()
                .AsNoTracking()
                .Include(x => x.PeriodicTicker)
                .Where(x => x.Id == tickerId)
                .Select(x => x.PeriodicTicker.Request)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task UpdatePeriodicTickerOccurrencesWithUnifiedContext(Guid[] occurrenceIds, InternalFunctionContext functionContext, CancellationToken cancellationToken = default)
        {
            if (occurrenceIds == null || occurrenceIds.Length == 0) return;

            var idList = occurrenceIds.ToList();
            using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var dbContext = session.Context;
            var now = _clock.UtcNow;

            await dbContext.Set<PeriodicTickerOccurrenceEntity<TPeriodicTicker>>()
                .Where(x => idList.Contains(x.Id))
                .ExecuteUpdateAsync(setter => setter
                    .SetProperty(x => x.Status, functionContext.Status)
                    .SetProperty(x => x.UpdatedAt, now), cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task ReleaseDeadNodePeriodicOccurrenceResources(string instanceIdentifier, CancellationToken cancellationToken = default)
        {
            var now = _clock.UtcNow;
            using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var dbContext = session.Context;
            var occSet = dbContext.Set<PeriodicTickerOccurrenceEntity<TPeriodicTicker>>();

            await occSet
                .Where(x => x.LockHolder == instanceIdentifier
                            && (x.Status == TickerStatus.Queued || x.Status == TickerStatus.InProgress))
                .ExecuteUpdateAsync(setter => setter
                    .SetProperty(x => x.LockHolder, _ => null)
                    .SetProperty(x => x.LockedAt, _ => (DateTime?)null)
                    .SetProperty(x => x.Status, TickerStatus.Idle)
                    .SetProperty(x => x.UpdatedAt, now), cancellationToken)
                .ConfigureAwait(false);
        }

        // -------- Shared (CRUD) --------

        public async Task<TPeriodicTicker> GetPeriodicTickerById(Guid id, CancellationToken cancellationToken = default)
        {
            using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var dbContext = session.Context;
            return await dbContext.Set<TPeriodicTicker>()
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task<TPeriodicTicker[]> GetPeriodicTickers(Expression<Func<TPeriodicTicker, bool>> predicate, CancellationToken cancellationToken = default)
        {
            using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var dbContext = session.Context;
            return await dbContext.Set<TPeriodicTicker>()
                .AsNoTracking()
                .Where(predicate)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task<PaginationResult<TPeriodicTicker>> GetPeriodicTickersPaginated(
            Expression<Func<TPeriodicTicker, bool>> predicate, int pageNumber, int pageSize,
            CancellationToken cancellationToken = default)
        {
            using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var dbContext = session.Context;
            var query = dbContext.Set<TPeriodicTicker>().AsNoTracking().Where(predicate);

            var total = await query.CountAsync(cancellationToken).ConfigureAwait(false);
            var items = await query
                .OrderByDescending(x => x.UpdatedAt)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);

            return new PaginationResult<TPeriodicTicker>
            {
                Items = items,
                TotalCount = total,
                PageNumber = pageNumber,
                PageSize = pageSize
            };
        }

        public async Task<int> InsertPeriodicTickers(TPeriodicTicker[] tickers, CancellationToken cancellationToken = default)
        {
            using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var dbContext = session.Context;
            await dbContext.Set<TPeriodicTicker>().AddRangeAsync(tickers, cancellationToken).ConfigureAwait(false);
            return await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task<int> UpdatePeriodicTickers(TPeriodicTicker[] tickers, CancellationToken cancellationToken = default)
        {
            using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var dbContext = session.Context;
            dbContext.Set<TPeriodicTicker>().UpdateRange(tickers);
            return await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task<int> RemovePeriodicTickers(Guid[] tickerIds, CancellationToken cancellationToken = default)
        {
            if (tickerIds == null || tickerIds.Length == 0) return 0;

            var idList = tickerIds.ToList();
            using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var dbContext = session.Context;
            return await dbContext.Set<TPeriodicTicker>()
                .Where(x => idList.Contains(x.Id))
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        // -------- Occurrences (CRUD) --------

        public async Task<PeriodicTickerOccurrenceEntity<TPeriodicTicker>[]> GetAllPeriodicTickerOccurrences(
            Expression<Func<PeriodicTickerOccurrenceEntity<TPeriodicTicker>, bool>> predicate,
            CancellationToken cancellationToken = default)
        {
            using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var dbContext = session.Context;
            return await dbContext.Set<PeriodicTickerOccurrenceEntity<TPeriodicTicker>>()
                .AsNoTracking()
                .Include(x => x.PeriodicTicker)
                .Where(predicate)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task<PaginationResult<PeriodicTickerOccurrenceEntity<TPeriodicTicker>>> GetAllPeriodicTickerOccurrencesPaginated(
            Expression<Func<PeriodicTickerOccurrenceEntity<TPeriodicTicker>, bool>> predicate,
            int pageNumber, int pageSize, CancellationToken cancellationToken = default)
        {
            using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var dbContext = session.Context;
            var query = dbContext.Set<PeriodicTickerOccurrenceEntity<TPeriodicTicker>>()
                .AsNoTracking()
                .Include(x => x.PeriodicTicker)
                .Where(predicate);

            var total = await query.CountAsync(cancellationToken).ConfigureAwait(false);
            var items = await query
                .OrderByDescending(x => x.UpdatedAt)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);

            return new PaginationResult<PeriodicTickerOccurrenceEntity<TPeriodicTicker>>
            {
                Items = items,
                TotalCount = total,
                PageNumber = pageNumber,
                PageSize = pageSize
            };
        }

        public async Task<int> InsertPeriodicTickerOccurrences(PeriodicTickerOccurrenceEntity<TPeriodicTicker>[] occurrences, CancellationToken cancellationToken = default)
        {
            using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var dbContext = session.Context;
            await dbContext.Set<PeriodicTickerOccurrenceEntity<TPeriodicTicker>>()
                .AddRangeAsync(occurrences, cancellationToken)
                .ConfigureAwait(false);
            return await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task<int> RemovePeriodicTickerOccurrences(Guid[] occurrenceIds, CancellationToken cancellationToken = default)
        {
            if (occurrenceIds == null || occurrenceIds.Length == 0) return 0;

            var idList = occurrenceIds.ToList();
            using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var dbContext = session.Context;
            return await dbContext.Set<PeriodicTickerOccurrenceEntity<TPeriodicTicker>>()
                .Where(x => idList.Contains(x.Id))
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task<PeriodicTickerOccurrenceEntity<TPeriodicTicker>[]> AcquireImmediatePeriodicOccurrencesAsync(Guid[] occurrenceIds, CancellationToken cancellationToken = default)
        {
            if (occurrenceIds == null || occurrenceIds.Length == 0) return [];

            var idList = occurrenceIds.ToList();
            var now = _clock.UtcNow;

            using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var dbContext = session.Context;
            var occSet = dbContext.Set<PeriodicTickerOccurrenceEntity<TPeriodicTicker>>();

            var affected = await occSet
                .Where(x => idList.Contains(x.Id))
                .Where(x => (x.Status == TickerStatus.Idle || x.Status == TickerStatus.Queued)
                            && (x.LockHolder == null || x.LockHolder == _lockHolder))
                .ExecuteUpdateAsync(setter => setter
                    .SetProperty(x => x.LockHolder, _lockHolder)
                    .SetProperty(x => x.LockedAt, now)
                    .SetProperty(x => x.Status, TickerStatus.InProgress)
                    .SetProperty(x => x.UpdatedAt, now), cancellationToken)
                .ConfigureAwait(false);

            if (affected == 0) return [];

            return await occSet
                .AsNoTracking()
                .Include(x => x.PeriodicTicker)
                .Where(x => idList.Contains(x.Id) && x.LockHolder == _lockHolder && x.Status == TickerStatus.InProgress)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);
        }
    }
}

