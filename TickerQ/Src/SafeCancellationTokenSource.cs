using System;
using System.Threading;

namespace TickerQ
{
    public sealed class SafeCancellationTokenSource : IDisposable
    {
        private readonly CancellationTokenSource _innerCts;

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

        public bool IsDisposed { get; private set; }

        public void Cancel(){
            if(!IsDisposed)
                _innerCts.Cancel();
        }

        public void Cancel(bool throwOnFirstException) => _innerCts.Cancel(throwOnFirstException);

        public void CancelAfter(TimeSpan delay) => _innerCts.CancelAfter(delay);

        public void CancelAfter(int millisecondsDelay) => _innerCts.CancelAfter(millisecondsDelay);

        public void Dispose()
        {
            if (IsDisposed) return;
            IsDisposed = true;
            _innerCts.Dispose();
        }
    }
}