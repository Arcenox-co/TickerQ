using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using TickerQ.Utilities;
using TickerQ.Utilities.Base;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces.Managers;

namespace TickerQ.Tests;

public class TickerRequestProviderTests
{
    [Fact]
    public async Task GetRequestAsync_WhenPersistenceIsCancelled_PropagatesCancellation()
    {
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();
        var cancellationToken = cancellationSource.Token;
        var manager = Substitute.For<IInternalTickerManager>();
        manager
            .GetRequestAsync<Request>(Arg.Any<Guid>(), Arg.Any<TickerType>(), cancellationToken)
            .Returns(Task.FromCanceled<Request>(cancellationToken));
        await using var provider = CreateServiceProvider(manager);
        await using var scope = provider.CreateAsyncScope();
        var context = CreateContext(scope);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => TickerRequestProvider.GetRequestAsync<Request>(context, cancellationToken));
    }

    [Fact]
    public async Task GetRequestAsync_WithJsonTypeInfo_WhenPersistenceIsCancelled_PropagatesCancellation()
    {
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();
        var cancellationToken = cancellationSource.Token;
        var typeInfo = (System.Text.Json.Serialization.Metadata.JsonTypeInfo<Request>)
            JsonSerializerOptions.Default.GetTypeInfo(typeof(Request));
        var manager = Substitute.For<IInternalTickerManager>();
        manager
            .GetRequestAsync(
                Arg.Any<Guid>(),
                Arg.Any<TickerType>(),
                typeInfo,
                cancellationToken)
            .Returns(Task.FromCanceled<Request>(cancellationToken));
        await using var provider = CreateServiceProvider(manager);
        await using var scope = provider.CreateAsyncScope();
        var context = CreateContext(scope);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => TickerRequestProvider.GetRequestAsync(
                context,
                typeInfo,
                cancellationToken));
    }

    private static ServiceProvider CreateServiceProvider(IInternalTickerManager manager)
    {
        return new ServiceCollection()
            .AddSingleton(manager)
            .BuildServiceProvider();
    }

    private static TickerFunctionContext CreateContext(AsyncServiceScope scope)
    {
        var context = new TickerFunctionContext();
        context.SetServiceScope(scope);
        return context;
    }

    private sealed record Request(string Value);
}
