using System;
using System.Threading;

namespace TickerQ
{
    public sealed class SafeCancellationTokenSource : IDisposable
    {
        private readonly CancellationTokenSource _innerCts;
        private int _disposed;

        private SafeCancellationTokenSource(CancellationTokenSource cts)
        {
            _innerCts = cts;
        }

        /// <summary>
        /// Creates a new SafeCancellationTokenSource that is not linked to anything else.
        /// </summary>
        public SafeCancellationTokenSource()
            : this(new CancellationTokenSource())
        {
        }

        /// <summary>
        /// Creates a SafeCancellationTokenSource linked to the specified tokens.
        /// Any cancellation request in these tokens triggers this source to cancel as well.
        /// </summary>
        public static SafeCancellationTokenSource CreateLinked(params CancellationToken[] tokens)
        {
            return new SafeCancellationTokenSource(
                CancellationTokenSource.CreateLinkedTokenSource(tokens)
            );
        }

        public CancellationToken Token => _innerCts.Token;

        public bool IsCancellationRequested => _innerCts.IsCancellationRequested;

        public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

        public void Cancel()
        {
            if (IsDisposed)
                return;

            try
            {
                _innerCts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Race between Cancel and Dispose — safe to ignore
            }
        }

        public void Cancel(bool throwOnFirstException)
        {
            if (IsDisposed)
                return;

            try
            {
                _innerCts.Cancel(throwOnFirstException);
            }
            catch (ObjectDisposedException)
            {
                // Race between Cancel and Dispose — safe to ignore
            }
        }

        public void CancelAfter(TimeSpan delay)
        {
            if (IsDisposed)
                return;

            try
            {
                _innerCts.CancelAfter(delay);
            }
            catch (ObjectDisposedException)
            {
                // Race between CancelAfter and Dispose — safe to ignore
            }
        }

        public void CancelAfter(int millisecondsDelay)
        {
            if (IsDisposed)
                return;

            try
            {
                _innerCts.CancelAfter(millisecondsDelay);
            }
            catch (ObjectDisposedException)
            {
                // Race between CancelAfter and Dispose — safe to ignore
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            _innerCts.Dispose();
        }
    }
}
