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
    private const string LogicalNodeId = "test-node-1";
    private string NodeId;
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

        var schedulerOptions = new SchedulerOptionsBuilder { NodeIdentifier = LogicalNodeId };
        NodeId = schedulerOptions.ExecutionOwnerId;

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

    [Fact]
    public void SchedulerOptions_SameLogicalNode_UsesDistinctExecutionOwners()
    {
        var first = new SchedulerOptionsBuilder { NodeIdentifier = LogicalNodeId };
        var second = new SchedulerOptionsBuilder { NodeIdentifier = LogicalNodeId };

        Assert.Equal(first.NodeIdentifier, second.NodeIdentifier);
        Assert.NotEqual(first.ExecutionOwnerId, second.ExecutionOwnerId);
        Assert.StartsWith($"{LogicalNodeId}:{Environment.ProcessId}:", first.ExecutionOwnerId);
    }

    [Fact]
    public async Task SameLogicalNode_SecondProviderCannotReleaseLiveSiblingOwnership()
    {
        var ticker = CreateTimeTicker(status: TickerStatus.Idle);
        await SeedTimeTickers(ticker);
        await _provider.AcquireImmediateTimeTickersAsync([ticker.Id], CancellationToken.None);

        var secondOptions = new SchedulerOptionsBuilder { NodeIdentifier = LogicalNodeId };
        Assert.NotEqual(NodeId, secondOptions.ExecutionOwnerId);
        var services = new ServiceCollection();
        services.AddSingleton<IDbContextFactory<TestTickerQDbContext>>(
            new PooledDbContextFactory<TestTickerQDbContext>(_options));
        using var secondServiceProvider = services.BuildServiceProvider();
        var secondProvider = new TestableProvider(secondServiceProvider, _clock, secondOptions, _redisContext);

        await secondProvider.ReleaseDeadNodeTimeTickerResources(secondOptions.ExecutionOwnerId, CancellationToken.None);

        using var ctx = CreateVerifyContext();
        var persisted = await ctx.Set<TimeTickerEntity>().AsNoTracking()
            .SingleAsync(x => x.Id == ticker.Id);
        Assert.Equal(TickerStatus.InProgress, persisted.Status);
        Assert.Equal(NodeId, persisted.LockHolder);
    }

    [Fact]
    public void EfProvider_SupportsLeaseBasedRecovery()
        => Assert.True(_provider.SupportsLeaseBasedRecovery);

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

    [Fact]
    public async Task ReplaceTimeTickerChainAsync_WhenReplacementInsertFails_OriginalAggregateRemainsUnchanged()
    {
        var root = CreateTimeTicker(function: "OriginalRoot");
        root.Description = "root-description";
        root.Request = System.Text.Encoding.UTF8.GetBytes("{\"message\":\"héllo\"}");
        root.Retries = 3;
        root.RetryIntervals = [5, 13, 21];
        root.OnStale = StaleAction.Restart;
        root.TimeoutSeconds = 47;
        root.RunCondition = RunCondition.OnSuccess;

        var child = CreateTimeTicker(function: "OriginalChild");
        child.ParentId = root.Id;
        child.Description = "child-description";
        child.Request = System.Text.Encoding.UTF8.GetBytes("{\"child\":true}");
        child.Retries = 2;
        child.RetryIntervals = [8, 34];
        child.OnStale = StaleAction.Cancel;
        child.TimeoutSeconds = 59;
        child.RunCondition = RunCondition.OnFailure;
        root.Children = [child];

        // Reusing this unrelated row's primary key forces the replacement insert to fail
        // before the old aggregate is removed.
        var collision = CreateTimeTicker(function: "UnrelatedCollision");
        await SeedTimeTickers(root, collision);

        var replacement = CreateTimeTicker(id: collision.Id, function: "InvalidReplacement");
        replacement.Children = [CreateTimeTicker(function: "ReplacementChild")];

        await Assert.ThrowsAnyAsync<Exception>(() =>
            _provider.ReplaceTimeTickerChainAsync(root.Id, replacement, CancellationToken.None));

        await using var verify = CreateVerifyContext();
        var persisted = await verify.Set<TimeTickerEntity>()
            .AsNoTracking()
            .Where(x => x.Id == root.Id || x.Id == child.Id || x.Id == collision.Id)
            .OrderBy(x => x.Function)
            .ToListAsync();

        Assert.Equal(3, persisted.Count);
        var persistedRoot = Assert.Single(persisted, x => x.Id == root.Id);
        var persistedChild = Assert.Single(persisted, x => x.Id == child.Id);
        Assert.Single(persisted, x => x.Id == collision.Id);

        Assert.Equal("OriginalRoot", persistedRoot.Function);
        Assert.Equal("root-description", persistedRoot.Description);
        Assert.Equal(root.Request, persistedRoot.Request);
        Assert.Equal(3, persistedRoot.Retries);
        Assert.Equal([5, 13, 21], persistedRoot.RetryIntervals);
        Assert.Equal(StaleAction.Restart, persistedRoot.OnStale);
        Assert.Equal(47, persistedRoot.TimeoutSeconds);
        Assert.Equal(RunCondition.OnSuccess, persistedRoot.RunCondition);

        Assert.Equal(root.Id, persistedChild.ParentId);
        Assert.Equal("OriginalChild", persistedChild.Function);
        Assert.Equal("child-description", persistedChild.Description);
        Assert.Equal(child.Request, persistedChild.Request);
        Assert.Equal(2, persistedChild.Retries);
        Assert.Equal([8, 34], persistedChild.RetryIntervals);
        Assert.Equal(StaleAction.Cancel, persistedChild.OnStale);
        Assert.Equal(59, persistedChild.TimeoutSeconds);
        Assert.Equal(RunCondition.OnFailure, persistedChild.RunCondition);
    }

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
        Assert.NotNull(results[0].AcquisitionToken);

        // Verify DB: status should be InProgress (fallback sets InProgress)
        using var ctx = CreateVerifyContext();
        var dbTicker = await ctx.Set<TimeTickerEntity>().AsNoTracking().FirstAsync(t => t.Id == ticker.Id);
        Assert.Equal(TickerStatus.InProgress, dbTicker.Status);
        Assert.Equal(NodeId, dbTicker.LockHolder);
        Assert.Equal(results[0].AcquisitionToken, dbTicker.AcquisitionToken);
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
    public async Task AcquireImmediateTimeTickersAsync_UsesUniqueTokenPerInvocation()
    {
        var ticker = CreateTimeTicker(status: TickerStatus.Idle);
        await SeedTimeTickers(ticker);

        await _provider.AcquireImmediateTimeTickersAsync([ticker.Id], CancellationToken.None);
        Guid? firstToken;
        using (var ctx = CreateVerifyContext())
            firstToken = await ctx.Set<TimeTickerEntity>()
                .Where(x => x.Id == ticker.Id)
                .Select(x => x.AcquisitionToken)
                .SingleAsync();

        await _provider.ReleaseDeadNodeTimeTickerResources(NodeId, CancellationToken.None);
        await _provider.AcquireImmediateTimeTickersAsync([ticker.Id], CancellationToken.None);
        Guid? secondToken;
        using (var ctx = CreateVerifyContext())
            secondToken = await ctx.Set<TimeTickerEntity>()
                .Where(x => x.Id == ticker.Id)
                .Select(x => x.AcquisitionToken)
                .SingleAsync();

        Assert.NotNull(firstToken);
        Assert.NotNull(secondToken);
        Assert.NotEqual(firstToken, secondToken);
    }

    [Fact]
    public async Task AcquireImmediateTimeTickersAsync_HydratesBeyondGrandchildren()
    {
        var root = CreateTimeTicker(status: TickerStatus.Idle);
        var child = CreateTimeTicker(executionTime: null, status: TickerStatus.Idle);
        var grandchild = CreateTimeTicker(executionTime: null, status: TickerStatus.Idle);
        var greatGrandchild = CreateTimeTicker(executionTime: null, status: TickerStatus.Idle);
        greatGrandchild.TimeoutSeconds = 37;
        child.ParentId = root.Id;
        grandchild.ParentId = child.Id;
        greatGrandchild.ParentId = grandchild.Id;
        root.Children = [child];
        child.Children = [grandchild];
        grandchild.Children = [greatGrandchild];
        await SeedTimeTickers(root);

        var acquired = await _provider.AcquireImmediateTimeTickersAsync([root.Id], CancellationToken.None);

        var acquiredRoot = Assert.Single(acquired);
        var acquiredChild = Assert.Single(acquiredRoot.Children);
        var acquiredGrandchild = Assert.Single(acquiredChild.Children);
        var acquiredGreatGrandchild = Assert.Single(acquiredGrandchild.Children);
        Assert.Equal(root.Id, acquiredChild.ParentId);
        Assert.Equal(child.Id, acquiredGrandchild.ParentId);
        Assert.Equal(greatGrandchild.Id, acquiredGreatGrandchild.Id);
        Assert.Equal(grandchild.Id, acquiredGreatGrandchild.ParentId);
        Assert.Equal(37, acquiredGreatGrandchild.TimeoutSeconds);
    }

    // -------------------------------------------------------------------------
    // Contract identity must survive the BFS extension below grandchild depth for
    // every deep-chain acquisition path (normal / immediate / timed-out). The fast
    // projection loads root+child+grandchild; the BFS hydrates great-grandchild and
    // deeper and must carry RequestContractVersion/Fingerprint or those descendants
    // execute under legacy compatibility, bypassing drift checks.
    // -------------------------------------------------------------------------

    private (TimeTickerEntity Root, Guid GreatGrandchildId, Guid GreatGreatGrandchildId) BuildIdentityChain(
        TickerStatus status, DateTime? rootExecutionTime)
    {
        var root = CreateTimeTicker(executionTime: rootExecutionTime, status: status);
        var child = CreateTimeTicker(executionTime: null, status: status);
        var grandchild = CreateTimeTicker(executionTime: null, status: status);
        var greatGrandchild = CreateTimeTicker(executionTime: null, status: status);
        var greatGreatGrandchild = CreateTimeTicker(executionTime: null, status: status);

        greatGrandchild.RequestContractVersion = 42;
        greatGrandchild.RequestContractFingerprint = "sha256:great-grandchild";
        greatGreatGrandchild.RequestContractVersion = 43;
        greatGreatGrandchild.RequestContractFingerprint = "sha256:great-great-grandchild";

        child.ParentId = root.Id;
        grandchild.ParentId = child.Id;
        greatGrandchild.ParentId = grandchild.Id;
        greatGreatGrandchild.ParentId = greatGrandchild.Id;
        root.Children = [child];
        child.Children = [grandchild];
        grandchild.Children = [greatGrandchild];
        greatGrandchild.Children = [greatGreatGrandchild];

        return (root, greatGrandchild.Id, greatGreatGrandchild.Id);
    }

    private static TimeTickerEntity FindById(TimeTickerEntity node, Guid id)
    {
        if (node.Id == id) return node;
        if (node.Children == null) return null;
        foreach (var child in node.Children)
        {
            var found = FindById(child, id);
            if (found != null) return found;
        }
        return null;
    }

    private static void AssertDeepContractIdentity(TimeTickerEntity acquiredRoot, Guid ggcId, Guid gggcId)
    {
        var greatGrandchild = FindById(acquiredRoot, ggcId);
        Assert.NotNull(greatGrandchild);
        Assert.Equal(42, greatGrandchild.RequestContractVersion);
        Assert.Equal("sha256:great-grandchild", greatGrandchild.RequestContractFingerprint);

        var greatGreatGrandchild = FindById(acquiredRoot, gggcId);
        Assert.NotNull(greatGreatGrandchild);
        Assert.Equal(43, greatGreatGrandchild.RequestContractVersion);
        Assert.Equal("sha256:great-great-grandchild", greatGreatGrandchild.RequestContractFingerprint);
    }

    [Fact]
    public async Task AcquireImmediateTimeTickersAsync_PreservesContractIdentityBeyondGrandchild()
    {
        var (root, ggcId, gggcId) = BuildIdentityChain(TickerStatus.Idle, rootExecutionTime: null);
        await SeedTimeTickers(root);

        var acquired = await _provider.AcquireImmediateTimeTickersAsync([root.Id], CancellationToken.None);

        AssertDeepContractIdentity(Assert.Single(acquired), ggcId, gggcId);
    }

    [Fact]
    public async Task GetEarliestTimeTickers_PreservesContractIdentityBeyondGrandchild()
    {
        // Root within the 1-second normal-scheduling window.
        var (root, ggcId, gggcId) = BuildIdentityChain(TickerStatus.Idle, rootExecutionTime: _fixedNow);
        await SeedTimeTickers(root);

        var earliest = await _provider.GetEarliestTimeTickers(CancellationToken.None);

        var acquiredRoot = Assert.Single(earliest.Where(t => t.Id == root.Id));
        AssertDeepContractIdentity(acquiredRoot, ggcId, gggcId);
    }

    [Fact]
    public async Task QueueTimedOutTimeTickers_PreservesContractIdentityBeyondGrandchild()
    {
        // Root older than the 1-second window, so the timed-out fallback path hydrates it.
        var (root, ggcId, gggcId) = BuildIdentityChain(TickerStatus.Idle, rootExecutionTime: _fixedNow.AddSeconds(-5));
        await SeedTimeTickers(root);

        var results = await ToListAsync(_provider.QueueTimedOutTimeTickers(CancellationToken.None));

        var acquiredRoot = Assert.Single(results.Where(t => t.Id == root.Id));
        AssertDeepContractIdentity(acquiredRoot, ggcId, gggcId);
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

    [Fact]
    public async Task AcquireImmediateTimeTickersAsync_SkipsRowAlreadyInProgressUnderSameNode()
    {
        // One row this invocation can legitimately acquire...
        var idle = CreateTimeTicker(
            status: TickerStatus.Idle,
            lockHolder: null,
            lockedAt: null);
        // ...and one this same node already holds from an earlier acquisition.
        // WhereCanAcquire cannot transition it, so this call did NOT acquire it —
        // the read-back must not resurface it (that would re-dispatch a running job).
        var alreadyRunning = CreateTimeTicker(
            status: TickerStatus.InProgress,
            lockHolder: NodeId,
            lockedAt: _fixedNow.AddMinutes(-5),
            updatedAt: _fixedNow.AddMinutes(-5));
        await SeedTimeTickers(idle, alreadyRunning);

        var results = await _provider.AcquireImmediateTimeTickersAsync(
            new[] { idle.Id, alreadyRunning.Id }, CancellationToken.None);

        // Only the Idle row was acquired by this invocation.
        Assert.Single(results);
        Assert.Equal(idle.Id, results[0].Id);

        using var ctx = CreateVerifyContext();
        var idleRow = await ctx.Set<TimeTickerEntity>().AsNoTracking().FirstAsync(t => t.Id == idle.Id);
        Assert.Equal(TickerStatus.InProgress, idleRow.Status);
        Assert.Equal(_fixedNow, idleRow.LockedAt);

        // The pre-existing InProgress row was left untouched by this invocation.
        var runningRow = await ctx.Set<TimeTickerEntity>().AsNoTracking().FirstAsync(t => t.Id == alreadyRunning.Id);
        Assert.Equal(TickerStatus.InProgress, runningRow.Status);
        Assert.Equal(_fixedNow.AddMinutes(-5), runningRow.LockedAt);
    }

    [Fact]
    public async Task AcquireImmediateTimeTickersAsync_RollsBackEarlierRows_WhenLaterUpdateFails()
    {
        var first = CreateTimeTicker(status: TickerStatus.Idle);
        var second = CreateTimeTicker(status: TickerStatus.Idle);
        await SeedTimeTickers(first, second);

        await _seedContext.Database.ExecuteSqlRawAsync("""
            CREATE TABLE AcquisitionUpdateCount (Value INTEGER NOT NULL);
            INSERT INTO AcquisitionUpdateCount VALUES (0);
            CREATE TRIGGER FailSecondAcquisition
            BEFORE UPDATE ON TimeTickers
            BEGIN
                UPDATE AcquisitionUpdateCount SET Value = Value + 1;
                SELECT CASE WHEN (SELECT Value FROM AcquisitionUpdateCount) = 2
                    THEN RAISE(ABORT, 'forced acquisition failure') END;
            END;
            """);

        await Assert.ThrowsAsync<SqliteException>(() =>
            _provider.AcquireImmediateTimeTickersAsync([first.Id, second.Id], CancellationToken.None));

        using var ctx = CreateVerifyContext();
        var rows = await ctx.Set<TimeTickerEntity>()
            .AsNoTracking()
            .Where(x => x.Id == first.Id || x.Id == second.Id)
            .ToArrayAsync();

        Assert.Equal(2, rows.Length);
        Assert.All(rows, row =>
        {
            Assert.Equal(TickerStatus.Idle, row.Status);
            Assert.Null(row.LockHolder);
            Assert.Null(row.LockedAt);
            Assert.Null(row.AcquisitionToken);
        });
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
        ticker.LeaseUntil = _fixedNow.AddMinutes(5);
        ticker.AcquisitionToken = Guid.NewGuid();
        await SeedTimeTickers(ticker);

        await _provider.ReleaseAcquiredTimeTickers(new[] { ticker.Id }, CancellationToken.None);

        using var ctx = CreateVerifyContext();
        var dbTicker = await ctx.Set<TimeTickerEntity>().AsNoTracking().FirstAsync(t => t.Id == ticker.Id);
        Assert.Equal(TickerStatus.Idle, dbTicker.Status);
        Assert.Null(dbTicker.LockHolder);
        Assert.Null(dbTicker.LockedAt);
        Assert.Null(dbTicker.LeaseUntil);
        Assert.Null(dbTicker.AcquisitionToken);
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
        ticker.RequestContractVersion = 21;
        ticker.RequestContractFingerprint = "sha256:ef-time-contract";
        await SeedTimeTickers(ticker);

        var results = await _provider.GetEarliestTimeTickers(CancellationToken.None);

        Assert.Single(results);
        Assert.Equal(ticker.Id, results[0].Id);
        Assert.Equal(ticker.RequestContractVersion, results[0].RequestContractVersion);
        Assert.Equal(ticker.RequestContractFingerprint, results[0].RequestContractFingerprint);
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
        cron.RequestContractVersion = 12;
        cron.RequestContractFingerprint = "sha256:ef-queued-contract";
        await SeedCronTickers(cron);

        var executionTime = _fixedNow.AddMinutes(5);
        var managerContext = new InternalManagerContext(cron.Id)
        {
            FunctionName = cron.Function,
            Expression = cron.Expression,
            RequestContractVersion = cron.RequestContractVersion,
            RequestContractFingerprint = cron.RequestContractFingerprint,
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
        Assert.Equal(cron.RequestContractVersion, results[0].CronTicker.RequestContractVersion);
        Assert.Equal(cron.RequestContractFingerprint, results[0].CronTicker.RequestContractFingerprint);

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
        cron.RequestContractVersion = 11;
        cron.RequestContractFingerprint = "sha256:ef-cron-contract";
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
        Assert.Equal(11, result.CronTicker.RequestContractVersion);
        Assert.Equal("sha256:ef-cron-contract", result.CronTicker.RequestContractFingerprint);
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
        Assert.Equal(oldTime, results[0].ExecutionTime);
        Assert.NotNull(results[0].AcquisitionToken);

        // Verify DB
        using var ctx = CreateVerifyContext();
        var dbOcc = await ctx.Set<CronTickerOccurrenceEntity<CronTickerEntity>>()
            .AsNoTracking()
            .FirstAsync(o => o.Id == occ.Id);
        Assert.Equal(TickerStatus.InProgress, dbOcc.Status);
        Assert.Equal(NodeId, dbOcc.LockHolder);
        Assert.Equal(results[0].AcquisitionToken, dbOcc.AcquisitionToken);
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
        occ.LeaseUntil = _fixedNow.AddMinutes(5);
        occ.AcquisitionToken = Guid.NewGuid();
        await SeedCronOccurrences(occ);

        await _provider.ReleaseAcquiredCronTickerOccurrences(new[] { occ.Id }, CancellationToken.None);

        using var ctx = CreateVerifyContext();
        var dbOcc = await ctx.Set<CronTickerOccurrenceEntity<CronTickerEntity>>()
            .AsNoTracking()
            .FirstAsync(o => o.Id == occ.Id);
        Assert.Equal(TickerStatus.Idle, dbOcc.Status);
        Assert.Null(dbOcc.LockHolder);
        Assert.Null(dbOcc.LockedAt);
        Assert.Null(dbOcc.LeaseUntil);
        Assert.Null(dbOcc.AcquisitionToken);
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

    [Fact]
    public async Task RecoverStaleTickers_ReleasesOnlyAgedIdleOrQueuedOwnership()
    {
        var stale = CreateTimeTicker(
            status: TickerStatus.Queued,
            lockHolder: "test-node-1:123:predecessor",
            lockedAt: _fixedNow.AddMinutes(-3));
        var liveSibling = CreateTimeTicker(
            status: TickerStatus.Queued,
            lockHolder: "test-node-1:456:live-sibling",
            lockedAt: _fixedNow.AddMinutes(-1));
        var staleLegacyIdle = CreateTimeTicker(
            status: TickerStatus.Idle,
            lockHolder: "test-node-1:789:legacy",
            lockedAt: _fixedNow.AddMinutes(-3));
        await SeedTimeTickers(stale, liveSibling, staleLegacyIdle);

        await _provider.RecoverStaleTickers(3, CancellationToken.None);

        using var ctx = CreateVerifyContext();
        var recovered = await ctx.Set<TimeTickerEntity>().AsNoTracking()
            .SingleAsync(x => x.Id == stale.Id);
        Assert.Equal(TickerStatus.Idle, recovered.Status);
        Assert.Null(recovered.LockHolder);
        Assert.Null(recovered.LockedAt);
        Assert.Null(recovered.AcquisitionToken);

        var recoveredLegacyIdle = await ctx.Set<TimeTickerEntity>().AsNoTracking()
            .SingleAsync(x => x.Id == staleLegacyIdle.Id);
        Assert.Equal(TickerStatus.Idle, recoveredLegacyIdle.Status);
        Assert.Null(recoveredLegacyIdle.LockHolder);
        Assert.Null(recoveredLegacyIdle.LockedAt);

        var preserved = await ctx.Set<TimeTickerEntity>().AsNoTracking()
            .SingleAsync(x => x.Id == liveSibling.Id);
        Assert.Equal(TickerStatus.Queued, preserved.Status);
        Assert.Equal("test-node-1:456:live-sibling", preserved.LockHolder);
    }

    [Fact]
    public async Task TransitionQueuedTimeTicker_RejectsStaleGenerationAndReturnsOnlyExactWinner()
    {
        var currentToken = Guid.NewGuid();
        var staleToken = Guid.NewGuid();
        var ticker = CreateTimeTicker(
            status: TickerStatus.Queued,
            lockHolder: NodeId,
            lockedAt: _fixedNow.AddSeconds(-10));
        ticker.AcquisitionToken = currentToken;
        await SeedTimeTickers(ticker);

        var staleWinners = await _provider.TransitionQueuedTimeTickersToInProgressAsync(
            [new AcquisitionLease(ticker.Id, staleToken)], CancellationToken.None);

        Assert.Empty(staleWinners);
        using (var ctx = CreateVerifyContext())
        {
            var unchanged = await ctx.Set<TimeTickerEntity>().AsNoTracking().SingleAsync(x => x.Id == ticker.Id);
            Assert.Equal(TickerStatus.Queued, unchanged.Status);
            Assert.Equal(currentToken, unchanged.AcquisitionToken);
        }

        var winners = await _provider.TransitionQueuedTimeTickersToInProgressAsync(
            [new AcquisitionLease(ticker.Id, currentToken)], CancellationToken.None);

        Assert.Equal([ticker.Id], winners);
        using var verify = CreateVerifyContext();
        var transitioned = await verify.Set<TimeTickerEntity>().AsNoTracking().SingleAsync(x => x.Id == ticker.Id);
        Assert.Equal(TickerStatus.InProgress, transitioned.Status);
        Assert.Equal(currentToken, transitioned.AcquisitionToken);
    }

    [Theory]
    [InlineData(TickerStatus.Failed)]
    [InlineData(TickerStatus.Done)]
    [InlineData(TickerStatus.DueDone)]
    [InlineData(TickerStatus.Cancelled)]
    [InlineData(TickerStatus.Skipped)]
    public async Task AcquireTimeTickerOnDemand_RevivesTerminalAndResetsExecutionMetadata(TickerStatus status)
    {
        var ticker = CreateTimeTicker(status: status, lockHolder: "old-owner", lockedAt: _fixedNow.AddMinutes(-5));
        ticker.RetryCount = 2;
        ticker.ExceptionMessage = "old failure";
        ticker.SkippedReason = "old skip";
        ticker.ExecutedAt = _fixedNow.AddMinutes(-4);
        ticker.ElapsedTime = 1234;
        ticker.StaleRestartCount = 2;
        ticker.LeaseUntil = _fixedNow.AddMinutes(-3);
        ticker.AcquisitionToken = Guid.NewGuid();
        await SeedTimeTickers(ticker);

        var acquired = await _provider.AcquireTimeTickerOnDemandAsync(
            ticker.Id, _fixedNow, CancellationToken.None);

        Assert.NotNull(acquired);
        Assert.NotNull(acquired.AcquisitionToken);
        using var ctx = CreateVerifyContext();
        var persisted = await ctx.Set<TimeTickerEntity>().AsNoTracking().SingleAsync(x => x.Id == ticker.Id);
        Assert.Equal(TickerStatus.InProgress, persisted.Status);
        Assert.Equal(NodeId, persisted.LockHolder);
        Assert.Equal(0, persisted.RetryCount);
        Assert.Null(persisted.ExceptionMessage);
        Assert.Null(persisted.SkippedReason);
        Assert.Null(persisted.ExecutedAt);
        Assert.Equal(0, persisted.ElapsedTime);
        Assert.Equal(0, persisted.StaleRestartCount);
        Assert.Equal(acquired.AcquisitionToken, persisted.AcquisitionToken);
    }

    [Fact]
    public async Task AcquireTimeTickerOnDemand_ConcurrentCalls_ExactlyOneWins()
    {
        var ticker = CreateTimeTicker(status: TickerStatus.Failed);
        await SeedTimeTickers(ticker);

        var results = await Task.WhenAll(
            _provider.AcquireTimeTickerOnDemandAsync(ticker.Id, _fixedNow, CancellationToken.None),
            _provider.AcquireTimeTickerOnDemandAsync(ticker.Id, _fixedNow, CancellationToken.None));

        Assert.Single(results, result => result != null);
    }

    [Fact]
    public async Task AcquireTimeTickerOnDemand_InProgress_IsRejected()
    {
        var ticker = CreateTimeTicker(
            status: TickerStatus.InProgress, lockHolder: "live-owner", lockedAt: _fixedNow);
        await SeedTimeTickers(ticker);

        var result = await _provider.AcquireTimeTickerOnDemandAsync(
            ticker.Id, _fixedNow, CancellationToken.None);

        Assert.Null(result);
        using var ctx = CreateVerifyContext();
        var persisted = await ctx.Set<TimeTickerEntity>().AsNoTracking().SingleAsync(x => x.Id == ticker.Id);
        Assert.Equal("live-owner", persisted.LockHolder);
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
    public async Task GenerationFence_RejectsStaleSameNodeRenewalAndTerminalWrite()
    {
        var ticker = CreateTimeTicker(status: TickerStatus.Idle);
        await SeedTimeTickers(ticker);

        var first = Assert.Single(await _provider.AcquireImmediateTimeTickersAsync([ticker.Id], CancellationToken.None));
        Assert.NotNull(first.AcquisitionToken);

        await _provider.ReleaseDeadNodeTimeTickerResources(NodeId, CancellationToken.None);
        using (var releasedContext = CreateVerifyContext())
        {
            var released = await releasedContext.Set<TimeTickerEntity>().AsNoTracking()
                .SingleAsync(x => x.Id == ticker.Id);
            Assert.Null(released.AcquisitionToken);
        }

        var second = Assert.Single(await _provider.AcquireImmediateTimeTickersAsync([ticker.Id], CancellationToken.None));
        Assert.NotNull(second.AcquisitionToken);
        Assert.NotEqual(first.AcquisitionToken, second.AcquisitionToken);

        var renewedUntil = _fixedNow.AddHours(1);
        var renewed = await _provider.RenewTimeTickerLeases(
            [new AcquisitionLease(ticker.Id, first.AcquisitionToken)], renewedUntil, CancellationToken.None);
        Assert.Equal(0, renewed);

        var staleCompletion = new InternalFunctionContext()
            .SetProperty(x => x.TickerId, ticker.Id)
            .SetProperty(x => x.Type, TickerType.TimeTicker)
            .SetProperty(x => x.AcquisitionToken, first.AcquisitionToken)
            .SetProperty(x => x.Status, TickerStatus.Done);
        var terminalWrites = await _provider.UpdateTimeTicker(staleCompletion, CancellationToken.None);
        Assert.Equal(0, terminalWrites);

        using var verifyContext = CreateVerifyContext();
        var persisted = await verifyContext.Set<TimeTickerEntity>().AsNoTracking()
            .SingleAsync(x => x.Id == ticker.Id);
        Assert.Equal(TickerStatus.InProgress, persisted.Status);
        Assert.Equal(second.AcquisitionToken, persisted.AcquisitionToken);
        Assert.NotEqual(renewedUntil, persisted.LeaseUntil);
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

    // =========================================================================
    // Retry-count-only updates must not touch Status / SkippedReason
    // =========================================================================

    [Fact]
    public async Task UpdateTimeTicker_RetryCountOnly_LeavesStatusAndSkippedReasonUntouched()
    {
        // A live, in-progress row that already carries a skip reason from an earlier attempt.
        var ticker = CreateTimeTicker(
            status: TickerStatus.InProgress, lockHolder: NodeId, lockedAt: _fixedNow);
        ticker.SkippedReason = "prior-skip-reason";
        ticker.RetryCount = 1;
        await SeedTimeTickers(ticker);

        // Retry-count-only context: Status is deliberately absent from the update set.
        var retryOnly = new InternalFunctionContext { TickerId = ticker.Id }
            .SetProperty(x => x.RetryCount, 2);
        Assert.DoesNotContain(nameof(InternalFunctionContext.Status), retryOnly.GetPropsToUpdate());

        var affected = await _provider.UpdateTimeTicker(retryOnly, CancellationToken.None);
        Assert.Equal(1, affected);

        using var ctx = CreateVerifyContext();
        var persisted = await ctx.Set<TimeTickerEntity>().AsNoTracking().SingleAsync(x => x.Id == ticker.Id);
        Assert.Equal(2, persisted.RetryCount);
        // Status absent from the update ⇒ status and skip reason must be untouched.
        Assert.Equal(TickerStatus.InProgress, persisted.Status);
        Assert.Equal("prior-skip-reason", persisted.SkippedReason);
    }

    [Fact]
    public async Task UpdateCronTickerOccurrence_RetryCountOnly_LeavesStatusAndSkippedReasonUntouched()
    {
        var cron = CreateCronTicker();
        await SeedCronTickers(cron);
        var occ = CreateCronOccurrence(
            cron.Id, status: TickerStatus.InProgress, lockHolder: NodeId, lockedAt: _fixedNow);
        occ.SkippedReason = "prior-skip-reason";
        occ.RetryCount = 1;
        await SeedCronOccurrences(occ);

        var retryOnly = new InternalFunctionContext { TickerId = occ.Id }
            .SetProperty(x => x.RetryCount, 2);
        Assert.DoesNotContain(nameof(InternalFunctionContext.Status), retryOnly.GetPropsToUpdate());

        await _provider.UpdateCronTickerOccurrence(retryOnly, CancellationToken.None);

        using var ctx = CreateVerifyContext();
        var persisted = await ctx.Set<CronTickerOccurrenceEntity<CronTickerEntity>>()
            .AsNoTracking().SingleAsync(x => x.Id == occ.Id);
        Assert.Equal(2, persisted.RetryCount);
        Assert.Equal(TickerStatus.InProgress, persisted.Status);
        Assert.Equal("prior-skip-reason", persisted.SkippedReason);
    }

    [Fact]
    public async Task UpdateTimeTicker_SkippedStatus_StampsSkippedReason()
    {
        // Guards the other side of the contract: SkippedReason IS written when the update
        // carries a Skipped status transition.
        var token = Guid.NewGuid();
        var ticker = CreateTimeTicker(
            status: TickerStatus.InProgress, lockHolder: NodeId, lockedAt: _fixedNow);
        ticker.AcquisitionToken = token;
        await SeedTimeTickers(ticker);

        var skip = new InternalFunctionContext { TickerId = ticker.Id, AcquisitionToken = token }
            .SetProperty(x => x.Status, TickerStatus.Skipped)
            .SetProperty(x => x.ExceptionDetails, "cron overlap skip");

        var affected = await _provider.UpdateTimeTicker(skip, CancellationToken.None);
        Assert.Equal(1, affected);

        using var ctx = CreateVerifyContext();
        var persisted = await ctx.Set<TimeTickerEntity>().AsNoTracking().SingleAsync(x => x.Id == ticker.Id);
        Assert.Equal(TickerStatus.Skipped, persisted.Status);
        Assert.Equal("cron overlap skip", persisted.SkippedReason);
    }
}
