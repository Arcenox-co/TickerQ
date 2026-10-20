using System.Diagnostics;
using NSubstitute;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;
using Microsoft.Extensions.DependencyInjection;
using TickerQ.Utilities.Instrumentation;
using TickerQ.Utilities;
using TickerQ.Utilities.Models;

namespace TickerQ.Tests;

[Collection("TickerCancellationTokenState")]
public class RetryBehaviorTests : IDisposable
{
    public void Dispose()
    {
        TickerCancellationTokenManager.CleanUpTickerCancellationTokens();
    }

    // End-to-end unit tests that call the public ExecuteTaskAsync with a CronTickerOccurrence
    // so RunContextFunctionAsync + retry logic is exercised. Tests use short intervals (1..3s).

    [Fact]
    public async Task ExecuteTaskAsync_CronTickerOccurrence_AppliesRetryIntervals_AndUpdatesRetryCount()
    {
        // Arrange: cron occurrence -> RunContextFunctionAsync path
        // Use three distinct short intervals so we can verify mapping without overly long waits
        var (handler, context, _, attempts, instrumentation) = SetupRetryTestFixture([1, 2, 3], retries: 3);

        // Act
        await handler.ExecuteTaskAsync(context, isDue: true);

        // Assert - initial + 3 retries = 4 attempts
        Assert.Equal(4, attempts.Count);
        for (int i = 0; i < 4; i++)
            Assert.Equal(i, attempts[i].RetryCount);

        AssertScheduledIntervals(instrumentation, context, [1, 2, 3]);
        Assert.Equal(TickerStatus.Failed, context.Status);
        Assert.Equal(3, context.RetryCount);

        // A retry must not start early. Monotonic time avoids wall-clock corrections,
        // and no upper bound is imposed on how promptly the OS schedules a due timer.
        // Exact interval selection is checked independently through retry instrumentation.
        for (var i = 1; i < attempts.Count; i++)
        {
            var elapsed = Stopwatch.GetElapsedTime(attempts[i - 1].Timestamp, attempts[i].Timestamp);
            var minimum = TimeSpan.FromSeconds(context.RetryIntervals[i - 1]) - TimeSpan.FromMilliseconds(20);
            Assert.True(elapsed >= minimum, $"Retry {i} started after {elapsed}, before its configured interval {minimum}.");
        }
    }

    [Fact]
    public async Task ExecuteTaskAsync_CronTickerOccurrence_UsesLastInterval_WhenRetriesExceedArrayLength()
    {
        // Distinct values detect an accidental fallback to the first interval.
        var (handler, context, _, attempts, instrumentation) = SetupRetryTestFixture([1, 0], retries: 4);

        await handler.ExecuteTaskAsync(context, isDue: true);

        // initial + 4 retries = 5 attempts
        Assert.Equal(5, attempts.Count);
        AssertScheduledIntervals(instrumentation, context, [1, 0, 0, 0]);

        // Ensure attempts remain ordered even if the wall clock is corrected during execution.
        var timestamps = attempts.Select(a => a.Timestamp).ToList();
        for (int i = 1; i < timestamps.Count; i++)
            Assert.True(timestamps[i] >= timestamps[i - 1]);
    }

    [Fact]
    public async Task ExecuteTaskAsync_CronTickerOccurrence_StopsRetrying_WhenFunctionSucceeds()
    {
        // Arrange: succeed on RetryCount==2
        // Use zero intervals for speed; succeed at retry=2
        var (handler, context, _, attempts, instrumentation) = SetupRetryTestFixture([0, 0, 0, 0], retries: 4, succeedOnRetryCount: 2);

        await handler.ExecuteTaskAsync(context, isDue: true);

        // Should stop after success on attempt with RetryCount=2 => initial + retry1 + retry2 = 3 attempts
        Assert.Equal(3, attempts.Count);
        Assert.Equal(2, attempts.Last().RetryCount);
        AssertScheduledIntervals(instrumentation, context, [0, 0]);
        Assert.Equal(TickerStatus.DueDone, context.Status);
    }

    [Fact]
    public async Task ExecuteTaskAsync_CronTickerOccurrence_CancellationInterruptsRetryWaitWithoutAnotherAttempt()
    {
        const int retrySeconds = 3600;
        var (handler, context, manager, attempts, instrumentation) = SetupRetryTestFixture([retrySeconds], retries: 3);
        var retryScheduled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        instrumentation.When(x => x.LogJobRetryScheduled(context.TickerId, context.FunctionName, 1, 3, retrySeconds))
            .Do(_ => retryScheduled.TrySetResult());
        var persistedStatuses = new List<TickerStatus>();
        manager.UpdateTickerAsync(context, Arg.Any<CancellationToken>()).Returns(_ =>
        {
            persistedStatuses.Add(context.Status);
            return Task.CompletedTask;
        });
        using var cancellation = new CancellationTokenSource();
        var execution = handler.ExecuteTaskAsync(context, isDue: true, cancellation.Token);
        try
        {
            await retryScheduled.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(execution.IsCompleted);
        }
        finally { cancellation.Cancel(); }

        // This is a deadlock guard well below the configured wait, not a timer-precision assertion.
        await execution.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Single(attempts);
        Assert.Equal(0, attempts[0].RetryCount);
        Assert.Equal(TickerStatus.Cancelled, context.Status);
        Assert.Equal(TickerStatus.Cancelled, persistedStatuses[^1]);
        AssertScheduledIntervals(instrumentation, context, [retrySeconds]);
        await manager.Received().UpdateTickerAsync(context, CancellationToken.None);
    }

    private static void AssertScheduledIntervals(ITickerQInstrumentation instrumentation,
        InternalFunctionContext context, int[] expected)
    {
        instrumentation.Received(expected.Length).LogJobRetryScheduled(
            context.TickerId, context.FunctionName, Arg.Any<int>(), context.Retries, Arg.Any<int>());
        for (var i = 0; i < expected.Length; i++)
            instrumentation.Received(1).LogJobRetryScheduled(
                context.TickerId, context.FunctionName, i + 1, context.Retries, expected[i]);
    }

    private record Attempt(long Timestamp, int RetryCount);

    // Helpers
    private static (TickerExecutionTaskHandler handler, InternalFunctionContext context, IInternalTickerManager manager,
        List<Attempt> attempts, ITickerQInstrumentation instrumentation) SetupRetryTestFixture(
        int[] retryIntervals,
        int retries,
        int? succeedOnRetryCount = null)
    {
        var services = new ServiceCollection();
        var clock = Substitute.For<ITickerClock>();
        var internalManager = Substitute.For<IInternalTickerManager>();
        var instrumentation = Substitute.For<ITickerQInstrumentation>();

        clock.UtcNow.Returns(DateTime.UtcNow);

        services.AddSingleton(internalManager);
        services.AddSingleton(instrumentation);
        var serviceProvider = services.BuildServiceProvider();

        var handler = new TickerExecutionTaskHandler(serviceProvider, clock, instrumentation, internalManager, new SchedulerOptionsBuilder(), Substitute.For<ITickerQFailureNotifier>(), LicenseTestState.Active(clock), new TickerFunctionConcurrencyGate());

        var attempts = new List<Attempt>();

        var context = new InternalFunctionContext
        {
            TickerId = Guid.NewGuid(),
            FunctionName = "TestFunction",
            Type = TickerType.CronTickerOccurrence,
            ExecutionTime = DateTime.UtcNow,
            RetryIntervals = retryIntervals,
            Retries = retries,
            RetryCount = 0,
            Status = TickerStatus.Idle,
            CachedDelegate = (ct, sp, tctx) =>
            {
                attempts.Add(new Attempt(Stopwatch.GetTimestamp(), tctx.RetryCount));

                if (succeedOnRetryCount.HasValue && tctx.RetryCount >= succeedOnRetryCount.Value)
                    return Task.CompletedTask;

                throw new InvalidOperationException("Fail for retry test");
            }
        };

        return (handler, context, internalManager, attempts, instrumentation);
    }
}
