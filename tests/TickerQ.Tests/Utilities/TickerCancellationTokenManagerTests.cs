using FluentAssertions;
using TickerQ.Utilities;
using TickerQ.Utilities.Enums;
using Xunit.Abstractions;

namespace TickerQ.Tests.Utilities;

public class TickerCancellationTokenManagerTests : IDisposable
{
    private readonly ITestOutputHelper output;
    private readonly TickerType typeOfTicker = TickerType.Timer;
    private readonly bool isDue = true;
    private const string FunctionName = "TestFunc";

    /// <summary>
    /// since the class is static, we must call TickerCancellationTokenManager.CleanUpTickerCancellationTokens() before each test
    /// </summary>
    /// <param name="output"></param>
    public TickerCancellationTokenManagerTests(ITestOutputHelper output)
    {
        this.output = output;
        output.WriteLine("TickerCancellationTokenManager.CleanUpTickerCancellationTokens has been called");
        TickerCancellationTokenManager.CleanUpTickerCancellationTokens();
    }

    [Fact]
    public void AddTickerCancellationToken_ShouldAddATickerCancellationTokenDetailsInstanceToTickerCancellationTokens()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        var id = Guid.NewGuid();

        // Act
        TickerCancellationTokenManager.AddTickerCancellationToken(cts, FunctionName, id, typeOfTicker, isDue);

        // Assert
        TickerCancellationTokenManager.TickerCancellationTokens.Should().ContainKey(id);
        var details = TickerCancellationTokenManager.TickerCancellationTokens[id];
        details.FunctionName.Should().Be(FunctionName);
        details.Type.Should().Be(typeOfTicker);
        details.IsDue.Should().BeTrue();
        details.CancellationSource.Should().Be(cts);
    }


    [Fact]
    public void RemoveTickerCancellationToken_ShouldRemoveTickerCancellationTokens()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        var id = Guid.NewGuid();

        // Act
        TickerCancellationTokenManager.AddTickerCancellationToken(cts, FunctionName, id, typeOfTicker, isDue);
        var result = TickerCancellationTokenManager.RemoveTickerCancellationToken(id);

        // Assert
        result.Should().BeTrue();
        TickerCancellationTokenManager.TickerCancellationTokens.Should().NotContainKey(id);
    }

    [Fact]
    public void CleanUpTickerCancellationTokens_ShouldRemoveAllElementsOfTickerCancellationTokens()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();

        // Act
        TickerCancellationTokenManager.AddTickerCancellationToken(cts, FunctionName, id1, typeOfTicker, isDue);
        TickerCancellationTokenManager.AddTickerCancellationToken(cts, FunctionName, id2, typeOfTicker, isDue);
        TickerCancellationTokenManager.CleanUpTickerCancellationTokens();

        // Assert
        TickerCancellationTokenManager.TickerCancellationTokens.Should().BeEmpty();
    }

    [Fact]
    public void RequestTickerCancellationById_ShouldCancelTickerCancellationToken()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        var id = Guid.NewGuid();

        // Act
        TickerCancellationTokenManager.AddTickerCancellationToken(cts, FunctionName, id, typeOfTicker, isDue);
        var result = TickerCancellationTokenManager.RequestTickerCancellationById(id);
        var details = TickerCancellationTokenManager.TickerCancellationTokens[id];

        // Assert
        details.CancellationSource.IsCancellationRequested.Should().BeTrue();
        result.Should().BeTrue();
    }

    [Fact]
    public void RequestTickerCancellationById_WithNonExistentId_ShouldReturnFalse()
    {
        // Act
        var result = TickerCancellationTokenManager.RequestTickerCancellationById(Guid.NewGuid());

        // Assert
        result.Should().BeFalse();
    }

    /// <summary>
    /// since the class is static, we must call TickerCancellationTokenManager.CleanUpTickerCancellationTokens() after each test
    /// </summary>
    public void Dispose()
    {
        output.WriteLine("TickerCancellationTokenManager.CleanUpTickerCancellationTokens has been called (Dispose)");
        TickerCancellationTokenManager.CleanUpTickerCancellationTokens();
        GC.SuppressFinalize(this);
    }
}