using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.TickerQThreadPool;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using Xunit;

namespace TickerQ.Tests;

/// <summary>
/// Phase 1 concurrency contract tests. These encode the invariants an independent
/// concurrency review required: exact counter settlement (no underflow / false drain),
/// atomic publication versus freeze/disposal, safe disposal for work that outlives the
/// bounded wait, deadlock-free reentrancy at maxConcurrency=1, and strict drain
/// timeout/cancellation semantics.
///
/// Every gate is released and every scheduler disposed in a finally / using so a failing
/// assertion can never hang the suite. Deadlock-prone scenarios use a bounded WhenAny so
/// a regression fails as a timeout assertion rather than a hang.
/// </summary>
public class TickerQTaskSchedulerConcurrencyTests
{
    // ------------------------------------------------------------------
    // Requirement 1: TotalQueuedTasks must settle EXACTLY at zero, never
    // negative. The old tests used `<= 0`, which hides publication underflow.
    // ------------------------------------------------------------------

    [Fact]
    public async Task Counter_Settles_Exactly_Zero_After_Normal_Work()
    {
        await using var scheduler = new TickerQTaskScheduler(4);
        var done = new CountdownEvent(50);

        for (int i = 0; i < 50; i++)
        {
            await scheduler.QueueAsync(_ =>
            {
                done.Signal();
                return Task.CompletedTask;
            }, TickerTaskPriority.Normal);
        }

        Assert.True(done.Wait(TimeSpan.FromSeconds(10)), "All queued work should complete");

        var (settled, minQueued) = await SettleAsync(scheduler, TimeSpan.FromSeconds(5));
        Assert.True(settled, "Counters must reach zero");
        Assert.Equal(0, scheduler.TotalQueuedTasks);          // exactly zero, not <= 0
        Assert.Equal(0, scheduler.ActiveExecutionCount);
        Assert.True(minQueued >= 0, $"Queued counter underflowed to {minQueued}");
    }

    [Fact]
    public async Task Counter_Settles_Exactly_Zero_After_PreCancelled_Work()
    {
        await using var scheduler = new TickerQTaskScheduler(2);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        int executed = 0;
        for (int i = 0; i < 25; i++)
        {
            await scheduler.QueueAsync(_ =>
            {
                Interlocked.Increment(ref executed);
                return Task.CompletedTask;
            }, TickerTaskPriority.Normal, cts.Token);
        }

        var (settled, minQueued) = await SettleAsync(scheduler, TimeSpan.FromSeconds(5));
        Assert.True(settled, "Counters must reach zero even for cancelled work");
        Assert.Equal(0, Volatile.Read(ref executed));         // pre-cancelled => never runs
        Assert.Equal(0, scheduler.TotalQueuedTasks);          // exactly zero, not <= 0
        Assert.Equal(0, scheduler.ActiveExecutionCount);
        Assert.True(minQueued >= 0, $"Queued counter underflowed to {minQueued}");
    }

    // ------------------------------------------------------------------
    // Requirement 1 (review finding 1): concurrent producers must never make
    // the queued counter go negative (publication underflow) — an item may not
    // be dequeue-visible before its queued ownership is published.
    // ------------------------------------------------------------------

    [Fact]
    public async Task Concurrent_Producers_Never_Underflow_The_Queued_Counter()
    {
        await using var scheduler = new TickerQTaskScheduler(4);

        const int producers = 8;
        const int perProducer = 250;
        var done = new CountdownEvent(producers * perProducer);

        int minQueued = int.MaxValue;
        using var samplerCts = new CancellationTokenSource();

        // Sample the queued counter continuously while producers and consumers race.
        // A negative reading proves the enqueue-before-publish ordering bug.
        var sampler = Task.Run(async () =>
        {
            while (!samplerCts.IsCancellationRequested)
            {
                var q = scheduler.TotalQueuedTasks;
                if (q < Volatile.Read(ref minQueued)) Volatile.Write(ref minQueued, q);
                await Task.Yield();
            }
        });

        var producerTasks = new Task[producers];
        for (int p = 0; p < producers; p++)
        {
            producerTasks[p] = Task.Run(async () =>
            {
                for (int i = 0; i < perProducer; i++)
                {
                    await scheduler.QueueAsync(_ =>
                    {
                        done.Signal();
                        return Task.CompletedTask;
                    }, TickerTaskPriority.Normal);
                }
            });
        }

        await Task.WhenAll(producerTasks);
        Assert.True(done.Wait(TimeSpan.FromSeconds(30)), "All produced work should complete");

        var (settled, sampledMin) = await SettleAsync(scheduler, TimeSpan.FromSeconds(5));
        samplerCts.Cancel();
        await sampler;

        var observedMin = Math.Min(Volatile.Read(ref minQueued), sampledMin);
        Assert.True(observedMin >= 0,
            $"TotalQueuedTasks underflowed to {observedMin} — publication race between enqueue and counter");
        Assert.True(settled, "Counters must settle at zero");
        Assert.Equal(0, scheduler.TotalQueuedTasks);
        Assert.Equal(0, scheduler.ActiveExecutionCount);
    }

    // ------------------------------------------------------------------
    // Requirement 3 (mixed budget): Normal and LongRunning work draw from the
    // SAME global maxConcurrency budget; interleaving them must not exceed it.
    // ------------------------------------------------------------------

    [Fact]
    public async Task Mixed_Normal_And_LongRunning_Share_One_MaxConcurrency_Budget()
    {
        var scheduler = new TickerQTaskScheduler(2);

        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thirdStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        int active = 0, peak = 0, started = 0;
        var allDone = new CountdownEvent(10);

        Func<CancellationToken, Task> body = async _ =>
        {
            var now = Interlocked.Increment(ref active);
            UpdatePeak(ref peak, now);
            var s = Interlocked.Increment(ref started);
            if (s == 2) secondStarted.TrySetResult(true);
            if (s >= 3) thirdStarted.TrySetResult(true);
            try { await gate.Task; }
            finally { Interlocked.Decrement(ref active); allDone.Signal(); }
        };

        try
        {
            for (int i = 0; i < 10; i++)
            {
                var priority = (i % 2 == 0) ? TickerTaskPriority.Normal : TickerTaskPriority.LongRunning;
                await scheduler.QueueAsync(body, priority);
            }

            Assert.True(await WaitAsync(secondStarted.Task, TimeSpan.FromSeconds(3)),
                "Two work items should start under the shared budget");
            Assert.False(await WaitAsync(thirdStarted.Task, TimeSpan.FromMilliseconds(500)),
                "No third execution may start across the mixed Normal+LongRunning budget");
            Assert.Equal(2, Volatile.Read(ref peak));

            gate.TrySetResult(true);
            Assert.True(allDone.Wait(TimeSpan.FromSeconds(10)), "All ten mixed items must complete after release");
        }
        finally
        {
            gate.TrySetResult(true);
            await scheduler.DisposeAsync();
        }
    }

    // ------------------------------------------------------------------
    // Requirement 4: nested QueueAsync at maxConcurrency=1 that awaits the
    // child's completion must not deadlock, and must not exceed the budget
    // (inline reentrancy runs under the owning slot => still ONE active slot).
    // ------------------------------------------------------------------

    [Fact]
    public async Task Nested_QueueAsync_At_MaxConcurrency_One_Does_Not_Deadlock_And_Stays_One_Slot()
    {
        await using var scheduler = new TickerQTaskScheduler(1);

        var childRan = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var parentDone = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        int activeDuringChild = 0;

        await scheduler.QueueAsync(async _ =>
        {
            var childCompleted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            // Nested queue from within the single execution slot.
            await scheduler.QueueAsync(async _ =>
            {
                Volatile.Write(ref activeDuringChild, scheduler.ActiveExecutionCount);
                childRan.TrySetResult(true);
                childCompleted.TrySetResult(true);
                await Task.CompletedTask;
            }, TickerTaskPriority.Normal);

            // Await the child's completion from inside the parent. With a single worker
            // and pure queueing this deadlocks; inline reentrancy must avoid it.
            await childCompleted.Task;
            parentDone.TrySetResult(true);
        }, TickerTaskPriority.Normal);

        var completed = await WaitAsync(Task.WhenAll(childRan.Task, parentDone.Task), TimeSpan.FromSeconds(5));
        Assert.True(completed, "Nested scheduling at maxConcurrency=1 must complete without deadlock");
        Assert.Equal(1, Volatile.Read(ref activeDuringChild));   // inline: still exactly one active slot
    }

    [Fact]
    public async Task Reentrant_QueueAsync_After_Freeze_Is_Rejected()
    {
        var scheduler = new TickerQTaskScheduler(1);
        var parentStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var attemptNested = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var nestedResult = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        int childRan = 0;

        try
        {
            await scheduler.QueueAsync(async _ =>
            {
                parentStarted.TrySetResult(true);
                await attemptNested.Task;

                var exception = await Record.ExceptionAsync(() =>
                    scheduler.QueueAsync(_ =>
                    {
                        Interlocked.Increment(ref childRan);
                        return Task.CompletedTask;
                    }, TickerTaskPriority.Normal).AsTask());

                nestedResult.TrySetResult(exception);
            }, TickerTaskPriority.Normal);

            Assert.True(await WaitAsync(parentStarted.Task, TimeSpan.FromSeconds(3)));
            scheduler.Freeze();
            attemptNested.TrySetResult(true);

            Assert.True(await WaitAsync(nestedResult.Task, TimeSpan.FromSeconds(3)));
            Assert.IsType<InvalidOperationException>(await nestedResult.Task);
            Assert.Equal(0, Volatile.Read(ref childRan));
        }
        finally
        {
            attemptNested.TrySetResult(true);
            await scheduler.DisposeAsync();
        }
    }

    [Fact]
    public async Task Detached_Child_Does_Not_Inherit_An_Expired_Reentrant_Execution_Slot()
    {
        var scheduler = new TickerQTaskScheduler(1);
        var parentDone = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowDetachedQueue = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var childStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseChild = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task detached = Task.CompletedTask;
        int observedActiveCount = -1;

        try
        {
            await scheduler.QueueAsync(_ =>
            {
                // Task.Run captures ExecutionContext, including AsyncLocal. The inherited
                // marker must expire when this parent execution finishes.
                detached = Task.Run(async () =>
                {
                    await allowDetachedQueue.Task;
                    await scheduler.QueueAsync(async _ =>
                    {
                        Volatile.Write(ref observedActiveCount, scheduler.ActiveExecutionCount);
                        childStarted.TrySetResult(true);
                        await releaseChild.Task;
                    }, TickerTaskPriority.Normal);
                });

                parentDone.TrySetResult(true);
                return Task.CompletedTask;
            }, TickerTaskPriority.Normal);

            Assert.True(await WaitAsync(parentDone.Task, TimeSpan.FromSeconds(3)));
            Assert.True(await scheduler.WaitForRunningTasksAsync(TimeSpan.FromSeconds(3)));

            allowDetachedQueue.TrySetResult(true);
            Assert.True(await WaitAsync(childStarted.Task, TimeSpan.FromSeconds(3)));
            Assert.Equal(1, Volatile.Read(ref observedActiveCount));

            releaseChild.TrySetResult(true);
            Assert.True(await WaitAsync(detached, TimeSpan.FromSeconds(3)));
        }
        finally
        {
            allowDetachedQueue.TrySetResult(true);
            releaseChild.TrySetResult(true);
            await scheduler.DisposeAsync();
        }
    }

    [Fact]
    public async Task FireAndForget_Inline_Child_Retains_Nested_Ownership_After_Parent_Returns()
    {
        var scheduler = new TickerQTaskScheduler(1);
        var parentReturned = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var inlineStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowDeeperQueue = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var deeperRan = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var inlineDone = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseInline = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            await scheduler.QueueAsync(outerToken =>
            {
                _ = scheduler.QueueAsync(async inlineToken =>
                {
                    inlineStarted.TrySetResult(true);
                    await allowDeeperQueue.Task;
                    await scheduler.QueueAsync(_ =>
                    {
                        deeperRan.TrySetResult(true);
                        return Task.CompletedTask;
                    }, TickerTaskPriority.Normal);
                    await deeperRan.Task;
                    inlineDone.TrySetResult(true);
                    await releaseInline.Task;
                }, TickerTaskPriority.Normal);

                parentReturned.TrySetResult(true);
                return Task.CompletedTask;
            }, TickerTaskPriority.Normal);

            Assert.True(await WaitAsync(parentReturned.Task, TimeSpan.FromSeconds(3)));
            Assert.True(await WaitAsync(inlineStarted.Task, TimeSpan.FromSeconds(3)));
            allowDeeperQueue.TrySetResult(true);

            Assert.True(await WaitAsync(inlineDone.Task, TimeSpan.FromSeconds(3)),
                "A still-running inline child must route its own nested work inline after the parent returns");
            Assert.Equal(1, scheduler.ActiveExecutionCount);

            releaseInline.TrySetResult(true);
            Assert.True(await scheduler.WaitForRunningTasksAsync(TimeSpan.FromSeconds(3)));
        }
        finally
        {
            allowDeeperQueue.TrySetResult(true);
            deeperRan.TrySetResult(true);
            releaseInline.TrySetResult(true);
            await scheduler.DisposeAsync();
        }
    }

    [Fact]
    public async Task Reentrant_PreCancelled_Work_Completes_Without_Executing()
    {
        await using var scheduler = new TickerQTaskScheduler(1);
        var nestedExecuted = false;
        var parentDone = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        await scheduler.QueueAsync(async _ =>
        {
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();

            await scheduler.QueueAsync(_ =>
            {
                nestedExecuted = true;
                return Task.CompletedTask;
            }, TickerTaskPriority.Normal, cancelled.Token);

            parentDone.TrySetResult(true);
        }, TickerTaskPriority.Normal);

        Assert.True(await WaitAsync(parentDone.Task, TimeSpan.FromSeconds(3)));
        Assert.False(nestedExecuted);
        Assert.True(await scheduler.WaitForRunningTasksAsync(TimeSpan.FromSeconds(3)));
    }

    [Fact]
    public async Task Reentrant_Sibling_Work_Is_Serialized_Within_One_Execution_Slot()
    {
        var scheduler = new TickerQTaskScheduler(1);
        var parentReturned = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSecond = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        int concurrent = 0;
        int peak = 0;

        async Task Nested(TaskCompletionSource<bool> started, Task release)
        {
            var now = Interlocked.Increment(ref concurrent);
            UpdateMax(ref peak, now);
            started.TrySetResult(true);
            try { await release; }
            finally { Interlocked.Decrement(ref concurrent); }
        }

        try
        {
            await scheduler.QueueAsync(parentToken =>
            {
                _ = scheduler.QueueAsync(firstToken => Nested(firstStarted, releaseFirst.Task), TickerTaskPriority.Normal);
                _ = scheduler.QueueAsync(secondToken => Nested(secondStarted, releaseSecond.Task), TickerTaskPriority.Normal);
                parentReturned.TrySetResult(true);
                return Task.CompletedTask;
            }, TickerTaskPriority.Normal);

            Assert.True(await WaitAsync(parentReturned.Task, TimeSpan.FromSeconds(3)));
            Assert.True(await WaitAsync(firstStarted.Task, TimeSpan.FromSeconds(3)));
            Assert.False(await WaitAsync(secondStarted.Task, TimeSpan.FromMilliseconds(200)),
                "Sibling nested delegates must not overlap inside one scheduler slot");

            releaseFirst.TrySetResult(true);
            Assert.True(await WaitAsync(secondStarted.Task, TimeSpan.FromSeconds(3)));
            Assert.Equal(1, Volatile.Read(ref peak));

            releaseSecond.TrySetResult(true);
            Assert.True(await scheduler.WaitForRunningTasksAsync(TimeSpan.FromSeconds(3)));
        }
        finally
        {
            releaseFirst.TrySetResult(true);
            releaseSecond.TrySetResult(true);
            await scheduler.DisposeAsync();
        }
    }

    [Fact]
    public async Task Ignored_Nested_Fault_Is_Observed_By_The_Owning_Scope()
    {
        var marker = $"nested-fault-{Guid.NewGuid():N}";
        var unobserved = 0;
        EventHandler<UnobservedTaskExceptionEventArgs> handler = (_, args) =>
        {
            foreach (var exception in args.Exception.Flatten().InnerExceptions)
            {
                if (exception.Message != marker)
                    continue;

                Interlocked.Increment(ref unobserved);
                args.SetObserved();
                break;
            }
        };

        TaskScheduler.UnobservedTaskException += handler;
        try
        {
            await using var scheduler = new TickerQTaskScheduler(1);
            WeakReference ignoredTask = null!;

            await scheduler.QueueAsync(_ =>
            {
                ignoredTask = QueueIgnoredFault(scheduler, marker);
                return Task.CompletedTask;
            }, TickerTaskPriority.Normal);

            Assert.True(await scheduler.WaitForRunningTasksAsync(TimeSpan.FromSeconds(3)));

            for (int i = 0; i < 5 && ignoredTask.IsAlive; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                await Task.Delay(20);
            }

            Assert.False(ignoredTask.IsAlive, "The ignored nested task should be eligible for finalization");
            Assert.Equal(0, Volatile.Read(ref unobserved));
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= handler;
        }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference QueueIgnoredFault(TickerQTaskScheduler scheduler, string marker)
    {
        var task = scheduler.QueueAsync(async _ =>
        {
            await Task.Yield();
            throw new InvalidOperationException(marker);
        }, TickerTaskPriority.Normal).AsTask();
        return new WeakReference(task);
    }

    [Fact]
    public async Task Reentrant_LongRunning_Executes_On_A_Dedicated_Thread()
    {
        await using var scheduler = new TickerQTaskScheduler(1);
        var nestedRan = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        bool? wasThreadPoolThread = null;

        await scheduler.QueueAsync(async _ =>
        {
            await scheduler.QueueAsync(_ =>
            {
                wasThreadPoolThread = Thread.CurrentThread.IsThreadPoolThread;
                nestedRan.TrySetResult(true);
                return Task.CompletedTask;
            }, TickerTaskPriority.LongRunning);
        }, TickerTaskPriority.Normal);

        Assert.True(await WaitAsync(nestedRan.Task, TimeSpan.FromSeconds(3)));
        Assert.False(wasThreadPoolThread);
    }

    // ------------------------------------------------------------------
    // Requirement 2: QueueAsync racing disposal must not orphan work (leave a
    // queued item that can never drain) nor underflow the counter.
    // ------------------------------------------------------------------

    [Fact]
    public async Task QueueAsync_Racing_Disposal_Does_Not_Orphan_Or_Underflow()
    {
        for (int round = 0; round < 25; round++)
        {
            var scheduler = new TickerQTaskScheduler(4);
            int minQueued = int.MaxValue;

            var producers = new Task[6];
            for (int p = 0; p < producers.Length; p++)
            {
                producers[p] = Task.Run(async () =>
                {
                    for (int i = 0; i < 40; i++)
                    {
                        try
                        {
                            await scheduler.QueueAsync(_ => Task.CompletedTask, TickerTaskPriority.Normal);
                        }
                        catch (ObjectDisposedException) { /* rejected after disposal — fine */ }
                        catch (InvalidOperationException) { /* rejected (frozen/disposed) — fine */ }

                        var q = scheduler.TotalQueuedTasks;
                        if (q < Volatile.Read(ref minQueued)) Volatile.Write(ref minQueued, q);
                    }
                });
            }

            await Task.Delay(3);
            await scheduler.DisposeAsync();      // races with the producers
            await Task.WhenAll(producers);

            // No orphaned queued work: the counter must settle to exactly zero.
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (scheduler.TotalQueuedTasks != 0 && DateTime.UtcNow < deadline)
                await Task.Delay(5);

            Assert.Equal(0, scheduler.TotalQueuedTasks);
            Assert.True(Volatile.Read(ref minQueued) >= 0,
                $"Round {round}: queued counter underflowed to {Volatile.Read(ref minQueued)} during queue/dispose race");
        }
    }

    [Fact]
    public async Task QueueAsync_Suspended_For_Capacity_Is_Rejected_When_Freeze_Wins()
    {
        var scheduler = new TickerQTaskScheduler(1);
        var activeStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseActive = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var pendingCts = new CancellationTokenSource();

        try
        {
            await scheduler.QueueAsync(async _ =>
            {
                activeStarted.TrySetResult(true);
                await releaseActive.Task;
            }, TickerTaskPriority.Normal);
            Assert.True(await WaitAsync(activeStarted.Task, TimeSpan.FromSeconds(3)));

            // Fill the only worker queue to its fixed capacity while the worker is blocked.
            for (int i = 0; i < 1024; i++)
                await scheduler.QueueAsync(_ => Task.CompletedTask, TickerTaskPriority.Normal);

            var pending = scheduler.QueueAsync(
                _ => Task.CompletedTask,
                TickerTaskPriority.Normal,
                pendingCts.Token).AsTask();

            await Task.Delay(50);
            Assert.False(pending.IsCompleted, "The extra publication must be waiting for capacity");

            scheduler.Freeze();
            pendingCts.Cancel();

            await Assert.ThrowsAsync<InvalidOperationException>(() => pending);
            Assert.Equal(1024, scheduler.TotalQueuedTasks);
        }
        finally
        {
            releaseActive.TrySetResult(true);
            await scheduler.DisposeAsync();
        }
    }

    // ------------------------------------------------------------------
    // Requirement 3: disposal must cancel cooperative in-flight work through a
    // token linked to the scheduler stop token, so it unblocks promptly.
    // ------------------------------------------------------------------

    [Fact]
    public async Task Disposal_Cancels_Cooperative_Active_Work()
    {
        var scheduler = new TickerQTaskScheduler(2, idleWorkerTimeout: TimeSpan.FromSeconds(30));

        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var observedCancellation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        await scheduler.QueueAsync(async ct =>
        {
            started.TrySetResult(true);
            try
            {
                // Cooperative: honor the token the scheduler passes in.
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            }
            catch (OperationCanceledException)
            {
                observedCancellation.TrySetResult(true);
                throw;
            }
        }, TickerTaskPriority.Normal);

        Assert.True(await WaitAsync(started.Task, TimeSpan.FromSeconds(3)), "Work should start");

        var disposeTask = scheduler.DisposeAsync().AsTask();

        Assert.True(await WaitAsync(observedCancellation.Task, TimeSpan.FromSeconds(3)),
            "Cooperative active work must observe cancellation when the scheduler is disposed");
        Assert.True(await WaitAsync(disposeTask, TimeSpan.FromSeconds(5)),
            "Dispose should complete promptly once cooperative work cancels");
    }

    [Fact]
    public async Task Concurrent_DisposeAsync_Callers_Await_The_Same_Disposal_Completion()
    {
        var scheduler = new TickerQTaskScheduler(
            maxConcurrency: 1,
            idleWorkerTimeout: TimeSpan.FromSeconds(30),
            notifyDebounce: null,
            disposeDrainTimeout: TimeSpan.FromSeconds(2));
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        await scheduler.QueueAsync(async _ =>
        {
            started.TrySetResult(true);
            await release.Task; // deliberately non-cooperative
        }, TickerTaskPriority.Normal);
        Assert.True(await WaitAsync(started.Task, TimeSpan.FromSeconds(3)));

        var first = scheduler.DisposeAsync().AsTask();
        var second = scheduler.DisposeAsync().AsTask();

        await Task.Delay(50);
        Assert.False(first.IsCompleted);
        Assert.False(second.IsCompleted,
            "A concurrent disposer must await the shared disposal operation, not return early");

        release.TrySetResult(true);
        Assert.True(await WaitAsync(Task.WhenAll(first, second), TimeSpan.FromSeconds(3)));
    }

    // ------------------------------------------------------------------
    // Requirement 3: work that ignores the token can outlive the bounded
    // disposal wait. When it finally completes, the worker must NOT touch
    // disposed shared state (cached stop token + deferred CTS cleanup) or crash.
    // Uses a short injected drain timeout so there is no mandatory long delay.
    // ------------------------------------------------------------------

    [Fact]
    public async Task Late_Completion_After_Bounded_Disposal_Does_Not_Crash_Or_Touch_Disposed_State()
    {
        var scheduler = new TickerQTaskScheduler(
            maxConcurrency: 1,
            idleWorkerTimeout: TimeSpan.FromSeconds(30),
            notifyDebounce: null,
            disposeDrainTimeout: TimeSpan.FromMilliseconds(100));

        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new SemaphoreSlim(0);

        await scheduler.QueueAsync(async _ =>
        {
            started.TrySetResult(true);
            // Non-cooperative: ignore the token; block until released AFTER disposal.
            await release.WaitAsync();
        }, TickerTaskPriority.Normal);

        Assert.True(await WaitAsync(started.Task, TimeSpan.FromSeconds(3)), "Work should start");

        // Bounded disposal returns while the straggler is still running.
        await scheduler.DisposeAsync();
        Assert.True(scheduler.IsDisposed);

        // Let the straggler finish. The worker loops back and must survive against
        // already-disposed shared state without an unhandled background-thread crash.
        release.Release();

        var (settled, _) = await SettleAsync(scheduler, TimeSpan.FromSeconds(3));
        Assert.True(settled, "Counters must settle after the late completion");
        Assert.Equal(0, scheduler.TotalQueuedTasks);
        Assert.Equal(0, scheduler.ActiveExecutionCount);

        var workerDeadline = DateTime.UtcNow.AddSeconds(3);
        while (scheduler.ActiveWorkers != 0 && DateTime.UtcNow < workerDeadline)
            await Task.Delay(5);
        Assert.Equal(0, scheduler.ActiveWorkers);

        // Second dispose remains safe even though the last worker released shared state.
        var ex = await Record.ExceptionAsync(async () => await scheduler.DisposeAsync());
        Assert.Null(ex);

        // Diagnostics after late completion must not throw.
        Assert.NotNull(scheduler.GetDiagnostics());
    }

    // ------------------------------------------------------------------
    // Requirement 5: strict drain timeout and pre-cancelled cancellation.
    // ------------------------------------------------------------------

    [Fact]
    public async Task WaitForRunningTasks_PreCancelled_Returns_False_Even_When_Idle()
    {
        await using var scheduler = new TickerQTaskScheduler(2);

        // No work queued => idle. A pre-cancelled token must STILL return false.
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await scheduler.WaitForRunningTasksAsync(TimeSpan.FromSeconds(5), cts.Token);
        Assert.False(result, "Pre-cancelled cancellation must return false even when idle");
    }

    [Fact]
    public async Task WaitForRunningTasks_Strict_Timeout_Returns_False_While_Work_Pending()
    {
        var scheduler = new TickerQTaskScheduler(1);
        var release = new SemaphoreSlim(0);

        await scheduler.QueueAsync(async ct => await release.WaitAsync(ct), TickerTaskPriority.Normal);

        var sw = Stopwatch.StartNew();
        var result = await scheduler.WaitForRunningTasksAsync(TimeSpan.FromMilliseconds(150));
        sw.Stop();

        Assert.False(result, "Must return false when the timeout elapses before work completes");
        Assert.True(sw.Elapsed >= TimeSpan.FromMilliseconds(120),
            $"Must actually wait for the timeout ({sw.ElapsedMilliseconds}ms), not return early as drained");

        release.Release();
        await scheduler.DisposeAsync();
    }

    // ------------------------------------------------------------------
    // Requirement 6: both the original timeout-only overload and the
    // cancellation-aware overload are reachable through the interface.
    // ------------------------------------------------------------------

    [Fact]
    public async Task Interface_Exposes_Both_WaitForRunningTasks_Overloads()
    {
        await using var scheduler = new TickerQTaskScheduler(2);
        ITickerQTaskScheduler api = scheduler;

        var viaTimeoutOnly = await api.WaitForRunningTasksAsync(TimeSpan.FromMilliseconds(500));
        Assert.True(viaTimeoutOnly);

        var viaCancellationAware = await api.WaitForRunningTasksAsync(TimeSpan.FromMilliseconds(500), CancellationToken.None);
        Assert.True(viaCancellationAware);

        Assert.True(api.ActiveExecutionCount >= 0);
    }

    // ------------------------------------------------------------------
    // Requirement 6: a third-party implementer that omits the newly added
    // members must still compile and run against the interface defaults.
    // This type deliberately does NOT implement ActiveExecutionCount or the
    // cancellation-aware overload — it relies on the default interface members.
    // ------------------------------------------------------------------

    private sealed class MinimalThirdPartyScheduler : ITickerQTaskScheduler
    {
        public ValueTask QueueAsync(Func<CancellationToken, Task> work, TickerTaskPriority priority, CancellationToken cancellationToken = default)
            => default;
        public void Freeze() { }
        public void Resume() { }
        public bool IsFrozen => false;
        public int ActiveWorkers => 0;
        public int TotalQueuedTasks => 0;
        public bool IsDisposed => false;
        public string GetDiagnostics() => string.Empty;
        public Task<bool> WaitForRunningTasksAsync(TimeSpan? timeout = null) => Task.FromResult(true);
    }

    [Fact]
    public async Task ThirdParty_Minimal_Implementer_Uses_Interface_Defaults()
    {
        ITickerQTaskScheduler api = new MinimalThirdPartyScheduler();

        // Default interface member.
        Assert.Equal(0, api.ActiveExecutionCount);

        // Default interface member forwards to the implemented timeout-only overload.
        Assert.True(await api.WaitForRunningTasksAsync(TimeSpan.FromMilliseconds(10), CancellationToken.None));
        Assert.True(await api.WaitForRunningTasksAsync(TimeSpan.FromMilliseconds(10)));
    }

    [Fact]
    public void WorkItem_Retains_The_Public_Two_Argument_Constructor()
    {
        var constructor = typeof(WorkItem).GetConstructor([
            typeof(Func<CancellationToken, Task>),
            typeof(CancellationToken)
        ]);

        Assert.NotNull(constructor);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static void UpdateMax(ref int target, int candidate)
    {
        var current = Volatile.Read(ref target);
        while (candidate > current)
        {
            var observed = Interlocked.CompareExchange(ref target, candidate, current);
            if (observed == current)
                return;
            current = observed;
        }
    }

    private static async Task<bool> WaitAsync(Task task, TimeSpan timeout)
    {
        var completed = await Task.WhenAny(task, Task.Delay(timeout));
        return completed == task;
    }

    private static async Task<(bool settled, int minQueued)> SettleAsync(ITickerQTaskScheduler scheduler, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        int minQueued = scheduler.TotalQueuedTasks;
        while (DateTime.UtcNow < deadline)
        {
            var q = scheduler.TotalQueuedTasks;
            if (q < minQueued) minQueued = q;
            if (q == 0 && scheduler.ActiveExecutionCount == 0)
                return (true, minQueued);
            await Task.Delay(5);
        }

        var qFinal = scheduler.TotalQueuedTasks;
        if (qFinal < minQueued) minQueued = qFinal;
        return (qFinal == 0 && scheduler.ActiveExecutionCount == 0, minQueued);
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
