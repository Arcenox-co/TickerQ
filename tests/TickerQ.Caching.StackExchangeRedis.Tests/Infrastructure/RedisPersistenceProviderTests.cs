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
using TickerQ.Utilities.Models;

namespace TickerQ.Caching.StackExchangeRedis.Tests.Infrastructure;

/// <summary>
/// Source-gen context for test entity types. Required because the Redis provider
/// uses source-gen only (no DefaultJsonTypeInfoResolver fallback).
/// </summary>
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(TimeTickerEntity))]
[JsonSerializable(typeof(TimeTickerEntity[]))]
[JsonSerializable(typeof(List<TimeTickerEntity>))]
[JsonSerializable(typeof(CronTickerEntity))]
[JsonSerializable(typeof(CronTickerEntity[]))]
[JsonSerializable(typeof(CronTickerOccurrenceEntity<CronTickerEntity>))]
[JsonSerializable(typeof(CronTickerOccurrenceEntity<CronTickerEntity>[]))]
internal partial class TestJsonSerializerContext : JsonSerializerContext;

/// <summary>
/// Tests for TickerRedisPersistenceProvider that exercise persistence methods
/// using NSubstitute to mock IDatabase, wiring up realistic responses
/// for StringGet/StringSet/SetMembers/SortedSetRange/ScriptEvaluate operations.
/// </summary>
public class RedisPersistenceProviderTests : IAsyncLifetime
{
    private IDatabase _db = null!;
    private TickerRedisPersistenceProvider<TimeTickerEntity, CronTickerEntity> _provider = null!;
    private ITickerClock _clock = null!;
    private DateTime _fixedNow;
    private JsonSerializerOptions _jsonOptions = null!;
    private const string NodeId = "test-node-1";
    private const string Prefix = "tq";

    // In-memory stores backing the mock
    private readonly Dictionary<string, string> _store = new();
    private readonly Dictionary<string, HashSet<string>> _sets = new();
    private readonly Dictionary<string, SortedList<double, string>> _sortedSets = new();

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

        // Wire up IDatabase mock to in-memory stores
        SetupStringOperations();
        SetupSetOperations();
        SetupSortedSetOperations();
        SetupBatchOperations();
        SetupScriptOperations();
        SetupKeyOperations();

        var schedulerOptions = new SchedulerOptionsBuilder { NodeIdentifier = NodeId };
        var redisOptions = new TickerQRedisOptionBuilder
        {
            JsonSerializerContext = TestJsonSerializerContext.Default
        };
        var logger = NullLoggerFactory.Instance.CreateLogger<TickerRedisPersistenceProvider<TimeTickerEntity, CronTickerEntity>>();

        _provider = new TickerRedisPersistenceProvider<TimeTickerEntity, CronTickerEntity>(_db, _clock, schedulerOptions, redisOptions, logger);

        return Task.CompletedTask;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    #region IDatabase Mock Wiring

    private void SetupStringOperations()
    {
        _db.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(callInfo =>
            {
                var key = (string)callInfo.ArgAt<RedisKey>(0);
                return _store.TryGetValue(key, out var val) ? (RedisValue)val : RedisValue.Null;
            });

        _db.StringSetAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<TimeSpan?>(), Arg.Any<bool>(), Arg.Any<When>(), Arg.Any<CommandFlags>())
            .Returns(callInfo =>
            {
                var key = (string)callInfo.ArgAt<RedisKey>(0);
                var value = (string)callInfo.ArgAt<RedisValue>(1);
                _store[key] = value;
                return true;
            });

        // MGET
        _db.StringGetAsync(Arg.Any<RedisKey[]>(), Arg.Any<CommandFlags>())
            .Returns(callInfo =>
            {
                var keys = callInfo.ArgAt<RedisKey[]>(0);
                var results = new RedisValue[keys.Length];
                for (var i = 0; i < keys.Length; i++)
                {
                    var k = (string)keys[i];
                    results[i] = _store.TryGetValue(k, out var v) ? (RedisValue)v : RedisValue.Null;
                }
                return results;
            });
    }

    private void SetupSetOperations()
    {
        _db.SetMembersAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(callInfo =>
            {
                var key = (string)callInfo.ArgAt<RedisKey>(0);
                if (!_sets.TryGetValue(key, out var set)) return Array.Empty<RedisValue>();
                return set.Select(m => (RedisValue)m).ToArray();
            });

        _db.SetAddAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<CommandFlags>())
            .Returns(callInfo =>
            {
                var key = (string)callInfo.ArgAt<RedisKey>(0);
                var member = (string)callInfo.ArgAt<RedisValue>(1);
                if (!_sets.ContainsKey(key)) _sets[key] = [];
                return _sets[key].Add(member);
            });

        _db.SetRemoveAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<CommandFlags>())
            .Returns(callInfo =>
            {
                var key = (string)callInfo.ArgAt<RedisKey>(0);
                var member = (string)callInfo.ArgAt<RedisValue>(1);
                return _sets.TryGetValue(key, out var set) && set.Remove(member);
            });
    }

    private void SetupSortedSetOperations()
    {
        _db.SortedSetRangeByScoreAsync(Arg.Any<RedisKey>(), Arg.Any<double>(), Arg.Any<double>(),
                Arg.Any<Exclude>(), Arg.Any<Order>(), Arg.Any<long>(), Arg.Any<long>(), Arg.Any<CommandFlags>())
            .Returns(callInfo =>
            {
                var key = (string)callInfo.ArgAt<RedisKey>(0);
                var min = callInfo.ArgAt<double>(1);
                var max = callInfo.ArgAt<double>(2);
                if (!_sortedSets.TryGetValue(key, out var ss)) return Array.Empty<RedisValue>();
                return ss.Where(kv => kv.Key >= min && kv.Key <= max)
                    .Select(kv => (RedisValue)kv.Value).ToArray();
            });

        _db.SortedSetRangeByScoreWithScoresAsync(Arg.Any<RedisKey>(), Arg.Any<double>(), Arg.Any<double>(),
                Arg.Any<Exclude>(), Arg.Any<Order>(), Arg.Any<long>(), Arg.Any<long>(), Arg.Any<CommandFlags>())
            .Returns(callInfo =>
            {
                var key = (string)callInfo.ArgAt<RedisKey>(0);
                var min = callInfo.ArgAt<double>(1);
                var max = callInfo.ArgAt<double>(2);
                var skip = callInfo.ArgAt<long>(5);
                var take = callInfo.ArgAt<long>(6);
                if (!_sortedSets.TryGetValue(key, out var ss)) return Array.Empty<SortedSetEntry>();
                return ss.Where(kv => kv.Key >= min && kv.Key <= max)
                    .Skip((int)skip).Take((int)take)
                    .Select(kv => new SortedSetEntry(kv.Value, kv.Key)).ToArray();
            });

        _db.SortedSetAddAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<double>(), Arg.Any<CommandFlags>())
            .Returns(callInfo =>
            {
                var key = (string)callInfo.ArgAt<RedisKey>(0);
                var member = (string)callInfo.ArgAt<RedisValue>(1);
                var score = callInfo.ArgAt<double>(2);
                if (!_sortedSets.ContainsKey(key)) _sortedSets[key] = new SortedList<double, string>();
                _sortedSets[key][score] = member;
                return true;
            });

        _db.SortedSetRemoveAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<CommandFlags>())
            .Returns(callInfo =>
            {
                var key = (string)callInfo.ArgAt<RedisKey>(0);
                var member = (string)callInfo.ArgAt<RedisValue>(1);
                if (!_sortedSets.TryGetValue(key, out var ss)) return false;
                var toRemove = ss.Where(kv => kv.Value == (string)member).Select(kv => kv.Key).ToList();
                foreach (var k in toRemove) ss.Remove(k);
                return toRemove.Count > 0;
            });
    }

    private void SetupBatchOperations()
    {
        var batch = Substitute.For<IBatch>();

        batch.SetAddAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<CommandFlags>())
            .Returns(callInfo =>
            {
                var key = (string)callInfo.ArgAt<RedisKey>(0);
                var member = (string)callInfo.ArgAt<RedisValue>(1);
                if (!_sets.ContainsKey(key)) _sets[key] = [];
                _sets[key].Add(member);
                return true;
            });

        batch.SetRemoveAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<CommandFlags>())
            .Returns(callInfo =>
            {
                var key = (string)callInfo.ArgAt<RedisKey>(0);
                var member = (string)callInfo.ArgAt<RedisValue>(1);
                return _sets.TryGetValue(key, out var set) && set.Remove(member);
            });

        batch.SortedSetAddAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<double>(), Arg.Any<CommandFlags>())
            .Returns(callInfo =>
            {
                var key = (string)callInfo.ArgAt<RedisKey>(0);
                var member = (string)callInfo.ArgAt<RedisValue>(1);
                var score = callInfo.ArgAt<double>(2);
                if (!_sortedSets.ContainsKey(key)) _sortedSets[key] = new SortedList<double, string>();
                _sortedSets[key][score] = member;
                return true;
            });

        batch.SortedSetRemoveAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<CommandFlags>())
            .Returns(callInfo =>
            {
                var key = (string)callInfo.ArgAt<RedisKey>(0);
                var member = (string)callInfo.ArgAt<RedisValue>(1);
                if (!_sortedSets.TryGetValue(key, out var ss)) return false;
                var toRemove = ss.Where(kv => kv.Value == (string)member).Select(kv => kv.Key).ToList();
                foreach (var k in toRemove) ss.Remove(k);
                return toRemove.Count > 0;
            });

        _db.CreateBatch(Arg.Any<object>()).Returns(batch);
    }

    private static int Rvi(RedisValue v) => int.Parse(v.ToString());

    private void SetupScriptOperations()
    {
        // ScriptEvaluateAsync handles Acquire/Release/RecoverDeadNode Lua scripts.
        // We simulate the Lua behavior in C# by inspecting the script + args.
        _db.ScriptEvaluateAsync(Arg.Any<string>(), Arg.Any<RedisKey[]?>(), Arg.Any<RedisValue[]?>(), Arg.Any<CommandFlags>())
            .Returns(callInfo =>
            {
                var script = callInfo.ArgAt<string>(0);
                var keys = callInfo.ArgAt<RedisKey[]>(1);
                var argv = callInfo.ArgAt<RedisValue[]>(2);
                var entityKey = (string)keys[0];

                if (!_store.TryGetValue(entityKey, out var json))
                    return RedisResult.Create(RedisValue.Null);

                var obj = JsonSerializer.Deserialize<JsonElement>(json);
                var status = obj.GetProperty("Status").GetInt32();
                var lockHolder = obj.TryGetProperty("LockHolder", out var lh) && lh.ValueKind != JsonValueKind.Null
                    ? lh.GetString() : null;

                // Differentiate scripts by arg count: Acquire=6, Release=4, RecoverDeadNode=5
                if (argv.Length == 5)
                {
                    // RecoverDeadNode: ARGV[0]=deadNodeId, ARGV[1]=now, ARGV[2]=statusIdle, ARGV[3]=statusQueued, ARGV[4]=statusInProgress
                    var deadNodeId = (string)argv[0];
                    var statusIdle = Rvi(argv[2]);
                    var statusQueued = Rvi(argv[3]);
                    var statusInProgress = Rvi(argv[4]);
                    if (lockHolder != deadNodeId) return RedisResult.Create(RedisValue.Null);
                    if (status != statusIdle && status != statusQueued && status != statusInProgress)
                        return RedisResult.Create(RedisValue.Null);

                    var updated = SetJsonProperties(json, new Dictionary<string, object>
                    {
                        ["LockHolder"] = null,
                        ["LockedAt"] = null,
                        ["Status"] = statusIdle,
                        ["UpdatedAt"] = (string)argv[1]
                    });
                    _store[entityKey] = updated;
                    return RedisResult.Create((RedisValue)updated);
                }

                if (argv.Length == 4)
                {
                    // Release: ARGV[0]=lockHolder, ARGV[1]=now, ARGV[2]=statusIdle, ARGV[3]=statusQueued
                    var reqLockHolder = (string)argv[0];
                    var statusIdle = Rvi(argv[2]);
                    var statusQueued = Rvi(argv[3]);
                    if (status != statusIdle && status != statusQueued) return RedisResult.Create(RedisValue.Null);
                    if (!string.IsNullOrEmpty(lockHolder) && lockHolder != reqLockHolder)
                        return RedisResult.Create(RedisValue.Null);

                    var updated = SetJsonProperties(json, new Dictionary<string, object>
                    {
                        ["LockHolder"] = null,
                        ["LockedAt"] = null,
                        ["Status"] = statusIdle,
                        ["UpdatedAt"] = (string)argv[1]
                    });
                    _store[entityKey] = updated;
                    return RedisResult.Create((RedisValue)updated);
                }

                // Acquire: ARGV[0]=lockHolder, ARGV[1]=now, ARGV[2]=targetStatus, ARGV[3]=expectedUpdatedAt, ARGV[4]=statusIdle, ARGV[5]=statusQueued
                {
                    var reqLockHolder = (string)argv[0];
                    var targetStatus = Rvi(argv[2]);
                    var expectedUpdatedAt = (string)argv[3];
                    var statusIdle = Rvi(argv[4]);
                    var statusQueued = Rvi(argv[5]);

                    if (status != statusIdle && status != statusQueued) return RedisResult.Create(RedisValue.Null);
                    if (!string.IsNullOrEmpty(lockHolder) && lockHolder != reqLockHolder)
                        return RedisResult.Create(RedisValue.Null);

                    if (!string.IsNullOrEmpty(expectedUpdatedAt))
                    {
                        var currentUpdatedAt = obj.TryGetProperty("UpdatedAt", out var ua) ? ua.GetString() : null;
                        // Compare as DateTimes to handle format differences (JSON vs ISO "O")
                        if (currentUpdatedAt == null ||
                            !DateTime.TryParse(currentUpdatedAt, out var currentDt) ||
                            !DateTime.TryParse(expectedUpdatedAt, out var expectedDt) ||
                            currentDt != expectedDt)
                            return RedisResult.Create(RedisValue.Null);
                    }

                    var updated = SetJsonProperties(json, new Dictionary<string, object>
                    {
                        ["LockHolder"] = reqLockHolder,
                        ["LockedAt"] = (string)argv[1],
                        ["Status"] = targetStatus,
                        ["UpdatedAt"] = (string)argv[1]
                    });
                    _store[entityKey] = updated;
                    return RedisResult.Create((RedisValue)updated);
                }
            });
    }

    private void SetupKeyOperations()
    {
        _db.KeyDeleteAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(callInfo =>
            {
                var key = (string)callInfo.ArgAt<RedisKey>(0);
                return _store.Remove(key);
            });
    }

    private string SetJsonProperties(string json, Dictionary<string, object?> props)
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
                    switch (newVal)
                    {
                        case null: writer.WriteNullValue(); break;
                        case int i: writer.WriteNumberValue(i); break;
                        case string s: writer.WriteStringValue(s); break;
                        default: writer.WriteStringValue(newVal.ToString()); break;
                    }
                }
                else
                {
                    prop.WriteTo(writer);
                }
            }
            writer.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(ms.ToArray());
    }

    #endregion

    #region Seed Helpers

    private void SeedTimeTicker(TimeTickerEntity ticker)
    {
        var key = $"{Prefix}:tt:{ticker.Id}";
        _store[key] = JsonSerializer.Serialize(ticker, _jsonOptions);
        if (!_sets.ContainsKey($"{Prefix}:tt:ids")) _sets[$"{Prefix}:tt:ids"] = [];
        _sets[$"{Prefix}:tt:ids"].Add(ticker.Id.ToString());

        if (ticker.ExecutionTime.HasValue && ticker.Status is TickerStatus.Idle or TickerStatus.Queued
            && (string.IsNullOrEmpty(ticker.LockHolder) || ticker.LockHolder == NodeId))
        {
            if (!_sortedSets.ContainsKey($"{Prefix}:tt:pending"))
                _sortedSets[$"{Prefix}:tt:pending"] = new SortedList<double, string>();
            _sortedSets[$"{Prefix}:tt:pending"][ticker.ExecutionTime.Value.ToUniversalTime().Ticks] = ticker.Id.ToString();
        }
    }

    private void SeedCronTicker(CronTickerEntity cron)
    {
        var key = $"{Prefix}:cron:{cron.Id}";
        _store[key] = JsonSerializer.Serialize(cron, _jsonOptions);
        if (!_sets.ContainsKey($"{Prefix}:cron:ids")) _sets[$"{Prefix}:cron:ids"] = [];
        _sets[$"{Prefix}:cron:ids"].Add(cron.Id.ToString());
    }

    private void SeedCronOccurrence(CronTickerOccurrenceEntity<CronTickerEntity> occ)
    {
        var key = $"{Prefix}:co:{occ.Id}";
        _store[key] = JsonSerializer.Serialize(occ, _jsonOptions);
        if (!_sets.ContainsKey($"{Prefix}:co:ids")) _sets[$"{Prefix}:co:ids"] = [];
        _sets[$"{Prefix}:co:ids"].Add(occ.Id.ToString());

        // Reverse index
        var reverseKey = $"{Prefix}:cron:{occ.CronTickerId}:occurrences";
        if (!_sets.ContainsKey(reverseKey)) _sets[reverseKey] = [];
        _sets[reverseKey].Add(occ.Id.ToString());

        if (occ.Status is TickerStatus.Idle or TickerStatus.Queued
            && (string.IsNullOrEmpty(occ.LockHolder) || occ.LockHolder == NodeId))
        {
            if (!_sortedSets.ContainsKey($"{Prefix}:co:pending"))
                _sortedSets[$"{Prefix}:co:pending"] = new SortedList<double, string>();
            _sortedSets[$"{Prefix}:co:pending"][occ.ExecutionTime.ToUniversalTime().Ticks] = occ.Id.ToString();
        }
    }

    private TimeTickerEntity CreateTimeTicker(
        Guid? id = null,
        DateTime? executionTime = null,
        TickerStatus status = TickerStatus.Idle,
        string function = "TestFunction",
        string? lockHolder = null,
        DateTime? lockedAt = null,
        DateTime? updatedAt = null)
    {
        return new TimeTickerEntity
        {
            Id = id ?? Guid.NewGuid(),
            Function = function,
            ExecutionTime = executionTime ?? _fixedNow.AddMinutes(5),
            Status = status,
            LockHolder = lockHolder,
            LockedAt = lockedAt,
            CreatedAt = _fixedNow.AddHours(-1),
            UpdatedAt = updatedAt ?? _fixedNow.AddHours(-1),
            Request = []
        };
    }

    private CronTickerEntity CreateCronTicker(
        Guid? id = null,
        string function = "TestCronFunction",
        string expression = "*/5 * * * *")
    {
        return new CronTickerEntity
        {
            Id = id ?? Guid.NewGuid(),
            Function = function,
            Expression = expression,
            CreatedAt = _fixedNow.AddHours(-1),
            UpdatedAt = _fixedNow.AddHours(-1),
            Request = []
        };
    }

    private CronTickerOccurrenceEntity<CronTickerEntity> CreateCronOccurrence(
        Guid cronTickerId,
        Guid? id = null,
        DateTime? executionTime = null,
        TickerStatus status = TickerStatus.Idle,
        string? lockHolder = null,
        DateTime? lockedAt = null,
        DateTime? updatedAt = null)
    {
        return new CronTickerOccurrenceEntity<CronTickerEntity>
        {
            Id = id ?? Guid.NewGuid(),
            CronTickerId = cronTickerId,
            ExecutionTime = executionTime ?? _fixedNow.AddMinutes(5),
            Status = status,
            LockHolder = lockHolder,
            LockedAt = lockedAt,
            CreatedAt = _fixedNow.AddHours(-1),
            UpdatedAt = updatedAt ?? _fixedNow.AddHours(-1)
        };
    }

    private T? VerifyInStore<T>(string key) where T : class
    {
        if (!_store.TryGetValue(key, out var json)) return null;
        return JsonSerializer.Deserialize<T>(json, _jsonOptions);
    }

    private async Task<List<T>> ToListAsync<T>(IAsyncEnumerable<T> source)
    {
        var list = new List<T>();
        await foreach (var item in source)
            list.Add(item);
        return list;
    }

    #endregion

    // =========================================================================
    // 1. AddTimeTickers
    // =========================================================================

    [Fact]
    public async Task AddTimeTickers_InsertsAndVerifiesInStore()
    {
        var ticker1 = CreateTimeTicker();
        var ticker2 = CreateTimeTicker();

        var result = await _provider.AddTimeTickers([ticker1, ticker2], CancellationToken.None);

        Assert.Equal(2, result);
        Assert.NotNull(VerifyInStore<TimeTickerEntity>($"{Prefix}:tt:{ticker1.Id}"));
        Assert.NotNull(VerifyInStore<TimeTickerEntity>($"{Prefix}:tt:{ticker2.Id}"));
        Assert.Contains(ticker1.Id.ToString(), _sets[$"{Prefix}:tt:ids"]);
        Assert.Contains(ticker2.Id.ToString(), _sets[$"{Prefix}:tt:ids"]);
    }

    // =========================================================================
    // 2. UpdateTimeTickers
    // =========================================================================

    [Fact]
    public async Task UpdateTimeTickers_UpdatesPropertiesAndPersists()
    {
        var ticker = CreateTimeTicker();
        SeedTimeTicker(ticker);

        ticker.Function = "UpdatedFunction";
        ticker.ExecutionTime = _fixedNow.AddMinutes(99);
        var result = await _provider.UpdateTimeTickers([ticker], CancellationToken.None);

        Assert.Equal(1, result);
        var stored = VerifyInStore<TimeTickerEntity>($"{Prefix}:tt:{ticker.Id}");
        Assert.Equal("UpdatedFunction", stored!.Function);
        Assert.Equal(_fixedNow.AddMinutes(99), stored.ExecutionTime);
    }

    // =========================================================================
    // 3. RemoveTimeTickers
    // =========================================================================

    [Fact]
    public async Task RemoveTimeTickers_DeletesAndVerifiesRemoved()
    {
        var ticker1 = CreateTimeTicker();
        var ticker2 = CreateTimeTicker();
        SeedTimeTicker(ticker1);
        SeedTimeTicker(ticker2);

        var result = await _provider.RemoveTimeTickers([ticker1.Id], CancellationToken.None);

        Assert.Equal(1, result);
        Assert.Null(VerifyInStore<TimeTickerEntity>($"{Prefix}:tt:{ticker1.Id}"));
        Assert.NotNull(VerifyInStore<TimeTickerEntity>($"{Prefix}:tt:{ticker2.Id}"));
    }

    // =========================================================================
    // 4. GetTimeTickerById
    // =========================================================================

    [Fact]
    public async Task GetTimeTickerById_ExistingTicker_ReturnsTicker()
    {
        var ticker = CreateTimeTicker();
        SeedTimeTicker(ticker);

        var result = await _provider.GetTimeTickerById(ticker.Id, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(ticker.Id, result.Id);
        Assert.Equal(ticker.Function, result.Function);
    }

    [Fact]
    public async Task GetTimeTickerById_NonExistent_ReturnsNull()
    {
        var result = await _provider.GetTimeTickerById(Guid.NewGuid(), CancellationToken.None);
        Assert.Null(result);
    }

    // =========================================================================
    // 5. QueueTimeTickers
    // =========================================================================

    [Fact]
    public async Task QueueTimeTickers_UpdatesStatusToQueuedWithLock()
    {
        var ticker = CreateTimeTicker(updatedAt: _fixedNow.AddHours(-1));
        SeedTimeTicker(ticker);

        var inputTicker = new TimeTickerEntity
        {
            Id = ticker.Id,
            UpdatedAt = ticker.UpdatedAt
        };

        var results = await ToListAsync(_provider.QueueTimeTickers([inputTicker], CancellationToken.None));

        Assert.Single(results);
        Assert.Equal(ticker.Id, results[0].Id);
        Assert.Equal(TickerStatus.Queued, results[0].Status);
        Assert.Equal(NodeId, results[0].LockHolder);

        using var doc = JsonDocument.Parse(_store[$"{Prefix}:tt:{ticker.Id}"]);
        Assert.Equal((int)TickerStatus.Queued, doc.RootElement.GetProperty("Status").GetInt32());
        Assert.Equal(NodeId, doc.RootElement.GetProperty("LockHolder").GetString());
    }

    [Fact]
    public async Task QueueTimeTickers_StaleUpdatedAt_SkipsTicker()
    {
        var ticker = CreateTimeTicker(updatedAt: _fixedNow.AddHours(-1));
        SeedTimeTicker(ticker);

        var inputTicker = new TimeTickerEntity
        {
            Id = ticker.Id,
            UpdatedAt = _fixedNow.AddHours(-2) // stale
        };

        var results = await ToListAsync(_provider.QueueTimeTickers([inputTicker], CancellationToken.None));
        Assert.Empty(results);
    }

    // =========================================================================
    // 6. AcquireImmediateTimeTickersAsync
    // =========================================================================

    [Fact]
    public async Task AcquireImmediateTimeTickersAsync_AcquiresIdleTickers()
    {
        var ticker = CreateTimeTicker(status: TickerStatus.Idle, lockHolder: null, lockedAt: null);
        SeedTimeTicker(ticker);

        var results = await _provider.AcquireImmediateTimeTickersAsync([ticker.Id], CancellationToken.None);

        Assert.Single(results);

        // The store has the updated JSON (Status:2 = InProgress)
        var storeKey = $"{Prefix}:tt:{ticker.Id}";
        Assert.Contains("\"Status\":2", _store[storeKey]);

        // Verify deserialization round-trip via the provider's serializer
        // Note: VerifyInStore uses the test's jsonOptions, which may differ from provider's.
        // Use JsonDocument to verify directly:
        using var doc = JsonDocument.Parse(_store[storeKey]);
        Assert.Equal(2, doc.RootElement.GetProperty("Status").GetInt32()); // InProgress = 2
        Assert.Equal(NodeId, doc.RootElement.GetProperty("LockHolder").GetString());
    }

    [Fact]
    public async Task AcquireImmediateTimeTickersAsync_EmptyIds_ReturnsEmpty()
    {
        var results = await _provider.AcquireImmediateTimeTickersAsync([], CancellationToken.None);
        Assert.Empty(results);
    }

    [Fact]
    public async Task AcquireImmediateTimeTickersAsync_AlreadyLockedByOther_CannotAcquire()
    {
        var ticker = CreateTimeTicker(status: TickerStatus.Queued, lockHolder: "other-node", lockedAt: _fixedNow.AddMinutes(-1));
        SeedTimeTicker(ticker);

        var results = await _provider.AcquireImmediateTimeTickersAsync([ticker.Id], CancellationToken.None);
        Assert.Empty(results);
    }

    // =========================================================================
    // 7. ReleaseAcquiredTimeTickers
    // =========================================================================

    [Fact]
    public async Task ReleaseAcquiredTimeTickers_ReleasesLocksOnMatchingTickers()
    {
        var ticker = CreateTimeTicker(status: TickerStatus.Queued, lockHolder: NodeId, lockedAt: _fixedNow);
        SeedTimeTicker(ticker);

        await _provider.ReleaseAcquiredTimeTickers([ticker.Id], CancellationToken.None);

        var stored = VerifyInStore<TimeTickerEntity>($"{Prefix}:tt:{ticker.Id}");
        Assert.Equal(TickerStatus.Idle, stored!.Status);
        Assert.Null(stored.LockHolder);
        Assert.Null(stored.LockedAt);
    }

    [Fact]
    public async Task ReleaseAcquiredTimeTickers_DoesNotReleaseOtherNodesLocks()
    {
        var ticker = CreateTimeTicker(status: TickerStatus.Queued, lockHolder: "other-node", lockedAt: _fixedNow);
        SeedTimeTicker(ticker);

        await _provider.ReleaseAcquiredTimeTickers([ticker.Id], CancellationToken.None);

        using var doc = JsonDocument.Parse(_store[$"{Prefix}:tt:{ticker.Id}"]);
        Assert.Equal("other-node", doc.RootElement.GetProperty("LockHolder").GetString());
        Assert.Equal((int)TickerStatus.Queued, doc.RootElement.GetProperty("Status").GetInt32());
    }

    // =========================================================================
    // 8. GetEarliestTimeTickers
    // =========================================================================

    [Fact]
    public async Task GetEarliestTimeTickers_ReturnsEarliestAcquirableTickers()
    {
        var execTime = _fixedNow.AddMilliseconds(500);
        var ticker = CreateTimeTicker(executionTime: execTime, status: TickerStatus.Idle, lockHolder: null, lockedAt: null);
        SeedTimeTicker(ticker);

        var results = await _provider.GetEarliestTimeTickers(CancellationToken.None);

        Assert.Single(results);
        Assert.Equal(ticker.Id, results[0].Id);
    }

    [Fact]
    public async Task GetEarliestTimeTickers_ReturnsEmptyWhenNoneAvailable()
    {
        var results = await _provider.GetEarliestTimeTickers(CancellationToken.None);
        Assert.Empty(results);
    }

    // =========================================================================
    // 9. InsertCronTickers
    // =========================================================================

    [Fact]
    public async Task InsertCronTickers_InsertsAndVerifiesInStore()
    {
        var cron1 = CreateCronTicker();
        var cron2 = CreateCronTicker(function: "AnotherCron");

        var result = await _provider.InsertCronTickers([cron1, cron2], CancellationToken.None);

        Assert.Equal(2, result);
        Assert.NotNull(VerifyInStore<CronTickerEntity>($"{Prefix}:cron:{cron1.Id}"));
        Assert.NotNull(VerifyInStore<CronTickerEntity>($"{Prefix}:cron:{cron2.Id}"));
    }

    // =========================================================================
    // 10. UpdateCronTickers
    // =========================================================================

    [Fact]
    public async Task UpdateCronTickers_UpdatesAndVerifies()
    {
        var cron = CreateCronTicker();
        SeedCronTicker(cron);

        cron.Expression = "0 0 * * *";
        cron.Function = "UpdatedCronFunc";
        var result = await _provider.UpdateCronTickers([cron], CancellationToken.None);

        Assert.Equal(1, result);
        var stored = VerifyInStore<CronTickerEntity>($"{Prefix}:cron:{cron.Id}");
        Assert.Equal("0 0 * * *", stored!.Expression);
        Assert.Equal("UpdatedCronFunc", stored.Function);
    }

    // =========================================================================
    // 11. RemoveCronTickers
    // =========================================================================

    [Fact]
    public async Task RemoveCronTickers_DeletesAndVerifies()
    {
        var cron1 = CreateCronTicker();
        var cron2 = CreateCronTicker(function: "KeepMe");
        SeedCronTicker(cron1);
        SeedCronTicker(cron2);

        var result = await _provider.RemoveCronTickers([cron1.Id], CancellationToken.None);

        Assert.Equal(1, result);
        Assert.Null(VerifyInStore<CronTickerEntity>($"{Prefix}:cron:{cron1.Id}"));
        Assert.NotNull(VerifyInStore<CronTickerEntity>($"{Prefix}:cron:{cron2.Id}"));
    }

    // =========================================================================
    // 12. InsertCronTickerOccurrences
    // =========================================================================

    [Fact]
    public async Task InsertCronTickerOccurrences_BulkInsertAndVerify()
    {
        var cron = CreateCronTicker();
        SeedCronTicker(cron);

        var occ1 = CreateCronOccurrence(cron.Id, executionTime: _fixedNow.AddMinutes(1));
        var occ2 = CreateCronOccurrence(cron.Id, executionTime: _fixedNow.AddMinutes(2));

        var result = await _provider.InsertCronTickerOccurrences([occ1, occ2], CancellationToken.None);

        Assert.Equal(2, result);
        Assert.NotNull(VerifyInStore<CronTickerOccurrenceEntity<CronTickerEntity>>($"{Prefix}:co:{occ1.Id}"));
        Assert.NotNull(VerifyInStore<CronTickerOccurrenceEntity<CronTickerEntity>>($"{Prefix}:co:{occ2.Id}"));
    }

    // =========================================================================
    // 13. RemoveCronTickerOccurrences
    // =========================================================================

    [Fact]
    public async Task RemoveCronTickerOccurrences_BulkDeleteAndVerify()
    {
        var cron = CreateCronTicker();
        SeedCronTicker(cron);

        var occ1 = CreateCronOccurrence(cron.Id, executionTime: _fixedNow.AddMinutes(1));
        var occ2 = CreateCronOccurrence(cron.Id, executionTime: _fixedNow.AddMinutes(2));
        SeedCronOccurrence(occ1);
        SeedCronOccurrence(occ2);

        var result = await _provider.RemoveCronTickerOccurrences([occ1.Id], CancellationToken.None);

        Assert.Equal(1, result);
        Assert.Null(VerifyInStore<CronTickerOccurrenceEntity<CronTickerEntity>>($"{Prefix}:co:{occ1.Id}"));
        Assert.NotNull(VerifyInStore<CronTickerOccurrenceEntity<CronTickerEntity>>($"{Prefix}:co:{occ2.Id}"));
    }

    // =========================================================================
    // 14. AcquireImmediateCronOccurrencesAsync
    // =========================================================================

    [Fact]
    public async Task AcquireImmediateCronOccurrencesAsync_AcquiresIdleOccurrences()
    {
        var cron = CreateCronTicker();
        SeedCronTicker(cron);

        var occ = CreateCronOccurrence(cron.Id, status: TickerStatus.Idle, lockHolder: null, lockedAt: null);
        SeedCronOccurrence(occ);

        var results = await _provider.AcquireImmediateCronOccurrencesAsync([occ.Id], CancellationToken.None);

        Assert.Single(results);
        var stored = VerifyInStore<CronTickerOccurrenceEntity<CronTickerEntity>>($"{Prefix}:co:{occ.Id}");
        Assert.Equal(TickerStatus.InProgress, stored!.Status);
        Assert.Equal(NodeId, stored.LockHolder);
    }

    [Fact]
    public async Task AcquireImmediateCronOccurrencesAsync_EmptyIds_ReturnsEmpty()
    {
        var results = await _provider.AcquireImmediateCronOccurrencesAsync([], CancellationToken.None);
        Assert.Empty(results);
    }

    [Fact]
    public async Task AcquireImmediateCronOccurrencesAsync_LockedByOtherNode_CannotAcquire()
    {
        var cron = CreateCronTicker();
        SeedCronTicker(cron);

        var occ = CreateCronOccurrence(cron.Id, status: TickerStatus.Queued, lockHolder: "other-node", lockedAt: _fixedNow.AddMinutes(-1));
        SeedCronOccurrence(occ);

        var results = await _provider.AcquireImmediateCronOccurrencesAsync([occ.Id], CancellationToken.None);
        Assert.Empty(results);
    }

    /// <summary>
    /// Regression test for the NullReferenceException in
    /// TickerDashboardRepository.AddOnDemandCronTickerOccurrenceAsync:
    ///     FunctionName = occurrence.CronTicker.Function
    ///
    /// Root cause: InsertCronTickerOccurrences stores the occurrence with CronTicker=null
    /// (the navigation property is never set — only CronTickerId is populated).
    /// The Redis provider serialises and deserialises as-is, so AcquireImmediateCronOccurrencesAsync
    /// returns an occurrence whose CronTicker property is still null.
    /// The EF Core provider avoids the crash because it re-hydrates the nav property via a JOIN,
    /// but the Redis provider does not.
    ///
    /// This test asserts the CORRECT behaviour (CronTicker is populated after acquire).
    /// </summary>
    [Fact]
    public async Task AcquireImmediateCronOccurrencesAsync_WithRedisProvider_CronTickerNavigationProperty_ShouldBeHydrated()
    {
        // Arrange — a cron ticker stored in Redis (the "parent" row)
        const string expectedFunction = "MyScheduledJob";
        var cron = CreateCronTicker(function: expectedFunction);
        SeedCronTicker(cron);

        // Simulate what AddOnDemandCronTickerOccurrenceAsync does:
        // it creates the occurrence with only CronTickerId set — CronTicker nav property is null.
        var onDemandOccurrence = new CronTickerOccurrenceEntity<CronTickerEntity>
        {
            Id = Guid.NewGuid(),
            Status = TickerStatus.Idle,
            ExecutionTime = _fixedNow,
            LockedAt = null,
            CronTickerId = cron.Id,
            CronTicker = null!  // explicitly null — mirrors the dashboard repository (nav prop not set)
        };
        await _provider.InsertCronTickerOccurrences([onDemandOccurrence], CancellationToken.None);

        // Act — acquire the occurrence, exactly as the dashboard does
        var acquired = await _provider.AcquireImmediateCronOccurrencesAsync(
            [onDemandOccurrence.Id], CancellationToken.None);

        // Assert — the occurrence must be returned and CronTicker must be rehydrated
        // so that occurrence.CronTicker.Function does not throw NullReferenceException.
        Assert.Single(acquired);
        var occurrence = acquired[0];

        // This assertion fails today (CronTicker is null) and passes after the fix.
        Assert.NotNull(occurrence.CronTicker);
        Assert.Equal(expectedFunction, occurrence.CronTicker.Function);
    }

    // =========================================================================
    // 15. ReleaseAcquiredCronTickerOccurrences
    // =========================================================================

    [Fact]
    public async Task ReleaseAcquiredCronTickerOccurrences_ReleasesLocks()
    {
        var cron = CreateCronTicker();
        SeedCronTicker(cron);

        var occ = CreateCronOccurrence(cron.Id, status: TickerStatus.Queued, lockHolder: NodeId, lockedAt: _fixedNow);
        SeedCronOccurrence(occ);

        await _provider.ReleaseAcquiredCronTickerOccurrences([occ.Id], CancellationToken.None);

        var stored = VerifyInStore<CronTickerOccurrenceEntity<CronTickerEntity>>($"{Prefix}:co:{occ.Id}");
        Assert.Equal(TickerStatus.Idle, stored!.Status);
        Assert.Null(stored.LockHolder);
        Assert.Null(stored.LockedAt);
    }

    [Fact]
    public async Task ReleaseAcquiredCronTickerOccurrences_DoesNotReleaseOtherNodesLocks()
    {
        var cron = CreateCronTicker();
        SeedCronTicker(cron);

        var occ = CreateCronOccurrence(cron.Id, status: TickerStatus.Queued, lockHolder: "other-node", lockedAt: _fixedNow);
        SeedCronOccurrence(occ);

        await _provider.ReleaseAcquiredCronTickerOccurrences([occ.Id], CancellationToken.None);

        var stored = VerifyInStore<CronTickerOccurrenceEntity<CronTickerEntity>>($"{Prefix}:co:{occ.Id}");
        Assert.Equal("other-node", stored!.LockHolder);
        Assert.Equal(TickerStatus.Queued, stored.Status);
    }

    // =========================================================================
    // 16. ReleaseDeadNodeTimeTickerResources
    // =========================================================================

    [Fact]
    public async Task ReleaseDeadNodeTimeTickerResources_ReleasesDeadNodeTickers()
    {
        var deadNode = "dead-node-1";
        var ticker = CreateTimeTicker(status: TickerStatus.InProgress, lockHolder: deadNode, lockedAt: _fixedNow.AddMinutes(-10));
        SeedTimeTicker(ticker);

        await _provider.ReleaseDeadNodeTimeTickerResources(deadNode, CancellationToken.None);

        var stored = VerifyInStore<TimeTickerEntity>($"{Prefix}:tt:{ticker.Id}");
        Assert.Equal(TickerStatus.Idle, stored!.Status);
        Assert.Null(stored.LockHolder);
        Assert.Null(stored.LockedAt);
    }

    [Fact]
    public async Task ReleaseDeadNodeTimeTickerResources_DoesNotAffectOtherNodes()
    {
        var deadNode = "dead-node-1";
        var healthyTicker = CreateTimeTicker(status: TickerStatus.InProgress, lockHolder: "healthy-node", lockedAt: _fixedNow);
        SeedTimeTicker(healthyTicker);

        await _provider.ReleaseDeadNodeTimeTickerResources(deadNode, CancellationToken.None);

        using var doc = JsonDocument.Parse(_store[$"{Prefix}:tt:{healthyTicker.Id}"]);
        Assert.Equal((int)TickerStatus.InProgress, doc.RootElement.GetProperty("Status").GetInt32());
        Assert.Equal("healthy-node", doc.RootElement.GetProperty("LockHolder").GetString());
    }

    // =========================================================================
    // 17. ReleaseDeadNodeOccurrenceResources
    // =========================================================================

    [Fact]
    public async Task ReleaseDeadNodeOccurrenceResources_ReleasesDeadNodeOccurrences()
    {
        var deadNode = "dead-node-1";
        var cron = CreateCronTicker();
        SeedCronTicker(cron);

        var occ = CreateCronOccurrence(cron.Id, status: TickerStatus.InProgress, lockHolder: deadNode, lockedAt: _fixedNow.AddMinutes(-10));
        SeedCronOccurrence(occ);

        await _provider.ReleaseDeadNodeOccurrenceResources(deadNode, CancellationToken.None);

        var stored = VerifyInStore<CronTickerOccurrenceEntity<CronTickerEntity>>($"{Prefix}:co:{occ.Id}");
        Assert.Equal(TickerStatus.Idle, stored!.Status);
        Assert.Null(stored.LockHolder);
        Assert.Null(stored.LockedAt);
    }

    [Fact]
    public async Task ReleaseDeadNodeOccurrenceResources_DoesNotAffectOtherNodes()
    {
        var deadNode = "dead-node-1";
        var cron = CreateCronTicker();
        SeedCronTicker(cron);

        var healthyOcc = CreateCronOccurrence(cron.Id, status: TickerStatus.InProgress, lockHolder: "healthy-node", lockedAt: _fixedNow);
        SeedCronOccurrence(healthyOcc);

        await _provider.ReleaseDeadNodeOccurrenceResources(deadNode, CancellationToken.None);

        var stored = VerifyInStore<CronTickerOccurrenceEntity<CronTickerEntity>>($"{Prefix}:co:{healthyOcc.Id}");
        Assert.Equal(TickerStatus.InProgress, stored!.Status);
        Assert.Equal("healthy-node", stored.LockHolder);
    }

    // =========================================================================
    // 18. GetTimeTickers with predicate
    // =========================================================================

    [Fact]
    public async Task GetTimeTickers_WithPredicate_FiltersCorrectly()
    {
        var ticker1 = CreateTimeTicker(function: "FuncA");
        var ticker2 = CreateTimeTicker(function: "FuncB");
        SeedTimeTicker(ticker1);
        SeedTimeTicker(ticker2);

        var results = await _provider.GetTimeTickers(t => t.Function == "FuncA", CancellationToken.None);

        Assert.Single(results);
        Assert.Equal("FuncA", results[0].Function);
    }

    [Fact]
    public async Task GetTimeTickers_NullPredicate_ReturnsAll()
    {
        var ticker1 = CreateTimeTicker();
        var ticker2 = CreateTimeTicker();
        SeedTimeTicker(ticker1);
        SeedTimeTicker(ticker2);

        var results = await _provider.GetTimeTickers(null!, CancellationToken.None);

        Assert.Equal(2, results.Length);
    }

    // =========================================================================
    // 19. GetCronTickers with predicate
    // =========================================================================

    [Fact]
    public async Task GetCronTickers_WithPredicate_FiltersCorrectly()
    {
        var cron1 = CreateCronTicker(function: "CronA");
        var cron2 = CreateCronTicker(function: "CronB");
        SeedCronTicker(cron1);
        SeedCronTicker(cron2);

        var results = await _provider.GetCronTickers(c => c.Function == "CronA", CancellationToken.None);

        Assert.Single(results);
        Assert.Equal("CronA", results[0].Function);
    }

    // =========================================================================
    // 20. Pagination
    // =========================================================================

    [Fact]
    public async Task GetTimeTickersPaginated_ReturnsPaginatedResults()
    {
        for (var i = 0; i < 10; i++)
            SeedTimeTicker(CreateTimeTicker(executionTime: _fixedNow.AddMinutes(i)));

        var page = await _provider.GetTimeTickersPaginated(null!, 2, 3, CancellationToken.None);

        Assert.Equal(3, page.Items.Count());
        Assert.Equal(10, page.TotalCount);
        Assert.Equal(2, page.PageNumber);
    }
}
