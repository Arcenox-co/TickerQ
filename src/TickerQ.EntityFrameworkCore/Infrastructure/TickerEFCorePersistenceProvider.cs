using Microsoft.EntityFrameworkCore;

using System;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;
using System.Collections.Generic;

namespace TickerQ.EntityFrameworkCore.Infrastructure
{
    internal class TickerEfCorePersistenceProvider<TDbContext, TTimeTicker, TCronTicker> :
        BasePersistenceProvider<TDbContext, TTimeTicker, TCronTicker>,
        ITickerPersistenceProvider<TTimeTicker, TCronTicker>
        where TDbContext : DbContext
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        public TickerEfCorePersistenceProvider(IServiceProvider serviceProvider, ITickerClock clock, SchedulerOptionsBuilder optionsBuilder, ITickerQRedisContext  redisContext)
            :  base(serviceProvider, clock, optionsBuilder, redisContext) { }
        
        #region Queryable

        public ITickerQueryable<TTimeTicker> TimeTickersQuery()
        {
            return new EfTickerQueryable<TDbContext, TTimeTicker>(
                _serviceProvider,
                (query, relations) =>
                {
                    foreach (var relation in relations)
                    {
                        query = relation switch
                        {
                            TickerRelation.Children => query
                                .Include(x => x.Children),
                            TickerRelation.ChildrenDeep => query
                                .Include(x => x.Children)
                                .ThenInclude(x => x.Children),
                            _ => query
                        };
                    }
                    return query;
                });
        }

        public ITickerQueryable<TCronTicker> CronTickersQuery()
        {
            return new EfTickerQueryable<TDbContext, TCronTicker>(_serviceProvider);
        }

        public ITickerQueryable<CronTickerOccurrenceEntity<TCronTicker>> CronTickerOccurrencesQuery()
        {
            return new EfTickerQueryable<TDbContext, CronTickerOccurrenceEntity<TCronTicker>>(
                _serviceProvider,
                (query, relations) =>
                {
                    foreach (var relation in relations)
                    {
                        if (relation == TickerRelation.CronTicker)
                            query = query.Include(x => x.CronTicker);
                    }
                    return query;
                });
        }

        #endregion

        #region Time_Ticker_Implementations

        public async Task<TTimeTicker> GetTimeTickerById(Guid id, CancellationToken cancellationToken = default)
        {
            using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var dbContext = session.Context;
            var context = dbContext.Set<TTimeTicker>();
            var entity = await context
                .AsNoTracking()
                .Include(x => x.Children)
                .ThenInclude(x => x.Children)
                .FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
                .ConfigureAwait(false);

            if (entity != null)
                await ExtendReadChainsBeyondGrandchildrenAsync(context, new[] { entity }, cancellationToken).ConfigureAwait(false);
            return entity;
        }

        public async Task<TTimeTicker[]> GetTimeTickers(Expression<Func<TTimeTicker, bool>> predicate, CancellationToken cancellationToken)
        {
            using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var dbContext = session.Context;
            var context = dbContext.Set<TTimeTicker>();

            var baseQuery = context
                .Include(x => x.Children)
                .ThenInclude(x => x.Children)
                .AsNoTracking();

            if (predicate != null)
                baseQuery = baseQuery.Where(predicate);

            var result = await baseQuery
                .Where(x => x.ParentId == null)
                .OrderByDescending(x => x.ExecutionTime)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);

            await ExtendReadChainsBeyondGrandchildrenAsync(context, result, cancellationToken).ConfigureAwait(false);
            return result;
        }
        
        public async Task<PaginationResult<TTimeTicker>> GetTimeTickersPaginated(
            Expression<Func<TTimeTicker, bool>> predicate,
            int pageNumber,
            int pageSize,
            CancellationToken cancellationToken)
        {
            using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var dbContext = session.Context;
            var context = dbContext.Set<TTimeTicker>();

            var baseQuery = context
                .Include(x => x.Children)
                .ThenInclude(x => x.Children)
                .AsNoTracking();

            if (predicate != null)
                baseQuery = baseQuery.Where(predicate);

            baseQuery = baseQuery
                .Where(x => x.ParentId == null)
                .OrderByDescending(x => x.ExecutionTime);

            var paginated = await baseQuery.ToPaginatedListAsync(pageNumber, pageSize, cancellationToken).ConfigureAwait(false);
            await ExtendReadChainsBeyondGrandchildrenAsync(context, paginated.Items, cancellationToken).ConfigureAwait(false);
            return paginated;
        }

        public async Task<int> AddTimeTickers(TTimeTicker[] tickers, CancellationToken cancellationToken)
        {
            using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var dbContext = session.Context;

            await dbContext.Set<TTimeTicker>()
                .AddRangeAsync(tickers, cancellationToken);
            
            return await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task<int> UpdateTimeTickers(TTimeTicker[] timeTickers, CancellationToken cancellationToken = default)
        {
            using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var dbContext = session.Context;

            dbContext.Set<TTimeTicker>().UpdateRange(timeTickers);
             
            return await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task<int> RemoveTimeTickers(Guid[] timeTickerIds, CancellationToken cancellationToken)
        {
            using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var dbContext = session.Context;
            var context = dbContext.Set<TTimeTicker>();

            // Self-referencing FK is OnDelete(NoAction), so we must delete descendants
            // before their parents. BFS the tree to collect ids per depth tier, then
            // ExecuteDeleteAsync per level starting from the deepest. Works on every
            // provider (SQL Server / PostgreSQL / MySQL / SQLite) — no tracking,
            // no cartesian load, no EF cascade reliance.
            var levels = new List<List<Guid>> { timeTickerIds.ToList() };
            var frontier = levels[0];
            while (frontier.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var childIds = await context.AsNoTracking()
                    .Where(x => x.ParentId.HasValue && frontier.Contains(x.ParentId.Value))
                    .Select(x => x.Id)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (childIds.Count == 0)
                    break;
                levels.Add(childIds);
                frontier = childIds;
            }

            var total = 0;
            for (var i = levels.Count - 1; i >= 0; i--)
            {
                var levelIds = levels[i];
                if (levelIds.Count == 0)
                    continue;
                total += await context
                    .Where(x => levelIds.Contains(x.Id))
                    .ExecuteDeleteAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            return total;
        }

        public async Task<int> ReplaceTimeTickerChainAsync(Guid oldRootId, TTimeTicker newRoot, CancellationToken cancellationToken = default)
        {
            if (newRoot == null)
                throw new ArgumentNullException(nameof(newRoot));

            using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var dbContext = session.Context;
            var set = dbContext.Set<TTimeTicker>();
            var strategy = dbContext.Database.CreateExecutionStrategy();

            // Single transaction: persist the COMPLETE replacement first, then remove the
            // original. If the insert (validation/constraint) or the delete fails, the whole
            // transaction rolls back and the original aggregate — every node and field —
            // is left exactly as it was. This is the atomicity that a two-call
            // delete-then-create (each with its own session + SaveChanges) cannot provide.
            return await strategy.ExecuteInTransactionAsync(
                operation: async _ =>
                {
                    // AddAsync walks the Children navigation and cascade-inserts the whole tree.
                    await set.AddAsync(newRoot, cancellationToken).ConfigureAwait(false);
                    var inserted = await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                    // Self-referencing FK is OnDelete(NoAction): remove the original aggregate
                    // deepest-first, same tiered BFS as RemoveTimeTickers, inside this transaction.
                    var levels = new List<List<Guid>> { new() { oldRootId } };
                    var frontier = levels[0];
                    while (frontier.Count > 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var childIds = await set.AsNoTracking()
                            .Where(x => x.ParentId.HasValue && frontier.Contains(x.ParentId.Value))
                            .Select(x => x.Id)
                            .ToListAsync(cancellationToken)
                            .ConfigureAwait(false);
                        if (childIds.Count == 0)
                            break;
                        levels.Add(childIds);
                        frontier = childIds;
                    }

                    for (var i = levels.Count - 1; i >= 0; i--)
                    {
                        var levelIds = levels[i];
                        if (levelIds.Count == 0)
                            continue;
                        await set
                            .Where(x => levelIds.Contains(x.Id))
                            .ExecuteDeleteAsync(cancellationToken)
                            .ConfigureAwait(false);
                    }

                    return inserted;
                },
                verifySucceeded: async _ =>
                {
                    var newExists = await set.AsNoTracking()
                        .AnyAsync(x => x.Id == newRoot.Id, CancellationToken.None)
                        .ConfigureAwait(false);
                    var oldGone = !await set.AsNoTracking()
                        .AnyAsync(x => x.Id == oldRootId, CancellationToken.None)
                        .ConfigureAwait(false);
                    return newExists && oldGone;
                },
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        #endregion

        #region Retention

        // Built-in provider: implements bounded, provider-authoritative job retention.
        public bool SupportsRetention => true;

        // Eligibility for retention deletion (shared shape across time tickers and cron occurrences):
        //   terminal status with a configured window AND ExecutedAt strictly older than that window's cutoff
        //   AND not actively owned. A terminal write clears AcquisitionToken but deliberately leaves
        //   LockHolder and the last-renewed LeaseUntil in place, so the authoritative "actively owned"
        //   signals are AcquisitionToken (a live generation) and a still-live LeaseUntil (> now); a stale
        //   LockHolder on a terminal row is NOT an active claim. A null cutoff (window unset) or a null
        //   ExecutedAt yields a NULL comparison in SQL and is therefore excluded.
        private static Expression<Func<TTimeTicker, bool>> TimeEligible(RetentionCutoffs c, DateTime now)
        {
            var succeeded = c.SucceededBefore;
            var failed = c.FailedBefore;
            var cancelled = c.CancelledBefore;
            var skipped = c.SkippedBefore;
            return x =>
                (((x.Status == TickerStatus.Done || x.Status == TickerStatus.DueDone) && x.ExecutedAt < succeeded)
                 || (x.Status == TickerStatus.Failed && x.ExecutedAt < failed)
                 || (x.Status == TickerStatus.Cancelled && x.ExecutedAt < cancelled)
                 || (x.Status == TickerStatus.Skipped && x.ExecutedAt < skipped))
                && x.AcquisitionToken == null
                && (x.LeaseUntil == null || x.LeaseUntil <= now);
        }

        private static Expression<Func<CronTickerOccurrenceEntity<TCronTicker>, bool>> OccurrenceEligible(
            RetentionCutoffs c, DateTime now)
        {
            var succeeded = c.SucceededBefore;
            var failed = c.FailedBefore;
            var cancelled = c.CancelledBefore;
            var skipped = c.SkippedBefore;
            return x =>
                (((x.Status == TickerStatus.Done || x.Status == TickerStatus.DueDone) && x.ExecutedAt < succeeded)
                 || (x.Status == TickerStatus.Failed && x.ExecutedAt < failed)
                 || (x.Status == TickerStatus.Cancelled && x.ExecutedAt < cancelled)
                 || (x.Status == TickerStatus.Skipped && x.ExecutedAt < skipped))
                && x.AcquisitionToken == null
                && (x.LeaseUntil == null || x.LeaseUntil <= now);
        }

        public async Task<RetentionBatchResult> DeleteEligibleCronTickerOccurrencesAsync(
            RetentionCutoffs cutoffs, int batchSize, CancellationToken cancellationToken = default)
        {
            if (batchSize <= 0 || cutoffs is null || !cutoffs.HasAny)
                return RetentionBatchResult.Empty;

            var now = _clock.UtcNow;
            var eligible = OccurrenceEligible(cutoffs, now);

            using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var set = session.Context.Set<CronTickerOccurrenceEntity<TCronTicker>>();

            // Bounded candidate selection; take one extra to detect HasMore without a second scan.
            var ids = await set.AsNoTracking()
                .Where(eligible)
                .OrderBy(x => x.ExecutedAt)
                .Select(x => x.Id)
                .Take(batchSize + 1)
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            var hasMore = ids.Count > batchSize;
            if (hasMore)
                ids.RemoveAt(ids.Count - 1);
            if (ids.Count == 0)
                return new RetentionBatchResult(0, false);

            // Re-apply the eligibility predicate at delete time so a concurrently reactivated occurrence is
            // skipped; a single ExecuteDelete statement is atomic. Cron DEFINITIONS are never referenced.
            var deleted = await set
                .Where(x => ids.Contains(x.Id))
                .Where(eligible)
                .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);

            return new RetentionBatchResult(deleted, hasMore);
        }

        public async Task<RetentionChainBatchResult> DeleteEligibleTimeTickerChainsAsync(
            RetentionCutoffs cutoffs, int batchSize, RetentionCursor cursor, CancellationToken cancellationToken = default)
        {
            if (batchSize <= 0 || cutoffs is null || !cutoffs.HasAny)
                return RetentionChainBatchResult.Empty;

            var now = _clock.UtcNow;
            var eligible = TimeEligible(cutoffs, now);

            using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var dbContext = session.Context;
            var set = dbContext.Set<TTimeTicker>();

            // Candidate ROOTS (ParentId == null) whose own node is eligible — a necessary condition for the
            // whole chain to be deletable — strictly after the keyset cursor, ordered by (ExecutedAt, Id).
            // Bounded by Take; one extra row detects HasMore.
            IQueryable<TTimeTicker> query = set.AsNoTracking()
                .Where(x => x.ParentId == null)
                .Where(eligible);

            if (cursor.HasValue)
            {
                var cExec = cursor.ExecutedAt;
                var cId = cursor.Id;
                query = query.Where(x =>
                    x.ExecutedAt > cExec || (x.ExecutedAt == cExec && x.Id.CompareTo(cId) > 0));
            }

            var candidates = await query
                .OrderBy(x => x.ExecutedAt)
                .ThenBy(x => x.Id)
                .Select(x => new RetentionRootKey { Id = x.Id, ExecutedAt = x.ExecutedAt })
                .Take(batchSize + 1)
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            var hasMore = candidates.Count > batchSize;
            var examineCount = Math.Min(candidates.Count, batchSize);

            var totalDeleted = 0;
            var nextCursor = RetentionCursor.Start; // wrap by default (end of traversal)

            for (var i = 0; i < examineCount; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var root = candidates[i];

                // Advance the cursor past every examined root — deleted OR retained — so a blocked chain is
                // never reselected on the next call and cannot starve later eligible chains.
                if (hasMore && root.ExecutedAt.HasValue)
                    nextCursor = RetentionCursor.After(root.ExecutedAt.Value, root.Id);

                totalDeleted += await DeleteChainIfFullyEligibleAsync(
                        dbContext, set, root.Id, cutoffs.MaxNodesPerChain, eligible, cancellationToken)
                    .ConfigureAwait(false);
            }

            return new RetentionChainBatchResult(totalDeleted, hasMore, nextCursor);
        }

        private sealed class RetentionRootKey
        {
            public Guid Id { get; init; }
            public DateTime? ExecutedAt { get; init; }
        }

        // Deletes the arbitrary-depth chain rooted at <paramref name="rootId"/> only if EVERY node is
        // eligible; otherwise the whole chain is retained. Concurrency behavior (bounded by the database's
        // transaction semantics): the eligibility predicate is re-applied inside the delete transaction and
        // the total deleted count is verified against the collected subtree size — if a node was reactivated
        // between the pre-check and the delete, the transaction rolls back and the chain is left fully intact.
        // A chain is therefore deleted whole or not at all; it is never partially erased.
        //
        // <paramref name="maxNodesPerChain"/> caps traversal: the BFS visits at most cap+1 nodes and stops
        // the instant the subtree is known to exceed the cap, retaining the oversized chain WHOLE and
        // building no delete/query predicate over an unbounded id set. This keeps every `IN (...)` collection
        // (frontier and level filters) bounded by the cap so no provider is handed an oversized SQL IN list.
        private static async Task<int> DeleteChainIfFullyEligibleAsync(
            TDbContext dbContext, DbSet<TTimeTicker> set, Guid rootId, int maxNodesPerChain,
            Expression<Func<TTimeTicker, bool>> eligible, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Fail closed: a non-positive cap cannot even admit the root, so retain the chain whole rather
            // than risk building an unbounded traversal.
            if (maxNodesPerChain < 1)
                return 0;

            // BFS the subtree, collecting ids per depth tier so deletion can proceed deepest-first
            // (the self-referencing FK is OnDelete(NoAction)). Bounded to cap+1 nodes: as soon as the
            // subtree is proven larger than the cap the whole chain is retained, before any delete runs.
            var levels = new List<List<Guid>> { new() { rootId } };
            var frontier = levels[0];
            var subtreeSize = 1;
            while (frontier.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Nodes we may still admit before reaching the cap; take one extra to detect overflow.
                var remaining = maxNodesPerChain - subtreeSize;
                var childIds = await set.AsNoTracking()
                    .Where(x => x.ParentId.HasValue && frontier.Contains(x.ParentId.Value))
                    .Select(x => x.Id)
                    .Take(remaining + 1)
                    .ToListAsync(cancellationToken).ConfigureAwait(false);
                if (childIds.Count == 0)
                    break;
                subtreeSize += childIds.Count;
                if (subtreeSize > maxNodesPerChain)
                    return 0; // oversized chain → retain whole; no delete/query predicate is built for it
                levels.Add(childIds);
                frontier = childIds;
            }

            cancellationToken.ThrowIfCancellationRequested();
            var allIds = levels.SelectMany(l => l).ToList();

            // Cheap pre-check outside any transaction: is EVERY node eligible? Short-circuits the common
            // "not fully eligible" case without opening a transaction that would only roll back.
            var eligibleCount = await set.AsNoTracking()
                .Where(x => allIds.Contains(x.Id))
                .Where(eligible)
                .CountAsync(cancellationToken).ConfigureAwait(false);
            if (eligibleCount != subtreeSize)
                return 0; // any node ineligible → retain the whole chain

            var strategy = dbContext.Database.CreateExecutionStrategy();
            try
            {
                return await strategy.ExecuteInTransactionAsync(
                    operation: async _ =>
                    {
                        var deleted = 0;
                        for (var i = levels.Count - 1; i >= 0; i--)
                        {
                            var levelIds = levels[i];
                            if (levelIds.Count == 0)
                                continue;
                            deleted += await set
                                .Where(x => levelIds.Contains(x.Id))
                                .Where(eligible)
                                .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
                        }

                        // All-or-nothing: a mismatch means a node was reactivated/removed concurrently.
                        if (deleted != subtreeSize)
                            throw new RetentionChainConcurrentlyModifiedException();

                        return deleted;
                    },
                    verifySucceeded: async _ =>
                        !await set.AsNoTracking().AnyAsync(x => x.Id == rootId, CancellationToken.None)
                            .ConfigureAwait(false),
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (RetentionChainConcurrentlyModifiedException)
            {
                // The transaction rolled back; the chain is fully retained. Skip it this sweep.
                return 0;
            }
        }

        private sealed class RetentionChainConcurrentlyModifiedException : Exception
        {
        }

        #endregion

        #region Cron_Ticker_Implementations

        public async Task<TCronTicker> GetCronTickerById(Guid id, CancellationToken cancellationToken)
        {
            using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var dbContext = session.Context;
            return await dbContext.Set<TCronTicker>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken).ConfigureAwait(false);;
        }

        public async Task<TCronTicker[]> GetCronTickers(Expression<Func<TCronTicker, bool>> predicate,
            CancellationToken cancellationToken)
        {
            using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var dbContext = session.Context;

            var baseQuery = dbContext.Set<TCronTicker>()
                .AsNoTracking();
            
            if (predicate != null)
                baseQuery = baseQuery.Where(predicate);
            
            return await baseQuery
                .OrderByDescending(x => x.CreatedAt)
                .ToArrayAsync(cancellationToken).ConfigureAwait(false);
        }
        
        public async Task<PaginationResult<TCronTicker>> GetCronTickersPaginated(
            Expression<Func<TCronTicker, bool>> predicate, 
            int pageNumber, 
            int pageSize, 
            CancellationToken cancellationToken)
        {
            using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var dbContext = session.Context;

            var baseQuery = dbContext.Set<TCronTicker>()
                .AsNoTracking();
            
            if (predicate != null)
                baseQuery = baseQuery.Where(predicate);
            
            baseQuery = baseQuery.OrderByDescending(x => x.CreatedAt);
            
            return await baseQuery.ToPaginatedListAsync(pageNumber, pageSize, cancellationToken).ConfigureAwait(false);
        }

        public async Task<int> InsertCronTickers(TCronTicker[] tickers, CancellationToken cancellationToken)
        {
            using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var dbContext = session.Context;

            await dbContext.Set<TCronTicker>().AddRangeAsync(tickers, cancellationToken).ConfigureAwait(false);
            
            var result = await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            
            if(RedisContext.HasRedisConnection)
                await RedisContext.DistributedCache.RemoveAsync("cron:expressions", cancellationToken).ConfigureAwait(false);
            
            return result;
        }

        public async Task<int> UpdateCronTickers(TCronTicker[] cronTickers, CancellationToken cancellationToken = default)
        {
            using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var dbContext = session.Context;

            dbContext.Set<TCronTicker>().UpdateRange(cronTickers);

            var result =  await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            
            if(RedisContext.HasRedisConnection)
                await RedisContext.DistributedCache.RemoveAsync("cron:expressions", cancellationToken).ConfigureAwait(false);
            
            return result;
        }

        public async Task<int> RemoveCronTickers(Guid[] cronTickerIds, CancellationToken cancellationToken)
        {
            using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var dbContext = session.Context;
            var idList = cronTickerIds.ToList();
            var result = await dbContext.Set<TCronTicker>().Where(x => idList.Contains(x.Id))
                .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            
            if(RedisContext.HasRedisConnection)
                await RedisContext.DistributedCache.RemoveAsync("cron:expressions", cancellationToken).ConfigureAwait(false);
            
            return result;
        }

        #endregion

        #region Cron_TickerOccurrence_Implementations
        public async Task<CronTickerOccurrenceEntity<TCronTicker>[]> GetAllCronTickerOccurrences(Expression<Func<CronTickerOccurrenceEntity<TCronTicker>, bool>> predicate, CancellationToken cancellationToken = default)
        {
            using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var dbContext = session.Context;
            var cronTickerOccurrenceContext = dbContext.Set<CronTickerOccurrenceEntity<TCronTicker>>()
                .AsNoTracking();

            var query = predicate == null
                ? cronTickerOccurrenceContext.Include(x => x.CronTicker)
                : cronTickerOccurrenceContext.Include(x => x.CronTicker).Where(predicate);
            
            return await query.OrderByDescending(x => x.ExecutionTime).ToArrayAsync(cancellationToken).ConfigureAwait(false);
        }
        
        public async Task<PaginationResult<CronTickerOccurrenceEntity<TCronTicker>>> GetAllCronTickerOccurrencesPaginated(
            Expression<Func<CronTickerOccurrenceEntity<TCronTicker>, bool>> predicate, 
            int pageNumber, 
            int pageSize, 
            CancellationToken cancellationToken)
        {
            using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var dbContext = session.Context;

            var baseQuery = dbContext.Set<CronTickerOccurrenceEntity<TCronTicker>>()
                .Include(x => x.CronTicker)
                .AsNoTracking();

            if (predicate != null)
                baseQuery = baseQuery.Where(predicate);
            
            baseQuery = baseQuery.OrderByDescending(x => x.ExecutionTime);
            
            return await baseQuery.ToPaginatedListAsync(pageNumber, pageSize, cancellationToken).ConfigureAwait(false);
        }

        public async Task<int> InsertCronTickerOccurrences(CronTickerOccurrenceEntity<TCronTicker>[] cronTickerOccurrences, CancellationToken cancellationToken = default)
        {
            using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var dbContext = session.Context;

            await dbContext.Set<CronTickerOccurrenceEntity<TCronTicker>>().AddRangeAsync(cronTickerOccurrences, cancellationToken).ConfigureAwait(false);

            return await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task<int> RemoveCronTickerOccurrences(Guid[] cronTickerOccurrences, CancellationToken cancellationToken = default)
        {
            using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var dbContext = session.Context;
            var idList = cronTickerOccurrences.ToList();
            return await dbContext.Set<CronTickerOccurrenceEntity<TCronTicker>>()
                .Where(x => idList.Contains(x.Id))
                .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task<CronTickerOccurrenceEntity<TCronTicker>[]> AcquireImmediateCronOccurrencesAsync(Guid[] occurrenceIds, CancellationToken cancellationToken = default)
        {
            if (occurrenceIds == null || occurrenceIds.Length == 0)
                return Array.Empty<CronTickerOccurrenceEntity<TCronTicker>>();

            using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var dbContext = session.Context;
            var now = _clock.UtcNow;
            var acquisitionToken = Guid.NewGuid();
            var idList = occurrenceIds.ToList();

            // Only acquire occurrences that are acquirable (Idle/Queued and not locked by another node)
            var query = dbContext.Set<CronTickerOccurrenceEntity<TCronTicker>>()
                .Where(x => idList.Contains(x.Id))
                .WhereCanAcquire(_lockHolder);

            // Lock and mark InProgress
            var affected = await query
                .ExecuteUpdateAsync(setter => setter
                    .SetProperty(x => x.LockHolder, _lockHolder)
                    .SetProperty(x => x.LockedAt, now)
                    .SetProperty(x => x.LeaseUntil, NextLeaseUntil(now))
                    .SetProperty(x => x.AcquisitionToken, acquisitionToken)
                    .SetProperty(x => x.Status, TickerStatus.InProgress)
                    .SetProperty(x => x.UpdatedAt, now), cancellationToken)
                .ConfigureAwait(false);

            if (affected == 0)
                return Array.Empty<CronTickerOccurrenceEntity<TCronTicker>>();

            // Return acquired occurrences with CronTicker populated
            return await dbContext.Set<CronTickerOccurrenceEntity<TCronTicker>>()
                .AsNoTracking()
                .Where(x => idList.Contains(x.Id) && x.LockHolder == _lockHolder &&
                            x.Status == TickerStatus.InProgress && x.AcquisitionToken == acquisitionToken)
                .Include(x => x.CronTicker)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        #endregion
    }
}
