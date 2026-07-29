using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using TickerQ.BackgroundServices;
using TickerQ.Utilities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Models;

namespace TickerQ.Tests;

[Collection("TickerCancellationTokenState")]
public class TickerQSchedulerBackgroundServiceTests : IDisposable
{
    public void Dispose()
    {
        TickerCancellationTokenManager.CleanUpTickerCancellationTokens();
    }

    [Fact]
    public async Task RunScheduler_UsesMinPollingInterval_WhenTimeRemainingIsZero()
    {
        var executionContext = new TickerExecutionContext();
        var internalManager = Substitute.For<IInternalTickerManager>();
        internalManager.SetTickersInProgress(
                Arg.Any<InternalFunctionContext[]>(), Arg.Any<CancellationToken>())
            .Returns(call => call.ArgAt<InternalFunctionContext[]>(0));
        internalManager.GetNextTickers(Arg.Any<CancellationToken>())
            .Returns((TimeSpan.Zero, Array.Empty<InternalFunctionContext>()));

        var schedulerOptions = new SchedulerOptionsBuilder
        {
            MinPollingInterval = TimeSpan.FromMilliseconds(200)
        };

        var taskHandler = Substitute.For<ITickerExecutionTaskHandler>();
        var taskScheduler = Substitute.For<ITickerQTaskScheduler>();

        var service = new TickerQSchedulerBackgroundService(
            executionContext,
            taskHandler,
            taskScheduler,
            internalManager,
            schedulerOptions,
            new TickerFunctionConcurrencyGate());

        using var cts = new CancellationTokenSource();
        var before = DateTime.UtcNow;

        var runTask = InvokeRunSchedulerAsync(service, CancellationToken.None, cts.Token);

        var next = await WaitForNextOccurrenceAsync(executionContext, TimeSpan.FromSeconds(2));
        Assert.NotNull(next);
        Assert.True(next!.Value - before >= schedulerOptions.MinPollingInterval);

        cts.Cancel();
        try
        {
            await runTask;
        }
        catch (OperationCanceledException)
        {
            // Expected when cancelling the scheduler loop.
        }
    }

    private static Task InvokeRunSchedulerAsync(
        TickerQSchedulerBackgroundService service,
        CancellationToken stoppingToken,
        CancellationToken cancellationToken)
    {
        var method = typeof(TickerQSchedulerBackgroundService)
            .GetMethod("RunTickerQSchedulerAsync", BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(method);
        var task = method!.Invoke(service, new object[] { stoppingToken, cancellationToken }) as Task;
        Assert.NotNull(task);

        return task!;
    }

    [Fact]
    public async Task RunScheduler_WithMaxConcurrency_AcquiresAndReleasesSemaphore()
    {
        var executionContext = new TickerExecutionContext();
        var internalManager = Substitute.For<IInternalTickerManager>();
        internalManager.SetTickersInProgress(
                Arg.Any<InternalFunctionContext[]>(), Arg.Any<CancellationToken>())
            .Returns(call => call.ArgAt<InternalFunctionContext[]>(0));
        var taskHandler = Substitute.For<ITickerExecutionTaskHandler>();
        var taskScheduler = Substitute.For<ITickerQTaskScheduler>();
        var gate = new TickerFunctionConcurrencyGate();

        var function = new InternalFunctionContext
        {
            TickerId = Guid.NewGuid(),
            FunctionName = "GatedFunc",
            CachedPriority = TickerTaskPriority.Normal,
            CachedMaxConcurrency = 1,
            TimeTickerChildren = []
        };

        // First call: return functions to execute, then infinite wait
        var callCount = 0;
        internalManager.GetNextTickers(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                callCount++;
                if (callCount == 1)
                    return (TimeSpan.Zero, new[] { function });
                return (Timeout.InfiniteTimeSpan, Array.Empty<InternalFunctionContext>());
            });

        // Pre-set functions so the scheduler finds them on first iteration
        TickerFunctionProvider.Build();
        executionContext.SetFunctions([function]);

        Func<CancellationToken, Task>? capturedWork = null;
        taskScheduler.QueueAsync(
            Arg.Any<Func<CancellationToken, Task>>(),
            Arg.Any<TickerTaskPriority>(),
            Arg.Any<CancellationToken>())
            .ReturnsForAnyArgs(ci =>
            {
                capturedWork = ci.ArgAt<Func<CancellationToken, Task>>(0);
                return ValueTask.CompletedTask;
            });

        var schedulerOptions = new SchedulerOptionsBuilder
        {
            MinPollingInterval = TimeSpan.FromMilliseconds(50)
        };

        var service = new TickerQSchedulerBackgroundService(
            executionContext, taskHandler, taskScheduler,
            internalManager, schedulerOptions, gate);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        try
        {
            await InvokeRunSchedulerAsync(service, CancellationToken.None, cts.Token);
        }
        catch (OperationCanceledException) { }

        Assert.NotNull(capturedWork);

        // Verify semaphore was created for this function
        var semaphore = gate.GetSemaphoreOrNull("GatedFunc", 1);
        Assert.NotNull(semaphore);
        Assert.Equal(1, semaphore!.CurrentCount);

        // Execute captured work — should acquire and release semaphore
        await capturedWork!(CancellationToken.None);
        Assert.Equal(1, semaphore.CurrentCount);

        await taskHandler.Received(1).ExecuteRegisteredTaskAsync(
            Arg.Any<InternalFunctionContext>(), false, Arg.Any<CancellationTokenSource>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunScheduler_WithMaxConcurrency_ReleasesSemaphoreOnException()
    {
        var executionContext = new TickerExecutionContext();
        var internalManager = Substitute.For<IInternalTickerManager>();
        internalManager.SetTickersInProgress(
                Arg.Any<InternalFunctionContext[]>(), Arg.Any<CancellationToken>())
            .Returns(call => call.ArgAt<InternalFunctionContext[]>(0));
        var taskHandler = Substitute.For<ITickerExecutionTaskHandler>();
        var taskScheduler = Substitute.For<ITickerQTaskScheduler>();
        var gate = new TickerFunctionConcurrencyGate();

        taskHandler.ExecuteRegisteredTaskAsync(
            Arg.Any<InternalFunctionContext>(),
            Arg.Any<bool>(),
            Arg.Any<CancellationTokenSource>(),
            Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("boom"));

        var function = new InternalFunctionContext
        {
            TickerId = Guid.NewGuid(),
            FunctionName = "FailFunc",
            CachedPriority = TickerTaskPriority.Normal,
            CachedMaxConcurrency = 2,
            TimeTickerChildren = []
        };

        TickerFunctionProvider.Build();
        executionContext.SetFunctions([function]);

        internalManager.GetNextTickers(Arg.Any<CancellationToken>())
            .Returns((Timeout.InfiniteTimeSpan, Array.Empty<InternalFunctionContext>()));

        Func<CancellationToken, Task>? capturedWork = null;
        taskScheduler.QueueAsync(
            Arg.Any<Func<CancellationToken, Task>>(),
            Arg.Any<TickerTaskPriority>(),
            Arg.Any<CancellationToken>())
            .ReturnsForAnyArgs(ci =>
            {
                capturedWork = ci.ArgAt<Func<CancellationToken, Task>>(0);
                return ValueTask.CompletedTask;
            });

        var service = new TickerQSchedulerBackgroundService(
            executionContext, taskHandler, taskScheduler,
            internalManager, new SchedulerOptionsBuilder(), gate);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        try
        {
            await InvokeRunSchedulerAsync(service, CancellationToken.None, cts.Token);
        }
        catch (OperationCanceledException) { }

        Assert.NotNull(capturedWork);

        var semaphore = gate.GetSemaphoreOrNull("FailFunc", 2);
        Assert.Equal(2, semaphore!.CurrentCount);

        // Work should throw, but semaphore must still be released
        await Assert.ThrowsAsync<InvalidOperationException>(() => capturedWork!(CancellationToken.None));
        Assert.Equal(2, semaphore.CurrentCount);
    }

    [Fact]
    public async Task RunScheduler_ZeroMaxConcurrency_NoSemaphoreCreated()
    {
        var executionContext = new TickerExecutionContext();
        var internalManager = Substitute.For<IInternalTickerManager>();
        internalManager.SetTickersInProgress(
                Arg.Any<InternalFunctionContext[]>(), Arg.Any<CancellationToken>())
            .Returns(call => call.ArgAt<InternalFunctionContext[]>(0));
        var taskHandler = Substitute.For<ITickerExecutionTaskHandler>();
        var taskScheduler = Substitute.For<ITickerQTaskScheduler>();
        var gate = new TickerFunctionConcurrencyGate();

        var function = new InternalFunctionContext
        {
            TickerId = Guid.NewGuid(),
            FunctionName = "UnlimitedFunc",
            CachedPriority = TickerTaskPriority.Normal,
            CachedMaxConcurrency = 0,
            TimeTickerChildren = []
        };

        TickerFunctionProvider.Build();
        executionContext.SetFunctions([function]);

        internalManager.GetNextTickers(Arg.Any<CancellationToken>())
            .Returns((Timeout.InfiniteTimeSpan, Array.Empty<InternalFunctionContext>()));

        Func<CancellationToken, Task>? capturedWork = null;
        taskScheduler.QueueAsync(
            Arg.Any<Func<CancellationToken, Task>>(),
            Arg.Any<TickerTaskPriority>(),
            Arg.Any<CancellationToken>())
            .ReturnsForAnyArgs(ci =>
            {
                capturedWork = ci.ArgAt<Func<CancellationToken, Task>>(0);
                return ValueTask.CompletedTask;
            });

        var service = new TickerQSchedulerBackgroundService(
            executionContext, taskHandler, taskScheduler,
            internalManager, new SchedulerOptionsBuilder(), gate);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        try
        {
            await InvokeRunSchedulerAsync(service, CancellationToken.None, cts.Token);
        }
        catch (OperationCanceledException) { }

        Assert.NotNull(capturedWork);
        await capturedWork!(CancellationToken.None);

        // No semaphore should exist for maxConcurrency=0
        Assert.Null(gate.GetSemaphoreOrNull("UnlimitedFunc", 0));

        await taskHandler.Received(1).ExecuteRegisteredTaskAsync(
            Arg.Any<InternalFunctionContext>(), false, Arg.Any<CancellationTokenSource>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void AcquiredButNotYetExecuted_RootAndCron_AppearInLeaseRenewalSnapshot()
    {
        var (service, _) = BuildCapturingService(out _);

        var timeRoot = new InternalFunctionContext
        {
            TickerId = Guid.NewGuid(),
            FunctionName = "TimeRoot",
            Type = TickerType.TimeTicker,
            CachedPriority = TickerTaskPriority.Normal,
            TimeTickerChildren = []
        };
        var cronOcc = new InternalFunctionContext
        {
            TickerId = Guid.NewGuid(),
            FunctionName = "CronOcc",
            Type = TickerType.CronTickerOccurrence,
            CachedPriority = TickerTaskPriority.Normal,
            TimeTickerChildren = []
        };

        // Register + queue, but the capturing scheduler never runs the delegate — so both stay in the
        // "acquired, queued, not yet executed" state that lease renewal must keep alive.
        InvokeQueueAcquired(service, timeRoot, CancellationToken.None);
        InvokeQueueAcquired(service, cronOcc, CancellationToken.None);

        var timeIds = new List<Guid>();
        var cronIds = new List<Guid>();
        TickerCancellationTokenManager.SnapshotRunningForLeaseRenewal(timeIds, cronIds);

        Assert.Contains(timeRoot.TickerId, timeIds);
        Assert.Contains(cronOcc.TickerId, cronIds);
    }

    [Fact]
    public async Task PublicationFailure_AfterExecutionBegins_DoesNotUnregisterRunningWork()
    {
        var executionContext = new TickerExecutionContext();
        var internalManager = Substitute.For<IInternalTickerManager>();
        internalManager.SetTickersInProgress(
                Arg.Any<InternalFunctionContext[]>(), Arg.Any<CancellationToken>())
            .Returns(call => call.ArgAt<InternalFunctionContext[]>(0));
        var taskHandler = Substitute.For<ITickerExecutionTaskHandler>();
        var taskScheduler = Substitute.For<ITickerQTaskScheduler>();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? executionTask = null;

        taskHandler.ExecuteRegisteredTaskAsync(
                Arg.Any<InternalFunctionContext>(), Arg.Any<bool>(),
                Arg.Any<CancellationTokenSource>(), Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                started.TrySetResult();
                await release.Task;
            });

        taskScheduler.QueueAsync(
                Arg.Any<Func<CancellationToken, Task>>(),
                Arg.Any<TickerTaskPriority>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                executionTask = ci.ArgAt<Func<CancellationToken, Task>>(0)(CancellationToken.None);
                return new ValueTask(Task.FromException(new InvalidOperationException("publication failed")));
            });

        var service = new TickerQSchedulerBackgroundService(
            executionContext, taskHandler, taskScheduler, internalManager,
            new SchedulerOptionsBuilder(), new TickerFunctionConcurrencyGate());
        var function = new InternalFunctionContext
        {
            TickerId = Guid.NewGuid(),
            FunctionName = "PublicationRaceFunc",
            Type = TickerType.TimeTicker,
            CachedPriority = TickerTaskPriority.Normal,
            TimeTickerChildren = []
        };

        try
        {
            InvokeQueueAcquired(service, function, CancellationToken.None);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(3));

            Assert.NotNull(executionTask);
            Assert.False(executionTask!.IsCompleted);
            Assert.Equal(1, TickerCancellationTokenManager.ActiveCount);
            Assert.True(TickerCancellationTokenManager.RequestTickerCancellationById(function.TickerId));
        }
        finally
        {
            release.TrySetResult();
            if (executionTask != null)
                await executionTask;
        }

        Assert.Equal(0, TickerCancellationTokenManager.ActiveCount);
    }

    [Fact]
    public async Task StoppingWhileQueued_UnregistersBeforeDelegateCanBeDropped()
    {
        var (service, taskHandler) = BuildCapturingService(out var capturedWorkRef);
        var function = new InternalFunctionContext
        {
            TickerId = Guid.NewGuid(),
            FunctionName = "StoppingQueuedFunc",
            Type = TickerType.TimeTicker,
            CachedPriority = TickerTaskPriority.Normal,
            TimeTickerChildren = []
        };
        using var stopping = new CancellationTokenSource();

        InvokeQueueAcquired(service, function, stopping.Token);
        Assert.NotNull(capturedWorkRef());
        Assert.Equal(1, TickerCancellationTokenManager.ActiveCount);

        stopping.Cancel();

        Assert.Equal(0, TickerCancellationTokenManager.ActiveCount);
        await taskHandler.DidNotReceiveWithAnyArgs()
            .ExecuteRegisteredTaskAsync(default!, default, default!, default);
    }

    [Fact]
    public async Task IdentityCancellation_InterruptsBlockedSemaphoreWait_WithoutCallingHandler_AndUnregistersOnce()
    {
        var (service, taskHandler) = BuildCapturingService(out var capturedWorkRef, gate: out var gate);

        var function = new InternalFunctionContext
        {
            TickerId = Guid.NewGuid(),
            FunctionName = "BlockedFunc",
            Type = TickerType.CronTickerOccurrence,
            CachedPriority = TickerTaskPriority.Normal,
            CachedMaxConcurrency = 1,
            TimeTickerChildren = []
        };

        // Exhaust the single permit so the queued delegate blocks on WaitAsync.
        var semaphore = gate.GetSemaphoreOrNull("BlockedFunc", 1);
        Assert.NotNull(semaphore);
        await semaphore!.WaitAsync();

        InvokeQueueAcquired(service, function, CancellationToken.None);
        var capturedWork = capturedWorkRef();
        Assert.NotNull(capturedWork);

        // Registration is visible while the delegate is queued/blocked on the semaphore.
        Assert.Equal(1, TickerCancellationTokenManager.ActiveCount);

        var workTask = capturedWork!(CancellationToken.None);
        Assert.False(workTask.IsCompleted); // genuinely blocked on the exhausted semaphore

        // Identity cancellation signals the shared registered source, interrupting the wait.
        Assert.True(TickerCancellationTokenManager.RequestTickerCancellationById(function.TickerId));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => workTask);

        // Handler was never invoked — the wait was interrupted before execution began.
        await taskHandler.DidNotReceiveWithAnyArgs()
            .ExecuteRegisteredTaskAsync(default!, default, default!, default);

        // Unregistered exactly once by the delegate's finally; the permit we hold was never taken.
        Assert.Equal(0, TickerCancellationTokenManager.ActiveCount);
        Assert.False(TickerCancellationTokenManager.RemoveTickerCancellationToken(function.TickerId));
        Assert.Equal(0, semaphore.CurrentCount);
    }

    [Fact]
    public async Task BlockedOnSemaphore_RemainsVisibleAcrossTwoLeaseRenewals_ThenUnregistersOnCancel()
    {
        var (service, _) = BuildCapturingService(out var capturedWorkRef, gate: out var gate);

        var function = new InternalFunctionContext
        {
            TickerId = Guid.NewGuid(),
            FunctionName = "TwoRenewalFunc",
            Type = TickerType.CronTickerOccurrence,
            CachedPriority = TickerTaskPriority.Normal,
            CachedMaxConcurrency = 1,
            TimeTickerChildren = []
        };

        var semaphore = gate.GetSemaphoreOrNull("TwoRenewalFunc", 1);
        Assert.NotNull(semaphore);
        await semaphore!.WaitAsync(); // exhaust so the delegate blocks

        InvokeQueueAcquired(service, function, CancellationToken.None);
        var capturedWork = capturedWorkRef();
        Assert.NotNull(capturedWork);

        var workTask = capturedWork!(CancellationToken.None);
        Assert.False(workTask.IsCompleted);

        // Two back-to-back lease-renewal snapshots (no sleeps) both see the still-blocked occurrence.
        for (var renewal = 0; renewal < 2; renewal++)
        {
            var timeIds = new List<Guid>();
            var cronIds = new List<Guid>();
            TickerCancellationTokenManager.SnapshotRunningForLeaseRenewal(timeIds, cronIds);
            Assert.Contains(function.TickerId, cronIds);
        }

        // Cancel unblocks the wait and the delegate's finally unregisters exactly once.
        Assert.True(TickerCancellationTokenManager.RequestTickerCancellationById(function.TickerId));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => workTask);
        Assert.Equal(0, TickerCancellationTokenManager.ActiveCount);
    }

    private static (TickerQSchedulerBackgroundService Service, ITickerExecutionTaskHandler Handler) BuildCapturingService(
        out Func<Func<CancellationToken, Task>?> capturedWork)
        => BuildCapturingService(out capturedWork, out _);

    private static (TickerQSchedulerBackgroundService Service, ITickerExecutionTaskHandler Handler) BuildCapturingService(
        out Func<Func<CancellationToken, Task>?> capturedWork, out TickerFunctionConcurrencyGate gate)
    {
        var executionContext = new TickerExecutionContext();
        var internalManager = Substitute.For<IInternalTickerManager>();
        internalManager.SetTickersInProgress(
                Arg.Any<InternalFunctionContext[]>(), Arg.Any<CancellationToken>())
            .Returns(call => call.ArgAt<InternalFunctionContext[]>(0));
        var taskHandler = Substitute.For<ITickerExecutionTaskHandler>();
        var taskScheduler = Substitute.For<ITickerQTaskScheduler>();
        var localGate = new TickerFunctionConcurrencyGate();

        Func<CancellationToken, Task>? captured = null;
        taskScheduler.QueueAsync(
            Arg.Any<Func<CancellationToken, Task>>(),
            Arg.Any<TickerTaskPriority>(),
            Arg.Any<CancellationToken>())
            .ReturnsForAnyArgs(ci =>
            {
                captured = ci.ArgAt<Func<CancellationToken, Task>>(0);
                return ValueTask.CompletedTask;
            });

        var service = new TickerQSchedulerBackgroundService(
            executionContext, taskHandler, taskScheduler,
            internalManager, new SchedulerOptionsBuilder(), localGate);

        gate = localGate;
        capturedWork = () => captured;
        return (service, taskHandler);
    }

    private static void InvokeQueueAcquired(
        TickerQSchedulerBackgroundService service, InternalFunctionContext function, CancellationToken stoppingToken)
    {
        var method = typeof(TickerQSchedulerBackgroundService)
            .GetMethod("QueueAcquiredExecution", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(service, new object[] { function, stoppingToken });
    }

    private static async Task<DateTime?> WaitForNextOccurrenceAsync(
        TickerExecutionContext executionContext,
        TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            var next = executionContext.GetNextPlannedOccurrence();
            if (next.HasValue)
                return next;

            await Task.Delay(5);
        }

        return null;
    }
}
