using Microsoft.EntityFrameworkCore;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

using TickerQ.EntityFrameworkCore.DbContextFactory;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ.EntityFrameworkCore.Infrastructure;

internal abstract class BasePersistenceProvider<TDbContext, TTimeTicker, TCronTicker>
    where TDbContext : DbContext
    where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
    where TCronTicker : CronTickerEntity, new()
{
    public BasePersistenceProvider(IServiceProvider serviceProvider, ITickerClock clock, SchedulerOptionsBuilder optionsBuilder, ITickerQRedisContext redisContext)
    {
        _clock = clock;
        RedisContext = redisContext;
        _serviceProvider = serviceProvider;
        _lockHolder = optionsBuilder.NodeIdentifier;
        _schedulerOptions = optionsBuilder;
    }

    protected readonly SchedulerOptionsBuilder _schedulerOptions;

    /// <summary>
    /// Lease expiry to stamp on rows this node marks InProgress. Null when stale-job
    /// recovery is disabled — rows then carry no lease and are never swept.
    /// </summary>
    protected DateTime? NextLeaseUntil(DateTime now)
        => _schedulerOptions.StaleJobRecoveryEnabled ? now.Add(_schedulerOptions.LeaseDuration) : (DateTime?)null;

    protected readonly IServiceProvider _serviceProvider;
    protected readonly string _lockHolder;
    protected readonly ITickerClock _clock;
    protected readonly ITickerQRedisContext RedisContext;

    protected Task<DbContextLease<TDbContext>> CreateDbContextAsync(CancellationToken cancellationToken)
        => DbContextLease<TDbContext>.CreateAsync(_serviceProvider, cancellationToken);

    protected DbContextLease<TDbContext> CreateDbContext()
        => DbContextLease<TDbContext>.Create(_serviceProvider);
    
    #region Core_Time_Ticker_Methods
    public async IAsyncEnumerable<TimeTickerEntity> QueueTimeTickers(TimeTickerEntity[] timeTickers, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var dbContext = session.Context;
        var context = dbContext.Set<TTimeTicker>();
        var now = _clock.UtcNow;
        
        foreach (var timeTicker in timeTickers)
        {
            cancellationToken.ThrowIfCancellationRequested();
                
            var updatedTicker = await context
                .Where(x => x.Id == timeTicker.Id)
                .Where(x => x.UpdatedAt == timeTicker.UpdatedAt)
                .ExecuteUpdateAsync(prop => prop
                    .SetProperty(x => x.LockHolder, _lockHolder)
                    .SetProperty(x => x.LockedAt, now)
                    .SetProperty(x => x.UpdatedAt, now)
                    .SetProperty(x => x.Status, TickerStatus.Queued), cancellationToken);

            if (updatedTicker <= 0) 
                continue;
                
            timeTicker.UpdatedAt = now;
            timeTicker.LockHolder = _lockHolder;
            timeTicker.LockedAt = now;
            timeTicker.Status = TickerStatus.Queued;
                
            yield return timeTicker;
        }
    }

    public async IAsyncEnumerable<TimeTickerEntity> QueueTimedOutTimeTickers([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var dbContext = session.Context;
        var context = dbContext.Set<TTimeTicker>();
        var now = _clock.UtcNow;
        var fallbackThreshold = now.AddSeconds(-1);  // Fallback picks up tasks older than main 1-second window

        var timeTickersToUpdate =  await context
            .AsNoTracking()
            .Where(x => x.ExecutionTime != null)
            .Where(x => x.Status == TickerStatus.Idle || x.Status == TickerStatus.Queued)
            .Where(x => x.ExecutionTime <= fallbackThreshold)  // Only tasks older than 1 second
            .Include(x => x.Children.Where(y => y.ExecutionTime == null))
            .Select(MappingExtensions.ForQueueTimeTickers<TTimeTicker>())
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);

        // Probe-and-extend: the fast projection loads root + child + grandchild
        // (depth 3). If any chain actually goes deeper, BFS the rest in batch so
        // we don't silently truncate. Single EXISTS probe when nothing's deeper,
        // proportional cost only when chains exceed depth 3.
        await ExtendQueueChainsBeyondGrandchildrenAsync(context, timeTickersToUpdate, cancellationToken).ConfigureAwait(false);

        foreach (var timeTicker in timeTickersToUpdate)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var affected = await context
                .Where(x => x.Id == timeTicker.Id && x.UpdatedAt <= timeTicker.UpdatedAt)
                .ExecuteUpdateAsync(setter => setter
                    .SetProperty(x => x.LockHolder, _lockHolder)
                    .SetProperty(x => x.LockedAt, now)
                    .SetProperty(x => x.LeaseUntil, NextLeaseUntil(now))
                    .SetProperty(x => x.UpdatedAt, now)
                    .SetProperty(x => x.Status, TickerStatus.InProgress), cancellationToken).ConfigureAwait(false);
                
            if(affected <= 0)
                continue;

            yield return timeTicker;
        }
    }

    public async Task ReleaseAcquiredTimeTickers(Guid[] timeTickerIds, CancellationToken cancellationToken)
    {
        using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var dbContext = session.Context;
        var now = _clock.UtcNow;
            
        var idList = timeTickerIds.ToList();
        var baseQuery = idList.Count == 0
            ? dbContext.Set<TTimeTicker>()
            : dbContext.Set<TTimeTicker>().Where(x => idList.Contains(x.Id));
            
        await baseQuery
            .WhereCanAcquire(_lockHolder)
            .ExecuteUpdateAsync(setter => setter
                .SetProperty(x => x.LockHolder, _ => null)
                .SetProperty(x => x.LockedAt, _ => null)
                .SetProperty(x => x.Status, _ => TickerStatus.Idle)
                .SetProperty(x => x.UpdatedAt, _ => now), cancellationToken).ConfigureAwait(false);;
    }
        
    public async Task<int> UpdateTimeTicker(InternalFunctionContext functionContexts, CancellationToken cancellationToken)
    {
        using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var dbContext = session.Context;
        var now = _clock.UtcNow;

        var query = dbContext.Set<TTimeTicker>()
            .Where(x => x.Id == functionContexts.TickerId);

        // Fencing: a terminal write from this node must not overwrite a row the
        // stale watchdog already recovered (lock cleared / re-acquired elsewhere).
        // Only root tickers carry a lock — chain children execute under their
        // root's lock and are written unfenced, as before.
        if (IsFencedTerminalWrite(functionContexts) && functionContexts.ParentId == null)
            query = query.Where(x => x.LockHolder == _lockHolder);

        return await query
            .ExecuteUpdateAsync(setter => setter.UpdateTimeTicker<TTimeTicker>(functionContexts, now, NextLeaseUntil(now)), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// True when the context writes a terminal status — the writes that must be
    /// discarded if this node lost its lease while paused (fenced on LockHolder).
    /// </summary>
    protected static bool IsFencedTerminalWrite(InternalFunctionContext functionContext)
        => functionContext.GetPropsToUpdate().Contains(nameof(InternalFunctionContext.Status)) &&
           functionContext.Status is TickerStatus.Done or TickerStatus.DueDone or TickerStatus.Failed
               or TickerStatus.Cancelled or TickerStatus.Skipped;
        
    public async Task UpdateTimeTickersWithUnifiedContext(Guid[] timeTickerIds, InternalFunctionContext functionContext, CancellationToken cancellationToken = default)
    {
        using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var dbContext = session.Context;
        var idList = timeTickerIds.ToList();
        var now = _clock.UtcNow;
        await dbContext.Set<TTimeTicker>()
            .Where(x => idList.Contains(x.Id))
            .ExecuteUpdateAsync(setter => setter.UpdateTimeTicker<TTimeTicker>(functionContext, now, NextLeaseUntil(now)), cancellationToken).ConfigureAwait(false);
    }
        
    public async Task<TimeTickerEntity[]> GetEarliestTimeTickers(CancellationToken cancellationToken)
    {
        using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var dbContext = session.Context;
        var now = _clock.UtcNow;
    
        // Define the window: ignore anything older than 1 second ago
        var oneSecondAgo = now.AddSeconds(-1);
    
        var baseQuery = dbContext.Set<TTimeTicker>()
            .AsNoTracking()
            .Where(x => x.ExecutionTime != null)
            .Where(x => x.ExecutionTime >= oneSecondAgo)  // Ignore old tickers (fallback handles them)
            .WhereCanAcquire(_lockHolder);
    
        // Find the earliest ticker within our window
        var minExecutionTime = await baseQuery
            .OrderBy(x => x.ExecutionTime)
            .Select(x => x.ExecutionTime)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        if (minExecutionTime == null)
            return [];
    
        // Round the minimum execution time down to its second
        var minSecond = new DateTime(minExecutionTime.Value.Year, minExecutionTime.Value.Month,
            minExecutionTime.Value.Day, minExecutionTime.Value.Hour,
            minExecutionTime.Value.Minute, minExecutionTime.Value.Second,
            DateTimeKind.Utc);
    
        // Fetch all tickers within that complete second (this ensures we get all tickers in the same second)
        var maxExecutionTime = minSecond.AddSeconds(1);
    
        var earliest = await baseQuery
            .Include(x => x.Children.Where(y => y.ExecutionTime == null))
            .Where(x => x.ExecutionTime >= minSecond && x.ExecutionTime < maxExecutionTime)
            .OrderBy(x => x.ExecutionTime)
            .Select(MappingExtensions.ForQueueTimeTickers<TTimeTicker>())
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);

        await ExtendQueueChainsBeyondGrandchildrenAsync(dbContext.Set<TTimeTicker>(), earliest, cancellationToken).ConfigureAwait(false);
        return earliest;
    }

    /// <summary>
    /// Probe-and-extend for the queue projection. The fast path
    /// (<see cref="MappingExtensions.ForQueueTimeTickers{TTimeTicker}"/>) loads
    /// root + child + grandchild in one query. Anything below grandchild was
    /// historically dropped — this helper detects that case with a single
    /// EXISTS probe, then walks remaining levels with one batched query per
    /// depth tier (Contains(parentId) ⇒ "WHERE ParentId IN (...)") and stitches
    /// the new nodes into the existing tree via ParentId. Pure LINQ; portable
    /// across SQL Server / PostgreSQL / MySQL / SQLite.
    /// </summary>
    private static async Task ExtendQueueChainsBeyondGrandchildrenAsync(
        DbSet<TTimeTicker> context,
        TimeTickerEntity[] roots,
        CancellationToken cancellationToken)
    {
        if (roots == null || roots.Length == 0)
            return;

        // Collect IDs of the deepest layer the fast projection already loaded
        // (grandchildren). Also build a parent-id → node lookup so later levels
        // can attach themselves.
        var leafIds = new List<Guid>();
        var nodesById = new Dictionary<Guid, TimeTickerEntity>();
        foreach (var root in roots)
        {
            if (root.Children == null)
                continue;
            foreach (var child in root.Children)
            {
                if (child.Children == null)
                    continue;
                foreach (var grandchild in child.Children)
                {
                    grandchild.Children ??= new List<TimeTickerEntity>();
                    leafIds.Add(grandchild.Id);
                    nodesById[grandchild.Id] = grandchild;
                }
            }
        }

        if (leafIds.Count == 0)
            return;

        // Cheap EXISTS probe: does anything live below the grandchild layer?
        // 99% of chains stop at depth 3; in that case we pay one extra
        // sub-millisecond query and return.
        var hasDeeper = await context.AsNoTracking()
            .AnyAsync(x => x.ParentId.HasValue && leafIds.Contains(x.ParentId.Value), cancellationToken)
            .ConfigureAwait(false);
        if (!hasDeeper)
            return;

        // BFS extension: one query per remaining depth tier, all roots in batch.
        var frontier = leafIds;
        while (frontier.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var levelNodes = await context.AsNoTracking()
                .Where(x => x.ParentId.HasValue && frontier.Contains(x.ParentId.Value))
                .Select(e => new TimeTickerEntity
                {
                    Id = e.Id,
                    Function = e.Function,
                    Retries = e.Retries,
                    RetryIntervals = e.RetryIntervals,
                    RunCondition = e.RunCondition,
                    ParentId = e.ParentId,
                })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            if (levelNodes.Count == 0)
                break;

            var nextFrontier = new List<Guid>(levelNodes.Count);
            foreach (var node in levelNodes)
            {
                node.Children ??= new List<TimeTickerEntity>();
                if (node.ParentId.HasValue && nodesById.TryGetValue(node.ParentId.Value, out var parent))
                    parent.Children.Add(node);
                nodesById[node.Id] = node;
                nextFrontier.Add(node.Id);
            }
            frontier = nextFrontier;
        }
    }

    /// <summary>
    /// Probe-and-extend for the read paths (GetTimeTickerById, GetTimeTickers,
    /// GetTimeTickersPaginated, RemoveTimeTickers). Same shape as
    /// <see cref="ExtendQueueChainsBeyondGrandchildrenAsync"/> but works on the
    /// derived <typeparamref name="TTimeTicker"/> tree returned by EF Include
    /// chains. Caller is expected to have already eager-loaded 2 levels
    /// (Children + ThenInclude(Children)); this helper detects anything below
    /// and BFS-attaches it.
    /// </summary>
    protected static async Task ExtendReadChainsBeyondGrandchildrenAsync(
        DbSet<TTimeTicker> context,
        IEnumerable<TTimeTicker> roots,
        CancellationToken cancellationToken)
    {
        if (roots == null)
            return;

        var leafIds = new List<Guid>();
        var nodesById = new Dictionary<Guid, TTimeTicker>();
        foreach (var root in roots)
        {
            if (root.Children == null)
                continue;
            foreach (var child in root.Children)
            {
                if (child.Children == null)
                    continue;
                foreach (var grandchild in child.Children)
                {
                    grandchild.Children ??= new List<TTimeTicker>();
                    leafIds.Add(grandchild.Id);
                    nodesById[grandchild.Id] = grandchild;
                }
            }
        }

        if (leafIds.Count == 0)
            return;

        var hasDeeper = await context.AsNoTracking()
            .AnyAsync(x => x.ParentId.HasValue && leafIds.Contains(x.ParentId.Value), cancellationToken)
            .ConfigureAwait(false);
        if (!hasDeeper)
            return;

        var frontier = leafIds;
        while (frontier.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var levelNodes = await context.AsNoTracking()
                .Where(x => x.ParentId.HasValue && frontier.Contains(x.ParentId.Value))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            if (levelNodes.Count == 0)
                break;

            var nextFrontier = new List<Guid>(levelNodes.Count);
            foreach (var node in levelNodes)
            {
                node.Children ??= new List<TTimeTicker>();
                if (node.ParentId.HasValue && nodesById.TryGetValue(node.ParentId.Value, out var parent))
                    parent.Children.Add(node);
                nodesById[node.Id] = node;
                nextFrontier.Add(node.Id);
            }
            frontier = nextFrontier;
        }
    }
    
    public async Task<byte[]> GetTimeTickerRequest(Guid tickerId, CancellationToken cancellationToken = default)
    {
        using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var dbContext = session.Context;
        return await dbContext.Set<TTimeTicker>()
            .AsNoTracking()
            .Where(x => x.Id == tickerId)
            .Select(x => x.Request)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
    }
    
    public async Task ReleaseDeadNodeTimeTickerResources(string instanceIdentifier, CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var dbContext = session.Context;

        await dbContext.Set<TTimeTicker>()
            .WhereCanAcquire(instanceIdentifier)
            .ExecuteUpdateAsync(setter => setter
                .SetProperty(x => x.LockHolder, _ => null)
                .SetProperty(x => x.LockedAt, _ => null)
                .SetProperty(x => x.Status, TickerStatus.Idle)
                .SetProperty(x => x.UpdatedAt, now), cancellationToken)
            .ConfigureAwait(false);
        
        await dbContext.Set<TTimeTicker>()
            .Where(x => x.LockHolder == instanceIdentifier && x.Status == TickerStatus.InProgress)
            .ExecuteUpdateAsync(setter => setter
                .SetProperty(x => x.LockHolder, _ => null)
                .SetProperty(x => x.LockedAt, _ => null)
                .SetProperty(x => x.Status, TickerStatus.Idle)
                .SetProperty(x => x.UpdatedAt, now), cancellationToken)
            .ConfigureAwait(false);
    }
    #endregion

    public async Task<TimeTickerEntity[]> AcquireImmediateTimeTickersAsync(Guid[] ids, CancellationToken cancellationToken = default)
    {
        if (ids == null || ids.Length == 0)
            return [];

        using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var dbContext = session.Context;
        var now = _clock.UtcNow;
        var idList = ids.ToList();

        // Acquire and mark InProgress in a single update
        var affected = await dbContext.Set<TTimeTicker>()
            .Where(x => idList.Contains(x.Id))
            .WhereCanAcquire(_lockHolder)
            .ExecuteUpdateAsync(setter => setter
                .SetProperty(x => x.LockHolder, _lockHolder)
                .SetProperty(x => x.LockedAt, now)
                .SetProperty(x => x.LeaseUntil, NextLeaseUntil(now))
                .SetProperty(x => x.Status, TickerStatus.InProgress)
                .SetProperty(x => x.UpdatedAt, now), cancellationToken)
            .ConfigureAwait(false);

        if (affected == 0)
            return [];

        // Return the acquired tickers for immediate execution, with children
        return await dbContext.Set<TTimeTicker>()
            .AsNoTracking()
            .Where(x => idList.Contains(x.Id) && x.LockHolder == _lockHolder && x.Status == TickerStatus.InProgress)
            .Include(x => x.Children.Where(y => y.ExecutionTime == null))
            .Select(MappingExtensions.ForQueueTimeTickers<TTimeTicker>())
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
    }
        
    #region Core_Cron_Ticker_Methods
    public async Task MigrateDefinedCronTickers((string Function, string Expression)[] cronTickers, CancellationToken cancellationToken = default)
    {
        using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var dbContext = session.Context;
        var now = _clock.UtcNow;

        var functions = cronTickers.Select(x => x.Function).ToList();
        var cronSet = dbContext.Set<TCronTicker>();

        // Build the complete list of registered function names to detect orphaned tickers.
        // This covers functions whose InitIdentifier was cleared by a dashboard edit (#517).
        // Use List<string> instead of HashSet<string> for broader EF Core provider compatibility
        // (some providers like Devart MySQL don't assign type mappings to HashSet parameters).
        var allRegisteredFunctions = TickerFunctionProvider.TickerFunctions.Keys.ToList();

        // Orphan cleanup is intentionally narrowed to *seeded* crons (those that
        // carry an InitIdentifier from the code-defined-cron migration). Without
        // this filter we'd delete every dashboard-created cron whose function
        // is registered by an SDK / RemoteExecutor — at scheduler boot the SDK
        // hasn't synced yet, so those qualified function names (`name@node`)
        // wouldn't be in TickerFunctionProvider.TickerFunctions yet, and the
        // user's cron would be wiped out on every restart.
        //
        // Skipping non-seeded crons here is safe: they were never tied to a
        // code definition in the first place, so "the code definition went
        // away" doesn't apply to them.
        var orphanedCron = await cronSet
            .Where(c => !string.IsNullOrEmpty(c.InitIdentifier)
                        && !allRegisteredFunctions.Contains(c.Function))
            .Select(c => c.Id)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        var orphanedCronList = orphanedCron.ToList();
        if (orphanedCronList.Count > 0)
        {
            // Delete related occurrences first (if any), then the cron tickers
            await dbContext.Set<CronTickerOccurrenceEntity<TCronTicker>>()
                .Where(o => orphanedCronList.Contains(o.CronTickerId))
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            await cronSet
                .Where(c => orphanedCronList.Contains(c.Id))
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        var newFunctionSet = functions.ToHashSet(StringComparer.Ordinal);

        // Load existing (remaining) cron tickers for the current function set
        var existing = await cronSet
            .Where(c => functions.Contains(c.Function))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var existingByFunction = existing
            .GroupBy(c => c.Function)
            .ToDictionary(g => g.Key, g => g.First());

        foreach (var (function, expression) in cronTickers)
        {
            if (existingByFunction.TryGetValue(function, out var cron))
            {
                // Update expression if it changed
                if (!string.Equals(cron.Expression, expression, StringComparison.Ordinal))
                {
                    cron.Expression = expression;
                    cron.UpdatedAt = now;
                }
            }
            else
            {
                // Insert new seeded cron ticker
                var entity = new TCronTicker
                {
                    Id = Guid.NewGuid(),
                    Function = function,
                    Expression = expression,
                    InitIdentifier = $"MemoryTicker_Seeded_{function}",
                    CreatedAt = now,
                    UpdatedAt = now,
                    Request = Array.Empty<byte>()
                };
                await cronSet.AddAsync(entity, cancellationToken).ConfigureAwait(false);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
        
    public async Task<CronTickerEntity[]> GetAllCronTickerExpressions(CancellationToken cancellationToken = default)
    {
        var result = await RedisContext.GetOrSetArrayAsync(
            cacheKey: "cron:expressions",
            factory: async (ct) =>
            {
                using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
                var dbContext = session.Context;
                return await dbContext.Set<TCronTicker>()
                    .AsNoTracking()
                    .Where(x => x.IsEnabled && !x.IsSystemPaused)
                    .Select(MappingExtensions.ForCronTickerExpressions<CronTickerEntity>())
                    .ToArrayAsync(ct)
                    .ConfigureAwait(false);
            },
            expiration: TimeSpan.FromMinutes(10),
            cancellationToken: cancellationToken);

        if (result != null)
            return result;

        using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var dbContext = session.Context;
        return await dbContext.Set<TCronTicker>()
            .AsNoTracking()
            .Where(x => x.IsEnabled)
            .Select(MappingExtensions.ForCronTickerExpressions<CronTickerEntity>())
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
    }
    #endregion

    #region Core_Cron_TickerOccurrence_Methods
    public async Task UpdateCronTickerOccurrence(InternalFunctionContext functionContext, CancellationToken cancellationToken)
    {
        using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var dbContext = session.Context;
        var now = _clock.UtcNow;

        var query = dbContext.Set<CronTickerOccurrenceEntity<TCronTicker>>()
            .Where(x => x.Id == functionContext.TickerId);

        // Fencing: see UpdateTimeTicker. Occurrences are always lock-held by the
        // executing node, so every terminal write is fenced.
        if (IsFencedTerminalWrite(functionContext))
            query = query.Where(x => x.LockHolder == _lockHolder);

        await query
            .ExecuteUpdateAsync(setter => setter.UpdateCronTickerOccurrence<TCronTicker>(functionContext, NextLeaseUntil(now)), cancellationToken)
            .ConfigureAwait(false);
    }
    
    public async IAsyncEnumerable<CronTickerOccurrenceEntity<TCronTicker>> QueueTimedOutCronTickerOccurrences([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        var fallbackThreshold = now.AddSeconds(-1);  // Fallback picks up tasks older than main 1-second window

        using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var dbContext = session.Context;
        var context = dbContext.Set<CronTickerOccurrenceEntity<TCronTicker>>();
            
        var cronTickersToUpdate = await context
            .AsNoTracking()
            .Include(x => x.CronTicker)
            .Where(x => x.Status == TickerStatus.Idle || x.Status == TickerStatus.Queued)
            .Where(x => x.ExecutionTime <= fallbackThreshold)  // Only tasks older than 1 second
            .Select(MappingExtensions.ForQueueCronTickerOccurrence<CronTickerOccurrenceEntity<TCronTicker>, TCronTicker>())
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);

        foreach (var cronTickerOccurrence in cronTickersToUpdate)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var affected = await context
                .Where(x => x.Id == cronTickerOccurrence.Id && x.UpdatedAt == cronTickerOccurrence.UpdatedAt)
                .ExecuteUpdateAsync(setter => setter
                    .SetProperty(x => x.LockHolder, _lockHolder)
                    .SetProperty(x => x.LockedAt, now)
                    .SetProperty(x => x.LeaseUntil, NextLeaseUntil(now))
                    .SetProperty(x => x.UpdatedAt,  now)
                    .SetProperty(x => x.Status, TickerStatus.InProgress), cancellationToken)
                .ConfigureAwait(false);
                
            if(affected <= 0)
                continue;

            yield return cronTickerOccurrence;
        }
    }
    
    public async Task ReleaseDeadNodeOccurrenceResources(string instanceIdentifier, CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var dbContext = session.Context;

        await dbContext.Set<CronTickerOccurrenceEntity<TCronTicker>>()
            .WhereCanAcquire(instanceIdentifier)
            .ExecuteUpdateAsync(setter => setter
                .SetProperty(x => x.LockHolder, _ => null)
                .SetProperty(x => x.LockedAt, _ => null)
                .SetProperty(x => x.Status, TickerStatus.Idle)
                .SetProperty(x => x.UpdatedAt, now), cancellationToken)
            .ConfigureAwait(false);
        
        await dbContext.Set<CronTickerOccurrenceEntity<TCronTicker>>()
            .Where(x => x.LockHolder == instanceIdentifier && x.Status == TickerStatus.InProgress)
            .ExecuteUpdateAsync(setter => setter
                .SetProperty(x => x.LockHolder, _ => null)
                .SetProperty(x => x.LockedAt, _ => null)
                .SetProperty(x => x.Status, TickerStatus.Idle)
                .SetProperty(x => x.UpdatedAt, now), cancellationToken)
            .ConfigureAwait(false);
    }
    
    public async Task ReleaseAcquiredCronTickerOccurrences(Guid[] occurrenceIds, CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var dbContext = session.Context;

        var idList = occurrenceIds.ToList();
        var baseQuery = idList.Count == 0
            ? dbContext.Set<CronTickerOccurrenceEntity<TCronTicker>>()
            : dbContext.Set<CronTickerOccurrenceEntity<TCronTicker>>().Where(x => idList.Contains(x.Id));
           
        await baseQuery
            .WhereCanAcquire(_lockHolder)
            .ExecuteUpdateAsync(setter => setter
                .SetProperty(x => x.LockHolder, _ => null)
                .SetProperty(x => x.LockedAt, _ => null)
                .SetProperty(x => x.Status, TickerStatus.Idle)
                .SetProperty(x => x.UpdatedAt, now), cancellationToken)
            .ConfigureAwait(false);
    }
    
    public async IAsyncEnumerable<CronTickerOccurrenceEntity<TCronTicker>> QueueCronTickerOccurrences((DateTime Key, InternalManagerContext[] Items) cronTickerOccurrences, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        var executionTime = cronTickerOccurrences.Key;

        using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var dbContext = session.Context;
        var context = dbContext.Set<CronTickerOccurrenceEntity<TCronTicker>>();
        
        foreach (var item in cronTickerOccurrences.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (item.NextCronOccurrence is null)
            {
                var itemToAdd = new CronTickerOccurrenceEntity<TCronTicker>
                {
                    Id = Guid.NewGuid(),
                    Status = TickerStatus.Queued,
                    LockHolder = _lockHolder,
                    ExecutionTime = executionTime,
                    CronTickerId = item.Id,
                    LockedAt = now,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                
                var affectAdded = await context.Upsert(itemToAdd)
                    .On(x => new { x.ExecutionTime, x.CronTickerId })
                    .NoUpdate()
                    .RunAsync(cancellationToken).ConfigureAwait(false);;

                if (affectAdded <= 0)
                    continue;
                
                itemToAdd.CronTicker = new TCronTicker
                {
                    Id = item.Id,
                    Function = item.FunctionName,
                    InitIdentifier = _lockHolder,
                    Expression = item.Expression,
                    Retries = item.Retries,
                    RetryIntervals = item.RetryIntervals,
                    TimeoutSeconds = item.TimeoutSeconds
                };
                yield return itemToAdd;
            }
            else
            {
                var affectedUpdate = await context
                    .Where(x => x.Id == item.NextCronOccurrence.Id)
                    .Where(x => x.ExecutionTime == executionTime)
                    .WhereCanAcquire(_lockHolder)
                    .ExecuteUpdateAsync(prop => prop
                            .SetProperty(y => y.LockHolder, _lockHolder)
                            .SetProperty(y => y.LockedAt, now)
                            .SetProperty(y => y.UpdatedAt, now)
                            .SetProperty(y => y.Status, TickerStatus.Queued),
                        cancellationToken)
                    .ConfigureAwait(false);

                if (affectedUpdate <= 0)
                    continue;
                
                yield return new CronTickerOccurrenceEntity<TCronTicker>
                {
                    Id = item.NextCronOccurrence.Id,
                    CronTickerId = item.Id,
                    ExecutionTime = executionTime,
                    Status = TickerStatus.Queued,
                    LockHolder = _lockHolder,
                    LockedAt = now,
                    UpdatedAt = now,
                    CreatedAt = item.NextCronOccurrence.CreatedAt,
                    CronTicker = new TCronTicker
                    {
                        Id = item.Id,
                        Function = item.FunctionName,
                        InitIdentifier = _lockHolder,
                        Expression = item.Expression,
                        Retries = item.Retries,
                        RetryIntervals = item.RetryIntervals
                    }
                };
            }
        }
    }
    
    public async Task<CronTickerOccurrenceEntity<TCronTicker>> GetEarliestAvailableCronOccurrence(Guid[] ids, CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        var mainSchedulerThreshold = now.AddSeconds(-1);
        var idList = ids.ToList();
        using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var dbContext = session.Context;
        return await dbContext.Set<CronTickerOccurrenceEntity<TCronTicker>>()
            .AsNoTracking()
            .Include(x => x.CronTicker)
            .Where(x => idList.Contains(x.CronTickerId))
            .Where(x => x.ExecutionTime >= mainSchedulerThreshold)  // Only items within the 1-second main scheduler window
            .WhereCanAcquire(_lockHolder)
            .OrderBy(x => x.ExecutionTime)
            .Select(MappingExtensions.ForLatestQueuedCronTickerOccurrence<CronTickerOccurrenceEntity<TCronTicker>, TCronTicker>())
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }
    
    public async Task<byte[]> GetCronTickerOccurrenceRequest(Guid tickerId, CancellationToken cancellationToken = default)
    {
        using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var dbContext = session.Context;
        return await dbContext.Set<CronTickerOccurrenceEntity<TCronTicker>>()
            .AsNoTracking()
            .Include(x => x.CronTicker)
            .Where(x => x.Id == tickerId)
            .Select(x => x.CronTicker.Request)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }
    
    public async Task UpdateCronTickerOccurrencesWithUnifiedContext(Guid[] cronOccurrenceIds, InternalFunctionContext functionContext,
        CancellationToken cancellationToken = default)
    {
        var idList = cronOccurrenceIds.ToList();
        using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var dbContext = session.Context;
        var now = _clock.UtcNow;
        await dbContext.Set<CronTickerOccurrenceEntity<TCronTicker>>()
            .Where(x => idList.Contains(x.Id))
            .ExecuteUpdateAsync(setter => setter.UpdateCronTickerOccurrence<TCronTicker>(functionContext, NextLeaseUntil(now)), cancellationToken)
            .ConfigureAwait(false);
    }
    
    public async Task<int> SkipStaleCronOccurrencesAsync(TimeSpan staleThreshold, CancellationToken cancellationToken = default)
    {
        if (staleThreshold <= TimeSpan.Zero)
            return 0;

        var now = _clock.UtcNow;
        var cutoff = now - staleThreshold;

        using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var dbContext = session.Context;

        return await dbContext.Set<CronTickerOccurrenceEntity<TCronTicker>>()
            .Where(x => x.Status == TickerStatus.Idle || x.Status == TickerStatus.Queued)
            .Where(x => x.ExecutionTime < cutoff)
            .ExecuteUpdateAsync(setter => setter
                .SetProperty(x => x.Status, TickerStatus.Skipped)
                .SetProperty(x => x.SkippedReason, "Missed: occurrence was pending when the application restarted")
                .SetProperty(x => x.UpdatedAt, now), cancellationToken)
            .ConfigureAwait(false);
    }

    #endregion

    #region Stale_Job_Recovery

    public async Task<int> RenewTimeTickerLeases(Guid[] timeTickerIds, DateTime leaseUntil, CancellationToken cancellationToken = default)
    {
        if (timeTickerIds == null || timeTickerIds.Length == 0)
            return 0;

        using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var dbContext = session.Context;
        var idList = timeTickerIds.ToList();

        return await dbContext.Set<TTimeTicker>()
            .Where(x => idList.Contains(x.Id) && x.LockHolder == _lockHolder && x.Status == TickerStatus.InProgress)
            .ExecuteUpdateAsync(setter => setter
                .SetProperty(x => x.LeaseUntil, leaseUntil), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<int> RenewCronTickerOccurrenceLeases(Guid[] occurrenceIds, DateTime leaseUntil, CancellationToken cancellationToken = default)
    {
        if (occurrenceIds == null || occurrenceIds.Length == 0)
            return 0;

        using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var dbContext = session.Context;
        var idList = occurrenceIds.ToList();

        return await dbContext.Set<CronTickerOccurrenceEntity<TCronTicker>>()
            .Where(x => idList.Contains(x.Id) && x.LockHolder == _lockHolder && x.Status == TickerStatus.InProgress)
            .ExecuteUpdateAsync(setter => setter
                .SetProperty(x => x.LeaseUntil, leaseUntil), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<Guid[]> GetStillHeldTickerIds(Guid[] timeTickerIds, Guid[] occurrenceIds, CancellationToken cancellationToken = default)
    {
        using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var dbContext = session.Context;
        var held = new List<Guid>();

        if (timeTickerIds is { Length: > 0 })
        {
            var idList = timeTickerIds.ToList();
            held.AddRange(await dbContext.Set<TTimeTicker>()
                .AsNoTracking()
                .Where(x => idList.Contains(x.Id) && x.LockHolder == _lockHolder && x.Status == TickerStatus.InProgress)
                .Select(x => x.Id)
                .ToArrayAsync(cancellationToken).ConfigureAwait(false));
        }

        if (occurrenceIds is { Length: > 0 })
        {
            var idList = occurrenceIds.ToList();
            held.AddRange(await dbContext.Set<CronTickerOccurrenceEntity<TCronTicker>>()
                .AsNoTracking()
                .Where(x => idList.Contains(x.Id) && x.LockHolder == _lockHolder && x.Status == TickerStatus.InProgress)
                .Select(x => x.Id)
                .ToArrayAsync(cancellationToken).ConfigureAwait(false));
        }

        return held.ToArray();
    }

    public async Task<StaleTickerRecoveryResult> RecoverStaleTickers(int maxStaleRestarts, CancellationToken cancellationToken = default)
    {
        using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var dbContext = session.Context;
        var now = _clock.UtcNow;
        var result = new StaleTickerRecoveryResult();
        const string staleReason =
            "Stale: the node executing this ticker stopped renewing its lease (presumed dead).";

        // Restart pass first: expired-lease InProgress rows whose policy allows it
        // go back to Idle for any node to re-acquire (fallback picks them up since
        // their ExecutionTime is in the past). The subsequent Cancel pass then only
        // sees leftovers — Cancel policy or exhausted restart budget.
        result.RestartedTimeTickers = await dbContext.Set<TTimeTicker>()
            .Where(x => x.Status == TickerStatus.InProgress && x.LeaseUntil != null && x.LeaseUntil < now)
            .Where(x => x.OnStale == StaleAction.Restart && x.StaleRestartCount < maxStaleRestarts)
            .ExecuteUpdateAsync(setter => setter
                .SetProperty(x => x.Status, TickerStatus.Idle)
                .SetProperty(x => x.LockHolder, (string)null)
                .SetProperty(x => x.LockedAt, (DateTime?)null)
                .SetProperty(x => x.LeaseUntil, (DateTime?)null)
                .SetProperty(x => x.StaleRestartCount, x => x.StaleRestartCount + 1)
                .SetProperty(x => x.UpdatedAt, now), cancellationToken)
            .ConfigureAwait(false);

        result.CancelledTimeTickers = await dbContext.Set<TTimeTicker>()
            .Where(x => x.Status == TickerStatus.InProgress && x.LeaseUntil != null && x.LeaseUntil < now)
            .ExecuteUpdateAsync(setter => setter
                .SetProperty(x => x.Status, TickerStatus.Cancelled)
                .SetProperty(x => x.ExceptionMessage, staleReason)
                .SetProperty(x => x.ExecutedAt, now)
                .SetProperty(x => x.LockHolder, (string)null)
                .SetProperty(x => x.LockedAt, (DateTime?)null)
                .SetProperty(x => x.LeaseUntil, (DateTime?)null)
                .SetProperty(x => x.UpdatedAt, now), cancellationToken)
            .ConfigureAwait(false);

        // Occurrences take their OnStale policy from the parent cron template.
        result.RestartedCronOccurrences = await dbContext.Set<CronTickerOccurrenceEntity<TCronTicker>>()
            .Where(x => x.Status == TickerStatus.InProgress && x.LeaseUntil != null && x.LeaseUntil < now)
            .Where(x => x.CronTicker.OnStale == StaleAction.Restart && x.StaleRestartCount < maxStaleRestarts)
            .ExecuteUpdateAsync(setter => setter
                .SetProperty(x => x.Status, TickerStatus.Idle)
                .SetProperty(x => x.LockHolder, (string)null)
                .SetProperty(x => x.LockedAt, (DateTime?)null)
                .SetProperty(x => x.LeaseUntil, (DateTime?)null)
                .SetProperty(x => x.StaleRestartCount, x => x.StaleRestartCount + 1)
                .SetProperty(x => x.UpdatedAt, now), cancellationToken)
            .ConfigureAwait(false);

        result.CancelledCronOccurrences = await dbContext.Set<CronTickerOccurrenceEntity<TCronTicker>>()
            .Where(x => x.Status == TickerStatus.InProgress && x.LeaseUntil != null && x.LeaseUntil < now)
            .ExecuteUpdateAsync(setter => setter
                .SetProperty(x => x.Status, TickerStatus.Cancelled)
                .SetProperty(x => x.ExceptionMessage, staleReason)
                .SetProperty(x => x.ExecutedAt, now)
                .SetProperty(x => x.LockHolder, (string)null)
                .SetProperty(x => x.LockedAt, (DateTime?)null)
                .SetProperty(x => x.LeaseUntil, (DateTime?)null)
                .SetProperty(x => x.UpdatedAt, now), cancellationToken)
            .ConfigureAwait(false);

        return result;
    }

    #endregion
}
