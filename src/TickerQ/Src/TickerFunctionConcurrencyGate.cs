using System.Collections.Concurrent;
using System.Threading;
using TickerQ.Utilities.Interfaces;

namespace TickerQ
{
    internal sealed class TickerFunctionConcurrencyGate : ITickerFunctionConcurrencyGate
    {
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _semaphores = new();

        public SemaphoreSlim GetSemaphoreOrNull(string functionName, int maxConcurrency)
        {
            if (maxConcurrency <= 0)
                return null;

            return _semaphores.GetOrAdd(functionName, _ => new SemaphoreSlim(maxConcurrency, maxConcurrency));
        }
    }
}
