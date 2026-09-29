using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using TickerQ.BackgroundServices;
using TickerQ.Dispatcher;
using TickerQ.Utilities;
using TickerQ.Utilities.Instrumentation;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Licensing;
using TickerQ.Utilities.Models;

namespace TickerQ.Tests.Licensing;

public sealed class LicenseEnforcementTests
{
    [Fact]
    public async Task Direct_execution_handler_rejects_before_touching_persistence()
    {
        var clock = FixedClock();
        var manager = Substitute.For<IInternalTickerManager>();
        var services = new ServiceCollection().BuildServiceProvider();
        var handler = new TickerExecutionTaskHandler(
            services,
            clock,
            Substitute.For<ITickerQInstrumentation>(),
            manager,
            new SchedulerOptionsBuilder(),
            Substitute.For<ITickerQFailureNotifier>(),
            BlockedState(clock));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.ExecuteTaskAsync(null!, isDue: false));

        Assert.Contains("No TickerQ license certificate", error.Message, StringComparison.Ordinal);
        await manager.DidNotReceiveWithAnyArgs().UpdateTickerAsync(default!, default);
    }

    [Fact]
    public async Task Direct_dispatch_rejects_before_queueing_work()
    {
        var clock = FixedClock();
        var scheduler = Substitute.For<ITickerQTaskScheduler>();
        var dispatcher = new TickerQDispatcher(
            scheduler,
            Substitute.For<ITickerExecutionTaskHandler>(),
            new TickerFunctionConcurrencyGate(),
            BlockedState(clock));

        Assert.False(dispatcher.IsEnabled);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            dispatcher.DispatchAsync([new InternalFunctionContext()]));
        await scheduler.DidNotReceiveWithAnyArgs().QueueAsync(default!, default!, default);
    }

    [Fact]
    public async Task Scheduler_does_not_start_or_resume_when_license_blocks()
    {
        var clock = FixedClock();
        var taskScheduler = Substitute.For<ITickerQTaskScheduler>();
        var service = new TickerQSchedulerBackgroundService(
            new TickerExecutionContext(),
            Substitute.For<ITickerExecutionTaskHandler>(),
            taskScheduler,
            Substitute.For<IInternalTickerManager>(),
            new SchedulerOptionsBuilder(),
            new TickerFunctionConcurrencyGate(),
            BlockedState(clock));

        await service.StartAsync(CancellationToken.None);

        Assert.False(service.IsRunning);
        taskScheduler.DidNotReceive().Resume();
        taskScheduler.DidNotReceive().Freeze();
    }

    private static ITickerClock FixedClock()
    {
        var clock = Substitute.For<ITickerClock>();
        clock.UtcNow.Returns(new DateTime(2026, 9, 25, 12, 0, 0, DateTimeKind.Utc));
        return clock;
    }

    private static TickerQLicenseStateProvider BlockedState(ITickerClock clock)
    {
        var state = new TickerQLicenseStateProvider(clock);
        state.Publish(TickerQLicenseState.Missing());
        return state;
    }
}
