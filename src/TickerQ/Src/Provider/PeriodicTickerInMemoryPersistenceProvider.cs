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
    internal class PeriodicTickerInMemoryPersistenceProvider<TPeriodicTicker> : IPeriodicTickerPersistenceProvider<TPeriodicTicker>
        where TPeriodicTicker : PeriodicTickerEntity, new()
    {
        private static readonly ConcurrentDictionary<Guid, TPeriodicTicker> PeriodicTickers = new();
        private static readonly ConcurrentDictionary<Guid, PeriodicTickerOccurrenceEntity<TPeriodicTicker>> PeriodicOccurrences = new();

        private readonly ITickerClock _clock;
        private readonly string _lockHolder;
        private readonly TimeSpan _overlapStaleThreshold;

        public PeriodicTickerInMemoryPersistenceProvider(IServiceProvider serviceProvider)
        {
            _clock = serviceProvider.GetService<ITickerClock>() ?? new TickerSystemClock();
            var optionsBuilder = serviceProvider.GetService<SchedulerOptionsBuilder>();
            _lockHolder = optionsBuilder?.NodeIdentifier ?? Environment.MachineName;
            _overlapStaleThreshold = optionsBuilder?.PeriodicOverlapStaleThreshold > TimeSpan.Zero
                ? optionsBuilder.PeriodicOverlapStaleThreshold
                : TimeSpan.FromMinutes(10);
        }

        #region Core Methods

        public Task<PeriodicTickerEntity[]> GetAllActivePeriodicTickers(CancellationToken cancellationToken = default)
        {
            var now = _clock.UtcNow;
            var result = PeriodicTickers.Values
                .Where(x => x.IsActive)
                .Where(x => x.StartTime == null || x.StartTime <= now)
                .Where(x => x.EndTime == null || x.EndTime > now)
                .Cast<PeriodicTickerEntity>()
                .ToArray();

            return Task.FromResult(result);
        }

        public Task<PeriodicTickerOccurrenceEntity<TPeriodicTicker>> GetEarliestAvailablePeriodicOccurrence(Guid[] ids, CancellationToken cancellationToken = default)
        {
            var result = PeriodicOccurrences.Values
                .Where(x => ids.Contains(x.PeriodicTickerId))
                .Where(x => x.Status == TickerStatus.Idle || x.Status == TickerStatus.Queued)
                .OrderBy(x => x.ExecutionTime)
                .FirstOrDefault();

            return Task.FromResult(result);
        }

        public Task<HashSet<Guid>> GetPeriodicTickerIdsWithUnfinishedOccurrence(Guid[] ids, CancellationToken cancellationToken = default)
        {
            var result = new HashSet<Guid>();
            if (ids == null || ids.Length == 0)
                return Task.FromResult(result);

            // An InProgress occurrence only suppresses while fresh; once untouched past the stale
            // threshold it is assumed abandoned (crashed/hung node) and no longer blocks a Skip ticker.
            // Idle/Queued always count (pending, not running).
            var staleCutoff = _clock.UtcNow - _overlapStaleThreshold;

            var idSet = new HashSet<Guid>(ids);
            foreach (var occurrence in PeriodicOccurrences.Values)
            {
                if (!idSet.Contains(occurrence.PeriodicTickerId)) continue;

                var counts = occurrence.Status is TickerStatus.Idle or TickerStatus.Queued
                    || (occurrence.Status == TickerStatus.InProgress
                        && (occurrence.LockedAt == null || occurrence.LockedAt > staleCutoff));

                if (counts)
                    result.Add(occurrence.PeriodicTickerId);
            }

            return Task.FromResult(result);
        }

        public async IAsyncEnumerable<PeriodicTickerOccurrenceEntity<TPeriodicTicker>> QueuePeriodicTickerOccurrences(
            (DateTime Key, InternalManagerContext[] Items) periodicTickerOccurrences,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var now = _clock.UtcNow;

            foreach (var item in periodicTickerOccurrences.Items)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Overlap-suppressed (Skip ticker with an in-flight occurrence): present only to keep the
                // scheduler's wake-up alive — do not materialize a new occurrence.
                if (item.SuppressMaterialization)
                    continue;

                if (!PeriodicTickers.TryGetValue(item.Id, out var periodicTicker))
                    continue;

                // Check if occurrence already exists
                if (item.NextPeriodicOccurrence != null)
                {
                    if (PeriodicOccurrences.TryGetValue(item.NextPeriodicOccurrence.Id, out var existingOccurrence))
                    {
                        if (existingOccurrence.UpdatedAt == item.NextPeriodicOccurrence.UpdatedAt)
                        {
                            var updatedOccurrence = CloneOccurrence(existingOccurrence);
                            updatedOccurrence.LockHolder = _lockHolder;
                            updatedOccurrence.LockedAt = now;
                            updatedOccurrence.UpdatedAt = now;
                            updatedOccurrence.Status = TickerStatus.Queued;

                            if (PeriodicOccurrences.TryUpdate(existingOccurrence.Id, updatedOccurrence, existingOccurrence))
                            {
                                AdvanceLastStartedAt(periodicTicker.Id, periodicTickerOccurrences.Key);
                                yield return updatedOccurrence;
                            }
                        }
                    }
                }
                else
                {
                    // Create new occurrence
                    var newOccurrence = new PeriodicTickerOccurrenceEntity<TPeriodicTicker>
                    {
                        Id = Guid.NewGuid(),
                        PeriodicTickerId = periodicTicker.Id,
                        PeriodicTicker = periodicTicker,
                        ExecutionTime = periodicTickerOccurrences.Key,
                        Status = TickerStatus.Queued,
                        LockHolder = _lockHolder,
                        LockedAt = now,
                        CreatedAt = now,
                        UpdatedAt = now
                    };

                    if (PeriodicOccurrences.TryAdd(newOccurrence.Id, newOccurrence))
                    {
                        AdvanceLastStartedAt(periodicTicker.Id, periodicTickerOccurrences.Key);
                        yield return newOccurrence;
                    }
                }
            }

            await Task.CompletedTask;
        }

        public async IAsyncEnumerable<PeriodicTickerOccurrenceEntity<TPeriodicTicker>> QueueTimedOutPeriodicTickerOccurrences(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var now = _clock.UtcNow;
            var fallbackThreshold = now.AddSeconds(-1);
            var staleCutoff = now - _overlapStaleThreshold;

            // Recover occurrences abandoned in InProgress (crashed/hung node after acquiring): release to
            // Idle so they stop suppressing Skip tickers and normal scheduling can re-acquire them.
            foreach (var occurrence in PeriodicOccurrences.Values)
            {
                if (occurrence.Status != TickerStatus.InProgress) continue;
                if (occurrence.LockedAt == null || occurrence.LockedAt > staleCutoff) continue;

                var released = CloneOccurrence(occurrence);
                released.LockHolder = null;
                released.LockedAt = null;
                released.Status = TickerStatus.Idle;
                released.UpdatedAt = now;
                PeriodicOccurrences.TryUpdate(occurrence.Id, released, occurrence);
            }

            var timedOutOccurrences = PeriodicOccurrences.Values
                .Where(x => x.Status == TickerStatus.Idle || x.Status == TickerStatus.Queued)
                .Where(x => x.ExecutionTime <= fallbackThreshold)
                .ToArray();

            foreach (var occurrence in timedOutOccurrences)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (PeriodicOccurrences.TryGetValue(occurrence.Id, out var existing))
                {
                    if (existing.UpdatedAt <= occurrence.UpdatedAt)
                    {
                        var updated = CloneOccurrence(existing);
                        updated.LockHolder = _lockHolder;
                        updated.LockedAt = now;
                        updated.UpdatedAt = now;
                        updated.Status = TickerStatus.InProgress;

                        if (PeriodicOccurrences.TryUpdate(occurrence.Id, updated, existing))
                        {
                            // Attach parent ticker
                            if (PeriodicTickers.TryGetValue(updated.PeriodicTickerId, out var parentTicker))
                            {
                                updated.PeriodicTicker = parentTicker;
                            }
                            yield return updated;
                        }
                    }
                }
            }

            await Task.CompletedTask;
        }

        public Task UpdatePeriodicTickerOccurrence(InternalFunctionContext functionContext, CancellationToken cancellationToken = default)
        {
            if (PeriodicOccurrences.TryGetValue(functionContext.TickerId, out var occurrence))
            {
                var updated = CloneOccurrence(occurrence);
                updated.UpdatedAt = _clock.UtcNow;

                var propsToUpdate = functionContext.GetPropsToUpdate();

                if (propsToUpdate.Contains(nameof(InternalFunctionContext.Status)))
                    updated.Status = functionContext.Status;
                if (propsToUpdate.Contains(nameof(InternalFunctionContext.ElapsedTime)))
                    updated.ElapsedTime = functionContext.ElapsedTime;
                if (propsToUpdate.Contains(nameof(InternalFunctionContext.ExceptionDetails)))
                    updated.ExceptionMessage = functionContext.ExceptionDetails;
                if (propsToUpdate.Contains(nameof(InternalFunctionContext.ExecutedAt)))
                    updated.ExecutedAt = functionContext.ExecutedAt;
                if (propsToUpdate.Contains(nameof(InternalFunctionContext.RetryCount)))
                    updated.RetryCount = functionContext.RetryCount;

                PeriodicOccurrences.TryUpdate(functionContext.TickerId, updated, occurrence);
            }

            return Task.CompletedTask;
        }

        // Advances the parent's LastStartedAt to the moment an occurrence was materialized, so the
        // next interval is anchored to the start of the run rather than its completion. Idempotent:
        // a later (max) value wins, so re-locking the same occurrence never moves the anchor backwards.
        // CAS-retry mirrors UpdatePeriodicTickerAfterExecution to survive concurrent snapshot swaps.
        private void AdvanceLastStartedAt(Guid periodicTickerId, DateTime startedAt)
        {
            while (PeriodicTickers.TryGetValue(periodicTickerId, out var ticker))
            {
                if (ticker.LastStartedAt.HasValue && ticker.LastStartedAt.Value >= startedAt)
                    return;

                var updated = CloneTicker(ticker);
                updated.LastStartedAt = startedAt;
                updated.UpdatedAt = _clock.UtcNow;

                if (PeriodicTickers.TryUpdate(periodicTickerId, updated, ticker))
                    return;
            }
        }

        public Task UpdatePeriodicTickerAfterExecution(Guid periodicTickerId, DateTime executedAt, bool succeeded = true, CancellationToken cancellationToken = default)
        {
            // CAS-retry: TryUpdate fails if another writer (e.g. UpdateAsync from the app, or a
            // concurrent scheduler pass) swapped the snapshot in between. A plain TryUpdate would
            // silently drop the schedule advancement, leaving LastExecutedAt stale and reopening the
            // perpetual-refire window. Re-read the current snapshot and retry until the swap sticks.
            while (PeriodicTickers.TryGetValue(periodicTickerId, out var ticker))
            {
                var updated = CloneTicker(ticker);
                updated.LastExecutedAt = executedAt;
                if (succeeded)
                    updated.ExecutionCount = ticker.ExecutionCount + 1;
                updated.UpdatedAt = _clock.UtcNow;

                if (PeriodicTickers.TryUpdate(periodicTickerId, updated, ticker))
                    break;
            }

            return Task.CompletedTask;
        }

        public Task ReleaseAcquiredPeriodicTickerOccurrences(Guid[] occurrenceIds, CancellationToken cancellationToken = default)
        {
            var now = _clock.UtcNow;
            var idsToRelease = occurrenceIds.Length == 0
                ? PeriodicOccurrences.Keys.ToArray()
                : occurrenceIds;

            foreach (var id in idsToRelease)
            {
                if (PeriodicOccurrences.TryGetValue(id, out var occurrence))
                {
                    if (occurrence.LockHolder == _lockHolder && 
                        (occurrence.Status == TickerStatus.Queued || occurrence.Status == TickerStatus.Idle))
                    {
                        var updated = CloneOccurrence(occurrence);
                        updated.LockHolder = null;
                        updated.LockedAt = null;
                        updated.Status = TickerStatus.Idle;
                        updated.UpdatedAt = now;

                        PeriodicOccurrences.TryUpdate(id, updated, occurrence);
                    }
                }
            }

            return Task.CompletedTask;
        }

        public Task<byte[]> GetPeriodicTickerOccurrenceRequest(Guid tickerId, CancellationToken cancellationToken = default)
        {
            if (PeriodicOccurrences.TryGetValue(tickerId, out var occurrence) &&
                PeriodicTickers.TryGetValue(occurrence.PeriodicTickerId, out var ticker))
            {
                return Task.FromResult(ticker.Request);
            }

            return Task.FromResult<byte[]>(null);
        }

        public Task UpdatePeriodicTickerOccurrencesWithUnifiedContext(Guid[] occurrenceIds, InternalFunctionContext functionContext, CancellationToken cancellationToken = default)
        {
            var now = _clock.UtcNow;
            var propsToUpdate = functionContext.GetPropsToUpdate();

            foreach (var id in occurrenceIds)
            {
                if (PeriodicOccurrences.TryGetValue(id, out var occurrence))
                {
                    var updated = CloneOccurrence(occurrence);
                    updated.UpdatedAt = now;

                    if (propsToUpdate.Contains(nameof(InternalFunctionContext.Status)))
                        updated.Status = functionContext.Status;

                    PeriodicOccurrences.TryUpdate(id, updated, occurrence);
                }
            }

            return Task.CompletedTask;
        }

        public Task ReleaseDeadNodePeriodicOccurrenceResources(string instanceIdentifier, CancellationToken cancellationToken = default)
        {
            var now = _clock.UtcNow;

            foreach (var kvp in PeriodicOccurrences)
            {
                var occurrence = kvp.Value;
                if (occurrence.LockHolder == instanceIdentifier &&
                    (occurrence.Status == TickerStatus.Queued || occurrence.Status == TickerStatus.InProgress))
                {
                    var updated = CloneOccurrence(occurrence);
                    updated.LockHolder = null;
                    updated.LockedAt = null;
                    updated.Status = TickerStatus.Idle;
                    updated.UpdatedAt = now;

                    PeriodicOccurrences.TryUpdate(kvp.Key, updated, occurrence);
                }
            }

            return Task.CompletedTask;
        }

        #endregion

        #region Shared Methods

        public Task<TPeriodicTicker> GetPeriodicTickerById(Guid id, CancellationToken cancellationToken = default)
        {
            PeriodicTickers.TryGetValue(id, out var ticker);
            return Task.FromResult(ticker);
        }

        public Task<TPeriodicTicker[]> GetPeriodicTickers(Expression<Func<TPeriodicTicker, bool>> predicate, CancellationToken cancellationToken = default)
        {
            // A null predicate means "match all" (dashboard list endpoints pass null).
            var compiled = predicate?.Compile();
            var values = compiled == null ? PeriodicTickers.Values : PeriodicTickers.Values.Where(compiled);
            return Task.FromResult(values.ToArray());
        }

        public Task<PaginationResult<TPeriodicTicker>> GetPeriodicTickersPaginated(
            Expression<Func<TPeriodicTicker, bool>> predicate,
            int pageNumber,
            int pageSize,
            CancellationToken cancellationToken = default)
        {
            var compiled = predicate?.Compile();
            var filtered = (compiled == null ? PeriodicTickers.Values : PeriodicTickers.Values.Where(compiled)).ToList();
            var total = filtered.Count;
            var items = filtered.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToArray();

            return Task.FromResult(new PaginationResult<TPeriodicTicker>
            {
                Items = items,
                TotalCount = total,
                PageNumber = pageNumber,
                PageSize = pageSize
            });
        }

        public Task<int> InsertPeriodicTickers(TPeriodicTicker[] tickers, CancellationToken cancellationToken = default)
        {
            var count = 0;
            foreach (var ticker in tickers)
            {
                if (PeriodicTickers.TryAdd(ticker.Id, ticker))
                    count++;
            }
            return Task.FromResult(count);
        }

        public Task<int> UpdatePeriodicTickers(TPeriodicTicker[] tickers, CancellationToken cancellationToken = default)
        {
            var count = 0;
            foreach (var ticker in tickers)
            {
                if (PeriodicTickers.TryGetValue(ticker.Id, out var existing))
                {
                    // Schedule-state columns are owned by the scheduler. A caller-supplied entity
                    // (e.g. a dashboard edit) carries null/0 for them, so carry the existing values
                    // forward instead of letting the swap reset the schedule.
                    ticker.LastExecutedAt = existing.LastExecutedAt;
                    ticker.LastStartedAt = existing.LastStartedAt;
                    ticker.ExecutionCount = existing.ExecutionCount;

                    if (PeriodicTickers.TryUpdate(ticker.Id, ticker, existing))
                        count++;
                }
            }
            return Task.FromResult(count);
        }

        public Task<int> RemovePeriodicTickers(Guid[] tickerIds, CancellationToken cancellationToken = default)
        {
            var count = 0;
            foreach (var id in tickerIds)
            {
                if (PeriodicTickers.TryRemove(id, out _))
                {
                    count++;
                    // Also remove related occurrences
                    var occurrencesToRemove = PeriodicOccurrences.Values
                        .Where(x => x.PeriodicTickerId == id)
                        .Select(x => x.Id)
                        .ToArray();
                    
                    foreach (var occId in occurrencesToRemove)
                        PeriodicOccurrences.TryRemove(occId, out _);
                }
            }
            return Task.FromResult(count);
        }

        #endregion

        #region Occurrence Shared Methods

        public Task<PeriodicTickerOccurrenceEntity<TPeriodicTicker>[]> GetAllPeriodicTickerOccurrences(
            Expression<Func<PeriodicTickerOccurrenceEntity<TPeriodicTicker>, bool>> predicate,
            CancellationToken cancellationToken = default)
        {
            // A null predicate means "match all" (dashboard full-data endpoints pass null).
            var compiled = predicate?.Compile();
            var values = compiled == null ? PeriodicOccurrences.Values : PeriodicOccurrences.Values.Where(compiled);
            return Task.FromResult(values.ToArray());
        }

        public Task<PaginationResult<PeriodicTickerOccurrenceEntity<TPeriodicTicker>>> GetAllPeriodicTickerOccurrencesPaginated(
            Expression<Func<PeriodicTickerOccurrenceEntity<TPeriodicTicker>, bool>> predicate,
            int pageNumber,
            int pageSize,
            CancellationToken cancellationToken = default)
        {
            var compiled = predicate?.Compile();
            var filtered = (compiled == null ? PeriodicOccurrences.Values : PeriodicOccurrences.Values.Where(compiled)).ToList();
            var total = filtered.Count;
            var items = filtered.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToArray();

            return Task.FromResult(new PaginationResult<PeriodicTickerOccurrenceEntity<TPeriodicTicker>>
            {
                Items = items,
                TotalCount = total,
                PageNumber = pageNumber,
                PageSize = pageSize
            });
        }

        public Task<int> InsertPeriodicTickerOccurrences(PeriodicTickerOccurrenceEntity<TPeriodicTicker>[] occurrences, CancellationToken cancellationToken = default)
        {
            var count = 0;
            foreach (var occurrence in occurrences)
            {
                if (PeriodicOccurrences.TryAdd(occurrence.Id, occurrence))
                    count++;
            }
            return Task.FromResult(count);
        }

        public Task<int> RemovePeriodicTickerOccurrences(Guid[] occurrenceIds, CancellationToken cancellationToken = default)
        {
            var count = 0;
            foreach (var id in occurrenceIds)
            {
                if (PeriodicOccurrences.TryRemove(id, out _))
                    count++;
            }
            return Task.FromResult(count);
        }

        public Task<PeriodicTickerOccurrenceEntity<TPeriodicTicker>[]> AcquireImmediatePeriodicOccurrencesAsync(Guid[] occurrenceIds, CancellationToken cancellationToken = default)
        {
            var now = _clock.UtcNow;
            var acquired = new List<PeriodicTickerOccurrenceEntity<TPeriodicTicker>>();

            foreach (var id in occurrenceIds)
            {
                if (PeriodicOccurrences.TryGetValue(id, out var occurrence))
                {
                    if (occurrence.Status == TickerStatus.Idle || occurrence.Status == TickerStatus.Queued)
                    {
                        var updated = CloneOccurrence(occurrence);
                        updated.LockHolder = _lockHolder;
                        updated.LockedAt = now;
                        updated.Status = TickerStatus.InProgress;
                        updated.UpdatedAt = now;

                        if (PeriodicOccurrences.TryUpdate(id, updated, occurrence))
                        {
                            if (PeriodicTickers.TryGetValue(updated.PeriodicTickerId, out var parentTicker))
                                updated.PeriodicTicker = parentTicker;

                            acquired.Add(updated);
                        }
                    }
                }
            }

            return Task.FromResult(acquired.ToArray());
        }

        #endregion

        #region Helpers

        private static TPeriodicTicker CloneTicker(TPeriodicTicker source)
        {
            return new TPeriodicTicker
            {
                Id = source.Id,
                Function = source.Function,
                Description = source.Description,
                Interval = source.Interval,
                Request = source.Request,
                Retries = source.Retries,
                RetryIntervals = source.RetryIntervals,
                IsActive = source.IsActive,
                StartTime = source.StartTime,
                EndTime = source.EndTime,
                LastExecutedAt = source.LastExecutedAt,
                LastStartedAt = source.LastStartedAt,
                ExecutionCount = source.ExecutionCount,
                CreatedAt = source.CreatedAt,
                UpdatedAt = source.UpdatedAt,
                ChainTemplate = source.ChainTemplate,
                ChainOverlapBehavior = source.ChainOverlapBehavior
            };
        }

        private static PeriodicTickerOccurrenceEntity<TPeriodicTicker> CloneOccurrence(PeriodicTickerOccurrenceEntity<TPeriodicTicker> source)
        {
            return new PeriodicTickerOccurrenceEntity<TPeriodicTicker>
            {
                Id = source.Id,
                PeriodicTickerId = source.PeriodicTickerId,
                PeriodicTicker = source.PeriodicTicker,
                ExecutionTime = source.ExecutionTime,
                Status = source.Status,
                LockHolder = source.LockHolder,
                LockedAt = source.LockedAt,
                ExecutedAt = source.ExecutedAt,
                ExceptionMessage = source.ExceptionMessage,
                SkippedReason = source.SkippedReason,
                ElapsedTime = source.ElapsedTime,
                RetryCount = source.RetryCount,
                CreatedAt = source.CreatedAt,
                UpdatedAt = source.UpdatedAt
            };
        }

        #endregion
    }
}
