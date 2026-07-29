using Microsoft.EntityFrameworkCore;

using System;
using System.Data;
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

            var suppliedById = tickers.ToDictionary(x => x.Id);
            foreach (var ticker in tickers)
            {
                var rootId = await ResolveChainRootForInsertAsync(
                    dbContext.Set<TTimeTicker>(), ticker, suppliedById, cancellationToken).ConfigureAwait(false);
                NormalizeChainRoot(ticker, rootId);
            }

            await dbContext.Set<TTimeTicker>()
                .AddRangeAsync(tickers, cancellationToken);
            
            return await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task<int> UpdateTimeTickers(TTimeTicker[] timeTickers, CancellationToken cancellationToken = default)
        {
            using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var dbContext = session.Context;

            foreach (var ticker in timeTickers.Where(x => x.ParentId == null))
                NormalizeChainRoot(ticker, ticker.Id);
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

            NormalizeChainRoot(newRoot, newRoot.Id);

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

        private static void NormalizeChainRoot(TTimeTicker node, Guid rootId)
        {
            node.ChainRootId = rootId;
            if (node.Children == null) return;
            foreach (var child in node.Children)
                NormalizeChainRoot(child, rootId);
        }

        private static async Task<Guid> ResolveChainRootForInsertAsync(
            DbSet<TTimeTicker> set, TTimeTicker ticker,
            IReadOnlyDictionary<Guid, TTimeTicker> suppliedById, CancellationToken cancellationToken)
        {
            var current = ticker;
            var visited = new HashSet<Guid>();
            while (current.ParentId.HasValue)
            {
                if (!visited.Add(current.Id))
                    throw new InvalidOperationException("Cyclic time ticker parent chain detected.");

                if (suppliedById.TryGetValue(current.ParentId.Value, out var suppliedParent))
                {
                    current = suppliedParent;
                    continue;
                }

                var persistedParent = await set.AsNoTracking()
                    .Where(x => x.Id == current.ParentId.Value)
                    .Select(x => new { x.Id, x.ChainRootId })
                    .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
                if (persistedParent == null)
                    throw new InvalidOperationException(
                        $"Time ticker '{current.Id}' references missing parent '{current.ParentId.Value}'.");

                return persistedParent.ChainRootId ?? persistedParent.Id;
            }

            if (!visited.Add(current.Id))
                throw new InvalidOperationException("Cyclic time ticker parent chain detected.");
            return current.Id;
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
        // eligible; otherwise the whole chain is retained. Subtree discovery, topology/eligibility
        // verification, and the whole-chain delete are performed as ONE serializable transaction so a
        // concurrent supported graph mutation (reparent via UpdateTimeTickers, or a phantom insert via
        // AddTimeTickers/UpdateTimeTickers) races cleanly: either the mutation commits first and the
        // re-read inside the transaction observes the new topology (retaining the chain), or retention
        // commits first and the mutation fails/retries against the deleted rows — never an orphaned or
        // lost adopted child.
        //
        // Three guards make the atomicity provider-robust rather than relying on isolation alone:
        //   1. The subtree is re-discovered INSIDE the transaction (not from a pre-transaction snapshot).
        //   2. Each delete revalidates the EXACT parent edge (root: ParentId == null; descendant:
        //      ParentId still points into the discovered set) so a child reparented OUT of the chain is
        //      left intact; the total deleted count is verified against the subtree size (all-or-nothing).
        //   3. A phantom guard rejects the chain if any row OUTSIDE the discovered set references a
        //      discovered node as its parent (a child inserted/reparented INTO the chain in the window),
        //      which would otherwise be orphaned by the delete.
        //
        // <paramref name="maxNodesPerChain"/> caps traversal: the BFS visits at most cap+1 nodes and stops
        // the instant the subtree is known to exceed the cap, retaining the oversized chain WHOLE and
        // building no delete/query predicate over an unbounded id set. This keeps every `IN (...)` collection
        // (frontier and level filters) bounded by the cap so no provider is handed an oversized SQL IN list.
        private async Task<int> DeleteChainIfFullyEligibleAsync(
            TDbContext dbContext, DbSet<TTimeTicker> set, Guid rootId, int maxNodesPerChain,
            Expression<Func<TTimeTicker, bool>> eligible, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Fail closed: a non-positive cap cannot even admit the root, so retain the chain whole rather
            // than risk building an unbounded traversal.
            if (maxNodesPerChain < 1)
                return 0;

            var strategy = dbContext.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await dbContext.Database
                    .BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
                    .ConfigureAwait(false);

                // BFS the subtree INSIDE the transaction, collecting ids per depth tier so deletion can
                // proceed deepest-first (the self-referencing FK is OnDelete(NoAction)). Bounded to cap+1
                // nodes: as soon as the subtree exceeds the cap the whole chain is retained before any delete.
                var levels = new List<List<Guid>> { new() { rootId } };
                var allIds = new HashSet<Guid> { rootId };
                var expectedParents = new Dictionary<Guid, Guid?> { [rootId] = null };
                var frontier = levels[0];
                var subtreeSize = 1;
                while (frontier.Count > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // Nodes we may still admit before reaching the cap; take one extra to detect overflow.
                    var remaining = maxNodesPerChain - subtreeSize;
                    var children = await set.AsNoTracking()
                        .Where(x => x.ParentId.HasValue && frontier.Contains(x.ParentId.Value))
                        .Select(x => new { x.Id, x.ParentId })
                        .Take(remaining + 1)
                        .ToListAsync(cancellationToken).ConfigureAwait(false);
                    var childIds = children.Select(x => x.Id).ToList();
                    if (childIds.Count == 0)
                        break;
                    subtreeSize += childIds.Count;
                    if (subtreeSize > maxNodesPerChain)
                    {
                        await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                        return 0; // oversized chain → retain whole; no delete predicate is built for it
                    }
                    levels.Add(childIds);
                    frontier = childIds;
                    foreach (var child in children)
                    {
                        allIds.Add(child.Id);
                        expectedParents[child.Id] = child.ParentId;
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();
                var idList = allIds.ToList();

                // Every node must still be eligible (unowned, past cutoff) under this transaction.
                var eligibleCount = await set.AsNoTracking()
                    .Where(x => idList.Contains(x.Id))
                    .Where(eligible)
                    .CountAsync(cancellationToken).ConfigureAwait(false);
                if (eligibleCount != subtreeSize)
                {
                    await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                    return 0; // any node ineligible → retain the whole chain
                }

                // Deterministic test seam: interleave a concurrent graph mutation in the race window.
                await OnChainDiscoveredForTestAsync(dbContext, rootId, idList, cancellationToken).ConfigureAwait(false);

                // Phantom guard: reject if any row outside the discovered set now points at a discovered
                // node as its parent — a child inserted/reparented INTO the chain in the window that the
                // deepest-first delete would otherwise orphan. Also reconfirm the root is still a root.
                var rootStillRoot = await set.AsNoTracking()
                    .AnyAsync(x => x.Id == rootId && x.ParentId == null, cancellationToken)
                    .ConfigureAwait(false);
                var hasPhantomChild = await set.AsNoTracking()
                    .AnyAsync(x => x.ParentId.HasValue && idList.Contains(x.ParentId.Value) && !idList.Contains(x.Id),
                        cancellationToken).ConfigureAwait(false);
                var currentEdges = await set.AsNoTracking()
                    .Where(x => idList.Contains(x.Id))
                    .Select(x => new { x.Id, x.ParentId })
                    .ToListAsync(cancellationToken).ConfigureAwait(false);
                var exactEdgesStillMatch = currentEdges.Count == subtreeSize
                    && currentEdges.All(x => expectedParents.TryGetValue(x.Id, out var parentId)
                        && x.ParentId == parentId);
                if (!rootStillRoot || hasPhantomChild || !exactEdgesStillMatch)
                {
                    await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                    return 0; // topology changed under us → retain the whole chain
                }

                // Delete deepest-first, revalidating BOTH eligibility AND the exact parent edge so a node
                // reparented OUT of this chain (ParentId no longer points into the set) is left intact.
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

                // All-or-nothing: a mismatch means a node was reparented/reactivated/removed concurrently.
                if (deleted != subtreeSize)
                {
                    await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                    return 0;
                }

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return deleted;
            }).ConfigureAwait(false);
        }

        // Test seam: overridden in tests to deterministically interleave a concurrent graph mutation
        // (reparent / phantom insert) at the exact point between subtree discovery+validation and the
        // destructive chain delete. No-op in production.
        protected internal virtual Task OnChainDiscoveredForTestAsync(
            TDbContext dbContext, Guid rootId, IReadOnlyCollection<Guid> discoveredIds, CancellationToken cancellationToken)
            => Task.CompletedTask;

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
