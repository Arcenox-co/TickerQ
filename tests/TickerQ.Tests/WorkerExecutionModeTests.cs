using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using TickerQ.Utilities;
using TickerQ.Utilities.Base;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Instrumentation;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Models;
using Xunit;

namespace TickerQ.Tests;

public sealed class WorkerExecutionModeTests
{
    [Fact]
    public async Task ExecuteWorkerTaskAsync_ReturnsSuccessfulResult_WithoutLifecycleWrites()
    {
        var manager = Substitute.For<IInternalTickerManager>();
        var envelope = Envelope("final");
        var handler = CreateHandler(manager);
        var context = Context((_, _, runtime) =>
        {
            runtime.ResultSink.Set(envelope);
            return Task.CompletedTask;
        });

        var outcome = await handler.ExecuteWorkerTaskAsync(context, isDue: false);

        Assert.Equal(TickerStatus.Done, outcome.Status);
        Assert.Same(envelope, outcome.ResultEnvelope);
        Assert.Null(outcome.Error);
        await manager.DidNotReceiveWithAnyArgs().UpdateTickerAsync(default!, default);
        await manager.DidNotReceiveWithAnyArgs().ReleaseAcquiredResources(default!, default);
    }

    [Fact]
    public async Task ExecuteWorkerTaskAsync_SuccessWithoutResult_ExplicitlyReturnsAbsence_WithoutLifecycleWrites()
    {
        var manager = Substitute.For<IInternalTickerManager>();
        var handler = CreateHandler(manager);

        var outcome = await handler.ExecuteWorkerTaskAsync(
            Context((_, _, _) => Task.CompletedTask), isDue: true);

        Assert.Equal(TickerStatus.DueDone, outcome.Status);
        Assert.Null(outcome.ResultEnvelope);
        Assert.Null(outcome.Error);
        await manager.DidNotReceiveWithAnyArgs().UpdateTickerAsync(default!, default);
        await manager.DidNotReceiveWithAnyArgs().ReleaseAcquiredResources(default!, default);
    }

    [Fact]
    public async Task ExecuteWorkerTaskAsync_RetryPublishesOnlyFinalSuccessfulAttempt()
    {
        var manager = Substitute.For<IInternalTickerManager>();
        var handler = CreateHandler(manager);
        var attempts = 0;
        var discarded = Envelope("discarded");
        var final = Envelope("final");
        var context = Context((_, _, runtime) =>
        {
            attempts++;
            runtime.ResultSink.Set(attempts == 1 ? discarded : final);
            if (attempts == 1)
                throw new InvalidOperationException("retry me");
            return Task.CompletedTask;
        });
        context.Retries = 1;
        context.RetryIntervals = [0];

        var outcome = await handler.ExecuteWorkerTaskAsync(context, isDue: false);

        Assert.Equal(2, attempts);
        Assert.Equal(TickerStatus.Done, outcome.Status);
        Assert.Same(final, outcome.ResultEnvelope);
        await manager.DidNotReceiveWithAnyArgs().UpdateTickerAsync(default!, default);
        await manager.DidNotReceiveWithAnyArgs().ReleaseAcquiredResources(default!, default);
    }

    [Theory]
    [InlineData(false, TickerStatus.Failed)]
    [InlineData(true, TickerStatus.Cancelled)]
    public async Task ExecuteWorkerTaskAsync_FailedOrCancelled_ReturnsNoResult_WithoutLifecycleWrites(
        bool cancel, TickerStatus expectedStatus)
    {
        var manager = Substitute.For<IInternalTickerManager>();
        var handler = CreateHandler(manager);
        var context = Context((_, _, runtime) =>
        {
            runtime.ResultSink.Set(Envelope("must-not-leak"));
            return cancel
                ? Task.FromException(new TaskCanceledException("cancelled"))
                : Task.FromException(new InvalidOperationException("failed"));
        });

        var outcome = await handler.ExecuteWorkerTaskAsync(context, isDue: false);

        Assert.Equal(expectedStatus, outcome.Status);
        Assert.Null(outcome.ResultEnvelope);
        Assert.NotNull(outcome.Error);
        await manager.DidNotReceiveWithAnyArgs().UpdateTickerAsync(default!, default);
        await manager.DidNotReceiveWithAnyArgs().ReleaseAcquiredResources(default!, default);
    }

    private static TickerExecutionTaskHandler CreateHandler(IInternalTickerManager manager)
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var clock = Substitute.For<ITickerClock>();
        clock.UtcNow.Returns(DateTime.UtcNow);
        return new TickerExecutionTaskHandler(
            services,
            clock,
            Substitute.For<ITickerQInstrumentation>(),
            manager,
            new SchedulerOptionsBuilder(),
            Substitute.For<ITickerQFailureNotifier>(),
            _ => null);
    }

    private static InternalFunctionContext Context(TickerFunctionDelegate function) => new()
    {
        TickerId = Guid.NewGuid(),
        FunctionName = "WorkerFunction",
        Type = TickerType.TimeTicker,
        Status = TickerStatus.Idle,
        ExecutionTime = DateTime.UtcNow,
        RetryIntervals = [],
        CachedDelegate = function,
        TimeTickerChildren = []
    };

    private static TickerResultEnvelope Envelope(string value)
        => new(System.Text.Encoding.UTF8.GetBytes($"\"{value}\""),
            TickerResultEnvelope.CurrentVersion, "application/json");
}
