using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using TickerQ.Exceptions;
using TickerQ.Utilities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Instrumentation;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Models;

namespace TickerQ.Tests;

public class TickerExecutionTaskHandlerTests
{
    private readonly ITickerClock _clock;
    private readonly IInternalTickerManager _internalManager;
    private readonly ITickerQInstrumentation _instrumentation;
    private readonly ServiceProvider _serviceProvider;
    private readonly TickerExecutionTaskHandler _handler;

    public TickerExecutionTaskHandlerTests()
    {
        _clock = Substitute.For<ITickerClock>();
        _clock.UtcNow.Returns(DateTime.UtcNow);
        _internalManager = Substitute.For<IInternalTickerManager>();
        _instrumentation = Substitute.For<ITickerQInstrumentation>();

        var services = new ServiceCollection();
        services.AddSingleton(_internalManager);
        services.AddSingleton(_instrumentation);
        _serviceProvider = services.BuildServiceProvider();

        _handler = new TickerExecutionTaskHandler(_serviceProvider, _clock, _instrumentation, _internalManager);
    }

    #region Success Path

    [Fact]
    public async Task ExecuteTaskAsync_SetsStatusDone_WhenSucceeds_NotDue()
    {
        var context = CreateContext(ct: (_, _, _) => Task.CompletedTask);

        await _handler.ExecuteTaskAsync(context, isDue: false);

        context.Status.Should().Be(TickerStatus.Done);
    }

    [Fact]
    public async Task ExecuteTaskAsync_SetsStatusDueDone_WhenSucceeds_IsDue()
    {
        var context = CreateContext(ct: (_, _, _) => Task.CompletedTask);

        await _handler.ExecuteTaskAsync(context, isDue: true);

        context.Status.Should().Be(TickerStatus.DueDone);
    }

    [Fact]
    public async Task ExecuteTaskAsync_CallsUpdateTickerAsync_OnSuccess()
    {
        var context = CreateContext(ct: (_, _, _) => Task.CompletedTask);

        await _handler.ExecuteTaskAsync(context, isDue: false);

        await _internalManager.Received(1).UpdateTickerAsync(
            Arg.Is<InternalFunctionContext>(c => c.TickerId == context.TickerId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteTaskAsync_SetsElapsedTime_OnSuccess()
    {
        var context = CreateContext(ct: async (_, _, _) => await Task.Delay(10));

        await _handler.ExecuteTaskAsync(context, isDue: false);

        context.ElapsedTime.Should().BeGreaterOrEqualTo(0);
    }

    [Fact]
    public async Task ExecuteTaskAsync_SetsExecutedAt_OnSuccess()
    {
        var now = new DateTime(2025, 6, 1, 12, 0, 0, DateTimeKind.Utc);
        _clock.UtcNow.Returns(now);
        var context = CreateContext(ct: (_, _, _) => Task.CompletedTask);

        await _handler.ExecuteTaskAsync(context, isDue: false);

        context.ExecutedAt.Should().Be(now);
    }

    #endregion

    #region Failure Path

    [Fact]
    public async Task ExecuteTaskAsync_SetsStatusFailed_WhenDelegateThrows()
    {
        var context = CreateContext(ct: (_, _, _) => throw new InvalidOperationException("boom"));

        await _handler.ExecuteTaskAsync(context, isDue: false);

        context.Status.Should().Be(TickerStatus.Failed);
    }

    [Fact]
    public async Task ExecuteTaskAsync_RecordsExceptionDetails_WhenDelegateThrows()
    {
        var context = CreateContext(ct: (_, _, _) => throw new InvalidOperationException("boom"));

        await _handler.ExecuteTaskAsync(context, isDue: false);

        context.ExceptionDetails.Should().NotBeNullOrWhiteSpace();
        context.ExceptionDetails.Should().Contain("boom");
    }

    [Fact]
    public async Task ExecuteTaskAsync_CallsExceptionHandler_WhenRegistered_AndDelegateThrows()
    {
        var exceptionHandler = Substitute.For<ITickerExceptionHandler>();
        var services = new ServiceCollection();
        services.AddSingleton(_internalManager);
        services.AddSingleton(_instrumentation);
        services.AddSingleton(exceptionHandler);
        var sp = services.BuildServiceProvider();
        var handler = new TickerExecutionTaskHandler(sp, _clock, _instrumentation, _internalManager);

        var context = CreateContext(ct: (_, _, _) => throw new InvalidOperationException("boom"));

        await handler.ExecuteTaskAsync(context, isDue: false);

        await exceptionHandler.Received(1).HandleExceptionAsync(
            Arg.Any<Exception>(),
            Arg.Is(context.TickerId),
            Arg.Is(context.Type));
    }

    #endregion

    #region Cancellation Path

    [Fact]
    public async Task ExecuteTaskAsync_SetsStatusCancelled_WhenTaskCancelledExceptionThrown()
    {
        var context = CreateContext(ct: (_, _, _) => throw new TaskCanceledException("cancelled"));

        await _handler.ExecuteTaskAsync(context, isDue: false);

        context.Status.Should().Be(TickerStatus.Cancelled);
    }

    [Fact]
    public async Task ExecuteTaskAsync_CallsCancelledExceptionHandler_WhenRegistered()
    {
        var exceptionHandler = Substitute.For<ITickerExceptionHandler>();
        var services = new ServiceCollection();
        services.AddSingleton(_internalManager);
        services.AddSingleton(_instrumentation);
        services.AddSingleton(exceptionHandler);
        var sp = services.BuildServiceProvider();
        var handler = new TickerExecutionTaskHandler(sp, _clock, _instrumentation, _internalManager);

        var context = CreateContext(ct: (_, _, _) => throw new TaskCanceledException("cancelled"));

        await handler.ExecuteTaskAsync(context, isDue: false);

        await exceptionHandler.Received(1).HandleCanceledExceptionAsync(
            Arg.Any<Exception>(),
            Arg.Is(context.TickerId),
            Arg.Is(context.Type));
    }

    #endregion

    #region TerminateExecutionException Path

    [Fact]
    public async Task ExecuteTaskAsync_SetsStatusSkipped_WhenTerminateExecutionExceptionThrown()
    {
        var context = CreateContext(ct: (_, _, _) => throw new TerminateExecutionException("skip me"));

        await _handler.ExecuteTaskAsync(context, isDue: false);

        context.Status.Should().Be(TickerStatus.Skipped);
    }

    [Fact]
    public async Task ExecuteTaskAsync_SetsCustomStatus_WhenTerminateExecutionWithStatusThrown()
    {
        var context = CreateContext(ct: (_, _, _) =>
            throw new TerminateExecutionException(TickerStatus.Cancelled, "terminate as cancelled"));

        await _handler.ExecuteTaskAsync(context, isDue: false);

        context.Status.Should().Be(TickerStatus.Cancelled);
    }

    [Fact]
    public async Task ExecuteTaskAsync_RecordsExceptionMessage_WhenTerminateExecutionThrown()
    {
        var context = CreateContext(ct: (_, _, _) =>
            throw new TerminateExecutionException("skip reason"));

        await _handler.ExecuteTaskAsync(context, isDue: false);

        context.ExceptionDetails.Should().Contain("skip reason");
    }

    [Fact]
    public async Task ExecuteTaskAsync_RecordsInnerExceptionMessage_WhenTerminateExecutionHasInner()
    {
        var inner = new InvalidOperationException("inner reason");
        var context = CreateContext(ct: (_, _, _) =>
            throw new TerminateExecutionException("outer", inner));

        await _handler.ExecuteTaskAsync(context, isDue: false);

        context.ExceptionDetails.Should().Contain("inner reason");
    }

    #endregion

    #region Null CachedDelegate

    [Fact]
    public async Task ExecuteTaskAsync_SetsStatusFailed_WhenCachedDelegateIsNull()
    {
        var context = CreateContext(ct: null);

        await _handler.ExecuteTaskAsync(context, isDue: false);

        context.Status.Should().Be(TickerStatus.Failed);
        context.ExceptionDetails.Should().Contain("was not found");
    }

    #endregion

    #region Parent-Child Execution (TimeTicker)

    [Fact]
    public async Task ExecuteTaskAsync_RunsInProgressChildren_ConcurrentlyWithParent()
    {
        var childExecuted = false;
        var parentContext = CreateContext(
            type: TickerType.TimeTicker,
            ct: (_, _, _) => Task.CompletedTask);

        var childContext = CreateContext(
            type: TickerType.TimeTicker,
            ct: (_, _, _) =>
            {
                childExecuted = true;
                return Task.CompletedTask;
            });
        childContext.RunCondition = RunCondition.InProgress;
        parentContext.TimeTickerChildren.Add(childContext);

        await _handler.ExecuteTaskAsync(parentContext, isDue: false);

        childExecuted.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteTaskAsync_RunsOnSuccessChild_AfterParentSucceeds()
    {
        var childExecuted = false;
        var parentContext = CreateContext(
            type: TickerType.TimeTicker,
            ct: (_, _, _) => Task.CompletedTask);

        var childContext = CreateContext(
            type: TickerType.TimeTicker,
            ct: (_, _, _) =>
            {
                childExecuted = true;
                return Task.CompletedTask;
            });
        childContext.RunCondition = RunCondition.OnSuccess;
        parentContext.TimeTickerChildren.Add(childContext);

        await _handler.ExecuteTaskAsync(parentContext, isDue: false);

        childExecuted.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteTaskAsync_SkipsOnSuccessChild_WhenParentFails()
    {
        var childExecuted = false;
        var parentContext = CreateContext(
            type: TickerType.TimeTicker,
            ct: (_, _, _) => throw new InvalidOperationException("parent fail"));

        var childContext = CreateContext(
            type: TickerType.TimeTicker,
            ct: (_, _, _) =>
            {
                childExecuted = true;
                return Task.CompletedTask;
            });
        childContext.RunCondition = RunCondition.OnSuccess;
        parentContext.TimeTickerChildren.Add(childContext);

        await _handler.ExecuteTaskAsync(parentContext, isDue: false);

        childExecuted.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteTaskAsync_RunsOnFailureChild_WhenParentFails()
    {
        var childExecuted = false;
        var parentContext = CreateContext(
            type: TickerType.TimeTicker,
            ct: (_, _, _) => throw new InvalidOperationException("parent fail"));

        var childContext = CreateContext(
            type: TickerType.TimeTicker,
            ct: (_, _, _) =>
            {
                childExecuted = true;
                return Task.CompletedTask;
            });
        childContext.RunCondition = RunCondition.OnFailure;
        parentContext.TimeTickerChildren.Add(childContext);

        await _handler.ExecuteTaskAsync(parentContext, isDue: false);

        childExecuted.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteTaskAsync_SkipsOnFailureChild_WhenParentSucceeds()
    {
        var childExecuted = false;
        var parentContext = CreateContext(
            type: TickerType.TimeTicker,
            ct: (_, _, _) => Task.CompletedTask);

        var childContext = CreateContext(
            type: TickerType.TimeTicker,
            ct: (_, _, _) =>
            {
                childExecuted = true;
                return Task.CompletedTask;
            });
        childContext.RunCondition = RunCondition.OnFailure;
        parentContext.TimeTickerChildren.Add(childContext);

        await _handler.ExecuteTaskAsync(parentContext, isDue: false);

        childExecuted.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteTaskAsync_RunsOnAnyCompletedStatus_RegardlessOfParentOutcome()
    {
        var childExecutedOnSuccess = false;
        var childExecutedOnFail = false;

        // Test with parent success
        var parentSuccess = CreateContext(
            type: TickerType.TimeTicker,
            ct: (_, _, _) => Task.CompletedTask);
        var childSuccess = CreateContext(
            type: TickerType.TimeTicker,
            ct: (_, _, _) => { childExecutedOnSuccess = true; return Task.CompletedTask; });
        childSuccess.RunCondition = RunCondition.OnAnyCompletedStatus;
        parentSuccess.TimeTickerChildren.Add(childSuccess);
        await _handler.ExecuteTaskAsync(parentSuccess, isDue: false);

        // Test with parent failure
        var parentFail = CreateContext(
            type: TickerType.TimeTicker,
            ct: (_, _, _) => throw new InvalidOperationException("fail"));
        var childFail = CreateContext(
            type: TickerType.TimeTicker,
            ct: (_, _, _) => { childExecutedOnFail = true; return Task.CompletedTask; });
        childFail.RunCondition = RunCondition.OnAnyCompletedStatus;
        parentFail.TimeTickerChildren.Add(childFail);
        await _handler.ExecuteTaskAsync(parentFail, isDue: false);

        childExecutedOnSuccess.Should().BeTrue();
        childExecutedOnFail.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteTaskAsync_RunsOnFailureOrCancelled_WhenParentFails()
    {
        var childExecuted = false;
        var parentContext = CreateContext(
            type: TickerType.TimeTicker,
            ct: (_, _, _) => throw new InvalidOperationException("fail"));

        var childContext = CreateContext(
            type: TickerType.TimeTicker,
            ct: (_, _, _) => { childExecuted = true; return Task.CompletedTask; });
        childContext.RunCondition = RunCondition.OnFailureOrCancelled;
        parentContext.TimeTickerChildren.Add(childContext);

        await _handler.ExecuteTaskAsync(parentContext, isDue: false);

        childExecuted.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteTaskAsync_BulkSkipsDescendants_WhenChildConditionNotMet()
    {
        var parentContext = CreateContext(
            type: TickerType.TimeTicker,
            ct: (_, _, _) => Task.CompletedTask);

        var childContext = CreateContext(
            type: TickerType.TimeTicker,
            ct: (_, _, _) => Task.CompletedTask);
        childContext.RunCondition = RunCondition.OnFailure;

        var grandChild = CreateContext(
            type: TickerType.TimeTicker,
            ct: (_, _, _) => Task.CompletedTask);
        grandChild.RunCondition = RunCondition.OnSuccess;
        childContext.TimeTickerChildren.Add(grandChild);

        parentContext.TimeTickerChildren.Add(childContext);

        await _handler.ExecuteTaskAsync(parentContext, isDue: false);

        // The skipped children should be bulk-updated
        await _internalManager.Received().UpdateSkipTimeTickersWithUnifiedContextAsync(
            Arg.Any<InternalFunctionContext[]>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteTaskAsync_ChildWithNullDelegate_IsSkipped()
    {
        var parentContext = CreateContext(
            type: TickerType.TimeTicker,
            ct: (_, _, _) => Task.CompletedTask);

        var nullDelegateChild = new InternalFunctionContext
        {
            TickerId = Guid.NewGuid(),
            FunctionName = "NullChild",
            Type = TickerType.TimeTicker,
            CachedDelegate = null,
            RunCondition = RunCondition.OnSuccess,
            TimeTickerChildren = []
        };
        parentContext.TimeTickerChildren.Add(nullDelegateChild);

        // Should not throw
        await _handler.ExecuteTaskAsync(parentContext, isDue: false);
    }

    #endregion

    #region CronTickerOccurrence - direct path

    [Fact]
    public async Task ExecuteTaskAsync_CronTicker_GoesDirectlyToRunContextFunction()
    {
        var executed = false;
        var context = CreateContext(
            type: TickerType.CronTickerOccurrence,
            ct: (_, _, _) => { executed = true; return Task.CompletedTask; });

        await _handler.ExecuteTaskAsync(context, isDue: false);

        executed.Should().BeTrue();
        context.Status.Should().Be(TickerStatus.Done);
    }

    #endregion

    #region Instrumentation

    [Fact]
    public async Task ExecuteTaskAsync_LogsJobEnqueued_OnExecution()
    {
        var context = CreateContext(ct: (_, _, _) => Task.CompletedTask);

        await _handler.ExecuteTaskAsync(context, isDue: false);

        _instrumentation.Received().LogJobEnqueued(
            Arg.Any<string>(),
            Arg.Is(context.FunctionName),
            Arg.Is(context.TickerId),
            Arg.Any<string>());
    }

    [Fact]
    public async Task ExecuteTaskAsync_LogsJobCompleted_OnSuccess()
    {
        var context = CreateContext(ct: (_, _, _) => Task.CompletedTask);

        await _handler.ExecuteTaskAsync(context, isDue: false);

        _instrumentation.Received().LogJobCompleted(
            Arg.Is(context.TickerId),
            Arg.Is(context.FunctionName),
            Arg.Any<long>(),
            Arg.Is(true));
    }

    [Fact]
    public async Task ExecuteTaskAsync_LogsJobFailed_OnFailure()
    {
        var context = CreateContext(ct: (_, _, _) => throw new InvalidOperationException("fail"));

        await _handler.ExecuteTaskAsync(context, isDue: false);

        _instrumentation.Received().LogJobFailed(
            Arg.Is(context.TickerId),
            Arg.Is(context.FunctionName),
            Arg.Any<Exception>(),
            Arg.Any<int>());
    }

    [Fact]
    public async Task ExecuteTaskAsync_LogsJobCancelled_OnCancellation()
    {
        var context = CreateContext(ct: (_, _, _) => throw new TaskCanceledException());

        await _handler.ExecuteTaskAsync(context, isDue: false);

        _instrumentation.Received().LogJobCancelled(
            Arg.Is(context.TickerId),
            Arg.Is(context.FunctionName),
            Arg.Any<string>());
    }

    [Fact]
    public async Task ExecuteTaskAsync_LogsJobSkipped_OnTerminateExecution()
    {
        var context = CreateContext(ct: (_, _, _) =>
            throw new TerminateExecutionException("skipped reason"));

        await _handler.ExecuteTaskAsync(context, isDue: false);

        _instrumentation.Received().LogJobSkipped(
            Arg.Is(context.TickerId),
            Arg.Is(context.FunctionName),
            Arg.Any<string>());
    }

    #endregion

    #region Helpers

    private static InternalFunctionContext CreateContext(
        TickerFunctionDelegate? ct = null,
        TickerType type = TickerType.CronTickerOccurrence)
    {
        return new InternalFunctionContext
        {
            TickerId = Guid.NewGuid(),
            FunctionName = "TestFunction",
            Type = type,
            ExecutionTime = DateTime.UtcNow,
            RetryIntervals = [],
            Retries = 0,
            RetryCount = 0,
            Status = TickerStatus.Idle,
            CachedDelegate = ct,
            TimeTickerChildren = []
        };
    }

    #endregion
}
