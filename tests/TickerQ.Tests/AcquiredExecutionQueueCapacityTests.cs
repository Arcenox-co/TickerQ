using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using TickerQ.BackgroundServices;
using TickerQ.Provider;
using TickerQ.TickerQThreadPool;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Exceptions;
using TickerQ.Utilities.Infrastructure;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Instrumentation;
using TickerQ.Utilities.Licensing;
using TickerQ.Utilities.Managers;
using TickerQ.Utilities.Models;

namespace TickerQ.Tests;

[Collection("TickerCancellationTokenState")]
public sealed class AcquiredExecutionQueueCapacityTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    public sealed class CancellationTime : TimeTickerEntity<CancellationTime> { }
    public sealed class CancellationCron : CronTickerEntity { }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task QueueStopOrDispose_WakesCapacityWaiterWithoutScannerCancellation(bool dispose)
    {
        var manager = Substitute.For<IInternalTickerManager>();
        var handler = Substitute.For<ITickerExecutionTaskHandler>();
        var scheduler = new CapacityScheduler();
        var context = Context();
        using var queue = new AcquiredExecutionQueue(manager, handler, scheduler, NullLogger.Instance);
        var publish = queue.PublishAsync([context], false, CancellationToken.None, waitForCapacity: true);
        await scheduler.WaitStarted.Task.WaitAsync(Timeout);
        Assert.Contains(Leases(), lease => lease.TickerId == context.TickerId);

        if (dispose) queue.Dispose();
        else await queue.StopAsync(CancellationToken.None);

        Assert.False(await publish.WaitAsync(Timeout));
        await scheduler.WaitFinished.Task.WaitAsync(Timeout);
        await queue.StopAsync(CancellationToken.None);
        Assert.DoesNotContain(Leases(), lease => lease.TickerId == context.TickerId);
        await AssertReleasedExactlyOnceAsync(manager, context);
        await handler.DidNotReceiveWithAnyArgs().ExecuteRegisteredTaskAsync(default!, default, default!, default);
    }

    [Fact]
    public async Task LicenseInvalidation_WhileCapacityNeverChanges_ReleasesBatchAndEndsWaiter()
    {
        var manager = Substitute.For<IInternalTickerManager>();
        var scheduler = new CapacityScheduler();
        var license = LicenseTestState.Active();
        var contexts = new[] { Context(), Context() };
        using var queue = new AcquiredExecutionQueue(manager, Substitute.For<ITickerExecutionTaskHandler>(),
            scheduler, NullLogger.Instance, license);
        var publish = queue.PublishAsync(contexts, false, CancellationToken.None, waitForCapacity: true);
        await scheduler.WaitStarted.Task.WaitAsync(Timeout);
        foreach (var context in contexts)
            Assert.Contains(Leases(), lease => lease.TickerId == context.TickerId &&
                                              lease.AcquisitionToken == context.AcquisitionToken);
        license.Publish(TickerQLicenseState.NotValidated);

        Assert.False(await publish.WaitAsync(Timeout));
        await scheduler.WaitFinished.Task.WaitAsync(Timeout);
        Assert.Equal(1, scheduler.WaitCalls);
        Assert.Null(scheduler.Accepted);
        foreach (var context in contexts)
        {
            await AssertReleasedExactlyOnceAsync(manager, context);
            Assert.DoesNotContain(Leases(), lease => lease.TickerId == context.TickerId);
        }
    }

    [Fact]
    public async Task LicenseInvalidation_AfterAdmissionBeforeExecution_ReleasesPersistedGeneration()
    {
        var manager = Substitute.For<IInternalTickerManager>();
        var handler = Substitute.For<ITickerExecutionTaskHandler>();
        var scheduler = new CapacityScheduler { HasCapacity = true };
        var license = LicenseTestState.Active();
        var context = Context();
        using var queue = new AcquiredExecutionQueue(manager, handler, scheduler, NullLogger.Instance, license);
        Assert.True(await queue.PublishAsync([context], false, CancellationToken.None));
        license.Publish(TickerQLicenseState.NotValidated);

        await scheduler.Accepted!(CancellationToken.None);

        await AssertReleasedExactlyOnceAsync(manager, context);
        await handler.DidNotReceiveWithAnyArgs().ExecuteRegisteredTaskAsync(default!, default, default!, default);
        Assert.DoesNotContain(Leases(), lease => lease.TickerId == context.TickerId);
    }

    [Fact]
    public async Task ChainRetry_RejectedBeforeExecution_IsAbortedInsteadOfReleasedForNormalRecovery()
    {
        var manager = Substitute.For<IInternalTickerManager>();
        var handler = Substitute.For<ITickerExecutionTaskHandler>();
        var scheduler = new CapacityScheduler { HasCapacity = true };
        var license = LicenseTestState.Active();
        var context = Context();
        context.IsChainRetryRoot = true;
        context.IsChainRetryCoordinator = true;
        context.ChainRootId = context.TickerId;
        context.ChainGeneration = context.AcquisitionToken;
        using var queue = new AcquiredExecutionQueue(
            manager, handler, scheduler, NullLogger.Instance, license);
        Assert.True(await queue.PublishAsync([context], false, CancellationToken.None));
        license.Publish(TickerQLicenseState.NotValidated);

        await scheduler.Accepted!(CancellationToken.None);

        await manager.Received(1).AbortTimeTickerChainRetryAsync(context, CancellationToken.None);
        await manager.DidNotReceiveWithAnyArgs().ReleaseAcquiredResources(default!, default);
        await handler.DidNotReceiveWithAnyArgs()
            .ExecuteRegisteredTaskAsync(default!, default, default!, default);
        Assert.DoesNotContain(Leases(), lease => lease.TickerId == context.TickerId);
    }

    [Fact]
    public async Task CompletingPublication_DoesNotCancelTheAcceptedExecutionToken()
    {
        var manager = Substitute.For<IInternalTickerManager>();
        var handler = Substitute.For<ITickerExecutionTaskHandler>();
        var scheduler = new CapacityScheduler();
        var context = Context();
        using var queue = new AcquiredExecutionQueue(manager, handler, scheduler, NullLogger.Instance);
        var publish = queue.PublishAsync([context], false, CancellationToken.None, waitForCapacity: true);
        await scheduler.WaitStarted.Task.WaitAsync(Timeout);
        scheduler.HasCapacity = true;
        scheduler.CapacityAvailable.TrySetResult();
        Assert.True(await publish.WaitAsync(Timeout));
        handler.ExecuteRegisteredTaskAsync(context, false, Arg.Any<CancellationTokenSource>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                Assert.False(call.ArgAt<CancellationToken>(3).IsCancellationRequested);
                return Task.CompletedTask;
            });

        await scheduler.Accepted!(CancellationToken.None);

        await handler.Received(1).ExecuteRegisteredTaskAsync(context, false,
            Arg.Any<CancellationTokenSource>(), Arg.Any<CancellationToken>());
        await manager.DidNotReceiveWithAnyArgs().ReleaseAcquiredResources(default!, default);
        Assert.DoesNotContain(Leases(), lease => lease.TickerId == context.TickerId);
    }

    [Theory]
    [InlineData(TickerType.TimeTicker)]
    [InlineData(TickerType.CronTickerOccurrence)]
    public async Task JobCancellation_WhileCapacityNeverChanges_FinalizesWithoutExecutingOrCreatingCronChain(TickerType type)
    {
        var manager = Substitute.For<IInternalTickerManager>();
        var scheduler = new CapacityScheduler();
        var license = LicenseTestState.Active();
        var clock = Substitute.For<ITickerClock>();
        clock.UtcNow.Returns(new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc));
        await using var services = new ServiceCollection()
            .AddSingleton<ITickerQTaskScheduler>(scheduler).BuildServiceProvider();
        var handler = new TickerExecutionTaskHandler(services, clock,
            Substitute.For<ITickerQInstrumentation>(), manager, new SchedulerOptionsBuilder(),
            Substitute.For<ITickerQFailureNotifier>(), license, new TickerFunctionConcurrencyGate());
        var root = Context();
        root.Type = type;
        root.Status = TickerStatus.InProgress;
        root.CachedDelegate = (_, _, _) => throw new InvalidOperationException("User code must not execute.");
        var cancelled = new List<InternalFunctionContext> { root };
        if (type == TickerType.TimeTicker)
        {
            var child = Context();
            child.RunCondition = RunCondition.OnCancelled;
            child.CachedDelegate = root.CachedDelegate;
            var grandchild = Context();
            grandchild.CachedDelegate = root.CachedDelegate;
            child.TimeTickerChildren.Add(grandchild);
            root.TimeTickerChildren.Add(child);
            cancelled.Add(child);
            cancelled.Add(grandchild);
        }
        else
        {
            root.ParentId = Guid.NewGuid();
            manager.TryDispatchCronChainAsync(root, false, Arg.Any<CancellationToken>()).Returns(true);
        }
        using var queue = new AcquiredExecutionQueue(manager, handler, scheduler, NullLogger.Instance, license);
        var publish = queue.PublishAsync([root], false, CancellationToken.None, waitForCapacity: true);
        await scheduler.WaitStarted.Task.WaitAsync(Timeout);

        Assert.True(TickerCancellationTokenManager.RequestTickerCancellation(
            new TickerExecutionLease(type, root.TickerId, root.AcquisitionToken)));

        Assert.True(await publish.WaitAsync(Timeout));
        await scheduler.WaitFinished.Task.WaitAsync(Timeout);
        Assert.Null(scheduler.Accepted);
        foreach (var context in cancelled)
        {
            Assert.Equal(TickerStatus.Cancelled, context.Status);
            Assert.Equal(root.TickerId, context.ChainRootId);
            Assert.Equal(root.AcquisitionToken, context.ChainGeneration);
            await manager.Received(1).UpdateTickerAsync(context, CancellationToken.None);
        }
        await manager.DidNotReceiveWithAnyArgs().TryDispatchCronChainAsync(default!, default, default);
        await manager.DidNotReceiveWithAnyArgs().ReleaseAcquiredResources(default!, default);
        Assert.DoesNotContain(Leases(), lease => lease.TickerId == root.TickerId);
    }

    [Fact]
    public async Task CancellingOneWaitingJob_ReusesCapacityWaiterForTheNextJob()
    {
        var manager = Substitute.For<IInternalTickerManager>();
        var scheduler = new CapacityScheduler();
        var handler = Substitute.For<ITickerExecutionTaskHandler>();
        var cancelled = Context();
        var next = Context();
        var cancellationHandled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        handler.ExecuteRegisteredTaskAsync(cancelled, false, Arg.Any<CancellationTokenSource>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                Assert.True(call.ArgAt<CancellationToken>(3).IsCancellationRequested);
                cancellationHandled.TrySetResult();
                return Task.CompletedTask;
            });
        using var queue = new AcquiredExecutionQueue(manager, handler, scheduler, NullLogger.Instance);
        var publish = queue.PublishAsync([cancelled, next], false, CancellationToken.None, waitForCapacity: true);
        await scheduler.WaitStarted.Task.WaitAsync(Timeout);
        Assert.True(TickerCancellationTokenManager.RequestTickerCancellation(
            new TickerExecutionLease(cancelled.Type, cancelled.TickerId, cancelled.AcquisitionToken)));
        await cancellationHandled.Task.WaitAsync(Timeout);
        Assert.False(publish.IsCompleted);
        scheduler.HasCapacity = true;
        scheduler.CapacityAvailable.TrySetResult();

        Assert.True(await publish.WaitAsync(Timeout));
        Assert.Equal(1, scheduler.WaitCalls);
        await scheduler.Accepted!(CancellationToken.None);
        await handler.Received(1).ExecuteRegisteredTaskAsync(next, false,
            Arg.Any<CancellationTokenSource>(), Arg.Any<CancellationToken>());
        await manager.DidNotReceiveWithAnyArgs().ReleaseAcquiredResources(default!, default);
    }

    [Fact]
    public async Task CancellingWaitingTree_WithPreviouslyCancelledChild_FinalizesRootAndRemainingChildren()
    {
        var scheduler = new CapacityScheduler();
        var options = new SchedulerOptionsBuilder();
        var clock = Substitute.For<ITickerClock>();
        clock.UtcNow.Returns(new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc));
        await using var services = new ServiceCollection().AddSingleton(clock).AddSingleton(options)
            .AddSingleton<ITickerQTaskScheduler>(scheduler).BuildServiceProvider();
        var provider = new TickerInMemoryPersistenceProvider<CancellationTime, CancellationCron>(services);
        TickerInMemoryPersistenceProvider<CancellationTime, CancellationCron>.ResetAllStateForTests();
        var manager = new InternalTickerManager<CancellationTime, CancellationCron>(provider, clock,
            Substitute.For<ITickerQNotificationHubSender>(), options);
        var leaf = new CancellationTime { Function = "leaf" };
        var cancelledChild = new CancellationTime { Function = "cancelled", Children = [leaf] };
        var sibling = new CancellationTime { Function = "sibling" };
        var root = new CancellationTime
        {
            Function = "root",
            ExecutionTime = clock.UtcNow,
            Children = [sibling, cancelledChild]
        };
        await provider.AddTimeTickers([root]);
        var acquired = Assert.Single(await provider.AcquireImmediateTimeTickersAsync([root.Id]));
        // Match normal acquisition: queued contexts do not carry the persisted child status.
        var context = TimeTickerContextBuilder.Build(acquired, row => new InternalFunctionContext
        {
            TickerId = row.Id, Type = TickerType.TimeTicker, FunctionName = row.Function,
            ParentId = row.ParentId, RuntimePartitionKey = TickerQRuntimePartition.LegacyGlobal.StorageKey,
            ChainRootId = root.Id, ChainGeneration = acquired.ChainGeneration,
            AcquisitionToken = acquired.AcquisitionToken,
            CachedDelegate = (_, _, _) => throw new InvalidOperationException("User code must not execute.")
        });
        context.Status = TickerStatus.InProgress;
        Assert.Equal(2, await provider.CancelPendingTimeTickerAsync(cancelledChild.Id));
        var handler = new TickerExecutionTaskHandler(services, clock,
            Substitute.For<ITickerQInstrumentation>(), manager, options,
            Substitute.For<ITickerQFailureNotifier>(), LicenseTestState.Active(), new TickerFunctionConcurrencyGate());
        using var queue = new AcquiredExecutionQueue(manager, handler, scheduler, NullLogger.Instance);
        var publish = queue.PublishAsync([context], false, CancellationToken.None, waitForCapacity: true);
        try
        {
            await scheduler.WaitStarted.Task.WaitAsync(Timeout);
            Assert.True(TickerCancellationTokenManager.RequestTickerCancellation(
                new TickerExecutionLease(TickerType.TimeTicker, root.Id, acquired.AcquisitionToken)));

            Assert.True(await publish.WaitAsync(Timeout));
            Assert.Null(scheduler.Accepted);
            foreach (var id in new[] { root.Id, sibling.Id, cancelledChild.Id, leaf.Id })
                Assert.Equal(TickerStatus.Cancelled, (await provider.GetTimeTickerById(id)).Status);
            Assert.Null((await provider.GetTimeTickerById(cancelledChild.Id)).ExceptionMessage);
            Assert.Null((await provider.GetTimeTickerById(leaf.Id)).ExceptionMessage);
            Assert.DoesNotContain(Leases(), lease => lease.TickerId == root.Id);
        }
        finally
        {
            await queue.StopAsync(CancellationToken.None);
            await publish.WaitAsync(Timeout);
            TickerInMemoryPersistenceProvider<CancellationTime, CancellationCron>.ResetAllStateForTests();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AlreadyCancelledChild_DoesNotAcknowledgeStaleGenerationOrAcquisition(bool staleAcquisition)
    {
        var options = new SchedulerOptionsBuilder();
        var clock = Substitute.For<ITickerClock>();
        clock.UtcNow.Returns(DateTime.UtcNow);
        await using var services = new ServiceCollection().AddSingleton(clock).AddSingleton(options).BuildServiceProvider();
        var provider = new TickerInMemoryPersistenceProvider<CancellationTime, CancellationCron>(services);
        TickerInMemoryPersistenceProvider<CancellationTime, CancellationCron>.ResetAllStateForTests();
        var manager = new InternalTickerManager<CancellationTime, CancellationCron>(provider, clock,
            Substitute.For<ITickerQNotificationHubSender>(), options);
        var child = new CancellationTime { Function = "child" };
        var root = new CancellationTime { Function = "root", Children = [child] };
        try
        {
            await provider.AddTimeTickers([root]);
            var acquired = Assert.Single(await provider.AcquireImmediateTimeTickersAsync([root.Id]));
            Assert.Equal(1, await provider.CancelPendingTimeTickerAsync(child.Id));
            var stale = new InternalFunctionContext
            {
                TickerId = child.Id, Type = TickerType.TimeTicker, ParentId = root.Id, ChainRootId = root.Id,
                RuntimePartitionKey = TickerQRuntimePartition.LegacyGlobal.StorageKey,
                ChainGeneration = staleAcquisition ? acquired.ChainGeneration : Guid.NewGuid(),
                AcquisitionToken = staleAcquisition ? Guid.NewGuid() : acquired.AcquisitionToken
            };
            stale.SetProperty(x => x.Status, TickerStatus.Cancelled);

            await Assert.ThrowsAsync<TickerResultNotAcknowledgedException>(() =>
                manager.UpdateTickerAsync(stale, CancellationToken.None));
            var persistedRoot = await provider.GetTimeTickerById(root.Id);
            Assert.Equal(TickerStatus.InProgress, persistedRoot.Status);
            Assert.Equal(acquired.AcquisitionToken, persistedRoot.AcquisitionToken);
            Assert.Equal(TickerStatus.Cancelled, (await provider.GetTimeTickerById(child.Id)).Status);
        }
        finally
        {
            TickerInMemoryPersistenceProvider<CancellationTime, CancellationCron>.ResetAllStateForTests();
        }
    }

    private static InternalFunctionContext Context() => new()
    {
        TickerId = Guid.NewGuid(),
        AcquisitionToken = Guid.NewGuid(),
        Type = TickerType.TimeTicker,
        FunctionName = "CapacityRegression"
    };

    private static AcquisitionLease[] Leases()
    {
        var times = new List<AcquisitionLease>();
        var crons = new List<AcquisitionLease>();
        TickerCancellationTokenManager.SnapshotRunningForLeaseRenewal(times, crons);
        return times.Concat(crons).ToArray();
    }

    private static Task AssertReleasedExactlyOnceAsync(IInternalTickerManager manager, InternalFunctionContext context)
        => manager.Received(1).ReleaseAcquiredResources(
            Arg.Is<InternalFunctionContext[]>(rows => rows.Length == 1 && rows[0] == context &&
                                                      rows[0].AcquisitionToken == context.AcquisitionToken),
            CancellationToken.None);

    // The separate signals make ownership transitions deterministic; no worker timing or
    // sleeps are used to decide when the queue is waiting or when a callback may start.
    private sealed class CapacityScheduler : ITickerQTaskScheduler, ITickerQExecutionScheduler
    {
        internal TaskCompletionSource WaitStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource WaitFinished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource CapacityAvailable { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool HasCapacity { get; set; }
        internal int WaitCalls { get; private set; }
        internal Func<CancellationToken, Task>? Accepted { get; private set; }

        public async Task WaitForOrchestrationCapacityAsync(CancellationToken cancellationToken = default)
        {
            WaitCalls++;
            WaitStarted.TrySetResult();
            try { if (!HasCapacity) await CapacityAvailable.Task.WaitAsync(cancellationToken); }
            finally { WaitFinished.TrySetResult(); }
        }

        public bool TryQueueOrchestration(Func<CancellationToken, Task> work, CancellationToken admissionToken = default)
        {
            if (!HasCapacity || IsFrozen || admissionToken.IsCancellationRequested) return false;
            Accepted = work;
            return true;
        }

        public Task ExecuteAsync(Func<CancellationToken, Task> work, TickerTaskPriority priority, CancellationToken token = default)
            => work(token);
        public ValueTask QueueAsync(Func<CancellationToken, Task> work, TickerTaskPriority priority, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("The execution scheduler path must be used.");
        public void Freeze() { IsFrozen = true; CapacityAvailable.TrySetResult(); }
        public void Resume() => IsFrozen = false;
        public bool IsFrozen { get; private set; }
        public int ActiveWorkers => 0;
        public int ActiveOrchestrationCount => 0;
        public int TotalQueuedTasks => 0;
        public bool IsDisposed => false;
        public string GetDiagnostics() => nameof(CapacityScheduler);
        public Task<bool> WaitForRunningTasksAsync(TimeSpan? timeout = null) => Task.FromResult(true);
        public IDisposable BeginFunctionWait() => throw new NotSupportedException();
        public IDisposable BeginRetryDelay() => throw new NotSupportedException();
    }
}
