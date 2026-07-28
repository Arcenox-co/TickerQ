using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using TickerQ.EntityFrameworkCore.Entities;
using TickerQ.EntityFrameworkCore.Infrastructure;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ.EntityFrameworkCore.Tests.Infrastructure;

public sealed class EfCoreParentResultPersistenceTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private DbContextOptions<TestTickerQDbContext> _options = null!;
    private TestableProvider _provider = null!;
    private TestTickerQDbContext _seed = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();
        _options = new DbContextOptionsBuilder<TestTickerQDbContext>().UseSqlite(_connection).Options;
        _seed = new TestTickerQDbContext(_options);
        await _seed.Database.EnsureCreatedAsync();

        var clock = Substitute.For<ITickerClock>();
        clock.UtcNow.Returns(new DateTime(2026, 7, 28, 12, 0, 0, DateTimeKind.Utc));
        var redis = Substitute.For<ITickerQRedisContext>();
        redis.HasRedisConnection.Returns(false);
        var services = new ServiceCollection();
        services.AddSingleton<IDbContextFactory<TestTickerQDbContext>>(
            new PooledDbContextFactory<TestTickerQDbContext>(_options));
        _provider = new TestableProvider(
            services.BuildServiceProvider(), clock,
            new SchedulerOptionsBuilder { NodeIdentifier = "result-node" }, redis);
    }

    public async Task DisposeAsync()
    {
        await _seed.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public void Provider_AdvertisesResultPublication_AndModelUsesAdditiveCascadeTables()
    {
        Assert.True(_provider.SupportsResultPublication);
        var time = _seed.Model.FindEntityType(typeof(TimeTickerResultEntity<TimeTickerEntity>));
        var cron = _seed.Model.FindEntityType(typeof(CronTickerOccurrenceResultEntity<CronTickerEntity>));
        Assert.Equal("TimeTickerResults", time!.GetTableName());
        Assert.Equal("CronTickerOccurrenceResults", cron!.GetTableName());
        Assert.Equal(DeleteBehavior.Cascade, Assert.Single(time.GetForeignKeys()).DeleteBehavior);
        Assert.Equal(DeleteBehavior.Cascade, Assert.Single(cron.GetForeignKeys()).DeleteBehavior);
        Assert.Equal(1024 * 1024, time.FindProperty(nameof(TimeTickerResultEntity<TimeTickerEntity>.Payload))!.GetMaxLength());
    }

    [Fact]
    public async Task SuccessfulRootCommit_PersistsImmutableEnvelope_WithAllMetadata()
    {
        var ticker = NewTimeTicker();
        await SeedAsync(ticker);
        var acquired = Assert.Single(await _provider.AcquireImmediateTimeTickersAsync([ticker.Id]));
        var source = new byte[] { 1, 2, 3 };
        var envelope = new TickerResultEnvelope(source, 1, "application/json", "sha256:contract", "Example.Result");

        Assert.True(await _provider.CommitSuccessfulTickerAsync(Success(ticker.Id, acquired.AcquisitionToken, envelope)));
        source[0] = 99;
        var first = await _provider.GetTimeTickerResultAsync(ticker.Id);
        var returned = first!.ToPayloadArray();
        returned[1] = 88;
        var second = await _provider.GetTimeTickerResultAsync(ticker.Id);

        Assert.Equal(new byte[] { 1, 2, 3 }, first.ToPayloadArray());
        Assert.Equal(new byte[] { 1, 2, 3 }, second!.ToPayloadArray());
        Assert.Equal("application/json", second.MediaType);
        Assert.Equal("sha256:contract", second.ContractId);
        Assert.Equal("Example.Result", second.ContractType);
    }

    [Fact]
    public async Task DirectParentReads_AreIsolatedAcrossRootChildAndGrandchild()
    {
        var root = NewTimeTicker();
        var child = NewTimeTicker(parentId: root.Id);
        var grandchild = NewTimeTicker(parentId: child.Id);
        var generation = Guid.NewGuid();
        root.ChainRootId = root.Id;
        root.ChainGeneration = generation;
        child.ChainRootId = root.Id;
        child.ChainGeneration = generation;
        grandchild.ChainRootId = root.Id;
        grandchild.ChainGeneration = generation;
        await SeedAsync(root, child, grandchild);

        var success = Success(child.Id, null, Envelope("child"), child.ParentId);
        success.ChainRootId = root.Id;
        success.ChainGeneration = generation;
        Assert.True(await _provider.CommitSuccessfulTickerAsync(success));

        Assert.Null(await _provider.GetTimeTickerResultAsync(root.Id));
        Assert.Equal("child", System.Text.Encoding.UTF8.GetString(
            (await _provider.GetTimeTickerResultAsync(child.Id))!.Payload.Span));
        Assert.Null(await _provider.GetTimeTickerResultAsync(grandchild.Id));
    }

    [Fact]
    public async Task StaleRootToken_IsNotAcknowledged_AndPublishesNothing()
    {
        var ticker = NewTimeTicker();
        await SeedAsync(ticker);
        _ = Assert.Single(await _provider.AcquireImmediateTimeTickersAsync([ticker.Id]));

        Assert.False(await _provider.CommitSuccessfulTickerAsync(
            Success(ticker.Id, Guid.NewGuid(), Envelope("stale"))));
        Assert.Null(await _provider.GetTimeTickerResultAsync(ticker.Id));
        await using var verify = new TestTickerQDbContext(_options);
        Assert.Equal(TickerStatus.InProgress,
            (await verify.Set<TimeTickerEntity>().SingleAsync(x => x.Id == ticker.Id)).Status);
    }

    [Fact]
    public async Task SuccessfulRerunWithoutResult_ClearsPriorEnvelope()
    {
        var ticker = NewTimeTicker();
        await SeedAsync(ticker);
        var first = Assert.Single(await _provider.AcquireImmediateTimeTickersAsync([ticker.Id]));
        Assert.True(await _provider.CommitSuccessfulTickerAsync(Success(ticker.Id, first.AcquisitionToken, Envelope("old"))));
        var rerun = await _provider.AcquireTimeTickerOnDemandAsync(ticker.Id, DateTime.UtcNow);

        Assert.True(await _provider.CommitSuccessfulTickerAsync(Success(ticker.Id, rerun!.AcquisitionToken, null)));
        Assert.Null(await _provider.GetTimeTickerResultAsync(ticker.Id));
    }

    [Fact]
    public async Task OversizedOrUnknownResult_FailsClosed_AndRollsBackTerminalStatus()
    {
        var ticker = NewTimeTicker();
        await SeedAsync(ticker);
        var acquired = Assert.Single(await _provider.AcquireImmediateTimeTickersAsync([ticker.Id]));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _provider.CommitSuccessfulTickerAsync(
            Success(ticker.Id, acquired.AcquisitionToken, new TickerResultEnvelope(new byte[1024 * 1024 + 1], 1, "application/json"))));
        await Assert.ThrowsAsync<NotSupportedException>(() => _provider.CommitSuccessfulTickerAsync(
            Success(ticker.Id, acquired.AcquisitionToken, new TickerResultEnvelope([1], 2, "application/json"))));

        await using var verify = new TestTickerQDbContext(_options);
        Assert.Equal(TickerStatus.InProgress,
            (await verify.Set<TimeTickerEntity>().SingleAsync(x => x.Id == ticker.Id)).Status);
        Assert.Empty(await verify.Set<TimeTickerResultEntity<TimeTickerEntity>>().ToListAsync());
    }

    [Fact]
    public async Task ExactlyOneMiBResult_IsAccepted()
    {
        var ticker = NewTimeTicker();
        await SeedAsync(ticker);
        var acquired = Assert.Single(await _provider.AcquireImmediateTimeTickersAsync([ticker.Id]));
        Assert.True(await _provider.CommitSuccessfulTickerAsync(Success(
            ticker.Id, acquired.AcquisitionToken,
            new TickerResultEnvelope(new byte[1024 * 1024], 1, "application/octet-stream"))));
        var stored = await _provider.GetTimeTickerResultAsync(ticker.Id);
        Assert.Equal(1024 * 1024, stored!.PayloadLength);
        Assert.Null(stored.ContractId);
        Assert.Null(stored.ContractType);
    }

    [Fact]
    public async Task ResultWriteFailure_RollsBackAcceptedTerminalStatus()
    {
        var ticker = NewTimeTicker();
        await SeedAsync(ticker);
        var faulting = CreateFaultingProvider();
        var acquired = Assert.Single(await faulting.AcquireImmediateTimeTickersAsync([ticker.Id]));

        await Assert.ThrowsAsync<InjectedResultFailureException>(() => faulting.CommitSuccessfulTickerAsync(
            Success(ticker.Id, acquired.AcquisitionToken, Envelope("will-rollback"))));

        await using var verify = new TestTickerQDbContext(_options);
        Assert.Equal(TickerStatus.InProgress,
            (await verify.Set<TimeTickerEntity>().SingleAsync(x => x.Id == ticker.Id)).Status);
        Assert.Empty(await verify.Set<TimeTickerResultEntity<TimeTickerEntity>>().ToListAsync());
    }

    [Fact]
    public async Task FailedAttemptCannotPublish_AndDoesNotLeakIntoLaterSuccessWithoutResult()
    {
        var ticker = NewTimeTicker();
        await SeedAsync(ticker);
        var acquired = Assert.Single(await _provider.AcquireImmediateTimeTickersAsync([ticker.Id]));
        var failed = new InternalFunctionContext
        {
            TickerId = ticker.Id, Type = TickerType.TimeTicker, AcquisitionToken = acquired.AcquisitionToken
        }.SetProperty(x => x.Status, TickerStatus.Failed)
         .SetProperty(x => x.ResultEnvelope, Envelope("failed-attempt"));

        Assert.Equal(1, await _provider.UpdateTimeTicker(failed, CancellationToken.None));
        Assert.Null(await _provider.GetTimeTickerResultAsync(ticker.Id));
        var rerun = await _provider.AcquireTimeTickerOnDemandAsync(ticker.Id, DateTime.UtcNow);
        Assert.True(await _provider.CommitSuccessfulTickerAsync(Success(ticker.Id, rerun!.AcquisitionToken, null)));
        Assert.Null(await _provider.GetTimeTickerResultAsync(ticker.Id));
    }

    [Fact]
    public async Task CorruptPersistedMetadata_FailsClosedOnRead()
    {
        var ticker = NewTimeTicker();
        await SeedAsync(ticker);
        _seed.Set<TimeTickerResultEntity<TimeTickerEntity>>().Add(new()
        {
            TickerId = ticker.Id, Payload = [1], EnvelopeVersion = 999, MediaType = "application/json"
        });
        await _seed.SaveChangesAsync();

        await Assert.ThrowsAsync<NotSupportedException>(() => _provider.GetTimeTickerResultAsync(ticker.Id));
    }

    [Fact]
    public async Task CronOccurrenceResultPersists_ButCronDefinitionHasNoResultRow()
    {
        var cron = new CronTickerEntity
        {
            Id = Guid.NewGuid(), Function = "Cron", Expression = "* * * * *",
            Request = [], CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        var occurrence = new CronTickerOccurrenceEntity<CronTickerEntity>
        {
            Id = Guid.NewGuid(), CronTickerId = cron.Id, Status = TickerStatus.Idle,
            ExecutionTime = DateTime.UtcNow, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        _seed.Add(cron); _seed.Add(occurrence); await _seed.SaveChangesAsync(); _seed.ChangeTracker.Clear();
        var acquired = Assert.Single(await _provider.AcquireImmediateCronOccurrencesAsync([occurrence.Id]));
        var completion = Success(occurrence.Id, acquired.AcquisitionToken, Envelope("cron"));
        completion.Type = TickerType.CronTickerOccurrence;

        Assert.True(await _provider.CommitSuccessfulTickerAsync(completion));
        Assert.Equal("cron", System.Text.Encoding.UTF8.GetString(
            (await _provider.GetCronTickerOccurrenceResultAsync(occurrence.Id))!.Payload.Span));
        Assert.Null(await _provider.GetTimeTickerResultAsync(cron.Id));
    }

    [Fact]
    public async Task RecursiveDelete_CascadesEveryResultRow()
    {
        var root = NewTimeTicker();
        var child = NewTimeTicker(parentId: root.Id);
        await SeedAsync(root, child);
        _seed.AddRange(
            Result(root.Id, "root"), Result(child.Id, "child"));
        await _seed.SaveChangesAsync(); _seed.ChangeTracker.Clear();

        Assert.Equal(2, await _provider.RemoveTimeTickers([root.Id], CancellationToken.None));
        await using var verify = new TestTickerQDbContext(_options);
        Assert.Empty(await verify.Set<TimeTickerResultEntity<TimeTickerEntity>>().ToListAsync());
    }

    [Fact]
    public async Task RetentionDelete_CascadesResultRow()
    {
        var ticker = NewTimeTicker();
        ticker.Status = TickerStatus.Done;
        ticker.ExecutionTime = DateTime.UtcNow.AddDays(-10);
        ticker.ExecutedAt = DateTime.UtcNow.AddDays(-9);
        ticker.AcquisitionToken = null;
        ticker.LeaseUntil = null;
        await SeedAsync(ticker);
        _seed.Add(Result(ticker.Id, "expired"));
        await _seed.SaveChangesAsync();
        _seed.ChangeTracker.Clear();

        var deleted = await _provider.DeleteEligibleTimeTickerChainsAsync(
            new RetentionCutoffs(DateTime.UtcNow.AddDays(-1), null, null, null),
            10, RetentionCursor.Start, CancellationToken.None);

        Assert.Equal(1, deleted.Deleted);
        await using var verify = new TestTickerQDbContext(_options);
        Assert.Empty(await verify.Set<TimeTickerResultEntity<TimeTickerEntity>>().ToListAsync());
    }

    private FaultingResultProvider CreateFaultingProvider()
    {
        var clock = Substitute.For<ITickerClock>();
        clock.UtcNow.Returns(new DateTime(2026, 7, 28, 12, 0, 0, DateTimeKind.Utc));
        var redis = Substitute.For<ITickerQRedisContext>();
        redis.HasRedisConnection.Returns(false);
        var services = new ServiceCollection();
        services.AddSingleton<IDbContextFactory<TestTickerQDbContext>>(
            new PooledDbContextFactory<TestTickerQDbContext>(_options));
        return new FaultingResultProvider(services.BuildServiceProvider(), clock,
            new SchedulerOptionsBuilder { NodeIdentifier = "fault-result-node" }, redis);
    }

    private async Task SeedAsync(params TimeTickerEntity[] rows)
    {
        _seed.AddRange(rows); await _seed.SaveChangesAsync(); _seed.ChangeTracker.Clear();
    }

    private static TimeTickerEntity NewTimeTicker(Guid? parentId = null) => new()
    {
        Id = Guid.NewGuid(), ParentId = parentId, Function = "ResultFunction", Status = TickerStatus.Idle,
        ExecutionTime = parentId.HasValue ? null : DateTime.UtcNow, Request = [],
        CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
    };

    private static InternalFunctionContext Success(Guid id, Guid? token, TickerResultEnvelope? result, Guid? parentId = null)
        => new InternalFunctionContext
        {
            TickerId = id, Type = TickerType.TimeTicker, ParentId = parentId, AcquisitionToken = token
        }.SetProperty(x => x.Status, TickerStatus.Done)
         .SetProperty(x => x.ExecutedAt, DateTime.UtcNow)
         .SetProperty(x => x.ReleaseLock, true)
         .SetProperty(x => x.ResultEnvelope, result);

    private static TickerResultEnvelope Envelope(string value)
        => new(System.Text.Encoding.UTF8.GetBytes(value), 1, "application/json");

    private static TimeTickerResultEntity<TimeTickerEntity> Result(Guid id, string value) => new()
    {
        TickerId = id, Payload = System.Text.Encoding.UTF8.GetBytes(value), EnvelopeVersion = 1,
        MediaType = "application/json"
    };
}

internal sealed class FaultingResultProvider : TestableProvider
{
    public FaultingResultProvider(IServiceProvider serviceProvider, ITickerClock clock,
        SchedulerOptionsBuilder optionsBuilder, ITickerQRedisContext redisContext)
        : base(serviceProvider, clock, optionsBuilder, redisContext) { }

    protected internal override Task OnSuccessfulStatusWrittenForTestAsync(
        TestTickerQDbContext dbContext, InternalFunctionContext functionContext,
        CancellationToken cancellationToken)
        => throw new InjectedResultFailureException();
}

internal sealed class InjectedResultFailureException : Exception;