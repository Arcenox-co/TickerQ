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
        private static readonly ConcurrentDictionary<Guid, TickerCancellationTokenDetails>  TickerCancellationTokens = new();
        private static readonly ConcurrentDictionary<Guid, ConcurrentHashSet<Guid>> ParentIdIndex = new();
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

            if (!TickerCancellationTokens.TryAdd(context.TickerId, details))
                return;

            if (context.ParentId.HasValue && context.ParentId.Value != Guid.Empty)
                AddToParentIndex(context.ParentId.Value, context.TickerId);
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
        {
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

            // Atomic register: the id occupies the dictionary for the whole execution, so a second
            // acquisition of the same id fails here rather than racing a second CTS into flight.
            if (!TickerCancellationTokens.TryAdd(context.TickerId, details))
            {
                cancellationSource.Dispose();
                return null;
            }

            if (context.ParentId.HasValue && context.ParentId.Value != Guid.Empty)
                AddToParentIndex(context.ParentId.Value, context.TickerId);

            return cancellationSource;
        }

        internal static bool RemoveTickerCancellationToken(Guid tickerId)
        {
            var removed = TickerCancellationTokens.TryRemove(tickerId, out var details);
            
            if (removed && details != null)
            {
                // CRITICAL: Dispose CancellationTokenSource to prevent memory leak
                try
                {
                    details.CancellationSource?.Dispose();
                }
                catch
                {
                    // Ignore disposal errors
                }
                
                // Remove from parent index if it exists
                if (details.ParentId != Guid.Empty)
                {
                    RemoveFromParentIndex(details.ParentId, tickerId, details);
                }
            }

            return removed;
        }

        /// <summary>
        /// Owner-side removal: removes the entry only while it still holds <paramref name="ownedSource"/>,
        /// then disposes that source exactly once. The value comparison prevents ABA — a stale owner can
        /// never remove/dispose an entry that was re-registered under the same id with a different source.
        /// </summary>
        internal static bool RemoveTickerCancellationToken(Guid tickerId, CancellationTokenSource ownedSource)
        {
            if (ownedSource == null)
                return RemoveTickerCancellationToken(tickerId);

            if (!TickerCancellationTokens.TryGetValue(tickerId, out var details)
                || !ReferenceEquals(details.CancellationSource, ownedSource))
                return false;

            // Atomic compare-and-remove on the exact key/value pair (reference equality on the
            // details), so only this owner's registration is removed even under concurrent churn.
            var removed = ((ICollection<KeyValuePair<Guid, TickerCancellationTokenDetails>>)TickerCancellationTokens)
                .Remove(new KeyValuePair<Guid, TickerCancellationTokenDetails>(tickerId, details));

            if (removed)
            {
                try
                {
                    details.CancellationSource?.Dispose();
                }
                catch
                {
                    // Ignore disposal errors
                }

                if (details.ParentId != Guid.Empty)
                    RemoveFromParentIndex(details.ParentId, tickerId, details);
            }

            return removed;
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
                    cronOccurrenceIds.Add(kvp.Key);
                else if (kvp.Value.ParentId == Guid.Empty)
                    timeTickerIds.Add(kvp.Key);
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
                    cronOccurrenceLeases.Add(new AcquisitionLease(kvp.Key, kvp.Value.AcquisitionToken));
                else if (kvp.Value.ParentId == Guid.Empty)
                    timeTickerLeases.Add(new AcquisitionLease(kvp.Key, kvp.Value.AcquisitionToken));
            }
        }

        public static bool RequestTickerCancellationById(Guid tickerId)
        {
            // Signal only — never remove or dispose here. The owner (the finally in the execution
            // handler / scheduler delegate) performs removal and disposal exactly once. Disposing
            // the source here as well would race the still-running execution that holds the linked
            // token (disposed-source race) and, once the id is re-registered, could remove the wrong
            // registration (ABA). The entry stays tracked until the owner observes cancellation and
            // cleans up, which keeps IsParentRunning / lease renewal accurate in the meantime.
            if (!TickerCancellationTokens.TryGetValue(tickerId, out var details))
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

        private static void AddToParentIndex(Guid parentId, Guid tickerId)
        {
            lock (GetParentIndexLock(parentId))
            {
                var set = ParentIdIndex.GetOrAdd(parentId, static _ => new ConcurrentHashSet<Guid>());
                set.Add(tickerId);
            }
        }

        private static void RemoveFromParentIndex(
            Guid parentId, Guid tickerId, TickerCancellationTokenDetails removedDetails)
        {
            lock (GetParentIndexLock(parentId))
            {
                if (TickerCancellationTokens.TryGetValue(tickerId, out var current)
                    && current.ParentId == parentId
                    && !ReferenceEquals(current, removedDetails))
                    return;

                if (!ParentIdIndex.TryGetValue(parentId, out var set))
                    return;

                set.Remove(tickerId);
                if (set.IsEmpty)
                {
                    ((ICollection<KeyValuePair<Guid, ConcurrentHashSet<Guid>>>)ParentIdIndex)
                        .Remove(new KeyValuePair<Guid, ConcurrentHashSet<Guid>>(parentId, set));
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
            return ParentIdIndex.ContainsKey(parentId);
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
            if (!ParentIdIndex.TryGetValue(parentId, out var tickerSet))
                return false;
            
            return tickerSet.HasOtherItemsBesides(excludeTickerId);
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