using System.Reflection;

namespace TickerQ.Tests;

public class RestartThrottleManagerTests
{
    [Fact]
    public void RequestRestart_TriggersCallback_AfterDebounceWindow()
    {
        var clock = new ManualTimeProvider();
        var triggerCount = 0;
        using var manager = new RestartThrottleManager(() => triggerCount++, clock);

        manager.RequestRestart();

        clock.Advance(TimeSpan.FromMilliseconds(49));
        Assert.Equal(0, triggerCount);
        clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal(1, triggerCount);
        clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal(1, triggerCount); // A debounce timer must remain one-shot.
    }

    [Fact]
    public void MultipleRequests_CoalesceIntoSingleCallback()
    {
        var clock = new ManualTimeProvider();
        var triggerCount = 0;
        using var manager = new RestartThrottleManager(() => triggerCount++, clock);

        manager.RequestRestart();
        clock.Advance(TimeSpan.FromMilliseconds(10));
        manager.RequestRestart();
        clock.Advance(TimeSpan.FromMilliseconds(10));
        manager.RequestRestart();

        clock.Advance(TimeSpan.FromMilliseconds(49));
        Assert.Equal(0, triggerCount);
        clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal(1, triggerCount);
        clock.Advance(TimeSpan.FromMilliseconds(100));
        Assert.Equal(1, triggerCount);
    }

    [Fact]
    public void RequestRestart_ResetsTimer_OnSubsequentCalls()
    {
        var clock = new ManualTimeProvider();
        var triggerCount = 0;
        using var manager = new RestartThrottleManager(() => triggerCount++, clock);

        manager.RequestRestart();
        clock.Advance(TimeSpan.FromMilliseconds(30));
        manager.RequestRestart();

        clock.Advance(TimeSpan.FromMilliseconds(20)); // Original deadline, now cancelled.
        Assert.Equal(0, triggerCount);
        clock.Advance(TimeSpan.FromMilliseconds(29));
        Assert.Equal(0, triggerCount);
        clock.Advance(TimeSpan.FromMilliseconds(1)); // Full window since the reset.
        Assert.Equal(1, triggerCount);

        manager.RequestRestart(); // The same timer can schedule another restart.
        clock.Advance(TimeSpan.FromMilliseconds(49));
        Assert.Equal(1, triggerCount);
        clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal(2, triggerCount);
    }

    [Fact]
    public void Dispose_CanBeCalledSafely()
    {
        var manager = new RestartThrottleManager(() => { });
        var exception = Record.Exception(() => manager.Dispose());

        Assert.Null(exception);
    }

    [Fact]
    public void Dispose_BeforeAnyRequest_DoesNotThrow()
    {
        var manager = new RestartThrottleManager(() => { });

        // Timer hasn't been created yet (lazy creation)
        var exception = Record.Exception(() => manager.Dispose());
        Assert.Null(exception);
    }

    [Fact]
    public void Dispose_AfterRequest_DoesNotThrow()
    {
        var manager = new RestartThrottleManager(() => { });
        manager.RequestRestart();

        var exception = Record.Exception(() => manager.Dispose());
        Assert.Null(exception);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RequestRestart_AfterDispose_DoesNotCreateTimer(bool hadPendingRestart)
    {
        var triggerCount = 0;
        using var manager = new RestartThrottleManager(() => Interlocked.Increment(ref triggerCount));
        if (hadPendingRestart)
            ArrangeDequeuedCallback(manager);

        manager.Dispose();
        manager.Dispose(); // Repeated disposal must leave the throttle permanently closed.
        manager.RequestRestart();

        Assert.Null(GetTimer(manager));
        GetCallback(manager)(null!);
        Assert.Equal(0, triggerCount);
    }

    [Fact]
    public async Task QueuedCallback_WaitingForLock_WhenDisposed_IsIgnored()
    {
        var triggerCount = 0;
        using var manager = new RestartThrottleManager(() => Interlocked.Increment(ref triggerCount));
        using var callbackStarted = new ManualResetEventSlim();
        ArrangeDequeuedCallback(manager);
        var callback = GetCallback(manager);
        var sync = typeof(RestartThrottleManager).GetField("_lock", PrivateInstance)!.GetValue(manager)!;
        Task callbackTask;

        lock (sync)
        {
            callbackTask = Task.Factory.StartNew(() =>
            {
                callbackStarted.Set();
                callback(null!);
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

            Assert.True(callbackStarted.Wait(TimeSpan.FromSeconds(5)));
            // The callback cannot acquire the lock until disposal has cleared the timer.
            manager.Dispose();
        }

        // Invoking the dequeued callback on a Task captures the original NullReferenceException
        // as a test failure instead of letting a timer thread terminate the entire test runner.
        await callbackTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, triggerCount);
        Assert.Null(GetTimer(manager));
    }

    [Fact]
    public void RestartCallback_CanDisposeItself_AndLaterCallbacksAreIgnored()
    {
        var triggerCount = 0;
        RestartThrottleManager manager = null!;
        manager = new RestartThrottleManager(() =>
        {
            triggerCount++;
            manager.Dispose();
            manager.RequestRestart();
        });
        using (manager)
        {
            ArrangeDequeuedCallback(manager);
            var callback = GetCallback(manager);
            callback(null!);
            callback(null!);

            Assert.Equal(1, triggerCount);
            Assert.Null(GetTimer(manager));
        }
    }

    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    private static ITimer? GetTimer(RestartThrottleManager manager)
        => (ITimer?)typeof(RestartThrottleManager).GetField("_debounceTimer", PrivateInstance)!.GetValue(manager);

    private static TimerCallback GetCallback(RestartThrottleManager manager)
        => typeof(RestartThrottleManager).GetMethod("OnTimerCallback", PrivateInstance)!
            .CreateDelegate<TimerCallback>(manager);

    private static void ArrangeDequeuedCallback(RestartThrottleManager manager)
    {
        // Model a one-shot timer that has fired and dequeued its callback. Keep the real timer
        // disabled so the tests control callback ordering without timing-dependent sleeps.
        var timer = new Timer(_ => { }, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        typeof(RestartThrottleManager).GetField("_debounceTimer", PrivateInstance)!.SetValue(manager, timer);
        typeof(RestartThrottleManager).GetField("_restartPending", PrivateInstance)!.SetValue(manager, true);
    }

    // Only the one-shot timer operations used by the throttle are needed. Advancing
    // this clock runs due callbacks synchronously, so OS scheduling cannot move a
    // "before the deadline" assertion to after it.
    private sealed class ManualTimeProvider : TimeProvider
    {
        private readonly List<ManualTimer> _timers = [];
        private TimeSpan _elapsed;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(this, callback, state);
            _timers.Add(timer);
            timer.Change(dueTime, period);
            return timer;
        }

        internal void Advance(TimeSpan elapsed)
        {
            var target = _elapsed + elapsed;
            while (_timers.Where(timer => timer.DueAt.HasValue && timer.DueAt <= target)
                       .OrderBy(timer => timer.DueAt).FirstOrDefault() is { } next)
            {
                _elapsed = next.DueAt!.Value;
                next.Fire();
            }
            _elapsed = target;
        }

        private sealed class ManualTimer(ManualTimeProvider clock, TimerCallback callback, object? state) : ITimer
        {
            private bool _disposed;
            internal TimeSpan? DueAt { get; private set; }

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                if (_disposed) return false;
                if (period != Timeout.InfiniteTimeSpan)
                    throw new NotSupportedException("This test clock supports one-shot timers only.");
                DueAt = dueTime == Timeout.InfiniteTimeSpan ? null : clock._elapsed + dueTime;
                return true;
            }

            internal void Fire()
            {
                DueAt = null;
                callback(state);
            }

            public void Dispose()
            {
                _disposed = true;
                DueAt = null;
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}
