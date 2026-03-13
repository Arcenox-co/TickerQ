using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Instrumentation;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities;
using TickerQ.Utilities.Models;

namespace TickerQ.Tests;

public class RetryExhaustionTests
{
    private readonly ITickerClock _clock;
    private readonly IInternalTickerManager _internalManager;
    private readonly ITickerQInstrumentation _instrumentation;
    private readonly ServiceProvider _serviceProvider;
    private readonly TickerExecutionTaskHandler _handler;

    public RetryExhaustionTests()
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

    #region All retries exhausted - status becomes Failed

    [Fact]
    public async Task ExecuteTaskAsync_AllRetriesExhausted_StatusBecomesFailed()
    {
        // Arrange: Retries=2, function always throws => initial + 2 retries = 3 attempts, all fail
        var context = CreateContext(
            ct: (_, _, _) => throw new InvalidOperationException("always fails"),
            retries: 2,
            retryIntervals: [0, 0]);

        // Act
        await _handler.ExecuteTaskAsync(context, isDue: false);

        // Assert: final status must be Failed
        Assert.Equal(TickerStatus.Failed, context.Status);
    }

    [Fact]
    public async Task ExecuteTaskAsync_AllRetriesExhausted_UpdateTickerCalledWithFailedStatus()
    {
        // Arrange
        var context = CreateContext(
            ct: (_, _, _) => throw new InvalidOperationException("always fails"),
            retries: 2,
            retryIntervals: [0, 0]);

        var observedStatuses = new List<TickerStatus>();
        _internalManager
            .When(x => x.UpdateTickerAsync(Arg.Any<InternalFunctionContext>(), Arg.Any<CancellationToken>()))
            .Do(ci => observedStatuses.Add(ci.Arg<InternalFunctionContext>().Status));

        // Act
        await _handler.ExecuteTaskAsync(context, isDue: false);

        // Assert: the final UpdateTickerAsync call should have Failed status
        Assert.Contains(TickerStatus.Failed, observedStatuses);
        Assert.Equal(TickerStatus.Failed, observedStatuses[^1]);
    }

    #endregion

    #region Exception handler called on each attempt

    [Fact]
    public async Task ExecuteTaskAsync_ExceptionHandlerCalledOnEachAttempt_Retries2_CalledExactly3Times()
    {
        // Arrange: register ITickerExceptionHandler
        var exceptionHandler = Substitute.For<ITickerExceptionHandler>();
        var services = new ServiceCollection();
        services.AddSingleton(_internalManager);
        services.AddSingleton(_instrumentation);
        services.AddSingleton(exceptionHandler);
        var sp = services.BuildServiceProvider();
        var handler = new TickerExecutionTaskHandler(sp, _clock, _instrumentation, _internalManager);

        var context = CreateContext(
            ct: (_, _, _) => throw new InvalidOperationException("boom"),
            retries: 2,
            retryIntervals: [0, 0]);

        // Act
        await handler.ExecuteTaskAsync(context, isDue: false);

        // Assert: initial attempt + 2 retries = 3 calls to HandleExceptionAsync
        await exceptionHandler.Received(3).HandleExceptionAsync(
            Arg.Any<Exception>(),
            Arg.Is(context.TickerId),
            Arg.Is(context.Type));
    }

    [Fact]
    public async Task ExecuteTaskAsync_ExceptionHandlerCalledOnEachAttempt_Retries0_CalledExactly1Time()
    {
        // Arrange
        var exceptionHandler = Substitute.For<ITickerExceptionHandler>();
        var services = new ServiceCollection();
        services.AddSingleton(_internalManager);
        services.AddSingleton(_instrumentation);
        services.AddSingleton(exceptionHandler);
        var sp = services.BuildServiceProvider();
        var handler = new TickerExecutionTaskHandler(sp, _clock, _instrumentation, _internalManager);

        var context = CreateContext(
            ct: (_, _, _) => throw new InvalidOperationException("boom"),
            retries: 0,
            retryIntervals: []);

        // Act
        await handler.ExecuteTaskAsync(context, isDue: false);

        // Assert: only 1 attempt, so exactly 1 call
        await exceptionHandler.Received(1).HandleExceptionAsync(
            Arg.Any<Exception>(),
            Arg.Is(context.TickerId),
            Arg.Is(context.Type));
    }

    #endregion

    #region Exception details serialized after final failure

    [Fact]
    public async Task ExecuteTaskAsync_AfterRetryExhaustion_ExceptionDetailsIsNonNull()
    {
        // Arrange
        var context = CreateContext(
            ct: (_, _, _) => throw new InvalidOperationException("serialized error"),
            retries: 2,
            retryIntervals: [0, 0]);

        // Act
        await _handler.ExecuteTaskAsync(context, isDue: false);

        // Assert: ExceptionDetails should be set and contain the message
        Assert.NotNull(context.ExceptionDetails);
        Assert.False(string.IsNullOrWhiteSpace(context.ExceptionDetails));
        Assert.Contains("serialized error", context.ExceptionDetails);
    }

    [Fact]
    public async Task ExecuteTaskAsync_AfterRetryExhaustion_FinalUpdateContainsExceptionDetails()
    {
        // Arrange
        var context = CreateContext(
            ct: (_, _, _) => throw new InvalidOperationException("detailed error"),
            retries: 1,
            retryIntervals: [0]);

        string? lastExceptionDetails = null;
        _internalManager
            .When(x => x.UpdateTickerAsync(Arg.Any<InternalFunctionContext>(), Arg.Any<CancellationToken>()))
            .Do(ci =>
            {
                var ctx = ci.Arg<InternalFunctionContext>();
                if (ctx.Status == TickerStatus.Failed)
                    lastExceptionDetails = ctx.ExceptionDetails;
            });

        // Act
        await _handler.ExecuteTaskAsync(context, isDue: false);

        // Assert
        Assert.NotNull(lastExceptionDetails);
        Assert.Contains("detailed error", lastExceptionDetails);
    }

    #endregion

    #region Zero retries - single attempt then Failed

    [Fact]
    public async Task ExecuteTaskAsync_ZeroRetries_SingleAttempt_StatusBecomesFailed()
    {
        // Arrange: Retries=0, only the initial attempt
        var attemptCount = 0;
        var context = CreateContext(
            ct: (_, _, _) =>
            {
                attemptCount++;
                throw new InvalidOperationException("fail immediately");
            },
            retries: 0,
            retryIntervals: []);

        // Act
        await _handler.ExecuteTaskAsync(context, isDue: false);

        // Assert
        Assert.Equal(1, attemptCount);
        Assert.Equal(TickerStatus.Failed, context.Status);
    }

    [Fact]
    public async Task ExecuteTaskAsync_ZeroRetries_ExceptionDetailsRecorded()
    {
        // Arrange
        var context = CreateContext(
            ct: (_, _, _) => throw new InvalidOperationException("single shot failure"),
            retries: 0,
            retryIntervals: []);

        // Act
        await _handler.ExecuteTaskAsync(context, isDue: false);

        // Assert
        Assert.Equal(TickerStatus.Failed, context.Status);
        Assert.NotNull(context.ExceptionDetails);
        Assert.Contains("single shot failure", context.ExceptionDetails);
    }

    #endregion

    #region Retry succeeds on second attempt - no exhaustion

    [Fact]
    public async Task ExecuteTaskAsync_RetrySucceedsOnSecondAttempt_StatusBecomesDone()
    {
        // Arrange: Retries=2, fail first time, succeed second time
        var attemptCount = 0;
        var context = CreateContext(
            ct: (_, _, _) =>
            {
                attemptCount++;
                if (attemptCount == 1)
                    throw new InvalidOperationException("first attempt fails");
                return Task.CompletedTask;
            },
            retries: 2,
            retryIntervals: [0, 0]);

        // Act
        await _handler.ExecuteTaskAsync(context, isDue: false);

        // Assert: should succeed on second attempt (retry #1)
        Assert.Equal(2, attemptCount);
        Assert.Equal(TickerStatus.Done, context.Status);
    }

    [Fact]
    public async Task ExecuteTaskAsync_RetrySucceedsOnSecondAttempt_IsDue_StatusBecomesDueDone()
    {
        // Arrange
        var attemptCount = 0;
        var context = CreateContext(
            ct: (_, _, _) =>
            {
                attemptCount++;
                if (attemptCount == 1)
                    throw new InvalidOperationException("first attempt fails");
                return Task.CompletedTask;
            },
            retries: 2,
            retryIntervals: [0, 0]);

        // Act
        await _handler.ExecuteTaskAsync(context, isDue: true);

        // Assert
        Assert.Equal(2, attemptCount);
        Assert.Equal(TickerStatus.DueDone, context.Status);
    }

    [Fact]
    public async Task ExecuteTaskAsync_RetrySucceedsOnSecondAttempt_ExceptionHandlerCalledOnlyOnce()
    {
        // Arrange: exception handler should be called only for the failed first attempt
        var exceptionHandler = Substitute.For<ITickerExceptionHandler>();
        var services = new ServiceCollection();
        services.AddSingleton(_internalManager);
        services.AddSingleton(_instrumentation);
        services.AddSingleton(exceptionHandler);
        var sp = services.BuildServiceProvider();
        var handler = new TickerExecutionTaskHandler(sp, _clock, _instrumentation, _internalManager);

        var attemptCount = 0;
        var context = CreateContext(
            ct: (_, _, _) =>
            {
                attemptCount++;
                if (attemptCount == 1)
                    throw new InvalidOperationException("first attempt fails");
                return Task.CompletedTask;
            },
            retries: 2,
            retryIntervals: [0, 0]);

        // Act
        await handler.ExecuteTaskAsync(context, isDue: false);

        // Assert: only 1 failure, so HandleExceptionAsync called once
        await exceptionHandler.Received(1).HandleExceptionAsync(
            Arg.Any<Exception>(),
            Arg.Is(context.TickerId),
            Arg.Is(context.Type));
    }

    #endregion

    #region Cancellation during retry wait

    [Fact]
    public async Task ExecuteTaskAsync_CancellationDuringRetryWait_StatusBecomesCancelled()
    {
        // Arrange: Retries=2, first attempt fails, then cancellation fires during the retry delay.
        // We use a real (non-zero) retry interval so that Task.Delay is actually awaited,
        // and cancel the token after the first failure.
        var attemptCount = 0;
        var cts = new CancellationTokenSource();

        var context = CreateContext(
            ct: (_, _, _) =>
            {
                attemptCount++;
                throw new InvalidOperationException("fail to trigger retry");
            },
            retries: 2,
            retryIntervals: [30, 30]); // long intervals - we'll cancel before they elapse

        // Cancel the token after the first attempt's UpdateTickerAsync call
        _internalManager
            .When(x => x.UpdateTickerAsync(Arg.Any<InternalFunctionContext>(), Arg.Any<CancellationToken>()))
            .Do(_ =>
            {
                // Cancel after the first exception update so the retry delay gets cancelled
                if (attemptCount >= 1)
                    cts.Cancel();
            });

        // Act
        await _handler.ExecuteTaskAsync(context, isDue: false, cts.Token);

        // Assert: should have been cancelled, only 1 attempt executed
        Assert.Equal(1, attemptCount);
        Assert.Equal(TickerStatus.Cancelled, context.Status);
    }

    [Fact]
    public async Task ExecuteTaskAsync_CancellationDuringRetryWait_CallsCancelledExceptionHandler()
    {
        // Arrange
        var exceptionHandler = Substitute.For<ITickerExceptionHandler>();
        var internalManager = Substitute.For<IInternalTickerManager>();
        var services = new ServiceCollection();
        services.AddSingleton(internalManager);
        services.AddSingleton(_instrumentation);
        services.AddSingleton(exceptionHandler);
        var sp = services.BuildServiceProvider();
        var handler = new TickerExecutionTaskHandler(sp, _clock, _instrumentation, internalManager);

        var attemptCount = 0;
        var cts = new CancellationTokenSource();

        var context = CreateContext(
            ct: (_, _, _) =>
            {
                attemptCount++;
                throw new InvalidOperationException("fail to trigger retry");
            },
            retries: 2,
            retryIntervals: [30, 30]);

        internalManager
            .When(x => x.UpdateTickerAsync(Arg.Any<InternalFunctionContext>(), Arg.Any<CancellationToken>()))
            .Do(_ =>
            {
                if (attemptCount >= 1)
                    cts.Cancel();
            });

        // Act
        await handler.ExecuteTaskAsync(context, isDue: false, cts.Token);

        // Assert: cancellation during retry delay triggers HandleCanceledExceptionAsync
        await exceptionHandler.Received().HandleCanceledExceptionAsync(
            Arg.Any<Exception>(),
            Arg.Is(context.TickerId),
            Arg.Is(context.Type));
    }

    #endregion

    #region Helpers

    private static InternalFunctionContext CreateContext(
        TickerFunctionDelegate? ct = null,
        int retries = 0,
        int[]? retryIntervals = null,
        TickerType type = TickerType.CronTickerOccurrence)
    {
        return new InternalFunctionContext
        {
            TickerId = Guid.NewGuid(),
            FunctionName = "TestFunction",
            Type = type,
            ExecutionTime = DateTime.UtcNow,
            RetryIntervals = retryIntervals ?? [],
            Retries = retries,
            RetryCount = 0,
            Status = TickerStatus.Idle,
            CachedDelegate = ct,
            TimeTickerChildren = []
        };
    }

    #endregion
}
