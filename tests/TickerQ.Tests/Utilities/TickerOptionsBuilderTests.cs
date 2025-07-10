using FluentAssertions;
using TickerQ.Utilities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;

namespace TickerQ.Tests.Utilities;

public class TickerOptionsBuilderTests
{
    private readonly TickerOptionsBuilder sut = new();
    private const string RandomString = "TickerQ";
    private const int MinimumValidSeconds = 31;

    [Fact]
    public void SetMaxConcurrency_ShouldSetMaxConcurrency_WhenArgumentIsGreaterThanZero()
    {
        // Act
        sut.SetMaxConcurrency(1);

        // Assert
        sut.MaxConcurrency.Should().Be(1);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void SetMaxConcurrency_ShouldSetEnvironmentProcessorCount_WhenArgumentIsEqualOrLowerThanZero(
        int maxConcurrency)
    {
        // Arrange
        // This value can't be in method parameter since it doesn't get its value compile-time 
        var environmentProcessorCount = Environment.ProcessorCount;

        // Act
        sut.SetMaxConcurrency(maxConcurrency);

        // Assert
        sut.MaxConcurrency.Should().Be(environmentProcessorCount);
    }

    [Fact]
    public void SetInstanceIdentifier_ShouldSetInstanceIdentifier()
    {
        // Act
        sut.SetInstanceIdentifier(RandomString);

        // Assert
        sut.InstanceIdentifier.Should().Be(RandomString);
    }

    [Theory]
    [InlineData(MinimumValidSeconds - 100, 30)]
    [InlineData(MinimumValidSeconds, 31)]
    public void UpdateMissedJobCheckDelay_ShouldSetTimeOutChecker(int input, int expected)
    {
        // Act
        sut.UpdateMissedJobCheckDelay(TimeSpan.FromSeconds(input));

        // Assert
        sut.TimeOutChecker.Should().Be(TimeSpan.FromSeconds(expected));
    }

    [Fact]
    public void SetExceptionHandler_ShouldSetTickerExceptionHandlerTypeToGivenGenericType()
    {
        // Act
        sut.SetExceptionHandler<FakeExceptionHandler>();
        
        // Assert
        sut.TickerExceptionHandlerType.Should().Be<FakeExceptionHandler>();
    }
}

public abstract class FakeExceptionHandler : ITickerExceptionHandler
{
    public Task HandleExceptionAsync(Exception exception, Guid tickerId, TickerType tickerType) 
        => Task.CompletedTask;

    public Task HandleCanceledExceptionAsync(Exception exception, Guid tickerId, TickerType tickerType)
        => Task.CompletedTask;
}