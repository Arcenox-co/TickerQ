using System.Reflection;

namespace TickerQ.Tests;

public class RestartThrottleManagerTests
{
    [Fact]
    public async Task RequestRestart_TriggersCallback_AfterDebounceWindow()
    {
        var triggered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var manager = new RestartThrottleManager(() => triggered.TrySetResult());

        manager.RequestRestart();

        await triggered.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task MultipleRequests_CoalesceIntoSingleCallback()
    {
        var triggerCount = 0;
        using var manager = new RestartThrottleManager(() => Interlocked.Increment(ref triggerCount));

        // Multiple rapid requests should coalesce
        manager.RequestRestart();
        manager.RequestRestart();
        manager.RequestRestart();

        await Task.Delay(200);

        Assert.Equal(1, triggerCount);
    }

    [Fact]
    public async Task RequestRestart_ResetsTimer_OnSubsequentCalls()
    {
        var triggerCount = 0;
        using var manager = new RestartThrottleManager(() => Interlocked.Increment(ref triggerCount));

        manager.RequestRestart();
        await Task.Delay(30); // Less than debounce window (50ms)
        manager.RequestRestart(); // Should reset the timer
        await Task.Delay(30); // Still less than full window from second request

        // Should not have triggered yet since timer was reset
        // After full debounce from the last request it should trigger
        await Task.Delay(100);

        Assert.Equal(1, triggerCount);
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

    private static Timer? GetTimer(RestartThrottleManager manager)
        => (Timer?)typeof(RestartThrottleManager).GetField("_debounceTimer", PrivateInstance)!.GetValue(manager);

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
}
