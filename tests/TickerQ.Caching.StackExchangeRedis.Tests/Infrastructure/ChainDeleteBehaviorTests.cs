using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using TickerQ.Caching.StackExchangeRedis.Infrastructure;
using TickerQ.Tests.Shared;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces;
using TickerQ.Caching.StackExchangeRedis.Helpers;
using System.Text.Json.Nodes;
using StackExchange.Redis;
using TickerQ.Utilities.Enums;
using static TickerQ.Caching.StackExchangeRedis.DependencyInjection.ServiceExtension;

namespace TickerQ.Caching.StackExchangeRedis.Tests.Infrastructure;

[Collection("RedisRealScript")]
public sealed class ChainDeleteBehaviorTests : ChainDeleteBehaviorContract<TimeTickerEntity, CronTickerEntity>
{
    private readonly RedisRealScriptFixture _fixture;
    protected override ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity> Provider { get; }
    protected override DateTime Now => new(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc);
    public ChainDeleteBehaviorTests(RedisRealScriptFixture fixture)
    {
        _fixture = fixture;
        fixture.Db.Execute("FLUSHALL");
        var clock = Substitute.For<ITickerClock>(); clock.UtcNow.Returns(Now);
        Provider = new TickerRedisPersistenceProvider<TimeTickerEntity, CronTickerEntity>(fixture.Db, clock,
            new SchedulerOptionsBuilder(), new TickerQRedisOptionBuilder { JsonSerializerContext = TestJsonSerializerContext.Default },
            NullLogger<TickerRedisPersistenceProvider<TimeTickerEntity, CronTickerEntity>>.Instance);
    }

    protected override async Task SetDescendantStatusAsync(Guid rootId, Guid id, TickerStatus status)
    {
        // This deletion-policy fixture includes deliberately invalid lifecycle states (99).
        // Simulate their repair directly; configuration updates must never change execution state.
        var key = RedisKeyBuilder.TimeTickerKey(rootId);
        var original = await _fixture.Db.StringGetAsync(key);
        var root = JsonNode.Parse((string)original!)!;
        var pending = new Stack<JsonNode>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var node = pending.Pop();
            if (node["Id"]!.GetValue<Guid>() == id)
            {
                node["Status"] = (int)status;
                var changed = await _fixture.Db.ScriptEvaluateAsync(
                    "if redis.call('GET', KEYS[1]) ~= ARGV[1] then return 0 end redis.call('SET', KEYS[1], ARGV[2]) return 1",
                    new RedisKey[] { key }, new RedisValue[] { original, root.ToJsonString() });
                Assert.Equal(1, (long)changed);
                return;
            }
            if (node["Children"] is JsonArray children)
                foreach (var child in children) if (child != null) pending.Push(child);
        }
        Assert.Fail("Test descendant not found.");
    }

    [Fact]
    public async Task Legacy_document_without_policy_keeps_default_cascade_behavior()
    {
        var root = Chain(ChainDeleteBehavior.Cascade);
        await Provider.AddTimeTickers([root]);
        var key = RedisKeyBuilder.TimeTickerKey(root.Id);
        var json = JsonNode.Parse((string)(await _fixture.Db.StringGetAsync(key))!)!;
        json.AsObject().Remove("DeleteBehavior");
        await _fixture.Db.StringSetAsync(key, json.ToJsonString());
        Assert.Equal(ChainDeleteBehavior.Cascade, (await Provider.GetTimeTickerById(root.Id)).DeleteBehavior);
        Assert.True(await Provider.RemoveTimeTickers([root.Id]) > 0);
        Assert.Null(await Provider.GetTimeTickerById(root.Id));
    }

    [Fact]
    public async Task Policy_change_requires_the_bound_activation_epoch()
    {
        var clock = Substitute.For<ITickerClock>(); clock.UtcNow.Returns(Now);
        var options = new SchedulerOptionsBuilder { ReconciliationEpoch = 5 };
        options.BindRuntimeActivationScope("chain-policy-test", 5, schedulerEnabled: true);
        var provider = new TickerRedisPersistenceProvider<TimeTickerEntity, CronTickerEntity>(_fixture.Db, clock,
            options, new TickerQRedisOptionBuilder { JsonSerializerContext = TestJsonSerializerContext.Default },
            NullLogger<TickerRedisPersistenceProvider<TimeTickerEntity, CronTickerEntity>>.Instance);
        await provider.BeginReconciliationActivationEpochAsync(5);
        await provider.CommitReconciliationActivationEpochAsync(5);
        var root = Chain(ChainDeleteBehavior.Restrict);
        Assert.Equal(1, await provider.AddTimeTickers([root]));
        Assert.Equal(1, await provider.SetTimeTickerDeleteBehaviorAsync(root.Id, ChainDeleteBehavior.Cascade));
        await provider.BeginReconciliationActivationEpochAsync(6);
        Assert.Equal(0, await provider.SetTimeTickerDeleteBehaviorAsync(root.Id, ChainDeleteBehavior.Restrict));
        await provider.CommitReconciliationActivationEpochAsync(6);
        Assert.Equal(0, await provider.SetTimeTickerDeleteBehaviorAsync(root.Id, ChainDeleteBehavior.Restrict));
        Assert.Equal(ChainDeleteBehavior.Cascade, (await provider.GetTimeTickerById(root.Id)).DeleteBehavior);
    }
}
