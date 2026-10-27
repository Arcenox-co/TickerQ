using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using TickerQ.BackgroundServices;
using TickerQ.Utilities;
using TickerQ.Utilities.Cluster;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;

namespace TickerQ.Tests;

[Collection("TickerCancellationTokenState")]
public sealed class ClusterControlBackgroundServiceTests
{
    [Fact]
    public async Task Command_stays_pending_during_the_persisted_to_local_queue_handoff()
    {
        var now = new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);
        var options = new SchedulerOptionsBuilder { NodeIdentifier = "worker-b" };
        options.BindRuntimeActivationScope("cluster-command-retry", 1, true);
        var command = Command(options.ExecutionOwnerId, now);
        var store = Substitute.For<ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity>>();
        store.GetPendingCommandsAsync(options.ExecutionOwnerId, 50, Arg.Any<CancellationToken>())
            .Returns(new[] { command });
        store.GetExecutionOwnershipAsync(command.TickerType, command.TickerId, Arg.Any<CancellationToken>())
            .Returns(new TickerQExecutionOwnership
            {
                Type = command.TickerType, TickerId = command.TickerId,
                ExecutionOwnerId = command.TargetExecutionOwnerId,
                AcquisitionToken = command.AcquisitionToken, Status = TickerStatus.Queued
            });
        var service = Service(store, options, now);

        await service.ProcessCommandsAsync(now, CancellationToken.None);

        await store.DidNotReceiveWithAnyArgs().CompleteCommandAsync(default, default, default!, default);
    }

    [Fact]
    public async Task Command_stays_pending_while_a_finished_root_still_owns_its_running_chain()
    {
        var now = new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);
        var options = new SchedulerOptionsBuilder { NodeIdentifier = "worker-b" };
        options.BindRuntimeActivationScope("cluster-command-chain", 1, true);
        var command = Command(options.ExecutionOwnerId, now);
        var store = Substitute.For<ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity>>();
        store.GetPendingCommandsAsync(options.ExecutionOwnerId, 50, Arg.Any<CancellationToken>())
            .Returns(new[] { command });
        store.GetExecutionOwnershipAsync(command.TickerType, command.TickerId, Arg.Any<CancellationToken>())
            .Returns(new TickerQExecutionOwnership
            {
                Type = command.TickerType, TickerId = command.TickerId,
                ExecutionOwnerId = command.TargetExecutionOwnerId,
                AcquisitionToken = command.AcquisitionToken, Status = TickerStatus.Done, ChainActive = true
            });
        var service = Service(store, options, now);

        await service.ProcessCommandsAsync(now, CancellationToken.None);

        await store.DidNotReceiveWithAnyArgs().CompleteCommandAsync(default, default, default!, default);
    }

    [Fact]
    public async Task Command_is_completed_when_its_execution_generation_has_moved_on()
    {
        var now = new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);
        var options = new SchedulerOptionsBuilder { NodeIdentifier = "worker-b" };
        options.BindRuntimeActivationScope("cluster-command-finished", 1, true);
        var command = Command(options.ExecutionOwnerId, now);
        var store = Substitute.For<ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity>>();
        store.GetPendingCommandsAsync(options.ExecutionOwnerId, 50, Arg.Any<CancellationToken>())
            .Returns(new[] { command });
        store.GetExecutionOwnershipAsync(command.TickerType, command.TickerId, Arg.Any<CancellationToken>())
            .Returns(new TickerQExecutionOwnership
            {
                Type = command.TickerType, TickerId = command.TickerId,
                ExecutionOwnerId = command.TargetExecutionOwnerId,
                AcquisitionToken = command.AcquisitionToken, Status = TickerStatus.Done
            });
        var service = Service(store, options, now);

        await service.ProcessCommandsAsync(now, CancellationToken.None);

        await store.Received(1).CompleteCommandAsync(command.Id, TickerQControlCommandStatus.Completed,
            Arg.Is<string>(x => x.Contains("no longer running", StringComparison.Ordinal)), now,
            Arg.Any<CancellationToken>());
    }

    private static TickerQControlCommand Command(string owner, DateTime now) => new()
    {
        Id = Guid.NewGuid(), TargetNodeKey = TickerQClusterIdentity.NodeKey(owner),
        TargetExecutionOwnerId = owner, TickerType = TickerType.TimeTicker,
        TickerId = Guid.NewGuid(), AcquisitionToken = Guid.NewGuid(),
        RequestedAtUtc = now, ExpiresAtUtc = now.AddSeconds(30),
        Status = TickerQControlCommandStatus.Pending
    };

    private static TickerQClusterControlBackgroundService<TimeTickerEntity, CronTickerEntity> Service(
        ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity> store,
        SchedulerOptionsBuilder options, DateTime now)
    {
        var clock = Substitute.For<ITickerClock>();
        clock.UtcNow.Returns(now);
        return new TickerQClusterControlBackgroundService<TimeTickerEntity, CronTickerEntity>(
            store, Substitute.For<ITickerQTaskScheduler>(), Substitute.For<ITickerQHostScheduler>(),
            options, clock,
            NullLogger<TickerQClusterControlBackgroundService<TimeTickerEntity, CronTickerEntity>>.Instance);
    }
}
