using System.Text.Json;
using StackExchange.Redis;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using TickerQ.Caching.StackExchangeRedis.DependencyInjection;
using TickerQ.Caching.StackExchangeRedis.Helpers;
using TickerQ.Utilities;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;

namespace TickerQ.Caching.StackExchangeRedis.Tests;

public class RedisHeartbeatOwnershipTests
{
    [Fact]
    public async Task SlowRecoveryAndCacheRetry_DoNotBlockHeartbeatRenewal_AndShutdownCancelsBoth()
    {
        var gate = new TickerQActivationGate();
        gate.SignalActivated();
        var context = Substitute.For<ITickerQRedisContext>();
        var manager = Substitute.For<IInternalTickerManager>();
        var blocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var renewed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;
        context.NotifyNodeAliveAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            if (Interlocked.Increment(ref count) >= 3) renewed.TrySetResult();
            return Task.CompletedTask;
        });
        context.GetDeadNodesAsync(Arg.Any<CancellationToken>()).Returns(["dead"]);
        manager.ReleaseDeadNodeResources("dead", Arg.Any<CancellationToken>())
            .Returns(c => blocked.Task.WaitAsync(c.ArgAt<CancellationToken>(1)));
        context.RetryPendingInvalidationsAsync(Arg.Any<CancellationToken>())
            .Returns(c => blocked.Task.WaitAsync(c.ArgAt<CancellationToken>(0)));
        using var service = new NodeHeartBeatBackgroundService(new() { NodeHeartbeatInterval = TimeSpan.FromMilliseconds(10) },
            context, manager, NullLogger<NodeHeartBeatBackgroundService>.Instance, gate);
        try
        {
            await service.StartAsync(CancellationToken.None);
            await renewed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await service.StopAsync(timeout.Token);
            Assert.False(timeout.IsCancellationRequested);
        }
        await context.DidNotReceive().AcknowledgeDeadNodeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecoveryFailureIsRetried_AndDoesNotPreventOtherOwnersFromBeingAcknowledged()
    {
        var context = Substitute.For<ITickerQRedisContext>();
        var manager = Substitute.For<IInternalTickerManager>();
        var pending = new HashSet<string> { "failed", "healthy-recovery" };
        var completed = new HashSet<string>();
        var attempts = 0;
        context.GetDeadNodesAsync(Arg.Any<CancellationToken>()).Returns(_ => pending.ToArray());
        manager.ReleaseDeadNodeResources(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var owner = call.ArgAt<string>(0);
            if (owner == "failed" && ++attempts == 1) return Task.FromException(new IOException("store unavailable"));
            completed.Add(owner);
            return Task.CompletedTask;
        });
        context.AcknowledgeDeadNodeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var owner = call.ArgAt<string>(0);
            Assert.Contains(owner, completed);
            pending.Remove(owner);
            return Task.CompletedTask;
        });
        using var service = new NodeHeartBeatBackgroundService(new(), context, manager,
            NullLogger<NodeHeartBeatBackgroundService>.Instance, new TickerQActivationGate());
        await service.RecoverDeadNodesAsync(CancellationToken.None);
        Assert.Equal("failed", Assert.Single(pending));
        await service.RecoverDeadNodesAsync(CancellationToken.None);
        Assert.Empty(pending);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task HangingDashboardNotification_DoesNotDelayRenewalOrAccumulateSends()
    {
        var services = new ServiceCollection().AddDistributedMemoryCache();
        await using var provider = services.BuildServiceProvider();
        var sender = Substitute.For<ITickerQNotificationHubSender>();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        sender.UpdateNodeHeartBeatAsync(Arg.Any<JsonElement>()).Returns(_ => { started.TrySetResult(); return release.Task; });
        var database = Substitute.For<IDatabase>();
        var context = new TickerQRedisContext(provider.GetRequiredService<IDistributedCache>(), new(), new(), sender,
            database: database);
        try
        {
            await context.NotifyNodeAliveAsync().WaitAsync(TimeSpan.FromSeconds(5));
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            for (var i = 0; i < 5; i++) await context.NotifyNodeAliveAsync().WaitAsync(TimeSpan.FromSeconds(5));
            await sender.Received(1).UpdateNodeHeartBeatAsync(Arg.Any<JsonElement>());
            await database.Received(6).SetAddAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>());
        }
        finally { release.TrySetResult(); }
    }

    [Fact]
    public async Task Heartbeat_ClosedActivationGate_MakesZeroRedisOrManagerCalls()
    {
        var gate = new TickerQActivationGate();
        var context = Substitute.For<ITickerQRedisContext>();
        var manager = Substitute.For<IInternalTickerManager>();
        var service = new NodeHeartBeatBackgroundService(
            new ServiceExtension.TickerQRedisOptionBuilder
            {
                NodeHeartbeatInterval = TimeSpan.FromMilliseconds(1)
            },
            context,
            manager,
            NullLogger<NodeHeartBeatBackgroundService>.Instance,
            gate);

        await service.StartAsync(CancellationToken.None);
        await Task.Delay(100);
        await service.StopAsync(CancellationToken.None);

        await context.DidNotReceive().NotifyNodeAliveAsync(Arg.Any<CancellationToken>());
        await context.DidNotReceive().GetDeadNodesAsync(Arg.Any<CancellationToken>());
        await manager.DidNotReceive().ReleaseDeadNodeResources(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Heartbeat_RegistersProcessOwnerWhilePublishingLogicalNode()
    {
        var services = new ServiceCollection();
        services.AddDistributedMemoryCache();
        await using var provider = services.BuildServiceProvider();
        var cache = provider.GetRequiredService<IDistributedCache>();
        var options = new SchedulerOptionsBuilder { NodeIdentifier = "logical-node" };
        var sender = Substitute.For<ITickerQNotificationHubSender>();
        var published = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        sender.UpdateNodeHeartBeatAsync(Arg.Do<JsonElement>(value => published.TrySetResult(value)))
            .Returns(Task.CompletedTask);
        var database = Substitute.For<IDatabase>();
        var context = new TickerQRedisContext(
            cache, options, new ServiceExtension.TickerQRedisOptionBuilder(), sender, database: database);

        await context.NotifyNodeAliveAsync();

        var keys = new RedisKeyBuilder(options.RuntimePartition ?? TickerQ.Utilities.Models.TickerQRuntimePartition.LegacyGlobal);
        Assert.NotNull(await cache.GetStringAsync(keys.Heartbeat(options.ExecutionOwnerId)));
        Assert.Null(await cache.GetStringAsync(keys.Heartbeat("logical-node")));
        await database.Received(1).SetAddAsync(keys.NodesRegistryV2, options.ExecutionOwnerId);
        var payload = await published.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("logical-node", payload.GetProperty("Node").GetString());
    }
}
