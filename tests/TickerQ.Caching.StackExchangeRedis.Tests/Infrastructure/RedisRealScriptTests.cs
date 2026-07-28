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
    public RedisContainer Container { get; } = new RedisBuilder().WithImage("redis:7").Build();
    public IConnectionMultiplexer Mux { get; private set; } = null!;
    public IDatabase Db { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await Container.StartAsync();
        Mux = await ConnectionMultiplexer.ConnectAsync(Container.GetConnectionString());
        Db = Mux.GetDatabase();
    }

    public async Task DisposeAsync()
    {
        if (Mux is not null)
            await Mux.DisposeAsync();
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
