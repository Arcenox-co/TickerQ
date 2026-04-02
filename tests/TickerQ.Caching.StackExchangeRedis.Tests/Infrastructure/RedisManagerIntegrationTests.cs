using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using StackExchange.Redis;
using TickerQ.Caching.StackExchangeRedis.Infrastructure;
using static TickerQ.Caching.StackExchangeRedis.DependencyInjection.ServiceExtension;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Managers;
using TickerQ.Utilities.Models;

namespace TickerQ.Caching.StackExchangeRedis.Tests.Infrastructure;

/// <summary>
/// Integration tests that wire real TickerManager and InternalTickerManager
/// through the Redis persistence provider (with mocked IDatabase).
/// Verifies that manager operations correctly persist/retrieve data via Redis.
/// </summary>
[Collection("TickerFunctionProviderState")]
public class RedisManagerIntegrationTests : IAsyncLifetime, IDisposable
{
    private IDatabase _db = null!;
    private ITickerClock _clock = null!;
    private ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity> _provider = null!;
    private ITimeTickerManager<TimeTickerEntity> _timeTickerManager = null!;
    private ICronTickerManager<CronTickerEntity> _cronTickerManager = null!;
    private IInternalTickerManager _internalManager = null!;
    private ITickerQDispatcher _dispatcher = null!;
    private DateTime _fixedNow;
    private const string NodeId = "test-node-1";
    private const string ValidFunction = "TestFunction";
    private const string Prefix = "tq";

    // In-memory stores backing the mock
    private readonly Dictionary<string, string> _store = new();
    private readonly Dictionary<string, HashSet<string>> _sets = new();
    private readonly Dictionary<string, SortedList<double, string>> _sortedSets = new();
    private JsonSerializerOptions _jsonOptions = null!;

    public Task InitializeAsync()
    {
        _fixedNow = new DateTime(2025, 6, 15, 12, 0, 0, DateTimeKind.Utc);

        _clock = Substitute.For<ITickerClock>();
        _clock.UtcNow.Returns(_fixedNow);

        _db = Substitute.For<IDatabase>();

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = false,
            TypeInfoResolverChain = { TestJsonSerializerContext.Default, RedisContextJsonSerializerContext.Default }
        };

        SetupDatabaseMock();

        var schedulerOptions = new SchedulerOptionsBuilder { NodeIdentifier = NodeId };
        var redisOptions = new TickerQRedisOptionBuilder
        {
            JsonSerializerContext = TestJsonSerializerContext.Default
        };
        var logger = new NullLogger<TickerRedisPersistenceProvider<TimeTickerEntity, CronTickerEntity>>();

        _provider = new TickerRedisPersistenceProvider<TimeTickerEntity, CronTickerEntity>(
            _db, _clock, schedulerOptions, redisOptions, logger);

        // Register test function
        TickerFunctionProvider.RegisterFunctions(
            new Dictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate, int)>
            {
                [ValidFunction] = ("", TickerTaskPriority.Normal, (_, _, _) => Task.CompletedTask, 0)
            });
        TickerFunctionProvider.Build();

        // Wire managers with real Redis provider
        var notificationHub = Substitute.For<ITickerQNotificationHubSender>();
        notificationHub.AddTimeTickerNotifyAsync(Arg.Any<Guid>()).Returns(Task.CompletedTask);
        notificationHub.AddCronTickerNotifyAsync(Arg.Any<Guid>()).Returns(Task.CompletedTask);

        var hostScheduler = Substitute.For<ITickerQHostScheduler>();
        _dispatcher = Substitute.For<ITickerQDispatcher>();
        _dispatcher.IsEnabled.Returns(false);
        _dispatcher.DispatchAsync(Arg.Any<InternalFunctionContext[]>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var executionContext = new TickerExecutionContext();

        var tickerManager = new TickerManager<TimeTickerEntity, CronTickerEntity>(
            _provider, hostScheduler, _clock, notificationHub, executionContext, _dispatcher);

        _timeTickerManager = tickerManager;
        _cronTickerManager = tickerManager;

        _internalManager = new InternalTickerManager<TimeTickerEntity, CronTickerEntity>(
            _provider, _clock, notificationHub);

        return Task.CompletedTask;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        TickerFunctionProvider.Build();
    }

    #region IDatabase Mock (reuses same pattern as RedisPersistenceProviderTests)

    private void SetupDatabaseMock()
    {
        // StringGet
        _db.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(ci =>
            {
                var key = (string)ci.ArgAt<RedisKey>(0);
                return _store.TryGetValue(key, out var val) ? (RedisValue)val : RedisValue.Null;
            });

        // MGET
        _db.StringGetAsync(Arg.Any<RedisKey[]>(), Arg.Any<CommandFlags>())
            .Returns(ci =>
            {
                var keys = ci.ArgAt<RedisKey[]>(0);
                var results = new RedisValue[keys.Length];
                for (var i = 0; i < keys.Length; i++)
                {
                    var k = (string)keys[i];
                    results[i] = _store.TryGetValue(k, out var v) ? (RedisValue)v : RedisValue.Null;
                }
                return results;
            });

        // StringSet
        _db.StringSetAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<TimeSpan?>(), Arg.Any<bool>(), Arg.Any<When>(), Arg.Any<CommandFlags>())
            .Returns(ci =>
            {
                _store[(string)ci.ArgAt<RedisKey>(0)] = (string)ci.ArgAt<RedisValue>(1);
                return true;
            });

        // KeyDelete
        _db.KeyDeleteAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(ci => _store.Remove((string)ci.ArgAt<RedisKey>(0)));

        // SetMembers
        _db.SetMembersAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(ci =>
            {
                var key = (string)ci.ArgAt<RedisKey>(0);
                if (!_sets.TryGetValue(key, out var set)) return Array.Empty<RedisValue>();
                return set.Select(s => (RedisValue)s).ToArray();
            });

        // SetAdd
        _db.SetAddAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<CommandFlags>())
            .Returns(ci =>
            {
                var key = (string)ci.ArgAt<RedisKey>(0);
                var member = (string)ci.ArgAt<RedisValue>(1);
                if (!_sets.ContainsKey(key)) _sets[key] = [];
                return _sets[key].Add(member);
            });

        // SetRemove
        _db.SetRemoveAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<CommandFlags>())
            .Returns(ci =>
            {
                var key = (string)ci.ArgAt<RedisKey>(0);
                var member = (string)ci.ArgAt<RedisValue>(1);
                return _sets.TryGetValue(key, out var set) && set.Remove(member);
            });

        // SortedSetRangeByScore
        _db.SortedSetRangeByScoreAsync(Arg.Any<RedisKey>(), Arg.Any<double>(), Arg.Any<double>(),
                Arg.Any<Exclude>(), Arg.Any<Order>(), Arg.Any<long>(), Arg.Any<long>(), Arg.Any<CommandFlags>())
            .Returns(ci =>
            {
                var key = (string)ci.ArgAt<RedisKey>(0);
                if (!_sortedSets.TryGetValue(key, out var ss)) return Array.Empty<RedisValue>();
                return ss.Where(kv => kv.Key >= ci.ArgAt<double>(1) && kv.Key <= ci.ArgAt<double>(2))
                    .Select(kv => (RedisValue)kv.Value).ToArray();
            });

        // SortedSetRangeByScoreWithScores
        _db.SortedSetRangeByScoreWithScoresAsync(Arg.Any<RedisKey>(), Arg.Any<double>(), Arg.Any<double>(),
                Arg.Any<Exclude>(), Arg.Any<Order>(), Arg.Any<long>(), Arg.Any<long>(), Arg.Any<CommandFlags>())
            .Returns(ci =>
            {
                var key = (string)ci.ArgAt<RedisKey>(0);
                if (!_sortedSets.TryGetValue(key, out var ss)) return Array.Empty<SortedSetEntry>();
                return ss.Where(kv => kv.Key >= ci.ArgAt<double>(1) && kv.Key <= ci.ArgAt<double>(2))
                    .Skip((int)ci.ArgAt<long>(5)).Take((int)ci.ArgAt<long>(6))
                    .Select(kv => new SortedSetEntry(kv.Value, kv.Key)).ToArray();
            });

        // SortedSetAdd
        _db.SortedSetAddAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<double>(), Arg.Any<CommandFlags>())
            .Returns(ci =>
            {
                var key = (string)ci.ArgAt<RedisKey>(0);
                if (!_sortedSets.ContainsKey(key)) _sortedSets[key] = new SortedList<double, string>();
                _sortedSets[key][ci.ArgAt<double>(2)] = (string)ci.ArgAt<RedisValue>(1);
                return true;
            });

        // SortedSetRemove
        _db.SortedSetRemoveAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<CommandFlags>())
            .Returns(ci =>
            {
                var key = (string)ci.ArgAt<RedisKey>(0);
                if (!_sortedSets.TryGetValue(key, out var ss)) return false;
                var toRemove = ss.Where(kv => kv.Value == (string)ci.ArgAt<RedisValue>(1)).Select(kv => kv.Key).ToList();
                foreach (var k in toRemove) ss.Remove(k);
                return toRemove.Count > 0;
            });

        // Batch
        var batch = Substitute.For<IBatch>();
        batch.SetAddAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<CommandFlags>())
            .Returns(ci => { if (!_sets.ContainsKey((string)ci.ArgAt<RedisKey>(0))) _sets[(string)ci.ArgAt<RedisKey>(0)] = []; _sets[(string)ci.ArgAt<RedisKey>(0)].Add((string)ci.ArgAt<RedisValue>(1)); return true; });
        batch.SetRemoveAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<CommandFlags>())
            .Returns(ci => _sets.TryGetValue((string)ci.ArgAt<RedisKey>(0), out var s) && s.Remove((string)ci.ArgAt<RedisValue>(1)));
        batch.SortedSetAddAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<double>(), Arg.Any<CommandFlags>())
            .Returns(ci => { var k = (string)ci.ArgAt<RedisKey>(0); if (!_sortedSets.ContainsKey(k)) _sortedSets[k] = new SortedList<double, string>(); _sortedSets[k][ci.ArgAt<double>(2)] = (string)ci.ArgAt<RedisValue>(1); return true; });
        batch.SortedSetRemoveAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<CommandFlags>())
            .Returns(ci => { var k = (string)ci.ArgAt<RedisKey>(0); if (!_sortedSets.TryGetValue(k, out var ss)) return false; var r = ss.Where(kv => kv.Value == (string)ci.ArgAt<RedisValue>(1)).Select(kv => kv.Key).ToList(); foreach (var x in r) ss.Remove(x); return r.Count > 0; });
        _db.CreateBatch(Arg.Any<object>()).Returns(batch);

        // ScriptEvaluate (Lua simulation)
        _db.ScriptEvaluateAsync(Arg.Any<string>(), Arg.Any<RedisKey[]?>(), Arg.Any<RedisValue[]?>(), Arg.Any<CommandFlags>())
            .Returns(ci =>
            {
                var keys = ci.ArgAt<RedisKey[]>(1);
                var argv = ci.ArgAt<RedisValue[]>(2);
                var entityKey = (string)keys[0];

                if (!_store.TryGetValue(entityKey, out var json))
                    return RedisResult.Create(RedisValue.Null);

                var obj = JsonSerializer.Deserialize<JsonElement>(json);
                var status = obj.GetProperty("Status").GetInt32();
                var lockHolder = obj.TryGetProperty("LockHolder", out var lh) && lh.ValueKind != JsonValueKind.Null ? lh.GetString() : null;

                if (argv.Length == 5) // RecoverDeadNode
                {
                    if (lockHolder != (string)argv[0]) return RedisResult.Create(RedisValue.Null);
                    if (status != int.Parse(argv[2].ToString()) && status != int.Parse(argv[3].ToString()) && status != int.Parse(argv[4].ToString()))
                        return RedisResult.Create(RedisValue.Null);
                    var updated = SetJsonProps(json, new() { ["LockHolder"] = null, ["LockedAt"] = null, ["Status"] = int.Parse(argv[2].ToString()), ["UpdatedAt"] = (string)argv[1] });
                    _store[entityKey] = updated;
                    return RedisResult.Create((RedisValue)updated);
                }
                if (argv.Length == 4) // Release
                {
                    if (status != int.Parse(argv[2].ToString()) && status != int.Parse(argv[3].ToString())) return RedisResult.Create(RedisValue.Null);
                    if (!string.IsNullOrEmpty(lockHolder) && lockHolder != (string)argv[0]) return RedisResult.Create(RedisValue.Null);
                    var updated = SetJsonProps(json, new() { ["LockHolder"] = null, ["LockedAt"] = null, ["Status"] = int.Parse(argv[2].ToString()), ["UpdatedAt"] = (string)argv[1] });
                    _store[entityKey] = updated;
                    return RedisResult.Create((RedisValue)updated);
                }
                // Acquire (6 args)
                {
                    if (status != int.Parse(argv[4].ToString()) && status != int.Parse(argv[5].ToString())) return RedisResult.Create(RedisValue.Null);
                    if (!string.IsNullOrEmpty(lockHolder) && lockHolder != (string)argv[0]) return RedisResult.Create(RedisValue.Null);
                    var expectedUpdatedAt = (string)argv[3];
                    if (!string.IsNullOrEmpty(expectedUpdatedAt))
                    {
                        var cur = obj.TryGetProperty("UpdatedAt", out var ua) ? ua.GetString() : null;
                        if (cur == null || !DateTime.TryParse(cur, out var cd) || !DateTime.TryParse(expectedUpdatedAt, out var ed) || cd != ed)
                            return RedisResult.Create(RedisValue.Null);
                    }
                    var updated = SetJsonProps(json, new() { ["LockHolder"] = (string)argv[0], ["LockedAt"] = (string)argv[1], ["Status"] = int.Parse(argv[2].ToString()), ["UpdatedAt"] = (string)argv[1] });
                    _store[entityKey] = updated;
                    return RedisResult.Create((RedisValue)updated);
                }
            });
    }

    private string SetJsonProps(string json, Dictionary<string, object?> props)
    {
        using var doc = JsonDocument.Parse(json);
        using var ms = new MemoryStream();
        using (var writer = new Utf8JsonWriter(ms))
        {
            writer.WriteStartObject();
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (props.TryGetValue(prop.Name, out var newVal))
                {
                    writer.WritePropertyName(prop.Name);
                    switch (newVal) { case null: writer.WriteNullValue(); break; case int i: writer.WriteNumberValue(i); break; case string s: writer.WriteStringValue(s); break; default: writer.WriteStringValue(newVal.ToString()); break; }
                }
                else prop.WriteTo(writer);
            }
            writer.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(ms.ToArray());
    }

    #endregion

    #region Helpers

    private T? VerifyInStore<T>(string key) where T : class
    {
        if (!_store.TryGetValue(key, out var json)) return null;
        return JsonSerializer.Deserialize<T>(json, _jsonOptions);
    }

    #endregion

    // =========================================================================
    // TimeTickerManager → Redis Provider
    // =========================================================================

    [Fact]
    public async Task TimeTickerManager_AddAsync_PersistsViaRedis()
    {
        var entity = new TimeTickerEntity
        {
            Function = ValidFunction,
            ExecutionTime = _fixedNow.AddMinutes(10)
        };

        var result = await _timeTickerManager.AddAsync(entity, CancellationToken.None);

        Assert.True(result.IsSucceeded);
        Assert.NotNull(result.Result);
        Assert.NotEqual(Guid.Empty, result.Result.Id);

        var stored = VerifyInStore<TimeTickerEntity>($"{Prefix}:tt:{result.Result.Id}");
        Assert.NotNull(stored);
        Assert.Equal(ValidFunction, stored!.Function);
    }

    [Fact]
    public async Task TimeTickerManager_AddBatchAsync_PersistsAllViaRedis()
    {
        var entities = new List<TimeTickerEntity>
        {
            new() { Function = ValidFunction, ExecutionTime = _fixedNow.AddMinutes(10) },
            new() { Function = ValidFunction, ExecutionTime = _fixedNow.AddMinutes(20) },
            new() { Function = ValidFunction, ExecutionTime = _fixedNow.AddMinutes(30) }
        };

        var result = await _timeTickerManager.AddBatchAsync(entities, CancellationToken.None);

        Assert.True(result.IsSucceeded);
        Assert.Equal(3, result.Result.Count);

        foreach (var ticker in result.Result)
        {
            var stored = VerifyInStore<TimeTickerEntity>($"{Prefix}:tt:{ticker.Id}");
            Assert.NotNull(stored);
        }
    }

    [Fact]
    public async Task TimeTickerManager_DeleteAsync_RemovesFromRedis()
    {
        var entity = new TimeTickerEntity
        {
            Function = ValidFunction,
            ExecutionTime = _fixedNow.AddMinutes(10)
        };

        var addResult = await _timeTickerManager.AddAsync(entity, CancellationToken.None);
        Assert.True(addResult.IsSucceeded);

        var deleteResult = await _timeTickerManager.DeleteAsync(addResult.Result.Id, CancellationToken.None);
        Assert.True(deleteResult.IsSucceeded);

        var stored = VerifyInStore<TimeTickerEntity>($"{Prefix}:tt:{addResult.Result.Id}");
        Assert.Null(stored);
    }

    [Fact]
    public async Task TimeTickerManager_UpdateAsync_UpdatesInRedis()
    {
        var entity = new TimeTickerEntity
        {
            Function = ValidFunction,
            ExecutionTime = _fixedNow.AddMinutes(10)
        };

        var addResult = await _timeTickerManager.AddAsync(entity, CancellationToken.None);
        Assert.True(addResult.IsSucceeded);

        addResult.Result.ExecutionTime = _fixedNow.AddMinutes(99);
        var updateResult = await _timeTickerManager.UpdateAsync(addResult.Result, CancellationToken.None);
        Assert.True(updateResult.IsSucceeded);

        var stored = VerifyInStore<TimeTickerEntity>($"{Prefix}:tt:{addResult.Result.Id}");
        Assert.Equal(_fixedNow.AddMinutes(99), stored!.ExecutionTime);
    }

    // =========================================================================
    // CronTickerManager → Redis Provider
    // =========================================================================

    [Fact]
    public async Task CronTickerManager_AddAsync_PersistsViaRedis()
    {
        var entity = new CronTickerEntity
        {
            Function = ValidFunction,
            Expression = "0 0 * * * *"
        };

        var result = await _cronTickerManager.AddAsync(entity, CancellationToken.None);

        Assert.True(result.IsSucceeded);
        Assert.NotNull(result.Result);

        var stored = VerifyInStore<CronTickerEntity>($"{Prefix}:cron:{result.Result.Id}");
        Assert.NotNull(stored);
        Assert.Equal("0 0 * * * *", stored!.Expression);
    }

    [Fact]
    public async Task CronTickerManager_DeleteAsync_RemovesFromRedis()
    {
        var entity = new CronTickerEntity
        {
            Function = ValidFunction,
            Expression = "0 0 * * * *"
        };

        var addResult = await _cronTickerManager.AddAsync(entity, CancellationToken.None);
        Assert.True(addResult.IsSucceeded);

        var deleteResult = await _cronTickerManager.DeleteAsync(addResult.Result.Id, CancellationToken.None);
        Assert.True(deleteResult.IsSucceeded);

        var stored = VerifyInStore<CronTickerEntity>($"{Prefix}:cron:{addResult.Result.Id}");
        Assert.Null(stored);
    }

    [Fact]
    public async Task CronTickerManager_UpdateAsync_UpdatesInRedis()
    {
        var entity = new CronTickerEntity
        {
            Function = ValidFunction,
            Expression = "0 0 * * * *"
        };

        var addResult = await _cronTickerManager.AddAsync(entity, CancellationToken.None);
        Assert.True(addResult.IsSucceeded);

        addResult.Result.Expression = "0 */10 * * * *";
        var updateResult = await _cronTickerManager.UpdateAsync(addResult.Result, CancellationToken.None);
        Assert.True(updateResult.IsSucceeded);

        var stored = VerifyInStore<CronTickerEntity>($"{Prefix}:cron:{addResult.Result.Id}");
        Assert.Equal("0 */10 * * * *", stored!.Expression);
    }

    // =========================================================================
    // InternalTickerManager → Redis Provider
    // =========================================================================

    [Fact]
    public async Task InternalManager_ReleaseDeadNodeResources_WorksViaRedis()
    {
        var deadNode = "dead-node-1";

        // Seed a ticker locked by dead node directly in Redis
        var ticker = new TimeTickerEntity
        {
            Id = Guid.NewGuid(),
            Function = ValidFunction,
            ExecutionTime = _fixedNow.AddMinutes(5),
            Status = TickerStatus.InProgress,
            LockHolder = deadNode,
            LockedAt = _fixedNow.AddMinutes(-10),
            CreatedAt = _fixedNow.AddHours(-1),
            UpdatedAt = _fixedNow.AddHours(-1),
            Request = []
        };

        _store[$"{Prefix}:tt:{ticker.Id}"] = JsonSerializer.Serialize(ticker, _jsonOptions);
        _sets.TryAdd($"{Prefix}:tt:ids", []);
        _sets[$"{Prefix}:tt:ids"].Add(ticker.Id.ToString());

        await _internalManager.ReleaseDeadNodeResources(deadNode, CancellationToken.None);

        var stored = VerifyInStore<TimeTickerEntity>($"{Prefix}:tt:{ticker.Id}");
        Assert.Equal(TickerStatus.Idle, stored!.Status);
        Assert.Null(stored.LockHolder);
    }

    [Fact]
    public async Task InternalManager_ReleaseAcquiredResources_WorksViaRedis()
    {
        // Add a ticker via manager, then release
        var entity = new TimeTickerEntity
        {
            Function = ValidFunction,
            ExecutionTime = _fixedNow.AddMinutes(10)
        };

        var addResult = await _timeTickerManager.AddAsync(entity, CancellationToken.None);
        Assert.True(addResult.IsSucceeded);

        // Release all acquired resources for this node
        await _internalManager.ReleaseAcquiredResources([], CancellationToken.None);

        // Ticker should still exist but be in Idle state with no lock
        var stored = VerifyInStore<TimeTickerEntity>($"{Prefix}:tt:{addResult.Result.Id}");
        Assert.NotNull(stored);
        Assert.Equal(TickerStatus.Idle, stored!.Status);
        Assert.Null(stored.LockHolder);
    }

    // =========================================================================
    // On-Demand / Immediate Dispatch → Redis Provider
    // =========================================================================

    [Fact]
    public async Task OnDemand_AddTimeTicker_WithPastExecutionTime_AcquiresAndDispatches()
    {
        // Enable dispatcher to trigger immediate execution path
        _dispatcher.IsEnabled.Returns(true);

        var entity = new TimeTickerEntity
        {
            Function = ValidFunction,
            ExecutionTime = _fixedNow.AddSeconds(-5) // in the past → triggers immediate
        };

        var result = await _timeTickerManager.AddAsync(entity, CancellationToken.None);

        Assert.True(result.IsSucceeded);

        // Verify the ticker was acquired (InProgress) in Redis
        var stored = VerifyInStore<TimeTickerEntity>($"{Prefix}:tt:{result.Result.Id}");
        Assert.NotNull(stored);
        Assert.Equal(TickerStatus.InProgress, stored!.Status);
        Assert.Equal(NodeId, stored.LockHolder);

        // Verify dispatcher was called
        await _dispatcher.Received(1).DispatchAsync(
            Arg.Is<InternalFunctionContext[]>(c => c.Length > 0),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnDemand_AddTimeTicker_WithNullExecutionTime_AcquiresAndDispatches()
    {
        _dispatcher.IsEnabled.Returns(true);

        var entity = new TimeTickerEntity
        {
            Function = ValidFunction,
            ExecutionTime = null // null → immediate
        };

        var result = await _timeTickerManager.AddAsync(entity, CancellationToken.None);

        Assert.True(result.IsSucceeded);

        var stored = VerifyInStore<TimeTickerEntity>($"{Prefix}:tt:{result.Result.Id}");
        Assert.NotNull(stored);
        Assert.Equal(TickerStatus.InProgress, stored!.Status);
        Assert.Equal(NodeId, stored.LockHolder);

        await _dispatcher.Received(1).DispatchAsync(
            Arg.Is<InternalFunctionContext[]>(c => c.Length > 0),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnDemand_AddTimeTicker_WithFutureExecutionTime_DoesNotDispatch()
    {
        _dispatcher.IsEnabled.Returns(true);

        var entity = new TimeTickerEntity
        {
            Function = ValidFunction,
            ExecutionTime = _fixedNow.AddMinutes(30) // far future → scheduled, not immediate
        };

        var result = await _timeTickerManager.AddAsync(entity, CancellationToken.None);

        Assert.True(result.IsSucceeded);

        // Should NOT have been dispatched
        await _dispatcher.DidNotReceive().DispatchAsync(
            Arg.Any<InternalFunctionContext[]>(),
            Arg.Any<CancellationToken>());

        // Should still be Idle in Redis (not acquired)
        var stored = VerifyInStore<TimeTickerEntity>($"{Prefix}:tt:{result.Result.Id}");
        Assert.NotNull(stored);
        Assert.Equal(TickerStatus.Idle, stored!.Status);
    }

    [Fact]
    public async Task OnDemand_AddTimeTicker_DispatcherDisabled_DoesNotDispatch()
    {
        // Dispatcher disabled (default) — no immediate execution
        _dispatcher.IsEnabled.Returns(false);

        var entity = new TimeTickerEntity
        {
            Function = ValidFunction,
            ExecutionTime = _fixedNow.AddSeconds(-5) // past, but dispatcher is off
        };

        var result = await _timeTickerManager.AddAsync(entity, CancellationToken.None);

        Assert.True(result.IsSucceeded);

        await _dispatcher.DidNotReceive().DispatchAsync(
            Arg.Any<InternalFunctionContext[]>(),
            Arg.Any<CancellationToken>());

        // Should remain Idle
        var stored = VerifyInStore<TimeTickerEntity>($"{Prefix}:tt:{result.Result.Id}");
        Assert.Equal(TickerStatus.Idle, stored!.Status);
    }

    [Fact]
    public async Task OnDemand_AddBatch_WithImmediateTickers_AcquiresAndDispatches()
    {
        _dispatcher.IsEnabled.Returns(true);

        var entities = new List<TimeTickerEntity>
        {
            new() { Function = ValidFunction, ExecutionTime = _fixedNow.AddSeconds(-1) }, // immediate
            new() { Function = ValidFunction, ExecutionTime = _fixedNow },                 // immediate (within 1s)
            new() { Function = ValidFunction, ExecutionTime = _fixedNow.AddMinutes(30) }   // deferred
        };

        var result = await _timeTickerManager.AddBatchAsync(entities, CancellationToken.None);

        Assert.True(result.IsSucceeded);
        Assert.Equal(3, result.Result.Count);

        // Dispatcher should have been called for the immediate tickers
        await _dispatcher.Received().DispatchAsync(
            Arg.Any<InternalFunctionContext[]>(),
            Arg.Any<CancellationToken>());
    }
}
