using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Options;
using NSubstitute;
using TickerQ.Caching.StackExchangeRedis.DependencyInjection;
using TickerQ.Caching.StackExchangeRedis.Helpers;
using TickerQ.Utilities;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ.Caching.StackExchangeRedis.Tests;

public sealed class RedisHeartbeatIntegrationTests(ArrayCacheRedisFixture fixture) : IClassFixture<ArrayCacheRedisFixture>
{
    [Fact]
    public async Task ConcurrentRegistration_DiscoveryAndAcknowledgement_DoNotLoseOtherOwners()
    {
        var prefix = $"heartbeat:{Guid.NewGuid():N}:";
        using var cache = new RedisCache(Options.Create(new RedisCacheOptions
        {
            Configuration = fixture.Container.GetConnectionString(), InstanceName = prefix
        }));
        var keys = new RedisKeyBuilder(TickerQRuntimePartition.LegacyGlobal);
        var options = Enumerable.Range(0, 64).Select(i => new SchedulerOptionsBuilder { NodeIdentifier = $"node-{i}" }).ToArray();
        var contexts = options.Select(o => new TickerQRedisContext(cache, o,
            new ServiceExtension.TickerQRedisOptionBuilder { InstanceName = prefix },
            Substitute.For<ITickerQNotificationHubSender>(), database: fixture.Connection.GetDatabase())).ToArray();
        await Task.WhenAll(contexts.Select(c => c.NotifyNodeAliveAsync()));
        Assert.Equal(64, await fixture.Connection.GetDatabase().SetLengthAsync(prefix + keys.NodesRegistryV2));
        Assert.Empty(await contexts[0].GetDeadNodesAsync());
        foreach (var option in options.Take(32)) await cache.RemoveAsync(keys.Heartbeat(option.ExecutionOwnerId));
        var dead = await contexts[63].GetDeadNodesAsync();
        Assert.Equal(32, dead.Length);
        Assert.Equal(32, (await contexts[63].GetDeadNodesAsync()).Length); // Discovery never acknowledges.
        // A heartbeat renewed after discovery must not be removed by an older scan's acknowledgement.
        await contexts[0].NotifyNodeAliveAsync();
        await contexts[63].AcknowledgeDeadNodeAsync(options[0].ExecutionOwnerId);
        Assert.True(await fixture.Connection.GetDatabase().SetContainsAsync(prefix + keys.NodesRegistryV2, options[0].ExecutionOwnerId));
        await Task.WhenAll(dead.Skip(0).Where(x => x != options[0].ExecutionOwnerId)
            .Select(x => contexts[63].AcknowledgeDeadNodeAsync(x)));
        Assert.Equal(33, await fixture.Connection.GetDatabase().SetLengthAsync(prefix + keys.NodesRegistryV2));
        Assert.Empty(await contexts[63].GetDeadNodesAsync());
        // A different Redis instance prefix must never discover or acknowledge these owners.
        var otherPrefix = prefix + "other:";
        using var otherCache = new RedisCache(Options.Create(new RedisCacheOptions
        { Configuration = fixture.Container.GetConnectionString(), InstanceName = otherPrefix }));
        var other = new TickerQRedisContext(otherCache, new(), new() { InstanceName = otherPrefix },
            Substitute.For<ITickerQNotificationHubSender>(), database: fixture.Connection.GetDatabase());
        Assert.Empty(await other.GetDeadNodesAsync());
    }

    [Fact]
    public async Task LegacyJsonRegistry_RemainsRecoverable_WithoutRemovingLiveOrUnrelatedOwners()
    {
        var prefix = $"legacy-heartbeat:{Guid.NewGuid():N}:";
        using var cache = new RedisCache(Options.Create(new RedisCacheOptions
        { Configuration = fixture.Container.GetConnectionString(), InstanceName = prefix }));
        var keys = new RedisKeyBuilder(TickerQRuntimePartition.LegacyGlobal);
        await cache.SetStringAsync(keys.NodesRegistry, JsonSerializer.Serialize(new[] { "old-dead", "old-live" }),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1) });
        await cache.SetStringAsync(keys.Heartbeat("old-live"), "alive");
        var context = new TickerQRedisContext(cache, new(), new() { InstanceName = prefix },
            Substitute.For<ITickerQNotificationHubSender>(), database: fixture.Connection.GetDatabase());
        Assert.Equal("old-dead", Assert.Single(await context.GetDeadNodesAsync()));
        await context.AcknowledgeDeadNodeAsync("old-live");
        Assert.Equal(2, JsonSerializer.Deserialize<string[]>((await cache.GetStringAsync(keys.NodesRegistry))!)!.Length);
        await context.AcknowledgeDeadNodeAsync("old-dead");
        Assert.Equal("old-live", Assert.Single(JsonSerializer.Deserialize<string[]>((await cache.GetStringAsync(keys.NodesRegistry))!)!));
        Assert.NotNull(await fixture.Connection.GetDatabase().KeyTimeToLiveAsync(prefix + keys.NodesRegistry));
        await cache.RemoveAsync(keys.Heartbeat("old-live"));
        await context.AcknowledgeDeadNodeAsync("old-live");
        Assert.Null(await cache.GetStringAsync(keys.NodesRegistry));
    }
}
