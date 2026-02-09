using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
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

        scheduler.TotalQueuedTasks.Should().BeLessThanOrEqualTo(0);
        scheduler.IsDisposed.Should().BeTrue();
    }

    [Fact]
    public async Task QueueAsync_Throws_After_Dispose()
    {
        var scheduler = new TickerQTaskScheduler(1);
        await scheduler.DisposeAsync();

        Func<Task> act = () => scheduler.QueueAsync(_ => Task.CompletedTask, TickerTaskPriority.Normal).AsTask();

        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task QueueAsync_Throws_When_Frozen()
    {
        var scheduler = new TickerQTaskScheduler(1);
        scheduler.Freeze();

        Func<Task> act = () => scheduler.QueueAsync(_ => Task.CompletedTask, TickerTaskPriority.Normal).AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>();

        await scheduler.DisposeAsync();
    }

    [Fact]
    public async Task Freeze_And_Resume_Work()
    {
        var scheduler = new TickerQTaskScheduler(1);

        scheduler.Freeze();
        scheduler.IsFrozen.Should().BeTrue();

        scheduler.Resume();
        scheduler.IsFrozen.Should().BeFalse();

        // Should be able to queue after resume
        var executed = false;
        await scheduler.QueueAsync(_ =>
        {
            executed = true;
            return Task.CompletedTask;
        }, TickerTaskPriority.Normal);

        // Give worker time to pick it up
        await Task.Delay(200);

        executed.Should().BeTrue();
        await scheduler.DisposeAsync();
    }
}
