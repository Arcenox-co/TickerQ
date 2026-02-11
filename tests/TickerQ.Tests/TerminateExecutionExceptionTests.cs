using FluentAssertions;
using TickerQ.Exceptions;
using TickerQ.Utilities.Enums;

namespace TickerQ.Tests;

public class TerminateExecutionExceptionTests
{
    [Fact]
    public void Constructor_MessageOnly_SetsSkippedStatus()
    {
        var ex = new TerminateExecutionException("test message");

        ex.Message.Should().Be("test message");
        ex.Status.Should().Be(TickerStatus.Skipped);
        ex.InnerException.Should().BeNull();
    }

    [Fact]
    public void Constructor_WithStatus_SetsCustomStatus()
    {
        var ex = new TerminateExecutionException(TickerStatus.Cancelled, "cancelled");

        ex.Message.Should().Be("cancelled");
        ex.Status.Should().Be(TickerStatus.Cancelled);
        ex.InnerException.Should().BeNull();
    }

    [Fact]
    public void Constructor_WithInnerException_PreservesInner()
    {
        var inner = new InvalidOperationException("inner");
        var ex = new TerminateExecutionException("outer", inner);

        ex.Message.Should().Be("outer");
        ex.InnerException.Should().BeSameAs(inner);
        ex.Status.Should().Be(TickerStatus.Skipped);
    }

    [Fact]
    public void Constructor_WithStatusAndInnerException_SetsAll()
    {
        var inner = new InvalidOperationException("inner");
        var ex = new TerminateExecutionException(TickerStatus.Failed, "failed", inner);

        ex.Message.Should().Be("failed");
        ex.Status.Should().Be(TickerStatus.Failed);
        ex.InnerException.Should().BeSameAs(inner);
    }

    [Fact]
    public void IsException_InheritsFromException()
    {
        var ex = new TerminateExecutionException("test");

        ex.Should().BeAssignableTo<Exception>();
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
        ex.Status.Should().Be(status);
    }
}
