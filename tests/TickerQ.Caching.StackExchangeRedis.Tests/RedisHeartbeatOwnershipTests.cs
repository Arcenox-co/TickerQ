using System.Text.Json;
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

        await context.DidNotReceive().NotifyNodeAliveAsync();
        await context.DidNotReceive().GetDeadNodesAsync();
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
        JsonElement payload = default;
        sender.UpdateNodeHeartBeatAsync(Arg.Do<JsonElement>(value => payload = value))
            .Returns(Task.CompletedTask);
        var context = new TickerQRedisContext(
            cache, options, new ServiceExtension.TickerQRedisOptionBuilder(), sender);

        await context.NotifyNodeAliveAsync();

        var keys = new RedisKeyBuilder(options.RuntimePartition ?? TickerQ.Utilities.Models.TickerQRuntimePartition.LegacyGlobal);
        Assert.NotNull(await cache.GetStringAsync(keys.Heartbeat(options.ExecutionOwnerId)));
        Assert.Null(await cache.GetStringAsync(keys.Heartbeat("logical-node")));
        var registry = JsonSerializer.Deserialize<HashSet<string>>(
            (await cache.GetStringAsync(keys.NodesRegistry))!);
        Assert.Contains(options.ExecutionOwnerId, registry!);
        Assert.Equal("logical-node", payload.GetProperty("Node").GetString());
    }
}
