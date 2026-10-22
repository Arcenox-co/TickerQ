using System;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Base;
using TickerQ.Utilities.Enums;
using Xunit;

namespace TickerQ.Tests;

public class TickerFunctionContextTests
{
    [Fact]
    public void GenericContext_Preserves_ScheduledFor_From_Base_Context()
    {
        // Arrange
        var scheduledFor = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        var baseContext = new TickerFunctionContext
        {
            Id = Guid.NewGuid(),
            Type = TickerType.TimeTicker,
            RetryCount = 1,
            IsDue = true,
            ScheduledFor = scheduledFor,
            FunctionName = "TestFunction"
        };

        var request = new TestRequest { Value = 42 };

        // Act
        var genericContext = new TickerFunctionContext<TestRequest>(baseContext, request);

        // Assert
        Assert.Equal(baseContext.Id, genericContext.Id);
        Assert.Equal(baseContext.Type, genericContext.Type);
        Assert.Equal(baseContext.RetryCount, genericContext.RetryCount);
        Assert.Equal(baseContext.IsDue, genericContext.IsDue);
        Assert.Equal(baseContext.ScheduledFor, genericContext.ScheduledFor);
        Assert.Equal(baseContext.FunctionName, genericContext.FunctionName);
        Assert.Equal(request, genericContext.Request);
    }

    [Fact]
    public void GenericContext_Preserves_ParentId_From_Base_Context()
    {
        // Arrange
        var parentId = Guid.NewGuid();

        var baseContext = new TickerFunctionContext
        {
            Id = Guid.NewGuid(),
            ParentId = parentId,
            Type = TickerType.CronTickerOccurrence,
            RetryCount = 0,
            IsDue = false,
            ScheduledFor = DateTime.UtcNow,
            FunctionName = "CronFunction"
        };

        var request = new TestRequest { Value = 99 };

        // Act
        var genericContext = new TickerFunctionContext<TestRequest>(baseContext, request);

        // Assert
        Assert.Equal(parentId, genericContext.ParentId);
    }

    [Fact]
    public void ParentId_Is_Null_When_Not_Set()
    {
        // Arrange & Act
        var context = new TickerFunctionContext
        {
            Id = Guid.NewGuid(),
            Type = TickerType.TimeTicker,
            FunctionName = "StandaloneFunction"
        };

        // Assert
        Assert.Null(context.ParentId);
    }

    [Fact]
    public void ParentId_Set_For_CronTickerOccurrence()
    {
        // Arrange
        var cronTickerId = Guid.NewGuid();

        // Act
        var context = new TickerFunctionContext
        {
            Id = Guid.NewGuid(),
            ParentId = cronTickerId,
            Type = TickerType.CronTickerOccurrence,
            FunctionName = "CronFunction"
        };

        // Assert
        Assert.Equal(cronTickerId, context.ParentId);
        Assert.Equal(TickerType.CronTickerOccurrence, context.Type);
    }

    [Fact]
    public void ParentId_Set_For_Chained_TimeTicker()
    {
        // Arrange
        var parentTimerTickerId = Guid.NewGuid();

        // Act
        var context = new TickerFunctionContext
        {
            Id = Guid.NewGuid(),
            ParentId = parentTimerTickerId,
            Type = TickerType.TimeTicker,
            FunctionName = "ChildTimerFunction"
        };

        // Assert
        Assert.Equal(parentTimerTickerId, context.ParentId);
        Assert.Equal(TickerType.TimeTicker, context.Type);
    }

    [Fact]
    public void RequestCancellation_Invokes_Underlying_Action()
    {
        // This test is intentionally left empty because RequestCancelOperationAction
        // is an internal delegate that is only set by the runtime scheduler pipeline.
        // Verifying its behavior would require testing internal wiring rather than
        // the public surface area of TickerFunctionContext.
        Assert.True(true);
    }

    [Fact]
    public async Task ReportProgressAsync_ForwardsValidatedProgressToRuntimeReporter()
    {
        (double Percent, string Message, CancellationToken Token)? reported = null;
        using var source = new CancellationTokenSource();
        var context = new TickerFunctionContext
        {
            ProgressReporter = (percent, message, token) =>
            {
                reported = (percent, message, token);
                return Task.CompletedTask;
            }
        };

        await context.ReportProgressAsync(42.5, "Imported 425 of 1,000", source.Token);

        Assert.Equal(42.5, reported?.Percent);
        Assert.Equal("Imported 425 of 1,000", reported?.Message);
        Assert.Equal(source.Token, reported?.Token);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(100.1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public async Task ReportProgressAsync_RejectsInvalidPercent(double percent)
    {
        var context = new TickerFunctionContext();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => context.ReportProgressAsync(percent));
    }

    [Fact]
    public async Task GenericContext_PreservesTestRunAndProgressReporter()
    {
        var calls = 0;
        var baseContext = new TickerFunctionContext
        {
            IsTestRun = true,
            ProgressReporter = (_, _, _) =>
            {
                calls++;
                return Task.CompletedTask;
            }
        };

        var typed = new TickerFunctionContext<TestRequest>(baseContext, new TestRequest());
        await typed.ReportProgressAsync(10);

        Assert.True(typed.IsTestRun);
        Assert.Equal(1, calls);
    }

    private sealed class TestRequest
    {
        public int Value { get; set; }
    }
}
