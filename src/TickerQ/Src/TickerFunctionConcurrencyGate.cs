using System.Collections.Concurrent;
using System.Threading;

namespace TickerQ
{
    internal interface ITickerFunctionConcurrencyGate
    {
        /// <summary>
        /// Returns a <see cref="SemaphoreSlim"/> that limits concurrency for the given function,
        /// or <c>null</c> when <paramref name="maxConcurrency"/> is 0 (unlimited).
        /// The semaphore is created lazily and cached for the lifetime of the application.
        /// </summary>
        SemaphoreSlim GetSemaphoreOrNull(string functionName, int maxConcurrency);
    }

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
