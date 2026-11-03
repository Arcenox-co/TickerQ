using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NSubstitute;
using TickerQ.BackgroundServices;
using TickerQ.Utilities;
using TickerQ.Utilities.Infrastructure;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Models;

namespace TickerQ.Tests;

/// <summary>
/// Stopping cancels the stopping token, which runs callbacks registered on it. A database driver
/// registers one for a query in flight, and MySql.Data's throws while aborting a TLS handshake.
/// </summary>
[Collection("TickerCancellationTokenState")]
public sealed class TickerQBackgroundServiceTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task PlainBackgroundService_FailsItsStop_WhenACancellationCallbackThrows()
    {
        using var service = new PlainService();
        await service.StartAsync(CancellationToken.None);
        await service.Querying.WaitAsync(Timeout);

        await Assert.ThrowsAsync<AggregateException>(() => service.StopAsync(CancellationToken.None));
    }

    [Fact]
    public async Task TickerQBackgroundService_Stops_WhenACancellationCallbackThrows_AndReportsIt()
    {
        var logger = Substitute.For<ILogger>();
        logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);
        using var service = new SafeService(logger);
        await service.StartAsync(CancellationToken.None);
        await service.Querying.WaitAsync(Timeout);

        await service.StopAsync(CancellationToken.None);

        Assert.True(service.ExecuteTask!.IsCompleted);
        logger.Received(1).Log(LogLevel.Warning, Arg.Any<EventId>(), Arg.Any<object>(),
            Arg.Is<Exception>(x => x is AggregateException), Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task Scheduler_StopsAndRestarts_WhileAStoreReadsCancellationCallbackThrows()
    {
        var manager = Substitute.For<IInternalTickerManager>();
        var reads = 0;
        var reading = new SemaphoreSlim(0);
        manager.GetNextTickers(Arg.Any<CancellationToken>()).Returns(call =>
        {
            Interlocked.Increment(ref reads);
            reading.Release();
            return Query(call.Arg<CancellationToken>());
        });
        using var service = new TickerQSchedulerBackgroundService(new TickerExecutionContext(),
            Substitute.For<ITickerExecutionTaskHandler>(), Substitute.For<ITickerQTaskScheduler>(), manager,
            new SchedulerOptionsBuilder { MinPollingInterval = TimeSpan.Zero }, LicenseTestState.Active());

        await service.StartAsync(CancellationToken.None);
        Assert.True(await reading.WaitAsync(Timeout));
        // A restart cancels the loop from a timer thread; a throw there would end the process.
        service.Restart();
        Assert.True(await reading.WaitAsync(Timeout), "The scheduler did not read again after the restart.");

        await service.StopAsync(CancellationToken.None);
        Assert.True(service.ExecuteTask!.IsCompleted);
    }

    private static async Task<(TimeSpan, InternalFunctionContext[])> Query(CancellationToken cancellationToken)
    {
        using var abort = cancellationToken.Register(() => throw new IOException("I/O error occurred."));
        await Task.Delay(System.Threading.Timeout.Infinite, cancellationToken);
        return default;
    }

    // Each runs one long query on its stopping token, the way a store-backed loop does.
    private sealed class PlainService : BackgroundService
    {
        private readonly TaskCompletionSource _querying = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Querying => _querying.Task;

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _querying.TrySetResult();
            return Query(stoppingToken);
        }
    }

    private sealed class SafeService(ILogger logger) : TickerQBackgroundService(logger)
    {
        private readonly TaskCompletionSource _querying = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Querying => _querying.Task;

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _querying.TrySetResult();
            return Query(stoppingToken);
        }
    }
}
