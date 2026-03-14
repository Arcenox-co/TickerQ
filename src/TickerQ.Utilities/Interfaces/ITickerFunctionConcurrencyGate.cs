using System.Threading;

namespace TickerQ.Utilities.Interfaces
{
    public interface ITickerFunctionConcurrencyGate
    {
        /// <summary>
        /// Returns a <see cref="SemaphoreSlim"/> that limits concurrency for the given function,
        /// or <c>null</c> when <paramref name="maxConcurrency"/> is 0 (unlimited).
        /// The semaphore is created lazily and cached for the lifetime of the application.
        /// </summary>
        SemaphoreSlim GetSemaphoreOrNull(string functionName, int maxConcurrency);
    }
}
