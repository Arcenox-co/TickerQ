using System.Collections.Concurrent;
using TickerQ.TickerQThreadPool;
using TickerQ.Utilities.Enums;

namespace TickerQ.Tests;

[Collection("TickerQTaskScheduler")]
public class TickerQTaskSchedulerQueueTests
{
    [Fact]
    public async Task Concurrent_Writers_Respect_Total_Capacity_And_All_Eventually_Execute()
    {
        await using var scheduler = new TickerQTaskScheduler(1, queueCapacity: 2);
        var started = Signal();
        var release = Signal();
        var executed = 0;
        try
        {
            await scheduler.QueueAsync(async _ => { started.TrySetResult(true); await release.Task; }, TickerTaskPriority.Normal);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var submitted = new AsyncTestCountdown(32);
            var writers = Enumerable.Range(0, 32).Select(_ => Task.Run(async () =>
            {
                var publication = scheduler.QueueAsync(_ =>
                {
                    Interlocked.Increment(ref executed);
                    return Task.CompletedTask;
                }, TickerTaskPriority.Normal);
                submitted.Signal();
                await publication;
                Assert.InRange(scheduler.TotalQueuedTasks, 0, scheduler.QueueCapacity);
            })).ToArray();
            Assert.True(await submitted.WaitAsync(TimeSpan.FromSeconds(10)));

            Assert.Equal(2, scheduler.TotalQueuedTasks);
            Assert.True(writers.Count(task => task.IsCompletedSuccessfully) <= 2);
            release.TrySetResult(true);
            await Task.WhenAll(writers).WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(await scheduler.WaitForRunningTasksAsync(TimeSpan.FromSeconds(10)));
            Assert.Equal(32, executed);
            Assert.Equal(0, scheduler.TotalQueuedTasks);
        }
        finally { release.TrySetResult(true); }
    }

    [Fact]
    public async Task Freeze_Then_Immediate_Resume_Still_Rejects_Already_Waiting_Writer()
    {
        await using var scheduler = new TickerQTaskScheduler(1, queueCapacity: 1);
        var started = Signal();
        var release = Signal();
        try
        {
            await scheduler.QueueAsync(async _ => { started.TrySetResult(true); await release.Task; }, TickerTaskPriority.Normal);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await scheduler.QueueAsync(_ => Task.CompletedTask, TickerTaskPriority.Normal);
            var rejected = scheduler.QueueAsync(_ => Task.CompletedTask, TickerTaskPriority.Normal).AsTask();
            scheduler.Freeze();
            scheduler.Resume();
            await Assert.ThrowsAsync<InvalidOperationException>(() => rejected.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.Equal(1, scheduler.TotalQueuedTasks);
        }
        finally { release.TrySetResult(true); }
    }

    [Fact]
    public async Task Priority_Lanes_Are_FIFO_And_Lower_Priorities_Are_Not_Starved()
    {
        await using var scheduler = new TickerQTaskScheduler(1);
        var started = Signal();
        var release = Signal();
        var order = new ConcurrentQueue<string>();
        try
        {
            // Consuming Low advances the weighted cursor to the beginning of its cycle.
            await scheduler.QueueAsync(async _ => { started.TrySetResult(true); await release.Task; }, TickerTaskPriority.Low);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            for (var i = 0; i < 2; i++) await Enqueue("L", i, TickerTaskPriority.Low);
            for (var i = 0; i < 4; i++) await Enqueue("N", i, TickerTaskPriority.Normal);
            for (var i = 0; i < 8; i++) await Enqueue("H", i, TickerTaskPriority.High);
            release.TrySetResult(true);
            Assert.True(await scheduler.WaitForRunningTasksAsync(TimeSpan.FromSeconds(10)));
            Assert.Equal(new[] { "H0", "H1", "H2", "H3", "N0", "N1", "L0", "H4", "H5", "H6", "H7", "N2", "N3", "L1" }, order);
        }
        finally { release.TrySetResult(true); }

        ValueTask Enqueue(string label, int index, TickerTaskPriority priority) => scheduler.QueueAsync(_ =>
        {
            order.Enqueue(label + index);
            return Task.CompletedTask;
        }, priority);
    }

    [Fact]
    public async Task Idle_Consumers_Remain_Available_Between_Bursts()
    {
        await using var scheduler = new TickerQTaskScheduler(8);
        for (var iteration = 0; iteration < 3; iteration++)
        {
            await Task.Delay(20);
            Assert.Equal(8, scheduler.ActiveWorkers);
            var done = new AsyncTestCountdown(24);
            for (var i = 0; i < 24; i++)
                await scheduler.QueueAsync(_ => { done.Signal(); return Task.CompletedTask; }, TickerTaskPriority.Normal);
            Assert.True(await done.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.True(await scheduler.WaitForRunningTasksAsync(TimeSpan.FromSeconds(10)));
        }
    }

    [Fact]
    public async Task Throwing_Cancellation_Callback_Does_Not_Interrupt_Disposal_Cleanup()
    {
        var scheduler = new TickerQTaskScheduler(1, queueCapacity: 2);
        var started = Signal();
        await scheduler.QueueAsync(async token =>
        {
            using var registration = token.Register(() => throw new InvalidOperationException("Test callback failure"));
            started.TrySetResult(true);
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        }, TickerTaskPriority.Normal);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await scheduler.QueueAsync(_ => Task.CompletedTask, TickerTaskPriority.Normal);
        await scheduler.QueueAsync(_ => Task.CompletedTask, TickerTaskPriority.Normal);

        await scheduler.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(0, scheduler.TotalQueuedTasks);
        Assert.Equal(0, scheduler.ActiveExecutionCount);
        Assert.Equal(0, scheduler.ActiveWorkers);
        await scheduler.DisposeAsync();
    }

    [Fact]
    public async Task ExecuteAsync_Cancels_Queued_Work_Without_Waiting_For_A_Running_Job()
    {
        await using var scheduler = new TickerQTaskScheduler(1);
        var execution = (ITickerQExecutionScheduler)scheduler;
        var started = Signal();
        var release = Signal();
        using var cancellation = new CancellationTokenSource();
        var invoked = false;
        try
        {
            await scheduler.QueueAsync(async _ => { started.TrySetResult(true); await release.Task; }, TickerTaskPriority.Normal);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var waiting = execution.ExecuteAsync(_ => { invoked = true; return Task.CompletedTask; }, TickerTaskPriority.Normal, cancellation.Token);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.False(invoked);
            release.TrySetResult(true);
            Assert.True(await scheduler.WaitForRunningTasksAsync(TimeSpan.FromSeconds(10)));
            Assert.False(invoked);
        }
        finally { release.TrySetResult(true); }
    }

    [Fact]
    public async Task ExecuteAsync_Does_Not_Release_A_Running_NonCooperative_Attempt_On_Cancellation()
    {
        await using var scheduler = new TickerQTaskScheduler(1);
        var execution = (ITickerQExecutionScheduler)scheduler;
        var started = Signal();
        var release = Signal();
        using var cancellation = new CancellationTokenSource();
        try
        {
            var active = execution.ExecuteAsync(async _ => { started.TrySetResult(true); await release.Task; }, TickerTaskPriority.Normal, cancellation.Token);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            cancellation.Cancel();
            Assert.False(active.IsCompleted);
            Assert.Equal(1, scheduler.ActiveExecutionCount);
            release.TrySetResult(true);
            await active.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally { release.TrySetResult(true); }
    }

    [Fact]
    public async Task ExecuteAsync_Propagates_Delegate_Failures_And_Consumer_Continues()
    {
        await using var scheduler = new TickerQTaskScheduler(1);
        var execution = (ITickerQExecutionScheduler)scheduler;
        var failure = new InvalidOperationException("attempt failed");
        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => execution.ExecuteAsync(_ => throw failure, TickerTaskPriority.Normal));
        Assert.Same(failure, thrown);
        var next = false;
        await execution.ExecuteAsync(_ => { next = true; return Task.CompletedTask; }, TickerTaskPriority.Normal);
        Assert.True(next);
    }

    [Fact]
    public async Task Dispose_Cancels_An_Attempt_Waiting_For_Queue_Capacity()
    {
        var scheduler = new TickerQTaskScheduler(1, queueCapacity: 1);
        var execution = (ITickerQExecutionScheduler)scheduler;
        var started = Signal();
        await scheduler.QueueAsync(async token =>
        {
            started.TrySetResult(true);
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        }, TickerTaskPriority.Normal);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await scheduler.QueueAsync(_ => Task.CompletedTask, TickerTaskPriority.Normal);
        var waiting = execution.ExecuteAsync(_ => Task.CompletedTask, TickerTaskPriority.Normal);
        await scheduler.DisposeAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(0, scheduler.TotalQueuedTasks);
        Assert.Equal(0, scheduler.ActiveWorkers);
    }

    [Fact]
    public async Task ExecuteAsync_Preserves_Original_OperationCanceledException_And_Its_Token()
    {
        await using var scheduler = new TickerQTaskScheduler(1);
        var execution = (ITickerQExecutionScheduler)scheduler;
        using var unrelated = new CancellationTokenSource();
        var original = new OperationCanceledException("application-internal cancellation", unrelated.Token);

        var attempt = execution.ExecuteAsync(_ => throw original, TickerTaskPriority.Normal);
        var propagated = await Assert.ThrowsAsync<OperationCanceledException>(() => attempt);
        Assert.Same(original, propagated);
        Assert.Equal(unrelated.Token, propagated.CancellationToken);
        Assert.False(unrelated.IsCancellationRequested);
        Assert.True(attempt.IsCanceled);
    }

    [Fact]
    public async Task Blocking_Cancellation_Callback_Cannot_Block_Bounded_Disposal()
    {
        var scheduler = new TickerQTaskScheduler(1, notifyDebounce: null, disposeDrainTimeout: TimeSpan.FromMilliseconds(100));
        var started = Signal();
        var callbackEntered = Signal();
        var callbackExited = Signal();
        using var releaseCallback = new ManualResetEventSlim();
        Task disposal = Task.CompletedTask;
        try
        {
            await scheduler.QueueAsync(async token =>
            {
                using var registration = token.Register(() =>
                {
                    callbackEntered.TrySetResult(true);
                    // The test's finally releases this gate even if disposal regresses.
                    releaseCallback.Wait(TimeSpan.FromSeconds(15));
                    callbackExited.TrySetResult(true);
                });
                started.TrySetResult(true);
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }, TickerTaskPriority.Normal);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await scheduler.QueueAsync(_ => Task.CompletedTask, TickerTaskPriority.Normal);
            disposal = Task.Run(async () => await scheduler.DisposeAsync());
            await callbackEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));

            // Callback is still blocked. The 100ms disposal budget must nevertheless
            // finish, and not-yet-started queued ownership must already be released.
            await disposal.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(callbackExited.Task.IsCompleted);
            Assert.Equal(0, scheduler.TotalQueuedTasks);
            Assert.True(scheduler.IsDisposed);
        }
        finally
        {
            releaseCallback.Set();
            await scheduler.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
            await disposal.WaitAsync(TimeSpan.FromSeconds(10));
            if (callbackEntered.Task.IsCompleted)
                await callbackExited.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(await scheduler.WaitForRunningTasksAsync(TimeSpan.FromSeconds(10)));
            var workersStopped = DateTime.UtcNow.AddSeconds(10);
            while (scheduler.ActiveWorkers != 0 && DateTime.UtcNow < workersStopped)
                await Task.Delay(5);
            Assert.Equal(0, scheduler.ActiveWorkers);
            await scheduler.DisposeAsync();
        }
    }

    private static TaskCompletionSource<bool> Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
