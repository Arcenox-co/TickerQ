using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using TickerQ.Utilities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Licensing;
using TickerQ.Utilities.Models;
using Xunit;

namespace TickerQ.RemoteExecutor.Tests;

public sealed class TickerExecutionTaskHandlerRouterTests
{
    [Fact]
    public async Task QualifiedRemoteRoot_RoutesThroughGraphAwareLocalHandler()
    {
        var local = new RecordingHandler();
        var services = new ServiceCollection()
            .AddSingleton<ITickerExecutionTaskHandler>(local)
            .BuildServiceProvider();
        var remote = new TickerRemoteExecutionTaskHandler(services);
        var router = new TickerExecutionTaskHandlerRouter(services, remote);
        var root = new InternalFunctionContext
        {
            TickerId = Guid.NewGuid(),
            FunctionName = "RemoteRoot@node-a",
            TimeTickerChildren =
            [
                new InternalFunctionContext
                {
                    TickerId = Guid.NewGuid(),
                    FunctionName = "Child"
                }
            ]
        };

        await router.ExecuteTaskAsync(root, isDue: false, CancellationToken.None);

        Assert.Equal(1, local.ExecuteCalls);
        Assert.Same(root, local.LastContext);
    }

    [Fact]
    public async Task QualifiedRemoteRegisteredRoot_PreservesRegisteredSourceThroughLocalHandler()
    {
        var local = new RecordingHandler();
        var services = new ServiceCollection()
            .AddSingleton<ITickerExecutionTaskHandler>(local)
            .BuildServiceProvider();
        var remote = new TickerRemoteExecutionTaskHandler(services);
        var router = new TickerExecutionTaskHandlerRouter(services, remote);
        var root = new InternalFunctionContext
        {
            TickerId = Guid.NewGuid(),
            FunctionName = "RemoteRoot@node-a"
        };
        using var registeredSource = new CancellationTokenSource();

        await router.ExecuteRegisteredTaskAsync(
            root, isDue: false, registeredSource, CancellationToken.None);

        Assert.Equal(1, local.RegisteredCalls);
        Assert.Same(root, local.LastContext);
        Assert.Same(registeredSource, local.LastRegisteredSource);
    }

    [Fact]
    public async Task RemoteCompatibilityHandler_BlocksBeforeDelegate_WhenLicenseDeniesExecution()
    {
        const string functionName = "BlockedRemote@node-a";
        var originalFunctions = TickerFunctionProvider.TickerFunctions;
        var invoked = false;
        var clock = Substitute.For<ITickerClock>();
        clock.UtcNow.Returns(DateTime.UtcNow);
        var licenseState = new TickerQLicenseStateProvider(clock);
        licenseState.Publish(TickerQLicenseState.Invalid("test authority denied execution"));

        await using var services = new ServiceCollection()
            .AddSingleton(licenseState)
            .BuildServiceProvider();

        TickerFunctionProvider.ReplaceFunctions(
            new Dictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate, int)>
            {
                [functionName] = (string.Empty, TickerTaskPriority.Normal, (_, _, _) =>
                {
                    invoked = true;
                    return Task.CompletedTask;
                }, 0)
            });

        try
        {
            var handler = new TickerRemoteExecutionTaskHandler(services);
            var context = new InternalFunctionContext
            {
                TickerId = Guid.NewGuid(),
                FunctionName = functionName
            };

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => handler.ExecuteTaskAsync(context, isDue: false, CancellationToken.None));

            Assert.Contains("execution is disabled", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(invoked);
        }
        finally
        {
            TickerFunctionProvider.ReplaceFunctions(originalFunctions);
        }
    }

    private sealed class RecordingHandler : ITickerExecutionTaskHandler
    {
        public int ExecuteCalls { get; private set; }
        public int RegisteredCalls { get; private set; }
        public InternalFunctionContext? LastContext { get; private set; }
        public CancellationTokenSource? LastRegisteredSource { get; private set; }

        public Task ExecuteTaskAsync(
            InternalFunctionContext context,
            bool isDue,
            CancellationToken cancellationToken = default)
        {
            ExecuteCalls++;
            LastContext = context;
            return Task.CompletedTask;
        }

        public Task ExecuteRegisteredTaskAsync(
            InternalFunctionContext context,
            bool isDue,
            CancellationTokenSource registeredSource,
            CancellationToken cancellationToken = default)
        {
            RegisteredCalls++;
            LastContext = context;
            LastRegisteredSource = registeredSource;
            return Task.CompletedTask;
        }
    }
}
