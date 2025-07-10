using FluentAssertions;
using NSubstitute;
using TickerQ.Utilities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces.Managers;

namespace TickerQ.Tests.Utilities;

public class TickerFunctionProviderTests
{
    private const string FunctionName = "TestFunc";
    
    [Fact]
    public void RegisterFunctions_ShouldStoreFunctionsCorrectly()
    {
        // Arrange
        bool wasCalled;
        TickerFunctionDelegate testDelegate = (ct, sp, ctx) =>
        {
            wasCalled = true;
            return Task.CompletedTask;
        };

        var functions = new Dictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate)>
        {
            { FunctionName, ("* * * * *", TickerTaskPriority.Normal, testDelegate) }
        };

        // Act
        TickerFunctionProvider.RegisterFunctions(functions);

        // Assert
        TickerFunctionProvider.TickerFunctions.Should().ContainKey(FunctionName);
        var entry = TickerFunctionProvider.TickerFunctions[FunctionName];
        entry.cronExpression.Should().Be("* * * * *");
        entry.Priority.Should().Be(TickerTaskPriority.Normal);
        entry.Delegate.Should().Be(testDelegate);
    }

    [Fact]
    public void RegisterRequestType_ShouldStoreValuesCorrectly()
    {
        // Arrange
        var dictionary = new Dictionary<string, (string, Type)>
        {
            { FunctionName, (FunctionName, typeof(string)) }
        };
        
        // Act
        TickerFunctionProvider.RegisterRequestType(dictionary);
        
        // Assert
        TickerFunctionProvider.TickerFunctionRequestTypes.Should().ContainKey(FunctionName);
        var entry = TickerFunctionProvider.TickerFunctionRequestTypes[FunctionName];
        entry.Item1.Should().Be(FunctionName);
        entry.Item2.Should().Be<string>();

    }
    
    [Fact]
    public async Task GetRequestAsync_ShouldReturnRequestFromManager()
    {
        // Arrange
        var expected = "request";
        var tickerId = Guid.NewGuid();
        var tickerType = TickerType.CronExpression;

        // Create a substitute for IInternalTickerManager
        var managerSub = Substitute.For<IInternalTickerManager>();
        managerSub.GetRequestAsync<string>(tickerId, tickerType).Returns(expected);

        // Create a substitute for IServiceProvider
        var serviceProviderSub = Substitute.For<IServiceProvider>();
        serviceProviderSub.GetService(typeof(IInternalTickerManager)).Returns(managerSub);

        // Act
        var result = await TickerRequestProvider.GetRequestAsync<string>(serviceProviderSub, tickerId, tickerType);

        // Assert
        result.Should().Be(expected);
        await managerSub.Received(1).GetRequestAsync<string>(tickerId, tickerType);
    }
}