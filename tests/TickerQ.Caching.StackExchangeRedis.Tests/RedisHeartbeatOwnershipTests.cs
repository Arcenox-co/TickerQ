using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using TickerQ.Caching.StackExchangeRedis.DependencyInjection;
using TickerQ.Utilities;
using TickerQ.Utilities.Interfaces;

namespace TickerQ.Caching.StackExchangeRedis.Tests;

public class RedisHeartbeatOwnershipTests
{
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

        Assert.NotNull(await cache.GetStringAsync($"hb:{options.ExecutionOwnerId}"));
        Assert.Null(await cache.GetStringAsync("hb:logical-node"));
        var registry = JsonSerializer.Deserialize<HashSet<string>>(
            (await cache.GetStringAsync("nodes:registry"))!);
        Assert.Contains(options.ExecutionOwnerId, registry!);
        Assert.Equal("logical-node", payload.GetProperty("Node").GetString());
    }
}
