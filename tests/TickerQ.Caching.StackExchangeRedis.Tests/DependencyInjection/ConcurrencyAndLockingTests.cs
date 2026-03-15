using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using TickerQ.DependencyInjection;
using TickerQ.Provider;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;

namespace TickerQ.Caching.StackExchangeRedis.Tests.DependencyInjection;

/// <summary>
/// Tests verifying lock acquisition, release, dead node recovery, concurrent access,
/// and per-function concurrency gating — patterns shared by both in-memory and Redis providers.
/// Uses the in-memory provider to test lock semantics without requiring a Redis connection.
/// </summary>
public class ConcurrencyAndLockingTests : IAsyncLifetime
{
    private sealed class TestTimeTicker : TimeTickerEntity<TestTimeTicker> { }
    private sealed class TestCronTicker : CronTickerEntity { }

    private readonly ITickerClock _clock;
    private readonly DateTime _now;
    private const string Node1 = "node-1";
    private const string Node2 = "node-2";

    private TickerInMemoryPersistenceProvider<TestTimeTicker, TestCronTicker> _providerNode1;
    private TickerInMemoryPersistenceProvider<TestTimeTicker, TestCronTicker> _providerNode2;
    private ITickerFunctionConcurrencyGate _concurrencyGate;

    private readonly List<Guid> _createdTimeTickerIds = new();
    private readonly List<Guid> _createdCronTickerIds = new();
    private readonly List<Guid> _createdCronOccurrenceIds = new();

    public ConcurrencyAndLockingTests()
    {
        _now = new DateTime(2026, 3, 15, 12, 0, 0, DateTimeKind.Utc);
        _clock = Substitute.For<ITickerClock>();
        _clock.UtcNow.Returns(_now);
    }

    public Task InitializeAsync()
    {
        _providerNode1 = CreateProvider(Node1);
        _providerNode2 = CreateProvider(Node2);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTickerQ();
        var sp = services.BuildServiceProvider();
        _concurrencyGate = sp.GetRequiredService<ITickerFunctionConcurrencyGate>();

        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_createdTimeTickerIds.Count > 0)
            await _providerNode1.RemoveTimeTickers(_createdTimeTickerIds.ToArray());
        if (_createdCronOccurrenceIds.Count > 0)
            await _providerNode1.RemoveCronTickerOccurrences(_createdCronOccurrenceIds.ToArray(), CancellationToken.None);
        if (_createdCronTickerIds.Count > 0)
            await _providerNode1.RemoveCronTickers(_createdCronTickerIds.ToArray(), CancellationToken.None);
    }

    private TickerInMemoryPersistenceProvider<TestTimeTicker, TestCronTicker> CreateProvider(string nodeId)
    {
        var optionsBuilder = new SchedulerOptionsBuilder { NodeIdentifier = nodeId };
        var services = new ServiceCollection();
        services.AddSingleton(_clock);
        services.AddSingleton(optionsBuilder);
        var sp = services.BuildServiceProvider();
        return new TickerInMemoryPersistenceProvider<TestTimeTicker, TestCronTicker>(sp);
    }

    private async Task<List<T>> CollectAsync<T>(IAsyncEnumerable<T> source)
    {
        var list = new List<T>();
        await foreach (var item in source)
            list.Add(item);
        return list;
    }

    private async Task<TestTimeTicker> SeedTimeTicker(
        Guid? id = null,
        TickerStatus status = TickerStatus.Idle,
        string lockHolder = null,
        DateTime? lockedAt = null,
        DateTime? executionTime = null)
    {
        var ticker = new TestTimeTicker
        {
            Id = id ?? Guid.NewGuid(),
            Function = "TestFunc",
            ExecutionTime = executionTime ?? _now.AddMinutes(1),
            Status = status,
            LockHolder = lockHolder,
            LockedAt = lockedAt,
            CreatedAt = _now.AddHours(-1),
            UpdatedAt = _now.AddHours(-1)
        };
        await _providerNode1.AddTimeTickers([ticker]);
        _createdTimeTickerIds.Add(ticker.Id);
        return ticker;
    }

    // =========================================================================
    // Lock Acquisition — Only Idle/Queued tickers with matching lock holder
    // =========================================================================

    [Fact]
    public async Task QueueTimeTickers_Node1AcquiresLock_SetsLockHolderAndStatus()
    {
        var ticker = await SeedTimeTicker();

        var toQueue = new TimeTickerEntity { Id = ticker.Id, UpdatedAt = ticker.UpdatedAt };
        var results = await CollectAsync(_providerNode1.QueueTimeTickers([toQueue]));

        Assert.Single(results);
        Assert.Equal(TickerStatus.Queued, results[0].Status);
        Assert.Equal(Node1, results[0].LockHolder);
        Assert.Equal(_now, results[0].LockedAt);
    }

    [Fact]
    public async Task QueueTimeTickers_Node2CannotAcquireWithStaleUpdatedAt()
    {
        var ticker = await SeedTimeTicker();
        var originalUpdatedAt = ticker.UpdatedAt;

        // Node1 acquires the lock — this updates the ticker's UpdatedAt
        var toQueue1 = new TimeTickerEntity { Id = ticker.Id, UpdatedAt = originalUpdatedAt };
        var node1Results = await CollectAsync(_providerNode1.QueueTimeTickers([toQueue1]));
        Assert.Single(node1Results);

        // Node2 tries with the original stale UpdatedAt — fails due to optimistic concurrency
        var toQueue2 = new TimeTickerEntity { Id = ticker.Id, UpdatedAt = originalUpdatedAt };
        var node2Results = await CollectAsync(_providerNode2.QueueTimeTickers([toQueue2]));

        Assert.Empty(node2Results);
    }

    [Fact]
    public async Task QueueTimeTickers_SameNodeCanReacquireOwnLock()
    {
        var ticker = await SeedTimeTicker();

        // Node1 acquires the lock
        var toQueue1 = new TimeTickerEntity { Id = ticker.Id, UpdatedAt = ticker.UpdatedAt };
        var results1 = await CollectAsync(_providerNode1.QueueTimeTickers([toQueue1]));
        Assert.Single(results1);

        // Node1 reacquires the same ticker — should succeed
        var toQueue2 = new TimeTickerEntity { Id = ticker.Id, UpdatedAt = results1[0].UpdatedAt };
        var results2 = await CollectAsync(_providerNode1.QueueTimeTickers([toQueue2]));

        Assert.Single(results2);
        Assert.Equal(Node1, results2[0].LockHolder);
    }

    [Fact]
    public async Task QueueTimeTickers_StaleUpdatedAt_RejectsAcquisition()
    {
        var ticker = await SeedTimeTicker();

        // Try to queue with an outdated UpdatedAt
        var toQueue = new TimeTickerEntity
        {
            Id = ticker.Id,
            UpdatedAt = _now.AddHours(-99) // doesn't match
        };

        var results = await CollectAsync(_providerNode1.QueueTimeTickers([toQueue]));
        Assert.Empty(results);
    }

    [Fact]
    public async Task QueueTimeTickers_InProgressTickerByOtherNode_CannotBeAcquired()
    {
        // InProgress ticker locked by Node2 — Node1 cannot acquire because
        // TryUpdate will fail (the stored ticker reference changes when Node2 acquires)
        var ticker = await SeedTimeTicker();

        // Node2 acquires first
        var toQueueNode2 = new TimeTickerEntity { Id = ticker.Id, UpdatedAt = ticker.UpdatedAt };
        var node2Results = await CollectAsync(_providerNode2.QueueTimeTickers([toQueueNode2]));
        Assert.Single(node2Results);

        // Node1 tries with stale UpdatedAt — should fail (optimistic concurrency)
        var toQueueNode1 = new TimeTickerEntity { Id = ticker.Id, UpdatedAt = ticker.UpdatedAt };
        var node1Results = await CollectAsync(_providerNode1.QueueTimeTickers([toQueueNode1]));

        Assert.Empty(node1Results);
    }

    // =========================================================================
    // Lock Release — Only the lock owner can release
    // =========================================================================

    [Fact]
    public async Task ReleaseAcquiredTimeTickers_OwnerReleasesSuccessfully()
    {
        var ticker = await SeedTimeTicker();

        // Acquire
        var toQueue = new TimeTickerEntity { Id = ticker.Id, UpdatedAt = ticker.UpdatedAt };
        await CollectAsync(_providerNode1.QueueTimeTickers([toQueue]));

        // Release by owner
        await _providerNode1.ReleaseAcquiredTimeTickers([ticker.Id]);

        // Verify released
        var retrieved = await _providerNode1.GetTimeTickerById(ticker.Id);
        Assert.Equal(TickerStatus.Idle, retrieved.Status);
        Assert.Null(retrieved.LockHolder);
        Assert.Null(retrieved.LockedAt);
    }

    [Fact]
    public async Task ReleaseAcquiredTimeTickers_NonOwnerCannotRelease()
    {
        var ticker = await SeedTimeTicker();

        // Node1 acquires
        var toQueue = new TimeTickerEntity { Id = ticker.Id, UpdatedAt = ticker.UpdatedAt };
        await CollectAsync(_providerNode1.QueueTimeTickers([toQueue]));

        // Node2 tries to release — should not work
        await _providerNode2.ReleaseAcquiredTimeTickers([ticker.Id]);

        // Verify still locked by Node1
        var retrieved = await _providerNode1.GetTimeTickerById(ticker.Id);
        Assert.Equal(TickerStatus.Queued, retrieved.Status);
        Assert.Equal(Node1, retrieved.LockHolder);
    }

    // =========================================================================
    // Dead Node Recovery
    // =========================================================================

    [Fact]
    public async Task ReleaseDeadNodeResources_ResetsTickersFromDeadNode()
    {
        var ticker = await SeedTimeTicker(
            status: TickerStatus.Queued,
            lockHolder: "dead-node",
            lockedAt: _now.AddMinutes(-10));

        await _providerNode1.ReleaseDeadNodeTimeTickerResources("dead-node");

        var retrieved = await _providerNode1.GetTimeTickerById(ticker.Id);
        Assert.Equal(TickerStatus.Idle, retrieved.Status);
        Assert.Null(retrieved.LockHolder);
        Assert.Null(retrieved.LockedAt);
    }

    [Fact]
    public async Task ReleaseDeadNodeResources_DoesNotAffectOtherNodesLocks()
    {
        var ticker = await SeedTimeTicker(
            status: TickerStatus.Queued,
            lockHolder: Node1,
            lockedAt: _now.AddMinutes(-1));

        // Release dead-node resources — should NOT touch Node1's lock
        await _providerNode1.ReleaseDeadNodeTimeTickerResources("dead-node");

        var retrieved = await _providerNode1.GetTimeTickerById(ticker.Id);
        Assert.Equal(TickerStatus.Queued, retrieved.Status);
        Assert.Equal(Node1, retrieved.LockHolder);
    }

    [Fact]
    public async Task ReleaseDeadNodeResources_InProgressTickerIsSkipped()
    {
        var ticker = await SeedTimeTicker(
            status: TickerStatus.InProgress,
            lockHolder: "dead-node",
            lockedAt: _now.AddMinutes(-5));

        await _providerNode1.ReleaseDeadNodeTimeTickerResources("dead-node");

        var retrieved = await _providerNode1.GetTimeTickerById(ticker.Id);
        // InProgress tasks from dead nodes should be marked as Skipped
        Assert.Equal(TickerStatus.Skipped, retrieved.Status);
    }

    // =========================================================================
    // Concurrent Access — Multiple threads competing for same ticker
    // =========================================================================

    [Fact]
    public async Task ConcurrentQueueAttempts_OnlyOneNodeAcquires()
    {
        var ticker = await SeedTimeTicker();

        // Each node needs its own input entity to avoid data races on shared objects
        var toQueueNode1 = new TimeTickerEntity { Id = ticker.Id, UpdatedAt = ticker.UpdatedAt };
        var toQueueNode2 = new TimeTickerEntity { Id = ticker.Id, UpdatedAt = ticker.UpdatedAt };

        // Run both nodes concurrently trying to acquire the same ticker
        var task1 = CollectAsync(_providerNode1.QueueTimeTickers([toQueueNode1]));
        var task2 = CollectAsync(_providerNode2.QueueTimeTickers([toQueueNode2]));

        var results = await Task.WhenAll(task1, task2);

        var totalAcquired = results[0].Count + results[1].Count;

        // At most one node should acquire (ConcurrentDictionary.TryUpdate provides atomicity)
        Assert.True(totalAcquired <= 1, $"Expected at most 1 acquisition, but got {totalAcquired}");

        // Verify ticker is locked by exactly one node
        var retrieved = await _providerNode1.GetTimeTickerById(ticker.Id);
        if (totalAcquired == 1)
        {
            Assert.NotNull(retrieved.LockHolder);
            Assert.True(retrieved.LockHolder == Node1 || retrieved.LockHolder == Node2);
        }
    }

    [Fact]
    public async Task ConcurrentQueueAttempts_MultipleTickersDistributed()
    {
        // Create multiple tickers
        var tickers = new List<TestTimeTicker>();
        for (int i = 0; i < 10; i++)
        {
            tickers.Add(await SeedTimeTicker(executionTime: _now.AddMinutes(i + 1)));
        }

        // Each node gets its own copy of input entities to avoid data races on shared objects
        var toQueueNode1 = tickers.Select(t =>
            new TimeTickerEntity { Id = t.Id, UpdatedAt = t.UpdatedAt }).ToArray();
        var toQueueNode2 = tickers.Select(t =>
            new TimeTickerEntity { Id = t.Id, UpdatedAt = t.UpdatedAt }).ToArray();

        // Both nodes try to acquire all tickers concurrently
        var task1 = CollectAsync(_providerNode1.QueueTimeTickers(toQueueNode1));
        var task2 = CollectAsync(_providerNode2.QueueTimeTickers(toQueueNode2));

        var results = await Task.WhenAll(task1, task2);

        var node1Acquired = results[0];
        var node2Acquired = results[1];

        // No ticker should be acquired by both nodes
        var node1Ids = node1Acquired.Select(r => r.Id).ToHashSet();
        var node2Ids = node2Acquired.Select(r => r.Id).ToHashSet();
        var overlap = node1Ids.Intersect(node2Ids).ToList();

        Assert.Empty(overlap);
    }

    // =========================================================================
    // Cron Ticker Occurrence Locking
    // =========================================================================

    [Fact]
    public async Task CronOccurrence_AcquireImmediate_LocksAndPreventsOtherNode()
    {
        // Seed a cron ticker and occurrence
        var cronTicker = new TestCronTicker
        {
            Id = Guid.NewGuid(),
            Function = "TestCronFunc",
            Expression = "*/5 * * * *",
            CreatedAt = _now.AddHours(-1),
            UpdatedAt = _now.AddHours(-1),
            Request = Array.Empty<byte>()
        };
        await _providerNode1.InsertCronTickers([cronTicker], CancellationToken.None);
        _createdCronTickerIds.Add(cronTicker.Id);

        var occurrence = new CronTickerOccurrenceEntity<TestCronTicker>
        {
            Id = Guid.NewGuid(),
            CronTickerId = cronTicker.Id,
            ExecutionTime = _now.AddMinutes(5),
            Status = TickerStatus.Idle,
            CreatedAt = _now,
            UpdatedAt = _now
        };
        await _providerNode1.InsertCronTickerOccurrences([occurrence], CancellationToken.None);
        _createdCronOccurrenceIds.Add(occurrence.Id);

        // Node1 acquires — sets InProgress
        var acquired = await _providerNode1.AcquireImmediateCronOccurrencesAsync([occurrence.Id]);
        Assert.Single(acquired);
        Assert.Equal(TickerStatus.InProgress, acquired[0].Status);
        Assert.Equal(Node1, acquired[0].LockHolder);

        // Node2 cannot acquire the same occurrence (InProgress + different lock holder)
        var acquired2 = await _providerNode2.AcquireImmediateCronOccurrencesAsync([occurrence.Id]);
        Assert.Empty(acquired2);

        // Verify the occurrence is still locked by Node1
        var all = await _providerNode1.GetAllCronTickerOccurrences(o => o.Id == occurrence.Id);
        Assert.Single(all);
        Assert.Equal(TickerStatus.InProgress, all[0].Status);
        Assert.Equal(Node1, all[0].LockHolder);
    }

    [Fact]
    public async Task CronOccurrence_DeadNodeRecovery_ResetsLockedOccurrence()
    {
        var cronTicker = new TestCronTicker
        {
            Id = Guid.NewGuid(),
            Function = "TestCronFunc",
            Expression = "*/5 * * * *",
            CreatedAt = _now.AddHours(-1),
            UpdatedAt = _now.AddHours(-1),
            Request = Array.Empty<byte>()
        };
        await _providerNode1.InsertCronTickers([cronTicker], CancellationToken.None);
        _createdCronTickerIds.Add(cronTicker.Id);

        // Create an occurrence locked by a dead node (Queued status — recoverable)
        var occurrence = new CronTickerOccurrenceEntity<TestCronTicker>
        {
            Id = Guid.NewGuid(),
            CronTickerId = cronTicker.Id,
            ExecutionTime = _now.AddMinutes(5),
            Status = TickerStatus.Queued,
            LockHolder = "dead-node",
            LockedAt = _now.AddMinutes(-10),
            CreatedAt = _now,
            UpdatedAt = _now
        };
        await _providerNode1.InsertCronTickerOccurrences([occurrence], CancellationToken.None);
        _createdCronOccurrenceIds.Add(occurrence.Id);

        // Dead node recovery
        await _providerNode1.ReleaseDeadNodeOccurrenceResources("dead-node");

        // Verify recovered — Node1 can now acquire it
        var acquired = await _providerNode1.AcquireImmediateCronOccurrencesAsync([occurrence.Id]);
        Assert.Single(acquired);
        Assert.Equal(Node1, acquired[0].LockHolder);
    }

    // =========================================================================
    // Per-Function Concurrency Gate (ITickerFunctionConcurrencyGate)
    // =========================================================================

    [Fact]
    public void ConcurrencyGate_ZeroMaxConcurrency_ReturnsNull()
    {
        var semaphore = _concurrencyGate.GetSemaphoreOrNull("any-function", 0);
        Assert.Null(semaphore);
    }

    [Fact]
    public void ConcurrencyGate_NegativeMaxConcurrency_ReturnsNull()
    {
        var semaphore = _concurrencyGate.GetSemaphoreOrNull("any-function", -1);
        Assert.Null(semaphore);
    }

    [Fact]
    public void ConcurrencyGate_PositiveMaxConcurrency_ReturnsSemaphore()
    {
        var semaphore = _concurrencyGate.GetSemaphoreOrNull("limited-function", 3);
        Assert.NotNull(semaphore);
        Assert.Equal(3, semaphore.CurrentCount);
    }

    [Fact]
    public void ConcurrencyGate_SameFunctionReturnsSameSemaphore()
    {
        var s1 = _concurrencyGate.GetSemaphoreOrNull("shared-function", 2);
        var s2 = _concurrencyGate.GetSemaphoreOrNull("shared-function", 2);

        Assert.Same(s1, s2);
    }

    [Fact]
    public void ConcurrencyGate_DifferentFunctionsReturnDifferentSemaphores()
    {
        var s1 = _concurrencyGate.GetSemaphoreOrNull("func-a", 1);
        var s2 = _concurrencyGate.GetSemaphoreOrNull("func-b", 1);

        Assert.NotSame(s1, s2);
    }

    [Fact]
    public async Task ConcurrencyGate_MaxConcurrency1_SerializesExecution()
    {
        var semaphore = _concurrencyGate.GetSemaphoreOrNull("serial-function", 1);
        Assert.NotNull(semaphore);

        var executionOrder = new List<int>();
        var lock1Acquired = new TaskCompletionSource();

        // Task 1 acquires and holds the semaphore
        var task1 = Task.Run(async () =>
        {
            await semaphore.WaitAsync();
            try
            {
                executionOrder.Add(1);
                lock1Acquired.SetResult();
                await Task.Delay(200); // simulate work
            }
            finally
            {
                semaphore.Release();
            }
        });

        // Wait for task1 to acquire
        await lock1Acquired.Task;

        // Task 2 should be blocked until task1 releases
        var task2 = Task.Run(async () =>
        {
            await semaphore.WaitAsync();
            try
            {
                executionOrder.Add(2);
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(task1, task2);

        // Task 1 must complete before task 2 starts
        Assert.Equal(new[] { 1, 2 }, executionOrder);
    }

    [Fact]
    public async Task ConcurrencyGate_MaxConcurrency2_AllowsTwoConcurrent()
    {
        var semaphore = _concurrencyGate.GetSemaphoreOrNull("parallel-function", 2);
        Assert.NotNull(semaphore);

        var concurrentCount = 0;
        var maxConcurrentCount = 0;
        var counterLock = new object();

        var tasks = Enumerable.Range(0, 5).Select(i => Task.Run(async () =>
        {
            await semaphore.WaitAsync();
            try
            {
                lock (counterLock)
                {
                    concurrentCount++;
                    maxConcurrentCount = Math.Max(maxConcurrentCount, concurrentCount);
                }

                await Task.Delay(100); // simulate work

                lock (counterLock)
                {
                    concurrentCount--;
                }
            }
            finally
            {
                semaphore.Release();
            }
        })).ToArray();

        await Task.WhenAll(tasks);

        // Max concurrent should be exactly 2
        Assert.Equal(2, maxConcurrentCount);
    }

    [Fact]
    public async Task ConcurrencyGate_SemaphoreReleasedOnException()
    {
        var semaphore = _concurrencyGate.GetSemaphoreOrNull("exception-function", 1);
        Assert.NotNull(semaphore);

        // First execution throws
        try
        {
            await semaphore.WaitAsync();
            try
            {
                throw new InvalidOperationException("test error");
            }
            finally
            {
                semaphore.Release();
            }
        }
        catch (InvalidOperationException)
        {
            // expected
        }

        // Second execution should not be blocked
        Assert.Equal(1, semaphore.CurrentCount);
        Assert.True(await semaphore.WaitAsync(TimeSpan.FromMilliseconds(100)));
        semaphore.Release();
    }
}
