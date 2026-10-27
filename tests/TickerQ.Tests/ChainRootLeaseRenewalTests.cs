using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using TickerQ.BackgroundServices;
using TickerQ.Dispatcher;
using TickerQ.Utilities;
using TickerQ.Utilities.Base;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Exceptions;
using TickerQ.Utilities.Instrumentation;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Models;

namespace TickerQ.Tests;

/// <summary>
/// Engine side of the chain lease: a root whose descendants run after its own terminal write keeps
/// its lease through that write and releases it once the whole tree has finished. Provider
/// semantics are covered by the shared reliability contract and the EF end-to-end tests.
/// </summary>
[Collection("TickerCancellationTokenState")]
public sealed class ChainRootLeaseRenewalTests : IDisposable
{
    private readonly ServiceProvider _services = new ServiceCollection().BuildServiceProvider();
    private readonly IInternalTickerManager _manager = Substitute.For<IInternalTickerManager>();

    public void Dispose()
    {
        TickerCancellationTokenManager.CleanUpTickerCancellationTokens();
        _services.Dispose();
    }

    [Fact]
    public async Task ChainRoot_TerminalWriteRetainsLease_AndReleasesItOnlyAfterDescendantsFinish()
    {
        var events = new List<string>();
        _manager.UpdateTickerAsync(Arg.Any<InternalFunctionContext>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var context = call.Arg<InternalFunctionContext>();
                if (context.Status == TickerStatus.Done)
                    lock (events) events.Add($"{context.FunctionName}:Done:retains={context.RetainsChainLease}");
                return Task.CompletedTask;
            });
        _manager.ReleaseChainLeaseAsync(Arg.Any<InternalFunctionContext>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                lock (events) events.Add($"release:{call.Arg<InternalFunctionContext>().FunctionName}");
                return Task.CompletedTask;
            });
        var root = Job("Root");
        var child = Job("Child");
        AddChild(root, child);

        await Run(root);

        Assert.Equal(["Root:Done:retains=True", "Child:Done:retains=False", "release:Root"], events);
        Assert.Equal(TickerStatus.Done, child.Status);
    }

    [Fact]
    public async Task LeafRoot_KeepsTheLegacyTerminalWrite_AndHasNoChainLeaseToRelease()
    {
        InternalFunctionContext? terminal = null;
        _manager.UpdateTickerAsync(Arg.Any<InternalFunctionContext>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                if (call.Arg<InternalFunctionContext>().Status == TickerStatus.Done)
                    terminal = call.Arg<InternalFunctionContext>();
                return Task.CompletedTask;
            });

        await Run(Job("Leaf"));

        Assert.NotNull(terminal);
        Assert.False(terminal!.RetainsChainLease);
        await _manager.DidNotReceive().ReleaseChainLeaseAsync(Arg.Any<InternalFunctionContext>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ChainRoot_FailingRelease_DoesNotFailACompletedChain()
    {
        _manager.ReleaseChainLeaseAsync(Arg.Any<InternalFunctionContext>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("store unavailable"));
        var root = Job("Root");
        var child = Job("Child");
        AddChild(root, child);

        await Run(root);

        Assert.Equal(TickerStatus.Done, root.Status);
        Assert.Equal(TickerStatus.Done, child.Status);
        await _manager.Received(1).ReleaseChainLeaseAsync(root, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ChainRoot_RejectedTerminalWrite_DoesNotStartDescendants_ButStillReleases()
    {
        _manager.UpdateTickerAsync(Arg.Is<InternalFunctionContext>(c => c.FunctionName == "Root" &&
                c.Status == TickerStatus.Done), Arg.Any<CancellationToken>())
            .ThrowsAsync(new TickerResultNotAcknowledgedException("stale generation"));
        var childRan = false;
        var root = Job("Root");
        var child = Job("Child", (_, _, _) => { childRan = true; return Task.CompletedTask; });
        AddChild(root, child);

        await Assert.ThrowsAsync<TickerResultNotAcknowledgedException>(() => Run(root));

        Assert.False(childRan);
        await _manager.Received(1).ReleaseChainLeaseAsync(root, Arg.Any<CancellationToken>());
    }

    private async Task Run(InternalFunctionContext root)
    {
        var clock = Substitute.For<ITickerClock>();
        clock.UtcNow.Returns(DateTime.UtcNow);
        var license = LicenseTestState.Active(clock);
        var handler = new TickerExecutionTaskHandler(_services, clock, Substitute.For<ITickerQInstrumentation>(),
            _manager, new SchedulerOptionsBuilder(), Substitute.For<ITickerQFailureNotifier>(), license,
            new TickerFunctionConcurrencyGate());
        var scheduler = Substitute.For<ITickerQTaskScheduler>();
        var work = new List<Func<CancellationToken, Task>>();
        scheduler.QueueAsync(Arg.Any<Func<CancellationToken, Task>>(), Arg.Any<TickerTaskPriority>(), Arg.Any<CancellationToken>())
            .Returns(call => { work.Add(call.ArgAt<Func<CancellationToken, Task>>(0)); return ValueTask.CompletedTask; });
        using var queue = new AcquiredExecutionQueue(_manager, handler, scheduler, NullLogger.Instance, license);
        var dispatcher = new TickerQDispatcher(scheduler, handler, license, queue);

        await dispatcher.DispatchAsync([root]);
        await Task.WhenAll(work.Select(w => w(CancellationToken.None))).WaitAsync(TimeSpan.FromSeconds(10));
    }

    private static InternalFunctionContext Job(string name, TickerFunctionDelegate? body = null) => new()
    {
        TickerId = Guid.NewGuid(), FunctionName = name, Type = TickerType.TimeTicker,
        AcquisitionToken = Guid.NewGuid(), ChainGeneration = Guid.NewGuid(),
        ExecutionTime = DateTime.UtcNow, RetryIntervals = [], Status = TickerStatus.InProgress,
        CachedDelegate = body ?? ((_, _, _) => Task.CompletedTask), CachedMaxConcurrency = 0, TimeTickerChildren = []
    };

    private static void AddChild(InternalFunctionContext parent, InternalFunctionContext child)
    {
        child.ParentId = parent.TickerId;
        child.ChainGeneration = parent.ChainGeneration;
        child.RunCondition = RunCondition.OnSuccess;
        parent.TimeTickerChildren.Add(child);
    }
}
