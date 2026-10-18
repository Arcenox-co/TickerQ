using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using TickerQ.Caching.StackExchangeRedis.Infrastructure;
using TickerQ.Tests.Shared;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces;
using TickerQ.Caching.StackExchangeRedis.Helpers;
using System.Text.Json.Nodes;
using TickerQ.Utilities.Enums;
using static TickerQ.Caching.StackExchangeRedis.DependencyInjection.ServiceExtension;

namespace TickerQ.Caching.StackExchangeRedis.Tests.Infrastructure;

[Collection("RedisRealScript")]
public sealed class CronChainTests : CronChainContract<TimeTickerEntity, CronTickerEntity>
{
    private readonly RedisRealScriptFixture _fixture;
    protected override ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity> Provider { get; }
    protected override DateTime Now => new(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc);
    public CronChainTests(RedisRealScriptFixture fixture)
    {
        _fixture = fixture;
        fixture.Db.Execute("FLUSHALL");
        var clock = Substitute.For<ITickerClock>(); clock.UtcNow.Returns(Now);
        Provider = new TickerRedisPersistenceProvider<TimeTickerEntity, CronTickerEntity>(fixture.Db, clock,
            new SchedulerOptionsBuilder { FallbackBatchSize = 2 }, new TickerQRedisOptionBuilder { JsonSerializerContext = TestJsonSerializerContext.Default },
            NullLogger<TickerRedisPersistenceProvider<TimeTickerEntity, CronTickerEntity>>.Instance);
    }

    [Fact]
    public async Task Publication_requires_a_valid_exact_active_epoch_before_any_write()
    {
        var db = _fixture.Db;
        StackExchange.Redis.RedisKey[] keys = Enumerable.Range(1, 10).Select(i => (StackExchange.Redis.RedisKey)$"chain-epoch-test:{i}").ToArray();
        await db.StringSetAsync(keys[0], "{}"); await db.StringSetAsync(keys[1], "{}");
        StackExchange.Redis.RedisValue[] args = ["{}", "{}", "{\"Status\":0,\"Children\":[]}", "{\"Status\":3}", "{\"Status\":7}", Guid.NewGuid().ToString(), 100, 0, 1, "scoped"];
        var script = LuaScriptLoader.Load("MaterializeCronChain");
        Assert.Equal(4, (long)await db.ScriptEvaluateAsync(script, keys, args));
        await db.HashSetAsync(keys[6], [new("epoch", "1"), new("phase", "1"), new("checkpoint", "ready")]);
        Assert.Equal(4, (long)await db.ScriptEvaluateAsync(script, keys, args));
        await db.HashSetAsync(keys[6], "phase", "2");
        await db.HashSetAsync(keys[6], "epoch", "2");
        Assert.Equal(4, (long)await db.ScriptEvaluateAsync(script, keys, args));
        await db.HashSetAsync(keys[6], "epoch", "1");
        await db.HashSetAsync(keys[6], "unexpected", "corrupt");
        await Assert.ThrowsAsync<StackExchange.Redis.RedisServerException>(() => db.ScriptEvaluateAsync(script, keys, args));
        Assert.False(await db.KeyExistsAsync(keys[2]));
        Assert.Equal("{}", (string?)await db.StringGetAsync(keys[0]));
        await db.HashDeleteAsync(keys[6], "unexpected");
        Assert.Equal(1, (long)await db.ScriptEvaluateAsync(script, keys, args));
        Assert.True(await db.KeyExistsAsync(keys[2]));
    }

}
