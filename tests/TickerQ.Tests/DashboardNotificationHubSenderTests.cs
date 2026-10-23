using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using TickerQ.Dashboard.Hubs;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Models;

namespace TickerQ.Tests;

public sealed class DashboardNotificationHubSenderTests
{
    [Fact]
    public async Task SendFailure_IsBestEffortAndDoesNotEscapeIntoSchedulerOperations()
    {
        var (context, proxy) = CreateHubContext();
        proxy.SendCoreAsync(
                Arg.Any<string>(), Arg.Any<object?[]>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("backplane unavailable")));
        using var sender = new TickerQNotificationHubSender(
            context, NullLogger<TickerQNotificationHubSender>.Instance);

        var exception = await Record.ExceptionAsync(() => sender.AddTimeTickerNotifyAsync(Guid.NewGuid()));

        Assert.Null(exception);
    }

    [Fact]
    public async Task Dispose_StopsNewNotificationsAndMakesFinalExecutionUpdatesHarmless()
    {
        var (context, proxy) = CreateHubContext();
        var sender = new TickerQNotificationHubSender(
            context, NullLogger<TickerQNotificationHubSender>.Instance);
        sender.Dispose();

        await sender.AddTimeTickerNotifyAsync(Guid.NewGuid());
        await sender.UpdateTimeTickerFromInternalFunctionContext<TimeTickerEntity>(
            new InternalFunctionContext { TickerId = Guid.NewGuid() });

        await proxy.DidNotReceiveWithAnyArgs()
            .SendCoreAsync(default!, default!, default);
    }

    [Fact]
    public async Task ClearingNextOccurrence_StillInvalidatesConnectedDashboards()
    {
        var (context, proxy) = CreateHubContext();
        using var sender = new TickerQNotificationHubSender(
            context, NullLogger<TickerQNotificationHubSender>.Instance);

        sender.UpdateNextOccurrence(null);

        await proxy.Received(1).SendCoreAsync(
            "GetNextOccurrenceNotification",
            Arg.Is<object?[]>(args => args.Length == 1),
            Arg.Any<CancellationToken>());
    }

    private static (IHubContext<TickerQNotificationHub> Context, IClientProxy Proxy) CreateHubContext()
    {
        var context = Substitute.For<IHubContext<TickerQNotificationHub>>();
        var clients = Substitute.For<IHubClients>();
        var proxy = Substitute.For<IClientProxy>();
        context.Clients.Returns(clients);
        clients.All.Returns(proxy);
        proxy.SendCoreAsync(
                Arg.Any<string>(), Arg.Any<object?[]>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        return (context, proxy);
    }
}
