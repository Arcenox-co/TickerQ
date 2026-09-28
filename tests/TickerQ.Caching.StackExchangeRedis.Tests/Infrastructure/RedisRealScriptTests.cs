using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;
using Testcontainers.Redis;
using TickerQ.Caching.StackExchangeRedis.Helpers;
using TickerQ.Caching.StackExchangeRedis.Infrastructure;
using static TickerQ.Caching.StackExchangeRedis.DependencyInjection.ServiceExtension;
using NSubstitute;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ.Caching.StackExchangeRedis.Tests.Infrastructure;

/// <summary>
/// Boots a real Redis container and drives the ACTUAL embedded Lua scripts through the
/// real <see cref="TickerRedisPersistenceProvider{TTimeTicker,TCronTicker}"/> against a real
/// <see cref="IDatabase"/> — no NSubstitute IDatabase, no C#-simulated Lua. This is the only
/// tier that can catch bugs the mock hides, because the mock never runs real <c>cjson</c>
/// (which mangles empty <c>[]</c> arrays into <c>{}</c>) and never does the exact-string
/// UpdatedAt CAS comparison the production scripts perform.
/// </summary>
public sealed class RedisRealScriptFixture : IAsyncLifetime
{
    private readonly string? _externalConnection = Environment.GetEnvironmentVariable("TICKERQ_TEST_REDIS");
    public RedisContainer? Container { get; private set; }
    public IConnectionMultiplexer Mux { get; private set; } = null!;
    public IDatabase Db { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var connection = _externalConnection;
        if (string.IsNullOrWhiteSpace(connection))
        {
            Container = new RedisBuilder().WithImage("redis:7").Build();
            await Container.StartAsync();
            connection = Container.GetConnectionString();
        }
        Mux = await ConnectionMultiplexer.ConnectAsync(connection);
        Db = Mux.GetDatabase();
    }

    public async Task DisposeAsync()
    {
        if (Mux is not null)
            await Mux.DisposeAsync();
        if (Container is not null)
            await Container.DisposeAsync();
    }
}

[CollectionDefinition("RedisRealScript")]
public sealed class RedisRealScriptCollection : ICollectionFixture<RedisRealScriptFixture>;

[Collection("RedisRealScript")]
public sealed class RedisRealScriptTests
{
    private readonly RedisRealScriptFixture _fx;
    private readonly IDatabase _db;
    private readonly ITickerClock _clock;
    private DateTime _now;
    private readonly SchedulerOptionsBuilder _options;
    private readonly TickerRedisPersistenceProvider<TimeTickerEntity, CronTickerEntity> _provider;

    // A timestamp whose fractional seconds are all trailing zeros — System.Text.Json trims
    // these when serializing, so the on-wire form ("...T12:00:00Z") differs from ToString("O")
    // ("...T12:00:00.0000000Z"). This is exactly the shape that breaks the UpdatedAt CAS.
    private static readonly DateTime BaseNow = new(2025, 6, 15, 12, 0, 0, DateTimeKind.Utc);

    public RedisRealScriptTests(RedisRealScriptFixture fx)
    {
        _fx = fx;
        _db = fx.Db;
        _db.Execute("FLUSHALL");

        _now = BaseNow;
        _clock = Substitute.For<ITickerClock>();
        _clock.UtcNow.Returns(_ => _now);

        _options = new SchedulerOptionsBuilder { NodeIdentifier = "real-node" };
        var redisOptions = new TickerQRedisOptionBuilder
        {
            JsonSerializerContext = TestJsonSerializerContext.Default
        };
        _provider = new TickerRedisPersistenceProvider<TimeTickerEntity, CronTickerEntity>(
            _db, _clock, _options, redisOptions,
            NullLogger<TickerRedisPersistenceProvider<TimeTickerEntity, CronTickerEntity>>.Instance);
    }

    private TimeTickerEntity NewIdleTicker(int[]? retryIntervals = null)
        => new()
        {
            Id = Guid.NewGuid(),
            Function = "RealFn",
            ExecutionTime = BaseNow.AddMinutes(-5),
            Status = TickerStatus.Idle,
            CreatedAt = BaseNow.AddHours(-1),
            UpdatedAt = BaseNow.AddHours(-1),
            Request = [],
            RetryIntervals = retryIntervals
        };

    private string RawJson(Guid id) => (string)_db.StringGet(RedisKeyBuilder.TimeTickerKey(id))!;

    [Fact]
    public async Task AddTimeTickers_InitOwnedRace_FirstWriterWinsAndPublishesIndexesAtomically()
    {
        var id = Guid.NewGuid();
        var first = NewIdleTicker();
        first.Id = id;
        first.InitIdentifier = "startup:redis-race";
        first.Description = "first";
        var second = NewIdleTicker();
        second.Id = id;
        second.InitIdentifier = first.InitIdentifier;
        second.Description = "second";

        var affected = await Task.WhenAll(
            _provider.AddTimeTickers([first]),
            _provider.AddTimeTickers([second]));

        Assert.Equal([0, 1], affected.OrderBy(x => x).ToArray());
        var persisted = await _provider.GetTimeTickerById(id);
        Assert.NotNull(persisted);
        Assert.Contains(persisted!.Description, new[] { "first", "second" });
        Assert.True(_db.SetContains(RedisKeyBuilder.TimeTickerIdsKey, id.ToString()));
        Assert.True(_db.SortedSetScore(RedisKeyBuilder.TimeTickerPendingKey, id.ToString()).HasValue);
    }

    [Fact]
    public async Task UpdateTimeTicker_WrongTypeIndexFailsBeforeDocumentResultOrEvidenceMutation()
    {
        var ticker = NewIdleTicker();
        Assert.Equal(1, await _provider.AddTimeTickers([ticker], CancellationToken.None));
        var documentKey = RedisKeyBuilder.TimeTickerKey(ticker.Id);
        var resultKey = RedisKeyBuilder.TimeTickerResultKey(ticker.Id);
        var evidenceField = $"{(int)TickerType.TimeTicker}:{ticker.Id:D}";
        var originalDocument = await _db.StringGetAsync(documentKey);
        await _db.StringSetAsync(resultKey, "result-before");
        await _db.HashSetAsync(RedisKeyBuilder.TerminalMutationEvidenceKey, evidenceField, "evidence-before");
        await _db.KeyDeleteAsync(RedisKeyBuilder.TimeTickerIdsKey);
        await _db.StringSetAsync(RedisKeyBuilder.TimeTickerIdsKey, "wrong-type");
        ticker.Description = "must-not-persist";

        await Assert.ThrowsAsync<RedisServerException>(() =>
            _provider.UpdateTimeTickers([ticker], CancellationToken.None));

        Assert.Equal(originalDocument, await _db.StringGetAsync(documentKey));
        Assert.Equal("result-before", (string?)await _db.StringGetAsync(resultKey));
        Assert.Equal("evidence-before", (string?)await _db.HashGetAsync(
            RedisKeyBuilder.TerminalMutationEvidenceKey, evidenceField));
        Assert.Equal("wrong-type", (string?)await _db.StringGetAsync(RedisKeyBuilder.TimeTickerIdsKey));
    }

    [Fact]
    public async Task RepairTimeTickerChains_CasPreservesIndexesAndResult_AndRejectsMalformedBeforeMutation()
    {
        var generation = Guid.NewGuid();
        var root = NewIdleTicker();
        root.ChainRootId = Guid.NewGuid();
        root.ChainGeneration = generation;
        var child = NewIdleTicker();
        child.ParentId = root.Id;
        child.ChainRootId = child.Id;
        child.ChainGeneration = Guid.NewGuid();
        root.Children = [child];
        var rootKey = RedisKeyBuilder.TimeTickerKey(root.Id);
        await _db.StringSetAsync(rootKey,
            JsonSerializer.Serialize(root, TestJsonSerializerContext.Default.TimeTickerEntity));
        await _db.SetAddAsync(RedisKeyBuilder.TimeTickerIdsKey, root.Id.ToString());
        await _db.SortedSetAddAsync(RedisKeyBuilder.TimeTickerPendingKey, root.Id.ToString(), 123);
        await _db.StringSetAsync(RedisKeyBuilder.TimeTickerResultKey(root.Id), new byte[] { 9, 8, 7 });

        Assert.Equal(new TimeTickerChainRepairResult(2, 2, 0),
            await _provider.RepairTimeTickerChainsAsync());
        var persisted = await _provider.GetTimeTickerById(root.Id);
        var persistedChild = Assert.Single(persisted.Children);
        Assert.Equal((root.Id, generation), (persisted.ChainRootId, persisted.ChainGeneration));
        Assert.Equal((root.Id, generation), (persistedChild.ChainRootId, persistedChild.ChainGeneration));
        Assert.Equal(123, await _db.SortedSetScoreAsync(RedisKeyBuilder.TimeTickerPendingKey, root.Id.ToString()));
        Assert.Equal(new byte[] { 9, 8, 7 },
            (byte[])(await _db.StringGetAsync(RedisKeyBuilder.TimeTickerResultKey(root.Id)))!);

        var orphan = NewIdleTicker();
        orphan.ParentId = Guid.NewGuid();
        await _db.StringSetAsync(RedisKeyBuilder.TimeTickerKey(orphan.Id),
            JsonSerializer.Serialize(orphan, TestJsonSerializerContext.Default.TimeTickerEntity));
        await _db.SetAddAsync(RedisKeyBuilder.TimeTickerIdsKey, orphan.Id.ToString());
        var before = await _db.StringGetAsync(rootKey);

        var error = await Assert.ThrowsAsync<TimeTickerChainRepairException>(
            () => _provider.RepairTimeTickerChainsAsync());
        Assert.Equal(TimeTickerChainMalformedKind.Orphan, error.Kind);
        Assert.Equal(before, await _db.StringGetAsync(rootKey));
    }

    // -------------------------------------------------------------------------
    // 1. Empty (non-null) Children array must survive a real cjson re-encode.
    //    The mock never runs cjson, so it hides that `"Children":[]` becomes
    //    `"Children":{}` on re-encode and then fails C# deserialization -> the
    //    acquire silently returns nothing.
    // -------------------------------------------------------------------------
    [Fact]
    public async Task Acquire_ThroughRealLua_PreservesEmptyChildrenArray()
    {
        var ticker = NewIdleTicker();
        Assert.Equal(1, await _provider.AddTimeTickers([ticker], CancellationToken.None));

        var acquired = await _provider.AcquireImmediateTimeTickersAsync([ticker.Id], CancellationToken.None);

        Assert.Single(acquired);
        Assert.Equal(TickerStatus.InProgress, acquired[0].Status);

        using var doc = JsonDocument.Parse(RawJson(ticker.Id));
        Assert.Equal(JsonValueKind.Array, doc.RootElement.GetProperty("Children").ValueKind);
    }

    // -------------------------------------------------------------------------
    // 2. Empty (non-null) RetryIntervals array must survive a real cjson re-encode.
    // -------------------------------------------------------------------------
    [Fact]
    public async Task Acquire_ThroughRealLua_PreservesEmptyRetryIntervalsArray()
    {
        var ticker = NewIdleTicker(retryIntervals: []);
        Assert.Equal(1, await _provider.AddTimeTickers([ticker], CancellationToken.None));

        var acquired = await _provider.AcquireImmediateTimeTickersAsync([ticker.Id], CancellationToken.None);

        Assert.Single(acquired);
        using var doc = JsonDocument.Parse(RawJson(ticker.Id));
        Assert.Equal(JsonValueKind.Array, doc.RootElement.GetProperty("RetryIntervals").ValueKind);
        Assert.Empty(doc.RootElement.GetProperty("RetryIntervals").EnumerateArray());
    }

    // -------------------------------------------------------------------------
    // 3. Acquire -> two consecutive generation-guarded writes -> terminal.
    //    The second write's UpdatedAt CAS compares the stored timestamp string
    //    against expectedUpdatedAt.ToString("O"). With trailing-zero timestamps
    //    the first CasReplace writes a System.Text.Json-trimmed UpdatedAt, so the
    //    second write's exact-string compare fails and the terminal status is
    //    silently dropped. This must reach a persisted terminal state.
    // -------------------------------------------------------------------------
    [Fact]
    public async Task Acquire_ThenTwoConsecutiveFencedWrites_PersistTerminalStatus()
    {
        var ticker = NewIdleTicker();
        await _provider.AddTimeTickers([ticker], CancellationToken.None);

        _now = BaseNow; // acquire
        var acquired = Assert.Single(
            await _provider.AcquireImmediateTimeTickersAsync([ticker.Id], CancellationToken.None));
        Assert.NotNull(acquired.AcquisitionToken);

        // First guarded write: retry bookkeeping (non-terminal). Stamps UpdatedAt = T1,
        // which has trailing fractional zeros.
        _now = BaseNow.AddSeconds(5);
        var retryWrite = new InternalFunctionContext()
            .SetProperty(x => x.TickerId, ticker.Id)
            .SetProperty(x => x.Type, TickerType.TimeTicker)
            .SetProperty(x => x.RetryCount, 1);
        Assert.Equal(1, await _provider.UpdateTimeTicker(retryWrite, CancellationToken.None));

        // Second guarded write: fenced terminal completion. Its UpdatedAt CAS must match
        // the timestamp the first write persisted.
        _now = BaseNow.AddSeconds(10);
        var terminalWrite = new InternalFunctionContext()
            .SetProperty(x => x.TickerId, ticker.Id)
            .SetProperty(x => x.Type, TickerType.TimeTicker)
            .SetProperty(x => x.AcquisitionToken, acquired.AcquisitionToken)
            .SetProperty(x => x.Status, TickerStatus.Done);
        Assert.Equal(1, await _provider.UpdateTimeTicker(terminalWrite, CancellationToken.None));

        var persisted = await _provider.GetTimeTickerById(ticker.Id, CancellationToken.None);
        Assert.NotNull(persisted);
        Assert.Equal(TickerStatus.Done, persisted!.Status);
        Assert.Equal(1, persisted.RetryCount);
        Assert.Null(persisted.AcquisitionToken);
        Assert.Null(persisted.LeaseUntil);
    }

    [Theory]
    [InlineData("Acquire")]
    [InlineData("AcquireOnDemand")]
    [InlineData("TransitionQueued")]
    [InlineData("Release")]
    [InlineData("RecoverDeadNode")]
    [InlineData("RecoverStale")]
    public async Task Scoped_runtime_scripts_reject_missing_activation_before_mutation_real_lua(string scriptName)
    {
        var id = Guid.NewGuid();
        var entityKey = (RedisKey)$"tq:test:{scriptName}:{id}:entity";
        var resultKey = (RedisKey)$"tq:test:{scriptName}:{id}:result";
        var evidenceKey = (RedisKey)$"tq:test:{scriptName}:{id}:evidence";
        var activationKey = (RedisKey)$"tq:test:{scriptName}:{id}:activation";
        var token = Guid.NewGuid().ToString();
        var original = $"{{\"Id\":\"{id}\",\"Status\":0,\"UpdatedAt\":\"{BaseNow:O}\",\"Children\":[]}}";
        await _db.StringSetAsync(entityKey, original);
        RedisKey[] keys;
        RedisValue[] args;
        switch (scriptName)
        {
            case "Acquire":
                keys = [entityKey, resultKey, evidenceKey, activationKey];
                args = ["owner", BaseNow.ToString("O"), "2", "", "0", "1", token, BaseNow.AddMinutes(1).ToString("O"), "tq:tt:", "7", "scoped"];
                break;
            case "AcquireOnDemand":
                keys = [entityKey, resultKey, evidenceKey, activationKey];
                args = ["owner", BaseNow.ToString("O"), BaseNow.AddMinutes(1).ToString("O"), token, "2", BaseNow.ToString("O"), "1", "7", "scoped"];
                break;
            case "TransitionQueued":
                keys = [entityKey, activationKey];
                args = ["owner", token, BaseNow.ToString("O"), "1", "2", BaseNow.AddMinutes(1).ToString("O"), "7", "scoped"];
                break;
            case "Release":
                keys = [entityKey, resultKey, activationKey];
                args = ["owner", BaseNow.ToString("O"), "0", "1", "7", "scoped"];
                break;
            case "RecoverDeadNode":
                keys = [entityKey, resultKey, activationKey];
                args = ["owner", BaseNow.ToString("O"), "0", "1", "2", "7", "scoped"];
                break;
            default:
                keys = [entityKey, resultKey, activationKey];
                args = [BaseNow.ToString("O"), BaseNow.AddMinutes(-1).ToString("O"), "1", "0", "0", "1", "2", "3", "stale", "5", "revision", "7", "scoped"];
                break;
        }

        var result = await _db.ScriptEvaluateAsync(LuaScriptLoader.Load(scriptName), keys, args);

        Assert.True(result.IsNull);
        Assert.Equal(original, (string?)await _db.StringGetAsync(entityKey));
    }

    [Theory]
    [InlineData("Acquire", 0)]
    [InlineData("AcquireOnDemand", 0)]
    [InlineData("TransitionQueued", 1)]
    [InlineData("Release", 1)]
    [InlineData("RecoverDeadNode", 2)]
    [InlineData("RecoverStale", 2)]
    public async Task Legacy_runtime_scripts_reject_adoption_tombstone_before_mutation_real_lua(
        string scriptName, int status)
    {
        var id = Guid.NewGuid();
        var entityKey = (RedisKey)$"tq:test:{scriptName}:{id}:legacy-entity";
        var resultKey = (RedisKey)$"tq:test:{scriptName}:{id}:legacy-result";
        var evidenceKey = (RedisKey)$"tq:test:{scriptName}:{id}:legacy-evidence";
        var activationKey = (RedisKey)$"tq:test:{scriptName}:{id}:legacy-activation";
        var token = Guid.NewGuid().ToString();
        var original = $"{{\"Id\":\"{id}\",\"Status\":{status},\"LockHolder\":\"owner\"," +
                       $"\"LockedAt\":\"{BaseNow.AddMinutes(-3):O}\",\"LeaseUntil\":\"{BaseNow.AddMinutes(-2):O}\"," +
                       $"\"AcquisitionToken\":\"{token}\",\"UpdatedAt\":\"{BaseNow.AddMinutes(-3):O}\",\"Children\":[]}}";
        await _db.StringSetAsync(entityKey, original);
        await _db.HashSetAsync(activationKey, "legacyAdoptionState", "fenced");
        RedisKey[] keys;
        RedisValue[] args;
        switch (scriptName)
        {
            case "Acquire":
                keys = [entityKey, resultKey, evidenceKey, activationKey];
                args = ["owner", BaseNow.ToString("O"), "2", "", "0", "1", token,
                    BaseNow.AddMinutes(1).ToString("O"), "tq:tt:", "7", "legacy"];
                break;
            case "AcquireOnDemand":
                keys = [entityKey, resultKey, evidenceKey, activationKey];
                args = ["owner", BaseNow.ToString("O"), BaseNow.AddMinutes(1).ToString("O"), token,
                    "2", BaseNow.ToString("O"), "1", "7", "legacy"];
                break;
            case "TransitionQueued":
                keys = [entityKey, activationKey];
                args = ["owner", token, BaseNow.ToString("O"), "1", "2",
                    BaseNow.AddMinutes(1).ToString("O"), "7", "legacy"];
                break;
            case "Release":
                keys = [entityKey, resultKey, activationKey];
                args = ["owner", BaseNow.ToString("O"), "0", "1", "7", "legacy"];
                break;
            case "RecoverDeadNode":
                keys = [entityKey, resultKey, activationKey];
                args = ["owner", BaseNow.ToString("O"), "0", "1", "2", "7", "legacy"];
                break;
            default:
                keys = [entityKey, resultKey, activationKey];
                args = [BaseNow.ToString("O"), BaseNow.AddMinutes(-1).ToString("O"), "1", "0",
                    "0", "1", "2", "3", "stale", "5", "revision", "7", "legacy"];
                break;
        }

        var result = await _db.ScriptEvaluateAsync(LuaScriptLoader.Load(scriptName), keys, args);

        Assert.True(result.IsNull);
        Assert.Equal(original, (string?)await _db.StringGetAsync(entityKey));
    }

    [Fact]
    public async Task Legacy_repair_and_quarantine_scripts_reject_adoption_tombstone_before_mutation_real_lua()
    {
        var prefix = $"tq:test:repair-fence:{Guid.NewGuid()}";
        RedisKey activation = $"{prefix}:activation";
        RedisKey ids = $"{prefix}:ids";
        RedisKey phase = $"{prefix}:phase";
        RedisKey cursor = $"{prefix}:cursor";
        RedisKey pending = $"{prefix}:pending";
        RedisKey quarantine = $"{prefix}:quarantine";
        await _db.HashSetAsync(activation, "legacyAdoptionState", "fenced");
        await _db.SetAddAsync(ids, Guid.NewGuid().ToString());

        var repair = (RedisResult[])await _db.ScriptEvaluateAsync(
            LuaScriptLoader.Load("RepairCronDiscoverability"),
            [ids, phase, cursor, pending, quarantine, activation],
            [(RedisValue)16, (RedisValue)$"{prefix}:cron:"]);
        Assert.Equal("fenced", (string)repair[3]);
        Assert.False(await _db.KeyExistsAsync(phase));
        Assert.False(await _db.KeyExistsAsync(cursor));

        var occurrenceId = Guid.NewGuid();
        var cronId = Guid.NewGuid();
        RedisKey document = $"{prefix}:co:{occurrenceId}";
        var original = $"{{\"Id\":\"{occurrenceId}\",\"CronTickerId\":\"{cronId}\",\"Status\":0}}";
        await _db.StringSetAsync(document, original);
        var quarantineResult = await _db.ScriptEvaluateAsync(
            LuaScriptLoader.Load("DeletePendingCronOccurrence"),
            [document, (RedisKey)$"{prefix}:result", (RedisKey)$"{prefix}:all", (RedisKey)$"{prefix}:pending-occ",
             (RedisKey)$"{prefix}:by-cron", (RedisKey)$"{prefix}:r1", (RedisKey)$"{prefix}:r2",
             (RedisKey)$"{prefix}:r3", (RedisKey)$"{prefix}:r4", (RedisKey)$"{prefix}:occ-quarantine",
             (RedisKey)$"{prefix}:slot", activation],
            [(RedisValue)occurrenceId.ToString(), (RedisValue)cronId.ToString(), (RedisValue)BaseNow.ToString("O"),
             (RedisValue)"0", (RedisValue)"1", (RedisValue)"3", (RedisValue)"stale", (RedisValue)0]);
        Assert.Equal(-3, (long)quarantineResult);
        Assert.Equal(original, (string?)await _db.StringGetAsync(document));
    }

    [Fact]
    public async Task Derived_index_and_retention_mutation_rejects_adoption_tombstone_atomically_real_lua()
    {
        var tag = $"{{derived-fence-{Guid.NewGuid():N}}}";
        RedisKey activation = $"tq:{tag}:activation";
        RedisKey ids = $"tq:{tag}:ids";
        RedisKey pending = $"tq:{tag}:pending";
        RedisKey retention = $"tq:{tag}:retention:failed";
        var id = Guid.NewGuid().ToString();
        await _db.HashSetAsync(activation, "legacyAdoptionState", "adopting");

        var result = await _db.ScriptEvaluateAsync(
            LuaScriptLoader.Load("MutateDerivedIndexes"),
            [activation, ids, pending, retention],
            [(RedisValue)id, 3, "SADD", 2, "", "ZADD", 3, 123, "ZADD", 4, 456]);

        Assert.Equal(0, (long)result);
        Assert.False(await _db.KeyExistsAsync(ids));
        Assert.False(await _db.KeyExistsAsync(pending));
        Assert.False(await _db.KeyExistsAsync(retention));
    }

    [Fact]
    public async Task Derived_index_mutation_preflights_invalid_score_before_any_write_real_lua()
    {
        var tag = $"{{derived-score-{Guid.NewGuid():N}}}";
        RedisKey activation = $"tq:{tag}:activation";
        RedisKey ids = $"tq:{tag}:ids";
        RedisKey pending = $"tq:{tag}:pending";
        var id = Guid.NewGuid().ToString();

        await Assert.ThrowsAsync<RedisServerException>(() => _db.ScriptEvaluateAsync(
            LuaScriptLoader.Load("MutateDerivedIndexes"),
            [activation, ids, pending],
            [(RedisValue)id, 2, "SADD", 2, "", "ZADD", 3, "not-a-number"]));

        Assert.False(await _db.KeyExistsAsync(ids));
        Assert.False(await _db.KeyExistsAsync(pending));
    }

    [Fact]
    public async Task Unified_time_ticker_mutation_preflights_late_index_type_before_document_write_real_lua()
    {
        var tag = $"{{unified-preflight-{Guid.NewGuid():N}}}";
        var id = Guid.NewGuid();
        RedisKey document = $"tq:{tag}:tt:{id}";
        RedisKey activation = $"tq:{tag}:activation";
        RedisKey ids = $"tq:{tag}:ids";
        RedisKey pending = $"tq:{tag}:pending";
        RedisKey succeeded = $"tq:{tag}:retention:succeeded";
        RedisKey failed = $"tq:{tag}:retention:failed";
        RedisKey cancelled = $"tq:{tag}:retention:cancelled";
        RedisKey skipped = $"tq:{tag}:retention:skipped";
        var expectedUpdatedAt = BaseNow.ToString("O");
        var original = $"{{\"Id\":\"{id}\",\"Status\":0,\"UpdatedAt\":\"{expectedUpdatedAt}\"}}";
        var replacement = $"{{\"Id\":\"{id}\",\"Status\":7,\"UpdatedAt\":\"{BaseNow.AddTicks(1):O}\"}}";
        await _db.StringSetAsync(document, original);
        await _db.StringSetAsync(skipped, "wrong-type");

        await Assert.ThrowsAsync<RedisServerException>(() => _db.ScriptEvaluateAsync(
            LuaScriptLoader.Load("UpdateTimeTickerWithIndexes"),
            [document, activation, ids, pending, succeeded, failed, cancelled, skipped],
            [expectedUpdatedAt, replacement, id.ToString(), "0", "", 4, BaseNow.Ticks]));

        Assert.Equal(original, (string?)await _db.StringGetAsync(document));
        Assert.False(await _db.KeyExistsAsync(ids));
        Assert.False(await _db.KeyExistsAsync(pending));
        Assert.Equal("wrong-type", (string?)await _db.StringGetAsync(skipped));
    }

    [Fact]
    public async Task Retention_reconciliation_preflights_all_index_types_before_any_write_real_lua()
    {
        var id = Guid.NewGuid().ToString();
        await _db.ListRightPushAsync(RedisKeyBuilder.RetentionReconciliationPendingKey, id);
        await _db.SortedSetAddAsync(RedisKeyBuilder.TimeTickerRetentionSucceededKey, id, 123);
        await _db.StringSetAsync(RedisKeyBuilder.TimeTickerRetentionFailedKey, "wrong-type");

        await Assert.ThrowsAsync<RedisServerException>(() =>
            _provider.ReconcileRetentionIndexesAsync(1, CancellationToken.None));

        Assert.Equal(id, (string?)await _db.ListGetByIndexAsync(
            RedisKeyBuilder.RetentionReconciliationPendingKey, 0));
        Assert.Equal(123, await _db.SortedSetScoreAsync(
            RedisKeyBuilder.TimeTickerRetentionSucceededKey, id));
        Assert.Equal("wrong-type", (string?)await _db.StringGetAsync(
            RedisKeyBuilder.TimeTickerRetentionFailedKey));
    }

    [Fact]
    public async Task Retention_reconciliation_preflights_document_shape_before_any_write_real_lua()
    {
        var id = Guid.NewGuid();
        var value = id.ToString();
        await _db.StringSetAsync(RedisKeyBuilder.TimeTickerKey(id), "5");
        await _db.ListRightPushAsync(RedisKeyBuilder.RetentionReconciliationPendingKey, value);
        await _db.SortedSetAddAsync(RedisKeyBuilder.TimeTickerRetentionSucceededKey, value, 123);
        await _db.StringSetAsync(RedisKeyBuilder.RetentionReconciliationPhaseKey, "time_ids");
        await _db.StringSetAsync(RedisKeyBuilder.RetentionReconciliationCursorKey, "0");

        await Assert.ThrowsAsync<RedisServerException>(() =>
            _provider.ReconcileRetentionIndexesAsync(1, CancellationToken.None));

        Assert.Equal("5", (string?)await _db.StringGetAsync(RedisKeyBuilder.TimeTickerKey(id)));
        Assert.Equal(value, (string?)await _db.ListGetByIndexAsync(
            RedisKeyBuilder.RetentionReconciliationPendingKey, 0));
        Assert.Equal(123, await _db.SortedSetScoreAsync(
            RedisKeyBuilder.TimeTickerRetentionSucceededKey, value));
        Assert.Equal("time_ids", (string?)await _db.StringGetAsync(
            RedisKeyBuilder.RetentionReconciliationPhaseKey));
        Assert.Equal("0", (string?)await _db.StringGetAsync(
            RedisKeyBuilder.RetentionReconciliationCursorKey));
    }

    [Fact]
    public async Task Retention_delete_preflights_every_key_before_removing_time_ticker_real_lua()
    {
        var ticker = NewIdleTicker();
        ticker.Status = TickerStatus.Done;
        ticker.ExecutedAt = BaseNow.AddDays(-10);
        await _provider.AddTimeTickers([ticker], CancellationToken.None);
        await _db.StringSetAsync(RedisKeyBuilder.RetentionReconciliationPhaseKey, "occurrence_keys");
        await _db.StringSetAsync(RedisKeyBuilder.RetentionReconciliationCursorKey, "0");
        await _db.StringSetAsync(RedisKeyBuilder.TerminalMutationEvidenceKey, "wrong-type");
        var documentBefore = await _db.StringGetAsync(RedisKeyBuilder.TimeTickerKey(ticker.Id));
        var scoreBefore = await _db.SortedSetScoreAsync(
            RedisKeyBuilder.TimeTickerRetentionSucceededKey, ticker.Id.ToString());

        await Assert.ThrowsAsync<RedisServerException>(() =>
            _provider.DeleteEligibleTimeTickerChainsAsync(
                new RetentionCutoffs(BaseNow.AddDays(-7), null, null, null), 10,
                RetentionCursor.Start, CancellationToken.None));

        Assert.Equal(documentBefore, await _db.StringGetAsync(RedisKeyBuilder.TimeTickerKey(ticker.Id)));
        Assert.True(await _db.SetContainsAsync(RedisKeyBuilder.TimeTickerIdsKey, ticker.Id.ToString()));
        Assert.Equal(scoreBefore, await _db.SortedSetScoreAsync(
            RedisKeyBuilder.TimeTickerRetentionSucceededKey, ticker.Id.ToString()));
        Assert.Equal("wrong-type", (string?)await _db.StringGetAsync(RedisKeyBuilder.TerminalMutationEvidenceKey));
    }

    [Fact]
    public async Task Retention_delete_preflights_every_key_before_removing_occurrence_real_lua()
    {
        var cron = NewCron("occurrence-retention-preflight");
        await _provider.InsertCronTickers([cron], CancellationToken.None);
        var occurrence = NewOccurrence(cron.Id, BaseNow.AddDays(-10), TickerStatus.Failed);
        occurrence.ExecutedAt = BaseNow.AddDays(-10);
        await _provider.InsertCronTickerOccurrences([occurrence], CancellationToken.None);
        await _db.StringSetAsync(RedisKeyBuilder.RetentionReconciliationPhaseKey, "time_keys");
        await _db.StringSetAsync(RedisKeyBuilder.RetentionReconciliationCursorKey, "0");
        await _db.StringSetAsync(RedisKeyBuilder.TerminalMutationEvidenceKey, "wrong-type");
        var documentBefore = await _db.StringGetAsync(RedisKeyBuilder.CronOccurrenceKey(occurrence.Id));
        var scoreBefore = await _db.SortedSetScoreAsync(
            RedisKeyBuilder.CronOccurrenceRetentionFailedKey, occurrence.Id.ToString());

        await Assert.ThrowsAsync<RedisServerException>(() =>
            _provider.DeleteEligibleCronTickerOccurrencesAsync(
                new RetentionCutoffs(null, BaseNow.AddDays(-7), null, null), 10,
                CancellationToken.None));

        Assert.Equal(documentBefore, await _db.StringGetAsync(RedisKeyBuilder.CronOccurrenceKey(occurrence.Id)));
        Assert.True(await _db.SetContainsAsync(RedisKeyBuilder.CronOccurrenceIdsKey, occurrence.Id.ToString()));
        Assert.True(await _db.SetContainsAsync(
            RedisKeyBuilder.CronOccurrencesByCronKey(cron.Id), occurrence.Id.ToString()));
        Assert.Equal(scoreBefore, await _db.SortedSetScoreAsync(
            RedisKeyBuilder.CronOccurrenceRetentionFailedKey, occurrence.Id.ToString()));
        Assert.Equal("wrong-type", (string?)await _db.StringGetAsync(RedisKeyBuilder.TerminalMutationEvidenceKey));
    }

    [Fact]
    public async Task Retention_reconciliation_rejects_invalid_date_and_object_children_before_any_write_real_lua()
    {
        var first = Guid.NewGuid();
        var corrupt = Guid.NewGuid();
        var firstJson = $"{{\"Id\":\"{first}\",\"Status\":{(int)TickerStatus.Done}," +
                        $"\"ExecutedAt\":\"{BaseNow.AddDays(-10):O}\",\"Children\":[]}}";
        var corruptJson = $"{{\"Id\":\"{corrupt}\",\"Status\":{(int)TickerStatus.Done}," +
                          "\"ExecutedAt\":\"2025-99-99T99:99:99.12345678Q\"," +
                          "\"Children\":{\"not\":\"an-array\"}}";
        await _db.StringSetAsync(RedisKeyBuilder.TimeTickerKey(first), firstJson);
        await _db.StringSetAsync(RedisKeyBuilder.TimeTickerKey(corrupt), corruptJson);
        await _db.ListRightPushAsync(RedisKeyBuilder.RetentionReconciliationPendingKey,
            [first.ToString(), corrupt.ToString()]);
        await _db.SortedSetAddAsync(RedisKeyBuilder.TimeTickerRetentionFailedKey, first.ToString(), 321);
        await _db.StringSetAsync(RedisKeyBuilder.RetentionReconciliationPhaseKey, "time_ids");
        await _db.StringSetAsync(RedisKeyBuilder.RetentionReconciliationCursorKey, "0");

        await Assert.ThrowsAsync<RedisServerException>(() =>
            _provider.ReconcileRetentionIndexesAsync(2, CancellationToken.None));

        Assert.Equal([first.ToString(), corrupt.ToString()],
            (await _db.ListRangeAsync(RedisKeyBuilder.RetentionReconciliationPendingKey))
            .Select(x => x.ToString()).ToArray());
        Assert.Equal(321, await _db.SortedSetScoreAsync(
            RedisKeyBuilder.TimeTickerRetentionFailedKey, first.ToString()));
        Assert.Null(await _db.SortedSetScoreAsync(
            RedisKeyBuilder.TimeTickerRetentionSucceededKey, first.ToString()));
        Assert.Equal("time_ids", (string?)await _db.StringGetAsync(
            RedisKeyBuilder.RetentionReconciliationPhaseKey));
        Assert.Equal("0", (string?)await _db.StringGetAsync(
            RedisKeyBuilder.RetentionReconciliationCursorKey));
    }

    [Fact]
    public async Task Retention_rejects_empty_object_children_at_root_and_nested_before_any_write_real_lua()
    {
        foreach (var nested in new[] { false, true })
        {
            var id = Guid.NewGuid();
            var child = Guid.NewGuid();
            var children = nested
                ? $"[{{\"Id\":\"{child}\",\"Status\":{(int)TickerStatus.Done}," +
                  $"\"ExecutedAt\":\"{BaseNow.AddDays(-10):O}\",\"Children\":{{}}}}]"
                : "{}";
            var raw = $"{{\"Id\":\"{id}\",\"Status\":{(int)TickerStatus.Done}," +
                      $"\"ExecutedAt\":\"{BaseNow.AddDays(-10):O}\",\"Children\":{children}}}";
            await _db.StringSetAsync(RedisKeyBuilder.TimeTickerKey(id), raw);
            await _db.ListRightPushAsync(RedisKeyBuilder.RetentionReconciliationPendingKey, id.ToString());
            await _db.StringSetAsync(RedisKeyBuilder.RetentionReconciliationPhaseKey, "time_ids");
            await _db.StringSetAsync(RedisKeyBuilder.RetentionReconciliationCursorKey, "0");

            await Assert.ThrowsAsync<RedisServerException>(() =>
                _provider.ReconcileRetentionIndexesAsync(1, CancellationToken.None));

            Assert.Equal(raw, (string?)await _db.StringGetAsync(RedisKeyBuilder.TimeTickerKey(id)));
            Assert.Equal(id.ToString(), (string?)await _db.ListGetByIndexAsync(
                RedisKeyBuilder.RetentionReconciliationPendingKey, 0));
            Assert.Null(await _db.SortedSetScoreAsync(
                RedisKeyBuilder.TimeTickerRetentionSucceededKey, id.ToString()));
            await _db.ListTrimAsync(RedisKeyBuilder.RetentionReconciliationPendingKey, 1, 0);
        }

        var deletionId = Guid.NewGuid();
        var deletionRaw = $"{{\"Id\":\"{deletionId}\",\"Status\":{(int)TickerStatus.Done}," +
                          $"\"ExecutedAt\":\"{BaseNow.AddDays(-10):O}\",\"Children\":{{}}}}";
        await _db.StringSetAsync(RedisKeyBuilder.TimeTickerKey(deletionId), deletionRaw);
        await _db.SetAddAsync(RedisKeyBuilder.TimeTickerIdsKey, deletionId.ToString());
        await _db.SortedSetAddAsync(
            RedisKeyBuilder.TimeTickerRetentionSucceededKey, deletionId.ToString(), 1);

        await Assert.ThrowsAsync<RedisServerException>(() =>
            _provider.DeleteEligibleTimeTickerChainsAsync(
                new RetentionCutoffs(BaseNow.AddDays(-7), null, null, null), 10,
                RetentionCursor.Start, CancellationToken.None));

        Assert.Equal(deletionRaw,
            (string?)await _db.StringGetAsync(RedisKeyBuilder.TimeTickerKey(deletionId)));
        Assert.True(await _db.SetContainsAsync(RedisKeyBuilder.TimeTickerIdsKey, deletionId.ToString()));
        Assert.Equal(1, await _db.SortedSetScoreAsync(
            RedisKeyBuilder.TimeTickerRetentionSucceededKey, deletionId.ToString()));
    }

    [Fact]
    public async Task Explicit_time_deletion_rejects_owned_root_and_descendant_id_real_lua()
    {
        var root = NewIdleTicker();
        var child = NewIdleTicker();
        child.ParentId = root.Id;
        root.Children = [child];
        root.Status = TickerStatus.InProgress;
        root.AcquisitionToken = Guid.NewGuid();
        root.LeaseUntil = BaseNow.AddMinutes(5);
        var rootJson = JsonSerializer.Serialize(root, TestJsonSerializerContext.Default.TimeTickerEntity);
        await _db.StringSetAsync(RedisKeyBuilder.TimeTickerKey(root.Id), rootJson);
        await _db.SetAddAsync(RedisKeyBuilder.TimeTickerIdsKey, root.Id.ToString());

        Assert.Equal(0, await _provider.RemoveTimeTickers([root.Id], CancellationToken.None));
        Assert.Equal(rootJson, (string?)await _db.StringGetAsync(RedisKeyBuilder.TimeTickerKey(root.Id)));

        var childJson = JsonSerializer.Serialize(child, TestJsonSerializerContext.Default.TimeTickerEntity);
        await _db.StringSetAsync(RedisKeyBuilder.TimeTickerKey(child.Id), childJson);
        await _db.SetAddAsync(RedisKeyBuilder.TimeTickerIdsKey, child.Id.ToString());

        Assert.Equal(0, await _provider.RemoveTimeTickers([child.Id], CancellationToken.None));
        Assert.Equal(childJson, (string?)await _db.StringGetAsync(RedisKeyBuilder.TimeTickerKey(child.Id)));
        Assert.Equal(rootJson, (string?)await _db.StringGetAsync(RedisKeyBuilder.TimeTickerKey(root.Id)));
    }

    [Fact]
    public async Task Release_acquired_time_root_revokes_chain_generation_real_lua()
    {
        var root = NewIdleTicker();
        Assert.Equal(1, await _provider.AddTimeTickers([root], CancellationToken.None));
        var acquired = Assert.Single(await _provider.AcquireImmediateTimeTickersAsync(
            [root.Id], CancellationToken.None));
        Assert.NotNull(acquired.ChainGeneration);
        var key = RedisKeyBuilder.TimeTickerKey(root.Id);
        var persisted = JsonSerializer.Deserialize(
            (string)(await _db.StringGetAsync(key))!,
            TestJsonSerializerContext.Default.TimeTickerEntity)!;
        persisted.Status = TickerStatus.Queued;
        await _db.StringSetAsync(key,
            JsonSerializer.Serialize(persisted, TestJsonSerializerContext.Default.TimeTickerEntity));

        await _provider.ReleaseAcquiredTimeTickers([root.Id], CancellationToken.None);

        var released = await _provider.GetTimeTickerById(root.Id, CancellationToken.None);
        Assert.Equal(TickerStatus.Idle, released.Status);
        Assert.Null(released.AcquisitionToken);
        Assert.Null(released.ChainGeneration);
    }

    [Fact]
    public async Task Release_requires_exact_owner_and_acquisition_token_real_lua()
    {
        var ownerless = NewIdleTicker();
        ownerless.Status = TickerStatus.Queued;
        ownerless.AcquisitionToken = Guid.NewGuid();
        var tokenless = NewIdleTicker();
        tokenless.Status = TickerStatus.Queued;
        tokenless.LockHolder = _options.ExecutionOwnerId;
        var foreign = NewIdleTicker();
        foreign.Status = TickerStatus.Queued;
        foreign.LockHolder = "other-node";
        foreign.LockedAt = null;
        foreign.AcquisitionToken = Guid.NewGuid();
        foreign.ChainGeneration = foreign.AcquisitionToken;

        var tickers = new[] { ownerless, tokenless, foreign };
        var originals = new Dictionary<Guid, RedisValue>();
        foreach (var ticker in tickers)
        {
            var key = RedisKeyBuilder.TimeTickerKey(ticker.Id);
            var raw = JsonSerializer.Serialize(ticker, TestJsonSerializerContext.Default.TimeTickerEntity);
            originals[ticker.Id] = raw;
            await _db.StringSetAsync(key, raw);
            await _db.SetAddAsync(RedisKeyBuilder.TimeTickerIdsKey, ticker.Id.ToString());
            await _db.StringSetAsync(RedisKeyBuilder.TimeTickerResultKey(ticker.Id), "result-before");
        }

        await _provider.ReleaseAcquiredTimeTickers([ownerless.Id, tokenless.Id], CancellationToken.None);
        await _provider.ReleaseAcquiredTimeTickers(Array.Empty<Guid>(), CancellationToken.None);

        foreach (var ticker in tickers)
        {
            Assert.Equal(originals[ticker.Id], await _db.StringGetAsync(RedisKeyBuilder.TimeTickerKey(ticker.Id)));
            Assert.Equal("result-before", (string?)await _db.StringGetAsync(
                RedisKeyBuilder.TimeTickerResultKey(ticker.Id)));
        }

        var occurrence = NewOccurrence(Guid.NewGuid(), BaseNow.AddMinutes(1), TickerStatus.Queued);
        occurrence.AcquisitionToken = Guid.NewGuid();
        var occurrenceKey = RedisKeyBuilder.CronOccurrenceKey(occurrence.Id);
        var occurrenceRaw = JsonSerializer.Serialize(
            occurrence, TestJsonSerializerContext.Default.CronTickerOccurrenceEntityCronTickerEntity);
        await _db.StringSetAsync(occurrenceKey, occurrenceRaw);
        await _db.SetAddAsync(RedisKeyBuilder.CronOccurrenceIdsKey, occurrence.Id.ToString());
        await _db.StringSetAsync(RedisKeyBuilder.CronOccurrenceResultKey(occurrence.Id), "cron-result-before");

        await _provider.ReleaseAcquiredCronTickerOccurrences(Array.Empty<Guid>(), CancellationToken.None);

        Assert.Equal(occurrenceRaw, (string?)await _db.StringGetAsync(occurrenceKey));
        Assert.Equal("cron-result-before", (string?)await _db.StringGetAsync(
            RedisKeyBuilder.CronOccurrenceResultKey(occurrence.Id)));
    }

    [Fact]
    public async Task Dead_node_recovery_requires_token_and_revokes_time_generation_real_lua()
    {
        const string deadNode = "dead-node";
        var tokenless = NewIdleTicker();
        tokenless.Status = TickerStatus.Queued;
        tokenless.LockHolder = deadNode;
        tokenless.ChainGeneration = Guid.NewGuid();
        var tokenlessKey = RedisKeyBuilder.TimeTickerKey(tokenless.Id);
        var tokenlessRaw = JsonSerializer.Serialize(
            tokenless, TestJsonSerializerContext.Default.TimeTickerEntity);
        await _db.StringSetAsync(tokenlessKey, tokenlessRaw);
        await _db.SetAddAsync(RedisKeyBuilder.TimeTickerIdsKey, tokenless.Id.ToString());
        await _db.StringSetAsync(RedisKeyBuilder.TimeTickerResultKey(tokenless.Id), "result-before");

        await _provider.ReleaseDeadNodeTimeTickerResources(deadNode, CancellationToken.None);

        Assert.Equal(tokenlessRaw, (string?)await _db.StringGetAsync(tokenlessKey));
        Assert.Equal("result-before", (string?)await _db.StringGetAsync(
            RedisKeyBuilder.TimeTickerResultKey(tokenless.Id)));

        var root = NewIdleTicker();
        var child = NewIdleTicker();
        child.ParentId = root.Id;
        root.Children = [child];
        Assert.Equal(1, await _provider.AddTimeTickers([root], CancellationToken.None));
        var acquired = Assert.Single(await _provider.AcquireImmediateTimeTickersAsync(
            [root.Id], CancellationToken.None));
        var acquiredChild = Assert.Single(acquired.Children);
        Assert.NotNull(acquired.AcquisitionToken);
        Assert.Equal(acquired.AcquisitionToken, acquiredChild.ChainGeneration);

        await _provider.ReleaseDeadNodeTimeTickerResources(
            _options.ExecutionOwnerId, CancellationToken.None);

        var released = await _provider.GetTimeTickerById(root.Id, CancellationToken.None);
        Assert.Equal(TickerStatus.Idle, released!.Status);
        Assert.Null(released.ChainGeneration);
        var late = new InternalFunctionContext
        {
            TickerId = acquiredChild.Id,
            Type = TickerType.TimeTicker,
            AcquisitionToken = acquired.AcquisitionToken,
            ResultEnvelope = new TickerResultEnvelope([1, 2, 3], 1, "application/json")
        }.SetProperty(x => x.Status, TickerStatus.Done)
            .SetProperty(x => x.ResultEnvelope,
                new TickerResultEnvelope([1, 2, 3], 1, "application/json"));
        Assert.Equal(0, await _provider.UpdateTimeTicker(late, CancellationToken.None));
        Assert.False(await _db.KeyExistsAsync(RedisKeyBuilder.TimeTickerResultKey(acquiredChild.Id)));
    }

    [Fact]
    public async Task Cron_discoverability_repair_preserves_pending_when_dynamic_document_has_wrong_type_real_lua()
    {
        var prefix = $"tq:test:discoverability-preflight:{Guid.NewGuid():N}";
        var id = Guid.NewGuid().ToString();
        RedisKey ids = $"{prefix}:ids";
        RedisKey phase = $"{prefix}:phase";
        RedisKey cursor = $"{prefix}:cursor";
        RedisKey pending = $"{prefix}:pending";
        RedisKey quarantine = $"{prefix}:quarantine";
        RedisKey activation = $"{prefix}:activation";
        await _db.ListRightPushAsync(pending, id);
        await _db.HashSetAsync($"{prefix}:cron:{id}", "wrong", "type");

        await Assert.ThrowsAsync<RedisServerException>(() => _db.ScriptEvaluateAsync(
            LuaScriptLoader.Load("RepairCronDiscoverability"),
            [ids, phase, cursor, pending, quarantine, activation],
            [(RedisValue)16, (RedisValue)$"{prefix}:cron:"]));

        Assert.Equal(id, (string?)await _db.ListGetByIndexAsync(pending, 0));
        Assert.False(await _db.KeyExistsAsync(phase));
        Assert.False(await _db.KeyExistsAsync(cursor));
        Assert.False(await _db.KeyExistsAsync(ids));
        Assert.False(await _db.KeyExistsAsync(quarantine));
    }

    // -------------------------------------------------------------------------
    // 4. Generation fencing through the real scripts: a stale acquisition token
    //    cannot commit a terminal write; the winning token can.
    // -------------------------------------------------------------------------
    [Fact]
    public async Task TerminalWrite_IsFencedByAcquisitionGeneration_RealLua()
    {
        var ticker = NewIdleTicker();
        await _provider.AddTimeTickers([ticker], CancellationToken.None);

        _now = BaseNow;
        var acquired = Assert.Single(
            await _provider.AcquireImmediateTimeTickersAsync([ticker.Id], CancellationToken.None));

        _now = BaseNow.AddSeconds(5);
        var stale = new InternalFunctionContext()
            .SetProperty(x => x.TickerId, ticker.Id)
            .SetProperty(x => x.Type, TickerType.TimeTicker)
            .SetProperty(x => x.AcquisitionToken, Guid.NewGuid()) // wrong generation
            .SetProperty(x => x.Status, TickerStatus.Done);
        Assert.Equal(0, await _provider.UpdateTimeTicker(stale, CancellationToken.None));

        var winning = new InternalFunctionContext()
            .SetProperty(x => x.TickerId, ticker.Id)
            .SetProperty(x => x.Type, TickerType.TimeTicker)
            .SetProperty(x => x.AcquisitionToken, acquired.AcquisitionToken)
            .SetProperty(x => x.Status, TickerStatus.Done);
        Assert.Equal(1, await _provider.UpdateTimeTicker(winning, CancellationToken.None));

        var persisted = await _provider.GetTimeTickerById(ticker.Id, CancellationToken.None);
        Assert.Equal(TickerStatus.Done, persisted!.Status);
    }

    [Fact]
    public async Task Exact_old_terminal_replay_is_rejected_after_new_generation_is_acquired()
    {
        var ticker = NewIdleTicker();
        await _provider.AddTimeTickers([ticker], CancellationToken.None);
        var runA = Assert.Single(await _provider.AcquireImmediateTimeTickersAsync([ticker.Id]));
        var terminalA = new InternalFunctionContext()
            .SetProperty(x => x.TickerId, ticker.Id)
            .SetProperty(x => x.Type, TickerType.TimeTicker)
            .SetProperty(x => x.AcquisitionToken, runA.AcquisitionToken)
            .SetProperty(x => x.ResultEnvelope, (TickerResultEnvelope)null)
            .SetProperty(x => x.Status, TickerStatus.Done);

        Assert.True(await _provider.CommitTerminalTickerAsync(terminalA));
        Assert.True(await _provider.CommitTerminalTickerAsync(terminalA));

        Assert.Equal(1, await _provider.UpdateTimeTicker(
            new InternalFunctionContext { TickerId = ticker.Id, Type = TickerType.TimeTicker }
                .SetProperty(x => x.Status, TickerStatus.Idle)));
        var idle = await _provider.GetTimeTickerById(ticker.Id);
        var candidate = new TimeTickerEntity { Id = ticker.Id, UpdatedAt = idle!.UpdatedAt };
        var queued = new List<TimeTickerEntity>();
        await foreach (var item in _provider.QueueTimeTickers([candidate])) queued.Add(item);
        var runB = Assert.Single(queued);
        Assert.NotEqual(runA.AcquisitionToken, runB.AcquisitionToken);
        Assert.Equal(TickerStatus.Queued, runB.Status);

        Assert.False(await _provider.CommitTerminalTickerAsync(terminalA));
        var persisted = await _provider.GetTimeTickerById(ticker.Id);
        Assert.Equal(runB.AcquisitionToken, persisted!.AcquisitionToken);
        Assert.Equal(TickerStatus.Queued, persisted.Status);
    }

    [Fact]
    public async Task StaleChildGeneration_CannotOverwriteRerunStatusOrResult_RealLua()
    {
        var child = NewIdleTicker();
        child.ExecutionTime = null;
        var root = NewIdleTicker();
        root.Children.Add(child);
        await _provider.AddTimeTickers([root], CancellationToken.None);

        var runA = Assert.Single(await _provider.AcquireImmediateTimeTickersAsync(
            [root.Id], CancellationToken.None));
        var generationA = runA.ChainGeneration!.Value;
        var rootDone = new InternalFunctionContext
        {
            TickerId = root.Id, ChainRootId = root.Id, ChainGeneration = generationA,
            AcquisitionToken = runA.AcquisitionToken, Type = TickerType.TimeTicker
        }.SetProperty(x => x.Status, TickerStatus.Done)
         .SetProperty(x => x.ReleaseLock, true);
        Assert.Equal(1, await _provider.UpdateTimeTicker(rootDone, CancellationToken.None));

        _now = BaseNow.AddSeconds(1);
        var runB = await _provider.AcquireTimeTickerOnDemandAsync(
            root.Id, _now, CancellationToken.None);
        Assert.NotNull(runB);
        var generationB = runB!.ChainGeneration!.Value;
        Assert.NotEqual(generationA, generationB);

        InternalFunctionContext Success(Guid generation, byte value) =>
            new InternalFunctionContext
            {
                TickerId = child.Id, ParentId = root.Id, ChainRootId = root.Id,
                ChainGeneration = generation, Type = TickerType.TimeTicker
            }.SetProperty(x => x.Status, TickerStatus.Done)
             .SetProperty(x => x.ResultEnvelope,
                 new TickerResultEnvelope([value], 1, "application/octet-stream"));

        Assert.True(await _provider.CommitSuccessfulTickerAsync(Success(generationB, 2)));
        Assert.False(await _provider.CommitSuccessfulTickerAsync(Success(generationA, 1)));
        Assert.Equal(TickerStatus.Done,
            (await _provider.GetTimeTickerById(root.Id))!.Children.Single().Status);
        Assert.Equal([2], (await _provider.GetTimeTickerResultAsync(child.Id))!.ToPayloadArray());

        using var json = JsonDocument.Parse(RawJson(root.Id));
        Assert.Equal(generationB.ToString(),
            json.RootElement.GetProperty("ChainGeneration").GetString(), ignoreCase: true);
    }

    [Fact]
    public async Task Retention_DeletesStandaloneButRetainsChainedRoot_RealLua()
    {
        var standalone = NewIdleTicker();
        standalone.Status = TickerStatus.Done;
        standalone.ExecutedAt = BaseNow.AddDays(-10);

        var chained = NewIdleTicker();
        chained.Status = TickerStatus.Done;
        chained.ExecutedAt = BaseNow.AddDays(-10);
        var child = NewIdleTicker();
        child.Status = TickerStatus.Done;
        child.ExecutedAt = BaseNow.AddDays(-10);
        chained.Children.Add(child);

        await _provider.AddTimeTickers([standalone, chained], CancellationToken.None);
        var result = await _provider.DeleteEligibleTimeTickerChainsAsync(
            new RetentionCutoffs(BaseNow.AddDays(-7), null, null, null),
            10,
            RetentionCursor.Start,
            CancellationToken.None);

        Assert.Equal(1, result.Deleted);
        Assert.Null(await _provider.GetTimeTickerById(standalone.Id, CancellationToken.None));
        Assert.NotNull(await _provider.GetTimeTickerById(chained.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Retention_UsesStrictCutoffAndBlocksOwnedRows_RealLua()
    {
        var cutoff = BaseNow.AddDays(-7);
        var exactlyAtCutoff = NewIdleTicker();
        exactlyAtCutoff.Status = TickerStatus.Done;
        exactlyAtCutoff.ExecutedAt = cutoff;
        var owned = NewIdleTicker();
        owned.Status = TickerStatus.Done;
        owned.ExecutedAt = cutoff.AddDays(-1);
        owned.AcquisitionToken = Guid.NewGuid();
        owned.LeaseUntil = BaseNow.AddMinutes(1);
        await _provider.AddTimeTickers([exactlyAtCutoff, owned], CancellationToken.None);

        var result = await _provider.DeleteEligibleTimeTickerChainsAsync(
            new RetentionCutoffs(cutoff, null, null, null), 10,
            RetentionCursor.Start, CancellationToken.None);

        Assert.Equal(0, result.Deleted);
        Assert.NotNull(await _provider.GetTimeTickerById(exactlyAtCutoff.Id, CancellationToken.None));
        Assert.NotNull(await _provider.GetTimeTickerById(owned.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Retention_DeletesOccurrenceButPreservesCronDefinition_RealLua()
    {
        var cron = new CronTickerEntity
        {
            Id = Guid.NewGuid(),
            Function = "RetentionCron",
            Expression = "*/5 * * * *",
            CreatedAt = BaseNow.AddDays(-20),
            UpdatedAt = BaseNow.AddDays(-20),
            Request = []
        };
        await _provider.InsertCronTickers([cron], CancellationToken.None);
        var occurrence = new CronTickerOccurrenceEntity<CronTickerEntity>
        {
            Id = Guid.NewGuid(),
            CronTickerId = cron.Id,
            ExecutionTime = BaseNow.AddDays(-10),
            Status = TickerStatus.Failed,
            ExecutedAt = BaseNow.AddDays(-10),
            CreatedAt = BaseNow.AddDays(-10),
            UpdatedAt = BaseNow.AddDays(-10)
        };
        await _provider.InsertCronTickerOccurrences([occurrence], CancellationToken.None);

        var result = await _provider.DeleteEligibleCronTickerOccurrencesAsync(
            new RetentionCutoffs(null, BaseNow.AddDays(-7), null, null),
            10,
            CancellationToken.None);

        Assert.Equal(1, result.Deleted);
        Assert.NotNull(await _provider.GetCronTickerById(cron.Id, CancellationToken.None));
        Assert.Empty(await _provider.GetAllCronTickerOccurrences(_ => true, CancellationToken.None));
    }

    [Fact]
    public async Task RetentionReconciliation_BackfillsMissingIndexAndResumes_RealLua()
    {
        var tickers = Enumerable.Range(0, 3).Select(i =>
        {
            var ticker = NewIdleTicker();
            ticker.Status = TickerStatus.Done;
            ticker.ExecutedAt = BaseNow.AddDays(-10).AddMinutes(i);
            return ticker;
        }).ToArray();
        await _provider.AddTimeTickers(tickers, CancellationToken.None);
        foreach (var ticker in tickers)
            await _db.SortedSetRemoveAsync(RedisKeyBuilder.TimeTickerRetentionSucceededKey, ticker.Id.ToString());

        var first = await _provider.ReconcileRetentionIndexesAsync(1, CancellationToken.None);
        Assert.True(first.HasMore);
        Assert.Equal(1, first.Examined);
        Assert.Contains("pending=", first.NextCursor);
        for (var i = 0; i < 20; i++)
        {
            var result = await _provider.ReconcileRetentionIndexesAsync(1, CancellationToken.None);
            if (!result.HasMore)
                break;
        }

        Assert.Equal(3, await _db.SortedSetLengthAsync(RedisKeyBuilder.TimeTickerRetentionSucceededKey));
    }

    [Fact]
    public async Task Retention_ChildShapedRowWithEmptyChildren_IsNeverIndexedReconciledOrDeleted_RealLua()
    {
        var childShaped = NewIdleTicker();
        childShaped.Status = TickerStatus.Done;
        childShaped.ExecutedAt = BaseNow.AddDays(-10);
        childShaped.ParentId = Guid.NewGuid();
        Assert.Empty(childShaped.Children);

        await _provider.AddTimeTickers([childShaped], CancellationToken.None);
        Assert.Null(await _db.SortedSetScoreAsync(
            RedisKeyBuilder.TimeTickerRetentionSucceededKey, childShaped.Id.ToString()));

        // A stale index must not bypass the authoritative final-delete guard. Put reconciliation
        // on the empty occurrence-key phase so this time row reaches the real delete Lua unchanged.
        await _db.StringSetAsync(RedisKeyBuilder.RetentionReconciliationPhaseKey, "occurrence_keys");
        await _db.StringSetAsync(RedisKeyBuilder.RetentionReconciliationCursorKey, "0");
        await _db.SortedSetAddAsync(RedisKeyBuilder.TimeTickerRetentionSucceededKey,
            childShaped.Id.ToString(), childShaped.ExecutedAt.Value.Ticks);
        var deletion = await _provider.DeleteEligibleTimeTickerChainsAsync(
            new RetentionCutoffs(BaseNow.AddDays(-7), null, null, null), 10,
            RetentionCursor.Start, CancellationToken.None);
        Assert.Equal(0, deletion.Deleted);
        Assert.NotNull(await _provider.GetTimeTickerById(childShaped.Id, CancellationToken.None));

        await _provider.ReconcileRetentionIndexesAsync(10, CancellationToken.None);
        Assert.Null(await _db.SortedSetScoreAsync(
            RedisKeyBuilder.TimeTickerRetentionSucceededKey, childShaped.Id.ToString()));
    }

    [Fact]
    public async Task Retention_TokenFreeExpiredLeaseIsDeletedButLiveLeaseIsExcluded_ForTimeAndCron_RealLua()
    {
        var expiredTime = NewIdleTicker();
        expiredTime.Status = TickerStatus.Done;
        expiredTime.ExecutedAt = BaseNow.AddDays(-10);
        expiredTime.LeaseUntil = BaseNow;
        var liveTime = NewIdleTicker();
        liveTime.Status = TickerStatus.Done;
        liveTime.ExecutedAt = BaseNow.AddDays(-10);
        liveTime.LeaseUntil = BaseNow.AddTicks(1);
        await _provider.AddTimeTickers([expiredTime, liveTime], CancellationToken.None);

        var cron = new CronTickerEntity { Id = Guid.NewGuid(), Function = "LeaseCron", Expression = "* * * * *", Request = [] };
        await _provider.InsertCronTickers([cron], CancellationToken.None);
        CronTickerOccurrenceEntity<CronTickerEntity> Occurrence(DateTime leaseUntil) => new()
        {
            Id = Guid.NewGuid(), CronTickerId = cron.Id, Status = TickerStatus.Failed,
            ExecutionTime = BaseNow.AddDays(-10), ExecutedAt = BaseNow.AddDays(-10),
            LeaseUntil = leaseUntil, CreatedAt = BaseNow.AddDays(-10), UpdatedAt = BaseNow.AddDays(-10)
        };
        var expiredOccurrence = Occurrence(BaseNow);
        var liveOccurrence = Occurrence(BaseNow.AddTicks(1));
        await _provider.InsertCronTickerOccurrences([expiredOccurrence, liveOccurrence], CancellationToken.None);

        Assert.NotNull(await _db.SortedSetScoreAsync(RedisKeyBuilder.TimeTickerRetentionSucceededKey, expiredTime.Id.ToString()));
        Assert.Null(await _db.SortedSetScoreAsync(RedisKeyBuilder.TimeTickerRetentionSucceededKey, liveTime.Id.ToString()));
        Assert.NotNull(await _db.SortedSetScoreAsync(RedisKeyBuilder.CronOccurrenceRetentionFailedKey, expiredOccurrence.Id.ToString()));
        Assert.Null(await _db.SortedSetScoreAsync(RedisKeyBuilder.CronOccurrenceRetentionFailedKey, liveOccurrence.Id.ToString()));

        Assert.Equal(1, (await _provider.DeleteEligibleTimeTickerChainsAsync(
            new RetentionCutoffs(BaseNow.AddDays(-7), null, null, null), 10,
            RetentionCursor.Start, CancellationToken.None)).Deleted);
        Assert.Equal(1, (await _provider.DeleteEligibleCronTickerOccurrencesAsync(
            new RetentionCutoffs(null, BaseNow.AddDays(-7), null, null), 10,
            CancellationToken.None)).Deleted);
        Assert.Null(await _provider.GetTimeTickerById(expiredTime.Id, CancellationToken.None));
        Assert.NotNull(await _provider.GetTimeTickerById(liveTime.Id, CancellationToken.None));
        Assert.NotNull((await _provider.GetAllCronTickerOccurrences(x => x.Id == liveOccurrence.Id, CancellationToken.None)).SingleOrDefault());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1234567)]
    public async Task Retention_StrictTickCutoff_DeletesCutoffMinusOneTickButRetainsEquality_RealLua(int fractionalTicks)
    {
        var cutoff = BaseNow.AddTicks(fractionalTicks);
        var equal = NewIdleTicker();
        equal.Status = TickerStatus.Done;
        equal.ExecutedAt = cutoff;
        var older = NewIdleTicker();
        older.Status = TickerStatus.Done;
        older.ExecutedAt = cutoff.AddTicks(-1);
        await _provider.AddTimeTickers([equal, older], CancellationToken.None);

        var cron = new CronTickerEntity
        {
            Id = Guid.NewGuid(), Function = "TickPrecisionCron", Expression = "* * * * *", Request = []
        };
        await _provider.InsertCronTickers([cron], CancellationToken.None);
        CronTickerOccurrenceEntity<CronTickerEntity> Occurrence(DateTime executedAt) => new()
        {
            Id = Guid.NewGuid(), CronTickerId = cron.Id, Status = TickerStatus.Done,
            ExecutionTime = executedAt, ExecutedAt = executedAt,
            CreatedAt = BaseNow.AddDays(-10), UpdatedAt = BaseNow.AddDays(-10)
        };
        var equalOccurrence = Occurrence(cutoff);
        var olderOccurrence = Occurrence(cutoff.AddTicks(-1));
        await _provider.InsertCronTickerOccurrences([equalOccurrence, olderOccurrence], CancellationToken.None);

        var result = await _provider.DeleteEligibleTimeTickerChainsAsync(
            new RetentionCutoffs(cutoff, null, null, null), 10,
            RetentionCursor.Start, CancellationToken.None);
        var occurrenceResult = await _provider.DeleteEligibleCronTickerOccurrencesAsync(
            new RetentionCutoffs(cutoff, null, null, null), 10, CancellationToken.None);

        Assert.Equal(1, result.Deleted);
        Assert.Equal(1, occurrenceResult.Deleted);
        Assert.NotNull(await _provider.GetTimeTickerById(equal.Id, CancellationToken.None));
        Assert.Null(await _provider.GetTimeTickerById(older.Id, CancellationToken.None));
        var remainingOccurrences = await _provider.GetAllCronTickerOccurrences(
            x => x.CronTickerId == cron.Id, CancellationToken.None);
        Assert.Contains(remainingOccurrences, x => x.Id == equalOccurrence.Id);
        Assert.DoesNotContain(remainingOccurrences, x => x.Id == olderOccurrence.Id);
    }

    [Fact]
    public async Task Retention_ReconciliationHasMorePropagatesUntilMoreThanTwoBatchesAreRecoveredAndDeleted_RealLua()
    {
        var tickers = Enumerable.Range(0, 5).Select(i =>
        {
            var ticker = NewIdleTicker();
            ticker.Status = TickerStatus.Done;
            ticker.ExecutedAt = BaseNow.AddDays(-10).AddTicks(i);
            return ticker;
        }).ToArray();
        await _provider.AddTimeTickers(tickers, CancellationToken.None);
        await _db.KeyDeleteAsync(RedisKeyBuilder.TimeTickerRetentionSucceededKey);

        var calls = 0;
        var deleted = 0;
        RetentionChainBatchResult batch;
        do
        {
            batch = await _provider.DeleteEligibleTimeTickerChainsAsync(
                new RetentionCutoffs(BaseNow.AddDays(-7), null, null, null), 1,
                RetentionCursor.Start, CancellationToken.None);
            calls++;
            deleted += batch.Deleted;
            Assert.True(calls < 30, "reconciliation/deletion must converge");
        } while (batch.HasMore);

        Assert.True(calls > 2);
        Assert.Equal(tickers.Length, deleted);
        foreach (var ticker in tickers)
            Assert.Null(await _provider.GetTimeTickerById(ticker.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Retention_JsonOnlyRowWithoutIdSetOrIndex_IsDiscoveredRepairedAndDeleted_RealLua()
    {
        var ticker = NewIdleTicker();
        ticker.Status = TickerStatus.Done;
        ticker.ExecutedAt = BaseNow.AddDays(-10);
        await _provider.AddTimeTickers([ticker], CancellationToken.None);
        await _db.SetRemoveAsync(RedisKeyBuilder.TimeTickerIdsKey, ticker.Id.ToString());
        await _db.SortedSetRemoveAsync(RedisKeyBuilder.TimeTickerRetentionSucceededKey, ticker.Id.ToString());

        var deleted = 0;
        for (var i = 0; i < 30 && deleted == 0; i++)
        {
            var batch = await _provider.DeleteEligibleTimeTickerChainsAsync(
                new RetentionCutoffs(BaseNow.AddDays(-7), null, null, null), 1,
                RetentionCursor.Start, CancellationToken.None);
            deleted += batch.Deleted;
        }

        Assert.Equal(1, deleted);
        Assert.Null(await _provider.GetTimeTickerById(ticker.Id, CancellationToken.None));
    }

    // -------------------------------------------------------------------------
    // Slice 7: cron document/index atomicity and bounded startup repair.
    // -------------------------------------------------------------------------
    [Fact]
    public async Task MigrateDefinedCronTickers_PreCancelled_DoesNotPublishDocumentOrMembership_RealLua()
    {
        var seed = new DefinedCronTickerSeed("CancelledSeed", "*/5 * * * *", 1, null);
        var seedKey = CronSeedIdentity.SeedKeyForFunction(seed.Function);
        var id = CronSeedIdentity.DeterministicId(seedKey);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _provider.MigrateDefinedCronTickers([seed], cancellation.Token));

        Assert.False(await _db.KeyExistsAsync(RedisKeyBuilder.CronKey(id)));
        Assert.False(await _db.SetContainsAsync(RedisKeyBuilder.CronIdsKey, id.ToString()));
    }

    [Fact]
    public async Task MigrateDefinedCronTickers_StartupRepair_RemovesStaleAndRestoresMissingMemberships_RealLua()
    {
        var missing = new CronTickerEntity
        {
            Id = Guid.NewGuid(), Function = "MissingMembership", Expression = "*/3 * * * *",
            Request = [], CreatedAt = BaseNow, UpdatedAt = BaseNow
        };
        await _db.StringSetAsync(RedisKeyBuilder.CronKey(missing.Id),
            JsonSerializer.Serialize(missing, TestJsonSerializerContext.Default.CronTickerEntity));
        var stale = Guid.NewGuid();
        await _db.SetAddAsync(RedisKeyBuilder.CronIdsKey, stale.ToString());

        await _provider.MigrateDefinedCronTickers(Array.Empty<DefinedCronTickerSeed>(), CancellationToken.None);

        Assert.True(await _db.SetContainsAsync(RedisKeyBuilder.CronIdsKey, missing.Id.ToString()));
        Assert.False(await _db.SetContainsAsync(RedisKeyBuilder.CronIdsKey, stale.ToString()));
        Assert.NotNull(await _provider.GetCronTickerById(missing.Id, CancellationToken.None));
    }

    [Fact]
    public async Task MigrateDefinedCronTickers_CorruptIndexedJson_IsQuarantinedAndSurfaced_NotReplaced_RealLua()
    {
        var id = Guid.NewGuid();
        const string corrupt = "{not-json";
        await _db.StringSetAsync(RedisKeyBuilder.CronKey(id), corrupt);
        await _db.SetAddAsync(RedisKeyBuilder.CronIdsKey, id.ToString());

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            _provider.MigrateDefinedCronTickers(Array.Empty<DefinedCronTickerSeed>(), CancellationToken.None));

        Assert.Equal(corrupt, (string?)await _db.HashGetAsync(
            RedisKeyBuilder.CronRepairQuarantineKey, id.ToString()));
        Assert.Equal(corrupt, (string?)await _db.StringGetAsync(RedisKeyBuilder.CronKey(id)));
        Assert.False(await _db.SetContainsAsync(RedisKeyBuilder.CronIdsKey, id.ToString()));
    }

    [Fact]
    public async Task MigrateDefinedCronTickers_ConcurrentFirstSeed_ConvergesToOneDocumentAndMembership_RealLua()
    {
        var seed = new DefinedCronTickerSeed("ConcurrentSeed", "*/7 * * * *", 3, "sha256:concurrent");
        await Task.WhenAll(Enumerable.Range(0, 16).Select(_ =>
            _provider.MigrateDefinedCronTickers([seed], CancellationToken.None)));

        var expectedId = CronSeedIdentity.DeterministicId(CronSeedIdentity.SeedKeyForFunction(seed.Function));
        var members = await _db.SetMembersAsync(RedisKeyBuilder.CronIdsKey);
        Assert.Single(members, value => value.ToString() == expectedId.ToString());
        var persisted = await _provider.GetCronTickerById(expectedId, CancellationToken.None);
        Assert.NotNull(persisted);
        Assert.Equal(CronExpression.Parse(seed.Expression).Value, persisted!.Expression);
        Assert.Equal(seed.RequestContractVersion, persisted.RequestContractVersion);
        Assert.Equal(seed.RequestContractFingerprint, persisted.RequestContractFingerprint);
    }

    [Fact]
    public async Task MigrateDefinedCronTickers_HistoryBearingPendingOccurrence_IsPreserved_RealLua()
    {
        var original = new DefinedCronTickerSeed("HistoryBearingSeed", "*/7 * * * *", 1, "sha256:v1");
        await _provider.MigrateDefinedCronTickers([original], CancellationToken.None);
        var cronId = CronSeedIdentity.DeterministicId(CronSeedIdentity.SeedKeyForFunction(original.Function));
        var occurrence = new CronTickerOccurrenceEntity<CronTickerEntity>
        {
            Id = Guid.NewGuid(),
            CronTickerId = cronId,
            Status = TickerStatus.Idle,
            ExecutionTime = BaseNow.AddMinutes(1),
            CreatedAt = BaseNow,
            UpdatedAt = BaseNow
        };
        await _provider.InsertCronTickerOccurrences([occurrence], CancellationToken.None);
        await _db.StringSetAsync(RedisKeyBuilder.CronOccurrenceResultKey(occurrence.Id), "history");

        var changed = new DefinedCronTickerSeed(
            original.Function, "*/11 * * * *", original.RequestContractVersion,
            original.RequestContractFingerprint);
        await _provider.MigrateDefinedCronTickers([changed], CancellationToken.None);

        Assert.True(await _db.KeyExistsAsync(RedisKeyBuilder.CronOccurrenceKey(occurrence.Id)));
        Assert.Equal("history", (string?)await _db.StringGetAsync(
            RedisKeyBuilder.CronOccurrenceResultKey(occurrence.Id)));
        Assert.Null(await _db.SortedSetScoreAsync(
            RedisKeyBuilder.CronOccurrencePendingKey, occurrence.Id.ToString()));
    }

    [Fact]
    public async Task MigrateDefinedCronTickers_StartupRepairCompletesBeforeReconciliation_BeyondOneBatch()
    {
        var documents = Enumerable.Range(0, 300).Select(i => new CronTickerEntity
        {
            Id = Guid.NewGuid(), Function = $"dashboard-{i}", Expression = "*/5 * * * *", Request = [],
            CreatedAt = BaseNow, UpdatedAt = BaseNow
        }).ToArray();
        var legacyOwned = documents[299];
        legacyOwned.Function = "legacy-owned-beyond-batch";
        legacyOwned.InitIdentifier = $"MemoryTicker_Seeded_{legacyOwned.Function}";
        await Task.WhenAll(documents.Select(x => _db.StringSetAsync(
            RedisKeyBuilder.CronKey(x.Id), JsonSerializer.Serialize(x, TestJsonSerializerContext.Default.CronTickerEntity))));

        await _provider.MigrateDefinedCronTickers(
            [new DefinedCronTickerSeed(legacyOwned.Function, "*/9 * * * *", 1, null)],
            CancellationToken.None);

        Assert.Equal(300, await _db.SetLengthAsync(RedisKeyBuilder.CronIdsKey));
        var adopted = Assert.Single(await _provider.GetCronTickers(
            x => x.Function == legacyOwned.Function, CancellationToken.None));
        Assert.Equal(legacyOwned.Id, adopted.Id);
        Assert.Equal(CronSeedIdentity.SeedKeyForFunction(legacyOwned.Function), adopted.SeedKey);
        Assert.Equal(CronExpression.Parse("*/9 * * * *").Value, adopted.Expression);
    }

    [Fact]
    public async Task MigrateDefinedCronTickers_CorruptPendingOccurrenceIsQuarantinedAndSurfaced()
    {
        var seed = new DefinedCronTickerSeed("corrupt-occurrence", "*/5 * * * *", 1, null);
        await _provider.MigrateDefinedCronTickers([seed], CancellationToken.None);
        var cronId = CronSeedIdentity.DeterministicId(CronSeedIdentity.SeedKeyForFunction(seed.Function));
        var occurrenceId = Guid.NewGuid();
        const string corrupt = "{not-json";
        await _db.StringSetAsync(RedisKeyBuilder.CronOccurrenceKey(occurrenceId), corrupt);
        await _db.SetAddAsync(RedisKeyBuilder.CronOccurrenceIdsKey, occurrenceId.ToString());
        await _db.SetAddAsync(RedisKeyBuilder.CronOccurrencesByCronKey(cronId), occurrenceId.ToString());
        await _db.SortedSetAddAsync(RedisKeyBuilder.CronOccurrencePendingKey, occurrenceId.ToString(), 0);

        var changed = new DefinedCronTickerSeed(
            seed.Function, "*/10 * * * *", seed.RequestContractVersion, seed.RequestContractFingerprint);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            _provider.MigrateDefinedCronTickers([changed], CancellationToken.None));

        Assert.Equal(corrupt, (string?)await _db.StringGetAsync(RedisKeyBuilder.CronOccurrenceKey(occurrenceId)));
        Assert.Equal(corrupt, (string?)await _db.HashGetAsync(
            RedisKeyBuilder.CronOccurrenceRepairQuarantineKey, occurrenceId.ToString()));
        Assert.False(await _db.SetContainsAsync(RedisKeyBuilder.CronOccurrenceIdsKey, occurrenceId.ToString()));
        Assert.False(await _db.SetContainsAsync(RedisKeyBuilder.CronOccurrencesByCronKey(cronId), occurrenceId.ToString()));
    }

    [Fact]
    public async Task RecurringSlot_ConcurrentWriters_ReturnSameCommittedOwner_AndPublishCompleteIndexes_RealLua()
    {
        var cron = new CronTickerEntity
        {
            Id = Guid.NewGuid(), Function = "same-slot", Expression = "*/5 * * * *",
            DefinitionRevision = 1, Request = [], CreatedAt = BaseNow, UpdatedAt = BaseNow
        };
        await _provider.InsertCronTickers([cron], CancellationToken.None);
        var executionTime = BaseNow.AddMinutes(5);

        static async Task<CronTickerOccurrenceEntity<CronTickerEntity>[]> CollectAsync(
            IAsyncEnumerable<CronTickerOccurrenceEntity<CronTickerEntity>> source)
        {
            var rows = new List<CronTickerOccurrenceEntity<CronTickerEntity>>();
            await foreach (var row in source) rows.Add(row);
            return rows.ToArray();
        }

        var publications = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ =>
            CollectAsync(_provider.QueueCronTickerOccurrences(
                (executionTime, [new InternalManagerContext(cron.Id)]), CancellationToken.None))));

        var returned = publications.SelectMany(x => x).ToArray();
        Assert.Equal(16, returned.Length);
        var owner = Assert.Single(returned.Select(x => x.Id).Distinct());
        Assert.True(await _db.KeyExistsAsync(RedisKeyBuilder.CronOccurrenceKey(owner)));
        Assert.True(await _db.SetContainsAsync(RedisKeyBuilder.CronOccurrenceIdsKey, owner.ToString()));
        Assert.True(await _db.SetContainsAsync(RedisKeyBuilder.CronOccurrencesByCronKey(cron.Id), owner.ToString()));
        Assert.NotNull(await _db.SortedSetScoreAsync(RedisKeyBuilder.CronOccurrencePendingKey, owner.ToString()));
    }

    [Fact]
    public async Task CronRevision_ImmediateAndQueuedTransitionRejectOldRevision_RealLua()
    {
        var cron = new CronTickerEntity
        {
            Id = Guid.NewGuid(), Function = "revision-fence", Expression = "*/5 * * * *",
            DefinitionRevision = 1, Request = [], CreatedAt = BaseNow, UpdatedAt = BaseNow
        };
        await _provider.InsertCronTickers([cron], CancellationToken.None);
        var immediate = new CronTickerOccurrenceEntity<CronTickerEntity>
        {
            Id = Guid.NewGuid(), CronTickerId = cron.Id, DefinitionRevision = 1,
            Status = TickerStatus.Idle, ExecutionTime = BaseNow.AddMinutes(1),
            CreatedAt = BaseNow, UpdatedAt = BaseNow
        };
        await _provider.InsertCronTickerOccurrences([immediate], CancellationToken.None);

        var queued = Assert.Single(await CollectCronAsync(_provider.QueueCronTickerOccurrences(
            (BaseNow.AddMinutes(2), [new InternalManagerContext(cron.Id)]), CancellationToken.None)));
        cron.DefinitionRevision = 2;
        await _provider.UpdateCronTickers([cron], CancellationToken.None);

        Assert.Empty(await _provider.AcquireImmediateCronOccurrencesAsync([immediate.Id], CancellationToken.None));
        Assert.Empty(await _provider.TransitionQueuedCronOccurrencesToInProgressAsync(
            [new AcquisitionLease(queued.Id, queued.AcquisitionToken)], CancellationToken.None));
        Assert.Equal(TickerStatus.Queued,
            Assert.Single(await _provider.GetAllCronTickerOccurrences(x => x.Id == queued.Id)).Status);
    }

    [Fact]
    public async Task CronRevision_WrongTypeDerivedSlotFailsBeforeAnyDefinitionOrOccurrenceMutation()
    {
        var cron = NewCron("slot-preflight");
        await _provider.InsertCronTickers([cron], CancellationToken.None);
        var occurrence = NewOccurrence(cron.Id, BaseNow.AddMinutes(7), TickerStatus.Idle);
        await _provider.InsertCronTickerOccurrences([occurrence], CancellationToken.None);
        var definitionKey = RedisKeyBuilder.CronKey(cron.Id);
        var occurrenceKey = RedisKeyBuilder.CronOccurrenceKey(occurrence.Id);
        var slotKey = RedisKeyBuilder.CronOccurrenceSlotKey(cron.Id, occurrence.ExecutionTime);
        var definitionBefore = await _db.StringGetAsync(definitionKey);
        var occurrenceBefore = await _db.StringGetAsync(occurrenceKey);
        var missingId = Guid.Empty.ToString();
        await _db.SetAddAsync(RedisKeyBuilder.CronOccurrenceIdsKey, missingId);
        await _db.SetAddAsync(RedisKeyBuilder.CronOccurrencesByCronKey(cron.Id), missingId);
        await _db.SortedSetAddAsync(RedisKeyBuilder.CronOccurrencePendingKey, missingId, 1);
        await _db.KeyDeleteAsync(slotKey);
        await _db.HashSetAsync(slotKey, "wrong", "type");

        cron.DefinitionRevision++;
        cron.Expression = "*/13 * * * *";
        await Assert.ThrowsAsync<RedisServerException>(() =>
            _provider.UpdateCronTickers([cron], CancellationToken.None));

        Assert.Equal(definitionBefore, await _db.StringGetAsync(definitionKey));
        Assert.Equal(occurrenceBefore, await _db.StringGetAsync(occurrenceKey));
        Assert.True(await _db.SetContainsAsync(RedisKeyBuilder.CronIdsKey, cron.Id.ToString()));
        Assert.True(await _db.SetContainsAsync(
            RedisKeyBuilder.CronOccurrencesByCronKey(cron.Id), occurrence.Id.ToString()));
        Assert.True(await _db.SetContainsAsync(RedisKeyBuilder.CronOccurrenceIdsKey, missingId));
        Assert.True(await _db.SetContainsAsync(
            RedisKeyBuilder.CronOccurrencesByCronKey(cron.Id), missingId));
        Assert.NotNull(await _db.SortedSetScoreAsync(
            RedisKeyBuilder.CronOccurrencePendingKey, missingId));
        Assert.Equal("hash", (string?)await _db.ExecuteAsync("TYPE", slotKey));
    }

    [Fact]
    public async Task CorruptCronDefinitionIsNotQuarantinedBeforeReplacementJsonPreflightCompletes()
    {
        var cron = NewCron("corrupt-definition-preflight");
        await _provider.InsertCronTickers([cron], CancellationToken.None);
        var definitionKey = RedisKeyBuilder.CronKey(cron.Id);
        const string corruptDefinition = "{not-json";
        const string priorQuarantine = "prior-quarantine";
        await _db.StringSetAsync(definitionKey, corruptDefinition);
        await _db.HashSetAsync(RedisKeyBuilder.CronRepairQuarantineKey, cron.Id.ToString(), priorQuarantine);

        await Assert.ThrowsAsync<RedisServerException>(() =>
            _db.ScriptEvaluateAsync(LuaScriptLoader.Load("MutateCronDefinition"),
                [(RedisKey)definitionKey, RedisKeyBuilder.CronIdsKey, RedisKeyBuilder.CronRepairQuarantineKey,
                 RedisKeyBuilder.CronOccurrencesByCronKey(cron.Id), RedisKeyBuilder.CronOccurrenceIdsKey,
                 RedisKeyBuilder.CronOccurrencePendingKey, RedisKeyBuilder.CronOccurrenceRetentionSucceededKey,
                 RedisKeyBuilder.CronOccurrenceRetentionFailedKey, RedisKeyBuilder.CronOccurrenceRetentionCancelledKey,
                 RedisKeyBuilder.CronOccurrenceRetentionSkippedKey, RedisKeyBuilder.CronOccurrenceRepairQuarantineKey,
                 RedisKeyBuilder.ReconciliationActivationMetadataKey],
                [(RedisValue)cron.Id.ToString(), "upsert", "{bad-replacement", "0", BaseNow.ToString("O"),
                 (int)TickerStatus.Idle, (int)TickerStatus.Queued, (int)TickerStatus.Skipped, "unused", 0,
                 "unused-occurrence-prefix", "unused-slot-prefix", "1", "0", "0", "", "", -1, "legacy"]));

        Assert.Equal(corruptDefinition, (string?)await _db.StringGetAsync(definitionKey));
        Assert.True(await _db.SetContainsAsync(RedisKeyBuilder.CronIdsKey, cron.Id.ToString()));
        Assert.Equal(priorQuarantine, (string?)await _db.HashGetAsync(
            RedisKeyBuilder.CronRepairQuarantineKey, cron.Id.ToString()));
    }

    [Fact]
    public async Task CronRevision_ExpiredStaleOccurrenceIsQuarantinedNotRestarted_RealLua()
    {
        var cron = new CronTickerEntity
        {
            Id = Guid.NewGuid(), Function = "stale-revision", Expression = "*/5 * * * *",
            DefinitionRevision = 2, OnStale = StaleAction.Restart, Request = [],
            CreatedAt = BaseNow, UpdatedAt = BaseNow
        };
        await _provider.InsertCronTickers([cron], CancellationToken.None);
        var occurrence = new CronTickerOccurrenceEntity<CronTickerEntity>
        {
            Id = Guid.NewGuid(), CronTickerId = cron.Id, DefinitionRevision = 1,
            Status = TickerStatus.InProgress, LockHolder = "dead", AcquisitionToken = Guid.NewGuid(),
            LockedAt = BaseNow.AddMinutes(-10), LeaseUntil = BaseNow.AddMinutes(-1),
            ExecutionTime = BaseNow.AddMinutes(-20), CreatedAt = BaseNow.AddHours(-1), UpdatedAt = BaseNow.AddMinutes(-10)
        };
        await SeedLegacyOccurrenceAsync(occurrence);

        var result = await _provider.RecoverStaleTickers(3, CancellationToken.None);

        Assert.Equal(0, result.RestartedCronOccurrences);
        var stored = Assert.Single(await _provider.GetAllCronTickerOccurrences(x => x.Id == occurrence.Id));
        Assert.Equal(TickerStatus.Skipped, stored.Status);
        Assert.Contains("revision", stored.SkippedReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CronRevision_AcquiredOccurrenceCanCommitItsStampedRevisionAfterDefinitionAdvances()
    {
        var cron = NewCron("terminal-after-definition-advance");
        await _provider.InsertCronTickers([cron], CancellationToken.None);
        var occurrence = NewOccurrence(cron.Id, BaseNow.AddMinutes(-1), TickerStatus.Idle);
        await _provider.InsertCronTickerOccurrences([occurrence], CancellationToken.None);
        var acquired = Assert.Single(await _provider.AcquireImmediateCronOccurrencesAsync([occurrence.Id]));

        cron.DefinitionRevision = 2;
        cron.Expression = "*/11 * * * *";
        await _provider.UpdateCronTickers([cron], CancellationToken.None);

        var terminal = new InternalFunctionContext
        {
            TickerId = occurrence.Id,
            Type = TickerType.CronTickerOccurrence,
            AcquisitionToken = acquired.AcquisitionToken
        }.SetProperty(x => x.Status, TickerStatus.Done)
         .SetProperty(x => x.ResultEnvelope, (TickerResultEnvelope)null);

        Assert.True(await _provider.CommitTerminalTickerAsync(terminal));
        var stored = Assert.Single(await _provider.GetAllCronTickerOccurrences(x => x.Id == occurrence.Id));
        Assert.Equal(TickerStatus.Done, stored.Status);
        Assert.Equal(1, stored.DefinitionRevision);
    }

    [Fact]
    public async Task OrdinaryTerminal_LostReplyUsesTypedGenerationEvidence_AndTypesCannotCollide()
    {
        var sharedId = Guid.NewGuid();
        var time = new TimeTickerEntity
        {
            Id = sharedId, Function = "typed-time", ExecutionTime = BaseNow.AddMinutes(-1),
            Status = TickerStatus.Idle,
            CreatedAt = BaseNow.AddHours(-1), UpdatedAt = BaseNow.AddHours(-1), Request = []
        };
        await _provider.AddTimeTickers([time]);
        var acquiredTime = Assert.Single(await _provider.AcquireImmediateTimeTickersAsync([sharedId]));
        var cron = NewCron("typed-cron");
        await _provider.InsertCronTickers([cron], CancellationToken.None);
        var occurrence = NewOccurrence(cron.Id, BaseNow.AddMinutes(-1), TickerStatus.Idle);
        occurrence.Id = sharedId;
        await _provider.InsertCronTickerOccurrences([occurrence], CancellationToken.None);
        var acquiredOccurrence = Assert.Single(await _provider.AcquireImmediateCronOccurrencesAsync([sharedId]));

        var injected = 0;
        _provider.AfterTerminalMutationScriptEvaluatedAsync = () =>
        {
            if (Interlocked.Increment(ref injected) == 1)
                throw new IOException("injected lost Redis reply");
            return Task.CompletedTask;
        };
        var timeTerminal = new InternalFunctionContext
        {
            TickerId = sharedId, Type = TickerType.TimeTicker,
            AcquisitionToken = acquiredTime.AcquisitionToken
        }.SetProperty(x => x.Status, TickerStatus.Failed);
        Assert.True(await _provider.CommitTerminalTickerAsync(timeTerminal));

        var cronTerminal = new InternalFunctionContext
        {
            TickerId = sharedId, Type = TickerType.CronTickerOccurrence,
            AcquisitionToken = acquiredOccurrence.AcquisitionToken
        }.SetProperty(x => x.Status, TickerStatus.Failed);
        Assert.True(await _provider.CommitTerminalTickerAsync(cronTerminal));
        Assert.Equal(TickerStatus.Failed, (await _provider.GetTimeTickerById(sharedId))!.Status);
        Assert.Equal(TickerStatus.Failed,
            Assert.Single(await _provider.GetAllCronTickerOccurrences(x => x.Id == sharedId)).Status);
        var evidenceFields = (await _db.HashGetAllAsync(RedisKeyBuilder.TerminalMutationEvidenceKey))
            .Select(x => x.Name.ToString()).ToArray();
        Assert.Contains($"{(int)TickerType.TimeTicker}:{sharedId:D}", evidenceFields);
        Assert.Contains($"{(int)TickerType.CronTickerOccurrence}:{sharedId:D}", evidenceFields);
    }

    [Fact]
    public async Task Public_idle_republication_clears_terminal_evidence_and_stale_generation_cannot_replay()
    {
        var ticker = NewIdleTicker();
        await _provider.AddTimeTickers([ticker]);
        var runA = Assert.Single(await _provider.AcquireImmediateTimeTickersAsync([ticker.Id]));
        var terminalA = new InternalFunctionContext
        {
            TickerId = ticker.Id, Type = TickerType.TimeTicker,
            AcquisitionToken = runA.AcquisitionToken
        }.SetProperty(x => x.Status, TickerStatus.Failed);
        Assert.True(await _provider.CommitTerminalTickerAsync(terminalA));
        var evidenceField = $"{(int)TickerType.TimeTicker}:{ticker.Id:D}";
        Assert.True(await _db.HashExistsAsync(
            RedisKeyBuilder.TerminalMutationEvidenceKey, evidenceField));

        var persisted = (await _provider.GetTimeTickerById(ticker.Id))!;
        persisted.Status = TickerStatus.Idle;
        persisted.LockHolder = null;
        persisted.LockedAt = null;
        persisted.AcquisitionToken = null;
        persisted.ExecutionTime = BaseNow.AddMinutes(-1);
        Assert.Equal(1, await _provider.UpdateTimeTickers([persisted]));

        Assert.False(await _db.HashExistsAsync(
            RedisKeyBuilder.TerminalMutationEvidenceKey, evidenceField));
        await _db.HashSetAsync(RedisKeyBuilder.TerminalMutationEvidenceKey,
            evidenceField, $"{runA.AcquisitionToken:D}:stale-evidence");
        var runB = Assert.Single(await _provider.AcquireImmediateTimeTickersAsync([ticker.Id]));
        Assert.NotEqual(runA.AcquisitionToken, runB.AcquisitionToken);
        Assert.False(await _db.HashExistsAsync(
            RedisKeyBuilder.TerminalMutationEvidenceKey, evidenceField));
        Assert.False(await _provider.CommitTerminalTickerAsync(terminalA));
    }

    [Fact]
    public async Task CronTerminal_CleanupErrorCannotTurnCommittedMutationIntoReportedFailure()
    {
        var cron = NewCron("truthful-cleanup");
        await _provider.InsertCronTickers([cron], CancellationToken.None);
        var occurrence = NewOccurrence(cron.Id, BaseNow.AddMinutes(42), TickerStatus.Idle);
        await _provider.InsertCronTickerOccurrences([occurrence], CancellationToken.None);
        await _db.StringSetAsync(RedisKeyBuilder.CronOccurrenceSlotKey(cron.Id, occurrence.ExecutionTime),
            occurrence.Id.ToString());
        var acquired = Assert.Single(await _provider.AcquireImmediateCronOccurrencesAsync([occurrence.Id]));
        _provider.AfterCronTerminalCleanupAsync = () => throw new IOException("injected cleanup failure");

        var terminal = new InternalFunctionContext
        {
            TickerId = occurrence.Id, Type = TickerType.CronTickerOccurrence,
            AcquisitionToken = acquired.AcquisitionToken
        }.SetProperty(x => x.Status, TickerStatus.Failed);

        Assert.True(await _provider.CommitTerminalTickerAsync(terminal));
        Assert.Equal(TickerStatus.Failed,
            Assert.Single(await _provider.GetAllCronTickerOccurrences(x => x.Id == occurrence.Id)).Status);
        Assert.False(await _db.KeyExistsAsync(
            RedisKeyBuilder.CronOccurrenceSlotKey(cron.Id, occurrence.ExecutionTime)));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("corrupt")]
    [InlineData("wrong-parent")]
    [InlineData("done")]
    [InlineData("cancelled")]
    [InlineData("skipped")]
    public async Task RecurringSlot_InvalidOwner_IsRepairedWithoutDestroyingEvidence_RealLua(string ownerKind)
    {
        var cron = NewCron("repair-" + ownerKind);
        await _provider.InsertCronTickers([cron], CancellationToken.None);
        var executionTime = BaseNow.AddMinutes(20);
        var staleId = Guid.NewGuid();
        var slotKey = RedisKeyBuilder.CronOccurrenceSlotKey(cron.Id, executionTime);
        await _db.StringSetAsync(slotKey, staleId.ToString());

        const string corrupt = "{corrupt-owner";
        if (ownerKind == "corrupt")
            await _db.StringSetAsync(RedisKeyBuilder.CronOccurrenceKey(staleId), corrupt);
        else if (ownerKind != "missing")
        {
            var stale = NewOccurrence(ownerKind == "wrong-parent" ? Guid.NewGuid() : cron.Id, executionTime,
                ownerKind switch
                {
                    "done" => TickerStatus.Done,
                    "cancelled" => TickerStatus.Cancelled,
                    "skipped" => TickerStatus.Skipped,
                    _ => TickerStatus.Queued
                });
            stale.Id = staleId;
            await _db.StringSetAsync(RedisKeyBuilder.CronOccurrenceKey(staleId),
                JsonSerializer.Serialize(stale, TestJsonSerializerContext.Default.CronTickerOccurrenceEntityCronTickerEntity));
        }

        var published = Assert.Single(await CollectCronAsync(_provider.QueueCronTickerOccurrences(
            (executionTime, [new InternalManagerContext(cron.Id)]), CancellationToken.None)));

        Assert.NotEqual(staleId, published.Id);
        Assert.Equal(published.Id.ToString(), (string?)await _db.StringGetAsync(slotKey));
        await AssertCompleteOccurrencePublicationAsync(published.Id, cron.Id);
        if (ownerKind == "corrupt")
            Assert.Equal(corrupt, (string?)await _db.StringGetAsync(RedisKeyBuilder.CronOccurrenceKey(staleId)));
        else if (ownerKind is not "missing")
            Assert.True(await _db.KeyExistsAsync(RedisKeyBuilder.CronOccurrenceKey(staleId)));
    }

    [Fact]
    public async Task RecurringSlot_ExistingValidOwner_RepairsPartialIndexes_RealLua()
    {
        var cron = NewCron("partial-index");
        await _provider.InsertCronTickers([cron], CancellationToken.None);
        var executionTime = BaseNow.AddMinutes(21);
        var owner = Assert.Single(await CollectCronAsync(_provider.QueueCronTickerOccurrences(
            (executionTime, [new InternalManagerContext(cron.Id)]), CancellationToken.None)));
        await _db.SetRemoveAsync(RedisKeyBuilder.CronOccurrenceIdsKey, owner.Id.ToString());
        await _db.SetRemoveAsync(RedisKeyBuilder.CronOccurrencesByCronKey(cron.Id), owner.Id.ToString());
        await _db.SortedSetRemoveAsync(RedisKeyBuilder.CronOccurrencePendingKey, owner.Id.ToString());

        var repaired = Assert.Single(await CollectCronAsync(_provider.QueueCronTickerOccurrences(
            (executionTime, [new InternalManagerContext(cron.Id)]), CancellationToken.None)));

        Assert.Equal(owner.Id, repaired.Id);
        await AssertCompleteOccurrencePublicationAsync(owner.Id, cron.Id);
    }

    [Theory]
    [InlineData(TickerStatus.Done)]
    [InlineData(TickerStatus.Cancelled)]
    [InlineData(TickerStatus.Skipped)]
    public async Task RecurringSlot_TerminalAcknowledgement_ReleasesOnlyItsReservation_AndAllowsRetry(
        TickerStatus terminalStatus)
    {
        var cron = NewCron("terminal-" + terminalStatus);
        await _provider.InsertCronTickers([cron], CancellationToken.None);
        var executionTime = BaseNow.AddMinutes(22);
        var owner = Assert.Single(await CollectCronAsync(_provider.QueueCronTickerOccurrences(
            (executionTime, [new InternalManagerContext(cron.Id)]), CancellationToken.None)));
        var slotKey = RedisKeyBuilder.CronOccurrenceSlotKey(cron.Id, executionTime);

        var terminal = new InternalFunctionContext
        {
            TickerId = owner.Id, Type = TickerType.CronTickerOccurrence,
            AcquisitionToken = owner.AcquisitionToken
        }.SetProperty(x => x.Status, terminalStatus);
        await _provider.UpdateCronTickerOccurrence(terminal, CancellationToken.None);

        Assert.False(await _db.KeyExistsAsync(slotKey));
        var retry = Assert.Single(await CollectCronAsync(_provider.QueueCronTickerOccurrences(
            (executionTime, [new InternalManagerContext(cron.Id)]), CancellationToken.None)));
        Assert.NotEqual(owner.Id, retry.Id);
        Assert.Equal(retry.Id.ToString(), (string?)await _db.StringGetAsync(slotKey));
    }

    [Fact]
    public async Task ExplicitOccurrenceDelete_RemovesExactDocumentResultAndIndexes_ButPreservesNewerAbaReservation()
    {
        var cron = NewCron("explicit-delete");
        await _provider.InsertCronTickers([cron], CancellationToken.None);
        var executionTime = BaseNow.AddMinutes(23);
        var owner = Assert.Single(await CollectCronAsync(_provider.QueueCronTickerOccurrences(
            (executionTime, [new InternalManagerContext(cron.Id)]), CancellationToken.None)));
        var slotKey = RedisKeyBuilder.CronOccurrenceSlotKey(cron.Id, executionTime);
        await _provider.UpdateCronTickerOccurrence(new InternalFunctionContext
        {
            TickerId = owner.Id, Type = TickerType.CronTickerOccurrence,
            AcquisitionToken = owner.AcquisitionToken
        }.SetProperty(x => x.Status, TickerStatus.Done), CancellationToken.None);
        await _db.StringSetAsync(RedisKeyBuilder.CronOccurrenceResultKey(owner.Id), "result-evidence");
        var newer = Guid.NewGuid();
        await _db.StringSetAsync(slotKey, newer.ToString());

        Assert.Equal(1, await _provider.RemoveCronTickerOccurrences([owner.Id], CancellationToken.None));

        Assert.False(await _db.KeyExistsAsync(RedisKeyBuilder.CronOccurrenceKey(owner.Id)));
        Assert.False(await _db.KeyExistsAsync(RedisKeyBuilder.CronOccurrenceResultKey(owner.Id)));
        Assert.False(await _db.SetContainsAsync(RedisKeyBuilder.CronOccurrenceIdsKey, owner.Id.ToString()));
        Assert.False(await _db.SetContainsAsync(RedisKeyBuilder.CronOccurrencesByCronKey(cron.Id), owner.Id.ToString()));
        Assert.Null(await _db.SortedSetScoreAsync(RedisKeyBuilder.CronOccurrencePendingKey, owner.Id.ToString()));
        Assert.Equal(newer.ToString(), (string?)await _db.StringGetAsync(slotKey));
    }

    [Fact]
    public async Task ExplicitOccurrenceDelete_RefusesGenerationAcquiredAfterPreRead()
    {
        var cron = NewCron("explicit-delete-race");
        await _provider.InsertCronTickers([cron], CancellationToken.None);
        var occurrence = new CronTickerOccurrenceEntity<CronTickerEntity>
        {
            Id = Guid.NewGuid(), CronTickerId = cron.Id, DefinitionRevision = cron.DefinitionRevision,
            Status = TickerStatus.Idle, ExecutionTime = BaseNow.AddMinutes(23),
            CreatedAt = BaseNow, UpdatedAt = BaseNow
        };
        Assert.Equal(1, await _provider.InsertCronTickerOccurrences([occurrence], CancellationToken.None));
        var resultKey = RedisKeyBuilder.CronOccurrenceResultKey(occurrence.Id);
        await _db.StringSetAsync(resultKey, "result-evidence");
        CronTickerOccurrenceEntity<CronTickerEntity>? acquired = null;
        _provider.BeforeCronOccurrenceDeleteForTestAsync = async (_, ct) =>
        {
            acquired = Assert.Single(await _provider.AcquireImmediateCronOccurrencesAsync([occurrence.Id], ct));
        };

        Assert.Equal(0, await _provider.RemoveCronTickerOccurrences([occurrence.Id], CancellationToken.None));

        Assert.NotNull(acquired);
        Assert.True(await _db.KeyExistsAsync(RedisKeyBuilder.CronOccurrenceKey(occurrence.Id)));
        Assert.True(await _db.SetContainsAsync(RedisKeyBuilder.CronOccurrenceIdsKey, occurrence.Id.ToString()));
        Assert.True(await _db.SetContainsAsync(RedisKeyBuilder.CronOccurrencesByCronKey(cron.Id), occurrence.Id.ToString()));
    }

    [Fact]
    public async Task BulkAndParentDeletes_ReleaseEveryOwnedSlot_RealLua()
    {
        var cron = NewCron("bulk-parent-delete");
        await _provider.InsertCronTickers([cron], CancellationToken.None);
        var firstTime = BaseNow.AddMinutes(24);
        var secondTime = BaseNow.AddMinutes(25);
        var parentTime = BaseNow.AddMinutes(26);
        var first = Assert.Single(await CollectCronAsync(_provider.QueueCronTickerOccurrences(
            (firstTime, [new InternalManagerContext(cron.Id)]), CancellationToken.None)));
        var second = Assert.Single(await CollectCronAsync(_provider.QueueCronTickerOccurrences(
            (secondTime, [new InternalManagerContext(cron.Id)]), CancellationToken.None)));
        var parentOwned = Assert.Single(await CollectCronAsync(_provider.QueueCronTickerOccurrences(
            (parentTime, [new InternalManagerContext(cron.Id)]), CancellationToken.None)));

        foreach (var occurrence in new[] { first, second, parentOwned })
            await _provider.UpdateCronTickerOccurrence(new InternalFunctionContext
            {
                TickerId = occurrence.Id, Type = TickerType.CronTickerOccurrence,
                AcquisitionToken = occurrence.AcquisitionToken
            }.SetProperty(x => x.Status, TickerStatus.Done), CancellationToken.None);

        Assert.Equal(2, await _provider.RemoveCronTickerOccurrences([first.Id, second.Id], CancellationToken.None));
        Assert.False(await _db.KeyExistsAsync(RedisKeyBuilder.CronOccurrenceSlotKey(cron.Id, firstTime)));
        Assert.False(await _db.KeyExistsAsync(RedisKeyBuilder.CronOccurrenceSlotKey(cron.Id, secondTime)));
        Assert.Equal(1, await _provider.RemoveCronTickers([cron.Id], CancellationToken.None));
        Assert.False(await _db.KeyExistsAsync(RedisKeyBuilder.CronOccurrenceSlotKey(cron.Id, parentTime)));
        Assert.False(await _db.KeyExistsAsync(RedisKeyBuilder.CronOccurrenceKey(parentOwned.Id)));
    }

    [Fact]
    public async Task RetentionDelete_ReleasesSlotAndAllowsNewReservation_RealLua()
    {
        var cron = NewCron("retention-slot");
        await _provider.InsertCronTickers([cron], CancellationToken.None);
        var executionTime = BaseNow.AddDays(-10);
        var occurrence = NewOccurrence(cron.Id, executionTime, TickerStatus.Failed);
        occurrence.ExecutedAt = executionTime;
        await _provider.InsertCronTickerOccurrences([occurrence], CancellationToken.None);
        var slotKey = RedisKeyBuilder.CronOccurrenceSlotKey(cron.Id, executionTime);
        await _db.StringSetAsync(slotKey, occurrence.Id.ToString());

        Assert.Equal(1, (await _provider.DeleteEligibleCronTickerOccurrencesAsync(
            new RetentionCutoffs(null, BaseNow.AddDays(-7), null, null), 10, CancellationToken.None)).Deleted);
        Assert.False(await _db.KeyExistsAsync(slotKey));

        var retry = Assert.Single(await CollectCronAsync(_provider.QueueCronTickerOccurrences(
            (executionTime, [new InternalManagerContext(cron.Id)]), CancellationToken.None)));
        Assert.Equal(retry.Id.ToString(), (string?)await _db.StringGetAsync(slotKey));
    }

    [Fact]
    public async Task CronDefinitionPublication_CancellationAfterLua_LeavesCompletePublication()
    {
        var cron = NewCron("cancel-after-boundary");
        using var cancellation = new CancellationTokenSource();
        _provider.AfterCronDefinitionMutationScriptEvaluatedAsync = () =>
        {
            cancellation.Cancel();
            return Task.CompletedTask;
        };

        Assert.Equal(1, await _provider.InsertCronTickers([cron], cancellation.Token));

        Assert.True(cancellation.IsCancellationRequested);
        Assert.True(await _db.KeyExistsAsync(RedisKeyBuilder.CronKey(cron.Id)));
        Assert.True(await _db.SetContainsAsync(RedisKeyBuilder.CronIdsKey, cron.Id.ToString()));
        Assert.NotNull(await _provider.GetCronTickerById(cron.Id, CancellationToken.None));
    }

    [Fact]
    public async Task SemanticDefinitionPublication_RacesOccurrenceCreation_AndNeverPublishesStaleRevision()
    {
        var cron = NewCron("semantic-race");
        await _provider.InsertCronTickers([cron], CancellationToken.None);
        cron.DefinitionRevision = 2;
        cron.Expression = "*/11 * * * *";
        var mutationReachedBoundary = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowMutationReturn = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _provider.AfterCronDefinitionMutationScriptEvaluatedAsync = async () =>
        {
            mutationReachedBoundary.TrySetResult();
            await allowMutationReturn.Task;
        };

        var mutation = _provider.UpdateCronTickers([cron], CancellationToken.None);
        await mutationReachedBoundary.Task;
        var occurrenceTask = CollectCronAsync(_provider.QueueCronTickerOccurrences(
            (BaseNow.AddMinutes(30), [new InternalManagerContext(cron.Id)]), CancellationToken.None));
        allowMutationReturn.TrySetResult();
        await mutation;
        var occurrence = Assert.Single(await occurrenceTask);

        Assert.Equal(2, occurrence.DefinitionRevision);
        var acquired = await _provider.TransitionQueuedCronOccurrencesToInProgressAsync(
            [new AcquisitionLease(occurrence.Id, occurrence.AcquisitionToken)], CancellationToken.None);
        Assert.Equal([occurrence.Id], acquired);
    }

    [Fact]
    public async Task QuarantineAndDeadNodeStaleRevision_PreserveResultAndReleaseSlot_RealLua()
    {
        var original = new DefinedCronTickerSeed("quarantine-results", "*/5 * * * *", 1, "sha256:v1");
        await _provider.MigrateDefinedCronTickers([original], CancellationToken.None);
        var cronId = CronSeedIdentity.DeterministicId(CronSeedIdentity.SeedKeyForFunction(original.Function));
        var pendingTime = BaseNow.AddMinutes(31);
        var pending = NewOccurrence(cronId, pendingTime, TickerStatus.Idle);
        await _provider.InsertCronTickerOccurrences([pending], CancellationToken.None);
        await _db.StringSetAsync(RedisKeyBuilder.CronOccurrenceSlotKey(cronId, pendingTime), pending.Id.ToString());
        await _db.StringSetAsync(RedisKeyBuilder.CronOccurrenceResultKey(pending.Id), "pending-result");

        var changed = new DefinedCronTickerSeed(original.Function, "*/11 * * * *", 1, "sha256:v1");
        await _provider.MigrateDefinedCronTickers([changed], CancellationToken.None);

        var pendingStored = Assert.Single(await _provider.GetAllCronTickerOccurrences(x => x.Id == pending.Id));
        Assert.Equal(TickerStatus.Skipped, pendingStored.Status);
        Assert.Equal("pending-result", (string?)await _db.StringGetAsync(RedisKeyBuilder.CronOccurrenceResultKey(pending.Id)));
        Assert.False(await _db.KeyExistsAsync(RedisKeyBuilder.CronOccurrenceSlotKey(cronId, pendingTime)));

        var deadTime = BaseNow.AddMinutes(32);
        var dead = NewOccurrence(cronId, deadTime, TickerStatus.InProgress);
        dead.DefinitionRevision = 1;
        dead.LockHolder = "dead-node";
        dead.AcquisitionToken = Guid.NewGuid();
        await SeedLegacyOccurrenceAsync(dead);
        await _db.StringSetAsync(RedisKeyBuilder.CronOccurrenceResultKey(dead.Id), "dead-result");

        await _provider.ReleaseDeadNodeOccurrenceResources("dead-node", CancellationToken.None);

        var deadStored = Assert.Single(await _provider.GetAllCronTickerOccurrences(x => x.Id == dead.Id));
        Assert.Equal(TickerStatus.Skipped, deadStored.Status);
        Assert.Equal("dead-result", (string?)await _db.StringGetAsync(RedisKeyBuilder.CronOccurrenceResultKey(dead.Id)));
        Assert.False(await _db.KeyExistsAsync(RedisKeyBuilder.CronOccurrenceSlotKey(cronId, deadTime)));
    }

    [Fact]
    public async Task RawOccurrenceInsert_CannotOverwriteTerminalDocumentOrEraseResultEvidence()
    {
        var cron = NewCron("raw-terminal-evidence");
        await _provider.InsertCronTickers([cron], CancellationToken.None);
        var occurrence = NewOccurrence(cron.Id, BaseNow.AddMinutes(40), TickerStatus.Done);
        occurrence.ExecutedAt = BaseNow;
        await _db.StringSetAsync(RedisKeyBuilder.CronOccurrenceKey(occurrence.Id),
            JsonSerializer.Serialize(occurrence, TestJsonSerializerContext.Default.CronTickerOccurrenceEntityCronTickerEntity));
        await _db.StringSetAsync(RedisKeyBuilder.CronOccurrenceResultKey(occurrence.Id), "terminal-evidence");
        var before = await _db.StringGetAsync(RedisKeyBuilder.CronOccurrenceKey(occurrence.Id));

        occurrence.Status = TickerStatus.Idle;
        occurrence.ExecutedAt = null;
        Assert.Equal(0, await _provider.InsertCronTickerOccurrences([occurrence], CancellationToken.None));

        Assert.Equal(before, await _db.StringGetAsync(RedisKeyBuilder.CronOccurrenceKey(occurrence.Id)));
        Assert.Equal("terminal-evidence",
            (string?)await _db.StringGetAsync(RedisKeyBuilder.CronOccurrenceResultKey(occurrence.Id)));
    }

    private CronTickerEntity NewCron(string function) => new()
    {
        Id = Guid.NewGuid(), Function = function, Expression = "*/5 * * * *", DefinitionRevision = 1,
        Request = [], CreatedAt = BaseNow, UpdatedAt = BaseNow
    };

    private static CronTickerOccurrenceEntity<CronTickerEntity> NewOccurrence(
        Guid cronId, DateTime executionTime, TickerStatus status) => new()
    {
        Id = Guid.NewGuid(), CronTickerId = cronId, DefinitionRevision = 1, Status = status,
        ExecutionTime = executionTime, CreatedAt = BaseNow.AddHours(-1), UpdatedAt = BaseNow.AddHours(-1)
    };

    private async Task SeedLegacyOccurrenceAsync(CronTickerOccurrenceEntity<CronTickerEntity> occurrence)
    {
        var id = occurrence.Id.ToString();
        await _db.StringSetAsync(RedisKeyBuilder.CronOccurrenceKey(occurrence.Id),
            JsonSerializer.Serialize(occurrence,
                TestJsonSerializerContext.Default.CronTickerOccurrenceEntityCronTickerEntity));
        await _db.SetAddAsync(RedisKeyBuilder.CronOccurrenceIdsKey, id);
        await _db.SetAddAsync(RedisKeyBuilder.CronOccurrencesByCronKey(occurrence.CronTickerId), id);
        await _db.SortedSetAddAsync(RedisKeyBuilder.CronOccurrencePendingKey, id,
            occurrence.ExecutionTime.ToUniversalTime().Ticks);
        await _db.StringSetAsync(
            RedisKeyBuilder.CronOccurrenceSlotKey(occurrence.CronTickerId, occurrence.ExecutionTime), id);
    }

    private async Task AssertCompleteOccurrencePublicationAsync(Guid occurrenceId, Guid cronId)
    {
        Assert.True(await _db.KeyExistsAsync(RedisKeyBuilder.CronOccurrenceKey(occurrenceId)));
        Assert.True(await _db.SetContainsAsync(RedisKeyBuilder.CronOccurrenceIdsKey, occurrenceId.ToString()));
        Assert.True(await _db.SetContainsAsync(RedisKeyBuilder.CronOccurrencesByCronKey(cronId), occurrenceId.ToString()));
        Assert.NotNull(await _db.SortedSetScoreAsync(RedisKeyBuilder.CronOccurrencePendingKey, occurrenceId.ToString()));
    }

    private static async Task<CronTickerOccurrenceEntity<CronTickerEntity>[]> CollectCronAsync(
        IAsyncEnumerable<CronTickerOccurrenceEntity<CronTickerEntity>> source)
    {
        var rows = new List<CronTickerOccurrenceEntity<CronTickerEntity>>();
        await foreach (var row in source) rows.Add(row);
        return rows.ToArray();
    }

    // -------------------------------------------------------------------------
    // 5. RecoverStale relies on lexicographic ordering of fixed-width timestamps
    //    (LeaseUntil / LockedAt). Trailing-zero trimming makes "...00Z" sort AFTER
    //    "...00.5Z", so an expired InProgress lease with a trimmed timestamp is not
    //    recognised as stale. Recovery must move the row off InProgress.
    // -------------------------------------------------------------------------
    [Fact]
    public async Task RecoverStale_ExpiredInProgressLease_RecoversRow_RealLua()
    {
        var ticker = NewIdleTicker();
        await _provider.AddTimeTickers([ticker], CancellationToken.None);

        _now = BaseNow;
        var acquired = Assert.Single(
            await _provider.AcquireImmediateTimeTickersAsync([ticker.Id], CancellationToken.None));
        Assert.Equal(TickerStatus.InProgress, acquired.Status);

        // Advance well past the lease duration so the InProgress lease is expired.
        _now = BaseNow.Add(_options.LeaseDuration).AddMinutes(5);

        var result = await _provider.RecoverStaleTickers(maxStaleRestarts: 3, CancellationToken.None);

        Assert.True(result.RestartedTimeTickers + result.CancelledTimeTickers >= 1,
            "expired InProgress lease should have been recovered");

        var persisted = await _provider.GetTimeTickerById(ticker.Id, CancellationToken.None);
        Assert.NotEqual(TickerStatus.InProgress, persisted!.Status);
    }
}
