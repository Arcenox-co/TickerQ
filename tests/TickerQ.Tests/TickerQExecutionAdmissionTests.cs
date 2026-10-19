using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using TickerQ.BackgroundServices;
using TickerQ.Dispatcher;
using TickerQ.TickerQThreadPool;
using TickerQ.Utilities;
using TickerQ.Utilities.Base;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Instrumentation;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Models;

namespace TickerQ.Tests;

// These tests intentionally use the real scheduler, execution handler, and acquired-job
// lifecycle together. A mocked QueueAsync misses admission deadlocks and chain fan-out.
[Collection("TickerCancellationTokenState")]
public sealed class TickerQExecutionAdmissionTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ImmediateSameFunctionDispatch_ReturnsAfterAdmission_WithoutWaitingForItsOwnPermit(
        bool useAcquiredQueue)
    {
        await using var rig = new ExecutionRig(1, useAcquiredQueue);
        var published = Signal();
        var parentBodyExited = false;
        var childEnteredBeforeParentExit = false;
        var child = Job("Serial", (_, _, _) =>
        {
            childEnteredBeforeParentExit = !parentBodyExited;
            return Task.CompletedTask;
        }, limit: 1);
        var parent = Job("Serial", async (ct, _, _) =>
        {
            await rig.Dispatcher.DispatchAsync([child], ct);
            published.TrySetResult();
            parentBodyExited = true;
        }, limit: 1);

        await rig.Dispatcher.DispatchAsync([parent]);
        await published.Task.WaitAsync(TestTimeout);
        await rig.AssertDrainedAsync();

        Assert.False(childEnteredBeforeParentExit);
        Assert.Equal(TickerStatus.Done, parent.Status);
        Assert.Equal(TickerStatus.Done, child.Status);
        Assert.Equal(1, rig.FunctionGate.GetSemaphoreOrNull("Serial", 1)!.CurrentCount);
    }

    [Theory]
    [InlineData(1, RunCondition.InProgress)]
    [InlineData(2, RunCondition.InProgress)]
    [InlineData(1, RunCondition.OnSuccess)]
    [InlineData(2, RunCondition.OnSuccess)]
    public async Task ChainBranchesAndStandaloneJobs_ShareTheGlobalExecutionBudget(
        int concurrency, RunCondition condition)
    {
        await using var rig = new ExecutionRig(concurrency);
        var release = Signal();
        var allOrchestrated = Signal();
        var capacityOccupied = Signal();
        var tracked = new ExecutionCounter();
        var enteredOrchestration = 0;
        const int childCount = 8;

        Task Body(CancellationToken ct, IServiceProvider _, TickerFunctionContext __)
            => tracked.RunAsync(release.Task, ct, concurrency, capacityOccupied);

        var root = Job("Root", condition == RunCondition.InProgress ? Body : NoOp);
        for (var i = 0; i < childCount; i++)
            AddChild(root, Job($"Child{i}", Body), condition);
        var independent = Job("Independent", Body);
        rig.Instrumentation.When(x => x.LogJobEnqueued(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<string>()))
            .Do(_ =>
            {
                if (Interlocked.Increment(ref enteredOrchestration) == childCount + 2)
                    allOrchestrated.TrySetResult();
            });

        try
        {
            await rig.Dispatcher.DispatchAsync([root, independent]);
            await allOrchestrated.Task.WaitAsync(TestTimeout);
            await capacityOccupied.Task.WaitAsync(TestTimeout);
            Assert.False(await rig.Scheduler.WaitForRunningTasksAsync(TimeSpan.Zero));
        }
        finally
        {
            release.TrySetResult();
        }

        await rig.AssertDrainedAsync();
        Assert.Equal(concurrency, tracked.Peak);
        Assert.Equal(childCount + 1 + (condition == RunCondition.InProgress ? 1 : 0), tracked.Completed);
        Assert.Equal(TickerStatus.Done, root.Status);
        Assert.All(root.TimeTickerChildren, child => Assert.Equal(TickerStatus.Done, child.Status));
        Assert.Equal(TickerStatus.Done, independent.Status);
    }

    [Theory]
    [InlineData(RunCondition.InProgress)]
    [InlineData(RunCondition.OnSuccess)]
    public async Task DeepSameFunctionChain_CompletesWithOneExecutionSlot(RunCondition condition)
    {
        await using var rig = new ExecutionRig(1);
        var calls = 0;
        Task Body(CancellationToken _, IServiceProvider __, TickerFunctionContext ___)
        {
            Interlocked.Increment(ref calls);
            return Task.CompletedTask;
        }

        var root = Job("Serial", Body, limit: 1);
        var parent = root;
        var allJobs = new List<InternalFunctionContext> { root };
        for (var i = 0; i < 6; i++)
        {
            var child = Job("Serial", Body, limit: 1);
            AddChild(parent, child, condition);
            allJobs.Add(child);
            parent = child;
        }

        await rig.Dispatcher.DispatchAsync([root]);
        await rig.AssertDrainedAsync();

        Assert.Equal(7, calls);
        Assert.All(allJobs, job => Assert.Equal(TickerStatus.Done, job.Status));
    }

    [Fact]
    public async Task FunctionPermitWaiters_DoNotConsumeExecutionSlotsNeededByAnotherFunction()
    {
        await using var rig = new ExecutionRig(2);
        var heldEntered = Signal();
        var release = Signal();
        var waiterOrchestrated = Signal();
        var independentEntered = Signal();
        var held = Job("Serial", async (ct, _, _) =>
        {
            heldEntered.TrySetResult();
            await release.Task.WaitAsync(ct);
        }, limit: 1);
        var waiting = Job("Serial", NoOp, limit: 1);
        rig.Instrumentation.When(x => x.LogJobEnqueued(
                Arg.Any<string>(), "Serial", waiting.TickerId, Arg.Any<string>()))
            .Do(_ => waiterOrchestrated.TrySetResult());
        var independent = Job("Unrelated", (_, _, _) =>
        {
            independentEntered.TrySetResult();
            return Task.CompletedTask;
        });

        try
        {
            await rig.Dispatcher.DispatchAsync([held]);
            await heldEntered.Task.WaitAsync(TestTimeout);
            await rig.Dispatcher.DispatchAsync([waiting]);
            await waiterOrchestrated.Task.WaitAsync(TestTimeout);
            await WaitUntilAsync(() => rig.Scheduler.WaitingForFunctionCount == 1);
            await rig.Dispatcher.DispatchAsync([independent]);
            await independentEntered.Task.WaitAsync(TestTimeout);

            Assert.False(release.Task.IsCompleted);
            Assert.Equal(0, rig.FunctionGate.GetSemaphoreOrNull("Serial", 1)!.CurrentCount);
        }
        finally
        {
            release.TrySetResult();
        }

        await rig.AssertDrainedAsync();
        Assert.Equal(TickerStatus.Done, held.Status);
        Assert.Equal(TickerStatus.Done, waiting.Status);
        Assert.Equal(TickerStatus.Done, independent.Status);
    }

    [Fact]
    public async Task RetryDelay_ReleasesExecutionCapacity_ButRemainsTrackedForDrainAndCancellation()
    {
        await using var rig = new ExecutionRig(1);
        var retryScheduled = Signal();
        var independentEntered = Signal();
        var attempts = 0;
        var retrying = Job("Retrying", (_, _, _) =>
        {
            Interlocked.Increment(ref attempts);
            throw new InvalidOperationException("The first attempt fails deliberately.");
        }, limit: 1);
        retrying.Retries = 1;
        retrying.RetryIntervals = [60];
        rig.Instrumentation.When(x => x.LogJobRetryScheduled(
                retrying.TickerId, "Retrying", 1, 1, 60))
            .Do(_ => retryScheduled.TrySetResult());
        var independent = Job("Unrelated", (_, _, _) =>
        {
            independentEntered.TrySetResult();
            return Task.CompletedTask;
        });

        try
        {
            await rig.Dispatcher.DispatchAsync([retrying]);
            await retryScheduled.Task.WaitAsync(TestTimeout);
            await WaitUntilAsync(() => rig.Scheduler.RetryingCount == 1);
            Assert.False(await rig.Scheduler.WaitForRunningTasksAsync(TimeSpan.Zero));
            Assert.Equal(1, TickerCancellationTokenManager.ActiveCount);
            Assert.Equal(1, rig.FunctionGate.GetSemaphoreOrNull("Retrying", 1)!.CurrentCount);

            await rig.Dispatcher.DispatchAsync([independent]);
            await independentEntered.Task.WaitAsync(TestTimeout);
            Assert.Equal(1, Volatile.Read(ref attempts));
        }
        finally
        {
            TickerCancellationTokenManager.RequestTickerCancellationById(retrying.TickerId);
        }

        await rig.AssertDrainedAsync();
        Assert.Equal(TickerStatus.Cancelled, retrying.Status);
        Assert.Equal(TickerStatus.Done, independent.Status);
    }

    [Fact]
    public async Task TimeoutPending_HoldsGlobalAndFunctionPermits_UntilTheDelegateActuallyExits()
    {
        await using var rig = new ExecutionRig(1, configure: options =>
        {
            options.DefaultExecutionTimeout = TimeSpan.FromMilliseconds(40);
            options.TimeoutGracePeriod = TimeSpan.FromMilliseconds(40);
        });
        var entered = Signal();
        var release = Signal();
        var timeoutPending = Signal();
        var sameFunctionCalls = 0;
        var otherFunctionCalls = 0;
        var stuck = Job("Serial", (_, _, _) =>
        {
            entered.TrySetResult();
            return release.Task; // Deliberately ignores cancellation to exercise timeout ownership.
        }, limit: 1);
        rig.Instrumentation.When(x => x.LogJobTimeoutPending(
                stuck.TickerId, "Serial", Arg.Any<TimeSpan>(), Arg.Any<TimeSpan>()))
            .Do(_ => timeoutPending.TrySetResult());
        var sameFunction = Job("Serial", (_, _, _) =>
        {
            Interlocked.Increment(ref sameFunctionCalls);
            return Task.CompletedTask;
        }, limit: 1);
        var independent = Job("Unrelated", (_, _, _) =>
        {
            Interlocked.Increment(ref otherFunctionCalls);
            return Task.CompletedTask;
        });

        try
        {
            await rig.Dispatcher.DispatchAsync([stuck]);
            await entered.Task.WaitAsync(TestTimeout);
            await rig.Dispatcher.DispatchAsync([sameFunction, independent]);
            await timeoutPending.Task.WaitAsync(TestTimeout);

            Assert.Equal(0, Volatile.Read(ref sameFunctionCalls));
            Assert.Equal(0, Volatile.Read(ref otherFunctionCalls));
            Assert.Equal(1, rig.Scheduler.ActiveExecutionCount);
            Assert.Equal(0, rig.FunctionGate.GetSemaphoreOrNull("Serial", 1)!.CurrentCount);
            Assert.Equal(3, TickerCancellationTokenManager.ActiveCount);
            Assert.False(await rig.Scheduler.WaitForRunningTasksAsync(TimeSpan.Zero));
        }
        finally
        {
            release.TrySetResult();
        }

        await rig.AssertDrainedAsync();
        Assert.Equal(TickerStatus.Cancelled, stuck.Status);
        Assert.Equal(TickerStatus.Done, sameFunction.Status);
        Assert.Equal(TickerStatus.Done, independent.Status);
        Assert.Equal(1, sameFunctionCalls);
        Assert.Equal(1, otherFunctionCalls);
        Assert.Equal(1, rig.FunctionGate.GetSemaphoreOrNull("Serial", 1)!.CurrentCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Freeze_RejectsNewRoots_ButAcceptedChainAndRetryAttemptsCanDrain(bool retry)
    {
        await using var rig = new ExecutionRig(1);
        var entered = Signal();
        var release = Signal();
        var parentCalls = 0;
        var childCalls = 0;
        var parent = Job("Serial", async (ct, _, _) =>
        {
            var attempt = Interlocked.Increment(ref parentCalls);
            if (attempt == 1)
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(ct);
                if (retry) throw new InvalidOperationException("Retry after the admission freeze.");
            }
        }, limit: 1);
        if (retry)
        {
            parent.Retries = 1;
            parent.RetryIntervals = [0];
        }
        var child = Job("Serial", (_, _, _) =>
        {
            Interlocked.Increment(ref childCalls);
            return Task.CompletedTask;
        }, limit: 1);
        AddChild(parent, child, RunCondition.OnSuccess);
        var rejectedCalls = 0;
        var rejected = Job("NewRoot", (_, _, _) =>
        {
            Interlocked.Increment(ref rejectedCalls);
            return Task.CompletedTask;
        });

        try
        {
            await rig.Dispatcher.DispatchAsync([parent]);
            await entered.Task.WaitAsync(TestTimeout);
            rig.Scheduler.Freeze();
            await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Dispatcher.DispatchAsync([rejected]));
        }
        finally
        {
            release.TrySetResult();
        }

        await rig.AssertDrainedAsync();
        Assert.Equal(retry ? 2 : 1, parentCalls);
        Assert.Equal(1, childCalls);
        Assert.Equal(0, rejectedCalls);
        Assert.Equal(TickerStatus.Done, parent.Status);
        Assert.Equal(TickerStatus.Done, child.Status);
    }

    [Fact]
    public async Task NestedImmediateDispatch_WhenOrchestrationIsFull_RejectsAndReleasesWithoutDeadlock()
    {
        await using var rig = new ExecutionRig(1, orchestrationCapacity: 2);
        var parentEntered = Signal();
        var tryPublish = Signal();
        var rejected = Signal();
        var nestedCalls = 0;
        var nested = Job("Serial", (_, _, _) =>
        {
            Interlocked.Increment(ref nestedCalls);
            return Task.CompletedTask;
        }, limit: 1);
        var parent = Job("Serial", async (ct, _, _) =>
        {
            parentEntered.TrySetResult();
            await tryPublish.Task.WaitAsync(ct);
            try
            {
                await rig.Dispatcher.DispatchAsync([nested], ct);
            }
            catch (InvalidOperationException)
            {
                rejected.TrySetResult();
            }
        }, limit: 1);
        var waiting = Job("Serial", NoOp, limit: 1);

        try
        {
            await rig.Dispatcher.DispatchAsync([parent]);
            await parentEntered.Task.WaitAsync(TestTimeout);
            await rig.Dispatcher.DispatchAsync([waiting]);
            Assert.Equal(2, ((ITickerQExecutionScheduler)rig.Scheduler).ActiveOrchestrationCount);
            tryPublish.TrySetResult();
            await rejected.Task.WaitAsync(TestTimeout);
            await rig.AssertDrainedAsync();
        }
        finally
        {
            tryPublish.TrySetResult();
        }

        Assert.Equal(0, nestedCalls);
        Assert.Equal(TickerStatus.Done, parent.Status);
        Assert.Equal(TickerStatus.Done, waiting.Status);
        await rig.Manager.Received(1).ReleaseAcquiredResources(
            Arg.Is<InternalFunctionContext[]>(contexts => contexts.Length == 1 &&
                contexts[0].TickerId == nested.TickerId &&
                contexts[0].AcquisitionToken == nested.AcquisitionToken), CancellationToken.None);
    }

    [Theory]
    [InlineData(TickerTaskPriority.Normal)]
    [InlineData(TickerTaskPriority.LongRunning)]
    public async Task AttemptInvocation_PreservesItsJobActivityAcrossQueueAndAwait(TickerTaskPriority priority)
    {
        await using var rig = new ExecutionRig(1);
        Activity? expected = null;
        Activity? atEntry = null;
        Activity? afterAwait = null;
        rig.Instrumentation.StartJobActivity(Arg.Any<string>(), Arg.Any<InternalFunctionContext>())
            .Returns(_ => expected = new Activity("tickerq.job.execution-admission-test").Start());
        var job = Job("Traced", async (_, _, _) =>
        {
            atEntry = Activity.Current;
            await Task.Yield();
            afterAwait = Activity.Current;
        });
        job.CachedPriority = priority;

        await rig.Dispatcher.DispatchAsync([job]);
        await rig.AssertDrainedAsync();

        Assert.NotNull(expected);
        Assert.Same(expected, atEntry);
        Assert.Same(expected, afterAwait);
        Assert.Equal(TickerStatus.Done, job.Status);
    }

    [Fact]
    public async Task CancellingAdmissionAfterExecutionStarted_DoesNotCancelTheAcceptedJob()
    {
        await using var rig = new ExecutionRig(1);
        using var admission = new CancellationTokenSource();
        var entered = Signal();
        var release = Signal();
        CancellationToken executionToken = default;
        var job = Job("Accepted", async (ct, _, _) =>
        {
            executionToken = ct;
            entered.TrySetResult();
            await release.Task.WaitAsync(ct);
        });

        try
        {
            await rig.Dispatcher.DispatchAsync([job], admission.Token);
            await entered.Task.WaitAsync(TestTimeout);
            admission.Cancel();

            Assert.False(executionToken.IsCancellationRequested);
            Assert.Equal(1, TickerCancellationTokenManager.ActiveCount);
            Assert.False(await rig.Scheduler.WaitForRunningTasksAsync(TimeSpan.Zero));
        }
        finally
        {
            release.TrySetResult();
        }

        await rig.AssertDrainedAsync();
        Assert.Equal(TickerStatus.Done, job.Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Disposal_CancelsAndUnregistersOrchestrationWaitingOutsideExecutionSlots(bool retrying)
    {
        await using var rig = new ExecutionRig(1);
        var permit = rig.FunctionGate.GetSemaphoreOrNull("Waiting", 1)!;
        var calls = 0;
        var job = Job("Waiting", (_, _, _) =>
        {
            Interlocked.Increment(ref calls);
            throw new InvalidOperationException("Hold orchestration in retry delay.");
        }, limit: 1);
        if (retrying)
        {
            job.Retries = 1;
            job.RetryIntervals = [60];
        }
        else
        {
            // Hold the permit without occupying a scheduler slot, as another execution
            // boundary can do. Cancelling this waiter must not release our permit.
            await permit.WaitAsync();
        }

        try
        {
            await rig.Dispatcher.DispatchAsync([job]);
            await WaitUntilAsync(() => rig.Scheduler.ActiveExecutionCount == 0 && (retrying
                ? rig.Scheduler.RetryingCount == 1
                : rig.Scheduler.WaitingForFunctionCount == 1));
            Assert.Equal(0, rig.Scheduler.ActiveExecutionCount);
            Assert.Equal(1, rig.Scheduler.ActiveOrchestrationCount);
            Assert.Equal(1, TickerCancellationTokenManager.ActiveCount);

            await rig.Scheduler.DisposeAsync();
            await rig.AssertDrainedAsync();

            Assert.Equal(TickerStatus.Cancelled, job.Status);
            Assert.Equal(retrying ? 1 : 0, calls);
            Assert.Equal(retrying ? 1 : 0, permit.CurrentCount);
        }
        finally
        {
            if (!retrying) permit.Release();
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task BareOperationCanceledException_WithAnUncancelledJobToken_PreservesRetryAndFailureSemantics(
        bool recoverOnRetry, bool throwAfterAwait)
    {
        await using var rig = new ExecutionRig(1);
        const string failureMessage = "A dependency interrupted without cancelling the job token.";
        var calls = 0;
        var observedCancellation = false;

        Task Invoke(CancellationToken ct, IServiceProvider _, TickerFunctionContext __)
        {
            var attempt = Interlocked.Increment(ref calls);
            observedCancellation |= ct.IsCancellationRequested;
            if (recoverOnRetry && attempt == 2) return Task.CompletedTask;
            if (throwAfterAwait) return ThrowAsynchronously();
            throw new OperationCanceledException(failureMessage);
        }

        async Task ThrowAsynchronously()
        {
            await Task.Yield();
            throw new OperationCanceledException(failureMessage);
        }

        var job = Job("InterruptedDependency", Invoke, limit: 1);
        job.Retries = 1;
        job.RetryIntervals = [0];

        await rig.Dispatcher.DispatchAsync([job]);
        await rig.AssertDrainedAsync();

        Assert.False(observedCancellation);
        Assert.Equal(2, calls);
        Assert.Equal(recoverOnRetry ? TickerStatus.Done : TickerStatus.Failed, job.Status);
        if (!recoverOnRetry) Assert.Contains(failureMessage, job.ExceptionDetails);
        Assert.Equal(1, rig.FunctionGate.GetSemaphoreOrNull("InterruptedDependency", 1)!.CurrentCount);
    }

    private sealed class ExecutionRig : IAsyncDisposable
    {
        private readonly ServiceProvider _services;
        private readonly AcquiredExecutionQueue? _acquired;
        internal TickerQTaskScheduler Scheduler { get; }
        internal TickerQDispatcher Dispatcher { get; }
        internal TickerFunctionConcurrencyGate FunctionGate { get; } = new();
        internal ITickerQInstrumentation Instrumentation { get; } = Substitute.For<ITickerQInstrumentation>();
        internal IInternalTickerManager Manager { get; } = Substitute.For<IInternalTickerManager>();

        internal ExecutionRig(int concurrency, bool useAcquiredQueue = true,
            Action<SchedulerOptionsBuilder>? configure = null, int? orchestrationCapacity = null)
        {
            Scheduler = orchestrationCapacity is { } capacity
                ? new TickerQTaskScheduler(concurrency, queueCapacity: 8, orchestrationCapacity: capacity)
                : new TickerQTaskScheduler(concurrency);
            var options = new SchedulerOptionsBuilder
            {
                MaxConcurrency = concurrency,
                TimeoutGracePeriod = TimeSpan.Zero
            };
            configure?.Invoke(options);
            var clock = Substitute.For<ITickerClock>();
            clock.UtcNow.Returns(_ => DateTime.UtcNow);
            var license = LicenseTestState.Active(clock);
            _services = new ServiceCollection()
                .AddSingleton<ITickerQTaskScheduler>(Scheduler)
                .BuildServiceProvider();
            var handler = new TickerExecutionTaskHandler(_services, clock, Instrumentation, Manager,
                options, Substitute.For<ITickerQFailureNotifier>(), license, FunctionGate);
            if (useAcquiredQueue)
                _acquired = new AcquiredExecutionQueue(Manager, handler, Scheduler, NullLogger.Instance, license);
            Dispatcher = new TickerQDispatcher(Scheduler, handler, license, _acquired);
        }

        internal async Task AssertDrainedAsync()
        {
            Assert.True(await Scheduler.WaitForRunningTasksAsync(TestTimeout), Scheduler.GetDiagnostics());
            Assert.Equal(0, Scheduler.ActiveExecutionCount);
            Assert.Equal(0, Scheduler.ActiveOrchestrationCount);
            Assert.Equal(0, Scheduler.WaitingForFunctionCount);
            Assert.Equal(0, Scheduler.RetryingCount);
            Assert.Equal(0, Scheduler.TotalQueuedTasks);
            Assert.Equal(0, TickerCancellationTokenManager.ActiveCount);
        }

        public async ValueTask DisposeAsync()
        {
            if (_acquired != null)
                await _acquired.StopAsync(CancellationToken.None);
            await Scheduler.DisposeAsync();
            _acquired?.Dispose();
            await _services.DisposeAsync();
            TickerCancellationTokenManager.CleanUpTickerCancellationTokens();
        }
    }

    private sealed class ExecutionCounter
    {
        private int _active;
        private int _peak;
        private int _completed;
        internal int Peak => Volatile.Read(ref _peak);
        internal int Completed => Volatile.Read(ref _completed);

        internal async Task RunAsync(Task release, CancellationToken ct, int target, TaskCompletionSource occupied)
        {
            var active = Interlocked.Increment(ref _active);
            int previous;
            do
            {
                previous = Volatile.Read(ref _peak);
                if (active <= previous) break;
            } while (Interlocked.CompareExchange(ref _peak, active, previous) != previous);
            if (active >= target) occupied.TrySetResult();
            try
            {
                await release.WaitAsync(ct);
                Interlocked.Increment(ref _completed);
            }
            finally
            {
                Interlocked.Decrement(ref _active);
            }
        }
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static Task NoOp(CancellationToken _, IServiceProvider __, TickerFunctionContext ___) => Task.CompletedTask;

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var deadline = new CancellationTokenSource(TestTimeout);
        while (!condition())
            await Task.Delay(10, deadline.Token);
    }

    private static InternalFunctionContext Job(string function, TickerFunctionDelegate body, int limit = 0) => new()
    {
        TickerId = Guid.NewGuid(),
        FunctionName = function,
        Type = TickerType.TimeTicker,
        AcquisitionToken = Guid.NewGuid(),
        ChainGeneration = Guid.NewGuid(),
        ExecutionTime = DateTime.UtcNow,
        RetryIntervals = [],
        Status = TickerStatus.Idle,
        CachedDelegate = body,
        CachedMaxConcurrency = limit,
        TimeTickerChildren = []
    };

    private static void AddChild(InternalFunctionContext parent, InternalFunctionContext child, RunCondition condition)
    {
        child.ParentId = parent.TickerId;
        child.ChainGeneration = parent.ChainGeneration;
        child.RunCondition = condition;
        parent.TimeTickerChildren.Add(child);
    }
}
