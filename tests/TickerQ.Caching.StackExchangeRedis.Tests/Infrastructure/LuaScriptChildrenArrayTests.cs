using System.Text.Json;
using StackExchange.Redis;
using Testcontainers.Redis;
using TickerQ.Caching.StackExchangeRedis.Infrastructure;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using Xunit;

namespace TickerQ.Caching.StackExchangeRedis.Tests.Infrastructure;

/// <summary>
/// Regression tests for issue #856: Redis's bundled cjson can't tell an empty
/// JSON array from an empty JSON object once decoded into a Lua table, so the
/// Acquire/Release/RecoverDeadNode Lua scripts silently rewrote an empty
/// TimeTickerEntity.Children ([]) as {} on every lock-state transition,
/// breaking deserialization on the next read. Runs the real embedded Lua
/// scripts against a real Redis container (NSubstitute mocks of IDatabase,
/// used elsewhere in this test project, don't execute real Lua/cjson and so
/// can't catch this class of bug).
/// </summary>
public class LuaScriptChildrenArrayTests : IAsyncLifetime
{
    private RedisContainer _redisContainer = null!;
    private ConnectionMultiplexer _connection = null!;
    private IDatabase _db = null!;

    public async Task InitializeAsync()
    {
        _redisContainer = new RedisBuilder().WithImage("redis:7-alpine").Build();
        await _redisContainer.StartAsync();
        _connection = await ConnectionMultiplexer.ConnectAsync(_redisContainer.GetConnectionString());
        _db = _connection.GetDatabase();
    }

    public async Task DisposeAsync()
    {
        await _connection.DisposeAsync();
        await _redisContainer.DisposeAsync();
    }

    private static JsonSerializerOptions JsonOptions() => new()
    {
        TypeInfoResolverChain = { RedisContextJsonSerializerContext.Default }
    };

    private static string SerializeEntity(TimeTickerEntity entity)
    {
        var opts = JsonOptions();
        return JsonSerializer.Serialize(entity, opts.GetTypeInfo(typeof(TimeTickerEntity)));
    }

    private static TimeTickerEntity DeserializeEntity(string json)
    {
        var opts = JsonOptions();
        return (TimeTickerEntity)JsonSerializer.Deserialize(json, opts.GetTypeInfo(typeof(TimeTickerEntity)))!;
    }

    [Fact]
    public async Task Acquire_PreservesEmptyChildrenAsArray_SoTheEntityStillDeserializes()
    {
        var entity = new TimeTickerEntity { Id = Guid.NewGuid(), Function = "MyJob", Status = TickerStatus.Idle };
        var key = $"tq:tt:{entity.Id}";
        await _db.StringSetAsync(key, SerializeEntity(entity));

        var script = LuaScriptLoader.Load("Acquire");
        var result = await _db.ScriptEvaluateAsync(
            script,
            [(RedisKey)key],
            [
                (RedisValue)"node1", (RedisValue)DateTime.UtcNow.ToString("O"),
                (RedisValue)(int)TickerStatus.InProgress, (RedisValue)"",
                (RedisValue)(int)TickerStatus.Idle, (RedisValue)(int)TickerStatus.Queued
            ]);

        Assert.False(result.IsNull);

        // This is the exact failure mode from the reported bug: deserializing
        // the entity the Lua script wrote back throws JsonException if
        // Children was corrupted from [] to {}.
        var rehydrated = DeserializeEntity((string)result!);
        Assert.Empty(rehydrated.Children);
    }

    [Fact]
    public async Task Acquire_LeavesPopulatedChildrenUntouched()
    {
        var child = new TimeTickerEntity { Id = Guid.NewGuid(), Function = "ChildJob" };
        var entity = new TimeTickerEntity
        {
            Id = Guid.NewGuid(), Function = "ParentJob", Status = TickerStatus.Idle,
            Children = [child]
        };
        var key = $"tq:tt:{entity.Id}";
        await _db.StringSetAsync(key, SerializeEntity(entity));

        var script = LuaScriptLoader.Load("Acquire");
        var result = await _db.ScriptEvaluateAsync(
            script,
            [(RedisKey)key],
            [
                (RedisValue)"node1", (RedisValue)DateTime.UtcNow.ToString("O"),
                (RedisValue)(int)TickerStatus.InProgress, (RedisValue)"",
                (RedisValue)(int)TickerStatus.Idle, (RedisValue)(int)TickerStatus.Queued
            ]);

        Assert.False(result.IsNull);
        var rehydrated = DeserializeEntity((string)result!);
        Assert.Single(rehydrated.Children);
        Assert.Equal(child.Id, rehydrated.Children.Single().Id);
    }

    [Fact]
    public async Task Release_PreservesEmptyChildrenAsArray_SoTheEntityStillDeserializes()
    {
        var entity = new TimeTickerEntity
        {
            Id = Guid.NewGuid(), Function = "MyJob", Status = TickerStatus.Queued, LockHolder = "node1"
        };
        var key = $"tq:tt:{entity.Id}";
        await _db.StringSetAsync(key, SerializeEntity(entity));

        var script = LuaScriptLoader.Load("Release");
        var result = await _db.ScriptEvaluateAsync(
            script,
            [(RedisKey)key],
            [
                (RedisValue)"node1", (RedisValue)DateTime.UtcNow.ToString("O"),
                (RedisValue)(int)TickerStatus.Idle, (RedisValue)(int)TickerStatus.Queued
            ]);

        Assert.False(result.IsNull);
        var rehydrated = DeserializeEntity((string)result!);
        Assert.Empty(rehydrated.Children);
    }

    [Fact]
    public async Task RecoverDeadNode_PreservesEmptyChildrenAsArray_SoTheEntityStillDeserializes()
    {
        var entity = new TimeTickerEntity
        {
            Id = Guid.NewGuid(), Function = "MyJob", Status = TickerStatus.InProgress, LockHolder = "dead-node"
        };
        var key = $"tq:tt:{entity.Id}";
        await _db.StringSetAsync(key, SerializeEntity(entity));

        var script = LuaScriptLoader.Load("RecoverDeadNode");
        var result = await _db.ScriptEvaluateAsync(
            script,
            [(RedisKey)key],
            [
                (RedisValue)"dead-node", (RedisValue)DateTime.UtcNow.ToString("O"),
                (RedisValue)(int)TickerStatus.Idle, (RedisValue)(int)TickerStatus.Queued,
                (RedisValue)(int)TickerStatus.InProgress
            ]);

        Assert.False(result.IsNull);
        var rehydrated = DeserializeEntity((string)result!);
        Assert.Empty(rehydrated.Children);
    }
}
