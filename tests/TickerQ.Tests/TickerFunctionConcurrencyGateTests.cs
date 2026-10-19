using System.Threading;
using TickerQ;

namespace TickerQ.Tests;

public class TickerFunctionConcurrencyGateTests
{
    private readonly TickerFunctionConcurrencyGate _gate = new();

    [Fact]
    public async Task OwnedPermits_ReclaimDynamicFunctionsAfterTheirLastHolder()
    {
        for (var i = 0; i < 2000; i++)
        {
            var permit = await _gate.AcquireAsync($"Dynamic-{i}", 1);
            Assert.Equal(1, _gate.RetainedGateCount);
            permit.Dispose();
            permit.Dispose();
        }
        Assert.Equal(0, _gate.RetainedGateCount);
    }

    [Fact]
    public async Task OwnedPermits_KeepWaitersOnTheSameGateAndReclaimCancelledWaiters()
    {
        var first = await _gate.AcquireAsync("Serial", 1);
        using var cancellation = new CancellationTokenSource();
        var cancelled = _gate.AcquireAsync("Serial", 1, cancellation.Token).AsTask();
        var next = _gate.AcquireAsync("Serial", 1).AsTask();
        Assert.False(next.IsCompleted);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        Assert.Equal(1, _gate.RetainedGateCount);
        first.Dispose();
        var second = await next.WaitAsync(TimeSpan.FromSeconds(5));
        var third = _gate.AcquireAsync("Serial", 1).AsTask();
        Assert.False(third.IsCompleted);
        second.Dispose();
        (await third.WaitAsync(TimeSpan.FromSeconds(5))).Dispose();
        Assert.Equal(0, _gate.RetainedGateCount);
    }

    [Fact]
    public async Task OwnedPermits_ConcurrentReleaseAndAcquireNeverExceedsFunctionLimit()
    {
        var active = 0;
        var peak = 0;
        await Task.WhenAll(Enumerable.Range(0, 32).Select(async _ =>
        {
            for (var i = 0; i < 100; i++)
            {
                using var permit = await _gate.AcquireAsync("Serial", 1);
                InterlockedMax(ref peak, Interlocked.Increment(ref active));
                await Task.Yield();
                Interlocked.Decrement(ref active);
            }
        }));
        Assert.Equal(1, peak);
        Assert.Equal(0, _gate.RetainedGateCount);
    }

    [Fact]
    public void GetSemaphoreOrNull_ZeroMaxConcurrency_ReturnsNull()
    {
        var result = _gate.GetSemaphoreOrNull("Func1", 0);

        Assert.Null(result);
    }

    [Fact]
    public void GetSemaphoreOrNull_NegativeMaxConcurrency_ReturnsNull()
    {
        var result = _gate.GetSemaphoreOrNull("Func1", -1);

        Assert.Null(result);
    }

    [Fact]
    public void GetSemaphoreOrNull_PositiveMaxConcurrency_ReturnsSemaphore()
    {
        var result = _gate.GetSemaphoreOrNull("Func1", 3);

        Assert.NotNull(result);
        Assert.Equal(3, result.CurrentCount);
    }

    [Fact]
    public void GetSemaphoreOrNull_SameFunctionName_ReturnsSameInstance()
    {
        var first = _gate.GetSemaphoreOrNull("Func1", 2);
        var second = _gate.GetSemaphoreOrNull("Func1", 2);

        Assert.Same(first, second);
    }

    [Fact]
    public void GetSemaphoreOrNull_DifferentFunctionNames_ReturnDifferentInstances()
    {
        var funcA = _gate.GetSemaphoreOrNull("FuncA", 1);
        var funcB = _gate.GetSemaphoreOrNull("FuncB", 1);

        Assert.NotSame(funcA, funcB);
    }

    [Fact]
    public void GetSemaphoreOrNull_MaxConcurrencyOne_LimitsToOneSlot()
    {
        var semaphore = _gate.GetSemaphoreOrNull("Serial", 1);

        Assert.Equal(1, semaphore!.CurrentCount);

        // Acquire the single slot
        semaphore.Wait(0);
        Assert.Equal(0, semaphore.CurrentCount);

        // Release it
        semaphore.Release();
        Assert.Equal(1, semaphore.CurrentCount);
    }

    [Fact]
    public async Task GetSemaphoreOrNull_EnforcesConcurrencyLimit()
    {
        var semaphore = _gate.GetSemaphoreOrNull("Limited", 2);
        var concurrentCount = 0;
        var maxObserved = 0;
        var tasks = new List<Task>();

        for (int i = 0; i < 10; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                await semaphore!.WaitAsync();
                try
                {
                    var current = Interlocked.Increment(ref concurrentCount);
                    InterlockedMax(ref maxObserved, current);
                    await Task.Delay(20);
                }
                finally
                {
                    Interlocked.Decrement(ref concurrentCount);
                    semaphore.Release();
                }
            }));
        }

        await Task.WhenAll(tasks);

        Assert.True(maxObserved <= 2, $"Expected max concurrency of 2, but observed {maxObserved}");
        Assert.True(maxObserved >= 1, "At least one task should have run concurrently");
    }

    private static void InterlockedMax(ref int location, int value)
    {
        int current;
        do
        {
            current = Volatile.Read(ref location);
            if (value <= current) return;
        } while (Interlocked.CompareExchange(ref location, value, current) != current);
    }
}
