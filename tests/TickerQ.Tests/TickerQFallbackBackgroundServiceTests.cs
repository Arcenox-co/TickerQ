using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TickerQ.Utilities.Infrastructure;
using TickerQ.Utilities.Base;
using NSubstitute;
using NSubstitute.ReceivedExtensions;
using TickerQ.BackgroundServices;
using TickerQ.TickerQThreadPool;
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
            LicenseTestState.Active(), logger: logger);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SaturatedAdmission_WaitsWithoutReacquiringReleasedRows_AndWakesOnCapacityOrShutdown(bool stopWhileFull)
    {
        await using var scheduler = new TickerQTaskScheduler(1, 1, 1);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Assert.True(((ITickerQExecutionScheduler)scheduler).TryQueueOrchestration(_ => finish.Task));
        var context = new InternalFunctionContext
        {
            TickerId = Guid.NewGuid(), FunctionName = "SaturationTest", Type = TickerType.TimeTicker,
            AcquisitionToken = Guid.NewGuid()
        };
        var scans = 0;
        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resumed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _internalManager.RunTimedOutTickers(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            if (Interlocked.Increment(ref scans) == 1) return Task.FromResult(new[] { context });
            resumed.TrySetResult();
            return Task.FromResult(Array.Empty<InternalFunctionContext>());
        });
        _internalManager.ReleaseAcquiredResources(Arg.Any<InternalFunctionContext[]>(), Arg.Any<CancellationToken>())
            .Returns(_ => { released.TrySetResult(); return Task.CompletedTask; });
        using var service = new TickerQFallbackBackgroundService(_internalManager,
            new SchedulerOptionsBuilder { FallbackIntervalChecker = TimeSpan.FromMilliseconds(20) },
            _taskHandler, scheduler, LicenseTestState.Active());
        try
        {
            await service.StartAsync(CancellationToken.None);
            await released.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await Task.Delay(150);
            Assert.Equal(1, Volatile.Read(ref scans));
            if (stopWhileFull)
                await service.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(3));
            else
            {
                finish.TrySetResult();
                await resumed.Task.WaitAsync(TimeSpan.FromSeconds(3));
            }
            await _internalManager.Received(1).ReleaseAcquiredResources(
                Arg.Is<InternalFunctionContext[]>(items => items.Length == 1 && items[0] == context), CancellationToken.None);
            await _taskHandler.DidNotReceive().ExecuteRegisteredTaskAsync(context, true,
                Arg.Any<CancellationTokenSource>(), Arg.Any<CancellationToken>());
        }
        finally
        {
            finish.TrySetResult();
            await service.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(3));
        }
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
        Assert.Equal(LogLevel.Warning, log.Level);
        Assert.Same(failure, log.Exception);
        Assert.Contains("retrying", log.Message);
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
    public async Task Repeated_failures_are_throttled_and_recovery_updates_health()
    {
        var calls = 0;
        _internalManager.RunTimedOutTickers(Arg.Any<CancellationToken>()).Returns(_ =>
            Interlocked.Increment(ref calls) <= 2
                ? Task.FromException<InternalFunctionContext[]>(new InvalidOperationException("database unavailable"))
                : Task.FromResult(Array.Empty<InternalFunctionContext>()));
        var sink = Substitute.For<ILogger>();
        var factory = Substitute.For<ILoggerFactory>();
        factory.CreateLogger(Arg.Any<string>()).Returns(sink);
        var logger = new Logger<TickerQFallbackBackgroundService>(factory);
        using var health = new TickerQRuntimeDiagnostics(new InMemoryTickerExecutionLogStore());
        using var service = new TickerQFallbackBackgroundService(_internalManager, _schedulerOptions, _taskHandler,
            _taskScheduler, LicenseTestState.Active(), logger: logger, diagnostics: health);
        await service.StartAsync(CancellationToken.None);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (health.LastSuccessfulFallbackScanUtc == null) await Task.Delay(10, timeout.Token);
        await service.StopAsync(CancellationToken.None);
        Assert.Equal(2, health.FallbackScanFailures);
        Assert.NotNull(health.LastFallbackFailureUtc);
        Assert.True(health.LastSuccessfulFallbackScanUtc >= health.LastFallbackFailureUtc);
        Assert.Single(sink.ReceivedCalls(), call => call.GetMethodInfo().Name == "Log"
            && call.GetArguments()[0] is LogLevel.Warning);
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
    public async Task Recovery_LoadsFunctionAndConcurrencySettingsBeyondGrandchildren()
    {
        var name = $"RecoveryDepth-{Guid.NewGuid():N}";
        TickerFunctionDelegate body = (_, _, _) => Task.CompletedTask;
        TickerFunctionProvider.RegisterFunctions(
            new System.Collections.Generic.Dictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate, int)>
            {
                [name] = ("", TickerTaskPriority.Normal, body, 1)
            });
        TickerFunctionProvider.Build();
        var root = new InternalFunctionContext { TickerId = Guid.NewGuid(), FunctionName = name, TimeTickerChildren = [] };
        var descendant = root;
        for (var depth = 0; depth < 6; depth++)
        {
            var child = new InternalFunctionContext { TickerId = Guid.NewGuid(), FunctionName = name, TimeTickerChildren = [] };
            descendant.TimeTickerChildren.Add(child);
            descendant = child;
        }
        var scans = 0;
        _internalManager.RunTimedOutTickers(Arg.Any<CancellationToken>())
            .Returns(_ => Interlocked.Increment(ref scans) == 1 ? new[] { root } : Array.Empty<InternalFunctionContext>());
        var queued = new TaskCompletionSource<Func<CancellationToken, Task>>(TaskCreationOptions.RunContinuationsAsynchronously);
        _taskScheduler.QueueAsync(Arg.Any<Func<CancellationToken, Task>>(),
                Arg.Any<TickerTaskPriority>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                queued.TrySetResult(call.ArgAt<Func<CancellationToken, Task>>(0));
                return ValueTask.CompletedTask;
            });
        using var service = CreateService();
        await service.StartAsync(CancellationToken.None);
        try
        {
            var work = await queued.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Same(body, descendant.CachedDelegate);
            Assert.Equal(1, descendant.CachedMaxConcurrency);
            await work(CancellationToken.None);
            await _taskHandler.Received(1).ExecuteRegisteredTaskAsync(root, true, Arg.Any<CancellationTokenSource>(), Arg.Any<CancellationToken>());
        }
        finally { await service.StopAsync(CancellationToken.None); }
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
