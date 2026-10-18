using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using NSubstitute.ReceivedExtensions;
using Microsoft.Extensions.Logging;
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

    private TickerQFallbackBackgroundService CreateService(ILogger<TickerQFallbackBackgroundService>? logger = null)
    {
        return new TickerQFallbackBackgroundService(
            _internalManager,
            _schedulerOptions,
            _taskHandler,
            _taskScheduler,
            new TickerFunctionConcurrencyGate(),
            LicenseTestState.Active(), logger: logger);
    }

    [Fact]
    public async Task RecoveryFailure_IsLogged_AndNextScanStillRuns()
    {
        var failure = new InvalidOperationException("recovery store unavailable");
        var logger = new RecordingLogger();
        var retried = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        _internalManager.RunTimedOutTickers(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            if (Interlocked.Increment(ref calls) == 1)
                return Task.FromException<InternalFunctionContext[]>(failure);
            retried.TrySetResult();
            return Task.FromResult(Array.Empty<InternalFunctionContext>());
        });
        using var service = CreateService(logger);
        try
        {
            await service.StartAsync(CancellationToken.None);
            await retried.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
        var log = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, log.Level);
        Assert.Same(failure, log.Exception);
        Assert.Contains("Retrying", log.Message);
        Assert.True(service.ExecuteTask!.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task ShutdownDuringFailureDelay_CompletesWithoutFaultingService()
    {
        var failed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _internalManager.RunTimedOutTickers(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            failed.TrySetResult();
            return Task.FromException<InternalFunctionContext[]>(new InvalidOperationException("store unavailable"));
        });
        _schedulerOptions.FallbackIntervalChecker = TimeSpan.FromMinutes(1);
        using var service = CreateService();
        await service.StartAsync(CancellationToken.None);
        await failed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await service.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(service.ExecuteTask!.IsCompletedSuccessfully);
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

    private sealed class RecordingLogger : ILogger<TickerQFallbackBackgroundService>
    {
        public ConcurrentQueue<(LogLevel Level, Exception? Exception, string Message)> Entries { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Entries.Enqueue((logLevel, exception, formatter(state, exception)));
    }
}
