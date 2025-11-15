using System;
using System.Threading;

namespace TickerQ
{
    public sealed class SoftSchedulerNotifyDebounce : IDisposable
    {
        private readonly Action<string> _notifyCoreAction;
        private readonly Timer _timer;

        private int _latest;
        private int _lastNotified = -1;

        private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(100);
        private int _disposed;

        public SoftSchedulerNotifyDebounce(Action<string> notifyCoreAction)
        {
            _notifyCoreAction = notifyCoreAction;
            _timer = new Timer(Callback, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }

        /// <summary>
        /// Sends notifications in a thread-safe way and suppresses duplicates.
        /// Fires immediately for every change.
        /// </summary>
        internal void NotifySafely(int count)
        {
            Volatile.Write(ref _latest, count);

            if (Volatile.Read(ref _disposed) == 1)
                return;

            // Call immediately so the reported thread count stays in sync
            Callback(null);
        }

        /// <summary>
        /// Synchronously push the latest value now (used on shutdown).
        /// </summary>
        internal void Flush()
        {
            Callback(null);
        }

        private void Callback(object _)
        {
            if (Volatile.Read(ref _disposed) == 1)
                return;

            var latest = Volatile.Read(ref _latest);
            var last   = Volatile.Read(ref _lastNotified);

            if (latest != 0 && latest == last)
                return;

            Volatile.Write(ref _lastNotified, latest);
            
            _notifyCoreAction?.Invoke(latest.ToString());
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1)
                return;

            try { _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan); } catch { /* ignore */ }
            _timer.Dispose();
        }
    }
}
