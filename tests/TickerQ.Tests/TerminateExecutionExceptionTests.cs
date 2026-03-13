using TickerQ.Exceptions;
using TickerQ.Utilities.Enums;

namespace TickerQ.Tests;

public class TerminateExecutionExceptionTests
{
    [Fact]
    public void Constructor_MessageOnly_SetsSkippedStatus()
    {
        var ex = new TerminateExecutionException("test message");

        Assert.Equal("test message", ex.Message);
        Assert.Equal(TickerStatus.Skipped, ex.Status);
        Assert.Null(ex.InnerException);
    }

    [Fact]
    public void Constructor_WithStatus_SetsCustomStatus()
    {
        var ex = new TerminateExecutionException(TickerStatus.Cancelled, "cancelled");

        Assert.Equal("cancelled", ex.Message);
        Assert.Equal(TickerStatus.Cancelled, ex.Status);
        Assert.Null(ex.InnerException);
    }

    [Fact]
    public void Constructor_WithInnerException_PreservesInner()
    {
        var inner = new InvalidOperationException("inner");
        var ex = new TerminateExecutionException("outer", inner);

        Assert.Equal("outer", ex.Message);
        Assert.Same(inner, ex.InnerException);
        Assert.Equal(TickerStatus.Skipped, ex.Status);
    }

    [Fact]
    public void Constructor_WithStatusAndInnerException_SetsAll()
    {
        var inner = new InvalidOperationException("inner");
        var ex = new TerminateExecutionException(TickerStatus.Failed, "failed", inner);

        Assert.Equal("failed", ex.Message);
        Assert.Equal(TickerStatus.Failed, ex.Status);
        Assert.Same(inner, ex.InnerException);
    }

    [Fact]
    public void IsException_InheritsFromException()
    {
        var ex = new TerminateExecutionException("test");

        Assert.IsAssignableFrom<Exception>(ex);
    }

    [Theory]
    [InlineData(TickerStatus.Done)]
    [InlineData(TickerStatus.Failed)]
    [InlineData(TickerStatus.Cancelled)]
    [InlineData(TickerStatus.InProgress)]
    [InlineData(TickerStatus.Idle)]
    public void Constructor_WithStatus_SupportsAllStatusValues(TickerStatus status)
    {
        var ex = new TerminateExecutionException(status, "msg");
        Assert.Equal(status, ex.Status);
    }
}
