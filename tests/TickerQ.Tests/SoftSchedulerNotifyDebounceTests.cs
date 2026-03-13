namespace TickerQ.Tests;

public class SoftSchedulerNotifyDebounceTests
{
    [Fact]
    public void NotifySafely_InvokesCallback_WithLatestValue()
    {
        var receivedValues = new List<string>();
        using var debounce = new SoftSchedulerNotifyDebounce(v => receivedValues.Add(v));

        debounce.NotifySafely(5);

        Assert.Contains("5", receivedValues);
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
        Assert.Equal(countAfterFirst, countAfterSecond);
    }

    [Fact]
    public void NotifySafely_AllowsDifferentValues()
    {
        var receivedValues = new List<string>();
        using var debounce = new SoftSchedulerNotifyDebounce(v => receivedValues.Add(v));

        debounce.NotifySafely(1);
        debounce.NotifySafely(2);

        Assert.Contains("1", receivedValues);
        Assert.Contains("2", receivedValues);
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
        Assert.NotEmpty(receivedValues);
    }

    [Fact]
    public void NotifySafely_DoesNotInvoke_AfterDispose()
    {
        var receivedValues = new List<string>();
        var debounce = new SoftSchedulerNotifyDebounce(v => receivedValues.Add(v));

        debounce.Dispose();
        var countBeforeNotify = receivedValues.Count;

        debounce.NotifySafely(100);

        Assert.Equal(countBeforeNotify, receivedValues.Count);
    }

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        var debounce = new SoftSchedulerNotifyDebounce(_ => { });

        var exception = Record.Exception(() =>
        {
            debounce.Dispose();
            debounce.Dispose();
        });

        Assert.Null(exception);
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

        Assert.True(callCount >= 2);
    }

    [Fact]
    public void Constructor_DoesNotInvoke_Callback()
    {
        var callCount = 0;
        using var debounce = new SoftSchedulerNotifyDebounce(_ => callCount++);

        Assert.Equal(0, callCount);
    }
}
