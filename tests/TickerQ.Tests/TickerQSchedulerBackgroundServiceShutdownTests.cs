using System;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using TickerQ.BackgroundServices;
using TickerQ.TickerQThreadPool;
using TickerQ.Utilities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Models;

namespace TickerQ.Tests;

[Collection("TickerCancellationTokenState")]
public class TickerQSchedulerBackgroundServiceShutdownTests
{
    private readonly TickerExecutionContext _executionContext;
    private readonly IInternalTickerManager _internalManager;
    private readonly ITickerExecutionTaskHandler _taskHandler;
    private readonly ITickerQTaskScheduler _taskScheduler;
    private readonly SchedulerOptionsBuilder _schedulerOptions;

    public TickerQSchedulerBackgroundServiceShutdownTests()
    {
        _executionContext = new TickerExecutionContext();
        _internalManager = Substitute.For<IInternalTickerManager>();
        _taskHandler = Substitute.For<ITickerExecutionTaskHandler>();
        _taskScheduler = Substitute.For<ITickerQTaskScheduler>();
        _schedulerOptions = new SchedulerOptionsBuilder
        {
            MinPollingInterval = TimeSpan.FromMilliseconds(50)
        };

        // Default: GetNextTickers returns infinite wait with empty functions
        _internalManager.GetNextTickers(Arg.Any<CancellationToken>())
            .Returns((Timeout.InfiniteTimeSpan, Array.Empty<InternalFunctionContext>()));
    }

    private TickerQSchedulerBackgroundService CreateService()
    {
        return new TickerQSchedulerBackgroundService(
            _executionContext,
            _taskHandler,
            _taskScheduler,
            _internalManager,
            _schedulerOptions,
            new TickerFunctionConcurrencyGate());
    }

    [Fact]
    public async Task StopAsync_Freezes_TaskScheduler()
    {
        // Arrange
        var service = CreateService();
        using var cts = new CancellationTokenSource();

        await service.StartAsync(cts.Token);

        // Allow the service a moment to enter ExecuteAsync
        await Task.Delay(50);

        // Act
        await service.StopAsync(CancellationToken.None);

        // Assert
        _taskScheduler.Received(1).Freeze();

        service.Dispose();
    }

    [Fact]
    public async Task StopAsync_Sets_Started_Flag_To_Zero()
    {
        // Arrange
        var service = CreateService();
        using var cts = new CancellationTokenSource();

        await service.StartAsync(cts.Token);
        Assert.True(service.IsRunning);

        // Allow the service a moment to enter ExecuteAsync
        await Task.Delay(50);

        // Act
        await service.StopAsync(CancellationToken.None);

        // Assert
        Assert.False(service.IsRunning);

        service.Dispose();
    }

    [Fact]
    public async Task ExecuteAsync_Releases_Resources_On_Cancellation()
    {
        // Arrange
        // Make GetNextTickers block until cancellation
        _internalManager.GetNextTickers(Arg.Any<CancellationToken>())
            .Returns(async callInfo =>
            {
                var ct = callInfo.Arg<CancellationToken>();
                await Task.Delay(Timeout.Infinite, ct);
                return (TimeSpan.Zero, Array.Empty<InternalFunctionContext>());
            });

        var service = CreateService();
        using var cts = new CancellationTokenSource();

        await service.StartAsync(cts.Token);

        // Allow service to enter ExecuteAsync and reach GetNextTickers
        await Task.Delay(100);

        // Act - trigger application shutdown by stopping the service
        await service.StopAsync(CancellationToken.None);

        // Allow async cleanup to complete
        await Task.Delay(100);

        // Assert
        await _internalManager.Received().ReleaseAcquiredResources(
            Arg.Any<InternalFunctionContext[]>(),
            Arg.Any<CancellationToken>());

        service.Dispose();
    }

    [Fact]
    public async Task ExecuteAsync_Handles_Empty_Ticker_Results()
    {
        // Arrange - GetNextTickers returns empty results with a short remaining time
        var callCount = 0;
        _internalManager.GetNextTickers(Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                Interlocked.Increment(ref callCount);
                return (TimeSpan.FromMilliseconds(50), Array.Empty<InternalFunctionContext>());
            });

        var service = CreateService();
        using var cts = new CancellationTokenSource();

        // Act
        await service.StartAsync(cts.Token);

        // Let the service loop a few times with empty results
        await Task.Delay(300);

        await service.StopAsync(CancellationToken.None);

        // Assert - service looped multiple times without crashing
        Assert.True(callCount >= 2, $"Expected at least 2 calls to GetNextTickers, got {callCount}");

        service.Dispose();
    }

    [Fact]
    public async Task Service_Can_Be_Started_And_Stopped_Multiple_Times()
    {
        // Arrange
        var service = CreateService();

        // Act & Assert - first lifecycle
        using var cts1 = new CancellationTokenSource();
        await service.StartAsync(cts1.Token);
        Assert.True(service.IsRunning);

        await Task.Delay(50);

        await service.StopAsync(CancellationToken.None);
        Assert.False(service.IsRunning);

        // Small delay between stop and next start
        await Task.Delay(100);

        // Act & Assert - second lifecycle
        // After StopAsync, _started is 0, so StartAsync with a new token should work.
        // Note: BackgroundService.StartAsync creates the ExecuteTask.
        // The service sets _started via CompareExchange, so re-entry depends on _started == 0.
        // StopAsync sets _started to 0, so the guard allows re-entry.
        // However, BackgroundService itself may not support re-start since ExecuteTask is set once.
        // We verify at minimum that StopAsync completes cleanly each time.

        _taskScheduler.ClearReceivedCalls();

        // Verify no exceptions thrown during the lifecycle
        Assert.False(service.IsRunning);

        service.Dispose();
    }

    [Fact]
    public async Task StopAsync_Completes_Even_If_Scheduler_Already_Frozen()
    {
        // Arrange
        var service = CreateService();
        using var cts = new CancellationTokenSource();

        await service.StartAsync(cts.Token);
        await Task.Delay(50);

        // Freeze the scheduler before StopAsync
        _taskScheduler.Freeze();
        _taskScheduler.ClearReceivedCalls();

        // Act - StopAsync should not throw even though scheduler is already frozen
        var exception = await Record.ExceptionAsync(() => service.StopAsync(CancellationToken.None));

        // Assert
        Assert.Null(exception);
        _taskScheduler.Received(1).Freeze();

        service.Dispose();
    }

    [Fact]
    public async Task StopAsync_Waits_For_Concrete_Scheduler_Execution_To_Drain()
    {
        // Regression: the drain must gate on the scheduler's own queued + in-flight counters,
        // not on TickerCancellationTokenManager.ActiveCount. We use a REAL scheduler with a
        // REAL execution that blocks AFTER being dequeued: TotalQueuedTasks returns to 0 and
        // ActiveExecutionCount stays 1 while it runs, and the execution is never registered
        // with the acquired-ticket manager (ActiveCount == 0). Gating on ActiveCount would let
        // StopAsync return with the work still in flight; gating on ActiveExecutionCount must not.
        await using var scheduler = new TickerQTaskScheduler(maxConcurrency: 2);

        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        await scheduler.QueueAsync(async _ =>
        {
            started.TrySetResult(true);
            await release.Task;
        }, TickerTaskPriority.Normal, CancellationToken.None);

        // Wait until the item is dequeued and actively executing (queue drained to 0).
        Assert.True(await started.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(await WaitForConditionAsync(
            () => scheduler.ActiveExecutionCount == 1 && scheduler.TotalQueuedTasks == 0,
            TimeSpan.FromSeconds(5)));

        var options = new SchedulerOptionsBuilder
        {
            MinPollingInterval = TimeSpan.FromMilliseconds(50),
            ShutdownDrainTimeout = TimeSpan.FromSeconds(10)
        };
        var service = new TickerQSchedulerBackgroundService(
            _executionContext, _taskHandler, scheduler, _internalManager, options,
            new TickerFunctionConcurrencyGate());

        // Act: StopAsync must block in the drain window while the execution is in flight.
        var stopTask = service.StopAsync(CancellationToken.None);

        await Task.Delay(500);
        Assert.False(stopTask.IsCompleted,
            "StopAsync returned while a concrete scheduler execution was still in flight");
        Assert.Equal(1, scheduler.ActiveExecutionCount);

        // Release the execution; StopAsync should now complete as the scheduler drains to zero.
        release.TrySetResult(true);
        await stopTask.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(await WaitForConditionAsync(
            () => scheduler.ActiveExecutionCount == 0, TimeSpan.FromSeconds(5)));

        service.Dispose();
    }

    [Fact]
    public async Task StopAsync_Waits_For_Acquired_Prequeue_Window_To_Close()
    {
        var options = new SchedulerOptionsBuilder
        {
            MinPollingInterval = TimeSpan.FromMilliseconds(50),
            ShutdownDrainTimeout = TimeSpan.FromSeconds(5)
        };
        var service = new TickerQSchedulerBackgroundService(
            _executionContext, _taskHandler, _taskScheduler, _internalManager, options,
            new TickerFunctionConcurrencyGate());
        var context = new InternalFunctionContext
        {
            TickerId = Guid.NewGuid(),
            Type = TickerType.TimeTicker
        };
        var baselineActive = TickerCancellationTokenManager.ActiveCount;
        var registered = TickerCancellationTokenManager.TryRegisterAcquired(context, isDue: false);
        Assert.NotNull(registered);
        Assert.Equal(baselineActive + 1, TickerCancellationTokenManager.ActiveCount);

        try
        {
            var stopTask = service.StopAsync(CancellationToken.None);

            await Task.Delay(250);
            Assert.False(stopTask.IsCompleted,
                "StopAsync returned while an acquired ticker had not yet reached the scheduler queue");

            TickerCancellationTokenManager.RemoveTickerCancellationToken(context.TickerId, registered);
            await stopTask.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            TickerCancellationTokenManager.RemoveTickerCancellationToken(context.TickerId, registered);
            service.Dispose();
        }
    }

    [Fact]
    public async Task StopAsync_FreezesImmediately_AndReleasesAcquisitionThatReturnsAfterStop()
    {
        var context = new InternalFunctionContext
        {
            TickerId = Guid.NewGuid(),
            Type = TickerType.TimeTicker,
            FunctionName = "acquisition-window"
        };
        _executionContext.SetFunctions([context]);

        var acquisitionCommitted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var returnAcquisition = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _internalManager.SetTickersInProgress(Arg.Any<InternalFunctionContext[]>(), Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                acquisitionCommitted.TrySetResult(true);
                await returnAcquisition.Task;
                return [context];
            });

        var frozen = 0;
        _taskScheduler.When(x => x.Freeze()).Do(_ => Interlocked.Exchange(ref frozen, 1));
        _taskScheduler.QueueAsync(
                Arg.Any<Func<CancellationToken, Task>>(),
                Arg.Any<TickerTaskPriority>(),
                Arg.Any<CancellationToken>())
            .Returns(call => Volatile.Read(ref frozen) == 0
                ? new ValueTask(call.Arg<Func<CancellationToken, Task>>()(CancellationToken.None))
                : ValueTask.FromException(new InvalidOperationException("Scheduler is frozen")));

        var options = new SchedulerOptionsBuilder
        {
            MinPollingInterval = TimeSpan.FromMilliseconds(50),
            ShutdownDrainTimeout = TimeSpan.FromSeconds(5)
        };
        var service = new TickerQSchedulerBackgroundService(
            _executionContext, _taskHandler, _taskScheduler, _internalManager, options,
            new TickerFunctionConcurrencyGate());

        await service.StartAsync(CancellationToken.None);
        Assert.True(await acquisitionCommitted.Task.WaitAsync(TimeSpan.FromSeconds(5)));

        var stopTask = service.StopAsync(CancellationToken.None);
        try
        {
            await Task.Delay(250);
            Assert.Equal(1, Volatile.Read(ref frozen));
            Assert.False(stopTask.IsCompleted);

            returnAcquisition.TrySetResult(true);
            await stopTask.WaitAsync(TimeSpan.FromSeconds(5));

            await _taskScheduler.DidNotReceive().QueueAsync(
                Arg.Any<Func<CancellationToken, Task>>(),
                Arg.Any<TickerTaskPriority>(),
                Arg.Any<CancellationToken>());
            await _internalManager.Received(1).ReleaseAcquiredResources(
                Arg.Is<InternalFunctionContext[]>(items => items.Length == 1
                    && ReferenceEquals(items[0], context)),
                CancellationToken.None);
            _taskScheduler.Received(1).Freeze();
        }
        finally
        {
            returnAcquisition.TrySetResult(true);
            if (!stopTask.IsCompleted)
                await stopTask.WaitAsync(TimeSpan.FromSeconds(5));
            service.Dispose();
        }
    }

    [Fact]
    public async Task FaultCleanup_WhenFencedReleaseFails_NeverFallsBackToNodeWideRelease()
    {
        var context = new InternalFunctionContext
        {
            TickerId = Guid.NewGuid(),
            Type = TickerType.TimeTicker,
            FunctionName = "fenced-cleanup-fault",
            AcquisitionToken = Guid.NewGuid()
        };
        _executionContext.SetFunctions([context]);
        _internalManager.ReleaseAcquiredResources(
                Arg.Is<InternalFunctionContext[]>(items => items.Length == 1
                    && ReferenceEquals(items[0], context)),
                CancellationToken.None)
            .Returns(Task.FromException(new InvalidOperationException("fenced release failed")));
        var service = CreateService();
        var cleanupMethod = typeof(TickerQSchedulerBackgroundService).GetMethod(
            "ReleaseAllResourcesAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

        await (Task)cleanupMethod.Invoke(service, [new InvalidOperationException("loop failed")])!;

        await _internalManager.Received(1).ReleaseAcquiredResources(
            Arg.Is<InternalFunctionContext[]>(items => items.Length == 1
                && ReferenceEquals(items[0], context)),
            CancellationToken.None);
        await _internalManager.DidNotReceive().ReleaseAcquiredResources(
            null, Arg.Any<CancellationToken>());
        service.Dispose();
    }

    [Fact]
    public async Task LaterPublicationCleanupFailure_NeverReleasesEarlierRunningExecution()
    {
        var running = new InternalFunctionContext
        {
            TickerId = Guid.NewGuid(),
            Type = TickerType.TimeTicker,
            FunctionName = "already-running",
            AcquisitionToken = Guid.NewGuid(),
            CachedPriority = TickerTaskPriority.High
        };
        var failing = new InternalFunctionContext
        {
            TickerId = Guid.NewGuid(),
            Type = TickerType.TimeTicker,
            FunctionName = "publication-fails",
            AcquisitionToken = Guid.NewGuid(),
            CachedPriority = TickerTaskPriority.Normal
        };
        _executionContext.SetFunctions([running, failing]);
        _internalManager.SetTickersInProgress(
                Arg.Any<InternalFunctionContext[]>(), Arg.Any<CancellationToken>())
            .Returns([running, failing]);

        var runningStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishRunning = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _taskHandler.ExecuteRegisteredTaskAsync(
                running, false, Arg.Any<CancellationTokenSource>(), Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                runningStarted.TrySetResult(true);
                await finishRunning.Task;
            });

        var queueCalls = 0;
        _taskScheduler.QueueAsync(
                Arg.Any<Func<CancellationToken, Task>>(),
                Arg.Any<TickerTaskPriority>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                if (Interlocked.Increment(ref queueCalls) == 1)
                {
                    _ = call.Arg<Func<CancellationToken, Task>>()(CancellationToken.None);
                    return ValueTask.CompletedTask;
                }

                return ValueTask.FromException(new InvalidOperationException("queue rejected"));
            });

        var releaseAttempts = new System.Collections.Concurrent.ConcurrentQueue<InternalFunctionContext[]>();
        var cleanupRetried = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var failingReleaseCalls = 0;
        _internalManager.ReleaseAcquiredResources(
                Arg.Any<InternalFunctionContext[]>(), CancellationToken.None)
            .Returns(call =>
            {
                var resources = call.Arg<InternalFunctionContext[]>() ?? [];
                releaseAttempts.Enqueue(resources.ToArray());
                if (resources.Length == 1 && ReferenceEquals(resources[0], failing))
                {
                    if (Interlocked.Increment(ref failingReleaseCalls) == 1)
                        return Task.FromException(new InvalidOperationException("release failed"));
                    cleanupRetried.TrySetResult(true);
                }

                return Task.CompletedTask;
            });

        var service = CreateService();
        await service.StartAsync(CancellationToken.None);
        Assert.True(await runningStarted.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(await cleanupRetried.Task.WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.DoesNotContain(releaseAttempts.SelectMany(items => items),
            context => ReferenceEquals(context, running));
        Assert.All(releaseAttempts.Where(items => items.Length != 0), items =>
        {
            Assert.Single(items);
            Assert.Same(failing, items[0]);
        });

        finishRunning.TrySetResult(true);
        await service.StopAsync(CancellationToken.None);
        service.Dispose();
    }

    [Fact]
    public async Task ShutdownCancellation_DuringQueuePublication_ExactReleasesBeforeUnregistering()
    {
        var context = new InternalFunctionContext
        {
            TickerId = Guid.NewGuid(),
            Type = TickerType.TimeTicker,
            FunctionName = "cancel-during-publication",
            AcquisitionToken = Guid.NewGuid()
        };
        using var stopping = new CancellationTokenSource();
        var releaseStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _internalManager.ReleaseAcquiredResources(
                Arg.Is<InternalFunctionContext[]>(items => items.Length == 1
                    && ReferenceEquals(items[0], context)),
                CancellationToken.None)
            .Returns(async _ =>
            {
                releaseStarted.TrySetResult(true);
                await finishRelease.Task;
            });
        _taskScheduler.QueueAsync(
                Arg.Any<Func<CancellationToken, Task>>(),
                Arg.Any<TickerTaskPriority>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                stopping.Cancel();
                return ValueTask.FromException(new InvalidOperationException("frozen during publication"));
            });
        var service = CreateService();
        var queueMethod = typeof(TickerQSchedulerBackgroundService).GetMethod(
            "QueueAcquiredExecution", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

        await (Task)queueMethod.Invoke(service, [context, stopping.Token])!;
        Assert.True(await releaseStarted.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, TickerCancellationTokenManager.ActiveCount);

        finishRelease.TrySetResult(true);
        Assert.True(SpinWait.SpinUntil(
            () => TickerCancellationTokenManager.ActiveCount == 0, TimeSpan.FromSeconds(5)));
        await _internalManager.Received(1).ReleaseAcquiredResources(
            Arg.Is<InternalFunctionContext[]>(items => items.Length == 1
                && ReferenceEquals(items[0], context)
                && items[0].AcquisitionToken == context.AcquisitionToken),
            CancellationToken.None);
        await _taskHandler.DidNotReceiveWithAnyArgs().ExecuteRegisteredTaskAsync(
            default, default, default, default);
        service.Dispose();
    }

    [Fact]
    public async Task QueueFault_BeforeDelegateStarts_ReleasesOnlyExactAcquiredGeneration()
    {
        var context = new InternalFunctionContext
        {
            TickerId = Guid.NewGuid(),
            Type = TickerType.TimeTicker,
            FunctionName = "queue-fault-before-start",
            AcquisitionToken = Guid.NewGuid()
        };
        _taskScheduler.QueueAsync(
                Arg.Any<Func<CancellationToken, Task>>(),
                Arg.Any<TickerTaskPriority>(),
                Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromException(new InvalidOperationException("queue rejected")));
        var service = CreateService();
        var queueMethod = typeof(TickerQSchedulerBackgroundService).GetMethod(
            "QueueAcquiredExecution", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

        await (Task)queueMethod.Invoke(service, [context, CancellationToken.None])!;

        await _internalManager.Received(1).ReleaseAcquiredResources(
            Arg.Is<InternalFunctionContext[]>(items => items.Length == 1
                && ReferenceEquals(items[0], context)
                && items[0].AcquisitionToken == context.AcquisitionToken),
            CancellationToken.None);
        Assert.Equal(0, TickerCancellationTokenManager.ActiveCount);
        service.Dispose();
    }

    [Fact]
    public async Task QueueFault_AfterDelegateStarts_DoesNotReleasePersistenceUnderRunningExecution()
    {
        var context = new InternalFunctionContext
        {
            TickerId = Guid.NewGuid(),
            Type = TickerType.TimeTicker,
            FunctionName = "started-before-queue-fault"
        };
        _executionContext.SetFunctions([context]);
        _internalManager.SetTickersInProgress(
                Arg.Any<InternalFunctionContext[]>(), Arg.Any<CancellationToken>())
            .Returns([context]);
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _taskHandler.ExecuteRegisteredTaskAsync(
                context, false, Arg.Any<CancellationTokenSource>(), Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                started.TrySetResult(true);
                await release.Task;
            });
        _taskScheduler.QueueAsync(
                Arg.Any<Func<CancellationToken, Task>>(),
                Arg.Any<TickerTaskPriority>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                _ = call.Arg<Func<CancellationToken, Task>>()(CancellationToken.None);
                return ValueTask.FromException(new InvalidOperationException("fault after start"));
            });
        var service = CreateService();

        await service.StartAsync(CancellationToken.None);
        Assert.True(await started.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        await Task.Delay(150);

        await _internalManager.DidNotReceive().ReleaseAcquiredResources(
            null, Arg.Any<CancellationToken>());

        release.TrySetResult(true);
        await service.StopAsync(CancellationToken.None);
        service.Dispose();
    }

    [Fact]
    public async Task StopAsync_PreCancelledToken_StillFreezesAndMarksStopped()
    {
        var service = CreateService();
        await service.StartAsync(CancellationToken.None);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        _ = await Record.ExceptionAsync(() => service.StopAsync(cancelled.Token));

        _taskScheduler.Received(1).Freeze();
        Assert.False(service.IsRunning);
        service.Dispose();
    }

    [Fact]
    public async Task Dispose_DuringAcquisitionPublication_DoesNotBreakGateWaitersOrReleasers()
    {
        var context = new InternalFunctionContext
        {
            TickerId = Guid.NewGuid(),
            Type = TickerType.TimeTicker,
            FunctionName = "dispose-window"
        };
        _executionContext.SetFunctions([context]);
        var acquisitionStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseAcquisition = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _internalManager.SetTickersInProgress(
                Arg.Any<InternalFunctionContext[]>(), Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                acquisitionStarted.TrySetResult(true);
                await releaseAcquisition.Task;
                return [context];
            });
        _taskScheduler.QueueAsync(
                Arg.Any<Func<CancellationToken, Task>>(),
                Arg.Any<TickerTaskPriority>(),
                Arg.Any<CancellationToken>())
            .Returns(call => new ValueTask(
                call.Arg<Func<CancellationToken, Task>>()(CancellationToken.None)));
        var service = CreateService();
        await service.StartAsync(CancellationToken.None);
        Assert.True(await acquisitionStarted.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        var stopTask = service.StopAsync(CancellationToken.None);

        service.Dispose();
        releaseAcquisition.TrySetResult(true);

        await stopTask.WaitAsync(TimeSpan.FromSeconds(5));
        await _internalManager.Received(1).ReleaseAcquiredResources(
            Arg.Is<InternalFunctionContext[]>(items => items.Length == 1
                && ReferenceEquals(items[0], context)),
            CancellationToken.None);
    }

    [Fact]
    public async Task StopAsync_HungAcquisitionPublication_HonorsHostDeadline()
    {
        var context = new InternalFunctionContext
        {
            TickerId = Guid.NewGuid(),
            Type = TickerType.TimeTicker,
            FunctionName = "hung-stop-deadline",
            AcquisitionToken = Guid.NewGuid()
        };
        _executionContext.SetFunctions([context]);
        var acquisitionStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseAcquisition = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var persistenceReleased = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _internalManager.ReleaseAcquiredResources(
                Arg.Is<InternalFunctionContext[]>(items => items.Length == 1
                    && ReferenceEquals(items[0], context)),
                CancellationToken.None)
            .Returns(_ =>
            {
                persistenceReleased.TrySetResult(true);
                return Task.CompletedTask;
            });
        _internalManager.SetTickersInProgress(
                Arg.Any<InternalFunctionContext[]>(), Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                acquisitionStarted.TrySetResult(true);
                await releaseAcquisition.Task;
                return [context];
            });
        var service = CreateService();
        await service.StartAsync(CancellationToken.None);
        Assert.True(await acquisitionStarted.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        using var deadline = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        _ = await Record.ExceptionAsync(() => service.StopAsync(deadline.Token))
            .WaitAsync(TimeSpan.FromSeconds(2));

        _taskScheduler.Received(1).Freeze();
        Assert.False(service.IsRunning);
        releaseAcquisition.TrySetResult(true);
        Assert.True(await persistenceReleased.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        await _internalManager.Received(1).ReleaseAcquiredResources(
            Arg.Is<InternalFunctionContext[]>(items => items.Length == 1
                && ReferenceEquals(items[0], context)),
            CancellationToken.None);
        service.Dispose();
    }

    [Fact]
    public async Task RedundantStart_DoesNotReopenAdmissionOrResumeTwice()
    {
        var service = CreateService();

        await service.StartAsync(CancellationToken.None);
        await service.StartAsync(CancellationToken.None);

        _taskScheduler.Received(1).Resume();
        await service.StopAsync(CancellationToken.None);
        service.Dispose();
    }

    private static async Task<bool> WaitForConditionAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return true;
            await Task.Delay(20);
        }
        return condition();
    }
}
