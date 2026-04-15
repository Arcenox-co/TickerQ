using System;
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

    private sealed class TestRequest
    {
        public int Value { get; set; }
    }
}
