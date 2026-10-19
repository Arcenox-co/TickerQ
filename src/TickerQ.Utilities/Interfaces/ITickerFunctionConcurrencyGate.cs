using System;
using System.Threading;
using System.Threading.Tasks;

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

        /// <summary>
        /// Acquires a function permit whose ownership ends on disposal. Implementations can
        /// reclaim idle gates after their last holder or waiter releases its reference.
        /// </summary>
        async ValueTask<IDisposable> AcquireAsync(string functionName, int maxConcurrency,
            CancellationToken cancellationToken = default)
        {
            var semaphore = GetSemaphoreOrNull(functionName, maxConcurrency);
            if (semaphore == null) return null;
            await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            return new FunctionConcurrencyPermit(semaphore);
        }
    }

    internal sealed class FunctionConcurrencyPermit(SemaphoreSlim semaphore) : IDisposable
    {
        private SemaphoreSlim _semaphore = semaphore;
        public void Dispose() => Interlocked.Exchange(ref _semaphore, null)?.Release();
    }
}
