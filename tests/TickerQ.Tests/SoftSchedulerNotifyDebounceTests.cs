using FluentAssertions;

namespace TickerQ.Tests;

public class SoftSchedulerNotifyDebounceTests
{
    [Fact]
    public void NotifySafely_InvokesCallback_WithLatestValue()
    {
        var receivedValues = new List<string>();
        using var debounce = new SoftSchedulerNotifyDebounce(v => receivedValues.Add(v));

        debounce.NotifySafely(5);

        receivedValues.Should().Contain("5");
    }

    [Fact]
    public void NotifySafely_SuppressesDuplicateValues()
    {
        var receivedValues = new List<string>();
        using var debounce = new SoftSchedulerNotifyDebounce(v => receivedValues.Add(v));

        debounce.NotifySafely(3);
        var countAfterFirst = receivedValues.Count;

        debounce.NotifySafely(3);
        var countAfterSecond = receivedValues.Count;

        // Second call with same non-zero value should be suppressed
        countAfterSecond.Should().Be(countAfterFirst);
    }

    [Fact]
    public void NotifySafely_AllowsDifferentValues()
    {
        var receivedValues = new List<string>();
        using var debounce = new SoftSchedulerNotifyDebounce(v => receivedValues.Add(v));

        debounce.NotifySafely(1);
        debounce.NotifySafely(2);

        receivedValues.Should().Contain("1");
        receivedValues.Should().Contain("2");
    }

    [Fact]
    public void Flush_InvokesCallbackImmediately()
    {
        var receivedValues = new List<string>();
        using var debounce = new SoftSchedulerNotifyDebounce(v => receivedValues.Add(v));

        debounce.NotifySafely(10);
        receivedValues.Clear();

        debounce.NotifySafely(20);
        debounce.Flush();

        // Flush should ensure the latest value is pushed
        receivedValues.Should().NotBeEmpty();
    }

    [Fact]
    public void NotifySafely_DoesNotInvoke_AfterDispose()
    {
        var receivedValues = new List<string>();
        var debounce = new SoftSchedulerNotifyDebounce(v => receivedValues.Add(v));

        debounce.Dispose();
        var countBeforeNotify = receivedValues.Count;

        debounce.NotifySafely(100);

        receivedValues.Count.Should().Be(countBeforeNotify);
    }

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        var debounce = new SoftSchedulerNotifyDebounce(_ => { });

        var act = () =>
        {
            debounce.Dispose();
            debounce.Dispose();
        };

        act.Should().NotThrow();
    }

    [Fact]
    public void NotifySafely_AlwaysInvokes_ForZeroValue()
    {
        var callCount = 0;
        using var debounce = new SoftSchedulerNotifyDebounce(_ => callCount++);

        // Zero is special: it always triggers the callback
        // (the code checks `latest != 0 && latest == last` for suppression)
        debounce.NotifySafely(0);
        debounce.NotifySafely(0);

        callCount.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public void Constructor_DoesNotInvoke_Callback()
    {
        var callCount = 0;
        using var debounce = new SoftSchedulerNotifyDebounce(_ => callCount++);

        callCount.Should().Be(0);
    }
}
