using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ.Provider
{
    internal class
        TickerInMemoryPersistenceProvider<TTimeTicker, TCronTicker> : ITickerPersistenceProvider<TTimeTicker,
        TCronTicker>
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        private static readonly ConcurrentDictionary<Guid, TTimeTicker> TimeTickers =
            new(new Dictionary<Guid, TTimeTicker>());

        // Index of parent -> child ids for fast hierarchy lookup in memory
        private static readonly ConcurrentDictionary<Guid, ConcurrentDictionary<Guid, byte>> ChildrenIndex =
            new(new Dictionary<Guid, ConcurrentDictionary<Guid, byte>>());

        private static readonly ConcurrentDictionary<Guid, TCronTicker> CronTickers =
            new(new Dictionary<Guid, TCronTicker>());

        private static readonly ConcurrentDictionary<Guid, CronTickerOccurrenceEntity<TCronTicker>> CronOccurrences =
            new(new Dictionary<Guid, CronTickerOccurrenceEntity<TCronTicker>>());

        private readonly ITickerClock _clock;
        private readonly string _lockHolder;

        public TickerInMemoryPersistenceProvider(IServiceProvider serviceProvider)
        {
            _clock = serviceProvider.GetService<ITickerClock>() ?? new TickerSystemClock();
            var optionsBuilder = serviceProvider.GetService<SchedulerOptionsBuilder>();
            _lockHolder = optionsBuilder?.NodeIdentifier ?? Environment.MachineName;
        }

        #region Time Ticker Methods

        public async IAsyncEnumerable<TimeTickerEntity> QueueTimeTickers(TimeTickerEntity[] timeTickers, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var now = _clock.UtcNow;
            
            foreach (var timeTicker in timeTickers)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (TimeTickers.TryGetValue(timeTicker.Id, out var existingTicker))
                {
                    // Check if we can update (similar to optimistic concurrency)
                    if (existingTicker.UpdatedAt == timeTicker.UpdatedAt)
                    {
                        // Update the ticker
                        var updatedTicker = CloneTicker(existingTicker);
                        updatedTicker.LockHolder = _lockHolder;
                        updatedTicker.LockedAt = now;
                        updatedTicker.UpdatedAt = now;
                        updatedTicker.Status = TickerStatus.Queued;
                        
                        if (TimeTickers.TryUpdate(timeTicker.Id, updatedTicker, existingTicker))
                        {
                            timeTicker.UpdatedAt = now;
                            timeTicker.LockHolder = _lockHolder;
                            timeTicker.LockedAt = now;
                            timeTicker.Status = TickerStatus.Queued;
                            
                            yield return timeTicker;
                        }
                    }
                }
            }
            
            await Task.CompletedTask;
        }

        public async IAsyncEnumerable<TimeTickerEntity> QueueTimedOutTimeTickers([EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var now = _clock.UtcNow;
            var fallbackThreshold = now.AddSeconds(-1);  // Fallback picks up tasks older than main 1-second window

            // First, get the time tickers that need to be updated (matching EF query)
            // NOTE: we project to the raw ticker here and only build the full
            //       TimeTickerEntity graph after we successfully acquire the lock.
            var timeTickersToUpdate = TimeTickers.Values
                .Where(x => x.ExecutionTime != null)
                .Where(x => x.Status == TickerStatus.Idle || x.Status == TickerStatus.Queued)
                .Where(x => x.ExecutionTime <= fallbackThreshold)  // Only tasks older than 1 second
                .ToArray();

            foreach (var ticker in timeTickersToUpdate)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Now update the actual ticker in storage
                if (TimeTickers.TryGetValue(ticker.Id, out var existingTicker))
                {
                    // Check if we can update (matching EF's Where condition)
                    if (existingTicker.UpdatedAt <= ticker.UpdatedAt)
                    {
                        var updatedTicker = CloneTicker(existingTicker);
                        updatedTicker.LockHolder = _lockHolder;
                        updatedTicker.LockedAt = now;
                        updatedTicker.UpdatedAt = now;
                        updatedTicker.Status = TickerStatus.InProgress;

                        if (TimeTickers.TryUpdate(ticker.Id, updatedTicker, existingTicker))
                        {
                            // Only build the full hierarchy for successfully acquired tickers
                            yield return ForQueueTimeTickers(ticker);
                        }
                    }
                }
            }
            
            await Task.CompletedTask;
        }

        public Task ReleaseAcquiredTimeTickers(Guid[] timeTickerIds, CancellationToken cancellationToken = default)
        {
            var now = _clock.UtcNow;
            var idsToRelease = timeTickerIds.Length == 0 
                ? TimeTickers.Keys.ToArray() 
                : timeTickerIds;

            foreach (var id in idsToRelease)
            {
                if (TimeTickers.TryGetValue(id, out var ticker))
                {
                    // Check if we can release (similar to WhereCanAcquire)
                    if (CanAcquire(ticker))
                    {
                        var updatedTicker = CloneTicker(ticker);
                        updatedTicker.LockHolder = null;
                        updatedTicker.LockedAt = null;
                        updatedTicker.Status = TickerStatus.Idle;
                        updatedTicker.UpdatedAt = now;

                        TimeTickers.TryUpdate(id, updatedTicker, ticker);
                    }
                }
            }

            return Task.CompletedTask;
        }

        public Task<TimeTickerEntity[]> GetEarliestTimeTickers(CancellationToken cancellationToken = default)
        {
            var now = _clock.UtcNow;
            var oneSecondAgo = now.AddSeconds(-1);

            // Base query: same filter as EF provider, but over the snapshot
            var baseQuery = TimeTickers.Values
                .Where(x => x.ExecutionTime != null)
                .Where(CanAcquire)
                .Where(x => x.ExecutionTime >= oneSecondAgo)
                .ToArray();

            // Get minimum execution time
            var minExecutionTime = baseQuery
                .OrderBy(x => x.ExecutionTime)
                .Select(x => x.ExecutionTime)
                .FirstOrDefault();

            if (minExecutionTime == null)
                return Task.FromResult(Array.Empty<TimeTickerEntity>());

            // Round the minimum execution time down to its second
            var minSecond = new DateTime(
                minExecutionTime.Value.Year,
                minExecutionTime.Value.Month,
                minExecutionTime.Value.Day,
                minExecutionTime.Value.Hour,
                minExecutionTime.Value.Minute,
                minExecutionTime.Value.Second,
                DateTimeKind.Utc);

            var maxExecutionTime = minSecond.AddSeconds(1);

            // Fetch all tickers within that complete second and map using the children lookup
            var result = baseQuery
                .Where(x => x.ExecutionTime >= minSecond && x.ExecutionTime < maxExecutionTime)
                .OrderBy(x => x.ExecutionTime)
                .Select(ForQueueTimeTickers)
                .ToArray();

            return Task.FromResult(result);
        }

        public Task<int> UpdateTimeTicker(InternalFunctionContext functionContext, CancellationToken cancellationToken = default)
        {
            if (TimeTickers.TryGetValue(functionContext.TickerId, out var ticker))
            {
                var updatedTicker = CloneTicker(ticker);
                ApplyFunctionContextToTicker(updatedTicker, functionContext);
                
                if (TimeTickers.TryUpdate(functionContext.TickerId, updatedTicker, ticker))
                    return Task.FromResult(1);
            }
            
            return Task.FromResult(0);
        }

        public Task<byte[]> GetTimeTickerRequest(Guid id, CancellationToken cancellationToken)
        {
            if (TimeTickers.TryGetValue(id, out var ticker))
            {
                return Task.FromResult(ticker.Request);
            }
            
            return Task.FromResult<byte[]>(null);
        }

        public Task UpdateTimeTickersWithUnifiedContext(Guid[] timeTickerIds, InternalFunctionContext functionContext,
            CancellationToken cancellationToken = default)
        {
            foreach (var id in timeTickerIds)
            {
                if (TimeTickers.TryGetValue(id, out var ticker))
                {
                    var updatedTicker = CloneTicker(ticker);
                    ApplyFunctionContextToTicker(updatedTicker, functionContext);
                    TimeTickers.TryUpdate(id, updatedTicker, ticker);
                }
            }
            
            return Task.CompletedTask;
        }

        public Task<TimeTickerEntity[]> AcquireImmediateTimeTickersAsync(Guid[] ids, CancellationToken cancellationToken = default)
        {
            if (ids == null || ids.Length == 0)
                return Task.FromResult(Array.Empty<TimeTickerEntity>());

            var now = _clock.UtcNow;
            var acquired = new List<TimeTickerEntity>();

            foreach (var id in ids)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!TimeTickers.TryGetValue(id, out var ticker))
                    continue;

                if (!CanAcquire(ticker))
                    continue;

                var updatedTicker = CloneTicker(ticker);
                updatedTicker.LockHolder = _lockHolder;
                updatedTicker.LockedAt = now;
                updatedTicker.Status = TickerStatus.InProgress;
                updatedTicker.UpdatedAt = now;

                if (TimeTickers.TryUpdate(id, updatedTicker, ticker))
                {
                    acquired.Add(ForQueueTimeTickers(updatedTicker));
                }
            }

            return Task.FromResult(acquired.ToArray());
        }

        public Task<TTimeTicker> GetTimeTickerById(Guid id, CancellationToken cancellationToken = default)
        {
            if (TimeTickers.TryGetValue(id, out var ticker))
            {
                var result = BuildTickerHierarchy(ticker);
                return Task.FromResult(result);
            }
            
            return Task.FromResult<TTimeTicker>(null);
        }

        public Task<TTimeTicker[]> GetTimeTickers(Expression<Func<TTimeTicker, bool>> predicate, CancellationToken cancellationToken = default)
        {
            var compiledPredicate = predicate?.Compile();
            var query = TimeTickers.Values.AsEnumerable();
            
            if (compiledPredicate != null)
                query = query.Where(compiledPredicate);
                
            // Match EF Core - only return root items (ParentId == null) with nested children
            var results = query
                .Where(x => x.ParentId == null)  // Only root items, matching EF Core
                .OrderByDescending(x => x.ExecutionTime)  // Match EF Core's OrderByDescending(x => x.ExecutionTime)
                .Select(BuildTickerHierarchy)
                .ToArray();
                
            return Task.FromResult(results);
        }

        public Task<PaginationResult<TTimeTicker>> GetTimeTickersPaginated(Expression<Func<TTimeTicker, bool>> predicate, int pageNumber, int pageSize,
            CancellationToken cancellationToken = default)
        {
            var compiledPredicate = predicate?.Compile();
            var query = TimeTickers.Values.AsEnumerable();
            
            if (compiledPredicate != null)
                query = query.Where(compiledPredicate);
                
            // Match EF Core - only count and paginate root items
            query = query.Where(x => x.ParentId == null);
            
            var totalCount = query.Count();
            
            var items = query
                .OrderByDescending(x => x.ExecutionTime)  // Match EF Core's OrderByDescending(x => x.ExecutionTime)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .Select(BuildTickerHierarchy)
                .ToArray();
                
            return Task.FromResult(new PaginationResult<TTimeTicker>
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = pageNumber,
                PageSize = pageSize
            });
        }

        public Task<int> AddTimeTickers(TTimeTicker[] tickers, CancellationToken cancellationToken = default)
        {
            var count = 0;
            foreach (var ticker in tickers)
            {
                count += AddTickerWithChildren(ticker);
            }
            
            return Task.FromResult(count);
        }
        
        private int AddTickerWithChildren(TTimeTicker ticker, Guid? parentId = null)
        {
            var count = 0;
            
            // Set the parent ID if this is a child
            if (parentId.HasValue)
            {
                ticker.ParentId = parentId.Value;
            }
            
            // Add the ticker itself
            if (TimeTickers.TryAdd(ticker.Id, ticker))
            {
                // Maintain children index
                if (ticker.ParentId.HasValue)
                    AddChildIndex(ticker.ParentId.Value, ticker.Id);

                count++;
                
                // Recursively add all children
                if (ticker.Children != null && ticker.Children.Count > 0)
                {
                    foreach (var child in ticker.Children)
                    {
                        // Cast to TTimeTicker since Children is ICollection<TTimeTicker>
                        if (child is TTimeTicker childTicker)
                        {
                            count += AddTickerWithChildren(childTicker, ticker.Id);
                        }
                    }
                }
            }
            
            return count;
        }

        public Task<int> UpdateTimeTickers(TTimeTicker[] tickers, CancellationToken cancellationToken = default)
        {
            var count = 0;
            foreach (var ticker in tickers)
            {
                count += UpdateTickerWithChildren(ticker);
            }
            
            return Task.FromResult(count);
        }
        
        private int UpdateTickerWithChildren(TTimeTicker ticker, Guid? parentId = null)
        {
            var count = 0;
            
            // Set the parent ID if this is a child
            if (parentId.HasValue)
            {
                ticker.ParentId = parentId.Value;
            }
            
            // Update the ticker itself
            if (TimeTickers.TryGetValue(ticker.Id, out var existing))
            {
                if (TimeTickers.TryUpdate(ticker.Id, ticker, existing))
                {
                    // Maintain children index for parent changes
                    if (existing.ParentId != ticker.ParentId)
                    {
                        if (existing.ParentId.HasValue)
                            RemoveChildIndex(existing.ParentId.Value, ticker.Id);
                        if (ticker.ParentId.HasValue)
                            AddChildIndex(ticker.ParentId.Value, ticker.Id);
                    }

                    count++;
                    
                    // Recursively update all children
                    if (ticker.Children != null && ticker.Children.Count > 0)
                    {
                        foreach (var child in ticker.Children)
                        {
                            // Cast to TTimeTicker since Children is ICollection<TTimeTicker>
                            if (child is TTimeTicker childTicker)
                            {
                                count += UpdateTickerWithChildren(childTicker, ticker.Id);
                            }
                        }
                    }
                }
            }
            else
            {
                // If it doesn't exist, add it (this can happen for new children)
                count += AddTickerWithChildren(ticker, parentId);
            }
            
            return count;
        }

        public Task<int> RemoveTimeTickers(Guid[] tickerIds, CancellationToken cancellationToken = default)
        {
            var count = 0;
            foreach (var id in tickerIds)
            {
                // Remove ticker and all its children (cascade delete)
                if (TimeTickers.TryRemove(id, out var removed))
                {
                    count++;
                    
                    // Clean children index
                    if (removed.ParentId.HasValue)
                        RemoveChildIndex(removed.ParentId.Value, removed.Id);
                    
                    // Remove children
                    var childrenIds = GetChildrenIds(id);
                        
                    foreach (var childId in childrenIds)
                    {
                        if (TimeTickers.TryRemove(childId, out var child))
                        {
                            count++;
                            if (child.ParentId.HasValue)
                                RemoveChildIndex(child.ParentId.Value, child.Id);
                        }
                    }
                }
            }
            
            return Task.FromResult(count);
        }

        public Task ReleaseDeadNodeTimeTickerResources(string instanceIdentifier, CancellationToken cancellationToken = default)
        {
            var now = _clock.UtcNow;

            // Phase 1: release acquirable tickers for the dead node (match EF WhereCanAcquire(instanceIdentifier))
            var releasable = TimeTickers.Values
                .Where(x =>
                    (x.Status == TickerStatus.Idle || x.Status == TickerStatus.Queued) &&
                    (x.LockHolder == instanceIdentifier || x.LockedAt == null))
                .ToArray();

            foreach (var ticker in releasable)
            {
                if (!TimeTickers.TryGetValue(ticker.Id, out var currentTicker))
                    continue;

                var updatedTicker = CloneTicker(currentTicker);
                updatedTicker.LockHolder = null;
                updatedTicker.LockedAt = null;
                updatedTicker.Status = TickerStatus.Idle;
                updatedTicker.UpdatedAt = now;

                TimeTickers.TryUpdate(ticker.Id, updatedTicker, currentTicker);
            }

            // Phase 2: mark in-progress tickers for that node as skipped
            var inProgress = TimeTickers.Values
                .Where(x => x.LockHolder == instanceIdentifier && x.Status == TickerStatus.InProgress)
                .ToArray();

            foreach (var ticker in inProgress)
            {
                if (!TimeTickers.TryGetValue(ticker.Id, out var currentTicker))
                    continue;

                var updatedTicker = CloneTicker(currentTicker);
                updatedTicker.Status = TickerStatus.Skipped;
                updatedTicker.SkippedReason = "Node is not alive!";
                updatedTicker.ExecutedAt = now;
                updatedTicker.UpdatedAt = now;

                TimeTickers.TryUpdate(ticker.Id, updatedTicker, currentTicker);
            }

            return Task.CompletedTask;
        }

        #endregion

        #region Cron Ticker Methods

        public Task MigrateDefinedCronTickers((string Function, string Expression)[] cronTickers, CancellationToken cancellationToken = default)
        {
            var now = _clock.UtcNow;

            foreach (var (function, expression) in cronTickers)
            {
                // Check if already exists (take snapshot for thread safety)
                var exists = CronTickers.Values.ToArray().Any(x => x.Function == function && x.Expression == expression);
                if (!exists)
                {
                    var id = Guid.NewGuid();
                    var cronTicker = new TCronTicker
                    {
                        Id = id,
                        Function = function,
                        Expression = expression,
                        InitIdentifier = $"MemoryTicker_Seeded_{id}",
                        CreatedAt = now,
                        UpdatedAt = now,
                        Request = Array.Empty<byte>()
                    };
                    
                    CronTickers.TryAdd(id, cronTicker);
                }
            }

            return Task.CompletedTask;
        }

        public Task<CronTickerEntity[]> GetAllCronTickerExpressions(CancellationToken cancellationToken)
        {
            var result = CronTickers.Values
                .Cast<CronTickerEntity>()
                .ToArray();
                
            return Task.FromResult(result);
        }

        public Task<TCronTicker> GetCronTickerById(Guid id, CancellationToken cancellationToken)
        {
            CronTickers.TryGetValue(id, out var ticker);
            return Task.FromResult(ticker);
        }

        public Task<TCronTicker[]> GetCronTickers(Expression<Func<TCronTicker, bool>> predicate, CancellationToken cancellationToken)
        {
            var compiledPredicate = predicate?.Compile();
            var query = CronTickers.Values.AsEnumerable();
            
            if (compiledPredicate != null)
                query = query.Where(compiledPredicate);
                
            var results = query
                .OrderByDescending(x => x.CreatedAt)
                .ToArray();
                
            return Task.FromResult(results);
        }

        public Task<PaginationResult<TCronTicker>> GetCronTickersPaginated(Expression<Func<TCronTicker, bool>> predicate, int pageNumber, int pageSize,
            CancellationToken cancellationToken = default)
        {
            var compiledPredicate = predicate?.Compile();
            var query = CronTickers.Values.AsEnumerable();
            
            if (compiledPredicate != null)
                query = query.Where(compiledPredicate);
                
            var totalCount = query.Count();
            
            var items = query
                .OrderByDescending(x => x.CreatedAt)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToArray();
                
            return Task.FromResult(new PaginationResult<TCronTicker>
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = pageNumber,
                PageSize = pageSize
            });
        }

        public Task<int> InsertCronTickers(TCronTicker[] tickers, CancellationToken cancellationToken)
        {
            var count = 0;
            foreach (var ticker in tickers)
            {
                if (CronTickers.TryAdd(ticker.Id, ticker))
                    count++;
            }
            
            return Task.FromResult(count);
        }

        public Task<int> UpdateCronTickers(TCronTicker[] cronTicker, CancellationToken cancellationToken)
        {
            var count = 0;
            foreach (var ticker in cronTicker)
            {
                if (CronTickers.TryGetValue(ticker.Id, out var existing))
                {
                    if (CronTickers.TryUpdate(ticker.Id, ticker, existing))
                        count++;
                }
            }
            
            return Task.FromResult(count);
        }

        public Task<int> RemoveCronTickers(Guid[] cronTickerIds, CancellationToken cancellationToken)
        {
            var count = 0;
            foreach (var id in cronTickerIds)
            {
                if (CronTickers.TryRemove(id, out _))
                    count++;
            }
            
            return Task.FromResult(count);
        }

        #endregion

        #region Cron Occurrence Methods

        public Task<CronTickerOccurrenceEntity<TCronTicker>> GetEarliestAvailableCronOccurrence(Guid[] ids, CancellationToken cancellationToken = default)
        {
            var now = _clock.UtcNow;
            var mainSchedulerThreshold = now.AddSeconds(-1);  // Main scheduler handles items within the 1-second window
            
            var query = CronOccurrences.Values.AsEnumerable();
            
            if (ids != null && ids.Length > 0)
                query = query.Where(x => ids.Contains(x.CronTickerId));
                
            var occurrence = query
                .Where(x => CanAcquireCronOccurrence(x))
                .Where(x => x.ExecutionTime >= mainSchedulerThreshold)  // Only recent/upcoming tasks (not heavily overdue)
                .OrderBy(x => x.ExecutionTime)
                .FirstOrDefault();
                
            return Task.FromResult(occurrence);
        }

        public async IAsyncEnumerable<CronTickerOccurrenceEntity<TCronTicker>> QueueCronTickerOccurrences((DateTime Key, InternalManagerContext[] Items) cronTickerOccurrences, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var now = _clock.UtcNow;
            
            foreach (var context in cronTickerOccurrences.Items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                // Each cron occurrence should have a unique ID
                var occurrenceId = context.NextCronOccurrence?.Id ?? Guid.NewGuid();
                
                // Check if this specific occurrence already exists
                if (CronOccurrences.TryGetValue(occurrenceId, out var existingOccurrence))
                {
                    // Update existing occurrence (should be rare - only if re-queuing)
                    var updatedOccurrence = CloneCronOccurrence(existingOccurrence);
                    updatedOccurrence.LockHolder = _lockHolder;
                    updatedOccurrence.LockedAt = now;
                    updatedOccurrence.UpdatedAt = now;
                    updatedOccurrence.Status = TickerStatus.Queued;
                    
                    if (CronOccurrences.TryUpdate(occurrenceId, updatedOccurrence, existingOccurrence))
                    {
                        yield return updatedOccurrence;
                    }
                }
                else
                {
                    // Create new occurrence (normal case - each execution time gets its own occurrence)
                    var newOccurrence = new CronTickerOccurrenceEntity<TCronTicker>
                    {
                        Id = occurrenceId,
                        CronTickerId = context.Id,
                        ExecutionTime = cronTickerOccurrences.Key,
                        Status = TickerStatus.Queued,
                        LockHolder = _lockHolder,
                        LockedAt = now,
                        CreatedAt = context.NextCronOccurrence?.CreatedAt ?? now,
                        UpdatedAt = now,
                        RetryCount = 0
                    };
                    
                    // Try to get the cron ticker
                    if (CronTickers.TryGetValue(context.Id, out var cronTicker))
                    {
                        newOccurrence.CronTicker = cronTicker;
                    }
                    
                    if (CronOccurrences.TryAdd(newOccurrence.Id, newOccurrence))
                    {
                        yield return newOccurrence;
                    }
                }
            }
        }

        public async IAsyncEnumerable<CronTickerOccurrenceEntity<TCronTicker>> QueueTimedOutCronTickerOccurrences([EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var now = _clock.UtcNow;
            var fallbackThreshold = now.AddSeconds(-1);  // Fallback picks up tasks older than main 1-second window

            var occurrencesToUpdate = CronOccurrences.Values
                .Where(x => x.Status == TickerStatus.Idle || x.Status == TickerStatus.Queued)
                .Where(x => x.ExecutionTime <= fallbackThreshold)  // Only tasks older than 1 second
                .ToArray();

            foreach (var occurrence in occurrencesToUpdate)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (CronOccurrences.TryGetValue(occurrence.Id, out var existingOccurrence))
                {
                    if (existingOccurrence.UpdatedAt <= occurrence.UpdatedAt)
                    {
                        var updatedOccurrence = CloneCronOccurrence(existingOccurrence);
                        updatedOccurrence.LockHolder = _lockHolder;
                        updatedOccurrence.LockedAt = now;
                        updatedOccurrence.UpdatedAt = now;
                        updatedOccurrence.Status = TickerStatus.InProgress;

                        if (CronOccurrences.TryUpdate(occurrence.Id, updatedOccurrence, existingOccurrence))
                        {
                            yield return updatedOccurrence;
                        }
                    }
                }
            }
        }

        public Task UpdateCronTickerOccurrence(InternalFunctionContext functionContext, CancellationToken cancellationToken = default)
        {
            if (CronOccurrences.TryGetValue(functionContext.TickerId, out var occurrence))
            {
                var updatedOccurrence = CloneCronOccurrence(occurrence);
                ApplyFunctionContextToCronOccurrence(updatedOccurrence, functionContext);
                
                CronOccurrences.TryUpdate(functionContext.TickerId, updatedOccurrence, occurrence);
            }
            
            return Task.CompletedTask;
        }

        public Task ReleaseAcquiredCronTickerOccurrences(Guid[] occurrenceIds, CancellationToken cancellationToken = default)
        {
            var now = _clock.UtcNow;
            var idsToRelease = occurrenceIds.Length == 0 
                ? CronOccurrences.Keys.ToArray() 
                : occurrenceIds;

            foreach (var id in idsToRelease)
            {
                if (CronOccurrences.TryGetValue(id, out var occurrence))
                {
                    if (CanAcquireCronOccurrence(occurrence))
                    {
                        var updatedOccurrence = CloneCronOccurrence(occurrence);
                        updatedOccurrence.LockHolder = null;
                        updatedOccurrence.LockedAt = null;
                        updatedOccurrence.Status = TickerStatus.Idle;
                        updatedOccurrence.UpdatedAt = now;

                        CronOccurrences.TryUpdate(id, updatedOccurrence, occurrence);
                    }
                }
            }

            return Task.CompletedTask;
        }

        public Task<byte[]> GetCronTickerOccurrenceRequest(Guid tickerId, CancellationToken cancellationToken = default)
        {
            // Cron ticker occurrences don't have their own request, get it from the cron ticker
            if (CronOccurrences.TryGetValue(tickerId, out var occurrence))
            {
                if (occurrence.CronTicker != null)
                    return Task.FromResult(occurrence.CronTicker.Request);
                    
                if (CronTickers.TryGetValue(occurrence.CronTickerId, out var cronTicker))
                    return Task.FromResult(cronTicker.Request);
            }
            
            return Task.FromResult<byte[]>(null);
        }

        public Task UpdateCronTickerOccurrencesWithUnifiedContext(Guid[] timeTickerIds, InternalFunctionContext functionContext, CancellationToken cancellationToken = default)
        {
            foreach (var id in timeTickerIds)
            {
                if (CronOccurrences.TryGetValue(id, out var occurrence))
                {
                    var updatedOccurrence = CloneCronOccurrence(occurrence);
                    ApplyFunctionContextToCronOccurrence(updatedOccurrence, functionContext);
                    CronOccurrences.TryUpdate(id, updatedOccurrence, occurrence);
                }
            }
            
            return Task.CompletedTask;
        }

        public Task ReleaseDeadNodeOccurrenceResources(string instanceIdentifier, CancellationToken cancellationToken = default)
        {
            var now = _clock.UtcNow;

            // Phase 1: release acquirable occurrences for the dead node (match EF WhereCanAcquire(instanceIdentifier))
            var releasable = CronOccurrences.Values
                .Where(x =>
                    (x.Status == TickerStatus.Idle || x.Status == TickerStatus.Queued) &&
                    (x.LockHolder == instanceIdentifier || x.LockedAt == null))
                .ToArray();

            foreach (var occurrence in releasable)
            {
                if (!CronOccurrences.TryGetValue(occurrence.Id, out var currentOccurrence))
                    continue;

                var updatedOccurrence = CloneCronOccurrence(currentOccurrence);
                updatedOccurrence.LockHolder = null;
                updatedOccurrence.LockedAt = null;
                updatedOccurrence.Status = TickerStatus.Idle;
                updatedOccurrence.UpdatedAt = now;

                CronOccurrences.TryUpdate(occurrence.Id, updatedOccurrence, currentOccurrence);
            }

            // Phase 2: mark in-progress occurrences for that node as skipped
            var inProgress = CronOccurrences.Values
                .Where(x => x.LockHolder == instanceIdentifier && x.Status == TickerStatus.InProgress)
                .ToArray();

            foreach (var occurrence in inProgress)
            {
                if (!CronOccurrences.TryGetValue(occurrence.Id, out var currentOccurrence))
                    continue;

                var updatedOccurrence = CloneCronOccurrence(currentOccurrence);
                updatedOccurrence.Status = TickerStatus.Skipped;
                updatedOccurrence.SkippedReason = "Node is not alive!";
                updatedOccurrence.ExecutedAt = now;
                updatedOccurrence.UpdatedAt = now;

                CronOccurrences.TryUpdate(occurrence.Id, updatedOccurrence, currentOccurrence);
            }

            return Task.CompletedTask;
        }

        public Task<CronTickerOccurrenceEntity<TCronTicker>[]> GetAllCronTickerOccurrences(Expression<Func<CronTickerOccurrenceEntity<TCronTicker>, bool>> predicate, CancellationToken cancellationToken = default)
        {
            var compiledPredicate = predicate?.Compile();
            var query = CronOccurrences.Values.AsEnumerable();
            
            if (compiledPredicate != null)
                query = query.Where(compiledPredicate);
                
            var results = query
                .OrderByDescending(x => x.CreatedAt)
                .ToArray();
                
            return Task.FromResult(results);
        }

        public Task<PaginationResult<CronTickerOccurrenceEntity<TCronTicker>>> GetAllCronTickerOccurrencesPaginated(Expression<Func<CronTickerOccurrenceEntity<TCronTicker>, bool>> predicate, int pageNumber, int pageSize,
            CancellationToken cancellationToken = default)
        {
            var compiledPredicate = predicate?.Compile();
            var query = CronOccurrences.Values.AsEnumerable();
            
            if (compiledPredicate != null)
                query = query.Where(compiledPredicate);
                
            var totalCount = query.Count();
            
            var items = query
                .OrderByDescending(x => x.CreatedAt)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToArray();
                
            return Task.FromResult(new PaginationResult<CronTickerOccurrenceEntity<TCronTicker>>
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = pageNumber,
                PageSize = pageSize
            });
        }

        public Task<int> InsertCronTickerOccurrences(CronTickerOccurrenceEntity<TCronTicker>[] cronTickerOccurrences, CancellationToken cancellationToken)
        {
            var count = 0;
            foreach (var occurrence in cronTickerOccurrences)
            {
                // Ensure navigation is populated for in-memory usage
                if (occurrence.CronTicker == null && CronTickers.TryGetValue(occurrence.CronTickerId, out var cronTicker))
                {
                    occurrence.CronTicker = cronTicker;
                }

                if (CronOccurrences.TryAdd(occurrence.Id, occurrence))
                    count++;
            }
            
            return Task.FromResult(count);
        }

        public Task<int> RemoveCronTickerOccurrences(Guid[] cronTickerOccurrences, CancellationToken cancellationToken)
        {
            var count = 0;
            foreach (var id in cronTickerOccurrences)
            {
                if (CronOccurrences.TryRemove(id, out _))
                    count++;
            }
            
            return Task.FromResult(count);
        }

        public Task<CronTickerOccurrenceEntity<TCronTicker>[]> AcquireImmediateCronOccurrencesAsync(Guid[] occurrenceIds, CancellationToken cancellationToken = default)
        {
            if (occurrenceIds == null || occurrenceIds.Length == 0)
                return Task.FromResult(Array.Empty<CronTickerOccurrenceEntity<TCronTicker>>());

            var now = _clock.UtcNow;
            var acquired = new List<CronTickerOccurrenceEntity<TCronTicker>>();

            foreach (var id in occurrenceIds)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!CronOccurrences.TryGetValue(id, out var occurrence))
                    continue;

                if (!CanAcquireCronOccurrence(occurrence))
                    continue;

                var updated = CloneCronOccurrence(occurrence);
                updated.LockHolder = _lockHolder;
                updated.LockedAt = now;
                updated.Status = TickerStatus.InProgress;
                updated.UpdatedAt = now;

                if (CronOccurrences.TryUpdate(id, updated, occurrence))
                {
                    acquired.Add(updated);
                }
            }

            return Task.FromResult(acquired.ToArray());
        }

        #endregion

        #region Helper Methods

        private TTimeTicker BuildTickerHierarchy(TTimeTicker ticker)
        {
            var root = CloneTicker(ticker);
            root.Children = BuildChildrenHierarchy(ticker.Id);
            return root;
        }

        private List<TTimeTicker> BuildChildrenHierarchy(Guid parentId)
        {
            if (!ChildrenIndex.TryGetValue(parentId, out var children) || children.IsEmpty)
                return new List<TTimeTicker>();

            var results = new List<TTimeTicker>(children.Count);

            foreach (var childId in children.Keys)
            {
                if (!TimeTickers.TryGetValue(childId, out var child))
                    continue;

                var clonedChild = CloneTicker(child);
                clonedChild.Children = BuildChildrenHierarchy(child.Id);
                results.Add(clonedChild);
            }

            return results;
        }

        // Matches EF Core's MappingExtensions.ForQueueTimeTickers but uses an in-memory children index
        private static TimeTickerEntity ForQueueTimeTickers(TTimeTicker ticker)
        {
            var root = new TimeTickerEntity
            {
                Id = ticker.Id,
                Function = ticker.Function,
                Retries = ticker.Retries,
                RetryIntervals = ticker.RetryIntervals,
                UpdatedAt = ticker.UpdatedAt,
                ParentId = ticker.ParentId,
                ExecutionTime = ticker.ExecutionTime,
                Children = new List<TimeTickerEntity>()
            };

            if (ChildrenIndex.TryGetValue(ticker.Id, out var directChildren) && !directChildren.IsEmpty)
            {
                // Pre-size children collection to avoid repeated growth
                var children = new List<TimeTickerEntity>(directChildren.Count);

                foreach (var childId in directChildren.Keys)
                {
                    if (!TimeTickers.TryGetValue(childId, out var ch))
                        continue;

                    // Only children with null ExecutionTime, matching EF mapping
                    if (ch.ExecutionTime != null)
                        continue;

                    var childEntity = new TimeTickerEntity
                    {
                        Id = ch.Id,
                        Function = ch.Function,
                        Retries = ch.Retries,
                        RetryIntervals = ch.RetryIntervals,
                        RunCondition = ch.RunCondition,
                        Children = new List<TimeTickerEntity>()
                    };

                    if (ChildrenIndex.TryGetValue(ch.Id, out var grandChildren) && !grandChildren.IsEmpty)
                    {
                        // Pre-size grandchildren collection
                        var grandChildList = new List<TimeTickerEntity>(grandChildren.Count);

                        foreach (var grandChildId in grandChildren.Keys)
                        {
                            if (!TimeTickers.TryGetValue(grandChildId, out var gch))
                                continue;

                            grandChildList.Add(new TimeTickerEntity
                            {
                                Id = gch.Id,
                                Function = gch.Function,
                                Retries = gch.Retries,
                                RetryIntervals = gch.RetryIntervals,
                                RunCondition = gch.RunCondition
                            });
                        }

                        childEntity.Children = grandChildList;
                    }

                    children.Add(childEntity);
                }

                root.Children = children;
            }

            return root;
        }

        private static void AddChildIndex(Guid parentId, Guid childId)
        {
            var children = ChildrenIndex.GetOrAdd(parentId, _ => new ConcurrentDictionary<Guid, byte>());
            children.TryAdd(childId, 0);
        }

        private static void RemoveChildIndex(Guid parentId, Guid childId)
        {
            if (!ChildrenIndex.TryGetValue(parentId, out var children))
                return;

            children.TryRemove(childId, out _);

            // Optional: cleanup empty buckets
            if (children.IsEmpty)
            {
                ChildrenIndex.TryRemove(parentId, out _);
            }
        }

        private static Guid[] GetChildrenIds(Guid parentId)
        {
            if (!ChildrenIndex.TryGetValue(parentId, out var children))
                return Array.Empty<Guid>();

            return children.Keys.ToArray();
        }

        private bool CanAcquire(TTimeTicker ticker)
        {
            // Match EF provider logic: WhereCanAcquire
            // Can acquire if: (Status is Idle OR Queued) AND (LockHolder matches current OR LockedAt is null)
            return ((ticker.Status == TickerStatus.Idle || ticker.Status == TickerStatus.Queued) && ticker.LockHolder == _lockHolder) ||
                   ((ticker.Status == TickerStatus.Idle || ticker.Status == TickerStatus.Queued) && ticker.LockedAt == null);
        }
        
        private bool CanAcquireCronOccurrence(CronTickerOccurrenceEntity<TCronTicker> occurrence)
        {
            // Match EF provider logic: WhereCanAcquire
            // Can acquire if: (Status is Idle OR Queued) AND (LockHolder matches current OR LockedAt is null)
            return ((occurrence.Status == TickerStatus.Idle || occurrence.Status == TickerStatus.Queued) && occurrence.LockHolder == _lockHolder) ||
                   ((occurrence.Status == TickerStatus.Idle || occurrence.Status == TickerStatus.Queued) && occurrence.LockedAt == null);
        }

        private TTimeTicker CloneTicker(TTimeTicker ticker)
        {
            var cloned = new TTimeTicker
            {
                Id = ticker.Id,
                Function = ticker.Function,
                Status = ticker.Status,
                Retries = ticker.Retries,
                RetryCount = ticker.RetryCount,
                ExecutionTime = ticker.ExecutionTime,
                InitIdentifier = ticker.InitIdentifier,
                LockHolder = ticker.LockHolder,
                LockedAt = ticker.LockedAt,
                ParentId = ticker.ParentId,
                Request = ticker.Request,
                ExceptionMessage = ticker.ExceptionMessage,
                SkippedReason = ticker.SkippedReason,
                ElapsedTime = ticker.ElapsedTime,
                RetryIntervals = ticker.RetryIntervals,
                RunCondition = ticker.RunCondition,
                ExecutedAt = ticker.ExecutedAt,
                CreatedAt = ticker.CreatedAt,
                UpdatedAt = ticker.UpdatedAt,
                Description = ticker.Description,
                Children = new List<TTimeTicker>()
            };
            
            return cloned;
        }
        
        private CronTickerOccurrenceEntity<TCronTicker> CloneCronOccurrence(CronTickerOccurrenceEntity<TCronTicker> occurrence)
        {
            return new CronTickerOccurrenceEntity<TCronTicker>
            {
                Id = occurrence.Id,
                CronTicker = occurrence.CronTicker,
                CronTickerId = occurrence.CronTickerId,
                Status = occurrence.Status,
                RetryCount = occurrence.RetryCount,
                ExecutionTime = occurrence.ExecutionTime,
                LockHolder = occurrence.LockHolder,
                LockedAt = occurrence.LockedAt,
                ExceptionMessage = occurrence.ExceptionMessage,
                SkippedReason = occurrence.SkippedReason,
                ElapsedTime = occurrence.ElapsedTime,
                ExecutedAt = occurrence.ExecutedAt,
                CreatedAt = occurrence.CreatedAt,
                UpdatedAt = occurrence.UpdatedAt
            };
        }


        private void ApplyFunctionContextToTicker(TTimeTicker ticker, InternalFunctionContext context)
        {
            var propsToUpdate = context.GetPropsToUpdate();

            // STATUS / SKIPPED
            if (propsToUpdate.Contains(nameof(InternalFunctionContext.Status)) &&
                context.Status != TickerStatus.Skipped)
            {
                ticker.Status = context.Status;
            }
            else if (propsToUpdate.Contains(nameof(InternalFunctionContext.Status)))
            {
                ticker.Status = context.Status;
                ticker.SkippedReason = context.ExceptionDetails;
            }

            // EXECUTED_AT
            if (propsToUpdate.Contains(nameof(InternalFunctionContext.ExecutedAt)))
            {
                ticker.ExecutedAt = context.ExecutedAt;
            }

            // EXCEPTION DETAILS
            if (propsToUpdate.Contains(nameof(InternalFunctionContext.ExceptionDetails)) &&
                context.Status != TickerStatus.Skipped)
            {
                ticker.ExceptionMessage = context.ExceptionDetails;
            }

            // ELAPSED_TIME
            if (propsToUpdate.Contains(nameof(InternalFunctionContext.ElapsedTime)))
            {
                ticker.ElapsedTime = context.ElapsedTime;
            }

            // RETRY COUNT
            if (propsToUpdate.Contains(nameof(InternalFunctionContext.RetryCount)))
            {
                ticker.RetryCount = context.RetryCount;
            }

            // RELEASE LOCK
            if (propsToUpdate.Contains(nameof(InternalFunctionContext.ReleaseLock)))
            {
                ticker.LockHolder = null;
                ticker.LockedAt = null;
            }

            // UPDATED_AT ALWAYS
            ticker.UpdatedAt = _clock.UtcNow;
        }
        
        private void ApplyFunctionContextToCronOccurrence(CronTickerOccurrenceEntity<TCronTicker> occurrence, InternalFunctionContext context)
        {
            var propsToUpdate = context.GetPropsToUpdate();

            // STATUS / SKIPPED
            if (propsToUpdate.Contains(nameof(InternalFunctionContext.Status)) &&
                context.Status != TickerStatus.Skipped)
            {
                occurrence.Status = context.Status;
            }
            else if (propsToUpdate.Contains(nameof(InternalFunctionContext.Status)))
            {
                occurrence.Status = context.Status;
                occurrence.SkippedReason = context.ExceptionDetails;
            }

            // EXECUTED_AT
            if (propsToUpdate.Contains(nameof(InternalFunctionContext.ExecutedAt)))
            {
                occurrence.ExecutedAt = context.ExecutedAt;
            }

            // EXCEPTION DETAILS
            if (propsToUpdate.Contains(nameof(InternalFunctionContext.ExceptionDetails)) &&
                context.Status != TickerStatus.Skipped)
            {
                occurrence.ExceptionMessage = context.ExceptionDetails;
            }

            // ELAPSED_TIME
            if (propsToUpdate.Contains(nameof(InternalFunctionContext.ElapsedTime)))
            {
                occurrence.ElapsedTime = context.ElapsedTime;
            }

            // RETRY COUNT
            if (propsToUpdate.Contains(nameof(InternalFunctionContext.RetryCount)))
            {
                occurrence.RetryCount = context.RetryCount;
            }

            // RELEASE LOCK
            if (propsToUpdate.Contains(nameof(InternalFunctionContext.ReleaseLock)))
            {
                occurrence.LockHolder = null;
                occurrence.LockedAt = null;
            }

            // UPDATED_AT ALWAYS
            occurrence.UpdatedAt = _clock.UtcNow;
        }

        #endregion
    }
}
