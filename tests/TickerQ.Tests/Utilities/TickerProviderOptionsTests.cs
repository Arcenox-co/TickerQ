using FluentAssertions;
using TickerQ.Utilities;

namespace TickerQ.Tests.Utilities;

public class TickerProviderOptionsTests
{
    private readonly TickerProviderOptions _sut = new();

    [Fact]
    public void Tracking_Default_Value_Must_Be_False()
    {
        _sut.Tracking.Should().BeFalse();
    }

    [Fact]
    public void SetAsTracking_ShouldSetTrackingToTrue()
    {
        _sut.SetAsTracking();
        
        _sut.Tracking.Should().BeTrue();
    }

    [Fact]
    public void SetAsNoTracking_ShouldSetTrackingToFalse()
    {
        _sut.SetAsNoTracking();
        
        _sut.Tracking.Should().BeFalse();
    }
}