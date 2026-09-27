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
                }, query => query.Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey));
        }

        public ITickerQueryable<TCronTicker> CronTickersQuery()
        {
            return new EfTickerQueryable<TDbContext, TCronTicker>(
                _serviceProvider, pipeline: query =>
                    query.Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey));
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
                }, query => query.Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey));
        }

        #endregion

        #region Time_Ticker_Implementations

        public bool SupportsTimeTickerChainRepair => true;

        public async Task<TimeTickerChainRepairResult> RepairTimeTickerChainsAsync(
            CancellationToken cancellationToken = default)
        {
            return await ExecuteTimeTickerGraphMutationAsync(false, TimeTickerChainRepairResult.Empty,
                async (dbContext, ct) =>
                {
                    var rows = await dbContext.Set<TTimeTicker>()
                        .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey)
                        .ToArrayAsync(ct).ConfigureAwait(false);
                    var plan = TimeTickerChainRepairPlanner.Create(rows, ct);
                    plan.ThrowIfMalformed();
                    foreach (var update in plan.Updates)
                    {
                        update.Row.ChainRootId = update.ChainRootId;
                        update.Row.ChainGeneration = update.ChainGeneration;
                        update.Row.UpdatedAt = _clock.UtcNow;
                    }
                    if (plan.Updates.Count > 0)
                        await dbContext.SaveChangesAsync(ct).ConfigureAwait(false);
                    return plan.Result;
                }, cancellationToken).ConfigureAwait(false);
        }

        public async Task<TTimeTicker> GetTimeTickerById(Guid id, CancellationToken cancellationToken = default)
        {
            using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var dbContext = session.Context;
            var context = dbContext.Set<TTimeTicker>();
            var entity = await context
                .AsNoTracking()
                .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey)
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
                .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey)
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
                .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey)
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
            return await ExecuteTimeTickerGraphMutationAsync(true, 0, async (dbContext, ct) =>
                {
                    var suppliedById = tickers.ToDictionary(x => x.Id);
                    foreach (var ticker in tickers)
                    {
                        var rootId = await ResolveChainRootForInsertAsync(
                            dbContext.Set<TTimeTicker>(), ticker, suppliedById, ct).ConfigureAwait(false);
                        NormalizeChainRoot(ticker, rootId);
                    }
                    await dbContext.Set<TTimeTicker>().AddRangeAsync(tickers, ct).ConfigureAwait(false);
                    return await dbContext.SaveChangesAsync(ct).ConfigureAwait(false);
                }, cancellationToken).ConfigureAwait(false);
        }

        public async Task<int> UpdateTimeTickers(TTimeTicker[] timeTickers, CancellationToken cancellationToken = default)
        {
            var publishesRunnable = timeTickers.Any(x => x.Status is TickerStatus.Idle or TickerStatus.Queued);
            return await ExecuteTimeTickerGraphMutationAsync(publishesRunnable, 0,
                async (dbContext, ct) =>
                {
                    foreach (var ticker in timeTickers.Where(x => x.ParentId == null))
                        NormalizeChainRoot(ticker, ticker.Id);
                    foreach (var ticker in timeTickers.Where(x => x.Status == TickerStatus.Idle))
                    {
                        if (ticker.ParentId == null)
                        {
                            if (!ticker.AcquisitionToken.HasValue)
                                return 0;
                            var lockedRoot = await dbContext.Set<TTimeTicker>()
                                .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey &&
                                            x.Id == ticker.Id && x.ParentId == null &&
                                            x.LockHolder == _lockHolder &&
                                            x.AcquisitionToken == ticker.AcquisitionToken)
                                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Id, x => x.Id), ct)
                                .ConfigureAwait(false);
                            if (lockedRoot != 1)
                                return 0;
                            continue;
                        }

                        if (!ticker.ChainRootId.HasValue || !ticker.ChainGeneration.HasValue)
                            return 0;
                        var rootId = ticker.ChainRootId.Value;
                        var generation = ticker.ChainGeneration.Value;
                        var lockedGeneration = await dbContext.Set<TTimeTicker>()
                            .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey &&
                                        x.Id == rootId && x.ParentId == null &&
                                        x.ChainRootId == x.Id && x.ChainGeneration == generation)
                            .ExecuteUpdateAsync(setters => setters
                                .SetProperty(x => x.ChainGeneration, generation), ct)
                            .ConfigureAwait(false);
                        if (lockedGeneration != 1)
                            return 0;
                        var fencedChild = await dbContext.Set<TTimeTicker>()
                            .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey &&
                                        x.Id == ticker.Id && x.ParentId != null &&
                                        x.ChainRootId == rootId && x.ChainGeneration == generation)
                            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Id, x => x.Id), ct)
                            .ConfigureAwait(false);
                        if (fencedChild != 1)
                            return 0;
                    }
                    // The serializable graph transaction and authoritative root-generation write lock
                    // above fence every Idle lifecycle write before tracked full-row persistence.
                    dbContext.Set<TTimeTicker>().UpdateRange(timeTickers);
                    return await dbContext.SaveChangesAsync(ct).ConfigureAwait(false);
                }, cancellationToken).ConfigureAwait(false);
        }

        public async Task<int> RemoveTimeTickers(Guid[] timeTickerIds, CancellationToken cancellationToken)
        {
            return await ExecuteTimeTickerGraphMutationAsync(false, 0,
                (dbContext, ct) => DeleteTimeTickerTreesAsync(
                    dbContext.Set<TTimeTicker>(), timeTickerIds, ct),
                cancellationToken).ConfigureAwait(false);
        }

        public async Task<int> ReplaceTimeTickerChainAsync(Guid oldRootId, TTimeTicker newRoot, CancellationToken cancellationToken = default)
        {
            if (newRoot == null)
                throw new ArgumentNullException(nameof(newRoot));

            NormalizeChainRoot(newRoot, newRoot.Id);

            // Runnable replacement participates in the exact same serializable activation boundary
            // as every other runnable publication. The replacement aggregate is inserted completely
            // before the old aggregate is removed, and both changes commit with the activation lock.
            return await ExecuteTimeTickerGraphMutationAsync(true, 0, async (dbContext, ct) =>
                {
                    var set = dbContext.Set<TTimeTicker>();
                    // AddAsync walks the Children navigation and cascade-inserts the whole tree.
                    await set.AddAsync(newRoot, ct).ConfigureAwait(false);
                    var inserted = await dbContext.SaveChangesAsync(ct).ConfigureAwait(false);
                    await DeleteTimeTickerTreesAsync(set, [oldRootId], ct).ConfigureAwait(false);
                    return inserted;
                }, cancellationToken).ConfigureAwait(false);
        }

        private async Task<int> DeleteTimeTickerTreesAsync(
            DbSet<TTimeTicker> set, IReadOnlyCollection<Guid> rootIds, CancellationToken cancellationToken)
        {
            // Self-referencing FK is OnDelete(NoAction), so descendants are deleted deepest-first.
            var levels = new List<List<Guid>> { rootIds.Distinct().ToList() };
            var frontier = levels[0];
            while (frontier.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var childIds = await set.AsNoTracking()
                    .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey &&
                                x.ParentId.HasValue && frontier.Contains(x.ParentId.Value))
                    .Select(x => x.Id)
                    .ToListAsync(cancellationToken).ConfigureAwait(false);
                if (childIds.Count == 0) break;
                levels.Add(childIds);
                frontier = childIds;
            }

            var total = 0;
            for (var i = levels.Count - 1; i >= 0; i--)
            {
                var ids = levels[i];
                if (ids.Count > 0)
                    total += await set.Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey && ids.Contains(x.Id))
                        .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            }
            return total;
        }

        private static void NormalizeChainRoot(TTimeTicker node, Guid rootId)
        {
            node.ChainRootId = rootId;
            if (node.Children == null) return;
            foreach (var child in node.Children)
                NormalizeChainRoot(child, rootId);
        }

        private async Task<Guid> ResolveChainRootForInsertAsync(
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
                    .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey &&
                                x.Id == current.ParentId.Value)
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
                .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey)
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
                .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey && ids.Contains(x.Id))
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
                .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey && x.ParentId == null)
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
                        root.Id, cutoffs.MaxNodesPerChain, eligible, cancellationToken)
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
            Guid rootId, int maxNodesPerChain,
            Expression<Func<TTimeTicker, bool>> eligible, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Fail closed: a non-positive cap cannot even admit the root, so retain the chain whole rather
            // than risk building an unbounded traversal.
            if (maxNodesPerChain < 1)
                return 0;

            using var strategySession = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var strategy = strategySession.Context.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                using var attemptSession = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
                var dbContext = attemptSession.Context;
                var set = dbContext.Set<TTimeTicker>();
                await using var transaction = await dbContext.Database
                    .BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
                    .ConfigureAwait(false);
                await LockTimeTickerGraphMutationAsync(dbContext, cancellationToken).ConfigureAwait(false);

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
                        .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey &&
                                    x.ParentId.HasValue && frontier.Contains(x.ParentId.Value))
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
                    .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey && idList.Contains(x.Id))
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
                    .AnyAsync(x => x.ApplicationNamespaceKey == _runtimePartitionKey &&
                                   x.Id == rootId && x.ParentId == null, cancellationToken)
                    .ConfigureAwait(false);
                var hasPhantomChild = await set.AsNoTracking()
                    .AnyAsync(x => x.ApplicationNamespaceKey == _runtimePartitionKey &&
                                   x.ParentId.HasValue && idList.Contains(x.ParentId.Value) && !idList.Contains(x.Id),
                        cancellationToken).ConfigureAwait(false);
                var currentEdges = await set.AsNoTracking()
                    .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey && idList.Contains(x.Id))
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
                        .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey && levelIds.Contains(x.Id))
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
            return await dbContext.Set<TCronTicker>().AsNoTracking().FirstOrDefaultAsync(
                x => x.ApplicationNamespaceKey == _runtimePartitionKey && x.Id == id, cancellationToken).ConfigureAwait(false);;
        }

        public async Task<TCronTicker[]> GetCronTickers(Expression<Func<TCronTicker, bool>> predicate,
            CancellationToken cancellationToken)
        {
            using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var dbContext = session.Context;

            var baseQuery = dbContext.Set<TCronTicker>()
                .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey)
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
                .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey)
                .AsNoTracking();
            
            if (predicate != null)
                baseQuery = baseQuery.Where(predicate);
            
            baseQuery = baseQuery.OrderByDescending(x => x.CreatedAt);
            
            return await baseQuery.ToPaginatedListAsync(pageNumber, pageSize, cancellationToken).ConfigureAwait(false);
        }

        public async Task<int> InsertCronTickers(TCronTicker[] tickers, CancellationToken cancellationToken)
        {
            var result = await ExecuteRunnableAdmissionAsync(0, async (dbContext, ct) =>
            {
                foreach (var ticker in tickers)
                    if (ticker.DefinitionRevision <= 0)
                        ticker.DefinitionRevision = 1;
                await dbContext.Set<TCronTicker>().AddRangeAsync(tickers, ct).ConfigureAwait(false);
                return await dbContext.SaveChangesAsync(ct).ConfigureAwait(false);
            }, cancellationToken).ConfigureAwait(false);
            
            if(RedisContext.HasRedisConnection)
                await RedisContext.DistributedCache.RemoveAsync("cron:expressions", cancellationToken).ConfigureAwait(false);
            
            return result;
        }

        public async Task<int> UpdateCronTickers(TCronTicker[] cronTickers, CancellationToken cancellationToken = default)
        {
            var result = await ExecuteRunnableAdmissionAsync(0, async (dbContext, ct) =>
            {
                var ids = cronTickers.Select(x => x.Id).Distinct().ToList();
                var currentRows = await dbContext.Set<TCronTicker>()
                    .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey && ids.Contains(x.Id))
                    .ToDictionaryAsync(x => x.Id, ct)
                    .ConfigureAwait(false);
                var now = _clock.UtcNow;
                foreach (var ticker in cronTickers)
                {
                    if (!currentRows.TryGetValue(ticker.Id, out var current))
                        continue;
                    var priorRevision = current.DefinitionRevision;
                    dbContext.Entry(current).CurrentValues.SetValues(ticker);
                    current.DefinitionRevision = Math.Max(1, priorRevision + 1);
                    ticker.DefinitionRevision = current.DefinitionRevision;
                    ticker.UpdatedAt = now;
                    current.UpdatedAt = now;
                    await dbContext.Set<CronTickerOccurrenceEntity<TCronTicker>>()
                        .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey &&
                                    x.CronTickerId == ticker.Id &&
                                    x.DefinitionRevision == priorRevision &&
                                    (x.Status == TickerStatus.Idle || x.Status == TickerStatus.Queued) &&
                                    x.LockHolder == null)
                        .ExecuteUpdateAsync(setters => setters
                            .SetProperty(x => x.Status, TickerStatus.Skipped)
                            .SetProperty(x => x.SkippedReason,
                                "Quarantined because its Cron definition revision is stale.")
                            .SetProperty(x => x.ExecutedAt, x => x.ExecutedAt ?? now)
                            .SetProperty(x => x.UpdatedAt, now), ct)
                        .ConfigureAwait(false);
                }
                return await dbContext.SaveChangesAsync(ct).ConfigureAwait(false);
            }, cancellationToken).ConfigureAwait(false);
            
            if(RedisContext.HasRedisConnection)
                await RedisContext.DistributedCache.RemoveAsync("cron:expressions", cancellationToken).ConfigureAwait(false);
            
            return result;
        }

        public async Task<int> RemoveCronTickers(Guid[] cronTickerIds, CancellationToken cancellationToken)
        {
            var result = await ExecuteRunnableAdmissionAsync(0, async (dbContext, ct) =>
            {
                var idList = cronTickerIds.ToList();
                return await dbContext.Set<TCronTicker>().Where(
                        x => x.ApplicationNamespaceKey == _runtimePartitionKey && idList.Contains(x.Id))
                    .ExecuteDeleteAsync(ct).ConfigureAwait(false);
            }, cancellationToken).ConfigureAwait(false);
            
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
                .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey)
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
                .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey)
                .Include(x => x.CronTicker)
                .AsNoTracking();

            if (predicate != null)
                baseQuery = baseQuery.Where(predicate);
            
            baseQuery = baseQuery.OrderByDescending(x => x.ExecutionTime);
            
            return await baseQuery.ToPaginatedListAsync(pageNumber, pageSize, cancellationToken).ConfigureAwait(false);
        }

        public async Task<int> InsertCronTickerOccurrences(CronTickerOccurrenceEntity<TCronTicker>[] cronTickerOccurrences, CancellationToken cancellationToken = default)
        {
            return await ExecuteRunnableAdmissionAsync(0, async (dbContext, ct) =>
            {
            var cronIds = cronTickerOccurrences.Select(x => x.CronTickerId).Distinct().ToList();
            var revisions = await dbContext.Set<TCronTicker>()
                .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey && cronIds.Contains(x.Id))
                .Select(x => new { x.Id, x.DefinitionRevision })
                .ToDictionaryAsync(x => x.Id, x => x.DefinitionRevision, ct)
                .ConfigureAwait(false);
            // Convert the parent read into a write fence in this same serializable transaction.
            // A concurrent semantic publication must serialize before this point (and be observed)
            // or after the occurrence insert; it cannot publish between validation and insertion.
            foreach (var revision in revisions)
            {
                var locked = await dbContext.Set<TCronTicker>()
                    .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey &&
                                x.Id == revision.Key && x.DefinitionRevision == revision.Value)
                    .ExecuteUpdateAsync(setter => setter
                        .SetProperty(x => x.DefinitionRevision, revision.Value), ct)
                    .ConfigureAwait(false);
                if (locked != 1)
                    throw new DbUpdateConcurrencyException(
                        $"Cron definition '{revision.Key}' changed while creating an occurrence.");
            }

            var now = _clock.UtcNow;
            foreach (var occurrence in cronTickerOccurrences)
            {
                if (!revisions.TryGetValue(occurrence.CronTickerId, out var revision))
                    continue;
                if (occurrence.DefinitionRevision <= 0)
                    occurrence.DefinitionRevision = revision;
                if (occurrence.DefinitionRevision != revision &&
                    occurrence.Status is TickerStatus.Idle or TickerStatus.Queued)
                {
                    occurrence.Status = TickerStatus.Skipped;
                    occurrence.SkippedReason = "Quarantined because its Cron definition revision is stale.";
                    occurrence.ExecutedAt ??= now;
                    occurrence.LockHolder = null;
                    occurrence.LockedAt = null;
                    occurrence.LeaseUntil = null;
                    occurrence.AcquisitionToken = null;
                    occurrence.UpdatedAt = now;
                }
            }

            await dbContext.Set<CronTickerOccurrenceEntity<TCronTicker>>().AddRangeAsync(cronTickerOccurrences, ct).ConfigureAwait(false);

            return await dbContext.SaveChangesAsync(ct).ConfigureAwait(false);
            }, cancellationToken).ConfigureAwait(false);
        }

        public async Task<int> RemoveCronTickerOccurrences(Guid[] cronTickerOccurrences, CancellationToken cancellationToken = default)
        {
            using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var dbContext = session.Context;
            var idList = cronTickerOccurrences.ToList();
            return await dbContext.Set<CronTickerOccurrenceEntity<TCronTicker>>()
                .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey && idList.Contains(x.Id))
                .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task<CronTickerOccurrenceEntity<TCronTicker>[]> AcquireImmediateCronOccurrencesAsync(Guid[] occurrenceIds, CancellationToken cancellationToken = default)
        {
            if (occurrenceIds == null || occurrenceIds.Length == 0)
                return Array.Empty<CronTickerOccurrenceEntity<TCronTicker>>();

            return await ExecuteRunnableAdmissionAsync(
                Array.Empty<CronTickerOccurrenceEntity<TCronTicker>>(), async (dbContext, ct) =>
            {
                var now = _clock.UtcNow;
                var acquisitionToken = Guid.NewGuid();
                var idList = occurrenceIds.ToList();
                var affected = await dbContext.Set<CronTickerOccurrenceEntity<TCronTicker>>()
                .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey && idList.Contains(x.Id))
                .Where(x => x.DefinitionRevision == x.CronTicker.DefinitionRevision)
                .WhereCanAcquire(_lockHolder)
                .ExecuteUpdateAsync(setter => setter
                    .SetProperty(x => x.LockHolder, _lockHolder)
                    .SetProperty(x => x.LockedAt, now)
                    .SetProperty(x => x.LeaseUntil, NextLeaseUntil(now))
                    .SetProperty(x => x.AcquisitionToken, acquisitionToken)
                    .SetProperty(x => x.Status, TickerStatus.InProgress)
                    .SetProperty(x => x.UpdatedAt, now), ct)
                .ConfigureAwait(false);

                if (affected == 0)
                    return Array.Empty<CronTickerOccurrenceEntity<TCronTicker>>();

                return await dbContext.Set<CronTickerOccurrenceEntity<TCronTicker>>()
                .AsNoTracking()
                .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey &&
                            idList.Contains(x.Id) && x.LockHolder == _lockHolder &&
                            x.Status == TickerStatus.InProgress && x.AcquisitionToken == acquisitionToken)
                .Include(x => x.CronTicker)
                .ToArrayAsync(ct)
                .ConfigureAwait(false);
            }, cancellationToken).ConfigureAwait(false);
        }

        #endregion
    }
}
