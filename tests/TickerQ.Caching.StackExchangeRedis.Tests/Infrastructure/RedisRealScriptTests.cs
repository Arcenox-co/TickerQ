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
