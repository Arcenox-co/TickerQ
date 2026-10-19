using System;
using System.Threading;

namespace TickerQ;

public sealed class RestartThrottleManager : IDisposable
{
    private readonly Action _onRestartTriggered;
    private readonly TimeProvider _timeProvider;
    private readonly object _lock = new();
    private ITimer _debounceTimer;
    private volatile bool _restartPending;
    private bool _disposed;

    private readonly TimeSpan _debounceWindow = TimeSpan.FromMilliseconds(50);

    public RestartThrottleManager(Action onRestartTriggered)
        : this(onRestartTriggered, TimeProvider.System)
    {
    }

    internal RestartThrottleManager(Action onRestartTriggered, TimeProvider timeProvider)
    {
        _onRestartTriggered = onRestartTriggered;
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public void RequestRestart()
    {
        lock (_lock)
        {
            // Manager calls can arrive while the owning scheduler is shutting down.
            if (_disposed)
                return;

            _restartPending = true;

            // Create timer only when first needed
            if (_debounceTimer == null)
            {
                _debounceTimer = _timeProvider.CreateTimer(OnTimerCallback, null,
                    _debounceWindow, Timeout.InfiniteTimeSpan);
            }
            else
            {
                // Just reset existing timer
                _debounceTimer.Change(_debounceWindow, Timeout.InfiniteTimeSpan);
            }
        }
    }

    private void OnTimerCallback(object state)
    {
        lock (_lock)
        {
            // Timer.Dispose does not wait for callbacks already queued or waiting for this lock.
            if (_disposed || !_restartPending)
                return;

            _restartPending = false;
            _debounceTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            // Keep invocation serialized with disposal so a callback cannot start after Dispose returns.
            _onRestartTriggered();
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
                return;

            _disposed = true;
            _restartPending = false;
            _debounceTimer?.Dispose();
            _debounceTimer = null;
        }
    }
}
