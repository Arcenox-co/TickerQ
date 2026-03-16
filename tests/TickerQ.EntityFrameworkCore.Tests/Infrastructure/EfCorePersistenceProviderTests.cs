using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

using NSubstitute;

using TickerQ.EntityFrameworkCore.Configurations;
using TickerQ.EntityFrameworkCore.Infrastructure;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ.EntityFrameworkCore.Tests.Infrastructure;

#region Test Helpers

/// <summary>
/// Test DbContext that bypasses the TickerQEfCoreOptionBuilder service resolution
/// by directly applying entity configurations in OnModelCreating.
/// </summary>
public class TestTickerQDbContext : DbContext
{
    public TestTickerQDbContext(DbContextOptions<TestTickerQDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new TimeTickerConfigurations<TimeTickerEntity>("ticker"));
        modelBuilder.ApplyConfiguration(new CronTickerConfigurations<CronTickerEntity>("ticker"));
        modelBuilder.ApplyConfiguration(new CronTickerOccurrenceConfigurations<CronTickerEntity>("ticker"));
        base.OnModelCreating(modelBuilder);
    }
}

/// <summary>
/// Concrete subclass that exposes TickerEfCorePersistenceProvider for testing.
/// This is needed because TickerEfCorePersistenceProvider is internal.
/// </summary>
internal class TestableProvider : TickerEfCorePersistenceProvider<TestTickerQDbContext, TimeTickerEntity, CronTickerEntity>
{
    public TestableProvider(
        IServiceProvider serviceProvider,
        ITickerClock clock,
        SchedulerOptionsBuilder optionsBuilder,
        ITickerQRedisContext redisContext)
        : base(serviceProvider, clock, optionsBuilder, redisContext)
    {
    }
}

#endregion

public class EfCorePersistenceProviderTests : IAsyncLifetime
{
    private SqliteConnection _connection;
    private TestTickerQDbContext _seedContext;
    private DbContextOptions<TestTickerQDbContext> _options;
    private TestableProvider _provider;
    private ITickerClock _clock;
    private ITickerQRedisContext _redisContext;
    private const string NodeId = "test-node-1";
    private DateTime _fixedNow;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();

        _fixedNow = new DateTime(2025, 6, 15, 12, 0, 0, DateTimeKind.Utc);

        _clock = Substitute.For<ITickerClock>();
        _clock.UtcNow.Returns(_fixedNow);

        _redisContext = Substitute.For<ITickerQRedisContext>();
        _redisContext.HasRedisConnection.Returns(false);
        // Make GetOrSetArrayAsync call the factory directly (bypass cache)
        _redisContext.GetOrSetArrayAsync(
            Arg.Any<string>(),
            Arg.Any<Func<CancellationToken, Task<CronTickerEntity[]>>>(),
            Arg.Any<TimeSpan?>(),
            Arg.Any<CancellationToken>()
        ).Returns(callInfo =>
        {
            var factory = callInfo.ArgAt<Func<CancellationToken, Task<CronTickerEntity[]>>>(1);
            return factory(CancellationToken.None);
        });

        _options = new DbContextOptionsBuilder<TestTickerQDbContext>()
            .UseSqlite(_connection)
            .Options;

        _seedContext = new TestTickerQDbContext(_options);
        await _seedContext.Database.EnsureCreatedAsync();

        var schedulerOptions = new SchedulerOptionsBuilder { NodeIdentifier = NodeId };

        var services = new ServiceCollection();
        services.AddSingleton<IDbContextFactory<TestTickerQDbContext>>(
            new PooledDbContextFactory<TestTickerQDbContext>(_options));
        var serviceProvider = services.BuildServiceProvider();

        _provider = new TestableProvider(serviceProvider, _clock, schedulerOptions, _redisContext);
    }

    public async Task DisposeAsync()
    {
        await _seedContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private TestTickerQDbContext CreateVerifyContext() => new(_options);

    #region Helper Methods

    private TimeTickerEntity CreateTimeTicker(
        Guid? id = null,
        DateTime? executionTime = null,
        TickerStatus status = TickerStatus.Idle,
        string function = "TestFunction",
        string lockHolder = null,
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
            Request = Array.Empty<byte>()
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
            Request = Array.Empty<byte>()
        };
    }

    private CronTickerOccurrenceEntity<CronTickerEntity> CreateCronOccurrence(
        Guid cronTickerId,
        Guid? id = null,
        DateTime? executionTime = null,
        TickerStatus status = TickerStatus.Idle,
        string lockHolder = null,
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

    private async Task SeedTimeTickers(params TimeTickerEntity[] tickers)
    {
        _seedContext.Set<TimeTickerEntity>().AddRange(tickers);
        await _seedContext.SaveChangesAsync();
        DetachAll();
    }

    private async Task SeedCronTickers(params CronTickerEntity[] tickers)
    {
        _seedContext.Set<CronTickerEntity>().AddRange(tickers);
        await _seedContext.SaveChangesAsync();
        DetachAll();
    }

    private async Task SeedCronOccurrences(params CronTickerOccurrenceEntity<CronTickerEntity>[] occurrences)
    {
        _seedContext.Set<CronTickerOccurrenceEntity<CronTickerEntity>>().AddRange(occurrences);
        await _seedContext.SaveChangesAsync();
        DetachAll();
    }

    private void DetachAll()
    {
        foreach (var entry in _seedContext.ChangeTracker.Entries().ToList())
            entry.State = EntityState.Detached;
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
    public async Task AddTimeTickers_InsertsAndVerifiesInDb()
    {
        var ticker1 = CreateTimeTicker();
        var ticker2 = CreateTimeTicker();

        var result = await _provider.AddTimeTickers(new[] { ticker1, ticker2 }, CancellationToken.None);

        Assert.Equal(2, result);

        using var ctx = CreateVerifyContext();
        var allTickers = await ctx.Set<TimeTickerEntity>().AsNoTracking().ToListAsync();
        Assert.Equal(2, allTickers.Count);
        Assert.Contains(allTickers, t => t.Id == ticker1.Id);
        Assert.Contains(allTickers, t => t.Id == ticker2.Id);
    }

    // =========================================================================
    // 2. UpdateTimeTickers
    // =========================================================================

    [Fact]
    public async Task UpdateTimeTickers_UpdatesPropertiesAndPersists()
    {
        var ticker = CreateTimeTicker();
        await SeedTimeTickers(ticker);

        ticker.Function = "UpdatedFunction";
        ticker.ExecutionTime = _fixedNow.AddMinutes(99);

        var result = await _provider.UpdateTimeTickers(new[] { ticker }, CancellationToken.None);

        Assert.Equal(1, result);

        using var ctx = CreateVerifyContext();
        var updated = await ctx.Set<TimeTickerEntity>().AsNoTracking().FirstAsync(t => t.Id == ticker.Id);
        Assert.Equal("UpdatedFunction", updated.Function);
        Assert.Equal(_fixedNow.AddMinutes(99), updated.ExecutionTime);
    }

    // =========================================================================
    // 3. RemoveTimeTickers
    // =========================================================================

    [Fact]
    public async Task RemoveTimeTickers_DeletesAndVerifiesRemoved()
    {
        var ticker1 = CreateTimeTicker();
        var ticker2 = CreateTimeTicker();
        await SeedTimeTickers(ticker1, ticker2);

        var result = await _provider.RemoveTimeTickers(new[] { ticker1.Id }, CancellationToken.None);

        Assert.True(result > 0);

        using var ctx = CreateVerifyContext();
        var remaining = await ctx.Set<TimeTickerEntity>().AsNoTracking().ToListAsync();
        Assert.Single(remaining);
        Assert.Equal(ticker2.Id, remaining[0].Id);
    }

    // =========================================================================
    // 4. GetTimeTickerById
    // =========================================================================

    [Fact]
    public async Task GetTimeTickerById_ExistingTicker_ReturnsTicker()
    {
        var ticker = CreateTimeTicker();
        await SeedTimeTickers(ticker);

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
    // 5. QueueTimeTickers — updates status to Queued with lock
    // =========================================================================

    [Fact]
    public async Task QueueTimeTickers_UpdatesStatusToQueuedWithLock()
    {
        var ticker = CreateTimeTicker(updatedAt: _fixedNow.AddHours(-1));
        await SeedTimeTickers(ticker);

        // The method checks UpdatedAt matches for optimistic concurrency
        var inputTicker = new TimeTickerEntity
        {
            Id = ticker.Id,
            UpdatedAt = ticker.UpdatedAt
        };

        var results = await ToListAsync(_provider.QueueTimeTickers(new[] { inputTicker }, CancellationToken.None));

        Assert.Single(results);
        Assert.Equal(ticker.Id, results[0].Id);
        Assert.Equal(TickerStatus.Queued, results[0].Status);
        Assert.Equal(NodeId, results[0].LockHolder);
        Assert.Equal(_fixedNow, results[0].LockedAt);
        Assert.Equal(_fixedNow, results[0].UpdatedAt);

        // Verify in DB
        using var ctx = CreateVerifyContext();
        var dbTicker = await ctx.Set<TimeTickerEntity>().AsNoTracking().FirstAsync(t => t.Id == ticker.Id);
        Assert.Equal(TickerStatus.Queued, dbTicker.Status);
        Assert.Equal(NodeId, dbTicker.LockHolder);
    }

    [Fact]
    public async Task QueueTimeTickers_StaleUpdatedAt_SkipsTicker()
    {
        var ticker = CreateTimeTicker(updatedAt: _fixedNow.AddHours(-1));
        await SeedTimeTickers(ticker);

        // Use a different UpdatedAt so the WHERE clause won't match
        var inputTicker = new TimeTickerEntity
        {
            Id = ticker.Id,
            UpdatedAt = _fixedNow.AddHours(-2) // stale
        };

        var results = await ToListAsync(_provider.QueueTimeTickers(new[] { inputTicker }, CancellationToken.None));
        Assert.Empty(results);
    }

    // =========================================================================
    // 6. QueueTimedOutTimeTickers — picks up old tickers
    // =========================================================================

    [Fact]
    public async Task QueueTimedOutTimeTickers_PicksUpOldIdleTickers()
    {
        // fallbackThreshold = now.AddSeconds(-1), so execution time must be <= that
        var oldExecutionTime = _fixedNow.AddSeconds(-5);
        var ticker = CreateTimeTicker(
            executionTime: oldExecutionTime,
            status: TickerStatus.Idle,
            updatedAt: _fixedNow.AddHours(-1));
        await SeedTimeTickers(ticker);

        var results = await ToListAsync(_provider.QueueTimedOutTimeTickers(CancellationToken.None));

        Assert.Single(results);
        Assert.Equal(ticker.Id, results[0].Id);

        // Verify DB: status should be InProgress (fallback sets InProgress)
        using var ctx = CreateVerifyContext();
        var dbTicker = await ctx.Set<TimeTickerEntity>().AsNoTracking().FirstAsync(t => t.Id == ticker.Id);
        Assert.Equal(TickerStatus.InProgress, dbTicker.Status);
        Assert.Equal(NodeId, dbTicker.LockHolder);
    }

    [Fact]
    public async Task QueueTimedOutTimeTickers_IgnoresRecentTickers()
    {
        // Execution time within the 1-second main window — should NOT be picked up by fallback
        var recentExecutionTime = _fixedNow;
        var ticker = CreateTimeTicker(
            executionTime: recentExecutionTime,
            status: TickerStatus.Idle);
        await SeedTimeTickers(ticker);

        var results = await ToListAsync(_provider.QueueTimedOutTimeTickers(CancellationToken.None));
        Assert.Empty(results);
    }

    // =========================================================================
    // 7. AcquireImmediateTimeTickersAsync
    // =========================================================================

    [Fact]
    public async Task AcquireImmediateTimeTickersAsync_AcquiresIdleTickers()
    {
        var ticker = CreateTimeTicker(
            status: TickerStatus.Idle,
            lockHolder: null,
            lockedAt: null);
        await SeedTimeTickers(ticker);

        var results = await _provider.AcquireImmediateTimeTickersAsync(new[] { ticker.Id }, CancellationToken.None);

        Assert.Single(results);

        using var ctx = CreateVerifyContext();
        var dbTicker = await ctx.Set<TimeTickerEntity>().AsNoTracking().FirstAsync(t => t.Id == ticker.Id);
        Assert.Equal(TickerStatus.InProgress, dbTicker.Status);
        Assert.Equal(NodeId, dbTicker.LockHolder);
        Assert.Equal(_fixedNow, dbTicker.LockedAt);
    }

    [Fact]
    public async Task AcquireImmediateTimeTickersAsync_EmptyIds_ReturnsEmpty()
    {
        var results = await _provider.AcquireImmediateTimeTickersAsync(Array.Empty<Guid>(), CancellationToken.None);
        Assert.Empty(results);
    }

    [Fact]
    public async Task AcquireImmediateTimeTickersAsync_AlreadyLockedByOther_CannotAcquire()
    {
        var ticker = CreateTimeTicker(
            status: TickerStatus.Queued,
            lockHolder: "other-node",
            lockedAt: _fixedNow.AddMinutes(-1));
        await SeedTimeTickers(ticker);

        var results = await _provider.AcquireImmediateTimeTickersAsync(new[] { ticker.Id }, CancellationToken.None);

        // WhereCanAcquire: (Idle/Queued && lockHolder == _lockHolder) || (Idle/Queued && lockedAt == null)
        // This ticker is locked by "other-node" and lockedAt is not null, so it can't be acquired
        Assert.Empty(results);
    }

    [Fact]
    public async Task AcquireImmediateTimeTickersAsync_QueuedBySameNode_CanAcquire()
    {
        var ticker = CreateTimeTicker(
            status: TickerStatus.Queued,
            lockHolder: NodeId,
            lockedAt: _fixedNow.AddMinutes(-1));
        await SeedTimeTickers(ticker);

        var results = await _provider.AcquireImmediateTimeTickersAsync(new[] { ticker.Id }, CancellationToken.None);

        Assert.Single(results);
    }

    // =========================================================================
    // 8. ReleaseAcquiredTimeTickers
    // =========================================================================

    [Fact]
    public async Task ReleaseAcquiredTimeTickers_ReleasesLocksOnMatchingTickers()
    {
        var ticker = CreateTimeTicker(
            status: TickerStatus.Queued,
            lockHolder: NodeId,
            lockedAt: _fixedNow);
        await SeedTimeTickers(ticker);

        await _provider.ReleaseAcquiredTimeTickers(new[] { ticker.Id }, CancellationToken.None);

        using var ctx = CreateVerifyContext();
        var dbTicker = await ctx.Set<TimeTickerEntity>().AsNoTracking().FirstAsync(t => t.Id == ticker.Id);
        Assert.Equal(TickerStatus.Idle, dbTicker.Status);
        Assert.Null(dbTicker.LockHolder);
        Assert.Null(dbTicker.LockedAt);
    }

    [Fact]
    public async Task ReleaseAcquiredTimeTickers_DoesNotReleaseOtherNodesLocks()
    {
        var ticker = CreateTimeTicker(
            status: TickerStatus.Queued,
            lockHolder: "other-node",
            lockedAt: _fixedNow);
        await SeedTimeTickers(ticker);

        await _provider.ReleaseAcquiredTimeTickers(new[] { ticker.Id }, CancellationToken.None);

        using var ctx = CreateVerifyContext();
        var dbTicker = await ctx.Set<TimeTickerEntity>().AsNoTracking().FirstAsync(t => t.Id == ticker.Id);
        // WhereCanAcquire won't match: lockHolder is "other-node" and lockedAt is not null
        Assert.Equal("other-node", dbTicker.LockHolder);
        Assert.Equal(TickerStatus.Queued, dbTicker.Status);
    }

    // =========================================================================
    // 9. GetEarliestTimeTickers
    // =========================================================================

    [Fact]
    public async Task GetEarliestTimeTickers_ReturnsEarliestAcquirableTickers()
    {
        // Within the 1-second window: >= now.AddSeconds(-1)
        var execTime = _fixedNow.AddMilliseconds(500);
        var ticker = CreateTimeTicker(
            executionTime: execTime,
            status: TickerStatus.Idle,
            lockHolder: null,
            lockedAt: null);
        await SeedTimeTickers(ticker);

        var results = await _provider.GetEarliestTimeTickers(CancellationToken.None);

        Assert.Single(results);
        Assert.Equal(ticker.Id, results[0].Id);
    }

    [Fact]
    public async Task GetEarliestTimeTickers_IgnoresOldTickers()
    {
        // Older than 1 second — should be handled by fallback, not main scheduler
        var oldTime = _fixedNow.AddSeconds(-5);
        var ticker = CreateTimeTicker(
            executionTime: oldTime,
            status: TickerStatus.Idle);
        await SeedTimeTickers(ticker);

        var results = await _provider.GetEarliestTimeTickers(CancellationToken.None);
        Assert.Empty(results);
    }

    [Fact]
    public async Task GetEarliestTimeTickers_IgnoresLockedByOtherNode()
    {
        var execTime = _fixedNow.AddMilliseconds(500);
        var ticker = CreateTimeTicker(
            executionTime: execTime,
            status: TickerStatus.Queued,
            lockHolder: "other-node",
            lockedAt: _fixedNow);
        await SeedTimeTickers(ticker);

        var results = await _provider.GetEarliestTimeTickers(CancellationToken.None);
        Assert.Empty(results);
    }

    [Fact]
    public async Task GetEarliestTimeTickers_ReturnsEmptyWhenNoneAvailable()
    {
        var results = await _provider.GetEarliestTimeTickers(CancellationToken.None);
        Assert.Empty(results);
    }

    // =========================================================================
    // 10. InsertCronTickers
    // =========================================================================

    [Fact]
    public async Task InsertCronTickers_InsertsAndVerifiesInDb()
    {
        var cron1 = CreateCronTicker();
        var cron2 = CreateCronTicker(function: "AnotherCron");

        var result = await _provider.InsertCronTickers(new[] { cron1, cron2 }, CancellationToken.None);

        Assert.Equal(2, result);

        using var ctx = CreateVerifyContext();
        var allCrons = await ctx.Set<CronTickerEntity>().AsNoTracking().ToListAsync();
        Assert.Equal(2, allCrons.Count);
        Assert.Contains(allCrons, c => c.Id == cron1.Id);
        Assert.Contains(allCrons, c => c.Id == cron2.Id);
    }

    // =========================================================================
    // 11. UpdateCronTickers
    // =========================================================================

    [Fact]
    public async Task UpdateCronTickers_UpdatesAndVerifies()
    {
        var cron = CreateCronTicker();
        await SeedCronTickers(cron);

        cron.Expression = "0 0 * * *";
        cron.Function = "UpdatedCronFunc";

        var result = await _provider.UpdateCronTickers(new[] { cron }, CancellationToken.None);

        Assert.Equal(1, result);

        using var ctx = CreateVerifyContext();
        var updated = await ctx.Set<CronTickerEntity>().AsNoTracking().FirstAsync(c => c.Id == cron.Id);
        Assert.Equal("0 0 * * *", updated.Expression);
        Assert.Equal("UpdatedCronFunc", updated.Function);
    }

    // =========================================================================
    // 12. RemoveCronTickers
    // =========================================================================

    [Fact]
    public async Task RemoveCronTickers_DeletesAndVerifies()
    {
        var cron1 = CreateCronTicker();
        var cron2 = CreateCronTicker(function: "KeepMe");
        await SeedCronTickers(cron1, cron2);

        var result = await _provider.RemoveCronTickers(new[] { cron1.Id }, CancellationToken.None);

        Assert.Equal(1, result);

        using var ctx = CreateVerifyContext();
        var remaining = await ctx.Set<CronTickerEntity>().AsNoTracking().ToListAsync();
        Assert.Single(remaining);
        Assert.Equal(cron2.Id, remaining[0].Id);
    }

    // =========================================================================
    // 13. QueueCronTickerOccurrences — new occurrence (Upsert + NoUpdate)
    // =========================================================================

    [Fact]
    public async Task QueueCronTickerOccurrences_NewOccurrence_InsertsWithUpsert()
    {
        var cron = CreateCronTicker();
        await SeedCronTickers(cron);

        var executionTime = _fixedNow.AddMinutes(5);
        var managerContext = new InternalManagerContext(cron.Id)
        {
            FunctionName = cron.Function,
            Expression = cron.Expression,
            NextCronOccurrence = null // signals new insert path
        };

        var input = (Key: executionTime, Items: new[] { managerContext });

        var results = await ToListAsync(_provider.QueueCronTickerOccurrences(input, CancellationToken.None));

        Assert.Single(results);
        Assert.Equal(cron.Id, results[0].CronTickerId);
        Assert.Equal(executionTime, results[0].ExecutionTime);
        Assert.Equal(TickerStatus.Queued, results[0].Status);
        Assert.Equal(NodeId, results[0].LockHolder);
        Assert.NotNull(results[0].CronTicker);
        Assert.Equal(cron.Function, results[0].CronTicker.Function);

        // Verify in DB
        using var ctx = CreateVerifyContext();
        var dbOcc = await ctx.Set<CronTickerOccurrenceEntity<CronTickerEntity>>().AsNoTracking().FirstAsync();
        Assert.Equal(TickerStatus.Queued, dbOcc.Status);
        Assert.Equal(NodeId, dbOcc.LockHolder);
    }

    // =========================================================================
    // 14. QueueCronTickerOccurrences — duplicate (ExecutionTime, CronTickerId) is skipped
    // =========================================================================

    [Fact]
    public async Task QueueCronTickerOccurrences_DuplicateExecutionTimeAndCronTickerId_SkippedByUpsert()
    {
        var cron = CreateCronTicker();
        await SeedCronTickers(cron);

        var executionTime = _fixedNow.AddMinutes(5);

        // Seed an existing occurrence with the same (CronTickerId, ExecutionTime)
        var existingOcc = CreateCronOccurrence(cron.Id, executionTime: executionTime, status: TickerStatus.Queued);
        await SeedCronOccurrences(existingOcc);

        var managerContext = new InternalManagerContext(cron.Id)
        {
            FunctionName = cron.Function,
            Expression = cron.Expression,
            NextCronOccurrence = null // new insert path, but Upsert .NoUpdate() will skip
        };

        var input = (Key: executionTime, Items: new[] { managerContext });

        var results = await ToListAsync(_provider.QueueCronTickerOccurrences(input, CancellationToken.None));

        // Should be empty: Upsert with NoUpdate returns 0 affected when row exists
        Assert.Empty(results);

        // Verify original occurrence is unchanged
        using var ctx = CreateVerifyContext();
        var count = await ctx.Set<CronTickerOccurrenceEntity<CronTickerEntity>>().CountAsync();
        Assert.Equal(1, count);
    }

    // =========================================================================
    // 15. QueueCronTickerOccurrences — re-queue existing occurrence (update path)
    // =========================================================================

    [Fact]
    public async Task QueueCronTickerOccurrences_RequeueExistingOccurrence_UpdatesViaExecuteUpdate()
    {
        var cron = CreateCronTicker();
        await SeedCronTickers(cron);

        var executionTime = _fixedNow.AddMinutes(5);
        var existingOcc = CreateCronOccurrence(
            cron.Id,
            executionTime: executionTime,
            status: TickerStatus.Idle,
            lockHolder: null,
            lockedAt: null);
        await SeedCronOccurrences(existingOcc);

        var managerContext = new InternalManagerContext(cron.Id)
        {
            FunctionName = cron.Function,
            Expression = cron.Expression,
            NextCronOccurrence = new NextCronOccurrence(existingOcc.Id, existingOcc.CreatedAt)
        };

        var input = (Key: executionTime, Items: new[] { managerContext });

        var results = await ToListAsync(_provider.QueueCronTickerOccurrences(input, CancellationToken.None));

        Assert.Single(results);
        Assert.Equal(existingOcc.Id, results[0].Id);
        Assert.Equal(TickerStatus.Queued, results[0].Status);
        Assert.Equal(NodeId, results[0].LockHolder);

        // Verify in DB
        using var ctx = CreateVerifyContext();
        var dbOcc = await ctx.Set<CronTickerOccurrenceEntity<CronTickerEntity>>()
            .AsNoTracking()
            .FirstAsync(o => o.Id == existingOcc.Id);
        Assert.Equal(TickerStatus.Queued, dbOcc.Status);
        Assert.Equal(NodeId, dbOcc.LockHolder);
    }

    // =========================================================================
    // 16. GetEarliestAvailableCronOccurrence
    // =========================================================================

    [Fact]
    public async Task GetEarliestAvailableCronOccurrence_ReturnsEarliestAcquirable()
    {
        var cron = CreateCronTicker();
        await SeedCronTickers(cron);

        // Within the 1-second main scheduler window: >= now.AddSeconds(-1)
        var execTime = _fixedNow.AddMilliseconds(200);
        var occ = CreateCronOccurrence(
            cron.Id,
            executionTime: execTime,
            status: TickerStatus.Idle,
            lockHolder: null,
            lockedAt: null);
        await SeedCronOccurrences(occ);

        var result = await _provider.GetEarliestAvailableCronOccurrence(new[] { cron.Id }, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(occ.Id, result.Id);
        Assert.Equal(cron.Id, result.CronTickerId);
        Assert.NotNull(result.CronTicker);
        Assert.Equal(cron.Function, result.CronTicker.Function);
    }

    [Fact]
    public async Task GetEarliestAvailableCronOccurrence_IgnoresOldOccurrences()
    {
        var cron = CreateCronTicker();
        await SeedCronTickers(cron);

        // Older than 1 second — outside main scheduler window
        var oldTime = _fixedNow.AddSeconds(-5);
        var occ = CreateCronOccurrence(cron.Id, executionTime: oldTime, status: TickerStatus.Idle);
        await SeedCronOccurrences(occ);

        var result = await _provider.GetEarliestAvailableCronOccurrence(new[] { cron.Id }, CancellationToken.None);
        Assert.Null(result);
    }

    // =========================================================================
    // 17. QueueTimedOutCronTickerOccurrences
    // =========================================================================

    [Fact]
    public async Task QueueTimedOutCronTickerOccurrences_PicksUpOldOccurrences()
    {
        var cron = CreateCronTicker();
        await SeedCronTickers(cron);

        // Execution time older than fallbackThreshold (now - 1 second)
        var oldTime = _fixedNow.AddSeconds(-5);
        var occ = CreateCronOccurrence(
            cron.Id,
            executionTime: oldTime,
            status: TickerStatus.Idle,
            updatedAt: _fixedNow.AddHours(-1));
        await SeedCronOccurrences(occ);

        var results = await ToListAsync(_provider.QueueTimedOutCronTickerOccurrences(CancellationToken.None));

        Assert.Single(results);
        Assert.Equal(occ.Id, results[0].Id);

        // Verify DB
        using var ctx = CreateVerifyContext();
        var dbOcc = await ctx.Set<CronTickerOccurrenceEntity<CronTickerEntity>>()
            .AsNoTracking()
            .FirstAsync(o => o.Id == occ.Id);
        Assert.Equal(TickerStatus.InProgress, dbOcc.Status);
        Assert.Equal(NodeId, dbOcc.LockHolder);
    }

    [Fact]
    public async Task QueueTimedOutCronTickerOccurrences_IgnoresRecentOccurrences()
    {
        var cron = CreateCronTicker();
        await SeedCronTickers(cron);

        var recentTime = _fixedNow;
        var occ = CreateCronOccurrence(cron.Id, executionTime: recentTime, status: TickerStatus.Idle);
        await SeedCronOccurrences(occ);

        var results = await ToListAsync(_provider.QueueTimedOutCronTickerOccurrences(CancellationToken.None));
        Assert.Empty(results);
    }

    // =========================================================================
    // 18. InsertCronTickerOccurrences — bulk insert
    // =========================================================================

    [Fact]
    public async Task InsertCronTickerOccurrences_BulkInsertAndVerify()
    {
        var cron = CreateCronTicker();
        await SeedCronTickers(cron);

        var occ1 = CreateCronOccurrence(cron.Id, executionTime: _fixedNow.AddMinutes(1));
        var occ2 = CreateCronOccurrence(cron.Id, executionTime: _fixedNow.AddMinutes(2));

        var result = await _provider.InsertCronTickerOccurrences(new[] { occ1, occ2 }, CancellationToken.None);

        Assert.Equal(2, result);

        using var ctx = CreateVerifyContext();
        var allOccs = await ctx.Set<CronTickerOccurrenceEntity<CronTickerEntity>>().AsNoTracking().ToListAsync();
        Assert.Equal(2, allOccs.Count);
    }

    // =========================================================================
    // 19. RemoveCronTickerOccurrences — bulk delete
    // =========================================================================

    [Fact]
    public async Task RemoveCronTickerOccurrences_BulkDeleteAndVerify()
    {
        var cron = CreateCronTicker();
        await SeedCronTickers(cron);

        var occ1 = CreateCronOccurrence(cron.Id, executionTime: _fixedNow.AddMinutes(1));
        var occ2 = CreateCronOccurrence(cron.Id, executionTime: _fixedNow.AddMinutes(2));
        await SeedCronOccurrences(occ1, occ2);

        var result = await _provider.RemoveCronTickerOccurrences(new[] { occ1.Id }, CancellationToken.None);

        Assert.Equal(1, result);

        using var ctx = CreateVerifyContext();
        var remaining = await ctx.Set<CronTickerOccurrenceEntity<CronTickerEntity>>().AsNoTracking().ToListAsync();
        Assert.Single(remaining);
        Assert.Equal(occ2.Id, remaining[0].Id);
    }

    // =========================================================================
    // 20. AcquireImmediateCronOccurrencesAsync
    // =========================================================================

    [Fact]
    public async Task AcquireImmediateCronOccurrencesAsync_AcquiresIdleOccurrences()
    {
        var cron = CreateCronTicker();
        await SeedCronTickers(cron);

        var occ = CreateCronOccurrence(
            cron.Id,
            status: TickerStatus.Idle,
            lockHolder: null,
            lockedAt: null);
        await SeedCronOccurrences(occ);

        var results = await _provider.AcquireImmediateCronOccurrencesAsync(new[] { occ.Id }, CancellationToken.None);

        Assert.Single(results);

        using var ctx = CreateVerifyContext();
        var dbOcc = await ctx.Set<CronTickerOccurrenceEntity<CronTickerEntity>>()
            .AsNoTracking()
            .FirstAsync(o => o.Id == occ.Id);
        Assert.Equal(TickerStatus.InProgress, dbOcc.Status);
        Assert.Equal(NodeId, dbOcc.LockHolder);
        Assert.Equal(_fixedNow, dbOcc.LockedAt);
    }

    [Fact]
    public async Task AcquireImmediateCronOccurrencesAsync_EmptyIds_ReturnsEmpty()
    {
        var results = await _provider.AcquireImmediateCronOccurrencesAsync(Array.Empty<Guid>(), CancellationToken.None);
        Assert.Empty(results);
    }

    [Fact]
    public async Task AcquireImmediateCronOccurrencesAsync_LockedByOtherNode_CannotAcquire()
    {
        var cron = CreateCronTicker();
        await SeedCronTickers(cron);

        var occ = CreateCronOccurrence(
            cron.Id,
            status: TickerStatus.Queued,
            lockHolder: "other-node",
            lockedAt: _fixedNow.AddMinutes(-1));
        await SeedCronOccurrences(occ);

        var results = await _provider.AcquireImmediateCronOccurrencesAsync(new[] { occ.Id }, CancellationToken.None);
        Assert.Empty(results);
    }

    // =========================================================================
    // 21. ReleaseAcquiredCronTickerOccurrences
    // =========================================================================

    [Fact]
    public async Task ReleaseAcquiredCronTickerOccurrences_ReleasesLocks()
    {
        var cron = CreateCronTicker();
        await SeedCronTickers(cron);

        var occ = CreateCronOccurrence(
            cron.Id,
            status: TickerStatus.Queued,
            lockHolder: NodeId,
            lockedAt: _fixedNow);
        await SeedCronOccurrences(occ);

        await _provider.ReleaseAcquiredCronTickerOccurrences(new[] { occ.Id }, CancellationToken.None);

        using var ctx = CreateVerifyContext();
        var dbOcc = await ctx.Set<CronTickerOccurrenceEntity<CronTickerEntity>>()
            .AsNoTracking()
            .FirstAsync(o => o.Id == occ.Id);
        Assert.Equal(TickerStatus.Idle, dbOcc.Status);
        Assert.Null(dbOcc.LockHolder);
        Assert.Null(dbOcc.LockedAt);
    }

    [Fact]
    public async Task ReleaseAcquiredCronTickerOccurrences_DoesNotReleaseOtherNodesLocks()
    {
        var cron = CreateCronTicker();
        await SeedCronTickers(cron);

        var occ = CreateCronOccurrence(
            cron.Id,
            status: TickerStatus.Queued,
            lockHolder: "other-node",
            lockedAt: _fixedNow);
        await SeedCronOccurrences(occ);

        await _provider.ReleaseAcquiredCronTickerOccurrences(new[] { occ.Id }, CancellationToken.None);

        using var ctx = CreateVerifyContext();
        var dbOcc = await ctx.Set<CronTickerOccurrenceEntity<CronTickerEntity>>()
            .AsNoTracking()
            .FirstAsync(o => o.Id == occ.Id);
        Assert.Equal("other-node", dbOcc.LockHolder);
        Assert.Equal(TickerStatus.Queued, dbOcc.Status);
    }

    // =========================================================================
    // 22. ReleaseDeadNodeTimeTickerResources
    // =========================================================================

    [Fact]
    public async Task ReleaseDeadNodeTimeTickerResources_ReleasesIdleAndQueuedTickers()
    {
        var deadNode = "dead-node-1";
        // Idle ticker locked by dead node (LockedAt null means WhereCanAcquire matches)
        var idleTicker = CreateTimeTicker(
            status: TickerStatus.Idle,
            lockHolder: deadNode,
            lockedAt: null);
        await SeedTimeTickers(idleTicker);

        await _provider.ReleaseDeadNodeTimeTickerResources(deadNode, CancellationToken.None);

        using var ctx = CreateVerifyContext();
        var dbTicker = await ctx.Set<TimeTickerEntity>().AsNoTracking().FirstAsync(t => t.Id == idleTicker.Id);
        Assert.Equal(TickerStatus.Idle, dbTicker.Status);
        Assert.Null(dbTicker.LockHolder);
        Assert.Null(dbTicker.LockedAt);
    }

    [Fact]
    public async Task ReleaseDeadNodeTimeTickerResources_ReleasesInProgressTickers()
    {
        var deadNode = "dead-node-1";
        // InProgress ticker owned by dead node — second ExecuteUpdateAsync call handles this
        var inProgressTicker = CreateTimeTicker(
            status: TickerStatus.InProgress,
            lockHolder: deadNode,
            lockedAt: _fixedNow.AddMinutes(-10));
        await SeedTimeTickers(inProgressTicker);

        await _provider.ReleaseDeadNodeTimeTickerResources(deadNode, CancellationToken.None);

        using var ctx = CreateVerifyContext();
        var dbTicker = await ctx.Set<TimeTickerEntity>().AsNoTracking().FirstAsync(t => t.Id == inProgressTicker.Id);
        // The second query releases InProgress tickers back to Idle
        Assert.Equal(TickerStatus.Idle, dbTicker.Status);
        Assert.Null(dbTicker.LockHolder);
        Assert.Null(dbTicker.LockedAt);
    }

    [Fact]
    public async Task ReleaseDeadNodeTimeTickerResources_DoesNotAffectOtherNodes()
    {
        var deadNode = "dead-node-1";
        var healthyTicker = CreateTimeTicker(
            status: TickerStatus.InProgress,
            lockHolder: "healthy-node",
            lockedAt: _fixedNow);
        await SeedTimeTickers(healthyTicker);

        await _provider.ReleaseDeadNodeTimeTickerResources(deadNode, CancellationToken.None);

        using var ctx = CreateVerifyContext();
        var dbTicker = await ctx.Set<TimeTickerEntity>().AsNoTracking().FirstAsync(t => t.Id == healthyTicker.Id);
        Assert.Equal(TickerStatus.InProgress, dbTicker.Status);
        Assert.Equal("healthy-node", dbTicker.LockHolder);
    }

    // =========================================================================
    // 23. ReleaseDeadNodeOccurrenceResources
    // =========================================================================

    [Fact]
    public async Task ReleaseDeadNodeOccurrenceResources_ReleasesIdleAndQueuedOccurrences()
    {
        var deadNode = "dead-node-1";
        var cron = CreateCronTicker();
        await SeedCronTickers(cron);

        // Occurrence that is Idle with the dead node's lockHolder but no lockedAt
        // WhereCanAcquire will match: (Idle && lockedAt == null)
        var idleOcc = CreateCronOccurrence(
            cron.Id,
            status: TickerStatus.Idle,
            lockHolder: deadNode,
            lockedAt: null);
        await SeedCronOccurrences(idleOcc);

        await _provider.ReleaseDeadNodeOccurrenceResources(deadNode, CancellationToken.None);

        using var ctx = CreateVerifyContext();
        var dbOcc = await ctx.Set<CronTickerOccurrenceEntity<CronTickerEntity>>()
            .AsNoTracking()
            .FirstAsync(o => o.Id == idleOcc.Id);
        Assert.Equal(TickerStatus.Idle, dbOcc.Status);
        Assert.Null(dbOcc.LockHolder);
        Assert.Null(dbOcc.LockedAt);
    }

    [Fact]
    public async Task ReleaseDeadNodeOccurrenceResources_ReleasesInProgressOccurrences()
    {
        var deadNode = "dead-node-1";
        var cron = CreateCronTicker();
        await SeedCronTickers(cron);

        // InProgress occurrence owned by dead node
        var inProgressOcc = CreateCronOccurrence(
            cron.Id,
            status: TickerStatus.InProgress,
            lockHolder: deadNode,
            lockedAt: _fixedNow.AddMinutes(-10));
        await SeedCronOccurrences(inProgressOcc);

        await _provider.ReleaseDeadNodeOccurrenceResources(deadNode, CancellationToken.None);

        using var ctx = CreateVerifyContext();
        var dbOcc = await ctx.Set<CronTickerOccurrenceEntity<CronTickerEntity>>()
            .AsNoTracking()
            .FirstAsync(o => o.Id == inProgressOcc.Id);
        Assert.Equal(TickerStatus.Idle, dbOcc.Status);
        Assert.Null(dbOcc.LockHolder);
        Assert.Null(dbOcc.LockedAt);
    }

    [Fact]
    public async Task ReleaseDeadNodeOccurrenceResources_DoesNotAffectOtherNodes()
    {
        var deadNode = "dead-node-1";
        var cron = CreateCronTicker();
        await SeedCronTickers(cron);

        var healthyOcc = CreateCronOccurrence(
            cron.Id,
            status: TickerStatus.InProgress,
            lockHolder: "healthy-node",
            lockedAt: _fixedNow);
        await SeedCronOccurrences(healthyOcc);

        await _provider.ReleaseDeadNodeOccurrenceResources(deadNode, CancellationToken.None);

        using var ctx = CreateVerifyContext();
        var dbOcc = await ctx.Set<CronTickerOccurrenceEntity<CronTickerEntity>>()
            .AsNoTracking()
            .FirstAsync(o => o.Id == healthyOcc.Id);
        Assert.Equal(TickerStatus.InProgress, dbOcc.Status);
        Assert.Equal("healthy-node", dbOcc.LockHolder);
    }

    // =========================================================================
    // Additional edge cases
    // =========================================================================

    [Fact]
    public async Task ReleaseAcquiredTimeTickers_EmptyIds_ReleasesAllForNode()
    {
        // When ids array is empty, it should release ALL tickers owned by the node
        var ticker1 = CreateTimeTicker(
            status: TickerStatus.Queued,
            lockHolder: NodeId,
            lockedAt: _fixedNow);
        var ticker2 = CreateTimeTicker(
            status: TickerStatus.Idle,
            lockHolder: null,
            lockedAt: null);
        await SeedTimeTickers(ticker1, ticker2);

        await _provider.ReleaseAcquiredTimeTickers(Array.Empty<Guid>(), CancellationToken.None);

        using var ctx = CreateVerifyContext();
        // ticker1 should be released (Queued + locked by our node)
        var db1 = await ctx.Set<TimeTickerEntity>().AsNoTracking().FirstAsync(t => t.Id == ticker1.Id);
        Assert.Equal(TickerStatus.Idle, db1.Status);
        Assert.Null(db1.LockHolder);

        // ticker2 was Idle with no lock, WhereCanAcquire matches (LockedAt == null), so it gets "released" too
        var db2 = await ctx.Set<TimeTickerEntity>().AsNoTracking().FirstAsync(t => t.Id == ticker2.Id);
        Assert.Equal(TickerStatus.Idle, db2.Status);
    }

    [Fact]
    public async Task ReleaseAcquiredCronTickerOccurrences_EmptyIds_ReleasesAllForNode()
    {
        var cron = CreateCronTicker();
        await SeedCronTickers(cron);

        var occ1 = CreateCronOccurrence(
            cron.Id,
            executionTime: _fixedNow.AddMinutes(1),
            status: TickerStatus.Queued,
            lockHolder: NodeId,
            lockedAt: _fixedNow);
        var occ2 = CreateCronOccurrence(
            cron.Id,
            executionTime: _fixedNow.AddMinutes(2),
            status: TickerStatus.Queued,
            lockHolder: "other-node",
            lockedAt: _fixedNow);
        await SeedCronOccurrences(occ1, occ2);

        await _provider.ReleaseAcquiredCronTickerOccurrences(Array.Empty<Guid>(), CancellationToken.None);

        using var ctx = CreateVerifyContext();
        var db1 = await ctx.Set<CronTickerOccurrenceEntity<CronTickerEntity>>()
            .AsNoTracking().FirstAsync(o => o.Id == occ1.Id);
        Assert.Equal(TickerStatus.Idle, db1.Status);
        Assert.Null(db1.LockHolder);

        // occ2 locked by other node should remain
        var db2 = await ctx.Set<CronTickerOccurrenceEntity<CronTickerEntity>>()
            .AsNoTracking().FirstAsync(o => o.Id == occ2.Id);
        Assert.Equal(TickerStatus.Queued, db2.Status);
        Assert.Equal("other-node", db2.LockHolder);
    }

    [Fact]
    public async Task GetEarliestTimeTickers_ReturnsAllTickersInSameSecond()
    {
        // Multiple tickers in the same second should all be returned
        var baseTime = _fixedNow.AddMilliseconds(100);
        var ticker1 = CreateTimeTicker(
            executionTime: baseTime,
            status: TickerStatus.Idle,
            lockHolder: null,
            lockedAt: null);
        var ticker2 = CreateTimeTicker(
            executionTime: baseTime.AddMilliseconds(500),
            status: TickerStatus.Idle,
            lockHolder: null,
            lockedAt: null);
        // ticker3 is in the NEXT second — should not be returned
        var ticker3 = CreateTimeTicker(
            executionTime: baseTime.AddSeconds(1),
            status: TickerStatus.Idle,
            lockHolder: null,
            lockedAt: null);
        await SeedTimeTickers(ticker1, ticker2, ticker3);

        var results = await _provider.GetEarliestTimeTickers(CancellationToken.None);

        // ticker1 and ticker2 are in the same second, ticker3 is in the next
        Assert.Equal(2, results.Length);
        Assert.Contains(results, r => r.Id == ticker1.Id);
        Assert.Contains(results, r => r.Id == ticker2.Id);
    }

    [Fact]
    public async Task QueueTimedOutCronTickerOccurrences_IgnoresDoneStatus()
    {
        var cron = CreateCronTicker();
        await SeedCronTickers(cron);

        var oldTime = _fixedNow.AddSeconds(-5);
        // Occurrence with status Done should not be picked up
        var occ = CreateCronOccurrence(
            cron.Id,
            executionTime: oldTime,
            status: TickerStatus.Done,
            updatedAt: _fixedNow.AddHours(-1));
        await SeedCronOccurrences(occ);

        var results = await ToListAsync(_provider.QueueTimedOutCronTickerOccurrences(CancellationToken.None));

        // The method only picks up Idle or Queued occurrences
        Assert.Empty(results);
    }

    [Fact]
    public async Task AcquireImmediateTimeTickersAsync_MultipleTickers_AcquiresOnlyAcquirable()
    {
        var acquirable = CreateTimeTicker(
            status: TickerStatus.Idle,
            lockHolder: null,
            lockedAt: null);
        var locked = CreateTimeTicker(
            status: TickerStatus.InProgress,
            lockHolder: "other-node",
            lockedAt: _fixedNow);
        await SeedTimeTickers(acquirable, locked);

        var results = await _provider.AcquireImmediateTimeTickersAsync(
            new[] { acquirable.Id, locked.Id }, CancellationToken.None);

        Assert.Single(results);
        Assert.Equal(acquirable.Id, results[0].Id);
    }

    [Fact]
    public async Task AcquireImmediateCronOccurrencesAsync_MultipleMixed_AcquiresOnlyAcquirable()
    {
        var cron = CreateCronTicker();
        await SeedCronTickers(cron);

        var acquirable = CreateCronOccurrence(
            cron.Id,
            executionTime: _fixedNow.AddMinutes(1),
            status: TickerStatus.Idle,
            lockHolder: null,
            lockedAt: null);
        var inProgress = CreateCronOccurrence(
            cron.Id,
            executionTime: _fixedNow.AddMinutes(2),
            status: TickerStatus.InProgress,
            lockHolder: "other-node",
            lockedAt: _fixedNow);
        await SeedCronOccurrences(acquirable, inProgress);

        var results = await _provider.AcquireImmediateCronOccurrencesAsync(
            new[] { acquirable.Id, inProgress.Id }, CancellationToken.None);

        Assert.Single(results);
        Assert.Equal(acquirable.Id, results[0].Id);
    }
}
