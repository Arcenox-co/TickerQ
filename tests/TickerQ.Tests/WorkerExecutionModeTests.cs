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
    public async Task ExecuteWorkerTaskAsync_TestRun_ExposesTestModeAndKeepsProgressEphemeral()
    {
        var manager = Substitute.For<IInternalTickerManager>();
        var handler = CreateHandler(manager);
        var observedTestMode = false;
        var context = Context(async (_, _, runtime) =>
        {
            observedTestMode = runtime.IsTestRun;
            await runtime.ReportProgressAsync(50, "Halfway");
        });
        context.IsTestRun = true;

        var outcome = await handler.ExecuteWorkerTaskAsync(context, isDue: false);

        Assert.True(observedTestMode);
        Assert.True(outcome.Success);
        await manager.DidNotReceiveWithAnyArgs().ReportProgressAsync(default!, default);
        await manager.DidNotReceiveWithAnyArgs().UpdateTickerAsync(default!, default);
    }

    [Fact]
    public async Task ExecuteWorkerTaskAsync_TestFailure_DoesNotTriggerApplicationAlertsOrExceptionHooks()
    {
        var manager = Substitute.For<IInternalTickerManager>();
        var notifier = Substitute.For<ITickerQFailureNotifier>();
        var exceptionHandler = Substitute.For<ITickerExceptionHandler>();
        var services = new ServiceCollection()
            .AddSingleton(exceptionHandler)
            .BuildServiceProvider();
        var handler = CreateHandler(manager, services, notifier);
        var context = Context((_, _, _) =>
            Task.FromException(new InvalidOperationException("test failure")));
        context.IsTestRun = true;

        var outcome = await handler.ExecuteWorkerTaskAsync(context, isDue: false);

        Assert.Equal(TickerStatus.Failed, outcome.Status);
        notifier.DidNotReceiveWithAnyArgs().Notify(default!);
        await exceptionHandler.DidNotReceiveWithAnyArgs()
            .HandleExceptionAsync(default!, default, default);
        await exceptionHandler.DidNotReceiveWithAnyArgs()
            .HandleCanceledExceptionAsync(default!, default, default);
    }

    [Fact]
    public async Task ExecuteTestChainAsync_RunsEligibleDescendants_PassesParentResult_AndWritesNothing()
    {
        var manager = Substitute.For<IInternalTickerManager>();
        var handler = CreateHandler(manager);
        var parentResult = Envelope("parent-result");
        TickerResultEnvelope observedParentResult = null;
        var root = Context((_, _, runtime) =>
        {
            runtime.ResultSink.Set(parentResult);
            return Task.CompletedTask;
        });
        var successChild = Context((_, _, runtime) =>
        {
            observedParentResult = runtime.ParentResultEnvelope;
            return Task.CompletedTask;
        });
        successChild.ParentId = root.TickerId;
        successChild.RunCondition = RunCondition.OnSuccess;
        var failureChild = Context((_, _, _) =>
            Task.FromException(new InvalidOperationException("must not run")));
        failureChild.ParentId = root.TickerId;
        failureChild.RunCondition = RunCondition.OnFailure;
        root.TimeTickerChildren = [successChild, failureChild];

        await handler.ExecuteTestChainAsync(root);

        Assert.Equal(TickerStatus.Done, root.Status);
        Assert.Equal(TickerStatus.Done, successChild.Status);
        Assert.Equal(TickerStatus.Skipped, failureChild.Status);
        Assert.Same(parentResult, observedParentResult);
        Assert.True(root.IsTestRun);
        Assert.True(successChild.IsTestRun);
        await manager.DidNotReceiveWithAnyArgs().UpdateTickerAsync(default!, default);
        await manager.DidNotReceiveWithAnyArgs().UpdateSkipTimeTickersWithUnifiedContextAsync(default!, default);
        await manager.DidNotReceiveWithAnyArgs().GetParentResultAsync(default, default, default);
    }

    [Fact]
    public async Task ExecuteTestChainAsync_DoesNotAdvertiseAnUnpersistedLease()
    {
        var manager = Substitute.For<IInternalTickerManager>();
        var handler = CreateHandler(manager);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var partition = $"test:{Guid.NewGuid():N}";
        var root = Context(async (_, _, _) =>
        {
            entered.TrySetResult();
            await release.Task;
        });
        root.RuntimePartitionKey = partition;

        var execution = handler.ExecuteTestChainAsync(root);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var timeTickers = new List<AcquisitionLease>();
        var occurrences = new List<AcquisitionLease>();
        TickerCancellationTokenManager.SnapshotRunningForLeaseRenewal(
            partition, timeTickers, occurrences);

        Assert.Empty(timeTickers);
        Assert.Empty(occurrences);

        release.TrySetResult();
        await execution.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(TickerStatus.Done, root.Status);
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

    private static TickerExecutionTaskHandler CreateHandler(
        IInternalTickerManager manager,
        IServiceProvider services = null,
        ITickerQFailureNotifier notifier = null)
    {
        services ??= new ServiceCollection().BuildServiceProvider();
        var clock = Substitute.For<ITickerClock>();
        clock.UtcNow.Returns(DateTime.UtcNow);
        return new TickerExecutionTaskHandler(
            services,
            clock,
            Substitute.For<ITickerQInstrumentation>(),
            manager,
            new SchedulerOptionsBuilder(),
            notifier ?? Substitute.For<ITickerQFailureNotifier>(),
            LicenseTestState.Active(clock), new TickerFunctionConcurrencyGate(),
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
