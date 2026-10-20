using NSubstitute;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Managers;
using TickerQ.Utilities.Models;

namespace TickerQ.Tests;

[Collection("TickerCancellationTokenState")]
public sealed class ManagerCancellationTests
{
    [Fact]
    public async Task Cancellation_is_routed_to_the_exact_remote_execution_generation()
    {
        var id = Guid.NewGuid();
        var generation = Guid.NewGuid();
        var ownerId = "worker-b:4242:instance";
        var now = new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);
        var partition = new TickerQRuntimePartition("remote-cancellation");
        var persistence = Substitute.For<ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity>>();
        persistence.SupportsClusterControl.Returns(true);
        persistence.GetExecutionOwnershipAsync(TickerType.TimeTicker, id, Arg.Any<CancellationToken>())
            .Returns(new TickerQ.Utilities.Cluster.TickerQExecutionOwnership
            {
                Type = TickerType.TimeTicker, TickerId = id, ExecutionOwnerId = ownerId,
                AcquisitionToken = generation, Status = TickerStatus.InProgress
            });
        TickerQ.Utilities.Cluster.TickerQControlCommand captured = null;
        persistence.RequestCancellationAsync(
                Arg.Do<TickerQ.Utilities.Cluster.TickerQControlCommand>(x => captured = x),
                Arg.Any<CancellationToken>()).Returns(true);
        var clock = Substitute.For<ITickerClock>();
        clock.UtcNow.Returns(now);
        var options = new SchedulerOptionsBuilder();
        options.BindRuntimeActivationScope(partition.ApplicationNamespace, 1, true);
        var manager = new TickerManager<TimeTickerEntity, CronTickerEntity>(
            persistence, Substitute.For<ITickerQHostScheduler>(), clock,
            Substitute.For<ITickerQNotificationHubSender>(),
            new TickerExecutionContext { RuntimePartition = partition },
            Substitute.For<ITickerQDispatcher>(), options, new TickerQActivationGate());

        var result = await ((ITimeTickerManager<TimeTickerEntity>)manager).RequestCancellationAsync(id);

        Assert.Equal(1, result.AffectedRows);
        Assert.NotNull(captured);
        Assert.Equal(TickerQ.Utilities.Cluster.TickerQClusterIdentity.NodeKey(ownerId), captured.TargetNodeKey);
        Assert.Equal(ownerId, captured.TargetExecutionOwnerId);
        Assert.Equal(generation, captured.AcquisitionToken);
        Assert.Equal(now, captured.RequestedAtUtc);
        Assert.Equal(now.AddSeconds(30), captured.ExpiresAtUtc);
    }

    [Theory]
    [InlineData(TickerType.TimeTicker, false)] [InlineData(TickerType.CronTickerOccurrence, false)]
    [InlineData(TickerType.TimeTicker, true)] [InlineData(TickerType.CronTickerOccurrence, true)]
    public async Task Cancellation_is_scoped_to_type_and_partition_and_only_signals_the_execution(TickerType target, bool callbackFails)
    {
        var partition = new TickerQRuntimePartition("manager-cancellation");
        var otherPartition = new TickerQRuntimePartition("other-application");
        var id = Guid.NewGuid();
        var contexts = new[] {
            new InternalFunctionContext { TickerId = id, Type = TickerType.TimeTicker, RuntimePartitionKey = partition.StorageKey },
            new InternalFunctionContext { TickerId = id, Type = TickerType.CronTickerOccurrence, RuntimePartitionKey = partition.StorageKey },
            new InternalFunctionContext { TickerId = id, Type = target, RuntimePartitionKey = otherPartition.StorageKey }
        };
        var sources = contexts.Select(context => TickerCancellationTokenManager.TryRegisterAcquired(context, false)!).ToArray();
        using var callback = callbackFails
            ? sources[target == TickerType.TimeTicker ? 0 : 1].Token.Register(() => throw new InvalidOperationException("user callback failed"))
            : default;
        var notifier = Substitute.For<ITickerQNotificationHubSender>();
        notifier.CanceledTickerNotifyAsync(id).Returns(Task.FromException(new Exception("notification failure")));
        var options = new SchedulerOptionsBuilder(); options.BindRuntimeActivationScope(null, 1, false);
        var manager = new TickerManager<TimeTickerEntity, CronTickerEntity>(
            Substitute.For<ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity>>(),
            Substitute.For<ITickerQHostScheduler>(), Substitute.For<ITickerClock>(), notifier,
            new TickerExecutionContext { RuntimePartition = partition }, Substitute.For<ITickerQDispatcher>(), options, new TickerQActivationGate());
        try
        {
            if (target == TickerType.TimeTicker)
                Assert.Equal(1, (await ((ITimeTickerManager<TimeTickerEntity>)manager).RequestCancellationAsync(id)).AffectedRows);
            else
                Assert.Equal(1, (await ((ICronTickerManager<CronTickerEntity>)manager).RequestCancellationAsync(id)).AffectedRows);
            Assert.Equal(target == TickerType.TimeTicker, sources[0].IsCancellationRequested);
            Assert.Equal(target == TickerType.CronTickerOccurrence, sources[1].IsCancellationRequested);
            Assert.False(sources[2].IsCancellationRequested);
            Assert.Equal(0, (await ((ITimeTickerManager<TimeTickerEntity>)manager).RequestCancellationAsync(Guid.NewGuid())).AffectedRows);
            // Cancellation does not remove/dispose the owner registration.
            Assert.Null(TickerCancellationTokenManager.TryRegisterAcquired(contexts[0], false));
        }
        finally
        {
            for (var i = 0; i < contexts.Length; i++)
            {
                TickerCancellationTokenManager.RemoveTickerCancellationToken(new TickerExecutionKey(
                    contexts[i].RuntimePartitionKey, contexts[i].Type, id), sources[i]);
                sources[i].Dispose();
            }
        }
    }
}
