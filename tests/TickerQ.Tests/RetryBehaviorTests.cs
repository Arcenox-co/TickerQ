using FluentAssertions;
using NSubstitute;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;
using Microsoft.Extensions.DependencyInjection;
using TickerQ.Utilities.Instrumentation;
using TickerQ.Utilities.Models;

namespace TickerQ.Tests;

public class RetryBehaviorTests
{
    // End-to-end unit tests that call the public ExecuteTaskAsync with a CronTickerOccurrence
    // so RunContextFunctionAsync + retry logic is exercised. Tests use short intervals (1..3s).

    [Fact()]
    public async Task ExecuteTaskAsync_CronTickerOccurrence_AppliesRetryIntervals_AndUpdatesRetryCount()
    {
        // Arrange: cron occurrence -> RunContextFunctionAsync path
        // Use three distinct short intervals so we can verify mapping without overly long waits
        var (handler, context, _, attempts) = SetupRetryTestFixture([1, 2, 3], retries: 3);

        // Act
        await handler.ExecuteTaskAsync(context, isDue: true);

        // Assert - initial + 3 retries = 4 attempts
        attempts.Should().HaveCount(4);
        for (int i = 0; i < 4; i++)
            attempts[i].RetryCount.Should().Be(i);

        // Verify mapped retry intervals produced the expected spacing between attempts
        var timeDiffs = new[]
        {
            (attempts[1].Timestamp - attempts[0].Timestamp).TotalSeconds,
            (attempts[2].Timestamp - attempts[1].Timestamp).TotalSeconds,
            (attempts[3].Timestamp - attempts[2].Timestamp).TotalSeconds,
        };

        // allow a small tolerance for timing, but ensure each spacing reflects the configured intervals
        timeDiffs[0].Should().BeInRange(0.8, 1.2); // first retry uses ~1s
        timeDiffs[1].Should().BeInRange(1.8, 2.2); // second retry uses ~2s
        timeDiffs[2].Should().BeInRange(2.8, 3.2); // third retry uses ~3s
    }

    [Fact]
    public async Task ExecuteTaskAsync_CronTickerOccurrence_UsesLastInterval_WhenRetriesExceedArrayLength()
    {
        // Use zero intervals for speed
        var (handler, context, _, attempts) = SetupRetryTestFixture([0, 0], retries: 4);

        await handler.ExecuteTaskAsync(context, isDue: true);

        // initial + 4 retries = 5 attempts
        attempts.Should().HaveCount(5);

        // Ensure we captured attempts and they happened in order. Timing is intentionally tiny.
        attempts.Select(a => a.Timestamp).Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task ExecuteTaskAsync_CronTickerOccurrence_StopsRetrying_WhenFunctionSucceeds()
    {
        // Arrange: succeed on RetryCount==2
        // Use zero intervals for speed; succeed at retry=2
        var (handler, context, _, attempts) = SetupRetryTestFixture([0, 0, 0, 0], retries: 4, succeedOnRetryCount: 2);

        await handler.ExecuteTaskAsync(context, isDue: true);

        // Should stop after success on attempt with RetryCount=2 => initial + retry1 + retry2 = 3 attempts
        attempts.Should().HaveCount(3);
        attempts.Last().RetryCount.Should().Be(2);
    }

    private record Attempt(DateTime Timestamp, int RetryCount);

    // Helpers
    private static (TickerExecutionTaskHandler handler, InternalFunctionContext context, IInternalTickerManager manager, List<Attempt> attempts) SetupRetryTestFixture(
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

        var handler = new TickerExecutionTaskHandler(serviceProvider, clock, instrumentation, internalManager);

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
                attempts.Add(new Attempt(DateTime.UtcNow, tctx.RetryCount));

                if (succeedOnRetryCount.HasValue && tctx.RetryCount >= succeedOnRetryCount.Value)
                    return Task.CompletedTask;

                throw new InvalidOperationException("Fail for retry test");
            }
        };

        return (handler, context, internalManager, attempts);
    }
}
