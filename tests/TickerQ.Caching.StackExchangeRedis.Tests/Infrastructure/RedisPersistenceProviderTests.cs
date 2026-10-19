using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using StackExchange.Redis;
using TickerQ.Caching.StackExchangeRedis.Infrastructure;
using TickerQ.Caching.StackExchangeRedis.Helpers;
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
    private static readonly string Prefix = new RedisKeyBuilder(
        TickerQRuntimePartition.LegacyGlobal).PartitionPrefix;

    // In-memory stores backing the mock
    private readonly Dictionary<string, string> _store = new();
    private readonly Dictionary<string, HashSet<string>> _sets = new();
    private readonly Dictionary<string, SortedList<double, string>> _sortedSets = new();
    private readonly Dictionary<string, Dictionary<string, string>> _hashes = new();

    public Task InitializeAsync()
    {
        _fixedNow = new DateTime(2025, 6, 15, 12, 0, 0, DateTimeKind.Utc);

        _clock = Substitute.For<ITickerClock>();
        _clock.UtcNow.Returns(_fixedNow);

        _db = Substitute.For<IDatabase>();
        var multiplexer = Substitute.For<IConnectionMultiplexer>();
        var server = Substitute.For<IServer>();
        var endpoint = new DnsEndPoint("standalone.test", 6379);
        _db.Multiplexer.Returns(multiplexer);
        multiplexer.GetEndPoints(Arg.Any<bool>()).Returns([endpoint]);
        multiplexer.GetServer(endpoint, Arg.Any<object>()).Returns(server);
        server.IsConnected.Returns(true);
        server.ServerType.Returns(ServerType.Standalone);

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

    [Fact]
    public async Task RetentionIndex_TracksOnlyTerminalStandaloneTimeTickers()
    {
        var standalone = CreateTimeTicker(status: TickerStatus.Done);
        standalone.ExecutedAt = _fixedNow.AddDays(-10);
        var chained = CreateTimeTicker(status: TickerStatus.Done);
        chained.ExecutedAt = _fixedNow.AddDays(-10);
        chained.Children.Add(CreateTimeTicker(status: TickerStatus.Done));

        await _provider.AddTimeTickers([standalone, chained], CancellationToken.None);

        Assert.True(_sortedSets.TryGetValue($"{Prefix}:tt:retention:succeeded", out var indexed));
        Assert.Contains(standalone.Id.ToString(), indexed!.Values);
        Assert.DoesNotContain(chained.Id.ToString(), indexed.Values);
    }

    [Fact]
    public async Task RetentionIndex_ReactivationRemovesTerminalCandidate()
    {
        var ticker = CreateTimeTicker(status: TickerStatus.Done);
        ticker.ExecutedAt = _fixedNow.AddDays(-10);
        await _provider.AddTimeTickers([ticker], CancellationToken.None);
        Assert.Contains(ticker.Id.ToString(), _sortedSets[$"{Prefix}:tt:retention:succeeded"].Values);

        await _provider.AcquireTimeTickerOnDemandAsync(ticker.Id, _fixedNow, CancellationToken.None);

        Assert.False(_sortedSets.TryGetValue($"{Prefix}:tt:retention:succeeded", out var indexed) &&
                     indexed.Values.Contains(ticker.Id.ToString()));
    }

    [Fact]
    public async Task UpdateTimeTicker_GrandchildMutatesEmbeddedRootAggregate()
    {
        var grandchild = CreateTimeTicker(status: TickerStatus.Idle);
        var child = CreateTimeTicker(status: TickerStatus.Idle);
        child.Children.Add(grandchild);
        var root = CreateTimeTicker(status: TickerStatus.Idle);
        root.Children.Add(child);
        child.ParentId = root.Id;
        grandchild.ParentId = child.Id;
        await _provider.AddTimeTickers([root], CancellationToken.None);
        var acquired = Assert.Single(await _provider.AcquireImmediateTimeTickersAsync(
            [root.Id], CancellationToken.None));

        var update = new InternalFunctionContext
        {
            TickerId = grandchild.Id,
            ParentId = child.Id,
            ChainRootId = root.Id,
            ChainGeneration = acquired.ChainGeneration,
            Type = TickerType.TimeTicker
        }.SetProperty(x => x.Status, TickerStatus.InProgress);

        Assert.Equal(1, await _provider.UpdateTimeTicker(update, CancellationToken.None));
        var persisted = await _provider.GetTimeTickerById(root.Id, CancellationToken.None);
        Assert.Equal(TickerStatus.InProgress, persisted!.Children.Single().Children.Single().Status);
        Assert.Null(persisted.Children.Single().Children.Single().LeaseUntil);
    }

    [Fact]
    public void SupportsRetention_IsTrue()
        => Assert.True(_provider.SupportsRetention);

    [Fact]
    public async Task ReconcileRetentionIndexes_BackfillsMissingTimeAndOccurrenceIndexes()
    {
        var ticker = CreateTimeTicker(status: TickerStatus.Done);
        ticker.ExecutedAt = _fixedNow.AddDays(-10);
        SeedTimeTicker(ticker);
        var cron = CreateCronTicker();
        SeedCronTicker(cron);
        var occurrence = CreateCronOccurrence(cron.Id, status: TickerStatus.Failed);
        occurrence.ExecutedAt = _fixedNow.AddDays(-9);
        SeedCronOccurrence(occurrence);

        await ReconcileToCompletionAsync(batchSize: 1);

        Assert.Contains(ticker.Id.ToString(), _sortedSets[$"{Prefix}:tt:retention:succeeded"].Values);
        Assert.Contains(occurrence.Id.ToString(), _sortedSets[$"{Prefix}:co:retention:failed"].Values);
    }

    [Fact]
    public async Task ReconcileRetentionIndexes_RepairsStaleEntryAndIsIdempotent()
    {
        var ticker = CreateTimeTicker(status: TickerStatus.Failed);
        ticker.ExecutedAt = _fixedNow.AddDays(-10);
        SeedTimeTicker(ticker);
        AddSortedSetEntry($"{Prefix}:tt:retention:succeeded", ticker.Id, ticker.ExecutedAt.Value);

        await ReconcileToCompletionAsync(batchSize: 10);
        await ReconcileToCompletionAsync(batchSize: 10);

        Assert.DoesNotContain(ticker.Id.ToString(), _sortedSets[$"{Prefix}:tt:retention:succeeded"].Values);
        Assert.Equal(1, _sortedSets[$"{Prefix}:tt:retention:failed"].Values.Count(x => x == ticker.Id.ToString()));
    }

    [Fact]
    public async Task ReconcileRetentionIndexes_IsBoundedAndResumesFromPersistedProgress()
    {
        for (var i = 0; i < 3; i++)
        {
            var ticker = CreateTimeTicker(status: TickerStatus.Done);
            ticker.ExecutedAt = _fixedNow.AddDays(-10).AddMinutes(i);
            SeedTimeTicker(ticker);
        }

        var first = await _provider.ReconcileRetentionIndexesAsync(2, CancellationToken.None);
        var second = await _provider.ReconcileRetentionIndexesAsync(2, CancellationToken.None);

        Assert.InRange(first.Examined, 1, 2);
        Assert.True(first.HasMore);
        Assert.InRange(second.Examined, 1, 2);
        Assert.Equal(3, _sortedSets[$"{Prefix}:tt:retention:succeeded"].Count);
    }

    [Fact]
    public async Task ReconcileRetentionIndexes_HonorsCancellationAndExcludesChainedRoots()
    {
        var chained = CreateTimeTicker(status: TickerStatus.Done);
        chained.ExecutedAt = _fixedNow.AddDays(-10);
        chained.Children.Add(CreateTimeTicker(status: TickerStatus.Done));
        SeedTimeTicker(chained);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _provider.ReconcileRetentionIndexesAsync(10, cancellation.Token));
        await ReconcileToCompletionAsync(batchSize: 10);

        Assert.False(_sortedSets.TryGetValue($"{Prefix}:tt:retention:succeeded", out var indexed) &&
                     indexed.Values.Contains(chained.Id.ToString()));
    }

    [Fact]
    public async Task ReconcileRetentionIndexes_NeverDeletesCronDefinitions()
    {
        var cron = CreateCronTicker();
        SeedCronTicker(cron);

        await ReconcileToCompletionAsync(batchSize: 10);

        Assert.NotNull(await _provider.GetCronTickerById(cron.Id, CancellationToken.None));
    }

    private async Task ReconcileToCompletionAsync(int batchSize)
    {
        for (var i = 0; i < 20; i++)
        {
            var result = await _provider.ReconcileRetentionIndexesAsync(batchSize, CancellationToken.None);
            if (!result.HasMore)
                return;
        }

        throw new InvalidOperationException("Redis retention reconciliation did not converge.");
    }

    private void AddSortedSetEntry(string key, Guid id, DateTime score)
    {
        if (!_sortedSets.TryGetValue(key, out var entries))
            _sortedSets[key] = entries = new SortedList<double, string>();
        entries[score.ToUniversalTime().Ticks] = id.ToString();
    }

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

        batch.SortedSetAddAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<double>(),
                Arg.Any<SortedSetWhen>(), Arg.Any<CommandFlags>())
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

                if (script.Contains("Atomically mutate derived indexes", StringComparison.Ordinal))
                {
                    var activationKey = (string)keys[0];
                    if (_hashes.TryGetValue(activationKey, out var activation) &&
                        activation.ContainsKey("legacyAdoptionState"))
                        return RedisResult.Create((RedisValue)0);
                    var member = (string)argv[0];
                    var operationCount = Rvi(argv[1]);
                    for (var index = 0; index < operationCount; index++)
                    {
                        var offset = 2 + index * 3;
                        var command = (string)argv[offset];
                        var key = (string)keys[Rvi(argv[offset + 1]) - 1];
                        switch (command)
                        {
                            case "SADD":
                                if (!_sets.TryGetValue(key, out var set)) _sets[key] = set = [];
                                set.Add(member);
                                break;
                            case "SREM":
                                if (_sets.TryGetValue(key, out var existingSet)) existingSet.Remove(member);
                                break;
                            case "DELSET":
                                _sets.Remove(key);
                                break;
                            case "ZADD":
                                var score = double.Parse(argv[offset + 2].ToString(),
                                    System.Globalization.CultureInfo.InvariantCulture);
                                AddSortedSetEntry(key, Guid.Parse(member),
                                    new DateTime((long)score, DateTimeKind.Utc));
                                break;
                            case "ZREM":
                                RemoveSortedSetMember(key, member);
                                break;
                        }
                    }
                    return RedisResult.Create((RedisValue)1);
                }

                if (script.Contains("retention index reconciliation", StringComparison.Ordinal))
                    return SimulateRetentionReconciliation(keys, argv);

                if (script.Contains("Bounded, resumable repair of cron definition discoverability", StringComparison.Ordinal))
                    return RedisResult.Create(new RedisValue[] { 0, 0, 0, "ids:0" });

                if (script.Contains("Atomically publish/remove one Cron definition", StringComparison.Ordinal))
                    return SimulateCronDefinitionMutation(keys, argv);

                if (script.Contains("Atomically delete one occurrence", StringComparison.Ordinal))
                    return SimulateCronOccurrenceDeletion(keys, argv);

                if (script.Contains("Atomically reserve one logical recurring slot", StringComparison.Ordinal))
                {
                    var definitionKey = (string)keys[0];
                    var candidateKey = (string)keys[1];
                    var slotKey = (string)keys[2];
                    var allIdsKey = (string)keys[3];
                    var reverseKey = (string)keys[4];
                    var pendingKey = (string)keys[5];
                    var retentionKey = (string)keys[7];
                    var candidateId = (string)argv[0];
                    var candidateJson = (string)argv[1];
                    var score = double.Parse(argv[2].ToString(), System.Globalization.CultureInfo.InvariantCulture);
                    var expectedRevision = Rvi(argv[3]);
                    var occurrencePrefix = (string)argv[4];
                    var cronId = (string)argv[5];
                    var idle = Rvi(argv[6]);
                    var queued = Rvi(argv[7]);
                    var firstTerminal = Rvi(argv[8]);
                    var pendingEligible = Rvi(argv[10]) == 1;
                    var retentionEligible = Rvi(argv[11]) == 1;
                    var retentionScore = double.Parse(argv[12].ToString(), System.Globalization.CultureInfo.InvariantCulture);

                    if (!_store.TryGetValue(definitionKey, out var definitionJson))
                        return RedisResult.Create(new RedisValue[] { -2, "" });
                    using (var definitionDoc = JsonDocument.Parse(definitionJson))
                    {
                        if (!definitionDoc.RootElement.TryGetProperty("DefinitionRevision", out var revision) ||
                            revision.GetInt32() != expectedRevision)
                            return RedisResult.Create(new RedisValue[] { -2, "" });
                    }

                    if (_store.TryGetValue(slotKey, out var existingId))
                    {
                        var valid = false;
                        if (_store.TryGetValue(occurrencePrefix + existingId, out var ownerJson))
                        {
                            try
                            {
                                using var ownerDoc = JsonDocument.Parse(ownerJson);
                                var owner = ownerDoc.RootElement;
                                valid = owner.GetProperty("Status").GetInt32() < firstTerminal &&
                                    string.Equals(owner.GetProperty("CronTickerId").GetString(), cronId,
                                        StringComparison.OrdinalIgnoreCase);
                                if (valid)
                                {
                                    if (!_sets.TryGetValue(allIdsKey, out var all)) _sets[allIdsKey] = all = [];
                                    if (!_sets.TryGetValue(reverseKey, out var reverse)) _sets[reverseKey] = reverse = [];
                                    all.Add(existingId);
                                    reverse.Add(existingId);
                                    var ownerStatus = owner.GetProperty("Status").GetInt32();
                                    if (ownerStatus == idle || ownerStatus == queued)
                                        AddSortedSetEntry(pendingKey, Guid.Parse(existingId), new DateTime((long)score, DateTimeKind.Utc));
                                }
                            }
                            catch (JsonException) { }
                        }
                        if (valid)
                            return RedisResult.Create(new RedisValue[] { 0, existingId });
                        if (_store.GetValueOrDefault(slotKey) == existingId)
                            _store.Remove(slotKey);
                    }

                    // The real script validates the candidate identity/revision before this point.
                    using (var candidateDoc = JsonDocument.Parse(candidateJson))
                    {
                        var candidate = candidateDoc.RootElement;
                        if (!string.Equals(candidate.GetProperty("Id").GetString(), candidateId, StringComparison.OrdinalIgnoreCase) ||
                            !string.Equals(candidate.GetProperty("CronTickerId").GetString(), cronId, StringComparison.OrdinalIgnoreCase) ||
                            candidate.GetProperty("DefinitionRevision").GetInt32() != expectedRevision)
                            throw new InvalidDataException("Mock recurring-slot candidate does not match the Lua contract.");
                    }
                    _store[candidateKey] = candidateJson;
                    _store[slotKey] = candidateId;
                    if (!_sets.TryGetValue(allIdsKey, out var candidateIds)) _sets[allIdsKey] = candidateIds = [];
                    if (!_sets.TryGetValue(reverseKey, out var candidateReverse)) _sets[reverseKey] = candidateReverse = [];
                    candidateIds.Add(candidateId);
                    candidateReverse.Add(candidateId);
                    if (pendingEligible)
                        AddSortedSetEntry(pendingKey, Guid.Parse(candidateId), new DateTime((long)score, DateTimeKind.Utc));
                    if (retentionEligible)
                        AddSortedSetEntry(retentionKey, Guid.Parse(candidateId), new DateTime((long)retentionScore, DateTimeKind.Utc));
                    return RedisResult.Create(new RedisValue[] { 1, candidateId });
                }

                if (script.Contains("Atomically quarantine one unleased pending occurrence", StringComparison.Ordinal))
                {
                    var occurrenceEntityKey = (string)keys[0];
                    var id = (string)argv[0];
                    var cronId = (string)argv[1];
                    if (!_store.TryGetValue(occurrenceEntityKey, out var occurrenceJson))
                    {
                        if (_sets.TryGetValue((string)keys[2], out var missingAll)) missingAll.Remove(id);
                        RemoveSortedSetMember((string)keys[3], id);
                        if (_sets.TryGetValue((string)keys[4], out var missingReverse)) missingReverse.Remove(id);
                        for (var index = 5; index <= 8; index++) RemoveSortedSetMember((string)keys[index], id);
                        if (_store.GetValueOrDefault((string)keys[10]) == id) _store.Remove((string)keys[10]);
                        return RedisResult.Create((RedisValue)0);
                    }

                    JsonElement occurrence;
                    try
                    {
                        using var occurrenceDoc = JsonDocument.Parse(occurrenceJson);
                        occurrence = occurrenceDoc.RootElement.Clone();
                    }
                    catch (JsonException)
                    {
                        var quarantineKey = (string)keys[9];
                        if (!_hashes.TryGetValue(quarantineKey, out var quarantine))
                            _hashes[quarantineKey] = quarantine = new Dictionary<string, string>();
                        quarantine[id] = occurrenceJson;
                        if (_sets.TryGetValue((string)keys[2], out var corruptAll)) corruptAll.Remove(id);
                        RemoveSortedSetMember((string)keys[3], id);
                        if (_sets.TryGetValue((string)keys[4], out var corruptReverse)) corruptReverse.Remove(id);
                        for (var index = 5; index <= 8; index++) RemoveSortedSetMember((string)keys[index], id);
                        if (_store.GetValueOrDefault((string)keys[10]) == id) _store.Remove((string)keys[10]);
                        return RedisResult.Create((RedisValue)(-1));
                    }

                    if (!string.Equals(occurrence.GetProperty("CronTickerId").GetString(), cronId, StringComparison.OrdinalIgnoreCase))
                        return RedisResult.Create((RedisValue)0);
                    var occurrenceStatus = occurrence.GetProperty("Status").GetInt32();
                    var hasHolder = occurrence.TryGetProperty("LockHolder", out var holder) &&
                                    holder.ValueKind != JsonValueKind.Null && !string.IsNullOrEmpty(holder.GetString());
                    var hasToken = occurrence.TryGetProperty("AcquisitionToken", out var token) &&
                                   token.ValueKind != JsonValueKind.Null && !string.IsNullOrEmpty(token.GetString());
                    var liveLease = occurrence.TryGetProperty("LeaseUntil", out var lease) &&
                                    lease.ValueKind != JsonValueKind.Null && lease.GetDateTime() > DateTime.Parse((string)argv[2]);
                    if ((occurrenceStatus != Rvi(argv[3]) && occurrenceStatus != Rvi(argv[4])) || hasHolder || hasToken || liveLease)
                        return RedisResult.Create((RedisValue)0);
                    _store[occurrenceEntityKey] = SetJsonProperties(occurrenceJson, new Dictionary<string, object?>
                    {
                        ["Status"] = Rvi(argv[5]), ["SkippedReason"] = (string)argv[6],
                        ["ExecutedAt"] = occurrence.TryGetProperty("ExecutedAt", out var executed) && executed.ValueKind != JsonValueKind.Null
                            ? executed.GetString() : (string)argv[2],
                        ["UpdatedAt"] = (string)argv[2], ["LockHolder"] = null, ["LockedAt"] = null,
                        ["LeaseUntil"] = null, ["AcquisitionToken"] = null
                    });
                    RemoveSortedSetMember((string)keys[3], id);
                    for (var index = 5; index <= 7; index++) RemoveSortedSetMember((string)keys[index], id);
                    RemoveSortedSetMember((string)keys[8], id);
                    AddSortedSetEntry((string)keys[8], Guid.Parse(id), new DateTime((long)double.Parse(argv[7].ToString(),
                        System.Globalization.CultureInfo.InvariantCulture), DateTimeKind.Utc));
                    if (_hashes.TryGetValue((string)keys[9], out var repaired)) repaired.Remove(id);
                    if (_store.GetValueOrDefault((string)keys[10]) == id) _store.Remove((string)keys[10]);
                    return RedisResult.Create((RedisValue)1);
                }

                if (script.Contains("delete one time-ticker aggregate root", StringComparison.Ordinal))
                {
                    var id = (string)argv[0];
                    var removed = _store.Remove((string)keys[0]);
                    if (_sets.TryGetValue((string)keys[1], out var ids)) ids.Remove(id);
                    RemoveSortedSetMember((string)keys[2], id);
                    if (_hashes.TryGetValue((string)keys[3], out var evidence))
                        for (var index = 1; index < argv.Length; index++) evidence.Remove((string)argv[index]);
                    for (var index = 4; index < keys.Length; index++) _store.Remove((string)keys[index]);
                    return RedisResult.Create((RedisValue)(removed ? 1 : 0));
                }

                if (script.Contains("publishes a TimeTicker document", StringComparison.Ordinal))
                {
                    var timeTickerKey = (string)keys[0];
                    var id = (string)argv[0];
                    var activationKey = (string)keys[3];
                    if (activationKey.Contains(":scope:", StringComparison.Ordinal) &&
                        !_hashes.ContainsKey(activationKey))
                        return RedisResult.Create((RedisValue)0);
                    if ((string)argv[3] == "once" && _store.ContainsKey(timeTickerKey))
                        return RedisResult.Create((RedisValue)0);
                    for (var index = 4; index < keys.Length; index++)
                        _store.Remove((string)keys[index]);
                    _store[timeTickerKey] = (string)argv[1];
                    if (!_sets.TryGetValue((string)keys[1], out var ids))
                        _sets[(string)keys[1]] = ids = [];
                    ids.Add(id);
                    var pendingScore = (string)argv[2];
                    if (string.IsNullOrEmpty(pendingScore))
                        RemoveSortedSetMember((string)keys[2], id);
                    else
                        AddSortedSetEntry((string)keys[2], Guid.Parse(id),
                            new DateTime((long)double.Parse(pendingScore,
                                System.Globalization.CultureInfo.InvariantCulture), DateTimeKind.Utc));
                    return RedisResult.Create((RedisValue)1);
                }

                var entityKey = (string)keys[0];

                if (!_store.TryGetValue(entityKey, out var json))
                    return RedisResult.Create(RedisValue.Null);

                var obj = JsonSerializer.Deserialize<JsonElement>(json);
                var status = obj.GetProperty("Status").GetInt32();
                var lockHolder = obj.TryGetProperty("LockHolder", out var lh) && lh.ValueKind != JsonValueKind.Null
                    ? lh.GetString() : null;

                if (script.Contains("Renew one lease", StringComparison.Ordinal))
                {
                    var token = obj.TryGetProperty("AcquisitionToken", out var at) && at.ValueKind != JsonValueKind.Null
                        ? at.GetString() : null;
                    if (lockHolder != (string)argv[0] ||
                        !string.Equals(token, (string)argv[1], StringComparison.OrdinalIgnoreCase) ||
                        status != Rvi(argv[2]))
                        return RedisResult.Create((RedisValue)0);
                    _store[entityKey] = SetJsonProperties(json, new Dictionary<string, object?>
                    {
                        ["LeaseUntil"] = (string)argv[3]
                    });
                    return RedisResult.Create((RedisValue)1);
                }

                if (script.Contains("still owns the same InProgress generation", StringComparison.Ordinal))
                {
                    var token = obj.TryGetProperty("AcquisitionToken", out var at) && at.ValueKind != JsonValueKind.Null
                        ? at.GetString() : null;
                    var held = lockHolder == (string)argv[0] &&
                               string.Equals(token, (string)argv[1], StringComparison.OrdinalIgnoreCase) &&
                               status == Rvi(argv[2]);
                    return RedisResult.Create((RedisValue)(held ? 1 : 0));
                }

                if (script.Contains("revive one non-running ticker", StringComparison.Ordinal))
                {
                    var inProgress = Rvi(argv[4]);
                    if (status == inProgress) return RedisResult.Create(RedisValue.Null);
                    var queued = Rvi(argv[6]);
                    if (status == queued && !string.IsNullOrEmpty(lockHolder) && lockHolder != (string)argv[0])
                        return RedisResult.Create(RedisValue.Null);
                    var updated = SetJsonProperties(json, new Dictionary<string, object?>
                    {
                        ["LockHolder"] = (string)argv[0],
                        ["LockedAt"] = (string)argv[1],
                        ["LeaseUntil"] = (string)argv[2],
                        ["AcquisitionToken"] = (string)argv[3],
                        ["ChainRootId"] = obj.GetProperty("Id").GetString(),
                        ["ChainGeneration"] = (string)argv[3],
                        ["Status"] = inProgress,
                        ["ExecutionTime"] = (string)argv[5],
                        ["UpdatedAt"] = (string)argv[1],
                        ["ExecutedAt"] = null,
                        ["ElapsedTime"] = 0,
                        ["StaleRestartCount"] = 0,
                        ["Exception"] = null
                    });
                    _store[entityKey] = updated;
                    return RedisResult.Create((RedisValue)updated);
                }

                if (script.Contains("guarded by the observed generation", StringComparison.Ordinal))
                {
                    var expectedHolder = (string)argv[0];
                    var expectedToken = (string)argv[1];
                    var expectedStatus = Rvi(argv[2]);
                    var embeddedTargetId = (string)argv[7];
                    var expectedGeneration = (string)argv[8];
                    var token = obj.TryGetProperty("AcquisitionToken", out var at) && at.ValueKind != JsonValueKind.Null
                        ? at.GetString() : null;
                    var fencedStatus = status;
                    if (!string.IsNullOrEmpty(embeddedTargetId))
                    {
                        var rootId = obj.GetProperty("Id").GetString();
                        var chainRootId = obj.TryGetProperty("ChainRootId", out var cr) ? cr.GetString() : null;
                        var generation = obj.TryGetProperty("ChainGeneration", out var cg) ? cg.GetString() : null;
                        var target = FindEmbeddedTicker(obj, embeddedTargetId);
                        if (string.IsNullOrEmpty(expectedGeneration) || target == null ||
                            !string.Equals(rootId, chainRootId, StringComparison.OrdinalIgnoreCase) ||
                            !string.Equals(generation, expectedGeneration, StringComparison.OrdinalIgnoreCase))
                            return RedisResult.Create(RedisValue.Null);
                        fencedStatus = target.Value.GetProperty("Status").GetInt32();
                    }
                    if ((!string.IsNullOrEmpty(expectedHolder) && lockHolder != expectedHolder) ||
                        (!string.IsNullOrEmpty(expectedToken) && !string.Equals(token, expectedToken, StringComparison.OrdinalIgnoreCase)) ||
                        (expectedStatus >= 0 && fencedStatus != expectedStatus))
                        return RedisResult.Create(RedisValue.Null);
                    var replacement = (string)argv[4];
                    _store[entityKey] = replacement;
                    return RedisResult.Create((RedisValue)replacement);
                }

                if (script.Contains("expected acquisition token", StringComparison.Ordinal))
                {
                    var expectedHolder = (string)argv[0];
                    var expectedToken = (string)argv[1];
                    var statusQueued = Rvi(argv[3]);
                    var statusInProgress = Rvi(argv[4]);
                    var token = obj.TryGetProperty("AcquisitionToken", out var at) && at.ValueKind != JsonValueKind.Null
                        ? at.GetString() : null;
                    if (status != statusQueued || lockHolder != expectedHolder ||
                        !string.Equals(token, expectedToken, StringComparison.OrdinalIgnoreCase))
                        return RedisResult.Create(RedisValue.Null);

                    var transitioned = SetJsonProperties(json, new Dictionary<string, object>
                    {
                        ["Status"] = statusInProgress,
                        ["UpdatedAt"] = (string)argv[2]
                    });
                    _store[entityKey] = transitioned;
                    return RedisResult.Create((RedisValue)transitioned);
                }

                // Differentiate remaining scripts by script body: admission mode is appended to argv.
                if (script.Contains("Recover resources owned by a dead node", StringComparison.Ordinal))
                {
                    // RecoverDeadNode: optional authoritative definition adds stale-revision quarantine args.
                    var deadNodeId = (string)argv[0];
                    var statusIdle = Rvi(argv[2]);
                    var statusQueued = Rvi(argv[3]);
                    var statusInProgress = Rvi(argv[4]);
                    if (lockHolder != deadNodeId) return RedisResult.Create(RedisValue.Null);
                    if (status != statusIdle && status != statusQueued && status != statusInProgress)
                        return RedisResult.Create(RedisValue.Null);

                    var staleRevision = false;
                    if (keys.Length == 4)
                    {
                        var occurrenceRevision = obj.TryGetProperty("DefinitionRevision", out var occurrenceRevisionElement)
                            ? occurrenceRevisionElement.GetInt32() : 0;
                        if (!_store.TryGetValue((string)keys[2], out var definitionJson))
                            staleRevision = true;
                        else
                        {
                            using var definitionDoc = JsonDocument.Parse(definitionJson);
                            var definitionRevision = definitionDoc.RootElement.TryGetProperty("DefinitionRevision", out var definitionRevisionElement)
                                ? definitionRevisionElement.GetInt32() : 0;
                            staleRevision = occurrenceRevision != definitionRevision;
                        }
                    }
                    var recoveredProperties = new Dictionary<string, object?>
                    {
                        ["LockHolder"] = null,
                        ["LockedAt"] = null,
                        ["LeaseUntil"] = null,
                        ["AcquisitionToken"] = null,
                        ["Status"] = staleRevision ? Rvi(argv[5]) : statusIdle,
                        ["UpdatedAt"] = (string)argv[1]
                    };
                    if (staleRevision)
                    {
                        recoveredProperties["SkippedReason"] = (string)argv[6];
                        if (!obj.TryGetProperty("ExecutedAt", out var executedAt) || executedAt.ValueKind == JsonValueKind.Null)
                            recoveredProperties["ExecutedAt"] = (string)argv[1];
                    }
                    var updated = SetJsonProperties(json, recoveredProperties);
                    _store[entityKey] = updated;
                    return RedisResult.Create((RedisValue)updated);
                }

                if (argv.Length == 6 && script.Contains("exact supported epoch", StringComparison.Ordinal))
                {
                    // Release: ARGV[0]=lockHolder, ARGV[1]=now, ARGV[2]=statusIdle,
                    // ARGV[3]=statusQueued, ARGV[4]=exact supported epoch, ARGV[5]=admission mode.
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
                        ["LeaseUntil"] = null,
                        ["AcquisitionToken"] = null,
                        ["Status"] = statusIdle,
                        ["UpdatedAt"] = (string)argv[1]
                    });
                    _store[entityKey] = updated;
                    return RedisResult.Create((RedisValue)updated);
                }

                // Acquire also receives ARGV[6]=fresh acquisition token.
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
                        ["UpdatedAt"] = (string)argv[1],
                        ["AcquisitionToken"] = (string)argv[6],
                        ["ChainRootId"] = obj.GetProperty("Id").GetString(),
                        ["ChainGeneration"] = (string)argv[6]
                    });
                    _store[entityKey] = updated;
                    return RedisResult.Create((RedisValue)updated);
                }
            });
    }

    private RedisResult SimulateCronDefinitionMutation(RedisKey[] keys, RedisValue[] argv)
    {
        var cronEntityKey = (string)keys[0];
        var idsKey = (string)keys[1];
        var id = (string)argv[0];
        if ((string)argv[3] == "1" && _sets.TryGetValue((string)keys[3], out var occurrenceIds))
        {
            // Match the Lua protocol: validate all JSON before mutating any valid occurrence.
            foreach (var occurrenceId in occurrenceIds.ToArray())
            {
                var occurrenceKey = (string)argv[10] + occurrenceId;
                if (!_store.TryGetValue(occurrenceKey, out var raw))
                    continue;
                try { using var _ = JsonDocument.Parse(raw); }
                catch (JsonException)
                {
                    if (!_hashes.TryGetValue((string)keys[10], out var quarantine))
                        _hashes[(string)keys[10]] = quarantine = new Dictionary<string, string>();
                    quarantine[occurrenceId] = raw;
                    if (_sets.TryGetValue((string)keys[4], out var all)) all.Remove(occurrenceId);
                    occurrenceIds.Remove(occurrenceId);
                    RemoveSortedSetMember((string)keys[5], occurrenceId);
                    for (var i = 6; i <= 9; i++) RemoveSortedSetMember((string)keys[i], occurrenceId);
                    return RedisResult.Create((RedisValue)(-2));
                }
            }

            foreach (var occurrenceId in occurrenceIds.ToArray())
            {
                var occurrenceKey = (string)argv[10] + occurrenceId;
                if (!_store.TryGetValue(occurrenceKey, out var raw))
                {
                    if (_sets.TryGetValue((string)keys[4], out var all)) all.Remove(occurrenceId);
                    occurrenceIds.Remove(occurrenceId);
                    RemoveSortedSetMember((string)keys[5], occurrenceId);
                    for (var i = 6; i <= 9; i++) RemoveSortedSetMember((string)keys[i], occurrenceId);
                    continue;
                }
                using var doc = JsonDocument.Parse(raw);
                var occurrence = doc.RootElement;
                if (!string.Equals(occurrence.GetProperty("CronTickerId").GetString(), id, StringComparison.OrdinalIgnoreCase))
                    continue;
                var status = occurrence.GetProperty("Status").GetInt32();
                var holderEmpty = !occurrence.TryGetProperty("LockHolder", out var holder) ||
                                  holder.ValueKind == JsonValueKind.Null || string.IsNullOrEmpty(holder.GetString());
                var tokenEmpty = !occurrence.TryGetProperty("AcquisitionToken", out var token) ||
                                 token.ValueKind == JsonValueKind.Null || string.IsNullOrEmpty(token.GetString());
                var leaseExpired = !occurrence.TryGetProperty("LeaseUntil", out var lease) ||
                                   lease.ValueKind == JsonValueKind.Null || lease.GetDateTime() <= DateTime.Parse((string)argv[4]);
                if ((status != Rvi(argv[5]) && status != Rvi(argv[6])) || !holderEmpty || !tokenEmpty || !leaseExpired)
                    continue;
                _store[occurrenceKey] = SetJsonProperties(raw, new Dictionary<string, object?>
                {
                    ["Status"] = Rvi(argv[7]), ["SkippedReason"] = (string)argv[8],
                    ["ExecutedAt"] = occurrence.TryGetProperty("ExecutedAt", out var executed) && executed.ValueKind != JsonValueKind.Null
                        ? executed.GetString() : (string)argv[4],
                    ["UpdatedAt"] = (string)argv[4], ["LockHolder"] = null, ["LockedAt"] = null,
                    ["LeaseUntil"] = null, ["AcquisitionToken"] = null
                });
                RemoveSortedSetMember((string)keys[5], occurrenceId);
                for (var i = 6; i <= 9; i++) RemoveSortedSetMember((string)keys[i], occurrenceId);
                AddSortedSetEntry((string)keys[9], Guid.Parse(occurrenceId),
                    new DateTime((long)double.Parse(argv[9].ToString(), System.Globalization.CultureInfo.InvariantCulture), DateTimeKind.Utc));
                var executionTime = occurrence.GetProperty("ExecutionTime").GetDateTime();
                var slotKey = (string)argv[11] + executionTime.ToUniversalTime().Ticks;
                if (_store.GetValueOrDefault(slotKey) == occurrenceId) _store.Remove(slotKey);
            }
        }

        if ((string)argv[1] == "delete")
        {
            var removed = _store.Remove(cronEntityKey);
            if (_sets.TryGetValue(idsKey, out var ids)) ids.Remove(id);
            return RedisResult.Create((RedisValue)(removed ? 1 : 0));
        }
        _store[cronEntityKey] = (string)argv[2];
        if (!_sets.TryGetValue(idsKey, out var members)) _sets[idsKey] = members = [];
        members.Add(id);
        return RedisResult.Create((RedisValue)1);
    }

    private RedisResult SimulateCronOccurrenceDeletion(RedisKey[] keys, RedisValue[] argv)
    {
        var occurrenceKey = (string)keys[0];
        var id = (string)argv[0];
        if (_store.TryGetValue(occurrenceKey, out var raw))
        {
            using var doc = JsonDocument.Parse(raw);
            var occurrence = doc.RootElement;
            if (!string.Equals(occurrence.GetProperty("Id").GetString(), id, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(occurrence.GetProperty("CronTickerId").GetString(), (string)argv[1], StringComparison.OrdinalIgnoreCase))
                return RedisResult.Create((RedisValue)0);
            if (argv.Length == 6)
            {
                var status = occurrence.GetProperty("Status").GetInt32();
                var executedAt = occurrence.GetProperty("ExecutedAt").GetDateTime();
                var hasToken = occurrence.TryGetProperty("AcquisitionToken", out var token) &&
                               token.ValueKind != JsonValueKind.Null && !string.IsNullOrEmpty(token.GetString());
                var liveLease = occurrence.TryGetProperty("LeaseUntil", out var lease) &&
                                lease.ValueKind != JsonValueKind.Null && lease.GetDateTime() > DateTime.Parse((string)argv[5]);
                if ((status != Rvi(argv[2]) && status != Rvi(argv[3])) ||
                    executedAt >= DateTime.Parse((string)argv[4]) || hasToken || liveLease)
                    return RedisResult.Create((RedisValue)0);
            }
        }
        var existed = _store.Remove(occurrenceKey);
        _store.Remove((string)keys[1]);
        if (_sets.TryGetValue((string)keys[2], out var allIds)) allIds.Remove(id);
        RemoveSortedSetMember((string)keys[3], id);
        if (_sets.TryGetValue((string)keys[4], out var reverse)) reverse.Remove(id);
        for (var i = 5; i <= 8; i++) RemoveSortedSetMember((string)keys[i], id);
        if (_store.GetValueOrDefault((string)keys[9]) == id) _store.Remove((string)keys[9]);
        return RedisResult.Create((RedisValue)(existed ? 1 : 0));
    }

    private RedisResult SimulateRetentionReconciliation(RedisKey[] keys, RedisValue[] argv)
    {
        var phaseKey = (string)keys[2];
        var cursorKey = (string)keys[3];
        var phase = _store.GetValueOrDefault(phaseKey, "time");
        var offset = int.TryParse(_store.GetValueOrDefault(cursorKey, "0"), out var parsed) ? parsed : 0;
        var max = Rvi(argv[0]);
        var idsKey = (string)(phase == "time" ? keys[0] : keys[1]);
        var prefix = (string)(phase == "time" ? argv[1] : argv[2]);
        var ids = _sets.GetValueOrDefault(idsKey, []).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var selected = ids.Skip(offset).Take(max).ToArray();
        var nextOffset = offset + selected.Length;

        foreach (var id in selected)
        {
            var firstIndex = phase == "time" ? 4 : 8;
            for (var i = firstIndex; i < firstIndex + 4; i++)
                RemoveSortedSetMember((string)keys[i], id);

            if (!_store.TryGetValue(prefix + id, out var raw))
                continue;
            using var doc = JsonDocument.Parse(raw);
            var obj = doc.RootElement;
            if (!obj.TryGetProperty("ExecutedAt", out var executed) || executed.ValueKind == JsonValueKind.Null ||
                HasValue(obj, "AcquisitionToken") || HasValue(obj, "LeaseUntil") ||
                phase == "time" && HasChildren(obj))
                continue;

            var status = obj.GetProperty("Status").GetInt32();
            var indexOffset = status == Rvi(argv[3]) || status == Rvi(argv[4]) ? 0
                : status == Rvi(argv[5]) ? 1
                : status == Rvi(argv[6]) ? 2
                : status == Rvi(argv[7]) ? 3 : -1;
            if (indexOffset >= 0)
                AddSortedSetEntry((string)keys[firstIndex + indexOffset], Guid.Parse(id), executed.GetDateTime());
        }

        var completed = false;
        if (nextOffset >= ids.Length)
        {
            nextOffset = 0;
            if (phase == "time")
                phase = "occurrence";
            else
            {
                phase = "time";
                completed = true;
            }
        }
        _store[phaseKey] = phase;
        _store[cursorKey] = nextOffset.ToString();
        return RedisResult.Create(new RedisValue[]
        {
            selected.Length,
            completed ? 0 : 1,
            $"{phase}:{nextOffset}"
        });
    }

    private static bool HasValue(JsonElement obj, string property)
        => obj.TryGetProperty(property, out var value) &&
           value.ValueKind != JsonValueKind.Null &&
           (value.ValueKind != JsonValueKind.String || !string.IsNullOrEmpty(value.GetString()));

    private static bool HasChildren(JsonElement obj)
    {
        if (!obj.TryGetProperty("Children", out var children) || children.ValueKind == JsonValueKind.Null)
            return false;
        return children.ValueKind switch
        {
            JsonValueKind.Array => children.EnumerateArray().Any(),
            JsonValueKind.Object => children.EnumerateObject().Any(),
            _ => false
        };
    }

    private static JsonElement? FindEmbeddedTicker(JsonElement root, string targetId)
    {
        if (!root.TryGetProperty("Children", out var children) || children.ValueKind != JsonValueKind.Array)
            return null;
        foreach (var child in children.EnumerateArray())
        {
            if (child.TryGetProperty("Id", out var id) &&
                string.Equals(id.GetString(), targetId, StringComparison.OrdinalIgnoreCase))
                return child;
            var descendant = FindEmbeddedTicker(child, targetId);
            if (descendant.HasValue)
                return descendant;
        }
        return null;
    }

    private void RemoveSortedSetMember(string key, string member)
    {
        if (!_sortedSets.TryGetValue(key, out var entries))
            return;
        foreach (var score in entries.Where(x => x.Value == member).Select(x => x.Key).ToArray())
            entries.Remove(score);
    }

    private void SetupKeyOperations()
    {
        _db.KeyDeleteAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(callInfo =>
            {
                var key = (string)callInfo.ArgAt<RedisKey>(0);
                return _store.Remove(key);
            });

        _db.KeyDeleteAsync(Arg.Any<RedisKey[]>(), Arg.Any<CommandFlags>())
            .Returns(callInfo =>
            {
                long removed = 0;
                foreach (var redisKey in callInfo.ArgAt<RedisKey[]>(0))
                {
                    var key = (string)redisKey;
                    if (_store.Remove(key)) removed++;
                }
                return removed;
            });
    }

    private string SetJsonProperties(string json, Dictionary<string, object?> props)
    {
        using var doc = JsonDocument.Parse(json);
        using var ms = new MemoryStream();
        using (var writer = new Utf8JsonWriter(ms))
        {
            writer.WriteStartObject();
            var written = new HashSet<string>(StringComparer.Ordinal);
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                written.Add(prop.Name);
                if (props.TryGetValue(prop.Name, out var newVal))
                {
                    writer.WritePropertyName(prop.Name);
                    WriteValue(writer, newVal);
                }
                else
                {
                    prop.WriteTo(writer);
                }
            }
            foreach (var (name, value) in props.Where(x => !written.Contains(x.Key)))
            {
                writer.WritePropertyName(name);
                WriteValue(writer, value);
            }
            writer.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(ms.ToArray());
    }

    private static void WriteValue(Utf8JsonWriter writer, object? value)
    {
        switch (value)
        {
            case null: writer.WriteNullValue(); break;
            case int i: writer.WriteNumberValue(i); break;
            case string s: writer.WriteStringValue(s); break;
            default: writer.WriteStringValue(value.ToString()); break;
        }
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

    // =========================================================================
    // Slice 2: stable seed ownership (SeedKey), adoption in place, convergence,
    // and duplicate tolerance (the old keyed ToDictionary threw on duplicates).
    // =========================================================================

    [Fact]
    public async Task Migrate_NewSeededRow_StampsSeedKey_AndDeterministicId()
    {
        await _provider.MigrateDefinedCronTickers(
            [new DefinedCronTickerSeed("redis-seedkey", "*/5 * * * *", 1, null)], CancellationToken.None);

        var rows = await _provider.GetCronTickers(c => c.Function == "redis-seedkey", CancellationToken.None);
        var row = Assert.Single(rows);
        var expectedKey = CronSeedIdentity.SeedKeyForFunction("redis-seedkey");
        Assert.Equal(expectedKey, row.SeedKey);
        Assert.Equal(CronSeedIdentity.DeterministicId(expectedKey), row.Id);
    }

    [Fact]
    public async Task Migrate_LegacySeededRow_AdoptsSeedKeyInPlace_WithoutChangingId()
    {
        var legacy = CreateCronTicker(function: "redis-legacy");
        legacy.InitIdentifier = "MemoryTicker_Seeded_redis-legacy";
        SeedCronTicker(legacy);

        await _provider.MigrateDefinedCronTickers(
            [new DefinedCronTickerSeed("redis-legacy", "*/9 * * * *", 1, null)], CancellationToken.None);

        var rows = await _provider.GetCronTickers(c => c.Function == "redis-legacy", CancellationToken.None);
        var row = Assert.Single(rows);
        Assert.Equal(legacy.Id, row.Id); // adopted in place
        Assert.Equal(CronSeedIdentity.SeedKeyForFunction("redis-legacy"), row.SeedKey);
        Assert.Equal(CronExpression.Parse("*/9 * * * *").Value, row.Expression);
    }

    [Fact]
    public async Task Migrate_Repeated_ConvergesToSingleSeededRow()
    {
        var seed = new DefinedCronTickerSeed("redis-idem", "*/5 * * * *", 1, null);
        await _provider.MigrateDefinedCronTickers([seed], CancellationToken.None);
        await _provider.MigrateDefinedCronTickers([seed], CancellationToken.None);

        var rows = await _provider.GetCronTickers(c => c.Function == "redis-idem", CancellationToken.None);
        Assert.Single(rows);
    }

    [Fact]
    public async Task Migrate_DuplicateLegacySeededRows_DoesNotThrow_CanonicalAdoptsSeedKey()
    {
        var a = CreateCronTicker(function: "redis-dup");
        a.InitIdentifier = "MemoryTicker_Seeded_redis-dup";
        var b = CreateCronTicker(function: "redis-dup");
        b.InitIdentifier = "MemoryTicker_Seeded_redis-dup";
        SeedCronTicker(a);
        SeedCronTicker(b);

        // The former keyed ToDictionary(c => c.Function) threw on the duplicate; must be tolerant now.
        await _provider.MigrateDefinedCronTickers(
            [new DefinedCronTickerSeed("redis-dup", "*/5 * * * *", 1, null)], CancellationToken.None);

        var rows = await _provider.GetCronTickers(c => c.Function == "redis-dup", CancellationToken.None);
        Assert.Equal(2, rows.Length); // no destructive delete in Slice 2
        var seedKey = CronSeedIdentity.SeedKeyForFunction("redis-dup");
        Assert.Single(rows, r => r.SeedKey == seedKey);
        Assert.Single(rows, r => r.SeedKey == null);
    }

    // =========================================================================
    // Slice 3: two-phase non-destructive retirement (grace default 24h). Driven
    // at the fixed clock by pre-setting the markers a prior pass would write.
    // =========================================================================

    private CronTickerEntity SeededCron(string function)
    {
        var cron = CreateCronTicker(function: function);
        cron.InitIdentifier = $"MemoryTicker_Seeded_{function}";
        return cron;
    }

    private async Task<CronTickerEntity> SingleCron(string function)
        => Assert.Single(await _provider.GetCronTickers(c => c.Function == function, CancellationToken.None));

    [Fact]
    public async Task Migrate_RemovedSeed_EntersGraceWithoutDelete_AndKeepsOccurrence()
    {
        var cron = SeededCron("redis-grace-enter");
        SeedCronTicker(cron);
        SeedCronOccurrence(CreateCronOccurrence(cron.Id));

        await _provider.MigrateDefinedCronTickers(
            [new DefinedCronTickerSeed("redis-other", "*/5 * * * *", 1, null)], CancellationToken.None);

        var row = await SingleCron("redis-grace-enter");
        Assert.Equal(_fixedNow, row.RetirementRequestedAt);
        Assert.Null(row.RetiredAt);
        Assert.True(row.IsEnabled);
        var occ = await _provider.GetAllCronTickerOccurrences(o => o.CronTickerId == cron.Id, CancellationToken.None);
        Assert.Single(occ); // history preserved — index membership never removed
    }

    [Fact]
    public async Task Migrate_GraceExpired_DisablesAndRetires_WithoutDelete()
    {
        var cron = SeededCron("redis-grace-expired");
        cron.RetirementRequestedAt = _fixedNow.AddHours(-25);
        SeedCronTicker(cron);

        await _provider.MigrateDefinedCronTickers(
            [new DefinedCronTickerSeed("redis-other", "*/5 * * * *", 1, null)], CancellationToken.None);

        var row = await SingleCron("redis-grace-expired");
        Assert.False(row.IsEnabled);
        Assert.Equal(_fixedNow, row.RetiredAt);
        Assert.Equal(_fixedNow.AddHours(-25), row.RetirementRequestedAt);
        Assert.True(row.SeedWasEnabledBeforeRetirement);
    }

    [Fact]
    public async Task Migrate_RetirementTimestamp_StableAcrossRepeatedAbsentPasses()
    {
        var cron = SeededCron("redis-stable");
        cron.RetirementRequestedAt = _fixedNow.AddHours(-2);
        SeedCronTicker(cron);

        var absent = new[] { new DefinedCronTickerSeed("redis-other", "*/5 * * * *", 1, null) };
        await _provider.MigrateDefinedCronTickers(absent, CancellationToken.None);
        await _provider.MigrateDefinedCronTickers(absent, CancellationToken.None);

        var row = await SingleCron("redis-stable");
        Assert.Equal(_fixedNow.AddHours(-2), row.RetirementRequestedAt);
        Assert.Null(row.RetiredAt);
        Assert.True(row.IsEnabled);
    }

    [Fact]
    public async Task Migrate_ReappearanceBeforeGrace_CancelsRetirement()
    {
        var cron = SeededCron("redis-reappear-early");
        cron.SeedKey = CronSeedIdentity.SeedKeyForFunction("redis-reappear-early");
        cron.RetirementRequestedAt = _fixedNow.AddHours(-2);
        SeedCronTicker(cron);

        await _provider.MigrateDefinedCronTickers(
            [new DefinedCronTickerSeed("redis-reappear-early", "*/9 * * * *", 1, null)], CancellationToken.None);

        var row = await SingleCron("redis-reappear-early");
        Assert.Null(row.RetirementRequestedAt);
        Assert.Null(row.RetiredAt);
        Assert.True(row.IsEnabled);
        Assert.Equal(CronExpression.Parse("*/9 * * * *").Value, row.Expression);
    }

    [Fact]
    public async Task Migrate_ReappearanceAfterRetirement_RestoresFrameworkDisabled_PreservesUserDisabled()
    {
        var restore = SeededCron("redis-restore");
        restore.SeedKey = CronSeedIdentity.SeedKeyForFunction("redis-restore");
        restore.RetirementRequestedAt = _fixedNow.AddHours(-25);
        restore.RetiredAt = _fixedNow.AddHours(-25);
        restore.IsEnabled = false;
        restore.SeedWasEnabledBeforeRetirement = true;
        SeedCronTicker(restore);

        var keep = SeededCron("redis-keep-disabled");
        keep.SeedKey = CronSeedIdentity.SeedKeyForFunction("redis-keep-disabled");
        keep.RetirementRequestedAt = _fixedNow.AddHours(-25);
        keep.RetiredAt = _fixedNow.AddHours(-25);
        keep.IsEnabled = false;
        keep.SeedWasEnabledBeforeRetirement = false;
        SeedCronTicker(keep);

        await _provider.MigrateDefinedCronTickers(
        [
            new DefinedCronTickerSeed("redis-restore", "*/5 * * * *", 1, null),
            new DefinedCronTickerSeed("redis-keep-disabled", "*/5 * * * *", 1, null)
        ], CancellationToken.None);

        var restored = await SingleCron("redis-restore");
        Assert.True(restored.IsEnabled);
        Assert.Null(restored.RetiredAt);
        Assert.Null(restored.SeedWasEnabledBeforeRetirement);

        var kept = await SingleCron("redis-keep-disabled");
        Assert.False(kept.IsEnabled);
        Assert.Null(kept.RetiredAt);
        Assert.Null(kept.SeedWasEnabledBeforeRetirement);
    }

    [Fact]
    public async Task Migrate_BlockedSeed_RetiresImmediately_WithoutDelete()
    {
        var cron = SeededCron("redis-blocked");
        SeedCronTicker(cron);

        await _provider.MigrateDefinedCronTickers(
            [new DefinedCronTickerSeed("redis-blocked", "*/5 * * * *", 2, "sha256:req", canSeed: false)], CancellationToken.None);

        var row = await SingleCron("redis-blocked");
        Assert.False(row.IsEnabled);
        Assert.Equal(_fixedNow, row.RetirementRequestedAt);
        Assert.Equal(_fixedNow, row.RetiredAt);
        Assert.True(row.SeedWasEnabledBeforeRetirement);
    }

    [Fact]
    public async Task Migrate_DuplicateLegacyRows_LeaveExactlyOneEnabled_PreserveAll()
    {
        var a = SeededCron("redis-dup-retire");
        var b = SeededCron("redis-dup-retire");
        SeedCronTicker(a);
        SeedCronTicker(b);

        await _provider.MigrateDefinedCronTickers(
            [new DefinedCronTickerSeed("redis-dup-retire", "*/5 * * * *", 1, null)], CancellationToken.None);

        var rows = await _provider.GetCronTickers(c => c.Function == "redis-dup-retire", CancellationToken.None);
        Assert.Equal(2, rows.Length);
        var seedKey = CronSeedIdentity.SeedKeyForFunction("redis-dup-retire");
        var canonical = Assert.Single(rows, r => r.SeedKey == seedKey);
        Assert.True(canonical.IsEnabled);
        var redundant = Assert.Single(rows, r => r.SeedKey == null);
        Assert.False(redundant.IsEnabled);
        Assert.NotNull(redundant.RetiredAt);
    }

    // Finding 1: a keyed canonical row exists at a HIGHER id alongside a lower-id legacy null-key
    // duplicate. Canonical selection must keep the already-keyed row as the enabled owner (never move
    // the key onto the legacy row); the legacy duplicate is retired in place, still null-keyed.
    [Fact]
    public async Task Migrate_KeyedCanonicalWithLowerIdLegacyDuplicate_KeepsKeyedRowCanonical()
    {
        var seedKey = CronSeedIdentity.SeedKeyForFunction("redis-keyed-canon");

        var legacy = CreateCronTicker(
            id: new Guid("11111111-1111-1111-1111-111111111111"), function: "redis-keyed-canon");
        legacy.InitIdentifier = "MemoryTicker_Seeded_redis-keyed-canon"; // lower id, null SeedKey
        var keyed = CreateCronTicker(
            id: new Guid("11111111-1111-1111-1111-111111111112"), function: "redis-keyed-canon");
        keyed.InitIdentifier = "MemoryTicker_Seeded_redis-keyed-canon";
        keyed.SeedKey = seedKey; // higher id, already owns the key
        SeedCronTicker(legacy);
        SeedCronTicker(keyed);

        await _provider.MigrateDefinedCronTickers(
            [new DefinedCronTickerSeed("redis-keyed-canon", "*/5 * * * *", 1, null)], CancellationToken.None);

        var rows = await _provider.GetCronTickers(c => c.Function == "redis-keyed-canon", CancellationToken.None);
        Assert.Equal(2, rows.Length);

        var canonical = Assert.Single(rows, r => r.SeedKey == seedKey);
        Assert.Equal(keyed.Id, canonical.Id);
        Assert.True(canonical.IsEnabled);
        Assert.Null(canonical.RetiredAt);

        var redundant = Assert.Single(rows, r => r.Id == legacy.Id);
        Assert.Null(redundant.SeedKey);
        Assert.False(redundant.IsEnabled);
        Assert.NotNull(redundant.RetiredAt);

        Assert.Single(rows, r => r.IsEnabled);
    }

    [Fact]
    public async Task Migrate_UserDashboardRow_Untouched()
    {
        var user = CreateCronTicker(function: "redis-user");
        user.InitIdentifier = string.Empty; // user/dashboard row
        user.Expression = "0 0 * * *";
        SeedCronTicker(user);

        await _provider.MigrateDefinedCronTickers(
            [new DefinedCronTickerSeed("redis-other", "*/5 * * * *", 1, null)], CancellationToken.None);

        var row = await SingleCron("redis-user");
        Assert.Null(row.SeedKey);
        Assert.Null(row.RetirementRequestedAt);
        Assert.Null(row.RetiredAt);
        Assert.True(row.IsEnabled);
        Assert.Equal("0 0 * * *", row.Expression);
    }

    // =========================================================================
    // Slice 4: expression / contract reconciliation with pending-only cleanup and
    // authoritative current-definition refresh in the queue/acquire paths.
    // =========================================================================

    private void AssertOccurrenceQuarantined(Guid occId, Guid cronId)
    {
        var id = occId.ToString();
        var inIds = _sets.TryGetValue($"{Prefix}:co:ids", out var ids) && ids.Contains(id);
        var inByCron = _sets.TryGetValue($"{Prefix}:cron:{cronId}:occurrences", out var byCron) && byCron.Contains(id);
        var inPending = _sortedSets.TryGetValue($"{Prefix}:co:pending", out var pending) && pending.Values.Contains(id);
        var stored = VerifyInStore<CronTickerOccurrenceEntity<CronTickerEntity>>($"{Prefix}:co:{occId}");
        Assert.True(inIds);
        Assert.True(inByCron);
        Assert.False(inPending);
        Assert.NotNull(stored);
        Assert.Equal(TickerStatus.Skipped, stored!.Status);
        Assert.Equal(cronId, stored.CronTickerId);
        Assert.Contains("revision", stored.SkippedReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Migrate_ExpressionChange_PreservesResultBackedPendingHistory_AndDeindexesIt()
    {
        var cron = SeededCron("redis-expr-change");
        cron.SeedKey = CronSeedIdentity.SeedKeyForFunction("redis-expr-change");
        SeedCronTicker(cron);
        var pending = CreateCronOccurrence(cron.Id);
        SeedCronOccurrence(pending);
        _store[$"{Prefix}:co:{pending.Id}:result"] = "{}"; // durable history despite inconsistent pending status

        await _provider.MigrateDefinedCronTickers(
            [new DefinedCronTickerSeed("redis-expr-change", "*/9 * * * *", 1, null)], CancellationToken.None);

        var row = await SingleCron("redis-expr-change");
        Assert.Equal(CronExpression.Parse("*/9 * * * *").Value, row.Expression);
        Assert.True(_store.ContainsKey($"{Prefix}:co:{pending.Id}"));
        Assert.True(_store.ContainsKey($"{Prefix}:co:{pending.Id}:result"));
        Assert.Contains(pending.Id.ToString(), _sets[$"{Prefix}:co:ids"]);
        Assert.Contains(pending.Id.ToString(), _sets[$"{Prefix}:cron:{cron.Id}:occurrences"]);
        Assert.DoesNotContain(pending.Id.ToString(), _sortedSets[$"{Prefix}:co:pending"].Values);
    }

    [Fact]
    public async Task Migrate_ContractOnlyChange_QuarantinesUnleasedPending()
    {
        var cron = SeededCron("redis-contract-change");
        cron.SeedKey = CronSeedIdentity.SeedKeyForFunction("redis-contract-change");
        cron.RequestContractVersion = 1;
        cron.RequestContractFingerprint = "sha256:old";
        SeedCronTicker(cron);
        var pending = CreateCronOccurrence(cron.Id);
        SeedCronOccurrence(pending);

        await _provider.MigrateDefinedCronTickers(
            [new DefinedCronTickerSeed("redis-contract-change", "*/5 * * * *", 2, "sha256:new")], CancellationToken.None);

        AssertOccurrenceQuarantined(pending.Id, cron.Id);
    }

    [Fact]
    public async Task Migrate_PhaseBRetirement_QuarantinesUnleasedPending()
    {
        var cron = SeededCron("redis-retire-pending");
        cron.RetirementRequestedAt = _fixedNow.AddHours(-25);
        SeedCronTicker(cron);
        var pending = CreateCronOccurrence(cron.Id);
        SeedCronOccurrence(pending);

        await _provider.MigrateDefinedCronTickers(
            [new DefinedCronTickerSeed("redis-other", "*/5 * * * *", 1, null)], CancellationToken.None);

        var row = await SingleCron("redis-retire-pending");
        Assert.Equal(_fixedNow, row.RetiredAt);
        AssertOccurrenceQuarantined(pending.Id, cron.Id);
    }

    [Fact]
    public async Task Migrate_ExpressionChange_PreservesLeasedAndTerminalOccurrences()
    {
        var cron = SeededCron("redis-preserve");
        cron.SeedKey = CronSeedIdentity.SeedKeyForFunction("redis-preserve");
        SeedCronTicker(cron);

        var pending = CreateCronOccurrence(cron.Id);
        var leased = CreateCronOccurrence(cron.Id, status: TickerStatus.Queued, lockHolder: "owner-node");
        leased.AcquisitionToken = Guid.NewGuid();
        leased.LeaseUntil = _fixedNow.AddMinutes(5);
        var terminal = CreateCronOccurrence(cron.Id, status: TickerStatus.Done);
        terminal.ExecutedAt = _fixedNow.AddMinutes(-4);
        SeedCronOccurrence(pending);
        SeedCronOccurrence(leased);
        SeedCronOccurrence(terminal);

        await _provider.MigrateDefinedCronTickers(
            [new DefinedCronTickerSeed("redis-preserve", "*/9 * * * *", 1, null)], CancellationToken.None);

        AssertOccurrenceQuarantined(pending.Id, cron.Id);
        Assert.True(_store.ContainsKey($"{Prefix}:co:{leased.Id}"));
        Assert.True(_store.ContainsKey($"{Prefix}:co:{terminal.Id}"));
        Assert.Contains(leased.Id.ToString(), _sets[$"{Prefix}:cron:{cron.Id}:occurrences"]);
        Assert.Contains(terminal.Id.ToString(), _sets[$"{Prefix}:cron:{cron.Id}:occurrences"]);
    }

    // Point 7: acquisition/queue must refresh the authoritative current CronTicker document by
    // CronTickerId and never trust a stale embedded snapshot carried in the occurrence/context.
    [Fact]
    public async Task QueueCronTickerOccurrences_ReAcquire_UsesAuthoritativeDefinition_NotStaleEmbeddedSnapshot()
    {
        var cronId = Guid.NewGuid();
        var authoritative = CreateCronTicker(id: cronId, function: "auth-fn", expression: "*/authoritative * * * *");
        authoritative.RequestContractVersion = 9;
        authoritative.RequestContractFingerprint = "sha256:authoritative";
        authoritative.Retries = 7;
        SeedCronTicker(authoritative);

        // An existing occurrence whose EMBEDDED CronTicker snapshot is stale (old expression/contract).
        var occ = CreateCronOccurrence(cronId, status: TickerStatus.Idle);
        occ.CronTicker = new CronTickerEntity
        {
            Id = cronId, Function = "auth-fn", Expression = "*/STALE * * * *",
            RequestContractVersion = 1, RequestContractFingerprint = "sha256:stale", Retries = 1
        };
        SeedCronOccurrence(occ);

        var context = new InternalManagerContext(cronId)
        {
            FunctionName = "auth-fn",
            Expression = "*/CONTEXT * * * *",
            RequestContractVersion = 5,
            RequestContractFingerprint = "sha256:context",
            NextCronOccurrence = new NextCronOccurrence(occ.Id, occ.CreatedAt)
        };

        var results = await ToListAsync(_provider.QueueCronTickerOccurrences(
            (_fixedNow.AddMinutes(1), new[] { context }), CancellationToken.None));

        var queued = Assert.Single(results);
        Assert.NotNull(queued.CronTicker);
        Assert.Equal("*/authoritative * * * *", queued.CronTicker!.Expression);
        Assert.Equal(9, queued.CronTicker.RequestContractVersion);
        Assert.Equal("sha256:authoritative", queued.CronTicker.RequestContractFingerprint);
        Assert.Equal(7, queued.CronTicker.Retries);
    }

    [Fact]
    public async Task QueueCronTickerOccurrences_NewOccurrence_UsesAuthoritativeDefinition_NotStaleContext()
    {
        var cronId = Guid.NewGuid();
        var authoritative = CreateCronTicker(id: cronId, function: "auth-new-fn", expression: "*/authoritative * * * *");
        authoritative.RequestContractVersion = 9;
        authoritative.RequestContractFingerprint = "sha256:authoritative";
        SeedCronTicker(authoritative);

        var context = new InternalManagerContext(cronId)
        {
            FunctionName = "auth-new-fn",
            Expression = "*/CONTEXT * * * *",
            RequestContractVersion = 5,
            RequestContractFingerprint = "sha256:context",
            NextCronOccurrence = null
        };

        var results = await ToListAsync(_provider.QueueCronTickerOccurrences(
            (_fixedNow.AddMinutes(1), new[] { context }), CancellationToken.None));

        var queued = Assert.Single(results);
        Assert.Equal("*/authoritative * * * *", queued.CronTicker!.Expression);
        Assert.Equal(9, queued.CronTicker.RequestContractVersion);
        Assert.Equal("sha256:authoritative", queued.CronTicker.RequestContractFingerprint);
    }

    [Fact]
    public async Task AcquireImmediateCronOccurrences_RefreshesAuthoritativeDefinition_OverStaleEmbeddedSnapshot()
    {
        var cronId = Guid.NewGuid();
        var authoritative = CreateCronTicker(id: cronId, function: "auth-imm-fn", expression: "*/authoritative * * * *");
        authoritative.RequestContractVersion = 9;
        authoritative.RequestContractFingerprint = "sha256:authoritative";
        SeedCronTicker(authoritative);

        var occ = CreateCronOccurrence(cronId, status: TickerStatus.Idle);
        occ.CronTicker = new CronTickerEntity
        {
            Id = cronId, Function = "auth-imm-fn", Expression = "*/STALE * * * *",
            RequestContractVersion = 1, RequestContractFingerprint = "sha256:stale"
        };
        SeedCronOccurrence(occ);

        var acquired = await _provider.AcquireImmediateCronOccurrencesAsync([occ.Id], CancellationToken.None);

        var one = Assert.Single(acquired);
        Assert.Equal("*/authoritative * * * *", one.CronTicker!.Expression);
        Assert.Equal(9, one.CronTicker.RequestContractVersion);
        Assert.Equal("sha256:authoritative", one.CronTicker.RequestContractFingerprint);
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
        Assert.StartsWith(NodeId + ":", results[0].LockHolder);

        using var doc = JsonDocument.Parse(_store[$"{Prefix}:tt:{ticker.Id}"]);
        Assert.Equal((int)TickerStatus.Queued, doc.RootElement.GetProperty("Status").GetInt32());
        Assert.StartsWith(NodeId + ":", doc.RootElement.GetProperty("LockHolder").GetString());
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
        Assert.StartsWith(NodeId + ":", doc.RootElement.GetProperty("LockHolder").GetString());
    }

    [Fact]
    public async Task AcquireImmediateTimeTickersAsync_EmptyIds_ReturnsEmpty()
    {
        var results = await _provider.AcquireImmediateTimeTickersAsync([], CancellationToken.None);
        Assert.Empty(results);
    }

    [Fact]
    public async Task AcquireTimeTickerOnDemandAsync_AtomicallyRevivesTerminalTicker()
    {
        var ticker = CreateTimeTicker(status: TickerStatus.Done, lockHolder: null, lockedAt: null);
        ticker.ExecutedAt = _fixedNow.AddMinutes(-1);
        ticker.ElapsedTime = 123;
        ticker.StaleRestartCount = 3;
        SeedTimeTicker(ticker);

        var acquired = await _provider.AcquireTimeTickerOnDemandAsync(
            ticker.Id, _fixedNow, CancellationToken.None);

        var raw = _store[$"{Prefix}:tt:{ticker.Id}"];
        using (var doc = JsonDocument.Parse(raw))
            Assert.NotEqual(JsonValueKind.Null, doc.RootElement.GetProperty("LeaseUntil").ValueKind);
        Assert.NotNull(JsonSerializer.Deserialize<TimeTickerEntity>(raw, _jsonOptions)!.LeaseUntil);

        Assert.NotNull(acquired);
        Assert.Equal(TickerStatus.InProgress, acquired.Status);
        Assert.NotNull(acquired.AcquisitionToken);
        Assert.NotNull(acquired.LeaseUntil);
        Assert.StartsWith(NodeId + ":", acquired.LockHolder);
        Assert.Null(acquired.ExecutedAt);
        Assert.Equal(0, acquired.ElapsedTime);
        Assert.Equal(0, acquired.StaleRestartCount);
        Assert.Null(await _provider.AcquireTimeTickerOnDemandAsync(
            ticker.Id, _fixedNow, CancellationToken.None));
    }

    [Fact]
    public async Task AcquireTimeTickerOnDemandAsync_DoesNotStealOtherOwnersQueuedGeneration()
    {
        var ticker = CreateTimeTicker(status: TickerStatus.Queued, lockHolder: "other-owner", lockedAt: _fixedNow);
        ticker.AcquisitionToken = Guid.NewGuid();
        SeedTimeTicker(ticker);

        Assert.Null(await _provider.AcquireTimeTickerOnDemandAsync(
            ticker.Id, _fixedNow, CancellationToken.None));

        var stored = VerifyInStore<TimeTickerEntity>($"{Prefix}:tt:{ticker.Id}");
        Assert.Equal("other-owner", stored!.LockHolder);
        Assert.Equal(ticker.AcquisitionToken, stored.AcquisitionToken);
    }

    [Fact]
    public async Task UpdateTimeTicker_TerminalWriteIsFencedByGeneration()
    {
        var ticker = CreateTimeTicker(status: TickerStatus.Idle, lockHolder: null, lockedAt: null);
        SeedTimeTicker(ticker);
        var acquired = Assert.Single(await _provider.AcquireImmediateTimeTickersAsync(
            [ticker.Id], CancellationToken.None));

        var stale = new InternalFunctionContext()
            .SetProperty(x => x.TickerId, ticker.Id)
            .SetProperty(x => x.Type, TickerType.TimeTicker)
            .SetProperty(x => x.AcquisitionToken, Guid.NewGuid())
            .SetProperty(x => x.Status, TickerStatus.Done);
        Assert.Equal(0, await _provider.UpdateTimeTicker(stale, CancellationToken.None));

        var winning = new InternalFunctionContext()
            .SetProperty(x => x.TickerId, ticker.Id)
            .SetProperty(x => x.Type, TickerType.TimeTicker)
            .SetProperty(x => x.AcquisitionToken, acquired.AcquisitionToken)
            .SetProperty(x => x.Status, TickerStatus.Done);
        Assert.Equal(1, await _provider.UpdateTimeTicker(winning, CancellationToken.None));

        var persisted = VerifyInStore<TimeTickerEntity>($"{Prefix}:tt:{ticker.Id}");
        Assert.Equal(TickerStatus.Done, persisted!.Status);
        Assert.Null(persisted.AcquisitionToken);
        Assert.Null(persisted.LeaseUntil);
    }

    [Fact]
    public async Task LeaseRenewalAndHeldCheck_AreFencedByGeneration()
    {
        var ticker = CreateTimeTicker(status: TickerStatus.Idle, lockHolder: null, lockedAt: null);
        SeedTimeTicker(ticker);
        var acquired = Assert.Single(await _provider.AcquireImmediateTimeTickersAsync(
            [ticker.Id], CancellationToken.None));
        var winningLease = new AcquisitionLease(ticker.Id, acquired.AcquisitionToken);
        var staleLease = new AcquisitionLease(ticker.Id, Guid.NewGuid());
        var renewedUntil = _fixedNow.AddMinutes(2);

        Assert.Equal(1, await _provider.RenewTimeTickerLeases(
            [winningLease], renewedUntil, CancellationToken.None));
        Assert.Equal(0, await _provider.RenewTimeTickerLeases(
            [staleLease], renewedUntil.AddMinutes(1), CancellationToken.None));
        Assert.Equal([ticker.Id], await _provider.GetStillHeldTickerIds(
            [winningLease], [], CancellationToken.None));
        Assert.Empty(await _provider.GetStillHeldTickerIds(
            [staleLease], [], CancellationToken.None));

        var persisted = VerifyInStore<TimeTickerEntity>($"{Prefix}:tt:{ticker.Id}");
        Assert.Equal(renewedUntil, persisted!.LeaseUntil);
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
        var ticker = CreateTimeTicker(status: TickerStatus.Idle, lockHolder: null, lockedAt: null);
        SeedTimeTicker(ticker);
        Assert.Single(await _provider.AcquireImmediateTimeTickersAsync([ticker.Id], CancellationToken.None));
        var tickerKey = $"{Prefix}:tt:{ticker.Id}";
        _store[tickerKey] = SetJsonProperties(_store[tickerKey], new() { ["Status"] = (int)TickerStatus.Queued });

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
    // Request-contract identity must survive the queue projection (MapQueueNode /
    // MapForQueue) on every time path and GetAllCronTickerExpressions on the cron
    // path. If it does not, TickerExecutionTaskHandler treats the row as legacy and
    // skips drift enforcement.
    // =========================================================================

    private const int RedisIdentityVersion = 77;
    private const string RedisIdentityFingerprint = "sha256:redis-identity";

    private TimeTickerEntity CreateIdentityTimeTicker(
        DateTime? executionTime = null, TickerStatus status = TickerStatus.Idle)
    {
        var ticker = CreateTimeTicker(executionTime: executionTime, status: status, lockHolder: null, lockedAt: null);
        ticker.RequestContractVersion = RedisIdentityVersion;
        ticker.RequestContractFingerprint = RedisIdentityFingerprint;
        return ticker;
    }

    private static void AssertTimeIdentity(TimeTickerEntity ticker)
    {
        Assert.Equal(RedisIdentityVersion, ticker.RequestContractVersion);
        Assert.Equal(RedisIdentityFingerprint, ticker.RequestContractFingerprint);
    }

    [Fact]
    public async Task AcquireImmediateTimeTickersAsync_PreservesContractIdentity()
    {
        var ticker = CreateIdentityTimeTicker(status: TickerStatus.Idle);
        SeedTimeTicker(ticker);

        var results = await _provider.AcquireImmediateTimeTickersAsync([ticker.Id], CancellationToken.None);

        AssertTimeIdentity(Assert.Single(results));
    }

    [Fact]
    public async Task GetEarliestTimeTickers_PreservesContractIdentity()
    {
        var ticker = CreateIdentityTimeTicker(executionTime: _fixedNow.AddMilliseconds(500), status: TickerStatus.Idle);
        SeedTimeTicker(ticker);

        var results = await _provider.GetEarliestTimeTickers(CancellationToken.None);

        AssertTimeIdentity(Assert.Single(results.Where(t => t.Id == ticker.Id)));
    }

    [Fact]
    public async Task QueueTimedOutTimeTickers_PreservesContractIdentity()
    {
        // Older than the fallback threshold (now - 100ms) so the timed-out path picks it up.
        var ticker = CreateIdentityTimeTicker(executionTime: _fixedNow.AddSeconds(-5), status: TickerStatus.Idle);
        SeedTimeTicker(ticker);

        var results = await ToListAsync(_provider.QueueTimedOutTimeTickers(CancellationToken.None));

        AssertTimeIdentity(Assert.Single(results.Where(t => t.Id == ticker.Id)));
    }

    [Fact]
    public async Task AcquireTimeTickerOnDemandAsync_PreservesContractIdentity()
    {
        var ticker = CreateIdentityTimeTicker(status: TickerStatus.Done);
        ticker.ExecutedAt = _fixedNow.AddMinutes(-1);
        SeedTimeTicker(ticker);

        var acquired = await _provider.AcquireTimeTickerOnDemandAsync(
            ticker.Id, _fixedNow, CancellationToken.None);

        Assert.NotNull(acquired);
        AssertTimeIdentity(acquired);
    }

    [Fact]
    public async Task GetAllCronTickerExpressions_PreservesContractIdentity()
    {
        var cron = CreateCronTicker();
        cron.RequestContractVersion = 88;
        cron.RequestContractFingerprint = "sha256:cron-identity";
        SeedCronTicker(cron);

        var results = await _provider.GetAllCronTickerExpressions(CancellationToken.None);

        var projected = Assert.Single(results.Where(c => c.Id == cron.Id));
        Assert.Equal(88, projected.RequestContractVersion);
        Assert.Equal("sha256:cron-identity", projected.RequestContractFingerprint);
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
        Assert.StartsWith(NodeId + ":", stored.LockHolder);
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

        var occ = CreateCronOccurrence(cron.Id, status: TickerStatus.Idle, lockHolder: null, lockedAt: null);
        SeedCronOccurrence(occ);
        Assert.Single(await _provider.AcquireImmediateCronOccurrencesAsync([occ.Id], CancellationToken.None));
        var occurrenceKey = $"{Prefix}:co:{occ.Id}";
        _store[occurrenceKey] = SetJsonProperties(_store[occurrenceKey], new() { ["Status"] = (int)TickerStatus.Queued });

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
