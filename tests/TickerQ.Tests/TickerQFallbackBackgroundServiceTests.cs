using System;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using NSubstitute.ReceivedExtensions;
using TickerQ.BackgroundServices;
using TickerQ.Utilities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Models;
using Xunit;

namespace TickerQ.Tests;

[Collection("TickerFunctionProviderState")]
public class TickerQFallbackBackgroundServiceTests
{
    private readonly IInternalTickerManager _internalManager;
    private readonly ITickerExecutionTaskHandler _taskHandler;
    private readonly ITickerQTaskScheduler _taskScheduler;
    private readonly SchedulerOptionsBuilder _schedulerOptions;

    public TickerQFallbackBackgroundServiceTests()
    {
        _internalManager = Substitute.For<IInternalTickerManager>();
        _taskHandler = Substitute.For<ITickerExecutionTaskHandler>();
        _taskScheduler = Substitute.For<ITickerQTaskScheduler>();
        _taskScheduler.IsFrozen.Returns(false);
        _taskScheduler.IsDisposed.Returns(false);

        _schedulerOptions = new SchedulerOptionsBuilder
        {
            FallbackIntervalChecker = TimeSpan.FromMilliseconds(50)
        };

        // Ensure TickerFunctionProvider.TickerFunctions is initialized
        // so the service doesn't get a NullReferenceException
        TickerFunctionProvider.Build();
    }

    private TickerQFallbackBackgroundService CreateService()
    {
        return new TickerQFallbackBackgroundService(
            _internalManager,
            _schedulerOptions,
            _taskHandler,
            _taskScheduler);
    }

    [Fact]
    public async Task NoTimedOutTickers_ServiceDelaysByFallbackPeriod_AndCancelsCleanly()
    {
        _internalManager.RunTimedOutTickers(Arg.Any<CancellationToken>())
            .Returns(Array.Empty<InternalFunctionContext>());

        var service = CreateService();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        await service.StartAsync(cts.Token);

        // Wait for the service to run a bit and then cancel
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(350), cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected
        }

        await service.StopAsync(CancellationToken.None);

        // Verify RunTimedOutTickers was called at least once
        await _internalManager.Received(Quantity.AtLeastOne()).RunTimedOutTickers(Arg.Any<CancellationToken>());

        // Verify QueueAsync was never called since there are no timed-out tickers
        await _taskScheduler.DidNotReceive().QueueAsync(
            Arg.Any<Func<CancellationToken, Task>>(),
            Arg.Any<TickerTaskPriority>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TimedOutTickersFound_QueuedToScheduler()
    {
        var function1 = new InternalFunctionContext
        {
            FunctionName = "TestFunc1",
            TickerId = Guid.NewGuid(),
            CachedPriority = TickerTaskPriority.Normal
        };

        var function2 = new InternalFunctionContext
        {
            FunctionName = "TestFunc2",
            TickerId = Guid.NewGuid(),
            CachedPriority = TickerTaskPriority.High
        };

        var callCount = 0;
        _internalManager.RunTimedOutTickers(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                // Return functions on first call, empty on subsequent calls
                if (Interlocked.Increment(ref callCount) == 1)
                    return new[] { function1, function2 };
                return Array.Empty<InternalFunctionContext>();
            });

        var service = CreateService();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        await service.StartAsync(cts.Token);

        // Wait for service to process
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(550), cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected
        }

        await service.StopAsync(CancellationToken.None);

        // Verify QueueAsync was called for each function
        await _taskScheduler.Received(2).QueueAsync(
            Arg.Any<Func<CancellationToken, Task>>(),
            Arg.Any<TickerTaskPriority>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SchedulerFrozen_SkipsQueueing()
    {
        _taskScheduler.IsFrozen.Returns(true);

        _internalManager.RunTimedOutTickers(Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                new InternalFunctionContext
                {
                    FunctionName = "TestFunc",
                    TickerId = Guid.NewGuid(),
                    CachedPriority = TickerTaskPriority.Normal
                }
            });

        var service = CreateService();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        await service.StartAsync(cts.Token);

        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(350), cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected
        }

        await service.StopAsync(CancellationToken.None);

        // RunTimedOutTickers should NOT be called because the service skips when frozen
        await _internalManager.DidNotReceive().RunTimedOutTickers(Arg.Any<CancellationToken>());

        // QueueAsync should NOT be called
        await _taskScheduler.DidNotReceive().QueueAsync(
            Arg.Any<Func<CancellationToken, Task>>(),
            Arg.Any<TickerTaskPriority>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ServiceStopsCleanly_NoExceptionsThrown()
    {
        _internalManager.RunTimedOutTickers(Arg.Any<CancellationToken>())
            .Returns(Array.Empty<InternalFunctionContext>());

        var service = CreateService();
        using var cts = new CancellationTokenSource();

        await service.StartAsync(cts.Token);

        // Let the service run briefly
        await Task.Delay(TimeSpan.FromMilliseconds(100));

        // Cancel and stop -- should not throw
        cts.Cancel();

        var exception = await Record.ExceptionAsync(() => service.StopAsync(CancellationToken.None));
        Assert.Null(exception);
    }
}
