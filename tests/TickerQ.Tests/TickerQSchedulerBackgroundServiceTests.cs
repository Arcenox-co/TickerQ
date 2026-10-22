using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TickerQ.Utilities.Instrumentation;
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

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 1)]
    public void RestartDecision_IsLoggedOnlyWhenDetailedLoggingIsEnabled(bool enabled, int expectedLogs)
    {
        var logger = new RecordingSchedulerLogger();
        using var service = new TickerQSchedulerBackgroundService(
            new TickerExecutionContext(),
            Substitute.For<ITickerExecutionTaskHandler>(),
            Substitute.For<ITickerQTaskScheduler>(),
            Substitute.For<IInternalTickerManager>(),
            new SchedulerOptionsBuilder { DetailedLoggingEnabled = enabled },
            LicenseTestState.Active(), logger);

        service.RestartIfNeeded(DateTime.UtcNow);

        Assert.Equal(expectedLogs, logger.Entries.Count);
        if (enabled)
            Assert.Contains("restart requested", logger.Entries[0].Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(0, 200)]
    [InlineData(50, 200)]
    [InlineData(600000, 600000)]
    public async Task RunScheduler_WaitsForNextOccurrence_WithMinimumFloor(int dueInMilliseconds, int expectedWaitMilliseconds)
    {
        var executionContext = new TickerExecutionContext();
        var internalManager = Substitute.For<IInternalTickerManager>();
        internalManager.SetTickersInProgress(
                Arg.Any<InternalFunctionContext[]>(), Arg.Any<CancellationToken>())
            .Returns(call => call.ArgAt<InternalFunctionContext[]>(0));
        internalManager.GetNextTickers(Arg.Any<CancellationToken>())
            .Returns((TimeSpan.FromMilliseconds(dueInMilliseconds), Array.Empty<InternalFunctionContext>()));

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
            schedulerOptions, LicenseTestState.Active());

        using var cts = new CancellationTokenSource();
        var before = DateTime.UtcNow;

        var runTask = InvokeRunSchedulerAsync(service, CancellationToken.None, cts.Token);

        var next = await WaitForNextOccurrenceAsync(executionContext, TimeSpan.FromSeconds(2));
        Assert.NotNull(next);
        Assert.InRange(next!.Value, before.AddMilliseconds(expectedWaitMilliseconds),
            DateTime.UtcNow.AddMilliseconds(expectedWaitMilliseconds));

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
    public async Task RunScheduler_DueBatches_DrainWithoutPollingDelay_AndWaitForQueueCapacity()
    {
        var executionContext = new TickerExecutionContext();
        var manager = Substitute.For<IInternalTickerManager>();
        var handler = Substitute.For<ITickerExecutionTaskHandler>();
        var scheduler = Substitute.For<ITickerQTaskScheduler>();
        var first = new InternalFunctionContext { TickerId = Guid.NewGuid(), FunctionName = "First", TimeTickerChildren = [] };
        var second = new InternalFunctionContext { TickerId = Guid.NewGuid(), FunctionName = "Second", TimeTickerChildren = [] };
        var blockedAdmission = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var enteredAdmission = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var drained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads = 0;
        manager.GetNextTickers(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            switch (Interlocked.Increment(ref reads))
            {
                case 1: return (TimeSpan.Zero, new[] { first });
                case 2: return (TimeSpan.Zero, new[] { second });
                default:
                    drained.TrySetResult();
                    return (Timeout.InfiniteTimeSpan, Array.Empty<InternalFunctionContext>());
            }
        });
        manager.SetTickersInProgress(Arg.Any<InternalFunctionContext[]>(), Arg.Any<CancellationToken>())
            .Returns(call => call.ArgAt<InternalFunctionContext[]>(0));
        var admissions = 0;
        scheduler.QueueAsync(Arg.Any<Func<CancellationToken, Task>>(),
                Arg.Any<TickerTaskPriority>(), Arg.Any<CancellationToken>())
            .Returns(call => new ValueTask(AdmitAsync(call.ArgAt<Func<CancellationToken, Task>>(0))));

        async Task AdmitAsync(Func<CancellationToken, Task> work)
        {
            if (Interlocked.Increment(ref admissions) == 1)
            {
                enteredAdmission.TrySetResult();
                await blockedAdmission.Task;
            }
            await work(CancellationToken.None);
        }

        using var service = new TickerQSchedulerBackgroundService(executionContext, handler, scheduler,
            manager, new SchedulerOptionsBuilder { MinPollingInterval = TimeSpan.FromMinutes(1) },
            LicenseTestState.Active());
        using var cts = new CancellationTokenSource();
        var run = InvokeRunSchedulerAsync(service, CancellationToken.None, cts.Token);
        try
        {
            await enteredAdmission.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, Volatile.Read(ref reads)); // No more acquisition while admission is blocked.
            blockedAdmission.TrySetResult();
            await drained.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(2, admissions);
            await handler.Received(1).ExecuteRegisteredTaskAsync(first, false,
                Arg.Any<CancellationTokenSource>(), Arg.Any<CancellationToken>());
            await handler.Received(1).ExecuteRegisteredTaskAsync(second, false,
                Arg.Any<CancellationTokenSource>(), Arg.Any<CancellationToken>());
        }
        finally
        {
            blockedAdmission.TrySetResult();
            cts.Cancel();
            try { await run; } catch (OperationCanceledException) { }
        }
    }

    [Fact]
    public async Task RunScheduler_PassesConcurrencyMetadataToRegisteredHandler()
    {
        var executionContext = new TickerExecutionContext();
        var internalManager = Substitute.For<IInternalTickerManager>();
        internalManager.SetTickersInProgress(
                Arg.Any<InternalFunctionContext[]>(), Arg.Any<CancellationToken>())
            .Returns(call => call.ArgAt<InternalFunctionContext[]>(0));
        var taskHandler = Substitute.For<ITickerExecutionTaskHandler>();
        var taskScheduler = Substitute.For<ITickerQTaskScheduler>();

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
            internalManager, schedulerOptions, LicenseTestState.Active());

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        try
        {
            await InvokeRunSchedulerAsync(service, CancellationToken.None, cts.Token);
        }
        catch (OperationCanceledException) { }

        Assert.NotNull(capturedWork);

        await capturedWork!(CancellationToken.None);
        Assert.Equal(0, TickerCancellationTokenManager.ActiveCount);

        await taskHandler.Received(1).ExecuteRegisteredTaskAsync(
            Arg.Is<InternalFunctionContext>(c => c == function && c.CachedMaxConcurrency == 1), false, Arg.Any<CancellationTokenSource>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunScheduler_HandlerFailure_UnregistersExecution()
    {
        var executionContext = new TickerExecutionContext();
        var internalManager = Substitute.For<IInternalTickerManager>();
        internalManager.SetTickersInProgress(
                Arg.Any<InternalFunctionContext[]>(), Arg.Any<CancellationToken>())
            .Returns(call => call.ArgAt<InternalFunctionContext[]>(0));
        var taskHandler = Substitute.For<ITickerExecutionTaskHandler>();
        var taskScheduler = Substitute.For<ITickerQTaskScheduler>();

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
            internalManager, new SchedulerOptionsBuilder(), LicenseTestState.Active());

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        try
        {
            await InvokeRunSchedulerAsync(service, CancellationToken.None, cts.Token);
        }
        catch (OperationCanceledException) { }

        Assert.NotNull(capturedWork);

        await Assert.ThrowsAsync<InvalidOperationException>(() => capturedWork!(CancellationToken.None));
        Assert.Equal(0, TickerCancellationTokenManager.ActiveCount);
    }

    [Fact]
    public async Task RunScheduler_ZeroMaxConcurrency_DispatchesToHandler()
    {
        var executionContext = new TickerExecutionContext();
        var internalManager = Substitute.For<IInternalTickerManager>();
        internalManager.SetTickersInProgress(
                Arg.Any<InternalFunctionContext[]>(), Arg.Any<CancellationToken>())
            .Returns(call => call.ArgAt<InternalFunctionContext[]>(0));
        var taskHandler = Substitute.For<ITickerExecutionTaskHandler>();
        var taskScheduler = Substitute.For<ITickerQTaskScheduler>();

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
            internalManager, new SchedulerOptionsBuilder(), LicenseTestState.Active());

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        try
        {
            await InvokeRunSchedulerAsync(service, CancellationToken.None, cts.Token);
        }
        catch (OperationCanceledException) { }

        Assert.NotNull(capturedWork);
        await capturedWork!(CancellationToken.None);

        await taskHandler.Received(1).ExecuteRegisteredTaskAsync(
            Arg.Is<InternalFunctionContext>(c => c == function && c.CachedMaxConcurrency == 0),
            false, Arg.Any<CancellationTokenSource>(), Arg.Any<CancellationToken>());

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
            new SchedulerOptionsBuilder(), LicenseTestState.Active());
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
    public async Task IdentityCancellation_InterruptsBlockedFunctionWait_WithoutInvokingDelegate_AndUnregistersOnce()
    {
        var (service, _) = BuildCapturingService(out var capturedWorkRef, gate: out var gate);
        var delegateCalled = false;

        var function = new InternalFunctionContext
        {
            TickerId = Guid.NewGuid(),
            FunctionName = "BlockedFunc",
            CachedDelegate = (_, _, _) => { delegateCalled = true; return Task.CompletedTask; },
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

        await workTask.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(TickerStatus.Cancelled, function.Status);

        // The real handler owns the wait and records cancellation without invoking user code.
        Assert.False(delegateCalled);

        // Unregistered exactly once by the delegate's finally; the permit we hold was never taken.
        Assert.Equal(0, TickerCancellationTokenManager.ActiveCount);
        Assert.False(TickerCancellationTokenManager.RemoveTickerCancellationToken(function.TickerId));
        Assert.Equal(0, semaphore.CurrentCount);
        semaphore.Release();
    }

    [Fact]
    public async Task BlockedOnSemaphore_RemainsVisibleAcrossTwoLeaseRenewals_ThenUnregistersOnCancel()
    {
        var (service, _) = BuildCapturingService(out var capturedWorkRef, gate: out var gate);

        var function = new InternalFunctionContext
        {
            TickerId = Guid.NewGuid(),
            FunctionName = "TwoRenewalFunc",
            CachedDelegate = (_, _, _) => Task.CompletedTask,
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
        await workTask.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(TickerStatus.Cancelled, function.Status);
        Assert.Equal(0, TickerCancellationTokenManager.ActiveCount);
        Assert.Equal(0, semaphore.CurrentCount);
        semaphore.Release();
    }

    private static (TickerQSchedulerBackgroundService Service, ITickerExecutionTaskHandler Handler) BuildCapturingService(
        out Func<Func<CancellationToken, Task>?> capturedWork)
        => BuildCapturingService(out capturedWork, out _, useRealHandler: false);

    private static (TickerQSchedulerBackgroundService Service, ITickerExecutionTaskHandler Handler) BuildCapturingService(
        out Func<Func<CancellationToken, Task>?> capturedWork, out TickerFunctionConcurrencyGate gate,
        bool useRealHandler = true)
    {
        var executionContext = new TickerExecutionContext();
        var internalManager = Substitute.For<IInternalTickerManager>();
        internalManager.SetTickersInProgress(
                Arg.Any<InternalFunctionContext[]>(), Arg.Any<CancellationToken>())
            .Returns(call => call.ArgAt<InternalFunctionContext[]>(0));
        var taskHandler = Substitute.For<ITickerExecutionTaskHandler>();
        var taskScheduler = Substitute.For<ITickerQTaskScheduler>();
        var localGate = new TickerFunctionConcurrencyGate();
        if (useRealHandler)
        {
            var clock = Substitute.For<ITickerClock>();
            clock.UtcNow.Returns(DateTime.UtcNow);
            taskHandler = new TickerExecutionTaskHandler(
                new ServiceCollection().BuildServiceProvider(), clock,
                Substitute.For<ITickerQInstrumentation>(), internalManager, new SchedulerOptionsBuilder(),
                Substitute.For<ITickerQFailureNotifier>(), LicenseTestState.Active(clock), localGate);
        }


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
            internalManager, new SchedulerOptionsBuilder(), LicenseTestState.Active());

        gate = localGate;
        capturedWork = () => captured;
        return (service, taskHandler);
    }

    private static void InvokeQueueAcquired(
        TickerQSchedulerBackgroundService service, InternalFunctionContext function, CancellationToken stoppingToken)
    {
        var queue = (AcquiredExecutionQueue)typeof(TickerQSchedulerBackgroundService)
            .GetField("_acquiredQueue", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(service)!;
        _ = queue.PublishAsync([function], false, stoppingToken);
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

    private sealed class RecordingSchedulerLogger : ILogger<TickerQSchedulerBackgroundService>
    {
        internal List<(LogLevel Level, string Message)> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }
}
