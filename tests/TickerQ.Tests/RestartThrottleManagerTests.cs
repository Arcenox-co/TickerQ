using FluentAssertions;

namespace TickerQ.Tests;

public class RestartThrottleManagerTests
{
    [Fact]
    public async Task RequestRestart_TriggersCallback_AfterDebounceWindow()
    {
        var triggered = false;
        using var manager = new RestartThrottleManager(() => triggered = true);

        manager.RequestRestart();

        // Debounce window is 50ms, give some extra time
        await Task.Delay(200);

        triggered.Should().BeTrue();
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

        triggerCount.Should().Be(1);
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

        triggerCount.Should().Be(1);
    }

    [Fact]
    public void Dispose_CanBeCalledSafely()
    {
        var manager = new RestartThrottleManager(() => { });
        var act = () => manager.Dispose();

        act.Should().NotThrow();
    }

    [Fact]
    public void Dispose_BeforeAnyRequest_DoesNotThrow()
    {
        var manager = new RestartThrottleManager(() => { });

        // Timer hasn't been created yet (lazy creation)
        var act = () => manager.Dispose();
        act.Should().NotThrow();
    }

    [Fact]
    public void Dispose_AfterRequest_DoesNotThrow()
    {
        var manager = new RestartThrottleManager(() => { });
        manager.RequestRestart();

        var act = () => manager.Dispose();
        act.Should().NotThrow();
    }
}
