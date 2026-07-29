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

[Collection("TickerCancellationTokenState")]
public class TickerExecutionTaskHandlerTests : IDisposable
{
    private sealed class ScopedProbe : IDisposable
    {
        public bool IsDisposed { get; private set; }
        public void ThrowIfDisposed()
        {
            if (IsDisposed) throw new ObjectDisposedException(nameof(ScopedProbe));
        }
        public void Dispose() => IsDisposed = true;
    }

    public void Dispose()
    {
        TickerCancellationTokenManager.CleanUpTickerCancellationTokens();
    }

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

        _handler = new TickerExecutionTaskHandler(_serviceProvider, _clock, _instrumentation, _internalManager, new SchedulerOptionsBuilder(), Substitute.For<ITickerQFailureNotifier>());
    }

    #region Success Path

    [Fact]
    public async Task ExecuteTaskAsync_SetsStatusDone_WhenSucceeds_NotDue()
    {
        var context = CreateContext(ct: (_, _, _) => Task.CompletedTask);

        await _handler.ExecuteTaskAsync(context, isDue: false);

        Assert.Equal(TickerStatus.Done, context.Status);
    }

    [Fact]
    public async Task ExecuteTaskAsync_SetsStatusDueDone_WhenSucceeds_IsDue()
    {
        var context = CreateContext(ct: (_, _, _) => Task.CompletedTask);

        await _handler.ExecuteTaskAsync(context, isDue: true);

        Assert.Equal(TickerStatus.DueDone, context.Status);
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
    public async Task ExecuteTaskAsync_Unregisters_When_Terminal_Persistence_Throws()
    {
        var context = CreateContext(ct: (_, _, _) => Task.CompletedTask);
        _internalManager.UpdateTickerAsync(
                Arg.Is<InternalFunctionContext>(candidate => candidate.TickerId == context.TickerId),
                Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("terminal persistence failed"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _handler.ExecuteTaskAsync(context, isDue: false));

        Assert.Equal(0, TickerCancellationTokenManager.ActiveCount);
        Assert.False(TickerCancellationTokenManager.RemoveTickerCancellationToken(context.TickerId));
    }

    [Fact]
    public async Task ExecuteTaskAsync_DoesNotReleaseDeferredChild_WhenTerminalOwnershipIsStale()
    {
        var childCalls = 0;
        var root = CreateContext(ct: (_, _, _) => Task.CompletedTask, type: TickerType.TimeTicker);
        var child = CreateContext(ct: (_, _, _) =>
        {
            childCalls++;
            return Task.CompletedTask;
        }, type: TickerType.TimeTicker);
        child.RunCondition = RunCondition.OnSuccess;
        root.TimeTickerChildren.Add(child);
        _internalManager.UpdateTickerAsync(
                Arg.Is<InternalFunctionContext>(candidate => ReferenceEquals(candidate, root)
                    && candidate.Status == TickerStatus.Done),
                Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new TickerQ.Utilities.Exceptions.TickerTerminalUpdateNotAcknowledgedException("stale"));

        await Assert.ThrowsAsync<TickerQ.Utilities.Exceptions.TickerTerminalUpdateNotAcknowledgedException>(() =>
            _handler.ExecuteTaskAsync(root, isDue: false));

        Assert.Equal(0, childCalls);
        await _internalManager.DidNotReceive().UpdateTickerAsync(
            Arg.Is<InternalFunctionContext>(candidate => ReferenceEquals(candidate, child)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteTaskAsync_WithPreRegisteredSource_ReusesIt_NoDuplicate_AndRequestCancelHitsSameSource()
    {
        var delegateStarted = new TaskCompletionSource();
        var context = CreateContext(ct: async (token, _, _) =>
        {
            delegateStarted.SetResult();
            await Task.Delay(Timeout.Infinite, token); // cancelled via the shared registered source
        });

        // Acquisition-time registration owned by the caller (mirrors the scheduler).
        var registered = TickerCancellationTokenManager.TryRegisterAcquired(context, isDue: false);
        Assert.NotNull(registered);
        Assert.Equal(1, TickerCancellationTokenManager.ActiveCount);

        var execTask = _handler.ExecuteRegisteredTaskAsync(context, isDue: false, registered, CancellationToken.None);
        await delegateStarted.Task;

        // Reused the supplied source — no second registration was created.
        Assert.Equal(1, TickerCancellationTokenManager.ActiveCount);

        // Request-cancel by id fires the SAME source the running delegate observes.
        Assert.True(TickerCancellationTokenManager.RequestTickerCancellationById(context.TickerId));

        await execTask;
        Assert.Equal(TickerStatus.Cancelled, context.Status);

        // The handler did not remove the pre-registered source — the caller still owns it.
        Assert.Equal(1, TickerCancellationTokenManager.ActiveCount);
        Assert.True(TickerCancellationTokenManager.RemoveTickerCancellationToken(context.TickerId, registered));
        Assert.Equal(0, TickerCancellationTokenManager.ActiveCount);
    }

    [Fact]
    public async Task ExecuteTaskAsync_SetsElapsedTime_OnSuccess()
    {
        var context = CreateContext(ct: async (_, _, _) => await Task.Delay(10));

        await _handler.ExecuteTaskAsync(context, isDue: false);

        Assert.True(context.ElapsedTime >= 0);
    }

    [Fact]
    public async Task ExecuteTaskAsync_SetsExecutedAt_OnSuccess()
    {
        var now = new DateTime(2025, 6, 1, 12, 0, 0, DateTimeKind.Utc);
        _clock.UtcNow.Returns(now);
        var context = CreateContext(ct: (_, _, _) => Task.CompletedTask);

        await _handler.ExecuteTaskAsync(context, isDue: false);

        Assert.Equal(now, context.ExecutedAt);
    }

    #endregion

    #region Failure Path

    [Fact]
    public async Task ExecuteTaskAsync_ContractIdentityMismatch_FailsBeforeDelegateWithoutRetry()
    {
        var delegateCalls = 0;
        var descriptor = new TickerFunctionDescriptor("TestFunction", contractVersion: 2);
        var handler = new TickerExecutionTaskHandler(
            _serviceProvider, _clock, _instrumentation, _internalManager,
            new SchedulerOptionsBuilder(), Substitute.For<ITickerQFailureNotifier>(),
            _ => descriptor);
        var context = CreateContext(ct: (_, _, _) =>
        {
            delegateCalls++;
            return Task.CompletedTask;
        });
        context.Retries = 3;
        context.RequestContractVersion = 1;

        await handler.ExecuteTaskAsync(context, isDue: false);

        Assert.Equal(0, delegateCalls);
        Assert.Equal(TickerStatus.Failed, context.Status);
        Assert.Contains("contract drift", context.ExceptionDetails, StringComparison.OrdinalIgnoreCase);
        await _internalManager.Received(1).UpdateTickerAsync(context, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteTaskAsync_PersistedIdentityWithMissingDescriptor_FailsBeforeDelegateWithoutRetry()
    {
        var delegateCalls = 0;
        var handler = new TickerExecutionTaskHandler(
            _serviceProvider, _clock, _instrumentation, _internalManager,
            new SchedulerOptionsBuilder(), Substitute.For<ITickerQFailureNotifier>(),
            _ => null);
        var context = CreateContext(ct: (_, _, _) =>
        {
            delegateCalls++;
            return Task.CompletedTask;
        });
        context.Retries = 3;
        context.RequestContractVersion = 2;
        context.RequestContractFingerprint = "persisted-fingerprint";

        await handler.ExecuteTaskAsync(context, isDue: false);

        Assert.Equal(0, delegateCalls);
        Assert.Equal(0, context.RetryCount);
        Assert.Equal(TickerStatus.Failed, context.Status);
        Assert.Contains("contract drift", context.ExceptionDetails, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("descriptor", context.ExceptionDetails, StringComparison.OrdinalIgnoreCase);
        await _internalManager.Received(1).UpdateTickerAsync(context, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteTaskAsync_ExactContractIdentity_ExecutesDelegate()
    {
        var delegateCalls = 0;
        var descriptor = new TickerFunctionDescriptor("TestFunction", contractVersion: 2);
        var handler = new TickerExecutionTaskHandler(
            _serviceProvider, _clock, _instrumentation, _internalManager,
            new SchedulerOptionsBuilder(), Substitute.For<ITickerQFailureNotifier>(),
            _ => descriptor);
        var context = CreateContext(ct: (_, _, _) =>
        {
            delegateCalls++;
            return Task.CompletedTask;
        });
        context.RequestContractVersion = 2;

        await handler.ExecuteTaskAsync(context, isDue: false);

        Assert.Equal(1, delegateCalls);
        Assert.Equal(TickerStatus.Done, context.Status);
    }

    [Fact]
    public async Task ExecuteTaskAsync_FingerprintMismatchWithSameVersion_FailsBeforeDelegate()
    {
        var delegateCalls = 0;
        var contract = new TickerRequestContract(
            "Test.Request",
            TickerRequestContractConstants.DefaultMediaType,
            required: true,
            TickerRequestContractConstants.SchemaDialect2020_12,
            "{\"type\":\"object\",\"additionalProperties\":false}");
        var descriptor = new TickerFunctionDescriptor("TestFunction", contractVersion: 2, request: contract);
        var handler = new TickerExecutionTaskHandler(
            _serviceProvider, _clock, _instrumentation, _internalManager,
            new SchedulerOptionsBuilder(), Substitute.For<ITickerQFailureNotifier>(),
            _ => descriptor);
        var context = CreateContext(ct: (_, _, _) =>
        {
            delegateCalls++;
            return Task.CompletedTask;
        });
        context.RequestContractVersion = 2;
        context.RequestContractFingerprint = "stale-fingerprint";

        await handler.ExecuteTaskAsync(context, isDue: false);

        Assert.Equal(0, delegateCalls);
        Assert.Equal(TickerStatus.Failed, context.Status);
        Assert.Contains("fingerprint changed=True", context.ExceptionDetails, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteTaskAsync_SetsStatusFailed_WhenDelegateThrows()
    {
        var context = CreateContext(ct: (_, _, _) => throw new InvalidOperationException("boom"));

        await _handler.ExecuteTaskAsync(context, isDue: false);

        Assert.Equal(TickerStatus.Failed, context.Status);
    }

    [Fact]
    public async Task ExecuteTaskAsync_RecordsExceptionDetails_WhenDelegateThrows()
    {
        var context = CreateContext(ct: (_, _, _) => throw new InvalidOperationException("boom"));

        await _handler.ExecuteTaskAsync(context, isDue: false);

        Assert.False(string.IsNullOrWhiteSpace(context.ExceptionDetails));
        Assert.Contains("boom", context.ExceptionDetails);
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
        var handler = new TickerExecutionTaskHandler(sp, _clock, _instrumentation, _internalManager, new SchedulerOptionsBuilder(), Substitute.For<ITickerQFailureNotifier>());

        var context = CreateContext(ct: (_, _, _) => throw new InvalidOperationException("boom"));

        await handler.ExecuteTaskAsync(context, isDue: false);

        await exceptionHandler.Received(1).HandleExceptionAsync(
            Arg.Any<Exception>(),
            Arg.Is(context.TickerId),
            Arg.Is(context.Type));
    }

    [Fact]
    public async Task ExecuteTaskAsync_CallsExceptionHandler_ForEachFailedAttempt()
    {
        var exceptionHandler = Substitute.For<ITickerExceptionHandler>();
        var services = new ServiceCollection();
        services.AddSingleton(_internalManager);
        services.AddSingleton(_instrumentation);
        services.AddSingleton(exceptionHandler);
        var sp = services.BuildServiceProvider();
        var handler = new TickerExecutionTaskHandler(sp, _clock, _instrumentation, _internalManager, new SchedulerOptionsBuilder(), Substitute.For<ITickerQFailureNotifier>());

        var context = CreateContext(ct: (_, _, _) => throw new InvalidOperationException("boom"));
        context.Retries = 2;
        context.RetryIntervals = [0, 0];

        await handler.ExecuteTaskAsync(context, isDue: false);

        await exceptionHandler.Received(3).HandleExceptionAsync(
            Arg.Any<Exception>(),
            Arg.Is(context.TickerId),
            Arg.Is(context.Type));
    }

    [Fact]
    public async Task ExecuteTaskAsync_QualifiedRemoteDelegate_DoesNotApplyCoreRetries()
    {
        var calls = 0;
        var context = CreateContext(ct: (_, _, _) =>
        {
            calls++;
            throw new InvalidOperationException("remote final failure");
        });
        context.FunctionName = "RemoteJob@node-a";
        context.Retries = 3;
        context.RetryIntervals = [0, 0, 0];

        await _handler.ExecuteTaskAsync(context, isDue: false);

        Assert.Equal(1, calls);
        Assert.Equal(TickerStatus.Failed, context.Status);
        Assert.Equal(0, context.RetryCount);
    }

    [Fact]
    public async Task ExecuteTaskAsync_UpdatesExceptionDetails_OnFailedAttemptBeforeRetriesComplete()
    {
        var context = CreateContext(ct: (_, _, _) => throw new InvalidOperationException("boom"));
        context.Retries = 1;
        context.RetryIntervals = [0];
        var observedUpdates = new List<(TickerStatus Status, string ExceptionDetails)>();

        _internalManager
            .When(x => x.UpdateTickerAsync(Arg.Any<InternalFunctionContext>(), Arg.Any<CancellationToken>()))
            .Do(callInfo =>
            {
                var ctx = callInfo.Arg<InternalFunctionContext>();
                observedUpdates.Add((ctx.Status, ctx.ExceptionDetails));
            });

        await _handler.ExecuteTaskAsync(context, isDue: false);

        Assert.Contains(observedUpdates, x =>
            x.Status == TickerStatus.InProgress &&
            !string.IsNullOrWhiteSpace(x.ExceptionDetails));
    }

    #endregion

    #region Cancellation Path

    [Fact]
    public async Task ExecuteTaskAsync_SetsStatusCancelled_WhenTaskCancelledExceptionThrown()
    {
        var context = CreateContext(ct: (_, _, _) => throw new TaskCanceledException("cancelled"));

        await _handler.ExecuteTaskAsync(context, isDue: false);

        Assert.Equal(TickerStatus.Cancelled, context.Status);
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
        var handler = new TickerExecutionTaskHandler(sp, _clock, _instrumentation, _internalManager, new SchedulerOptionsBuilder(), Substitute.For<ITickerQFailureNotifier>());

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

        Assert.Equal(TickerStatus.Skipped, context.Status);
    }

    [Fact]
    public async Task ExecuteTaskAsync_SetsCustomStatus_WhenTerminateExecutionWithStatusThrown()
    {
        var context = CreateContext(ct: (_, _, _) =>
            throw new TerminateExecutionException(TickerStatus.Cancelled, "terminate as cancelled"));

        await _handler.ExecuteTaskAsync(context, isDue: false);

        Assert.Equal(TickerStatus.Cancelled, context.Status);
    }

    [Fact]
    public async Task ExecuteTaskAsync_RecordsExceptionMessage_WhenTerminateExecutionThrown()
    {
        var context = CreateContext(ct: (_, _, _) =>
            throw new TerminateExecutionException("skip reason"));

        await _handler.ExecuteTaskAsync(context, isDue: false);

        Assert.Contains("skip reason", context.ExceptionDetails);
    }

    [Fact]
    public async Task ExecuteTaskAsync_RecordsInnerExceptionMessage_WhenTerminateExecutionHasInner()
    {
        var inner = new InvalidOperationException("inner reason");
        var context = CreateContext(ct: (_, _, _) =>
            throw new TerminateExecutionException("outer", inner));

        await _handler.ExecuteTaskAsync(context, isDue: false);

        Assert.Contains("inner reason", context.ExceptionDetails);
    }

    #endregion

    #region Null CachedDelegate

    [Fact]
    public async Task ExecuteTaskAsync_SetsStatusFailed_WhenCachedDelegateIsNull()
    {
        var context = CreateContext(ct: null);

        await _handler.ExecuteTaskAsync(context, isDue: false);

        Assert.Equal(TickerStatus.Failed, context.Status);
        Assert.Contains("was not found", context.ExceptionDetails);
    }

    #endregion

    #region Parent-Child Execution (TimeTicker)

    [Fact]
    public async Task ExecuteTaskAsync_ChildPersistsInProgressBeforeTerminalState()
    {
        var parentContext = CreateContext(
            type: TickerType.TimeTicker,
            ct: (_, _, _) => Task.CompletedTask);
        parentContext.AcquisitionToken = Guid.NewGuid();
        var childContext = CreateContext(
            type: TickerType.TimeTicker,
            ct: (_, _, _) => Task.CompletedTask);
        childContext.ParentId = parentContext.TickerId;
        childContext.RunCondition = RunCondition.OnSuccess;
        parentContext.TimeTickerChildren.Add(childContext);

        var childStatuses = new List<TickerStatus>();
        _internalManager.UpdateTickerAsync(
                Arg.Is<InternalFunctionContext>(candidate => candidate.TickerId == childContext.TickerId),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                childStatuses.Add(call.ArgAt<InternalFunctionContext>(0).Status);
                return Task.CompletedTask;
            });

        await _handler.ExecuteTaskAsync(parentContext, isDue: false);

        Assert.Equal([TickerStatus.InProgress, TickerStatus.Done], childStatuses);
    }

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

        Assert.True(childExecuted);
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

        Assert.True(childExecuted);
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

        Assert.False(childExecuted);
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

        Assert.True(childExecuted);
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

        Assert.False(childExecuted);
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

        Assert.True(childExecutedOnSuccess);
        Assert.True(childExecutedOnFail);
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

        Assert.True(childExecuted);
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

        await _internalManager.Received().UpdateSkipTimeTickersWithUnifiedContextAsync(
            Arg.Is<InternalFunctionContext[]>(resources =>
                resources.Length == 2 &&
                resources.All(resource => resource.ChainRootId == parentContext.TickerId)),
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

        Assert.True(executed);
        Assert.Equal(TickerStatus.Done, context.Status);
    }

    #endregion

    #region Instrumentation

    [Fact]
    public async Task NonCooperativeTimeout_RemainsTrackedScopedAndUnpersisted_UntilDelegateActuallyExits()
    {
        var manager = Substitute.For<IInternalTickerManager>();
        var instrumentation = Substitute.For<ITickerQInstrumentation>();
        var timeoutPending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        instrumentation.When(x => x.LogJobTimeoutPending(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<TimeSpan>()))
            .Do(_ => timeoutPending.TrySetResult());

        var services = new ServiceCollection();
        services.AddSingleton(manager);
        services.AddSingleton(instrumentation);
        services.AddScoped<ScopedProbe>();
        await using var serviceProvider = services.BuildServiceProvider();
        var options = new SchedulerOptionsBuilder
        {
            DefaultExecutionTimeout = TimeSpan.FromMilliseconds(30),
            TimeoutGracePeriod = TimeSpan.FromMilliseconds(30)
        };
        var handler = new TickerExecutionTaskHandler(
            serviceProvider, _clock, instrumentation, manager, options,
            Substitute.For<ITickerQFailureNotifier>());

        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ScopedProbe capturedProbe = null;
        var context = CreateContext(type: TickerType.TimeTicker, ct: async (_, sp, _) =>
        {
            capturedProbe = sp.GetRequiredService<ScopedProbe>();
            capturedProbe.ThrowIfDisposed();
            await release.Task; // deliberately ignores cancellation
            capturedProbe.ThrowIfDisposed();
        });
        context.AcquisitionToken = Guid.NewGuid();

        var execution = handler.ExecuteTaskAsync(context, isDue: false);
        await timeoutPending.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(execution.IsCompleted);
        Assert.NotNull(capturedProbe);
        Assert.False(capturedProbe.IsDisposed);
        Assert.Equal(1, TickerCancellationTokenManager.ActiveCount);
        var timeLeases = new List<AcquisitionLease>();
        var cronLeases = new List<AcquisitionLease>();
        TickerCancellationTokenManager.SnapshotRunningForLeaseRenewal(timeLeases, cronLeases);
        Assert.Contains(timeLeases, lease =>
            lease.TickerId == context.TickerId && lease.AcquisitionToken == context.AcquisitionToken);
        await manager.DidNotReceive().UpdateTickerAsync(
            Arg.Any<InternalFunctionContext>(), Arg.Any<CancellationToken>());

        release.SetResult();
        await execution.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(capturedProbe.IsDisposed);
        Assert.Equal(TickerStatus.Cancelled, context.Status);
        Assert.Equal(0, context.RetryCount);
        Assert.Equal(0, TickerCancellationTokenManager.ActiveCount);
        await manager.Received(1).UpdateTickerAsync(
            Arg.Is<InternalFunctionContext>(candidate =>
                candidate.TickerId == context.TickerId && candidate.Status == TickerStatus.Cancelled),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FailureNotifierThrows_TerminalFailurePersistenceStillOccursExactlyOnce()
    {
        var manager = Substitute.For<IInternalTickerManager>();
        var terminalWrites = 0;
        manager.When(x => x.UpdateTickerAsync(
                Arg.Any<InternalFunctionContext>(), Arg.Any<CancellationToken>()))
            .Do(call =>
            {
                if (call.ArgAt<InternalFunctionContext>(0).Status == TickerStatus.Failed)
                    terminalWrites++;
            });
        var instrumentation = Substitute.For<ITickerQInstrumentation>();
        var notifier = Substitute.For<ITickerQFailureNotifier>();
        notifier.When(x => x.Notify(Arg.Any<TickerFailureEvent>()))
            .Do(_ => throw new InvalidOperationException("notifier failed"));
        var services = new ServiceCollection();
        services.AddSingleton(manager);
        services.AddSingleton(instrumentation);
        await using var serviceProvider = services.BuildServiceProvider();
        var handler = new TickerExecutionTaskHandler(
            serviceProvider, _clock, instrumentation, manager,
            new SchedulerOptionsBuilder(), notifier);
        var context = CreateContext(ct: (_, _, _) =>
            throw new InvalidOperationException("delegate failed"));

        await handler.ExecuteTaskAsync(context, isDue: false);

        Assert.Equal(TickerStatus.Failed, context.Status);
        Assert.Equal(1, terminalWrites);
    }

    [Fact]
    public async Task FaultAfterTimeoutWithinGrace_RemainsAuthoritativeTimeoutAndDoesNotRetry()
    {
        var manager = Substitute.For<IInternalTickerManager>();
        var instrumentation = Substitute.For<ITickerQInstrumentation>();
        var services = new ServiceCollection();
        services.AddSingleton(manager);
        services.AddSingleton(instrumentation);
        await using var serviceProvider = services.BuildServiceProvider();
        var handler = new TickerExecutionTaskHandler(
            serviceProvider, _clock, instrumentation, manager,
            new SchedulerOptionsBuilder
            {
                DefaultExecutionTimeout = TimeSpan.FromMilliseconds(20),
                TimeoutGracePeriod = TimeSpan.FromMilliseconds(200),
            }, Substitute.For<ITickerQFailureNotifier>());
        var attempts = 0;
        var context = CreateContext(ct: async (_, _, _) =>
        {
            attempts++;
            await Task.Delay(60);
            throw new InvalidOperationException("fault after deadline");
        });
        context.Retries = 1;
        context.RetryIntervals = [0];

        await handler.ExecuteTaskAsync(context, isDue: false);

        Assert.Equal(1, attempts);
        Assert.Equal(TickerStatus.Cancelled, context.Status);
        Assert.Contains("Exceeded execution timeout", context.ExceptionDetails);
        await manager.Received(1).UpdateTickerAsync(
            Arg.Is<InternalFunctionContext>(x => x.Status == TickerStatus.Cancelled),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TimeoutHooksThrow_TerminalPersistenceStillOccurs()
    {
        var manager = Substitute.For<IInternalTickerManager>();
        var instrumentation = Substitute.For<ITickerQInstrumentation>();
        var exceptionHandler = Substitute.For<ITickerExceptionHandler>();
        exceptionHandler.HandleCanceledExceptionAsync(
                Arg.Any<Exception>(), Arg.Any<Guid>(), Arg.Any<TickerType>())
            .Returns<Task>(_ => throw new InvalidOperationException("hook failed"));
        var notifier = Substitute.For<ITickerQFailureNotifier>();
        notifier.When(x => x.Notify(Arg.Any<TickerFailureEvent>()))
            .Do(_ => throw new InvalidOperationException("notifier failed"));
        var services = new ServiceCollection();
        services.AddSingleton(manager);
        services.AddSingleton(instrumentation);
        services.AddSingleton(exceptionHandler);
        await using var serviceProvider = services.BuildServiceProvider();
        var handler = new TickerExecutionTaskHandler(
            serviceProvider, _clock, instrumentation, manager,
            new SchedulerOptionsBuilder
            {
                DefaultExecutionTimeout = TimeSpan.FromMilliseconds(20),
                TimeoutGracePeriod = TimeSpan.FromMilliseconds(50),
            }, notifier);
        var context = CreateContext(ct: async (token, _, _) =>
            await Task.Delay(Timeout.InfiniteTimeSpan, token));

        await handler.ExecuteTaskAsync(context, isDue: false);

        Assert.Equal(TickerStatus.Cancelled, context.Status);
        await manager.Received(1).UpdateTickerAsync(
            Arg.Is<InternalFunctionContext>(x => x.Status == TickerStatus.Cancelled),
            Arg.Any<CancellationToken>());
    }

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
            ChainGeneration = type == TickerType.TimeTicker ? Guid.NewGuid() : null,
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
