using System.Threading;
using TickerQ.Utilities;

namespace TickerQ
{
    internal static class SoftSchedulerNotifyDebounce
    {
        private static Timer _debounceTimer;
        private static int _latestCount = -1;
        private static int _lastNotified = -1;

        internal static void NotifySafely(int count)
        {
            _latestCount = count;

            _debounceTimer?.Dispose();
            _debounceTimer = new Timer(_ =>
            {
                // Always notify if count is 0 (reset signal)
                if (_latestCount != 0 && _latestCount == _lastNotified)
                    return;
                
                _lastNotified = _latestCount;
                
                TickerOptionsBuilder.NotifyThreadCountFunc?.Invoke(count);
                
            }, null, 100, Timeout.Infinite);
        }
    }
}