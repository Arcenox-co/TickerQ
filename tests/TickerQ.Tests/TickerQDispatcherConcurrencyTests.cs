using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using TickerQ.Dispatcher;
using TickerQ.Utilities;
using TickerQ.Utilities.Base;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Instrumentation;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Models;

namespace TickerQ.Tests;

[Collection("TickerCancellationTokenState")]
public sealed class TickerQDispatcherConcurrencyTests : IDisposable
{
    private readonly ServiceProvider _services = new ServiceCollection().BuildServiceProvider();
    private readonly TickerFunctionConcurrencyGate _gate = new();
    private readonly IInternalTickerManager _manager = Substitute.For<IInternalTickerManager>();
    private readonly ITickerQInstrumentation _instrumentation = Substitute.For<ITickerQInstrumentation>();
    private readonly TickerQDispatcher _dispatcher;
    private readonly List<Func<CancellationToken, Task>> _queued = [];
    private int _active;
    private int _peak;
    private int _completed;

    public TickerQDispatcherConcurrencyTests()
    {
        var clock = Substitute.For<ITickerClock>();
        clock.UtcNow.Returns(DateTime.UtcNow);
        var license = LicenseTestState.Active(clock);
        var handler = new TickerExecutionTaskHandler(_services, clock, _instrumentation, _manager,
            new SchedulerOptionsBuilder { TimeoutGracePeriod = TimeSpan.Zero },
            Substitute.For<ITickerQFailureNotifier>(), license, _gate);
        var scheduler = Substitute.For<ITickerQTaskScheduler>();
        scheduler.QueueAsync(Arg.Any<Func<CancellationToken, Task>>(),
                Arg.Any<TickerTaskPriority>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                _queued.Add(call.ArgAt<Func<CancellationToken, Task>>(0));
                return ValueTask.CompletedTask;
            });
        _dispatcher = new TickerQDispatcher(scheduler, handler, license);
    }

    public void Dispose()
    {
        TickerCancellationTokenManager.CleanUpTickerCancellationTokens();
        _services.Dispose();
    }

    [Theory]
    [InlineData(TickerType.TimeTicker)]
    [InlineData(TickerType.CronTickerOccurrence)]
    public async Task StandaloneJobs_OfSameFunction_AreSerialized(TickerType type)
    {
        var jobs = Enumerable.Range(0, 4).Select(_ => Job("Serial", TrackedBody)).ToArray();
        foreach (var job in jobs) job.Type = type;
        await Run(jobs);
        Assert.Equal(1, _peak);
        Assert.Equal(4, _completed);
        Assert.All(jobs, job => Assert.Equal(TickerStatus.Done, job.Status));
    }

    [Theory]
    [InlineData(RunCondition.OnSuccess, 1)]
    [InlineData(RunCondition.InProgress, 1)]
    [InlineData(RunCondition.OnSuccess, 2)]
    [InlineData(RunCondition.InProgress, 2)]
    public async Task SiblingJobs_RespectTheirFunctionLimit(RunCondition condition, int limit)
    {
        var root = Job("Parent", NoOp);
        for (var i = 0; i < 5; i++) AddChild(root, Job("Serial", TrackedBody, limit), condition);
        await Run(root);
        Assert.InRange(_peak, 1, limit);
        Assert.Equal(5, _completed);
        Assert.All(root.TimeTickerChildren, child => Assert.Equal(TickerStatus.Done, child.Status));
    }

    [Fact]
    public async Task StandaloneAndNestedJobs_ShareOneFunctionLimit()
    {
        var root = Job("Parent", NoOp);
        var child = Job("Branch", NoOp);
        AddChild(root, child);
        AddChild(child, Job("Serial", TrackedBody));
        AddChild(child, Job("Serial", TrackedBody));
        await Run(root, Job("Serial", TrackedBody), Job("Serial", TrackedBody));
        Assert.Equal(1, _peak);
        Assert.Equal(4, _completed);
    }

    [Theory]
    [InlineData(RunCondition.OnSuccess)]
    [InlineData(RunCondition.InProgress)]
    public async Task SameFunction_ThroughoutDeepChain_CompletesWithoutDeadlock(RunCondition condition)
    {
        var root = Job("Serial", TrackedBody);
        var parent = root;
        for (var i = 0; i < 6; i++)
        {
            var child = Job("Serial", TrackedBody);
            AddChild(parent, child, condition);
            parent = child;
        }
        await Run(root);
        Assert.Equal(1, _peak);
        Assert.Equal(7, _completed);
        Assert.Equal(TickerStatus.Done, parent.Status);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(2, false)]
    [InlineData(1, true)]
    public async Task AvailableCapacity_AllowsIndependentJobsToOverlap(int limit, bool differentFunctions)
    {
        var bothEntered = Signal();
        var entered = 0;
        async Task Body(CancellationToken ct, IServiceProvider _, TickerFunctionContext __)
        {
            if (Interlocked.Increment(ref entered) == 2) bothEntered.TrySetResult();
            await bothEntered.Task.WaitAsync(TimeSpan.FromSeconds(3), ct);
        }
        var root = Job("Parent", NoOp);
        AddChild(root, Job("A", Body, limit));
        AddChild(root, Job(differentFunctions ? "B" : "A", Body, limit));
        await Run(root);
        Assert.Equal(2, entered);
        Assert.All(root.TimeTickerChildren, child => Assert.Equal(TickerStatus.Done, child.Status));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedOrCancelledChild_ReleasesPermitForSibling(bool cancel)
    {
        var attempts = 0;
        var root = Job("Parent", NoOp);
        var failing = Job("Serial", (_, _, _) =>
        {
            Interlocked.Increment(ref attempts);
            throw cancel ? new TaskCanceledException("cancelled") : new InvalidOperationException("failure");
        });
        failing.Retries = 2;
        failing.RetryIntervals = [0];
        AddChild(root, failing);
        var succeeding = Job("Serial", TrackedBody);
        AddChild(root, succeeding);
        await Run(root);
        Assert.Equal(cancel ? TickerStatus.Cancelled : TickerStatus.Failed, failing.Status);
        Assert.Equal(cancel ? 1 : 3, attempts);
        Assert.Equal(TickerStatus.Done, succeeding.Status);
        Assert.Equal(1, _gate.GetSemaphoreOrNull("Serial", 1)!.CurrentCount);
    }

    [Fact]
    public async Task CancellationWhileWaiting_DoesNotInvokeChildOrReleaseAnotherJobsPermit()
    {
        var permit = _gate.GetSemaphoreOrNull("Serial", 1)!;
        await permit.WaitAsync();
        var root = Job("Parent", NoOp);
        var invoked = false;
        var child = Job("Serial", (_, _, _) => { invoked = true; return Task.CompletedTask; });
        AddChild(root, child);
        var waiting = Signal();
        _manager.UpdateTickerAsync(child, Arg.Any<CancellationToken>()).Returns(_ =>
        {
            if (child.Status == TickerStatus.InProgress) waiting.TrySetResult();
            return Task.CompletedTask;
        });
        var run = Run(root);
        try
        {
            await waiting.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.True(TickerCancellationTokenManager.RequestTickerCancellationById(child.TickerId));
            await run;
            Assert.False(invoked);
            Assert.Equal(TickerStatus.Cancelled, child.Status);
            Assert.Equal(0, permit.CurrentCount);
            Assert.Equal(0, TickerCancellationTokenManager.ActiveCount);
        }
        finally { permit.Release(); }
    }

    [Fact]
    public async Task Timeout_DoesNotReleasePermitUntilFunctionActuallyExits()
    {
        var timeoutPending = Signal();
        _instrumentation.When(i => i.LogJobTimeoutPending(Arg.Any<Guid>(), Arg.Any<string>(),
                Arg.Any<TimeSpan>(), Arg.Any<TimeSpan>()))
            .Do(_ => timeoutPending.TrySetResult());
        var release = Signal();
        var root = Job("Parent", NoOp);
        var first = Job("Serial", (_, _, _) => release.Task);
        first.TimeoutSeconds = 1;
        var secondCalled = false;
        var second = Job("Serial", (_, _, _) => { secondCalled = true; return Task.CompletedTask; });
        AddChild(root, first);
        AddChild(root, second);
        var run = Run(root);
        try
        {
            await timeoutPending.Task.WaitAsync(TimeSpan.FromSeconds(4));
            Assert.False(secondCalled);
            Assert.Equal(0, _gate.GetSemaphoreOrNull("Serial", 1)!.CurrentCount);
        }
        finally { release.TrySetResult(); await run; }
        Assert.True(secondCalled);
        Assert.Equal(TickerStatus.Cancelled, first.Status);
        Assert.Equal(TickerStatus.Done, second.Status);
    }

    private async Task Run(params InternalFunctionContext[] contexts)
    {
        await _dispatcher.DispatchAsync(contexts);
        var work = _queued.ToArray();
        _queued.Clear();
        await Task.WhenAll(work.Select(w => w(CancellationToken.None))).WaitAsync(TimeSpan.FromSeconds(10));
    }

    private async Task TrackedBody(CancellationToken ct, IServiceProvider _, TickerFunctionContext __)
    {
        var active = Interlocked.Increment(ref _active);
        int previous;
        do
        {
            previous = Volatile.Read(ref _peak);
            if (active <= previous) break;
        } while (Interlocked.CompareExchange(ref _peak, active, previous) != previous);
        try { await Task.Delay(20, ct); Interlocked.Increment(ref _completed); }
        finally { Interlocked.Decrement(ref _active); }
    }

    private static Task NoOp(CancellationToken _, IServiceProvider __, TickerFunctionContext ___) => Task.CompletedTask;
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static InternalFunctionContext Job(string name, TickerFunctionDelegate body, int limit = 1) => new()
    {
        TickerId = Guid.NewGuid(), FunctionName = name, Type = TickerType.TimeTicker,
        AcquisitionToken = Guid.NewGuid(), ChainGeneration = Guid.NewGuid(),
        ExecutionTime = DateTime.UtcNow, RetryIntervals = [], Status = TickerStatus.Idle,
        CachedDelegate = body, CachedMaxConcurrency = limit, TimeTickerChildren = []
    };

    private static void AddChild(InternalFunctionContext parent, InternalFunctionContext child,
        RunCondition condition = RunCondition.OnSuccess)
    {
        child.ParentId = parent.TickerId;
        child.ChainGeneration = parent.ChainGeneration;
        child.RunCondition = condition;
        parent.TimeTickerChildren.Add(child);
    }
}
