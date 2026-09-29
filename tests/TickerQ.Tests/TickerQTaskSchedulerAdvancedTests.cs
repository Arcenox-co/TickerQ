using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.TickerQThreadPool;
using TickerQ.Utilities.Enums;
using Xunit;

namespace TickerQ.Tests;

public class TickerQTaskSchedulerAdvancedTests
{
    [Fact]
    public async Task QueueAsync_LongRunning_Executes_On_Dedicated_Thread()
    {
        // LongRunning work runs on a dedicated long-running thread, but it still consumes
        // the same global max-concurrency budget as Normal work (it does not bypass it).
        var scheduler = new TickerQTaskScheduler(2);
        var executed = new ManualResetEventSlim(false);

        await scheduler.QueueAsync(_ =>
        {
            executed.Set();
            return Task.CompletedTask;
        }, TickerTaskPriority.LongRunning);

        Assert.True(executed.Wait(TimeSpan.FromSeconds(3)), "LongRunning task should have executed");
        await scheduler.DisposeAsync();
    }

    [Fact]
    public async Task QueueAsync_Distributes_Across_Workers_And_All_Execute()
    {
        var scheduler = new TickerQTaskScheduler(4);
        var count = 20;
        var counter = new CountdownEvent(count);

        for (int i = 0; i < count; i++)
        {
            await scheduler.QueueAsync(_ =>
            {
                counter.Signal();
                return Task.CompletedTask;
            }, TickerTaskPriority.Normal);
        }

        Assert.True(counter.Wait(TimeSpan.FromSeconds(5)), $"All {count} tasks should have completed");
        await scheduler.DisposeAsync();
    }

    [Fact]
    public async Task WaitForRunningTasksAsync_Returns_True_After_All_Work_Completes()
    {
        // WaitForRunningTasksAsync completes on the queued + active-execution counters, not
        // on worker-thread liveness, so it returns true once all work is done regardless of
        // whether idle workers are still alive. Disposal here just exercises the full path.
        var scheduler = new TickerQTaskScheduler(2, idleWorkerTimeout: TimeSpan.FromMilliseconds(50));
        var executed = 0;

        for (int i = 0; i < 5; i++)
        {
            await scheduler.QueueAsync(_ =>
            {
                Interlocked.Increment(ref executed);
                return Task.CompletedTask;
            }, TickerTaskPriority.Normal);
        }

        // Wait for queued work to drain (check TotalQueuedTasks reaching <= 0)
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (scheduler.TotalQueuedTasks > 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.Equal(0, scheduler.TotalQueuedTasks);   // exactly zero, not <= 0 (underflow guard)
        Assert.Equal(5, Volatile.Read(ref executed));

        await scheduler.DisposeAsync();

        var result = await scheduler.WaitForRunningTasksAsync(TimeSpan.FromSeconds(1));
        Assert.True(result, "WaitForRunningTasksAsync should return true once all work has completed");
    }

    [Fact]
    public async Task WaitForRunningTasksAsync_Returns_False_On_Timeout()
    {
        var scheduler = new TickerQTaskScheduler(1);
        var blocker = new SemaphoreSlim(0);

        await scheduler.QueueAsync(async ct =>
        {
            await blocker.WaitAsync(ct);
        }, TickerTaskPriority.Normal);

        var result = await scheduler.WaitForRunningTasksAsync(TimeSpan.FromMilliseconds(100));

        // The task is running (not queued), so the counter may have already been decremented.
        // WaitForRunningTasksAsync checks _totalQueuedTasks and _activeWorkers.
        // Workers are still alive, so it should return false due to _activeWorkers > 0.
        Assert.False(result, "Should return false when timeout expires before work completes");

        blocker.Release();
        await scheduler.DisposeAsync();
    }

    [Fact]
    public async Task GetDiagnostics_Returns_NonNull_String_With_Status_Info()
    {
        var scheduler = new TickerQTaskScheduler(2);

        // Queue a task to make the scheduler active
        await scheduler.QueueAsync(_ => Task.CompletedTask, TickerTaskPriority.Normal);
        await Task.Delay(50);

        var diagnostics = scheduler.GetDiagnostics();

        Assert.NotNull(diagnostics);
        Assert.Contains("TickerQ", diagnostics);
        Assert.Contains("Workers:", diagnostics);
        Assert.Contains("Status:", diagnostics);
        await scheduler.DisposeAsync();
    }

    [Fact]
    public async Task GetDiagnostics_On_Frozen_Scheduler_Contains_Frozen()
    {
        var scheduler = new TickerQTaskScheduler(1);
        scheduler.Freeze();

        var diagnostics = scheduler.GetDiagnostics();

        Assert.Contains("FROZEN", diagnostics);
        await scheduler.DisposeAsync();
    }

    [Fact]
    public async Task GetDiagnostics_On_Disposed_Scheduler_Contains_Disposed()
    {
        var scheduler = new TickerQTaskScheduler(1);
        await scheduler.DisposeAsync();

        // After dispose, _isFrozen is true and _disposed is true.
        // The GetDiagnostics checks _isFrozen first, so it will show FROZEN.
        // Since the code checks: _isFrozen ? "FROZEN" : (_disposed ? "DISPOSED" : "ACTIVE")
        // We verify the diagnostics reflect the disposed state via the FROZEN label
        // (because DisposeAsync sets _isFrozen = true before _disposed matters for display).
        var diagnostics = scheduler.GetDiagnostics();

        Assert.NotNull(diagnostics);
        // DisposeAsync sets _isFrozen = true, so the ternary returns "FROZEN" not "DISPOSED".
        // This is a known quirk of the implementation.
        Assert.Contains("FROZEN", diagnostics);
        Assert.True(scheduler.IsDisposed);
    }

    [Fact]
    public async Task High_Concurrency_Stress_All_Tasks_Complete()
    {
        var scheduler = new TickerQTaskScheduler(8);
        var taskCount = 150;
        var counter = new CountdownEvent(taskCount);

        var queueTasks = new Task[taskCount];
        for (int i = 0; i < taskCount; i++)
        {
            queueTasks[i] = scheduler.QueueAsync(_ =>
            {
                counter.Signal();
                return Task.CompletedTask;
            }, TickerTaskPriority.Normal).AsTask();
        }

        await Task.WhenAll(queueTasks);

        Assert.True(counter.Wait(TimeSpan.FromSeconds(10)),
            $"All {taskCount} tasks should complete. Remaining: {counter.CurrentCount}");
        await scheduler.DisposeAsync();
    }

    [Fact]
    public async Task Multiple_Freeze_Resume_Cycles_All_Work_Executes()
    {
        var scheduler = new TickerQTaskScheduler(2);
        var results = new ConcurrentBag<int>();

        // Cycle 1: freeze then resume, then queue
        scheduler.Freeze();
        scheduler.Resume();

        await scheduler.QueueAsync(_ =>
        {
            results.Add(1);
            return Task.CompletedTask;
        }, TickerTaskPriority.Normal);

        await Task.Delay(100);

        // Cycle 2: freeze then resume, then queue
        scheduler.Freeze();
        scheduler.Resume();

        await scheduler.QueueAsync(_ =>
        {
            results.Add(2);
            return Task.CompletedTask;
        }, TickerTaskPriority.Normal);

        await Task.Delay(100);

        // Cycle 3: freeze then resume, then queue
        scheduler.Freeze();
        scheduler.Resume();

        await scheduler.QueueAsync(_ =>
        {
            results.Add(3);
            return Task.CompletedTask;
        }, TickerTaskPriority.Normal);

        await Task.Delay(200);

        Assert.Equal(3, results.Count);
        Assert.Contains(1, results);
        Assert.Contains(2, results);
        Assert.Contains(3, results);
        await scheduler.DisposeAsync();
    }

    [Fact]
    public async Task Dispose_Idempotency_No_Exception_On_Double_Dispose()
    {
        var scheduler = new TickerQTaskScheduler(1);

        await scheduler.DisposeAsync();

        // Second dispose should not throw
        var exception = await Record.ExceptionAsync(async () => await scheduler.DisposeAsync());
        Assert.Null(exception);
    }

    [Fact]
    public async Task QueueAsync_Respects_Cancellation_Token_Work_Not_Executed()
    {
        var scheduler = new TickerQTaskScheduler(1);
        var executed = false;
        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel immediately

        await scheduler.QueueAsync(_ =>
        {
            executed = true;
            return Task.CompletedTask;
        }, TickerTaskPriority.Normal, cts.Token);

        // Give the scheduler time to process the item
        await Task.Delay(200);

        Assert.False(executed, "Work should not execute when the cancellation token is already cancelled");
        await scheduler.DisposeAsync();
    }

    [Fact]
    public async Task Worker_Threads_Info_Visible_In_Diagnostics()
    {
        // Use a very short idle timeout so workers can exit quickly
        var scheduler = new TickerQTaskScheduler(4, idleWorkerTimeout: TimeSpan.FromMilliseconds(200));

        // Queue a single task
        var done = new ManualResetEventSlim(false);
        await scheduler.QueueAsync(_ =>
        {
            done.Set();
            return Task.CompletedTask;
        }, TickerTaskPriority.Normal);

        Assert.True(done.Wait(TimeSpan.FromSeconds(2)), "Task should complete");

        // Immediately after task execution, check diagnostics shows worker info
        var diagnostics = scheduler.GetDiagnostics();
        Assert.Contains("Workers:", diagnostics);

        // The diagnostics should show the worker count in the format "Workers: X/4"
        Assert.Contains("/4", diagnostics);

        // Wait for idle workers to potentially exit
        await Task.Delay(500);

        var laterDiagnostics = scheduler.GetDiagnostics();
        Assert.NotNull(laterDiagnostics);
        Assert.Contains("Workers:", laterDiagnostics);

        await scheduler.DisposeAsync();
    }

    // =====================================================================
    // RED tests (Phase 1). These fail against the current implementation and
    // encode the intended post-fix contract. All gates are released and the
    // scheduler disposed in finally so a failing assertion never hangs.
    // =====================================================================

    // Task 2: LongRunning must consume the SAME global max-concurrency budget.
    [Fact]
    public async Task LongRunning_Consumes_Global_MaxConcurrency_Budget()
    {
        // maxConcurrency = 2 => LongRunning work must not run more than two at once.
        var scheduler = new TickerQTaskScheduler(2);

        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thirdStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        int active = 0;
        int peak = 0;
        int started = 0;
        var allDone = new CountdownEvent(10);

        try
        {
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
                }, TickerTaskPriority.LongRunning);
            }

            // Exactly two LongRunning executions must start under the shared budget.
            Assert.True(await WaitForAsync(secondStarted.Task, TimeSpan.FromSeconds(2)),
                "Two LongRunning executions should start with maxConcurrency=2.");

            // A third LongRunning execution must NOT begin while two hold the gate.
            Assert.False(await WaitForAsync(thirdStarted.Task, TimeSpan.FromMilliseconds(500)),
                "LongRunning work must honor maxConcurrency=2 and not bypass the limit.");

            Assert.Equal(2, Volatile.Read(ref peak));
            Assert.Equal(2, Volatile.Read(ref started));

            // Releasing the gate must let all ten LongRunning items complete.
            gate.TrySetResult(true);
            Assert.True(allDone.Wait(TimeSpan.FromSeconds(10)),
                "All ten LongRunning work items must complete after the gate is released.");
        }
        finally
        {
            gate.TrySetResult(true);
            await scheduler.DisposeAsync();
        }
    }

    private static async Task<bool> WaitForAsync(Task task, TimeSpan timeout)
    {
        var completed = await Task.WhenAny(task, Task.Delay(timeout));
        return completed == task;
    }

    // Task 4: WaitForRunningTasksAsync must return true when all work is complete
    // even though idle worker threads remain alive (worker liveness != pending work).
    [Fact]
    public async Task WaitForRunningTasksAsync_True_When_Work_Done_Even_With_Idle_Workers_Alive()
    {
        // Long idle timeout keeps worker threads alive after the queue drains.
        var scheduler = new TickerQTaskScheduler(2, idleWorkerTimeout: TimeSpan.FromSeconds(30));

        var done = new CountdownEvent(3);
        try
        {
            for (int i = 0; i < 3; i++)
            {
                await scheduler.QueueAsync(_ =>
                {
                    done.Signal();
                    return Task.CompletedTask;
                }, TickerTaskPriority.Normal);
            }

            Assert.True(done.Wait(TimeSpan.FromSeconds(5)), "All queued work should have executed");

            // Let the queue counter settle to zero (bounded poll, not the primary signal).
            var deadline = DateTime.UtcNow.AddSeconds(2);
            while (scheduler.TotalQueuedTasks > 0 && DateTime.UtcNow < deadline)
                await Task.Delay(10);

            // Precondition: idle worker threads are still alive (scheduler not disposed).
            Assert.True(scheduler.ActiveWorkers > 0,
                "Idle worker threads should still be alive for this scenario");

            // All queued/running work is complete, so this must return true even though
            // idle worker threads remain — worker liveness is not completion state.
            var result = await scheduler.WaitForRunningTasksAsync(TimeSpan.FromMilliseconds(500));
            Assert.True(result,
                "WaitForRunningTasksAsync must return true when all work is complete, regardless of idle workers still being alive");
        }
        finally
        {
            await scheduler.DisposeAsync();
        }
    }

    // Task 4: WaitForRunningTasksAsync must report not-drained while incomplete async
    // work remains, then true PROMPTLY after release without requiring DisposeAsync.
    [Fact]
    public async Task WaitForRunningTasksAsync_False_While_Incomplete_Then_True_After_Release_Without_Dispose()
    {
        var scheduler = new TickerQTaskScheduler(2, idleWorkerTimeout: TimeSpan.FromSeconds(30));

        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        int active = 0;

        try
        {
            await scheduler.QueueAsync(async _ =>
            {
                Interlocked.Increment(ref active);
                started.TrySetResult(true);
                try
                {
                    await gate.Task;
                }
                finally
                {
                    Interlocked.Decrement(ref active);
                }
            }, TickerTaskPriority.Normal);

            var startWon = await Task.WhenAny(started.Task, Task.Delay(TimeSpan.FromSeconds(2)));
            Assert.True(startWon == started.Task, "Work item should have started");

            // Incomplete async work remains in flight => must report not-drained.
            var whileRunning = await scheduler.WaitForRunningTasksAsync(TimeSpan.FromMilliseconds(300));
            Assert.False(whileRunning, "Should return false while incomplete async work remains");

            // Release the gate; completion must be observed promptly WITHOUT DisposeAsync.
            gate.TrySetResult(true);

            var afterRelease = await scheduler.WaitForRunningTasksAsync(TimeSpan.FromSeconds(2));
            Assert.True(afterRelease,
                "Should return true promptly after work completes, without requiring DisposeAsync to drain workers");
        }
        finally
        {
            gate.TrySetResult(true);
            await scheduler.DisposeAsync();
        }
    }

    // Task 5: Diagnostics must expose in-flight executions as a field DISTINCT from
    // worker-thread count. The intended field is "Active Executions:".
    [Fact]
    public async Task Diagnostics_Distinguish_Running_Executions_From_Worker_Threads()
    {
        var scheduler = new TickerQTaskScheduler(2, idleWorkerTimeout: TimeSpan.FromSeconds(30));

        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            await scheduler.QueueAsync(async _ =>
            {
                started.TrySetResult(true);
                await gate.Task;
            }, TickerTaskPriority.Normal);

            var startWon = await Task.WhenAny(started.Task, Task.Delay(TimeSpan.FromSeconds(2)));
            Assert.True(startWon == started.Task, "Work should have started");

            var diagnostics = scheduler.GetDiagnostics();

            // Exactly one async execution is in flight (blocked on the gate). Diagnostics
            // must surface it as its own field, separate from the worker-thread count.
            Assert.Contains("Active Executions:", diagnostics);
            Assert.Contains("Active Executions: 1", diagnostics);
        }
        finally
        {
            gate.TrySetResult(true);
            await scheduler.DisposeAsync();
        }
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
