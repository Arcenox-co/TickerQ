using System;
using System.Threading;

namespace TickerQ
{
    public sealed class SafeCancellationTokenSource : IDisposable
    {
        private readonly CancellationTokenSource _innerCts;
        private readonly Action<AggregateException> _onCallbackFailure;
        private CancellationTokenRegistration[] _parentRegistrations = [];
        private int _disposed;

        private SafeCancellationTokenSource(CancellationTokenSource cts, Action<AggregateException> onCallbackFailure)
        {
            _innerCts = cts;
            _onCallbackFailure = onCallbackFailure;
        }

        /// <summary>
        /// Creates a new SafeCancellationTokenSource that is not linked to anything else.
        /// </summary>
        public SafeCancellationTokenSource()
            : this(new CancellationTokenSource(), null)
        {
        }

        /// <summary>
        /// Creates a SafeCancellationTokenSource linked to the specified tokens.
        /// Any cancellation request in these tokens triggers this source to cancel as well.
        /// </summary>
        public static SafeCancellationTokenSource CreateLinked(params CancellationToken[] tokens)
            => new(CancellationTokenSource.CreateLinkedTokenSource(tokens), null);

        /// <summary>
        /// Creates a source linked to <paramref name="tokens"/> that confines failures of callbacks
        /// registered on its own token. Cancelling a token runs every callback registered on it, such
        /// as a database driver's for a query in flight, and one that throws makes
        /// <see cref="CancellationTokenSource.Cancel()"/> throw. Here such a failure goes to
        /// <paramref name="onCallbackFailure"/> instead: neither <see cref="Cancel()"/> nor the
        /// cancellation of a parent token throws it, so a parent owned by someone else (a host's
        /// stopping token, a timer) is never failed by callbacks on this source.
        /// </summary>
        public static SafeCancellationTokenSource CreateLinked(Action<AggregateException> onCallbackFailure,
            params CancellationToken[] tokens)
        {
            ArgumentNullException.ThrowIfNull(onCallbackFailure);
            var source = new SafeCancellationTokenSource(new CancellationTokenSource(), onCallbackFailure);
            var registrations = new CancellationTokenRegistration[tokens.Length];
            // A token that is already cancelled runs the callback here, cancelling the new source.
            for (var i = 0; i < tokens.Length; i++)
                registrations[i] = tokens[i].UnsafeRegister(static state => ((SafeCancellationTokenSource)state).Cancel(), source);
            source._parentRegistrations = registrations;
            return source;
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
            // The token is cancelled before its callbacks run, so this reports a callback's failure,
            // not a cancellation that did not happen.
            catch (AggregateException exception) when (_onCallbackFailure != null)
            {
                _onCallbackFailure(exception);
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
            catch (Exception exception) when (_onCallbackFailure != null && exception is not OperationCanceledException)
            {
                _onCallbackFailure(exception as AggregateException ?? new AggregateException(exception));
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

            foreach (var registration in _parentRegistrations)
                registration.Dispose();
            _innerCts.Dispose();
        }
    }
}
