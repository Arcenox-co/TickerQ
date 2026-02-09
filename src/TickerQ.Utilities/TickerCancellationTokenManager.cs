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

        internal static void AddTickerCancellationToken(CancellationTokenSource cancellationSource, InternalFunctionContext context, bool isDue)
        {
            var details = new TickerCancellationTokenDetails
            {
                FunctionName = context.FunctionName,
                Type = context.Type,
                CancellationSource = cancellationSource,
                IsDue = isDue,
                ParentId = context.ParentId ?? Guid.Empty
            };
            
            TickerCancellationTokens.TryAdd(context.TickerId, details);
            
            // Add to parent index for fast lookup if parentId exists
            if (context.ParentId.HasValue && context.ParentId.Value != Guid.Empty)
            {
                ParentIdIndex.AddOrUpdate(context.ParentId.Value,
                    key => { var set = new ConcurrentHashSet<Guid>(); set.Add(context.TickerId); return set; },
                    (key, existing) => { existing.Add(context.TickerId); return existing; });
            }
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
                    RemoveFromParentIndex(details.ParentId, tickerId);
                }
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

        public static bool RequestTickerCancellationById(Guid tickerId)
        {
            // Cancel while the entry is still tracked so IsParentRunning remains accurate
            if (!TickerCancellationTokens.TryGetValue(tickerId, out var details))
                return false;

            // Signal cancellation while the entry is still tracked
            try
            {
                details.CancellationSource?.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Already disposed by another thread - safe to ignore
            }

            // Now remove and dispose
            if (TickerCancellationTokens.TryRemove(tickerId, out var removed))
            {
                try
                {
                    removed.CancellationSource?.Dispose();
                }
                catch
                {
                    // Ignore disposal errors
                }

                if (removed.ParentId != Guid.Empty)
                {
                    RemoveFromParentIndex(removed.ParentId, tickerId);
                }
            }

            return true;
        }
        
        /// <summary>
        /// Atomically removes a ticker from the parent index, cleaning up the set if empty.
        /// Uses TryRemove with value comparison to avoid TOCTOU races.
        /// </summary>
        private static void RemoveFromParentIndex(Guid parentId, Guid tickerId)
        {
            if (!ParentIdIndex.TryGetValue(parentId, out var set))
                return;

            set.Remove(tickerId);

            // Only remove the set from the dictionary if it's still empty.
            // Use the ICollection<KVP> remove overload for atomic check-and-remove.
            if (set.IsEmpty)
            {
                ((ICollection<KeyValuePair<Guid, ConcurrentHashSet<Guid>>>)ParentIdIndex)
                    .Remove(new KeyValuePair<Guid, ConcurrentHashSet<Guid>>(parentId, set));
            }
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