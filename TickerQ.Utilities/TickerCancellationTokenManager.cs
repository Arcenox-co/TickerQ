using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using TickerQ.Utilities.Enums;

namespace TickerQ.Utilities
{
    public static class TickerCancellationTokenManager
    {
        private static readonly ConcurrentDictionary<Guid, TickerCancellationTokenDetails>  TickerCancellationTokensDictionary = new ConcurrentDictionary<Guid, TickerCancellationTokenDetails>();
        public static IReadOnlyDictionary<Guid, TickerCancellationTokenDetails> TickerCancellationTokens => TickerCancellationTokensDictionary;

        internal static void AddTickerCancellationToken(CancellationTokenSource cancellationSource, string functionName, Guid tickerId, TickerType type, bool isDue)
        {
            TickerCancellationTokensDictionary.TryAdd(tickerId, new TickerCancellationTokenDetails
            {
                FunctionName = functionName,
                Type = type,
                CancellationSource = cancellationSource,
                IsDue = isDue
            });
        }
        
        internal static bool RemoveTickerCancellationToken(Guid tickerId)
            => TickerCancellationTokensDictionary.TryRemove(tickerId, out _);

        internal static void CleanUpTickerCancellationTokens()
            => TickerCancellationTokensDictionary.Clear();

        public static bool RequestTickerCancellationById(Guid tickerId)
        {
            var existTickerCancellationToken = TickerCancellationTokensDictionary.TryGetValue(tickerId, out var tickerCancellationToken);
            
            if(existTickerCancellationToken) 
                tickerCancellationToken.CancellationSource.Cancel();
            
            return existTickerCancellationToken;
        }
    }

    public class TickerCancellationTokenDetails 
    {
        public string FunctionName { get; set; }
        public TickerType Type { get; set; }
        public bool IsDue { get; set; }
        public CancellationTokenSource CancellationSource { get; set; }
    }
}