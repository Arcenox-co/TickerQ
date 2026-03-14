using System;
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

public class TickerQSchedulerBackgroundServiceTests
{
    [Fact]
    public async Task RunScheduler_UsesMinPollingInterval_WhenTimeRemainingIsZero()
    {
        var executionContext = new TickerExecutionContext();
        var internalManager = Substitute.For<IInternalTickerManager>();
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

        await taskHandler.Received(1).ExecuteTaskAsync(
            Arg.Any<InternalFunctionContext>(), false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunScheduler_WithMaxConcurrency_ReleasesSemaphoreOnException()
    {
        var executionContext = new TickerExecutionContext();
        var internalManager = Substitute.For<IInternalTickerManager>();
        var taskHandler = Substitute.For<ITickerExecutionTaskHandler>();
        var taskScheduler = Substitute.For<ITickerQTaskScheduler>();
        var gate = new TickerFunctionConcurrencyGate();

        taskHandler.ExecuteTaskAsync(
            Arg.Any<InternalFunctionContext>(),
            Arg.Any<bool>(),
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

        await taskHandler.Received(1).ExecuteTaskAsync(
            Arg.Any<InternalFunctionContext>(), false, Arg.Any<CancellationToken>());
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
