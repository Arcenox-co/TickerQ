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
            
            // Remove from parent index if it exists
            if (removed && details?.ParentId != null && details.ParentId != Guid.Empty)
            {
                if (ParentIdIndex.TryGetValue(details.ParentId, out var set))
                {
                    set.Remove(tickerId);
                    // Clean up empty sets
                    if (set.IsEmpty)
                        ParentIdIndex.TryRemove(details.ParentId, out _);
                }
            }
            
            return removed;
        }

        internal static void CleanUpTickerCancellationTokens()
        {
            TickerCancellationTokens.Clear();
            ParentIdIndex.Clear();
        }

        public static bool RequestTickerCancellationById(Guid tickerId)
        {
            var existTickerCancellationToken = TickerCancellationTokens.TryRemove(tickerId, out var tickerCancellationToken);
            
            if(existTickerCancellationToken)
            {
                tickerCancellationToken.CancellationSource.Cancel();
                
                // Remove from parent index if it exists
                if (tickerCancellationToken.ParentId != Guid.Empty)
                {
                    if (ParentIdIndex.TryGetValue(tickerCancellationToken.ParentId, out var set))
                    {
                        set.Remove(tickerId);
                        if (set.IsEmpty)
                            ParentIdIndex.TryRemove(tickerCancellationToken.ParentId, out _);
                    }
                }
            }
            
            return existTickerCancellationToken;
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
        
        public void Dispose()
        {
            _lock?.Dispose();
        }
    }
}