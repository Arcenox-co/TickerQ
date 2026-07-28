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
using TickerQ.Utilities.Infrastructure;
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

        // Unique index on (ExecutionTime, CronTickerId) to prevent duplicate cron occurrences - mirrors EF Core's Upsert constraint
        private static readonly ConcurrentDictionary<(DateTime ExecutionTime, Guid CronTickerId), Guid> CronOccurrenceIndex = new();

        // Committed parent-result envelopes keyed by ticker/occurrence id. Populated only by a winning
        // successful terminal write and read by a child fetching its direct parent's result. Kept in a
        // side store (never on the shared entity) so the wire row and EF/Mongo mappings stay untouched.
        private static readonly ConcurrentDictionary<Guid, TickerResultEnvelope> TimeTickerResults = new();
        private static readonly ConcurrentDictionary<Guid, TickerResultEnvelope> CronOccurrenceResults = new();

        private readonly ITickerClock _clock;
        private readonly string _lockHolder;
        private readonly TimeSpan _leaseDuration;

        public TickerInMemoryPersistenceProvider(IServiceProvider serviceProvider)
        {
            _clock = serviceProvider.GetService<ITickerClock>() ?? new TickerSystemClock();
            var optionsBuilder = serviceProvider.GetService<SchedulerOptionsBuilder>();
            _lockHolder = optionsBuilder?.ExecutionOwnerId ?? $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";
            _leaseDuration = optionsBuilder?.LeaseDuration ?? TimeSpan.FromMinutes(1);
        }

        // In-memory persistence has no lease/stale-recovery contract, so it opts out
        // explicitly and inherits the compatibility-safe (fail-closed) interface defaults.
        public bool SupportsLeaseBasedRecovery => false;

        // Built-in provider: implements job retention with whole-chain, all-or-nothing semantics.
        public bool SupportsRetention => true;

        // Built-in provider: durably stores per-ticker result envelopes for parent-result propagation.
        public bool SupportsResultPublication => true;
        public bool SupportsAcknowledgedTerminalUpdates => true;

        // A result becomes visible only on the final successful terminal write: a present result envelope
        // on a Done/DueDone terminal update. Failed/cancelled/skipped/retry writes never carry one (the
        // execution handler only attaches it on success), and this re-check guards against any that slip through.
        private static bool IsSuccessfulResultWrite(InternalFunctionContext functionContext)
            => functionContext.ResultEnvelope != null
               && functionContext.GetPropsToUpdate().Contains(nameof(InternalFunctionContext.ResultEnvelope))
               && functionContext.GetPropsToUpdate().Contains(nameof(InternalFunctionContext.Status))
               && functionContext.Status is TickerStatus.Done or TickerStatus.DueDone;

        public Task<TickerResultEnvelope> GetTimeTickerResultAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(ReadGraph(() =>
                TimeTickerResults.TryGetValue(id, out var envelope) ? envelope : null));

        public Task<TickerResultEnvelope> GetCronTickerOccurrenceResultAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(ReadGraph(() =>
                CronOccurrenceResults.TryGetValue(id, out var envelope) ? envelope : null));

        public Task<bool> CommitSuccessfulTickerAsync(
            InternalFunctionContext functionContext, CancellationToken cancellationToken = default)
            => CommitTerminalTickerCoreAsync(functionContext, enforceChildFence: false, cancellationToken);

        public Task<bool> CommitTerminalTickerAsync(
            InternalFunctionContext functionContext, CancellationToken cancellationToken = default)
            => CommitTerminalTickerCoreAsync(functionContext, enforceChildFence: true, cancellationToken);

        private Task<bool> CommitTerminalTickerCoreAsync(
            InternalFunctionContext functionContext, bool enforceChildFence,
            CancellationToken cancellationToken)
        {
            if (functionContext == null)
                throw new ArgumentNullException(nameof(functionContext));
            if (!functionContext.GetPropsToUpdate().Contains(nameof(InternalFunctionContext.Status)) ||
                functionContext.Status is not (TickerStatus.Done or TickerStatus.DueDone or
                    TickerStatus.Failed or TickerStatus.Cancelled or TickerStatus.Skipped) ||
                (functionContext.Status is TickerStatus.Done or TickerStatus.DueDone &&
                 !functionContext.GetPropsToUpdate().Contains(nameof(InternalFunctionContext.ResultEnvelope))))
                throw new InvalidOperationException(
                    "Acknowledged terminal persistence requires a terminal status and an explicit optional result mutation on success.");

            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(WriteGraph(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (functionContext.Type == TickerType.CronTickerOccurrence)
                {
                    if (!CronOccurrences.TryGetValue(functionContext.TickerId, out var occurrence) ||
                        !functionContext.AcquisitionToken.HasValue || occurrence.LockHolder != _lockHolder ||
                        occurrence.AcquisitionToken != functionContext.AcquisitionToken)
                        return false;

                    var updatedOccurrence = CloneCronOccurrence(occurrence);
                    ApplyFunctionContextToCronOccurrence(updatedOccurrence, functionContext);
                    if (!TryUpdateCronOccurrence(functionContext.TickerId, updatedOccurrence, occurrence))
                        return false;
                    if (functionContext.Status is TickerStatus.Done or TickerStatus.DueDone)
                        ReplaceCommittedResult(CronOccurrenceResults, functionContext);
                    return true;
                }

                if (!TimeTickers.TryGetValue(functionContext.TickerId, out var ticker))
                    return false;
                if ((functionContext.ParentId == null || enforceChildFence) &&
                    (!functionContext.AcquisitionToken.HasValue || ticker.LockHolder != _lockHolder ||
                     ticker.AcquisitionToken != functionContext.AcquisitionToken))
                    return false;

                var updatedTicker = CloneTicker(ticker);
                ApplyFunctionContextToTicker(updatedTicker, functionContext);
                if (!TryUpdateTimeTicker(functionContext.TickerId, updatedTicker, ticker))
                    return false;
                if (functionContext.Status is TickerStatus.Done or TickerStatus.DueDone)
                    ReplaceCommittedResult(TimeTickerResults, functionContext);
                return true;
            }));
        }

        private static void ReplaceCommittedResult(
            ConcurrentDictionary<Guid, TickerResultEnvelope> results,
            InternalFunctionContext functionContext)
        {
            if (functionContext.ResultEnvelope == null)
                results.TryRemove(functionContext.TickerId, out _);
            else
                results[functionContext.TickerId] = functionContext.ResultEnvelope;
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
                        updatedTicker.AcquisitionToken = Guid.NewGuid();
                        updatedTicker.UpdatedAt = now;
                        updatedTicker.Status = TickerStatus.Queued;
                        
                        if (TryUpdateTimeTicker(timeTicker.Id, updatedTicker, existingTicker))
                        {
                            timeTicker.UpdatedAt = now;
                            timeTicker.LockHolder = _lockHolder;
                            timeTicker.LockedAt = now;
                            timeTicker.AcquisitionToken = updatedTicker.AcquisitionToken;
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
                        updatedTicker.AcquisitionToken = Guid.NewGuid();
                        updatedTicker.UpdatedAt = now;
                        updatedTicker.Status = TickerStatus.InProgress;

                        if (TryUpdateTimeTicker(ticker.Id, updatedTicker, existingTicker))
                        {
                            // Only build the full hierarchy for successfully acquired tickers
                            yield return ForQueueTimeTickers(updatedTicker);
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

                        TryUpdateTimeTicker(id, updatedTicker, ticker);
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
                if (IsFencedTerminalWrite(functionContext) && functionContext.ParentId == null &&
                    (!functionContext.AcquisitionToken.HasValue || ticker.LockHolder != _lockHolder ||
                     ticker.AcquisitionToken != functionContext.AcquisitionToken))
                    return Task.FromResult(0);

                var updatedTicker = CloneTicker(ticker);
                ApplyFunctionContextToTicker(updatedTicker, functionContext);

                if (TryUpdateTimeTicker(functionContext.TickerId, updatedTicker, ticker))
                {
                    // Commit the parent result only for the winning successful terminal write. This
                    // returns before the execution handler releases/queues children, so the result is
                    // durably visible to a child by the time it can read it.
                    if (IsSuccessfulResultWrite(functionContext))
                        TimeTickerResults[functionContext.TickerId] = functionContext.ResultEnvelope;

                    return Task.FromResult(1);
                }
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
                    TryUpdateTimeTicker(id, updatedTicker, ticker);
                }
            }
            
            return Task.CompletedTask;
        }

        public Task<Guid[]> TransitionQueuedTimeTickersToInProgressAsync(
            IReadOnlyCollection<AcquisitionLease> leases, CancellationToken cancellationToken = default)
        {
            var winners = new List<Guid>(leases.Count);
            var now = _clock.UtcNow;
            foreach (var lease in leases.Where(x => x.AcquisitionToken.HasValue).Distinct())
            {
                while (TimeTickers.TryGetValue(lease.TickerId, out var current))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (current.Status != TickerStatus.Queued || current.LockHolder != _lockHolder ||
                        current.AcquisitionToken != lease.AcquisitionToken)
                        break;
                    var updated = CloneTicker(current);
                    updated.Status = TickerStatus.InProgress;
                    updated.UpdatedAt = now;
                    if (!TryUpdateTimeTicker(lease.TickerId, updated, current)) continue;
                    winners.Add(lease.TickerId);
                    break;
                }
            }
            return Task.FromResult(winners.ToArray());
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
                updatedTicker.AcquisitionToken = Guid.NewGuid();
                updatedTicker.Status = TickerStatus.InProgress;
                updatedTicker.UpdatedAt = now;

                if (TryUpdateTimeTicker(id, updatedTicker, ticker))
                {
                    acquired.Add(ForQueueTimeTickers(updatedTicker));
                }
            }

            return Task.FromResult(acquired.ToArray());
        }

        public Task<TimeTickerEntity> AcquireTimeTickerOnDemandAsync(
            Guid id, DateTime executionTime, CancellationToken cancellationToken = default)
        {
            while (TimeTickers.TryGetValue(id, out var ticker))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var eligible = ticker.Status == TickerStatus.Idle ||
                               (ticker.Status == TickerStatus.Queued &&
                                (ticker.LockHolder == null || ticker.LockHolder == _lockHolder)) ||
                               ticker.Status is TickerStatus.Done or TickerStatus.DueDone or
                                   TickerStatus.Failed or TickerStatus.Cancelled or TickerStatus.Skipped;
                if (!eligible)
                    return Task.FromResult<TimeTickerEntity>(null);

                var now = _clock.UtcNow;
                var updated = CloneTicker(ticker);
                updated.ExecutionTime = executionTime;
                updated.Status = TickerStatus.InProgress;
                updated.LockHolder = _lockHolder;
                updated.LockedAt = now;
                updated.AcquisitionToken = Guid.NewGuid();
                updated.RetryCount = 0;
                updated.ExceptionMessage = null;
                updated.SkippedReason = null;
                updated.ExecutedAt = null;
                updated.ElapsedTime = 0;
                updated.StaleRestartCount = 0;
                updated.UpdatedAt = now;
                if (TryUpdateTimeTicker(id, updated, ticker))
                {
                    // A re-run must not surface the prior run's result until it publishes anew.
                    TimeTickerResults.TryRemove(id, out _);
                    return Task.FromResult<TimeTickerEntity>(ForQueueTimeTickers(updated));
                }
            }

            return Task.FromResult<TimeTickerEntity>(null);
        }

        public Task<TTimeTicker> GetTimeTickerById(Guid id, CancellationToken cancellationToken = default)
        {
            return ReadGraph(() =>
            {
                if (TimeTickers.TryGetValue(id, out var ticker))
                {
                    var result = BuildTickerHierarchy(ticker);
                    return Task.FromResult(result);
                }

                return Task.FromResult<TTimeTicker>(null);
            });
        }

        public Task<TTimeTicker[]> GetTimeTickers(Expression<Func<TTimeTicker, bool>> predicate, CancellationToken cancellationToken = default)
        {
            var compiledPredicate = predicate?.Compile();

            // Materialize the full projection under the read lock so a concurrent chain
            // replacement cannot surface a partially swapped aggregate.
            var results = ReadGraph(() =>
            {
                var query = TimeTickers.Values.AsEnumerable();

                if (compiledPredicate != null)
                    query = query.Where(compiledPredicate);

                // Match EF Core - only return root items (ParentId == null) with nested children
                return query
                    .Where(x => x.ParentId == null)  // Only root items, matching EF Core
                    .OrderByDescending(x => x.ExecutionTime)  // Match EF Core's OrderByDescending(x => x.ExecutionTime)
                    .Select(BuildTickerHierarchy)
                    .ToArray();
            });

            return Task.FromResult(results);
        }

        public Task<PaginationResult<TTimeTicker>> GetTimeTickersPaginated(Expression<Func<TTimeTicker, bool>> predicate, int pageNumber, int pageSize,
            CancellationToken cancellationToken = default)
        {
            var compiledPredicate = predicate?.Compile();

            // Count and page under the read lock so the total and the materialized page
            // reflect a single consistent graph snapshot, never a mid-replacement view.
            var (items, totalCount) = ReadGraph(() =>
            {
                var query = TimeTickers.Values.AsEnumerable();

                if (compiledPredicate != null)
                    query = query.Where(compiledPredicate);

                // Match EF Core - only count and paginate root items
                query = query.Where(x => x.ParentId == null);

                var count = query.Count();

                var paged = query
                    .OrderByDescending(x => x.ExecutionTime)  // Match EF Core's OrderByDescending(x => x.ExecutionTime)
                    .Skip((pageNumber - 1) * pageSize)
                    .Take(pageSize)
                    .Select(BuildTickerHierarchy)
                    .ToArray();

                return (paged, count);
            });

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
            // Structural insert of whole aggregates: take the write lock so a graph
            // reader (or a concurrent replacement/remove) never observes a partially
            // added root/children set. Matches ReplaceTimeTickerChainAsync fencing.
            var count = WriteGraph(() =>
            {
                var added = 0;
                foreach (var ticker in tickers)
                {
                    added += AddTickerWithChildren(ticker);
                }

                return added;
            });

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
            // Structural update can re-parent nodes and touch the children index, so it
            // runs under the write lock — a graph reader must never see a half-reparented
            // aggregate, and it must not interleave with a concurrent chain replacement.
            var count = WriteGraph(() =>
            {
                var updated = 0;
                foreach (var ticker in tickers)
                {
                    updated += UpdateTickerWithChildren(ticker);
                }

                return updated;
            });

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
                if (TryUpdateTimeTicker(ticker.Id, ticker, existing))
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
            // Cascade removal touches multiple graph entries; hold the write lock so a
            // reader never sees a parent gone while its children linger (torn aggregate)
            // and so it cannot interleave with a concurrent chain replacement.
            var count = WriteGraph(() =>
            {
                var removedCount = 0;
                foreach (var id in tickerIds)
                {
                    // Remove ticker and all its children (cascade delete)
                    if (TimeTickers.TryRemove(id, out var removed))
                    {
                        removedCount++;
                        TimeTickerResults.TryRemove(id, out _);

                        // Clean children index
                        if (removed.ParentId.HasValue)
                            RemoveChildIndex(removed.ParentId.Value, removed.Id);

                        // Remove children
                        var childrenIds = GetChildrenIds(id);

                        foreach (var childId in childrenIds)
                        {
                            if (TimeTickers.TryRemove(childId, out var child))
                            {
                                removedCount++;
                                TimeTickerResults.TryRemove(childId, out _);
                                if (child.ParentId.HasValue)
                                    RemoveChildIndex(child.ParentId.Value, child.Id);
                            }
                        }
                    }
                }

                return removedCount;
            });

            return Task.FromResult(count);
        }

        // Guards the shared time-ticker graph (TimeTickers + ChildrenIndex) so that
        // structural mutations (chain replacement, add/update/remove-with-children) are
        // observed atomically by graph-traversing readers. A reader holding the read lock
        // can never see a replacement's add-then-remove window (neither a doubled nor a
        // torn aggregate); a structural writer takes the write lock for its whole span.
        // The per-node CAS status writers keep operating lock-free on ConcurrentDictionary
        // entries — they never restructure the parent/child graph — so they are unaffected.
        private static readonly ReaderWriterLockSlim GraphLock = new(LockRecursionPolicy.NoRecursion);

        // Test-only seam invoked while an eligibility-changing CAS holds the read lock.
        // Retention's write lock cannot pass this point until the mutation completes.
        internal static Action<Guid> EligibilityMutationLockHook;

        private static bool TryUpdateTimeTicker(Guid id, TTimeTicker updated, TTimeTicker expected)
        {
            if (GraphLock.IsReadLockHeld || GraphLock.IsWriteLockHeld)
                return TimeTickers.TryUpdate(id, updated, expected);

            GraphLock.EnterReadLock();
            try
            {
                EligibilityMutationLockHook?.Invoke(id);
                return TimeTickers.TryUpdate(id, updated, expected);
            }
            finally
            {
                GraphLock.ExitReadLock();
            }
        }

        private static bool TryUpdateCronOccurrence(
            Guid id,
            CronTickerOccurrenceEntity<TCronTicker> updated,
            CronTickerOccurrenceEntity<TCronTicker> expected)
        {
            if (GraphLock.IsReadLockHeld || GraphLock.IsWriteLockHeld)
                return CronOccurrences.TryUpdate(id, updated, expected);

            GraphLock.EnterReadLock();
            try
            {
                EligibilityMutationLockHook?.Invoke(id);
                return CronOccurrences.TryUpdate(id, updated, expected);
            }
            finally
            {
                GraphLock.ExitReadLock();
            }
        }

        // Runs a graph read under the shared read lock. Callees must be lock-free (they
        // are: BuildTickerHierarchy / ForQueueTimeTickers and their private helpers).
        private static T ReadGraph<T>(Func<T> read)
        {
            GraphLock.EnterReadLock();
            try
            {
                return read();
            }
            finally
            {
                GraphLock.ExitReadLock();
            }
        }

        // Runs a structural graph mutation under the exclusive write lock.
        private static T WriteGraph<T>(Func<T> write)
        {
            GraphLock.EnterWriteLock();
            try
            {
                return write();
            }
            finally
            {
                GraphLock.ExitWriteLock();
            }
        }

        // Test-only seam. Invoked while the exclusive write lock is held, after the
        // replacement aggregate has been fully inserted but before the original is
        // removed — i.e. exactly the window in which the graph momentarily holds BOTH
        // the old and new roots. A deterministic concurrency regression sets this to
        // launch a reader and prove ReadGraph fences it out of the torn window. Null
        // and zero-cost in production.
        internal static Action GraphReplacementMidpointHook;

        public Task<int> ReplaceTimeTickerChainAsync(Guid oldRootId, TTimeTicker newRoot, CancellationToken cancellationToken = default)
        {
            if (newRoot == null)
                throw new ArgumentNullException(nameof(newRoot));

            return WriteGraph(() =>
            {
                // Persist the COMPLETE replacement first. Fail closed on any id collision
                // BEFORE removing anything, so the original aggregate is never lost.
                var replacementIds = new HashSet<Guid>();
                CollectChainIds(newRoot, replacementIds);
                foreach (var id in replacementIds)
                {
                    if (TimeTickers.ContainsKey(id))
                        throw new InvalidOperationException(
                            $"Cannot replace chain: a ticker with id {id} already exists.");
                }

                var insertedIds = new List<Guid>(replacementIds.Count);
                try
                {
                    AddReplacementWithChildren(newRoot, parentId: null, insertedIds);
                }
                catch
                {
                    // Remove only rows this replacement attempt inserted. The original graph
                    // was not touched yet, so failure is fully atomic even under a racing add.
                    for (var i = insertedIds.Count - 1; i >= 0; i--)
                    {
                        if (TimeTickers.TryRemove(insertedIds[i], out var removed) && removed.ParentId.HasValue)
                            RemoveChildIndex(removed.ParentId.Value, removed.Id);
                    }
                    throw;
                }

                // Both aggregates are momentarily present here (write lock still held).
                GraphReplacementMidpointHook?.Invoke();

                // Only once the replacement is in place do we remove the original aggregate.
                RemoveAggregateCascade(oldRootId);

                return Task.FromResult(insertedIds.Count);
            });
        }

        private static void CollectChainIds(TTimeTicker node, HashSet<Guid> ids)
        {
            if (!ids.Add(node.Id))
                throw new InvalidOperationException(
                    $"Cannot replace chain: replacement contains duplicate ticker id {node.Id}.");
            if (node.Children == null)
                return;
            foreach (var child in node.Children)
                if (child is TTimeTicker typedChild)
                    CollectChainIds(typedChild, ids);
        }

        private static void AddReplacementWithChildren(
            TTimeTicker ticker,
            Guid? parentId,
            List<Guid> insertedIds)
        {
            if (parentId.HasValue)
                ticker.ParentId = parentId.Value;

            if (!TimeTickers.TryAdd(ticker.Id, ticker))
                throw new InvalidOperationException(
                    $"Cannot replace chain: a ticker with id {ticker.Id} was added concurrently.");

            insertedIds.Add(ticker.Id);
            if (ticker.ParentId.HasValue)
                AddChildIndex(ticker.ParentId.Value, ticker.Id);

            if (ticker.Children == null)
                return;

            foreach (var child in ticker.Children)
                if (child is TTimeTicker typedChild)
                    AddReplacementWithChildren(typedChild, ticker.Id, insertedIds);
        }

        private static void RemoveAggregateCascade(Guid rootId)
        {
            // Depth-first so descendants at every level are removed, not just direct children.
            foreach (var childId in GetChildrenIds(rootId))
                RemoveAggregateCascade(childId);

            if (TimeTickers.TryRemove(rootId, out var removed) && removed.ParentId.HasValue)
                RemoveChildIndex(removed.ParentId.Value, removed.Id);
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

                TryUpdateTimeTicker(ticker.Id, updatedTicker, currentTicker);
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

                TryUpdateTimeTicker(ticker.Id, updatedTicker, currentTicker);
            }

            return Task.CompletedTask;
        }

        #endregion

        #region Cron Ticker Methods

        public Task MigrateDefinedCronTickers(DefinedCronTickerSeed[] cronTickers, CancellationToken cancellationToken = default)
        {
            var now = _clock.UtcNow;

            // Remove orphaned cron tickers whose function no longer exists in the
            // code definitions (#517). Limited to *seeded* crons (non-empty
            // InitIdentifier) so dashboard-created crons targeting SDK / remote
            // functions aren't wiped on scheduler restart — at boot the SDK
            // hasn't synced its functions yet, so the registry wouldn't contain
            // qualified `name@node` entries. See the EF provider's mirror
            // comment for the full rationale.
            var allRegisteredFunctions = TickerFunctionProvider.TickerFunctions.Keys
                .ToHashSet(StringComparer.Ordinal);
            var blockedFunctions = cronTickers.Where(x => !x.CanSeed)
                .Select(x => x.Function).ToHashSet(StringComparer.Ordinal);

            var snapshot = CronTickers.ToArray();
            foreach (var (id, ticker) in snapshot)
            {
                if (!string.IsNullOrEmpty(ticker.InitIdentifier)
                    && (!allRegisteredFunctions.Contains(ticker.Function)
                        || blockedFunctions.Contains(ticker.Function)))
                    CronTickers.TryRemove(id, out _);
            }

            foreach (var seed in cronTickers)
            {
                if (!seed.CanSeed)
                    continue;

                // Match only a SEEDED row for this function (snapshot for thread safety):
                // reconcile the seeded row's expression/contract-identity in place, and
                // never touch a user/dashboard-created row that shares the function name
                // but carries a null/non-seed InitIdentifier.
                var existing = CronTickers.Values.ToArray()
                    .FirstOrDefault(x => string.Equals(x.Function, seed.Function, StringComparison.Ordinal)
                        && !string.IsNullOrEmpty(x.InitIdentifier));

                if (existing != null)
                {
                    if (!string.Equals(existing.Expression, seed.Expression, StringComparison.Ordinal))
                    {
                        existing.Expression = seed.Expression;
                        existing.UpdatedAt = now;
                    }

                    // Reconcile authoritative contract identity onto seeded rows only; legacy/dashboard
                    // rows (no seed InitIdentifier) keep their own identity.
                    if (!string.IsNullOrEmpty(existing.InitIdentifier)
                        && !seed.MatchesIdentity(existing.RequestContractVersion, existing.RequestContractFingerprint))
                    {
                        existing.RequestContractVersion = seed.RequestContractVersion;
                        existing.RequestContractFingerprint = seed.RequestContractFingerprint;
                        existing.UpdatedAt = now;
                    }
                }
                else
                {
                    var id = Guid.NewGuid();
                    var cronTicker = new TCronTicker
                    {
                        Id = id,
                        Function = seed.Function,
                        Expression = seed.Expression,
                        InitIdentifier = $"MemoryTicker_Seeded_{id}",
                        CreatedAt = now,
                        UpdatedAt = now,
                        Request = Array.Empty<byte>(),
                        RequestContractVersion = seed.RequestContractVersion,
                        RequestContractFingerprint = seed.RequestContractFingerprint
                    };

                    CronTickers.TryAdd(id, cronTicker);
                }
            }

            return Task.CompletedTask;
        }

        public Task<CronTickerEntity[]> GetAllCronTickerExpressions(CancellationToken cancellationToken)
        {
            var result = CronTickers.Values
                .Where(x => x.IsEnabled && !x.IsSystemPaused)
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
                    updatedOccurrence.AcquisitionToken = Guid.NewGuid();
                    updatedOccurrence.UpdatedAt = now;
                    updatedOccurrence.Status = TickerStatus.Queued;
                    
                    if (TryUpdateCronOccurrence(occurrenceId, updatedOccurrence, existingOccurrence))
                    {
                        yield return updatedOccurrence;
                    }
                }
                else
                {
                    var indexKey = (cronTickerOccurrences.Key, context.Id);

                    // Atomically check uniqueness on (ExecutionTime, CronTickerId) - mirrors EF Core's Upsert .On() constraint
                    if (!CronOccurrenceIndex.TryAdd(indexKey, occurrenceId))
                        continue;

                    var newOccurrence = new CronTickerOccurrenceEntity<TCronTicker>
                    {
                        Id = occurrenceId,
                        CronTickerId = context.Id,
                        ExecutionTime = cronTickerOccurrences.Key,
                        Status = TickerStatus.Queued,
                        LockHolder = _lockHolder,
                        LockedAt = now,
                        AcquisitionToken = Guid.NewGuid(),
                        CreatedAt = context.NextCronOccurrence?.CreatedAt ?? now,
                        UpdatedAt = now,
                        RetryCount = 0
                    };

                    if (CronTickers.TryGetValue(context.Id, out var cronTicker))
                    {
                        newOccurrence.CronTicker = cronTicker;
                    }

                    if (CronOccurrences.TryAdd(newOccurrence.Id, newOccurrence))
                    {
                        yield return newOccurrence;
                    }
                    else
                    {
                        CronOccurrenceIndex.TryRemove(indexKey, out _);
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
                        updatedOccurrence.AcquisitionToken = Guid.NewGuid();
                        updatedOccurrence.UpdatedAt = now;
                        updatedOccurrence.Status = TickerStatus.InProgress;

                        if (TryUpdateCronOccurrence(occurrence.Id, updatedOccurrence, existingOccurrence))
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
                if (IsFencedTerminalWrite(functionContext) &&
                    (!functionContext.AcquisitionToken.HasValue || occurrence.LockHolder != _lockHolder ||
                     occurrence.AcquisitionToken != functionContext.AcquisitionToken))
                    return Task.CompletedTask;

                var updatedOccurrence = CloneCronOccurrence(occurrence);
                ApplyFunctionContextToCronOccurrence(updatedOccurrence, functionContext);

                if (TryUpdateCronOccurrence(functionContext.TickerId, updatedOccurrence, occurrence)
                    && IsSuccessfulResultWrite(functionContext))
                    CronOccurrenceResults[functionContext.TickerId] = functionContext.ResultEnvelope;
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

                        TryUpdateCronOccurrence(id, updatedOccurrence, occurrence);
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
                    TryUpdateCronOccurrence(id, updatedOccurrence, occurrence);
                }
            }
            
            return Task.CompletedTask;
        }

        public Task<Guid[]> TransitionQueuedCronOccurrencesToInProgressAsync(
            IReadOnlyCollection<AcquisitionLease> leases, CancellationToken cancellationToken = default)
        {
            var winners = new List<Guid>(leases.Count);
            var now = _clock.UtcNow;
            foreach (var lease in leases.Where(x => x.AcquisitionToken.HasValue).Distinct())
            {
                while (CronOccurrences.TryGetValue(lease.TickerId, out var current))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (current.Status != TickerStatus.Queued || current.LockHolder != _lockHolder ||
                        current.AcquisitionToken != lease.AcquisitionToken)
                        break;
                    var updated = CloneCronOccurrence(current);
                    updated.Status = TickerStatus.InProgress;
                    updated.UpdatedAt = now;
                    if (!TryUpdateCronOccurrence(lease.TickerId, updated, current)) continue;
                    winners.Add(lease.TickerId);
                    break;
                }
            }
            return Task.FromResult(winners.ToArray());
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

                TryUpdateCronOccurrence(occurrence.Id, updatedOccurrence, currentOccurrence);
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

                TryUpdateCronOccurrence(occurrence.Id, updatedOccurrence, currentOccurrence);
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

                var indexKey = (occurrence.ExecutionTime, occurrence.CronTickerId);
                if (CronOccurrenceIndex.TryAdd(indexKey, occurrence.Id) && CronOccurrences.TryAdd(occurrence.Id, occurrence))
                {
                    count++;
                }
                else
                {
                    CronOccurrenceIndex.TryRemove(indexKey, out _);
                }
            }

            return Task.FromResult(count);
        }

        public Task<int> RemoveCronTickerOccurrences(Guid[] cronTickerOccurrences, CancellationToken cancellationToken)
        {
            var count = 0;
            foreach (var id in cronTickerOccurrences)
            {
                if (CronOccurrences.TryRemove(id, out var removed))
                {
                    CronOccurrenceIndex.TryRemove((removed.ExecutionTime, removed.CronTickerId), out _);
                    CronOccurrenceResults.TryRemove(id, out _);
                    count++;
                }
            }

            return Task.FromResult(count);
        }

        #region Retention

        public Task<RetentionChainBatchResult> DeleteEligibleTimeTickerChainsAsync(
            RetentionCutoffs cutoffs, int batchSize, RetentionCursor cursor, CancellationToken cancellationToken = default)
        {
            if (batchSize <= 0 || cutoffs is null || !cutoffs.HasAny)
                return Task.FromResult(RetentionChainBatchResult.Empty);

            var now = _clock.UtcNow;

            // Hold the write lock for the whole examine-and-delete so each chain is judged and removed
            // atomically with respect to structural mutations, and eligibility is (re)read from the current
            // node values at the moment of deletion.
            var result = WriteGraph(() =>
            {
                // Candidate ROOTS: no parent, root node itself eligible (a necessary condition for the whole
                // chain), strictly after the keyset cursor, ordered by (ExecutedAt, Id). Bounded by Take:
                // deletion work never exceeds batchSize chains, and one extra candidate detects HasMore.
                var candidates = TimeTickers.Values
                    .Where(t => t.ParentId == null
                                && t.ExecutedAt.HasValue
                                && IsTimeNodeEligibleForRetention(t, cutoffs, now)
                                && cursor.IsBefore(t.ExecutedAt.Value, t.Id))
                    .OrderBy(t => t.ExecutedAt!.Value)
                    .ThenBy(t => t.Id)
                    .Take(batchSize + 1)
                    .ToList();

                var hasMore = candidates.Count > batchSize;
                var examineCount = Math.Min(candidates.Count, batchSize);

                var deletedRows = 0;
                var nextCursor = RetentionCursor.Start; // wrap by default (end of traversal)

                for (var i = 0; i < examineCount; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var root = candidates[i];

                    // Advance the cursor past every examined root — deleted OR retained — so a blocked chain
                    // is never reselected on the next call and cannot starve later eligible chains.
                    if (hasMore)
                        nextCursor = RetentionCursor.After(root.ExecutedAt!.Value, root.Id);

                    var subtree = new List<TTimeTicker>();
                    if (!TryCollectEligibleSubtree(
                            root.Id, cutoffs, now, cutoffs.MaxNodesPerChain, subtree, cancellationToken))
                        continue; // any node ineligible → retain the whole chain, but the cursor still advanced

                    foreach (var node in subtree)
                    {
                        if (TimeTickers.TryRemove(node.Id, out var removed) && removed.ParentId.HasValue)
                            RemoveChildIndex(removed.ParentId.Value, removed.Id);
                        TimeTickerResults.TryRemove(node.Id, out _);
                        deletedRows++;
                    }
                }

                return new RetentionChainBatchResult(deletedRows, hasMore, nextCursor);
            });

            return Task.FromResult(result);
        }

        // Collect a whole subtree with an EXPLICIT stack (never recursion) so an arbitrarily deep
        // chain cannot overflow the call stack. Returns false as soon as ANY node is ineligible,
        // missing, or the bound is exceeded so the caller retains the entire chain intact. A visited
        // set guards against cycles / DAG re-entry (each node counted and collected at most once).
        // Node values are read fresh from the store (recheck at delete time).
        private bool TryCollectEligibleSubtree(
            Guid rootId, RetentionCutoffs cutoffs, DateTime now, int maxNodes,
            List<TTimeTicker> collected, CancellationToken cancellationToken)
        {
            var stack = new Stack<Guid>();
            var visited = new HashSet<Guid>();
            stack.Push(rootId);

            while (stack.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var nodeId = stack.Pop();
                if (!visited.Add(nodeId))
                    continue; // already reached via another edge — do not re-collect or double count

                if (collected.Count >= maxNodes)
                    return false; // bound exceeded → fail closed, retain whole chain

                if (!TimeTickers.TryGetValue(nodeId, out var node))
                    return false; // cannot verify → retain

                if (!IsTimeNodeEligibleForRetention(node, cutoffs, now))
                    return false;

                collected.Add(node);

                foreach (var childId in GetChildrenIds(nodeId))
                    stack.Push(childId);
            }

            return true;
        }

        private static bool IsTimeNodeEligibleForRetention(TTimeTicker node, RetentionCutoffs cutoffs, DateTime now)
        {
            var cutoff = cutoffs.ForStatus(node.Status); // null when non-terminal or window unset
            if (cutoff is null)
                return false;
            if (node.ExecutedAt is null || node.ExecutedAt.Value >= cutoff.Value)
                return false;
            return IsNotActivelyOwned(node.AcquisitionToken, node.LeaseUntil, now);
        }

        public Task<RetentionBatchResult> DeleteEligibleCronTickerOccurrencesAsync(
            RetentionCutoffs cutoffs, int batchSize, CancellationToken cancellationToken = default)
        {
            if (batchSize <= 0 || cutoffs is null || !cutoffs.HasAny)
                return Task.FromResult(RetentionBatchResult.Empty);

            var result = WriteGraph(() =>
            {
                var now = _clock.UtcNow;
                var deleted = 0;
                var hasMore = false;

                foreach (var occurrence in CronOccurrences.Values)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (!IsCronOccurrenceEligibleForRetention(occurrence, cutoffs, now))
                        continue;

                    if (deleted >= batchSize)
                    {
                        hasMore = true;
                        break;
                    }

                    // Remove exactly the value that was rechecked. Even callers that do not
                    // participate in GraphLock cannot cause a newer replacement to be removed.
                    if (CronOccurrences.TryGetValue(occurrence.Id, out var current)
                        && IsCronOccurrenceEligibleForRetention(current, cutoffs, now)
                        && ((ICollection<KeyValuePair<Guid, CronTickerOccurrenceEntity<TCronTicker>>>)CronOccurrences)
                            .Remove(new KeyValuePair<Guid, CronTickerOccurrenceEntity<TCronTicker>>(
                                occurrence.Id, current)))
                    {
                        CronOccurrenceIndex.TryRemove((current.ExecutionTime, current.CronTickerId), out _);
                        CronOccurrenceResults.TryRemove(occurrence.Id, out _);
                        deleted++;
                    }
                }

                return new RetentionBatchResult(deleted, hasMore);
            });

            return Task.FromResult(result);
        }

        private static bool IsCronOccurrenceEligibleForRetention(
            CronTickerOccurrenceEntity<TCronTicker> occurrence, RetentionCutoffs cutoffs, DateTime now)
        {
            var cutoff = cutoffs.ForStatus(occurrence.Status);
            if (cutoff is null)
                return false;
            if (occurrence.ExecutedAt is null || occurrence.ExecutedAt.Value >= cutoff.Value)
                return false;
            return IsNotActivelyOwned(occurrence.AcquisitionToken, occurrence.LeaseUntil, now);
        }

        // Guards against deleting actively-owned work. A terminal write clears AcquisitionToken (the
        // authoritative live-generation marker) but deliberately leaves LockHolder and the last-renewed
        // LeaseUntil in place — so LockHolder on a terminal row is stale bookkeeping, not an active claim,
        // and requiring it to be null would make retention delete nothing. The real signals are: no live
        // generation (AcquisitionToken == null) and no still-live lease (LeaseUntil in the past or absent).
        private static bool IsNotActivelyOwned(Guid? acquisitionToken, DateTime? leaseUntil, DateTime now)
            => !acquisitionToken.HasValue
               && (!leaseUntil.HasValue || leaseUntil.Value <= now);

        #endregion

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
                updated.LeaseUntil = now.Add(_leaseDuration);
                updated.AcquisitionToken = Guid.NewGuid();
                updated.Status = TickerStatus.InProgress;
                updated.UpdatedAt = now;

                if (TryUpdateCronOccurrence(id, updated, occurrence))
                {
                    acquired.Add(updated);
                }
            }

            return Task.FromResult(acquired.ToArray());
        }

        public Task<int> SkipStaleCronOccurrencesAsync(TimeSpan staleThreshold, CancellationToken cancellationToken = default)
        {
            if (staleThreshold <= TimeSpan.Zero)
                return Task.FromResult(0);

            var now = _clock.UtcNow;
            var cutoff = now - staleThreshold;
            var count = 0;

            var staleOccurrences = CronOccurrences.Values
                .Where(x => x.Status == TickerStatus.Idle || x.Status == TickerStatus.Queued)
                .Where(x => x.ExecutionTime < cutoff)
                .ToArray();

            foreach (var occurrence in staleOccurrences)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!CronOccurrences.TryGetValue(occurrence.Id, out var current))
                    continue;

                var updated = CloneCronOccurrence(current);
                updated.Status = TickerStatus.Skipped;
                updated.SkippedReason = "Missed: occurrence was pending when the application restarted";
                updated.UpdatedAt = now;

                if (TryUpdateCronOccurrence(occurrence.Id, updated, current))
                    count++;
            }

            return Task.FromResult(count);
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

        // Matches EF Core's MappingExtensions.ForQueueTimeTickers but uses an in-memory
        // children index. Walks the chain recursively to unbounded depth (matching the
        // probe-and-extend behavior in the EF provider) so deep chains aren't truncated.
        private static TimeTickerEntity ForQueueTimeTickers(TTimeTicker ticker)
        {
            var root = new TimeTickerEntity
            {
                Id = ticker.Id,
                Function = ticker.Function,
                RequestContractVersion = ticker.RequestContractVersion,
                RequestContractFingerprint = ticker.RequestContractFingerprint,
                Retries = ticker.Retries,
                RetryIntervals = ticker.RetryIntervals,
                TimeoutSeconds = ticker.TimeoutSeconds,
                UpdatedAt = ticker.UpdatedAt,
                ParentId = ticker.ParentId,
                ExecutionTime = ticker.ExecutionTime,
                AcquisitionToken = ticker.AcquisitionToken,
                Children = BuildQueueDescendants(ticker.Id),
            };

            return root;
        }

        // Recursive descendants walker for the queue projection. Mirrors the EF
        // provider's probe-and-extend semantics: unbounded depth, only includes
        // chain children (ExecutionTime == null) for the direct-children layer.
        private static List<TimeTickerEntity> BuildQueueDescendants(Guid parentId)
        {
            if (!ChildrenIndex.TryGetValue(parentId, out var directChildren) || directChildren.IsEmpty)
                return new List<TimeTickerEntity>();

            var children = new List<TimeTickerEntity>(directChildren.Count);
            foreach (var childId in directChildren.Keys)
            {
                if (!TimeTickers.TryGetValue(childId, out var ch))
                    continue;

                // Only chain children with null ExecutionTime, matching the EF
                // .Include(x => x.Children.Where(y => y.ExecutionTime == null)) filter
                // on the direct-children layer.
                if (ch.ExecutionTime != null)
                    continue;

                children.Add(new TimeTickerEntity
                {
                    Id = ch.Id,
                    Function = ch.Function,
                    RequestContractVersion = ch.RequestContractVersion,
                    RequestContractFingerprint = ch.RequestContractFingerprint,
                    Retries = ch.Retries,
                    RetryIntervals = ch.RetryIntervals,
                    TimeoutSeconds = ch.TimeoutSeconds,
                    RunCondition = ch.RunCondition,
                    ParentId = ch.ParentId,
                    Children = BuildQueueDescendantsAtAnyDepth(ch.Id),
                });
            }

            return children;
        }

        // Same as BuildQueueDescendants but without the ExecutionTime filter — once
        // we're past the direct-children layer, every descendant is a chain node.
        private static List<TimeTickerEntity> BuildQueueDescendantsAtAnyDepth(Guid parentId)
        {
            if (!ChildrenIndex.TryGetValue(parentId, out var directChildren) || directChildren.IsEmpty)
                return new List<TimeTickerEntity>();

            var children = new List<TimeTickerEntity>(directChildren.Count);
            foreach (var childId in directChildren.Keys)
            {
                if (!TimeTickers.TryGetValue(childId, out var ch))
                    continue;

                children.Add(new TimeTickerEntity
                {
                    Id = ch.Id,
                    Function = ch.Function,
                    RequestContractVersion = ch.RequestContractVersion,
                    RequestContractFingerprint = ch.RequestContractFingerprint,
                    Retries = ch.Retries,
                    RetryIntervals = ch.RetryIntervals,
                    TimeoutSeconds = ch.TimeoutSeconds,
                    RunCondition = ch.RunCondition,
                    ParentId = ch.ParentId,
                    Children = BuildQueueDescendantsAtAnyDepth(ch.Id),
                });
            }
            return children;
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

        private static bool IsFencedTerminalWrite(InternalFunctionContext functionContext)
            => functionContext.GetPropsToUpdate().Contains(nameof(InternalFunctionContext.ReleaseLock)) ||
               (functionContext.GetPropsToUpdate().Contains(nameof(InternalFunctionContext.Status)) &&
                functionContext.Status is TickerStatus.Done or TickerStatus.DueDone or TickerStatus.Failed
                    or TickerStatus.Cancelled or TickerStatus.Skipped);

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
                RequestContractVersion = ticker.RequestContractVersion,
                RequestContractFingerprint = ticker.RequestContractFingerprint,
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
                LeaseUntil = ticker.LeaseUntil,
                AcquisitionToken = ticker.AcquisitionToken,
                OnStale = ticker.OnStale,
                StaleRestartCount = ticker.StaleRestartCount,
                TimeoutSeconds = ticker.TimeoutSeconds,
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
                UpdatedAt = occurrence.UpdatedAt,
                LeaseUntil = occurrence.LeaseUntil,
                AcquisitionToken = occurrence.AcquisitionToken,
                StaleRestartCount = occurrence.StaleRestartCount
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

            if (IsFencedTerminalWrite(context))
            {
                ticker.LeaseUntil = null;
                ticker.AcquisitionToken = null;
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

            if (IsFencedTerminalWrite(context))
            {
                occurrence.LeaseUntil = null;
                occurrence.AcquisitionToken = null;
            }

            // UPDATED_AT ALWAYS
            occurrence.UpdatedAt = _clock.UtcNow;
        }

        #endregion

        #region Queryable

        public ITickerQueryable<TTimeTicker> TimeTickersQuery()
        {
            return new InMemoryTickerQueryable<TTimeTicker>(_ =>
                Task.FromResult(TimeTickers.Values.ToList()));
        }

        public ITickerQueryable<TCronTicker> CronTickersQuery()
        {
            return new InMemoryTickerQueryable<TCronTicker>(_ =>
                Task.FromResult(CronTickers.Values.ToList()));
        }

        public ITickerQueryable<CronTickerOccurrenceEntity<TCronTicker>> CronTickerOccurrencesQuery()
        {
            return new InMemoryTickerQueryable<CronTickerOccurrenceEntity<TCronTicker>>(_ =>
                Task.FromResult(CronOccurrences.Values.ToList()));
        }

        #endregion
    }
}
