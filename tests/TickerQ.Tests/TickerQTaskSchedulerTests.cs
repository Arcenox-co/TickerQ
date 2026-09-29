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

        Assert.True(scheduler.TotalQueuedTasks <= 0);
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

    [Fact]
    public async Task Worker_Still_Running_After_DisposeAsync_Gives_Up_Exits_Cleanly()
    {
        // DisposeAsync waits at most 5 s for its workers and then disposes the shutdown token source.
        // A worker still busy at that point used to read _shutdownCts.Token on its way back to the loop
        // condition, throwing ObjectDisposedException on its dedicated thread and killing the process.
        // The same happens to an idle worker whose continuation is delayed past the 5 s, e.g. by thread
        // pool starvation during host shutdown.
        var scheduler = new TickerQTaskScheduler(1);
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();

        await scheduler.QueueAsync(_ =>
        {
            started.Set();
            release.Wait(TimeSpan.FromSeconds(30));
            return Task.CompletedTask;
        }, TickerTaskPriority.Normal);

        Assert.True(started.Wait(TimeSpan.FromSeconds(5)));

        await scheduler.DisposeAsync();
        var workersLeftByDispose = scheduler.ActiveWorkers;

        // The worker now returns to its loop condition, after _shutdownCts was disposed.
        release.Set();

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (scheduler.ActiveWorkers > 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        Assert.Equal(1, workersLeftByDispose);
        Assert.Equal(0, scheduler.ActiveWorkers);
    }
}
