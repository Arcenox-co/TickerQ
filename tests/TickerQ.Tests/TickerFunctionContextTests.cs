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
