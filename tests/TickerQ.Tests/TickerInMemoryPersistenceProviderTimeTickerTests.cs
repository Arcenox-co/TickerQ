using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using TickerQ.Provider;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;
using Xunit;

namespace TickerQ.Tests;

public class TickerInMemoryPersistenceProviderTimeTickerTests : IAsyncLifetime
{
    private sealed class FakeTimeTicker : TimeTickerEntity<FakeTimeTicker> { }
    private sealed class FakeCronTicker : CronTickerEntity { }

    private readonly ITickerClock _clock;
    private readonly TickerInMemoryPersistenceProvider<FakeTimeTicker, FakeCronTicker> _provider;
    private readonly DateTime _now;
    private readonly string _nodeId;

    // Track IDs created by each test for deterministic cleanup
    private readonly List<Guid> _createdTimeTickerIds = new();

    public TickerInMemoryPersistenceProviderTimeTickerTests()
    {
        _now = new DateTime(2025, 6, 15, 12, 0, 0, DateTimeKind.Utc);
        _nodeId = "test-node-1";

        _clock = Substitute.For<ITickerClock>();
        _clock.UtcNow.Returns(_now);

        var optionsBuilder = new SchedulerOptionsBuilder { NodeIdentifier = _nodeId };

        var services = new ServiceCollection();
        services.AddSingleton(_clock);
        services.AddSingleton(optionsBuilder);
        var serviceProvider = services.BuildServiceProvider();

        _provider = new TickerInMemoryPersistenceProvider<FakeTimeTicker, FakeCronTicker>(serviceProvider);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_createdTimeTickerIds.Count > 0)
            await _provider.RemoveTimeTickers(_createdTimeTickerIds.ToArray(), CancellationToken.None);
    }

    #region Helpers

    private FakeTimeTicker CreateTicker(
        Guid? id = null,
        string function = "TestFunc",
        DateTime? executionTime = null,
        bool useDefaultExecutionTime = true,
        TickerStatus status = TickerStatus.Idle,
        string? lockHolder = null,
        DateTime? lockedAt = null,
        Guid? parentId = null,
        byte[]? request = null,
        int retries = 0,
        int[]? retryIntervals = null)
    {
        var tickerId = id ?? Guid.NewGuid();
        return new FakeTimeTicker
        {
            Id = tickerId,
            Function = function,
            ExecutionTime = executionTime ?? (useDefaultExecutionTime ? _now.AddMinutes(1) : (DateTime?)null),
            Status = status,
            LockHolder = lockHolder,
            LockedAt = lockedAt,
            ParentId = parentId,
            Request = request,
            Retries = retries,
            RetryIntervals = retryIntervals,
            CreatedAt = _now.AddMinutes(-10),
            UpdatedAt = _now.AddMinutes(-5)
        };
    }

    private async Task<FakeTimeTicker> InsertAndTrack(FakeTimeTicker ticker)
    {
        await _provider.AddTimeTickers(new[] { ticker }, CancellationToken.None);
        _createdTimeTickerIds.Add(ticker.Id);
        return ticker;
    }

    private static async Task<List<T>> CollectAsync<T>(IAsyncEnumerable<T> source)
    {
        var list = new List<T>();
        await foreach (var item in source)
            list.Add(item);
        return list;
    }

    #endregion

    #region InsertTimeTickers (AddTimeTickers)

    [Fact]
    public async Task AddTimeTickers_SingleTicker_ReturnsCountOneAndStoresCorrectly()
    {
        // Arrange
        var ticker = CreateTicker(function: "MyFunc", executionTime: _now.AddMinutes(5));

        // Act
        var count = await _provider.AddTimeTickers(new[] { ticker }, CancellationToken.None);
        _createdTimeTickerIds.Add(ticker.Id);

        // Assert
        Assert.Equal(1, count);
        var retrieved = await _provider.GetTimeTickerById(ticker.Id, CancellationToken.None);
        Assert.NotNull(retrieved);
        Assert.Equal(ticker.Id, retrieved.Id);
        Assert.Equal("MyFunc", retrieved.Function);
        Assert.Equal(_now.AddMinutes(5), retrieved.ExecutionTime);
        Assert.Equal(TickerStatus.Idle, retrieved.Status);
    }

    [Fact]
    public async Task AddTimeTickers_MultipleTickers_ReturnsCorrectCountAndAllStored()
    {
        // Arrange
        var ticker1 = CreateTicker(function: "Func1");
        var ticker2 = CreateTicker(function: "Func2");
        var ticker3 = CreateTicker(function: "Func3");

        // Act
        var count = await _provider.AddTimeTickers(new[] { ticker1, ticker2, ticker3 }, CancellationToken.None);
        _createdTimeTickerIds.Add(ticker1.Id);
        _createdTimeTickerIds.Add(ticker2.Id);
        _createdTimeTickerIds.Add(ticker3.Id);

        // Assert
        Assert.Equal(3, count);
        Assert.NotNull(await _provider.GetTimeTickerById(ticker1.Id, CancellationToken.None));
        Assert.NotNull(await _provider.GetTimeTickerById(ticker2.Id, CancellationToken.None));
        Assert.NotNull(await _provider.GetTimeTickerById(ticker3.Id, CancellationToken.None));
    }

    [Fact]
    public async Task AddTimeTickers_VerifiesAllPropertiesStoredCorrectly()
    {
        // Arrange
        var tickerId = Guid.NewGuid();
        var request = new byte[] { 1, 2, 3, 4 };
        var ticker = new FakeTimeTicker
        {
            Id = tickerId,
            Function = "DetailedFunc",
            ExecutionTime = _now.AddHours(2),
            Status = TickerStatus.Idle,
            Request = request,
            Retries = 3,
            RetryIntervals = new[] { 1000, 2000, 3000 },
            Description = "Test description",
            CreatedAt = _now.AddMinutes(-20),
            UpdatedAt = _now.AddMinutes(-10)
        };

        // Act
        await _provider.AddTimeTickers(new[] { ticker }, CancellationToken.None);
        _createdTimeTickerIds.Add(tickerId);

        // Assert
        var retrieved = await _provider.GetTimeTickerById(tickerId, CancellationToken.None);
        Assert.NotNull(retrieved);
        Assert.Equal(tickerId, retrieved.Id);
        Assert.Equal("DetailedFunc", retrieved.Function);
        Assert.Equal(_now.AddHours(2), retrieved.ExecutionTime);
        Assert.Equal(TickerStatus.Idle, retrieved.Status);
        Assert.Equal(request, retrieved.Request);
        Assert.Equal(3, retrieved.Retries);
        Assert.Equal(new[] { 1000, 2000, 3000 }, retrieved.RetryIntervals);
        Assert.Equal("Test description", retrieved.Description);
    }

    [Fact]
    public async Task AddTimeTickers_DuplicateId_DoesNotOverwrite()
    {
        // Arrange
        var tickerId = Guid.NewGuid();
        var ticker1 = CreateTicker(id: tickerId, function: "Original");
        var ticker2 = CreateTicker(id: tickerId, function: "Duplicate");

        // Act
        var count1 = await _provider.AddTimeTickers(new[] { ticker1 }, CancellationToken.None);
        _createdTimeTickerIds.Add(tickerId);
        var count2 = await _provider.AddTimeTickers(new[] { ticker2 }, CancellationToken.None);

        // Assert
        Assert.Equal(1, count1);
        Assert.Equal(0, count2);
        var retrieved = await _provider.GetTimeTickerById(tickerId, CancellationToken.None);
        Assert.Equal("Original", retrieved.Function);
    }

    #endregion

    #region UpdateTimeTickers

    [Fact]
    public async Task UpdateTimeTickers_ExistingTicker_UpdatesPropertiesAndReturnsCount()
    {
        // Arrange
        var ticker = CreateTicker(function: "BeforeUpdate");
        await InsertAndTrack(ticker);

        var updated = CreateTicker(id: ticker.Id, function: "AfterUpdate", executionTime: _now.AddHours(10));
        updated.CreatedAt = ticker.CreatedAt;
        updated.UpdatedAt = _now;
        updated.Description = "Updated description";

        // Act
        var count = await _provider.UpdateTimeTickers(new[] { updated }, CancellationToken.None);

        // Assert
        Assert.Equal(1, count);
        var retrieved = await _provider.GetTimeTickerById(ticker.Id, CancellationToken.None);
        Assert.Equal("AfterUpdate", retrieved.Function);
        Assert.Equal(_now.AddHours(10), retrieved.ExecutionTime);
        Assert.Equal("Updated description", retrieved.Description);
    }

    [Fact]
    public async Task UpdateTimeTickers_NonExistentTicker_AddsItInstead()
    {
        // Arrange - ticker not inserted, so Update should add it
        var ticker = CreateTicker(function: "NewViaUpdate");

        // Act
        var count = await _provider.UpdateTimeTickers(new[] { ticker }, CancellationToken.None);
        _createdTimeTickerIds.Add(ticker.Id);

        // Assert: UpdateTickerWithChildren falls back to AddTickerWithChildren for non-existent
        Assert.Equal(1, count);
        var retrieved = await _provider.GetTimeTickerById(ticker.Id, CancellationToken.None);
        Assert.NotNull(retrieved);
        Assert.Equal("NewViaUpdate", retrieved.Function);
    }

    [Fact]
    public async Task UpdateTimeTickers_MultipleTickersUpdated()
    {
        // Arrange
        var ticker1 = CreateTicker(function: "Func1");
        var ticker2 = CreateTicker(function: "Func2");
        await InsertAndTrack(ticker1);
        await InsertAndTrack(ticker2);

        var updated1 = CreateTicker(id: ticker1.Id, function: "Func1Updated");
        updated1.CreatedAt = ticker1.CreatedAt;
        updated1.UpdatedAt = _now;

        var updated2 = CreateTicker(id: ticker2.Id, function: "Func2Updated");
        updated2.CreatedAt = ticker2.CreatedAt;
        updated2.UpdatedAt = _now;

        // Act
        var count = await _provider.UpdateTimeTickers(new[] { updated1, updated2 }, CancellationToken.None);

        // Assert
        Assert.Equal(2, count);
        var r1 = await _provider.GetTimeTickerById(ticker1.Id, CancellationToken.None);
        var r2 = await _provider.GetTimeTickerById(ticker2.Id, CancellationToken.None);
        Assert.Equal("Func1Updated", r1.Function);
        Assert.Equal("Func2Updated", r2.Function);
    }

    #endregion

    #region RemoveTimeTickers

    [Fact]
    public async Task RemoveTimeTickers_ExistingTicker_RemovesAndReturnsCount()
    {
        // Arrange
        var ticker = CreateTicker();
        await _provider.AddTimeTickers(new[] { ticker }, CancellationToken.None);
        // Do NOT track because we are removing manually

        // Act
        var count = await _provider.RemoveTimeTickers(new[] { ticker.Id }, CancellationToken.None);

        // Assert
        Assert.Equal(1, count);
        var retrieved = await _provider.GetTimeTickerById(ticker.Id, CancellationToken.None);
        Assert.Null(retrieved);
    }

    [Fact]
    public async Task RemoveTimeTickers_NonExistentTicker_ReturnsZeroAndDoesNotCrash()
    {
        // Act
        var count = await _provider.RemoveTimeTickers(new[] { Guid.NewGuid() }, CancellationToken.None);

        // Assert
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task RemoveTimeTickers_CascadeDeletesChildren()
    {
        // Arrange: parent with child
        var childId = Guid.NewGuid();
        var child = CreateTicker(id: childId, function: "ChildFunc", useDefaultExecutionTime: false);

        var parentId = Guid.NewGuid();
        var parent = CreateTicker(id: parentId, function: "ParentFunc");
        parent.Children = new List<FakeTimeTicker> { child };

        await _provider.AddTimeTickers(new[] { parent }, CancellationToken.None);
        // Track only parent; children are cascade-deleted

        // Verify both exist
        Assert.NotNull(await _provider.GetTimeTickerById(parentId, CancellationToken.None));

        // Act
        var count = await _provider.RemoveTimeTickers(new[] { parentId }, CancellationToken.None);

        // Assert: parent + child removed = 2
        Assert.Equal(2, count);
        Assert.Null(await _provider.GetTimeTickerById(parentId, CancellationToken.None));
    }

    [Fact]
    public async Task RemoveTimeTickers_CleansUpChildrenIndex()
    {
        // Arrange: parent with child, then remove parent
        var childId = Guid.NewGuid();
        var child = CreateTicker(id: childId, function: "ChildFunc", useDefaultExecutionTime: false);

        var parentId = Guid.NewGuid();
        var parent = CreateTicker(id: parentId, function: "ParentFunc");
        parent.Children = new List<FakeTimeTicker> { child };

        await _provider.AddTimeTickers(new[] { parent }, CancellationToken.None);

        // Act
        await _provider.RemoveTimeTickers(new[] { parentId }, CancellationToken.None);

        // Assert: re-inserting a new parent with same ID should work cleanly
        var newParent = CreateTicker(id: parentId, function: "NewParent");
        var addCount = await _provider.AddTimeTickers(new[] { newParent }, CancellationToken.None);
        _createdTimeTickerIds.Add(parentId);
        Assert.Equal(1, addCount);

        // The new parent should have no children
        var retrieved = await _provider.GetTimeTickerById(parentId, CancellationToken.None);
        Assert.NotNull(retrieved);
        Assert.Empty(retrieved.Children);
    }

    #endregion

    #region GetTimeTickerById

    [Fact]
    public async Task GetTimeTickerById_ExistingTicker_ReturnsTicker()
    {
        // Arrange
        var ticker = CreateTicker(function: "FindMe");
        await InsertAndTrack(ticker);

        // Act
        var result = await _provider.GetTimeTickerById(ticker.Id, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(ticker.Id, result.Id);
        Assert.Equal("FindMe", result.Function);
    }

    [Fact]
    public async Task GetTimeTickerById_NonExistentId_ReturnsNull()
    {
        // Act
        var result = await _provider.GetTimeTickerById(Guid.NewGuid(), CancellationToken.None);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetTimeTickerById_ReturnsTickerWithChildren()
    {
        // Arrange
        var childId = Guid.NewGuid();
        var child = CreateTicker(id: childId, function: "ChildFunc", useDefaultExecutionTime: false);

        var parentId = Guid.NewGuid();
        var parent = CreateTicker(id: parentId, function: "ParentFunc");
        parent.Children = new List<FakeTimeTicker> { child };

        await _provider.AddTimeTickers(new[] { parent }, CancellationToken.None);
        _createdTimeTickerIds.Add(parentId);
        _createdTimeTickerIds.Add(childId);

        // Act
        var result = await _provider.GetTimeTickerById(parentId, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result.Children);
        Assert.Equal(childId, result.Children.First().Id);
    }

    #endregion

    #region GetTimeTickers

    [Fact]
    public async Task GetTimeTickers_NoPredicate_ReturnsAllRootTickers()
    {
        // Arrange
        var ticker1 = CreateTicker(function: "GetAll1", executionTime: _now.AddMinutes(1));
        var ticker2 = CreateTicker(function: "GetAll2", executionTime: _now.AddMinutes(2));
        await InsertAndTrack(ticker1);
        await InsertAndTrack(ticker2);

        // Act
        var results = await _provider.GetTimeTickers(null, CancellationToken.None);

        // Assert: should contain at least our two tickers
        Assert.True(results.Length >= 2);
        Assert.Contains(results, r => r.Id == ticker1.Id);
        Assert.Contains(results, r => r.Id == ticker2.Id);
    }

    [Fact]
    public async Task GetTimeTickers_FilterByFunctionName_ReturnsOnlyMatching()
    {
        // Arrange
        var ticker1 = CreateTicker(function: "UniqueFilterFunc_ABC");
        var ticker2 = CreateTicker(function: "OtherFunc_XYZ");
        await InsertAndTrack(ticker1);
        await InsertAndTrack(ticker2);

        // Act
        var results = await _provider.GetTimeTickers(x => x.Function == "UniqueFilterFunc_ABC", CancellationToken.None);

        // Assert
        Assert.All(results, r => Assert.Equal("UniqueFilterFunc_ABC", r.Function));
        Assert.Contains(results, r => r.Id == ticker1.Id);
        Assert.DoesNotContain(results, r => r.Id == ticker2.Id);
    }

    [Fact]
    public async Task GetTimeTickers_ExcludesChildTickers()
    {
        // Arrange: parent with child (child has ParentId set)
        var childId = Guid.NewGuid();
        var child = CreateTicker(id: childId, function: "ChildOnly", useDefaultExecutionTime: false);

        var parentId = Guid.NewGuid();
        var parent = CreateTicker(id: parentId, function: "ParentOnly");
        parent.Children = new List<FakeTimeTicker> { child };

        await _provider.AddTimeTickers(new[] { parent }, CancellationToken.None);
        _createdTimeTickerIds.Add(parentId);
        _createdTimeTickerIds.Add(childId);

        // Act
        var results = await _provider.GetTimeTickers(null, CancellationToken.None);

        // Assert: child tickers (with ParentId) should be excluded from root results
        Assert.DoesNotContain(results, r => r.Id == childId);
        Assert.Contains(results, r => r.Id == parentId);
    }

    [Fact]
    public async Task GetTimeTickers_OrderedByExecutionTimeDescending()
    {
        // Arrange
        var ticker1 = CreateTicker(function: "OrderTest1", executionTime: _now.AddMinutes(1));
        var ticker2 = CreateTicker(function: "OrderTest2", executionTime: _now.AddMinutes(10));
        var ticker3 = CreateTicker(function: "OrderTest3", executionTime: _now.AddMinutes(5));
        await InsertAndTrack(ticker1);
        await InsertAndTrack(ticker2);
        await InsertAndTrack(ticker3);

        // Act
        var results = await _provider.GetTimeTickers(
            x => x.Function.StartsWith("OrderTest"), CancellationToken.None);

        // Assert: should be ordered by ExecutionTime descending
        Assert.True(results.Length >= 3);
        var ourResults = results.Where(r =>
            r.Id == ticker1.Id || r.Id == ticker2.Id || r.Id == ticker3.Id).ToArray();
        Assert.Equal(3, ourResults.Length);
        Assert.Equal(ticker2.Id, ourResults[0].Id); // latest execution time first
        Assert.Equal(ticker3.Id, ourResults[1].Id);
        Assert.Equal(ticker1.Id, ourResults[2].Id);
    }

    #endregion

    #region GetTimeTickersPaginated

    [Fact]
    public async Task GetTimeTickersPaginated_ReturnsCorrectPageAndTotalCount()
    {
        // Arrange: insert 5 tickers with unique function prefix
        var tickers = new List<FakeTimeTicker>();
        for (int i = 0; i < 5; i++)
        {
            var t = CreateTicker(
                function: $"PaginateFunc_{Guid.NewGuid():N}",
                executionTime: _now.AddMinutes(i + 1));
            await InsertAndTrack(t);
            tickers.Add(t);
        }

        var allIds = tickers.Select(t => t.Id).ToHashSet();

        // Act: get page 1 with page size 2, no predicate filtering (get everything)
        // NOTE: other tests may have data in the static dict, so we use a predicate
        var result = await _provider.GetTimeTickersPaginated(
            x => allIds.Contains(x.Id),
            pageNumber: 1, pageSize: 2, CancellationToken.None);

        // Assert
        Assert.Equal(5, result.TotalCount);
        Assert.Equal(1, result.PageNumber);
        Assert.Equal(2, result.PageSize);
        Assert.Equal(2, result.Items.Count());
    }

    [Fact]
    public async Task GetTimeTickersPaginated_SecondPage_ReturnsCorrectItems()
    {
        // Arrange
        var tickers = new List<FakeTimeTicker>();
        for (int i = 0; i < 5; i++)
        {
            var t = CreateTicker(
                function: $"PaginatePage2_{Guid.NewGuid():N}",
                executionTime: _now.AddMinutes(i + 1));
            await InsertAndTrack(t);
            tickers.Add(t);
        }

        var allIds = tickers.Select(t => t.Id).ToHashSet();

        // Act
        var result = await _provider.GetTimeTickersPaginated(
            x => allIds.Contains(x.Id),
            pageNumber: 2, pageSize: 2, CancellationToken.None);

        // Assert
        Assert.Equal(5, result.TotalCount);
        Assert.Equal(2, result.PageNumber);
        Assert.Equal(2, result.PageSize);
        Assert.Equal(2, result.Items.Count());
    }

    [Fact]
    public async Task GetTimeTickersPaginated_LastPage_ReturnRemainingItems()
    {
        // Arrange
        var tickers = new List<FakeTimeTicker>();
        for (int i = 0; i < 5; i++)
        {
            var t = CreateTicker(
                function: $"PaginateLast_{Guid.NewGuid():N}",
                executionTime: _now.AddMinutes(i + 1));
            await InsertAndTrack(t);
            tickers.Add(t);
        }

        var allIds = tickers.Select(t => t.Id).ToHashSet();

        // Act: page 3 with page size 2 -> should have 1 item
        var result = await _provider.GetTimeTickersPaginated(
            x => allIds.Contains(x.Id),
            pageNumber: 3, pageSize: 2, CancellationToken.None);

        // Assert
        Assert.Equal(5, result.TotalCount);
        Assert.Single(result.Items);
    }

    [Fact]
    public async Task GetTimeTickersPaginated_ExcludesChildTickers()
    {
        // Arrange: parent with child
        var childId = Guid.NewGuid();
        var child = CreateTicker(id: childId, function: "PaginateChild", useDefaultExecutionTime: false);

        var parentId = Guid.NewGuid();
        var parent = CreateTicker(id: parentId, function: "PaginateParent");
        parent.Children = new List<FakeTimeTicker> { child };

        await _provider.AddTimeTickers(new[] { parent }, CancellationToken.None);
        _createdTimeTickerIds.Add(parentId);
        _createdTimeTickerIds.Add(childId);

        // Act
        var result = await _provider.GetTimeTickersPaginated(
            x => x.Id == parentId || x.Id == childId,
            pageNumber: 1, pageSize: 10, CancellationToken.None);

        // Assert: only the parent counts (root only), child excluded from pagination
        Assert.Equal(1, result.TotalCount);
        Assert.Single(result.Items);
        Assert.Equal(parentId, result.Items.First().Id);
    }

    #endregion

    #region GetEarliestTimeTickers

    [Fact]
    public async Task GetEarliestTimeTickers_ReturnsEarliestByExecutionTime()
    {
        // Arrange: two acquirable tickers at different times within the scheduler window
        var early = CreateTicker(function: "EarliestFunc", executionTime: _now.AddSeconds(1));
        var late = CreateTicker(function: "LaterFunc", executionTime: _now.AddMinutes(5));
        await InsertAndTrack(early);
        await InsertAndTrack(late);

        // Act
        var results = await _provider.GetEarliestTimeTickers(CancellationToken.None);

        // Assert: the earliest ticker within the same second should be returned
        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.Id == early.Id);
    }

    [Fact]
    public async Task GetEarliestTimeTickers_SkipsNonAcquirableTickers()
    {
        // Arrange: one non-acquirable (InProgress, locked by other), one acquirable (Idle)
        var nonAcquirable = CreateTicker(
            function: "NonAcq",
            executionTime: _now.AddSeconds(1),
            status: TickerStatus.InProgress,
            lockHolder: "other-node",
            lockedAt: _now);
        var acquirable = CreateTicker(
            function: "Acquirable",
            executionTime: _now.AddSeconds(2));
        await InsertAndTrack(nonAcquirable);
        await InsertAndTrack(acquirable);

        // Act
        var results = await _provider.GetEarliestTimeTickers(CancellationToken.None);

        // Assert: InProgress with foreign lock is not acquirable
        Assert.DoesNotContain(results, r => r.Id == nonAcquirable.Id);
    }

    [Fact]
    public async Task GetEarliestTimeTickers_ReturnsEmptyWhenNoAcquirableTickersExist()
    {
        // Arrange: only InProgress tickers locked by another node
        var locked = CreateTicker(
            function: "Locked",
            executionTime: _now.AddSeconds(1),
            status: TickerStatus.InProgress,
            lockHolder: "other-node",
            lockedAt: _now);
        await InsertAndTrack(locked);

        // Act
        var results = await _provider.GetEarliestTimeTickers(CancellationToken.None);

        // Assert: the locked ticker should not be returned
        Assert.DoesNotContain(results, r => r.Id == locked.Id);
    }

    [Fact]
    public async Task GetEarliestTimeTickers_SkipsTickersOlderThanOneSecond()
    {
        // Arrange: ticker with execution time more than 1 second in the past
        var old = CreateTicker(
            function: "OldTicker",
            executionTime: _now.AddSeconds(-5));
        await InsertAndTrack(old);

        // Act
        var results = await _provider.GetEarliestTimeTickers(CancellationToken.None);

        // Assert: should not include tickers with ExecutionTime < now - 1 second
        Assert.DoesNotContain(results, r => r.Id == old.Id);
    }

    [Fact]
    public async Task GetEarliestTimeTickers_SkipsTickersWithNullExecutionTime()
    {
        // Arrange
        var noExecTime = CreateTicker(function: "NoExecTime", useDefaultExecutionTime: false);
        await InsertAndTrack(noExecTime);

        // Act
        var results = await _provider.GetEarliestTimeTickers(CancellationToken.None);

        // Assert: null ExecutionTime tickers are filtered out
        Assert.DoesNotContain(results, r => r.Id == noExecTime.Id);
    }

    [Fact]
    public async Task GetEarliestTimeTickers_ReturnsAllTickersWithinSameSecondBucket()
    {
        // Arrange: two tickers within the same second
        var ticker1 = CreateTicker(
            function: "SameSecond1",
            executionTime: _now.AddSeconds(1));
        var ticker2 = CreateTicker(
            function: "SameSecond2",
            executionTime: _now.AddSeconds(1).AddMilliseconds(500));
        var tickerLater = CreateTicker(
            function: "LaterSecond",
            executionTime: _now.AddSeconds(5));
        await InsertAndTrack(ticker1);
        await InsertAndTrack(ticker2);
        await InsertAndTrack(tickerLater);

        // Act
        var results = await _provider.GetEarliestTimeTickers(CancellationToken.None);

        // Assert: both tickers within the same second bucket should be returned
        Assert.Contains(results, r => r.Id == ticker1.Id);
        Assert.Contains(results, r => r.Id == ticker2.Id);
        // The later ticker should NOT be returned (different second bucket)
        Assert.DoesNotContain(results, r => r.Id == tickerLater.Id);
    }

    #endregion

    #region AddTickerWithChildren

    [Fact]
    public async Task AddTickerWithChildren_ParentWithChildren_PopulatesChildrenIndex()
    {
        // Arrange
        var childId = Guid.NewGuid();
        var child = CreateTicker(id: childId, function: "ChildFunc", useDefaultExecutionTime: false);

        var parentId = Guid.NewGuid();
        var parent = CreateTicker(id: parentId, function: "ParentFunc");
        parent.Children = new List<FakeTimeTicker> { child };

        // Act
        var count = await _provider.AddTimeTickers(new[] { parent }, CancellationToken.None);
        _createdTimeTickerIds.Add(parentId);
        _createdTimeTickerIds.Add(childId);

        // Assert: count should include both parent and child
        Assert.Equal(2, count);

        // Verify the parent has children when retrieved
        var retrieved = await _provider.GetTimeTickerById(parentId, CancellationToken.None);
        Assert.NotNull(retrieved);
        Assert.Single(retrieved.Children);
        Assert.Equal(childId, retrieved.Children.First().Id);
    }

    [Fact]
    public async Task AddTickerWithChildren_ChildrenHaveParentIdSet()
    {
        // Arrange
        var childId = Guid.NewGuid();
        var child = CreateTicker(id: childId, function: "ChildWithParent", useDefaultExecutionTime: false);
        // Note: child.ParentId is NOT explicitly set; AddTickerWithChildren should set it

        var parentId = Guid.NewGuid();
        var parent = CreateTicker(id: parentId, function: "Parent");
        parent.Children = new List<FakeTimeTicker> { child };

        // Act
        await _provider.AddTimeTickers(new[] { parent }, CancellationToken.None);
        _createdTimeTickerIds.Add(parentId);
        _createdTimeTickerIds.Add(childId);

        // Assert: the child should have its ParentId set to the parent's ID
        // We verify by checking that GetTimeTickers excludes the child (ParentId != null)
        var rootTickers = await _provider.GetTimeTickers(
            x => x.Id == childId || x.Id == parentId, CancellationToken.None);
        // Only root tickers (ParentId == null) are returned
        Assert.Single(rootTickers);
        Assert.Equal(parentId, rootTickers[0].Id);
    }

    [Fact]
    public async Task AddTickerWithChildren_MultipleChildren()
    {
        // Arrange
        var child1 = CreateTicker(id: Guid.NewGuid(), function: "Child1", useDefaultExecutionTime: false);
        var child2 = CreateTicker(id: Guid.NewGuid(), function: "Child2", useDefaultExecutionTime: false);
        var child3 = CreateTicker(id: Guid.NewGuid(), function: "Child3", useDefaultExecutionTime: false);

        var parentId = Guid.NewGuid();
        var parent = CreateTicker(id: parentId, function: "ParentMultiple");
        parent.Children = new List<FakeTimeTicker> { child1, child2, child3 };

        // Act
        var count = await _provider.AddTimeTickers(new[] { parent }, CancellationToken.None);
        _createdTimeTickerIds.Add(parentId);
        _createdTimeTickerIds.Add(child1.Id);
        _createdTimeTickerIds.Add(child2.Id);
        _createdTimeTickerIds.Add(child3.Id);

        // Assert
        Assert.Equal(4, count); // parent + 3 children
        var retrieved = await _provider.GetTimeTickerById(parentId, CancellationToken.None);
        Assert.Equal(3, retrieved.Children.Count);
    }

    [Fact]
    public async Task AddTickerWithChildren_GrandChildren()
    {
        // Arrange: parent -> child -> grandchild
        var grandChildId = Guid.NewGuid();
        var grandChild = CreateTicker(id: grandChildId, function: "GrandChild", useDefaultExecutionTime: false);

        var childId = Guid.NewGuid();
        var child = CreateTicker(id: childId, function: "Child", useDefaultExecutionTime: false);
        child.Children = new List<FakeTimeTicker> { grandChild };

        var parentId = Guid.NewGuid();
        var parent = CreateTicker(id: parentId, function: "Parent");
        parent.Children = new List<FakeTimeTicker> { child };

        // Act
        var count = await _provider.AddTimeTickers(new[] { parent }, CancellationToken.None);
        _createdTimeTickerIds.Add(parentId);
        _createdTimeTickerIds.Add(childId);
        _createdTimeTickerIds.Add(grandChildId);

        // Assert
        Assert.Equal(3, count);
        var retrieved = await _provider.GetTimeTickerById(parentId, CancellationToken.None);
        Assert.Single(retrieved.Children);
        var retrievedChild = retrieved.Children.First();
        Assert.Equal(childId, retrievedChild.Id);
        Assert.Single(retrievedChild.Children);
        Assert.Equal(grandChildId, retrievedChild.Children.First().Id);
    }

    #endregion

    #region UpdateTickerWithChildren

    [Fact]
    public async Task UpdateTickerWithChildren_UpdatesParentAndExistingChildren()
    {
        // Arrange: insert parent with child
        var childId = Guid.NewGuid();
        var child = CreateTicker(id: childId, function: "ChildBefore", useDefaultExecutionTime: false);

        var parentId = Guid.NewGuid();
        var parent = CreateTicker(id: parentId, function: "ParentBefore");
        parent.Children = new List<FakeTimeTicker> { child };

        await _provider.AddTimeTickers(new[] { parent }, CancellationToken.None);
        _createdTimeTickerIds.Add(parentId);
        _createdTimeTickerIds.Add(childId);

        // Update parent and child
        var updatedChild = CreateTicker(id: childId, function: "ChildAfter", useDefaultExecutionTime: false);
        var updatedParent = CreateTicker(id: parentId, function: "ParentAfter");
        updatedParent.Children = new List<FakeTimeTicker> { updatedChild };

        // Act
        var count = await _provider.UpdateTimeTickers(new[] { updatedParent }, CancellationToken.None);

        // Assert
        Assert.Equal(2, count); // parent + child
        var retrieved = await _provider.GetTimeTickerById(parentId, CancellationToken.None);
        Assert.Equal("ParentAfter", retrieved.Function);
        Assert.Single(retrieved.Children);
        Assert.Equal("ChildAfter", retrieved.Children.First().Function);
    }

    [Fact]
    public async Task UpdateTickerWithChildren_AddsNewChildren()
    {
        // Arrange: insert parent without children
        var parentId = Guid.NewGuid();
        var parent = CreateTicker(id: parentId, function: "ParentNoChildren");
        await _provider.AddTimeTickers(new[] { parent }, CancellationToken.None);
        _createdTimeTickerIds.Add(parentId);

        // Update with new child (child does not exist yet, so it should be added)
        var newChildId = Guid.NewGuid();
        var newChild = CreateTicker(id: newChildId, function: "NewChild", useDefaultExecutionTime: false);
        var updatedParent = CreateTicker(id: parentId, function: "ParentWithNewChild");
        updatedParent.Children = new List<FakeTimeTicker> { newChild };

        // Act
        var count = await _provider.UpdateTimeTickers(new[] { updatedParent }, CancellationToken.None);
        _createdTimeTickerIds.Add(newChildId);

        // Assert: parent updated + new child added = 2
        Assert.Equal(2, count);
        var retrieved = await _provider.GetTimeTickerById(parentId, CancellationToken.None);
        Assert.Single(retrieved.Children);
        Assert.Equal(newChildId, retrieved.Children.First().Id);
    }

    [Fact]
    public async Task UpdateTickerWithChildren_UpdatesParentIdOnParentChange()
    {
        // Arrange: insert a standalone ticker
        var tickerId = Guid.NewGuid();
        var ticker = CreateTicker(id: tickerId, function: "Standalone");
        await InsertAndTrack(ticker);

        // Create a new parent and update the ticker as its child
        var parentId = Guid.NewGuid();
        var newParent = CreateTicker(id: parentId, function: "NewParent");
        newParent.Children = new List<FakeTimeTicker>
        {
            CreateTicker(id: tickerId, function: "NowAChild", useDefaultExecutionTime: false)
        };

        await _provider.AddTimeTickers(new[] { newParent }, CancellationToken.None);
        _createdTimeTickerIds.Add(parentId);

        // Act: update the parent (which triggers child updates)
        var updatedChild = CreateTicker(id: tickerId, function: "StillAChild", useDefaultExecutionTime: false);
        var updatedParent = CreateTicker(id: parentId, function: "UpdatedParent");
        updatedParent.Children = new List<FakeTimeTicker> { updatedChild };

        var count = await _provider.UpdateTimeTickers(new[] { updatedParent }, CancellationToken.None);

        // Assert
        Assert.True(count >= 1);
        var retrieved = await _provider.GetTimeTickerById(parentId, CancellationToken.None);
        Assert.NotNull(retrieved);
        Assert.Contains(retrieved.Children, c => c.Id == tickerId);
    }

    #endregion

    #region AcquireImmediateTimeTickersAsync

    [Fact]
    public async Task AcquireImmediateTimeTickersAsync_AcquiresIdleTicker()
    {
        // Arrange
        var ticker = CreateTicker(function: "AcquireMe", status: TickerStatus.Idle);
        await InsertAndTrack(ticker);

        // Act
        var result = await _provider.AcquireImmediateTimeTickersAsync(
            new[] { ticker.Id }, CancellationToken.None);

        // Assert: ForQueueTimeTickers maps to a new TimeTickerEntity that does not copy
        // Status/LockHolder/LockedAt, so verify the returned Id and check stored state.
        Assert.Single(result);
        Assert.Equal(ticker.Id, result[0].Id);

        var stored = await _provider.GetTimeTickerById(ticker.Id, CancellationToken.None);
        Assert.Equal(TickerStatus.InProgress, stored.Status);
        Assert.Equal(_nodeId, stored.LockHolder);
        Assert.Equal(_now, stored.LockedAt);
        Assert.Equal(_now, stored.UpdatedAt);
    }

    [Fact]
    public async Task AcquireImmediateTimeTickersAsync_SkipsLockedTicker()
    {
        // Arrange: ticker locked by another node with InProgress status
        var ticker = CreateTicker(
            function: "LockedTicker",
            status: TickerStatus.InProgress,
            lockHolder: "other-node",
            lockedAt: _now.AddMinutes(-1));
        await InsertAndTrack(ticker);

        // Act
        var result = await _provider.AcquireImmediateTimeTickersAsync(
            new[] { ticker.Id }, CancellationToken.None);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task AcquireImmediateTimeTickersAsync_EmptyArray_ReturnsEmpty()
    {
        // Act
        var result = await _provider.AcquireImmediateTimeTickersAsync(
            Array.Empty<Guid>(), CancellationToken.None);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task AcquireImmediateTimeTickersAsync_NullArray_ReturnsEmpty()
    {
        // Act
        var result = await _provider.AcquireImmediateTimeTickersAsync(
            null, CancellationToken.None);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task AcquireImmediateTimeTickersAsync_NonExistentId_ReturnsEmpty()
    {
        // Act
        var result = await _provider.AcquireImmediateTimeTickersAsync(
            new[] { Guid.NewGuid() }, CancellationToken.None);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task AcquireImmediateTimeTickersAsync_AcquiresQueuedTickerOwnedBySameNode()
    {
        // Arrange: queued ticker owned by the same node
        var ticker = CreateTicker(
            function: "QueuedOwned",
            status: TickerStatus.Queued,
            lockHolder: _nodeId,
            lockedAt: _now.AddMinutes(-1));
        await InsertAndTrack(ticker);

        // Act
        var result = await _provider.AcquireImmediateTimeTickersAsync(
            new[] { ticker.Id }, CancellationToken.None);

        // Assert: Queued + same lock holder = acquirable
        Assert.Single(result);
        var stored = await _provider.GetTimeTickerById(ticker.Id, CancellationToken.None);
        Assert.Equal(TickerStatus.InProgress, stored.Status);
    }

    [Fact]
    public async Task AcquireImmediateTimeTickersAsync_SkipsQueuedTickerLockedByOtherNode()
    {
        // Arrange: queued ticker locked by another node
        var ticker = CreateTicker(
            function: "QueuedOther",
            status: TickerStatus.Queued,
            lockHolder: "other-node",
            lockedAt: _now.AddMinutes(-1));
        await InsertAndTrack(ticker);

        // Act
        var result = await _provider.AcquireImmediateTimeTickersAsync(
            new[] { ticker.Id }, CancellationToken.None);

        // Assert: Queued + different lock holder + LockedAt not null = not acquirable
        Assert.Empty(result);
    }

    [Fact]
    public async Task AcquireImmediateTimeTickersAsync_AcquiresMultipleIdleTickers()
    {
        // Arrange
        var ticker1 = CreateTicker(function: "Multi1");
        var ticker2 = CreateTicker(function: "Multi2");
        await InsertAndTrack(ticker1);
        await InsertAndTrack(ticker2);

        // Act
        var result = await _provider.AcquireImmediateTimeTickersAsync(
            new[] { ticker1.Id, ticker2.Id }, CancellationToken.None);

        // Assert: ForQueueTimeTickers returns mapped TimeTickerEntity (Status not copied),
        // so verify count and check stored state instead
        Assert.Equal(2, result.Length);

        var stored1 = await _provider.GetTimeTickerById(ticker1.Id, CancellationToken.None);
        var stored2 = await _provider.GetTimeTickerById(ticker2.Id, CancellationToken.None);
        Assert.Equal(TickerStatus.InProgress, stored1.Status);
        Assert.Equal(_nodeId, stored1.LockHolder);
        Assert.Equal(TickerStatus.InProgress, stored2.Status);
        Assert.Equal(_nodeId, stored2.LockHolder);
    }

    [Fact]
    public async Task AcquireImmediateTimeTickersAsync_ReturnsTickerWithChildren()
    {
        // Arrange: parent with child (child has null ExecutionTime)
        var childId = Guid.NewGuid();
        var child = CreateTicker(id: childId, function: "AcqChild", useDefaultExecutionTime: false);

        var parentId = Guid.NewGuid();
        var parent = CreateTicker(id: parentId, function: "AcqParent");
        parent.Children = new List<FakeTimeTicker> { child };

        await _provider.AddTimeTickers(new[] { parent }, CancellationToken.None);
        _createdTimeTickerIds.Add(parentId);
        _createdTimeTickerIds.Add(childId);

        // Act
        var result = await _provider.AcquireImmediateTimeTickersAsync(
            new[] { parentId }, CancellationToken.None);

        // Assert: acquired parent should include children via ForQueueTimeTickers
        Assert.Single(result);
        Assert.Equal(parentId, result[0].Id);
        // ForQueueTimeTickers includes children with null ExecutionTime
        Assert.NotEmpty(result[0].Children);
    }

    #endregion

    #region ReleaseAcquiredTimeTickers

    [Fact]
    public async Task ReleaseAcquiredTimeTickers_ReleasesOwnedQueuedTicker()
    {
        // Arrange: queued ticker owned by our node
        var ticker = CreateTicker(
            function: "ReleaseMe",
            status: TickerStatus.Queued,
            lockHolder: _nodeId,
            lockedAt: _now.AddMinutes(-1));
        await InsertAndTrack(ticker);

        // Act
        await _provider.ReleaseAcquiredTimeTickers(new[] { ticker.Id }, CancellationToken.None);

        // Assert: should be reset to Idle with no lock
        var retrieved = await _provider.GetTimeTickerById(ticker.Id, CancellationToken.None);
        Assert.Equal(TickerStatus.Idle, retrieved.Status);
        Assert.Null(retrieved.LockHolder);
        Assert.Null(retrieved.LockedAt);
    }

    [Fact]
    public async Task ReleaseAcquiredTimeTickers_SkipsForeignLockedTicker()
    {
        // Arrange: ticker locked by another node (InProgress)
        var ticker = CreateTicker(
            function: "ForeignLocked",
            status: TickerStatus.InProgress,
            lockHolder: "other-node",
            lockedAt: _now.AddMinutes(-1));
        await InsertAndTrack(ticker);

        // Act
        await _provider.ReleaseAcquiredTimeTickers(new[] { ticker.Id }, CancellationToken.None);

        // Assert: should NOT be released (InProgress + other-node is not acquirable)
        var retrieved = await _provider.GetTimeTickerById(ticker.Id, CancellationToken.None);
        Assert.Equal(TickerStatus.InProgress, retrieved.Status);
        Assert.Equal("other-node", retrieved.LockHolder);
    }

    [Fact]
    public async Task ReleaseAcquiredTimeTickers_ReleasesIdleTickerWithNullLock()
    {
        // Arrange: idle ticker with null lock (acquirable)
        var ticker = CreateTicker(function: "IdleNullLock", status: TickerStatus.Idle);
        await InsertAndTrack(ticker);

        // Act
        await _provider.ReleaseAcquiredTimeTickers(new[] { ticker.Id }, CancellationToken.None);

        // Assert: Idle + null LockedAt = acquirable, so it gets released
        var retrieved = await _provider.GetTimeTickerById(ticker.Id, CancellationToken.None);
        Assert.Equal(TickerStatus.Idle, retrieved.Status);
        Assert.Null(retrieved.LockHolder);
        Assert.Null(retrieved.LockedAt);
        Assert.Equal(_now, retrieved.UpdatedAt);
    }

    [Fact]
    public async Task ReleaseAcquiredTimeTickers_EmptyArray_ReleasesAllAcquirable()
    {
        // Arrange: two acquirable tickers
        var ticker1 = CreateTicker(
            function: "ReleaseAll1",
            status: TickerStatus.Queued,
            lockHolder: _nodeId,
            lockedAt: _now);
        var ticker2 = CreateTicker(
            function: "ReleaseAll2",
            status: TickerStatus.Idle);
        await InsertAndTrack(ticker1);
        await InsertAndTrack(ticker2);

        // Act: empty array means release ALL acquirable
        await _provider.ReleaseAcquiredTimeTickers(Array.Empty<Guid>(), CancellationToken.None);

        // Assert
        var r1 = await _provider.GetTimeTickerById(ticker1.Id, CancellationToken.None);
        var r2 = await _provider.GetTimeTickerById(ticker2.Id, CancellationToken.None);
        Assert.Equal(TickerStatus.Idle, r1.Status);
        Assert.Null(r1.LockHolder);
        Assert.Equal(TickerStatus.Idle, r2.Status);
        Assert.Null(r2.LockHolder);
    }

    #endregion

    #region ReleaseDeadNodeTimeTickerResources

    [Fact]
    public async Task ReleaseDeadNodeTimeTickerResources_ReleasesIdleQueuedFromDeadNode()
    {
        // Arrange: ticker idle/queued owned by dead node
        var deadNode = "dead-node-tt-1";
        var ticker = CreateTicker(
            function: "DeadNodeIdle",
            status: TickerStatus.Queued,
            lockHolder: deadNode,
            lockedAt: _now.AddMinutes(-5));
        await InsertAndTrack(ticker);

        // Act
        await _provider.ReleaseDeadNodeTimeTickerResources(deadNode, CancellationToken.None);

        // Assert: should be released to Idle with null lock
        var retrieved = await _provider.GetTimeTickerById(ticker.Id, CancellationToken.None);
        Assert.Equal(TickerStatus.Idle, retrieved.Status);
        Assert.Null(retrieved.LockHolder);
        Assert.Null(retrieved.LockedAt);
        Assert.Equal(_now, retrieved.UpdatedAt);
    }

    [Fact]
    public async Task ReleaseDeadNodeTimeTickerResources_MarksInProgressAsSkipped()
    {
        // Arrange: ticker in-progress owned by dead node
        var deadNode = "dead-node-tt-2";
        var ticker = CreateTicker(
            function: "DeadNodeInProgress",
            status: TickerStatus.InProgress,
            lockHolder: deadNode,
            lockedAt: _now.AddMinutes(-5));
        await InsertAndTrack(ticker);

        // Act
        await _provider.ReleaseDeadNodeTimeTickerResources(deadNode, CancellationToken.None);

        // Assert: should be marked as Skipped
        var retrieved = await _provider.GetTimeTickerById(ticker.Id, CancellationToken.None);
        Assert.Equal(TickerStatus.Skipped, retrieved.Status);
        Assert.Equal("Node is not alive!", retrieved.SkippedReason);
        Assert.Equal(_now, retrieved.ExecutedAt);
        Assert.Equal(_now, retrieved.UpdatedAt);
    }

    [Fact]
    public async Task ReleaseDeadNodeTimeTickerResources_DoesNotAffectOtherNodeTickers()
    {
        // Arrange: ticker owned by alive node
        var deadNode = "dead-node-tt-3";
        var aliveNode = "alive-node-tt";
        var ticker = CreateTicker(
            function: "AliveNodeTicker",
            status: TickerStatus.InProgress,
            lockHolder: aliveNode,
            lockedAt: _now.AddMinutes(-1));
        await InsertAndTrack(ticker);

        // Act: release dead node resources
        await _provider.ReleaseDeadNodeTimeTickerResources(deadNode, CancellationToken.None);

        // Assert: alive node's ticker should not be affected
        var retrieved = await _provider.GetTimeTickerById(ticker.Id, CancellationToken.None);
        Assert.Equal(TickerStatus.InProgress, retrieved.Status);
        Assert.Equal(aliveNode, retrieved.LockHolder);
    }

    [Fact]
    public async Task ReleaseDeadNodeTimeTickerResources_ReleasesIdleWithNullLock()
    {
        // Arrange: idle ticker with null LockHolder (acquirable by dead node logic because LockedAt == null)
        var deadNode = "dead-node-tt-4";
        var ticker = CreateTicker(
            function: "IdleNullLockDead",
            status: TickerStatus.Idle);
        await InsertAndTrack(ticker);

        // Act
        await _provider.ReleaseDeadNodeTimeTickerResources(deadNode, CancellationToken.None);

        // Assert: Phase 1 matches (Idle && LockedAt == null), so it gets reset
        var retrieved = await _provider.GetTimeTickerById(ticker.Id, CancellationToken.None);
        Assert.Equal(TickerStatus.Idle, retrieved.Status);
        Assert.Null(retrieved.LockHolder);
        Assert.Equal(_now, retrieved.UpdatedAt);
    }

    [Fact]
    public async Task ReleaseDeadNodeTimeTickerResources_DoesNotAffectDoneOrFailedTickers()
    {
        // Arrange: Done ticker owned by dead node
        var deadNode = "dead-node-tt-5";
        var doneTicker = CreateTicker(
            function: "DoneTicker",
            status: TickerStatus.Done,
            lockHolder: deadNode,
            lockedAt: _now.AddMinutes(-5));
        await InsertAndTrack(doneTicker);

        // Act
        await _provider.ReleaseDeadNodeTimeTickerResources(deadNode, CancellationToken.None);

        // Assert: Done status should not match either phase
        var retrieved = await _provider.GetTimeTickerById(doneTicker.Id, CancellationToken.None);
        Assert.Equal(TickerStatus.Done, retrieved.Status);
        Assert.Equal(deadNode, retrieved.LockHolder);
    }

    [Fact]
    public async Task ReleaseDeadNodeTimeTickerResources_HandlesMultipleTickersInBothPhases()
    {
        // Arrange
        var deadNode = "dead-node-tt-6";
        var queuedTicker = CreateTicker(
            function: "QueuedDead",
            status: TickerStatus.Queued,
            lockHolder: deadNode,
            lockedAt: _now.AddMinutes(-5));
        var inProgressTicker = CreateTicker(
            function: "InProgressDead",
            status: TickerStatus.InProgress,
            lockHolder: deadNode,
            lockedAt: _now.AddMinutes(-3));
        await InsertAndTrack(queuedTicker);
        await InsertAndTrack(inProgressTicker);

        // Act
        await _provider.ReleaseDeadNodeTimeTickerResources(deadNode, CancellationToken.None);

        // Assert
        var retrievedQueued = await _provider.GetTimeTickerById(queuedTicker.Id, CancellationToken.None);
        Assert.Equal(TickerStatus.Idle, retrievedQueued.Status);
        Assert.Null(retrievedQueued.LockHolder);

        var retrievedInProgress = await _provider.GetTimeTickerById(inProgressTicker.Id, CancellationToken.None);
        Assert.Equal(TickerStatus.Skipped, retrievedInProgress.Status);
        Assert.Equal("Node is not alive!", retrievedInProgress.SkippedReason);
    }

    #endregion
}
