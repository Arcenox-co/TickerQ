using System;
using System.Threading;

namespace TickerQ.Base
{
    public sealed class RestartThrottleManager
    {
        private const int MaxBurstCount = 256;
        private readonly DateTime[] _timestampBuffer = new DateTime[MaxBurstCount];

        private int _start = 0;
        private int _count = 0;

        private readonly TimeSpan _burstWindow = TimeSpan.FromMilliseconds(20);
        private readonly TimeSpan _cooldownDelay = TimeSpan.FromSeconds(2);
        private readonly TimeSpan _maxExtraDelay = TimeSpan.FromSeconds(1);
        private readonly TimeSpan _postCooldownDebounceDelay = TimeSpan.FromMilliseconds(100);

        private TimeSpan _extraDelay = TimeSpan.Zero;
        private DateTime _lastRestartRequestAt = DateTime.MinValue;
        private DateTime _lastRequestTimeAfterCooldown = DateTime.MinValue;

        private int _inCooldown = 0; // atomic flag
        private bool _isWaitingForDebouncedRestart = false;

        private Timer _pendingRestartTimer = null;
        private Timer _postCooldownIdleTimer = null;

        private readonly Action _onRestartTriggered;
        private readonly object _lock = new object();

        public RestartThrottleManager(Action onRestartTriggered)
        {
            _onRestartTriggered = onRestartTriggered ?? throw new ArgumentNullException(nameof(onRestartTriggered));
        }

        public void RequestRestart()
        {
            var now = DateTime.UtcNow;

            lock (_lock)
            {
                RemoveOld(now);
                AddTimestamp(now);

                if (_isWaitingForDebouncedRestart)
                {
                    _lastRequestTimeAfterCooldown = now;
                    return;
                }

                if (_count >= MaxBurstCount)
                {
                    if (Interlocked.CompareExchange(ref _inCooldown, 1, 0) == 0)
                    {
                        _pendingRestartTimer?.Dispose();
                        _pendingRestartTimer = new Timer(_ =>
                        {
                            lock (_lock)
                            {
                                _count = 0;
                                _start = 0;
                                _extraDelay = TimeSpan.Zero;
                                _pendingRestartTimer?.Dispose();
                                _pendingRestartTimer = null;
                                Interlocked.Exchange(ref _inCooldown, 0);

                                _lastRequestTimeAfterCooldown = DateTime.UtcNow;
                                _isWaitingForDebouncedRestart = true;

                                ResetPostCooldownIdleTimer();
                            }
                        }, null, _cooldownDelay, Timeout.InfiniteTimeSpan);
                    }
                    return;
                }

                TimeSpan delay;
                if (_count == 1)
                {
                    delay = TimeSpan.FromMilliseconds(200);
                }
                else
                {
                    if ((now - _lastRestartRequestAt) < TimeSpan.FromMilliseconds(300))
                    {
                        _extraDelay = TimeSpan.FromMilliseconds(
                            Math.Min((_extraDelay.TotalMilliseconds * 2) + 50, _maxExtraDelay.TotalMilliseconds));
                    }
                    else
                    {
                        _extraDelay = TimeSpan.Zero;
                    }
                    delay = _extraDelay;
                }

                _lastRestartRequestAt = now;

                _pendingRestartTimer?.Dispose();
                _pendingRestartTimer = new Timer(_ =>
                {
                    lock (_lock)
                    {
                        _pendingRestartTimer?.Dispose();
                        _pendingRestartTimer = null;
                        _onRestartTriggered();
                    }
                }, null, delay, Timeout.InfiniteTimeSpan);
            }
        }

        private void ResetPostCooldownIdleTimer()
        {
            _postCooldownIdleTimer?.Dispose();
            _postCooldownIdleTimer = new Timer(_ =>
            {
                lock (_lock)
                {
                    var now = DateTime.UtcNow;
                    if ((now - _lastRequestTimeAfterCooldown) >= _postCooldownDebounceDelay)
                    {
                        _postCooldownIdleTimer?.Dispose();
                        _postCooldownIdleTimer = null;
                        _isWaitingForDebouncedRestart = false;
                        _onRestartTriggered();
                    }
                    else
                    {
                        ResetPostCooldownIdleTimer();
                    }
                }
            }, null, _postCooldownDebounceDelay, Timeout.InfiniteTimeSpan);
        }

        private void AddTimestamp(DateTime timestamp)
        {
            int index = (_start + _count) % MaxBurstCount;
            _timestampBuffer[index] = timestamp;

            if (_count < MaxBurstCount)
                _count++;
            else
                _start = (_start + 1) % MaxBurstCount;
        }

        private void RemoveOld(DateTime now)
        {
            while (_count > 0)
            {
                var oldest = _timestampBuffer[_start];
                if ((now - oldest) <= _burstWindow)
                    break;

                _start = (_start + 1) % MaxBurstCount;
                _count--;
            }
        }
    }
}
