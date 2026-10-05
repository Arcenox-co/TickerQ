using System;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.TickerQThreadPool;
using TickerQ.Utilities.Enums;
using Xunit;

namespace TickerQ.Tests;

public class TickerQTaskSchedulerTests
{
    [Fact]
    public async Task DisposeAsync_Drains_Queued_Tasks_And_Counter_Reaches_Zero()
    {
        var scheduler = new TickerQTaskScheduler(2);

        // Queue work that will never complete (blocks on a semaphore)
        var blocker = new SemaphoreSlim(0);
        await scheduler.QueueAsync(async ct => await blocker.WaitAsync(ct), TickerTaskPriority.Normal);
        await scheduler.QueueAsync(async ct => await blocker.WaitAsync(ct), TickerTaskPriority.Normal);

        // Queue more items that pile up behind the blockers
        for (int i = 0; i < 5; i++)
        {
            await scheduler.QueueAsync(_ => Task.CompletedTask, TickerTaskPriority.Normal);
        }

        await scheduler.DisposeAsync();

        // The queued counter must settle at EXACTLY zero. `<= 0` would hide publication
        // underflow (a negative counter), which is itself a bug.
        Assert.Equal(0, scheduler.TotalQueuedTasks);
        Assert.True(scheduler.IsDisposed);
    }

    [Fact]
    public async Task QueueAsync_Throws_After_Dispose()
    {
        var scheduler = new TickerQTaskScheduler(1);
        await scheduler.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => scheduler.QueueAsync(_ => Task.CompletedTask, TickerTaskPriority.Normal).AsTask());
    }

    [Fact]
    public async Task QueueAsync_Throws_When_Frozen()
    {
        var scheduler = new TickerQTaskScheduler(1);
        scheduler.Freeze();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => scheduler.QueueAsync(_ => Task.CompletedTask, TickerTaskPriority.Normal).AsTask());

        await scheduler.DisposeAsync();
    }

    [Fact]
    public async Task Freeze_And_Resume_Work()
    {
        var scheduler = new TickerQTaskScheduler(1);

        scheduler.Freeze();
        Assert.True(scheduler.IsFrozen);

        scheduler.Resume();
        Assert.False(scheduler.IsFrozen);

        // Should be able to queue after resume
        var executed = false;
        await scheduler.QueueAsync(_ =>
        {
            executed = true;
            return Task.CompletedTask;
        }, TickerTaskPriority.Normal);

        // Give worker time to pick it up
        await Task.Delay(200);

        Assert.True(executed);
        await scheduler.DisposeAsync();
    }

    // ---------------------------------------------------------------------
    // RED (Phase 1, Task 2): global active-execution concurrency bound.
    // These prove that maxConcurrency must bound the number of *active
    // asynchronous executions*, not merely the number of worker-loop threads.
    // ---------------------------------------------------------------------

    [Fact]
    public async Task MaxConcurrency_Bounds_Active_Async_Executions_Not_Just_Workers()
    {
        // maxConcurrency = 2 => at most two async work items may be *executing* at once.
        var scheduler = new TickerQTaskScheduler(2);

        // Release gate all work items block on. Coordination is deterministic:
        // no arbitrary sleeps are used as the primary completion/observation signal.
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thirdStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        int active = 0;
        int peak = 0;
        int started = 0;
        var allDone = new CountdownEvent(10);

        try
        {
            // Queue 10 delegates that increment an active counter, await the gate, then decrement.
            for (int i = 0; i < 10; i++)
            {
                await scheduler.QueueAsync(async _ =>
                {
                    var now = Interlocked.Increment(ref active);
                    UpdatePeak(ref peak, now);
                    var s = Interlocked.Increment(ref started);
                    if (s == 2) secondStarted.TrySetResult(true);
                    if (s >= 3) thirdStarted.TrySetResult(true);
                    try
                    {
                        await gate.Task;
                    }
                    finally
                    {
                        Interlocked.Decrement(ref active);
                        allDone.Signal();
                    }
                }, TickerTaskPriority.Normal);
            }

            // Exactly two executions must start and block on the gate.
            Assert.True(await WaitForAsync(secondStarted.Task, TimeSpan.FromSeconds(2)),
                "Two executions should start with maxConcurrency=2.");

            // A third must NOT begin while the first two hold the gate. A correctly-bounded
            // scheduler never signals this, so we require the bounded wait to time out.
            Assert.False(await WaitForAsync(thirdStarted.Task, TimeSpan.FromMilliseconds(500)),
                "A third execution must not start while two are active with maxConcurrency=2.");

            Assert.Equal(2, Volatile.Read(ref peak));
            Assert.Equal(2, Volatile.Read(ref started));

            // Releasing the gate must let all ten complete (nothing dropped or stalled).
            gate.TrySetResult(true);
            Assert.True(allDone.Wait(TimeSpan.FromSeconds(10)),
                "All ten work items must complete after the gate is released.");
        }
        finally
        {
            // Always release the gate and dispose so a failing assertion cannot hang the suite.
            gate.TrySetResult(true);
            await scheduler.DisposeAsync();
        }
    }

    private static async Task<bool> WaitForAsync(Task task, TimeSpan timeout)
    {
        var completed = await Task.WhenAny(task, Task.Delay(timeout));
        return completed == task;
    }

    private static void UpdatePeak(ref int peak, int candidate)
    {
        int current;
        do
        {
            current = Volatile.Read(ref peak);
            if (candidate <= current)
                return;
        }
        while (Interlocked.CompareExchange(ref peak, candidate, current) != current);
    }
}
