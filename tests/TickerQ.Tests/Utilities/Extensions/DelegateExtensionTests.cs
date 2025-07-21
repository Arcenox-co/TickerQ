using FluentAssertions;
using NSubstitute;
using TickerQ.Utilities;
using TickerQ.Utilities.Extensions;

namespace TickerQ.Tests.Utilities.Extensions;

public class DelegateExtensionTests
{
    [Fact]
    public void InvokeProviderOptions_WhenActionIsNotNull_ShouldInvokesActionWithOptions()
    {
        // Arrange
        var action = Substitute.For<Action<TickerProviderOptions>>();
        
        // Act
        var result = action.InvokeProviderOptions();
        
        // Assert
        action.Received(1).Invoke(Arg.Any<TickerProviderOptions>());
        result.Should().NotBeNull();
    }

    [Fact]
    public void InvokeProviderOptions_WhenActionModifiesOptions_ShouldReturnsModifiedOptions()
    {
        // Arrange
        Action<TickerProviderOptions> action = options => 
        {
            options.SetAsTracking();
        };
        
        // Act
        var result = action.InvokeProviderOptions();
        
        // Assert
        result.Tracking.Should().BeTrue();
    }

    [Fact]
    public void InvokeProviderOptions_WhenActionDoesNotModifyOptions_ShouldReturnsDefaultOptions()
    {
        // Arrange
        Action<TickerProviderOptions> emptyAction = _ => { };
        
        // Act
        var result = emptyAction.InvokeProviderOptions();
        
        // Assert
        result.Tracking.Should().BeFalse();
    }
}