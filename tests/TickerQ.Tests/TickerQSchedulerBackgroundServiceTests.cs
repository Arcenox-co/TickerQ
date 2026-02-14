using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using TickerQ.BackgroundServices;
using TickerQ.Utilities;
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
            schedulerOptions);

        using var cts = new CancellationTokenSource();
        var before = DateTime.UtcNow;

        var runTask = InvokeRunSchedulerAsync(service, CancellationToken.None, cts.Token);

        var next = await WaitForNextOccurrenceAsync(executionContext, TimeSpan.FromSeconds(2));
        next.Should().NotBeNull();
        (next!.Value - before).Should().BeGreaterThanOrEqualTo(schedulerOptions.MinPollingInterval);

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

        method.Should().NotBeNull();
        var task = method!.Invoke(service, new object[] { stoppingToken, cancellationToken }) as Task;
        task.Should().NotBeNull();

        return task!;
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
