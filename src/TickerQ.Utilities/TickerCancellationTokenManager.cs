using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Models;

namespace TickerQ.Utilities
{
    public static class TickerCancellationTokenManager
    {
        private static readonly ConcurrentDictionary<TickerExecutionKey, TickerCancellationTokenDetails> TickerCancellationTokens = new();
        private static readonly ConcurrentDictionary<TickerExecutionKey, ConcurrentHashSet<TickerExecutionKey>> ParentIdIndex = new();
        private static readonly object[] ParentIndexLocks = CreateParentIndexLocks();

        internal static void AddTickerCancellationToken(CancellationTokenSource cancellationSource, InternalFunctionContext context, bool isDue)
        {
            var details = new TickerCancellationTokenDetails
            {
                FunctionName = context.FunctionName,
                Type = context.Type,
                CancellationSource = cancellationSource,
                IsDue = isDue,
                ParentId = context.ParentId ?? Guid.Empty,
                AcquisitionToken = context.AcquisitionToken
            };

            var key = new TickerExecutionKey(context.Type, context.TickerId);
            if (!TickerCancellationTokens.TryAdd(key, details))
                return;

            if (context.ParentId.HasValue && context.ParentId.Value != Guid.Empty)
                AddToParentIndex(context.ParentId.Value, key, details);
        }

        /// <summary>
        /// Acquisition-time registration: creates a single <see cref="CancellationTokenSource"/>
        /// linked to <paramref name="linkedTokens"/>, atomically registers it under the ticker id,
        /// and returns it so the caller owns its removal/disposal. Returns <c>null</c> (disposing the
        /// source it created) if an entry already exists for this id — a fail-safe against duplicate
        /// registration so the existing owner keeps sole ownership.
        /// </summary>
        internal static CancellationTokenSource TryRegisterAcquired(
            InternalFunctionContext context, bool isDue, params CancellationToken[] linkedTokens)
            => TryRegisterAcquired(context, isDue, out _, linkedTokens);

        internal static CancellationTokenSource TryRegisterAcquired(
            InternalFunctionContext context, bool isDue, out bool generationConflict,
            params CancellationToken[] linkedTokens)
        {
            generationConflict = false;
            var cancellationSource = linkedTokens is { Length: > 0 }
                ? CancellationTokenSource.CreateLinkedTokenSource(linkedTokens)
                : new CancellationTokenSource();

            var details = new TickerCancellationTokenDetails
            {
                FunctionName = context.FunctionName,
                Type = context.Type,
                CancellationSource = cancellationSource,
                IsDue = isDue,
                ParentId = context.ParentId ?? Guid.Empty,
                AcquisitionToken = context.AcquisitionToken
            };

            var key = new TickerExecutionKey(context.Type, context.TickerId);
            while (true)
            {
                if (TickerCancellationTokens.TryAdd(key, details))
                {
                    if (context.ParentId.HasValue && context.ParentId.Value != Guid.Empty)
                        AddToParentIndex(context.ParentId.Value, key, details);
                    return cancellationSource;
                }

                if (!TickerCancellationTokens.TryGetValue(key, out var existing))
                    continue;

                // The same persisted generation is already owned locally.
                if (existing.AcquisitionToken == context.AcquisitionToken)
                {
                    cancellationSource.Dispose();
                    return null;
                }

                // Acquisition tokens are opaque GUIDs: inequality cannot prove recency. Fail closed
                // and let the caller perform an exact, generation-fenced persistence release. A stale
                // candidate's release is a no-op; a current candidate returns to Idle for retry after
                // the watchdog cancels the genuinely lost local generation.
                generationConflict = true;
                cancellationSource.Dispose();
                return null;
            }
        }

        internal static bool RemoveTickerCancellationToken(Guid tickerId)
        {
            var removedTime = RemoveTickerCancellationToken(
                new TickerExecutionKey(TickerType.TimeTicker, tickerId));
            var removedCron = RemoveTickerCancellationToken(
                new TickerExecutionKey(TickerType.CronTickerOccurrence, tickerId));
            return removedTime || removedCron;
        }

        private static bool RemoveTickerCancellationToken(TickerExecutionKey key)
        {
            var removed = TickerCancellationTokens.TryRemove(key, out var details);
            if (!removed || details == null)
                return false;

            try
            {
                details.CancellationSource?.Dispose();
            }
            catch
            {
                // Ignore disposal errors
            }

            if (details.ParentId != Guid.Empty)
                RemoveFromParentIndex(details.ParentId, key, details);

            return true;
        }

        /// <summary>
        /// Owner-side removal: removes the entry only while it still holds <paramref name="ownedSource"/>,
        /// then disposes that source exactly once. The value comparison prevents ABA — a stale owner can
        /// never remove/dispose an entry that was re-registered under the same typed id with a different source.
        /// </summary>
        internal static bool RemoveTickerCancellationToken(Guid tickerId, CancellationTokenSource ownedSource)
        {
            if (ownedSource == null)
                return RemoveTickerCancellationToken(tickerId);

            foreach (var type in new[] { TickerType.TimeTicker, TickerType.CronTickerOccurrence })
            {
                var key = new TickerExecutionKey(type, tickerId);
                if (TickerCancellationTokens.TryGetValue(key, out var details)
                    && ReferenceEquals(details.CancellationSource, ownedSource)
                    && RemoveTickerCancellationToken(key, ownedSource))
                    return true;
            }

            DisposeSource(ownedSource);
            return false;
        }

        internal static bool RemoveTickerCancellationToken(
            TickerExecutionKey key, CancellationTokenSource ownedSource)
        {
            if (!TickerCancellationTokens.TryGetValue(key, out var details)
                || !ReferenceEquals(details.CancellationSource, ownedSource))
            {
                DisposeSource(ownedSource);
                return false;
            }

            var removed = ((ICollection<KeyValuePair<TickerExecutionKey, TickerCancellationTokenDetails>>)TickerCancellationTokens)
                .Remove(new KeyValuePair<TickerExecutionKey, TickerCancellationTokenDetails>(key, details));

            if (removed)
            {
                DisposeSource(details.CancellationSource);

                if (details.ParentId != Guid.Empty)
                    RemoveFromParentIndex(details.ParentId, key, details);
            }
            else
            {
                DisposeSource(ownedSource);
            }

            return removed;
        }

        private static void DisposeSource(CancellationTokenSource source)
        {
            try
            {
                source?.Dispose();
            }
            catch
            {
                // Ignore disposal errors
            }
        }

        internal static void CleanUpTickerCancellationTokens()
        {
            // CRITICAL: Must dispose all CancellationTokenSources before clearing to prevent memory leaks
            foreach (var kvp in TickerCancellationTokens)
            {
                try
                {
                    kvp.Value.CancellationSource?.Dispose();
                }
                catch
                {
                    // Ignore disposal errors during cleanup
                }
            }
            
            TickerCancellationTokens.Clear();
            
            // Dispose all ConcurrentHashSet instances
            foreach (var kvp in ParentIdIndex)
            {
                try
                {
                    kvp.Value?.Dispose();
                }
                catch
                {
                    // Ignore disposal errors during cleanup
                }
            }
            
            ParentIdIndex.Clear();
        }

        /// <summary>Number of ticker executions currently in flight on this node.</summary>
        internal static int ActiveCount => TickerCancellationTokens.Count;

        /// <summary>
        /// Snapshot of currently executing tickers whose DB rows this node lock-holds, split by
        /// type for the lease renewal loop. Each entry carries the <see cref="AcquisitionLease"/>
        /// generation captured at registration so renewal can fence on id + token (a row this node
        /// re-acquired under a newer generation, or one another node recovered, must not be renewed
        /// under a stale snapshot). Chain children are excluded — they execute under their root's
        /// lock and carry no lease of their own.
        /// </summary>
        internal static void SnapshotRunningForLeaseRenewal(List<Guid> timeTickerIds, List<Guid> cronOccurrenceIds)
        {
            foreach (var kvp in TickerCancellationTokens)
            {
                if (kvp.Value.Type == TickerType.CronTickerOccurrence)
                    cronOccurrenceIds.Add(kvp.Key.TickerId);
                else if (kvp.Value.ParentId == Guid.Empty)
                    timeTickerIds.Add(kvp.Key.TickerId);
            }
        }

        /// <summary>
        /// Generation-aware snapshot used by the reliability lease-renewal loop.
        /// </summary>
        internal static void SnapshotRunningForLeaseRenewal(List<AcquisitionLease> timeTickerLeases, List<AcquisitionLease> cronOccurrenceLeases)
        {
            foreach (var kvp in TickerCancellationTokens)
            {
                if (kvp.Value.Type == TickerType.CronTickerOccurrence)
                    cronOccurrenceLeases.Add(new AcquisitionLease(kvp.Key.TickerId, kvp.Value.AcquisitionToken));
                else if (kvp.Value.ParentId == Guid.Empty)
                    timeTickerLeases.Add(new AcquisitionLease(kvp.Key.TickerId, kvp.Value.AcquisitionToken));
            }
        }

        public static bool RequestTickerCancellationById(Guid tickerId)
        {
            // ID-only callers cannot distinguish persistence namespaces, so cancel every matching
            // local execution rather than arbitrarily selecting one.
            var cancelledTime = RequestTickerCancellation(
                new TickerExecutionKey(TickerType.TimeTicker, tickerId));
            var cancelledCron = RequestTickerCancellation(
                new TickerExecutionKey(TickerType.CronTickerOccurrence, tickerId));
            return cancelledTime || cancelledCron;
        }

        internal static bool RequestTickerCancellation(TickerExecutionKey key)
        {
            // Signal only — never remove or dispose here. The owner performs cleanup.
            if (!TickerCancellationTokens.TryGetValue(key, out var details))
                return false;

            try
            {
                details.CancellationSource?.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Owner disposed it concurrently — the execution is already ending, safe to ignore.
            }

            return true;
        }

        internal static bool RequestTickerCancellation(TickerExecutionLease lease)
        {
            var key = new TickerExecutionKey(lease.Type, lease.TickerId);
            if (!TickerCancellationTokens.TryGetValue(key, out var details)
                || details.AcquisitionToken != lease.AcquisitionToken)
                return false;

            try
            {
                details.CancellationSource?.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }

            return true;
        }

        private static void AddToParentIndex(
            Guid parentId, TickerExecutionKey key, TickerCancellationTokenDetails details)
        {
            lock (GetParentIndexLock(parentId))
            {
                if (!TickerCancellationTokens.TryGetValue(key, out var current)
                    || !ReferenceEquals(current, details)
                    || current.ParentId != parentId)
                    return;

                var parentKey = new TickerExecutionKey(key.Type, parentId);
                var set = ParentIdIndex.GetOrAdd(parentKey, static _ => new ConcurrentHashSet<TickerExecutionKey>());
                set.Add(key);
            }
        }

        private static void RemoveFromParentIndex(
            Guid parentId, TickerExecutionKey key, TickerCancellationTokenDetails removedDetails)
        {
            lock (GetParentIndexLock(parentId))
            {
                var parentKey = new TickerExecutionKey(key.Type, parentId);
                if (TickerCancellationTokens.TryGetValue(key, out var current)
                    && current.ParentId == parentId
                    && !ReferenceEquals(current, removedDetails))
                    return;

                if (!ParentIdIndex.TryGetValue(parentKey, out var set))
                    return;

                set.Remove(key);
                if (set.IsEmpty)
                {
                    ((ICollection<KeyValuePair<TickerExecutionKey, ConcurrentHashSet<TickerExecutionKey>>>)ParentIdIndex)
                        .Remove(new KeyValuePair<TickerExecutionKey, ConcurrentHashSet<TickerExecutionKey>>(parentKey, set));
                }
            }
        }

        private static object GetParentIndexLock(Guid parentId)
            => ParentIndexLocks[(int)((uint)parentId.GetHashCode() % ParentIndexLocks.Length)];

        private static object[] CreateParentIndexLocks()
        {
            var locks = new object[64];
            for (var i = 0; i < locks.Length; i++)
                locks[i] = new object();
            return locks;
        }

        /// <summary>
        /// Fast O(1) lookup to check if any tickers are running for a given parent ID.
        /// This replaces the expensive LINQ Any() operation with a direct dictionary lookup.
        /// </summary>
        /// <param name="parentId">The parent ID to check</param>
        /// <returns>True if any tickers are running for this parent ID</returns>
        public static bool IsParentRunning(Guid parentId)
        {
            return ParentIdIndex.ContainsKey(new TickerExecutionKey(TickerType.TimeTicker, parentId))
                || ParentIdIndex.ContainsKey(new TickerExecutionKey(TickerType.CronTickerOccurrence, parentId));
        }
        
        /// <summary>
        /// Checks if any OTHER tickers (excluding the current one) are running for a given parent ID.
        /// Used to prevent false positives when checking if a sibling occurrence is already running.
        /// </summary>
        /// <param name="parentId">The parent ID to check</param>
        /// <param name="excludeTickerId">The ticker ID to exclude from the check (usually the current ticker)</param>
        /// <returns>True if any other tickers are running for this parent ID</returns>
        public static bool IsParentRunningExcludingSelf(Guid parentId, Guid excludeTickerId)
        {
            if (!ParentIdIndex.TryGetValue(
                    new TickerExecutionKey(TickerType.CronTickerOccurrence, parentId), out var tickerSet))
                return false;
            
            return tickerSet.HasOtherItemsBesides(
                new TickerExecutionKey(TickerType.CronTickerOccurrence, excludeTickerId));
        }
    }

    public class TickerCancellationTokenDetails 
    {
        public string FunctionName { get; set; }
        public TickerType Type { get; set; }
        public bool IsDue { get; set; }
        public CancellationTokenSource CancellationSource { get; set; }
        public Guid ParentId { get; set; }
        /// <summary>
        /// InProgress generation this execution was acquired under, used to fence lease renewal
        /// (see <see cref="TickerCancellationTokenManager.SnapshotRunningForLeaseRenewal"/>).
        /// Null for executions acquired by a provider that does not mint generation tokens.
        /// </summary>
        public Guid? AcquisitionToken { get; set; }
    }
    
    /// <summary>
    /// Thread-safe HashSet implementation for concurrent operations
    /// </summary>
    public class ConcurrentHashSet<T> : IDisposable
    {
        private readonly HashSet<T> _set = new();
        private readonly ReaderWriterLockSlim _lock = new();
        
        public bool Add(T item)
        {
            _lock.EnterWriteLock();
            try { return _set.Add(item); }
            finally { _lock.ExitWriteLock(); }
        }
        
        public bool Remove(T item)
        {
            _lock.EnterWriteLock();
            try { return _set.Remove(item); }
            finally { _lock.ExitWriteLock(); }
        }
        
        public bool IsEmpty
        {
            get
            {
                _lock.EnterReadLock();
                try { return _set.Count == 0; }
                finally { _lock.ExitReadLock(); }
            }
        }
        
        /// <summary>
        /// Checks if there are any items in the set other than the specified excluded item.
        /// </summary>
        /// <param name="excludeItem">The item to exclude from the check</param>
        /// <returns>True if there are other items besides the excluded one</returns>
        public bool HasOtherItemsBesides(T excludeItem)
        {
            _lock.EnterReadLock();
            try
            {
                if (_set.Count == 0)
                    return false;
                    
                if (_set.Count == 1)
                    return !_set.Contains(excludeItem);
                    
                // Multiple items - at least one must be different from excludeItem
                return true;
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }
        
        public void Dispose()
        {
            _lock?.Dispose();
        }
    }
}