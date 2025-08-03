using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using TickerQ.Utilities;
using TickerQ.Utilities.Base;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Models;
using Xunit;

namespace TickerQ.Tests.DependencyInjection;

public class ServiceDiscoveryTests
{
    [Fact]
    public void RegisterServicesFromAssemblies_ShouldAddAssembliesToCollection()
    {
        // Arrange
        var builder = new TickerOptionsBuilder();
        var assembly1 = Assembly.GetExecutingAssembly();
        var assembly2 = typeof(TickerOptionsBuilder).Assembly;

        // Act
        var result = builder.RegisterServicesFromAssemblies(assembly1, assembly2);

        // Assert
        result.Should().BeSameAs(builder); // Should return this for fluent interface
        builder.AssembliesToRegister.Should().HaveCount(2);
        builder.AssembliesToRegister.Should().Contain(assembly1);
        builder.AssembliesToRegister.Should().Contain(assembly2);
    }

    [Fact]
    public void RegisterServicesFromAssemblies_ShouldHandleNullAssemblies()
    {
        // Arrange
        var builder = new TickerOptionsBuilder();

        // Act
        var result = builder.RegisterServicesFromAssemblies(null);

        // Assert
        result.Should().BeSameAs(builder);
        builder.AssembliesToRegister.Should().BeEmpty();
    }

    [Fact]
    public void RegisterServicesFromAssemblies_ShouldHandleEmptyAssemblies()
    {
        // Arrange
        var builder = new TickerOptionsBuilder();

        // Act
        var result = builder.RegisterServicesFromAssemblies();

        // Assert
        result.Should().BeSameAs(builder);
        builder.AssembliesToRegister.Should().BeEmpty();
    }

    [Fact]
    public void RegisterServicesFromAssemblies_ShouldSupportMethodChaining()
    {
        // Arrange
        var builder = new TickerOptionsBuilder();
        var assembly1 = Assembly.GetExecutingAssembly();
        var assembly2 = typeof(TickerOptionsBuilder).Assembly;

        // Act
        var result = builder
            .RegisterServicesFromAssemblies(assembly1)
            .RegisterServicesFromAssemblies(assembly2);

        // Assert
        result.Should().BeSameAs(builder);
        builder.AssembliesToRegister.Should().HaveCount(2);
        builder.AssembliesToRegister.Should().Contain(assembly1);
        builder.AssembliesToRegister.Should().Contain(assembly2);
    }

    [Fact]
    public void RegisterFunctionsFromAssemblies_ShouldHandleNullAssemblies()
    {
        // Arrange & Act
        TickerFunctionProvider.RegisterFunctionsFromAssemblies(null);

        // Assert - Should not throw
        // The method should handle null gracefully
    }

    [Fact]
    public void RegisterFunctionsFromAssemblies_ShouldHandleEmptyAssemblies()
    {
        // Arrange & Act
        TickerFunctionProvider.RegisterFunctionsFromAssemblies(new Assembly[0]);

        // Assert - Should not throw
        // The method should handle empty arrays gracefully
    }
}

// Test classes for service discovery
public class TestTickerService
{
    [TickerFunction("TestFunction1", "0 */5 * * * *", TickerTaskPriority.Normal)]
    public async Task TestMethod1(CancellationToken cancellationToken, TickerFunctionContext context)
    {
        await Task.Delay(100, cancellationToken);
    }

    [TickerFunction("TestFunction2", TickerTaskPriority.High)]
    public void TestMethod2(TickerFunctionContext context)
    {
        // Simple synchronous method
    }

    [TickerFunction("TestFunction3")]
    public static async Task TestStaticMethod(CancellationToken cancellationToken)
    {
        await Task.Delay(50, cancellationToken);
    }
}

public class TestTickerServiceWithRequest
{
    [TickerFunction("TestFunctionWithRequest", "0 0 * * * *")]
    public async Task TestMethodWithRequest(TickerFunctionContext<TestRequest> context, CancellationToken cancellationToken)
    {
        await Task.Delay(100, cancellationToken);
    }
}

public class TestRequest
{
    public string Message { get; set; }
    public int Value { get; set; }
}
