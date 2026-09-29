using NSubstitute;
using TickerQ.Dashboard;
using TickerQ.Dashboard.Infrastructure.Dashboard;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ.Tests;

public class DashboardRunNowTests
{
    [Fact]
    public async Task RunTimeTickerOnDemand_AcquiredGeneration_DispatchesImmediatelyWithDeepContext()
    {
        var provider = Substitute.For<ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity>>();
        var dispatcher = Substitute.For<ITickerQDispatcher>();
        var notifier = Substitute.For<ITickerQNotificationHubSender>();
        var token = Guid.NewGuid();
        var childToken = Guid.NewGuid();
        var root = new TimeTickerEntity
        {
            Id = Guid.NewGuid(),
            Function = "run-root",
            Status = TickerStatus.InProgress,
            ExecutionTime = DateTime.UtcNow,
            AcquisitionToken = token,
            Children =
            [
                new TimeTickerEntity
                {
                    Id = Guid.NewGuid(), Function = "run-child",
                    Status = TickerStatus.Idle, AcquisitionToken = childToken
                }
            ]
        };
        provider.AcquireTimeTickerOnDemandAsync(root.Id, Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(root);
        var repository = CreateRepository(provider, dispatcher, notifier);

        var result = await repository.RunTimeTickerOnDemandAsync(root.Id);

        Assert.True(result);
        await dispatcher.Received(1).DispatchAsync(
            Arg.Is<InternalFunctionContext[]>(items =>
                items.Single().TickerId == root.Id &&
                items.Single().AcquisitionToken == token &&
                items.Single().TimeTickerChildren.Single().AcquisitionToken == childToken),
            CancellationToken.None);
        await notifier.Received(1).UpdateTimeTickerNotifyAsync(root.Id);
    }

    [Fact]
    public async Task RunTimeTickerOnDemand_LostAtomicAcquisition_DoesNotDispatch()
    {
        var provider = Substitute.For<ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity>>();
        var dispatcher = Substitute.For<ITickerQDispatcher>();
        var notifier = Substitute.For<ITickerQNotificationHubSender>();
        var id = Guid.NewGuid();
        provider.AcquireTimeTickerOnDemandAsync(id, Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns((TimeTickerEntity)null);
        var repository = CreateRepository(provider, dispatcher, notifier);

        var result = await repository.RunTimeTickerOnDemandAsync(id);

        Assert.False(result);
        await dispatcher.DidNotReceiveWithAnyArgs()
            .DispatchAsync(default, default);
        await notifier.DidNotReceiveWithAnyArgs().UpdateTimeTickerNotifyAsync(default);
    }

    private static TickerDashboardRepository<TimeTickerEntity, CronTickerEntity> CreateRepository(
        ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity> provider,
        ITickerQDispatcher dispatcher,
        ITickerQNotificationHubSender notifier)
        => new(
            new TickerExecutionContext(),
            provider,
            Substitute.For<ITickerQHostScheduler>(),
            notifier,
            new DashboardOptionsBuilder(),
            dispatcher);
}
