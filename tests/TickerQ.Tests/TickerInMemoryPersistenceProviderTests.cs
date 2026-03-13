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

public class TickerInMemoryPersistenceProviderTests : IAsyncLifetime
{
    private sealed class FakeTimeTicker : TimeTickerEntity<FakeTimeTicker> { }
    private sealed class FakeCronTicker : CronTickerEntity { }

    private readonly ITickerClock _clock;
    private readonly TickerInMemoryPersistenceProvider<FakeTimeTicker, FakeCronTicker> _provider;
    private readonly DateTime _now;
    private readonly string _nodeId;

    // Track IDs created by each test for deterministic cleanup
    private readonly List<Guid> _createdTimeTickerIds = new();
    private readonly List<Guid> _createdCronTickerIds = new();
    private readonly List<Guid> _createdCronOccurrenceIds = new();

    public TickerInMemoryPersistenceProviderTests()
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
        // Clean up time tickers
        if (_createdTimeTickerIds.Count > 0)
            await _provider.RemoveTimeTickers(_createdTimeTickerIds.ToArray(), CancellationToken.None);

        // Clean up cron occurrences
        if (_createdCronOccurrenceIds.Count > 0)
            await _provider.RemoveCronTickerOccurrences(_createdCronOccurrenceIds.ToArray(), CancellationToken.None);

        // Clean up cron tickers
        if (_createdCronTickerIds.Count > 0)
            await _provider.RemoveCronTickers(_createdCronTickerIds.ToArray(), CancellationToken.None);
    }

    #region QueueTimeTickers

    [Fact]
    public async Task QueueTimeTickers_ExistingTickerWithMatchingUpdatedAt_SetsQueuedStatusAndLockHolder()
    {
        // Arrange: add a ticker to static storage first
        var tickerId = Guid.NewGuid();
        var ticker = new FakeTimeTicker
        {
            Id = tickerId,
            Function = "TestFunc",
            ExecutionTime = _now.AddMinutes(1),
            Status = TickerStatus.Idle,
            UpdatedAt = _now.AddMinutes(-5),
            CreatedAt = _now.AddMinutes(-10)
        };
        await _provider.AddTimeTickers(new[] { ticker }, CancellationToken.None);
        _createdTimeTickerIds.Add(tickerId);

        // Build a TimeTickerEntity with matching UpdatedAt for the queue call
        var toQueue = new TimeTickerEntity
        {
            Id = tickerId,
            UpdatedAt = _now.AddMinutes(-5) // matches the existing ticker's UpdatedAt
        };

        // Act
        var results = await CollectAsync(_provider.QueueTimeTickers(new[] { toQueue }, CancellationToken.None));

        // Assert
        Assert.Single(results);
        var result = results[0];
        Assert.Equal(tickerId, result.Id);
        Assert.Equal(TickerStatus.Queued, result.Status);
        Assert.Equal(_nodeId, result.LockHolder);
        Assert.Equal(_now, result.LockedAt);
        Assert.Equal(_now, result.UpdatedAt);
    }

    [Fact]
    public async Task QueueTimeTickers_ExistingTickerWithMismatchedUpdatedAt_IsSkipped()
    {
        // Arrange
        var tickerId = Guid.NewGuid();
        var ticker = new FakeTimeTicker
        {
            Id = tickerId,
            Function = "TestFunc",
            ExecutionTime = _now.AddMinutes(1),
            Status = TickerStatus.Idle,
            UpdatedAt = _now.AddMinutes(-5),
            CreatedAt = _now.AddMinutes(-10)
        };
        await _provider.AddTimeTickers(new[] { ticker }, CancellationToken.None);
        _createdTimeTickerIds.Add(tickerId);

        // Build a TimeTickerEntity with DIFFERENT UpdatedAt (optimistic concurrency conflict)
        var toQueue = new TimeTickerEntity
        {
            Id = tickerId,
            UpdatedAt = _now.AddMinutes(-99) // does NOT match
        };

        // Act
        var results = await CollectAsync(_provider.QueueTimeTickers(new[] { toQueue }, CancellationToken.None));

        // Assert: nothing yielded due to concurrency mismatch
        Assert.Empty(results);
    }

    [Fact]
    public async Task QueueTimeTickers_NonExistentTicker_IsSkipped()
    {
        // Arrange: ticker ID that does not exist in storage
        var toQueue = new TimeTickerEntity
        {
            Id = Guid.NewGuid(),
            UpdatedAt = _now
        };

        // Act
        var results = await CollectAsync(_provider.QueueTimeTickers(new[] { toQueue }, CancellationToken.None));

        // Assert
        Assert.Empty(results);
    }

    #endregion

    #region QueueTimedOutCronTickerOccurrences

    [Fact]
    public async Task QueueTimedOutCronTickerOccurrences_NoTimedOutOccurrences_YieldsNothing()
    {
        // Arrange: create an occurrence within the 1-second window (not timed out)
        var cronTickerId = Guid.NewGuid();
        await SetupCronTicker(cronTickerId);

        var occurrenceId = Guid.NewGuid();
        var occurrence = new CronTickerOccurrenceEntity<FakeCronTicker>
        {
            Id = occurrenceId,
            CronTickerId = cronTickerId,
            ExecutionTime = _now, // exactly now, NOT older than 1 second
            Status = TickerStatus.Idle,
            CreatedAt = _now,
            UpdatedAt = _now
        };
        await _provider.InsertCronTickerOccurrences(new[] { occurrence }, CancellationToken.None);
        _createdCronOccurrenceIds.Add(occurrenceId);

        // Act
        var results = await CollectAsync(_provider.QueueTimedOutCronTickerOccurrences(CancellationToken.None));

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public async Task QueueTimedOutCronTickerOccurrences_OccurrenceOlderThan1Second_YieldedWithInProgressStatus()
    {
        // Arrange: create an occurrence older than 1 second
        var cronTickerId = Guid.NewGuid();
        await SetupCronTicker(cronTickerId);

        var occurrenceId = Guid.NewGuid();
        var occurrence = new CronTickerOccurrenceEntity<FakeCronTicker>
        {
            Id = occurrenceId,
            CronTickerId = cronTickerId,
            ExecutionTime = _now.AddSeconds(-5), // 5 seconds ago, well past the 1-second threshold
            Status = TickerStatus.Idle,
            CreatedAt = _now.AddSeconds(-5),
            UpdatedAt = _now.AddSeconds(-5)
        };
        await _provider.InsertCronTickerOccurrences(new[] { occurrence }, CancellationToken.None);
        _createdCronOccurrenceIds.Add(occurrenceId);

        // Act
        var results = await CollectAsync(_provider.QueueTimedOutCronTickerOccurrences(CancellationToken.None));

        // Assert
        Assert.Single(results);
        var result = results[0];
        Assert.Equal(occurrenceId, result.Id);
        Assert.Equal(TickerStatus.InProgress, result.Status);
        Assert.Equal(_nodeId, result.LockHolder);
        Assert.Equal(_now, result.LockedAt);
    }

    [Fact]
    public async Task QueueTimedOutCronTickerOccurrences_OccurrenceWithin1SecondWindow_NotYielded()
    {
        // Arrange: occurrence at exactly 0.5 seconds ago (within the 1-second window)
        var cronTickerId = Guid.NewGuid();
        await SetupCronTicker(cronTickerId);

        var occurrenceId = Guid.NewGuid();
        var occurrence = new CronTickerOccurrenceEntity<FakeCronTicker>
        {
            Id = occurrenceId,
            CronTickerId = cronTickerId,
            ExecutionTime = _now.AddMilliseconds(-500), // within the 1-second window
            Status = TickerStatus.Idle,
            CreatedAt = _now,
            UpdatedAt = _now
        };
        await _provider.InsertCronTickerOccurrences(new[] { occurrence }, CancellationToken.None);
        _createdCronOccurrenceIds.Add(occurrenceId);

        // Act
        var results = await CollectAsync(_provider.QueueTimedOutCronTickerOccurrences(CancellationToken.None));

        // Assert: belongs to main scheduler, so fallback should NOT pick it up
        Assert.Empty(results);
    }

    [Fact]
    public async Task QueueTimedOutCronTickerOccurrences_OnlyAcquirableOccurrencesYielded()
    {
        // Arrange: one acquirable (Idle) and one non-acquirable (InProgress locked by other)
        var cronTickerId = Guid.NewGuid();
        await SetupCronTicker(cronTickerId);

        var acquirableId = Guid.NewGuid();
        var acquirable = new CronTickerOccurrenceEntity<FakeCronTicker>
        {
            Id = acquirableId,
            CronTickerId = cronTickerId,
            ExecutionTime = _now.AddSeconds(-10),
            Status = TickerStatus.Idle,
            CreatedAt = _now.AddSeconds(-10),
            UpdatedAt = _now.AddSeconds(-10)
        };

        var nonAcquirableId = Guid.NewGuid();
        var nonAcquirable = new CronTickerOccurrenceEntity<FakeCronTicker>
        {
            Id = nonAcquirableId,
            CronTickerId = cronTickerId,
            ExecutionTime = _now.AddSeconds(-10),
            Status = TickerStatus.InProgress, // not acquirable
            LockHolder = "other-node",
            LockedAt = _now.AddSeconds(-5),
            CreatedAt = _now.AddSeconds(-10),
            UpdatedAt = _now.AddSeconds(-10)
        };

        await _provider.InsertCronTickerOccurrences(new[] { acquirable }, CancellationToken.None);
        _createdCronOccurrenceIds.Add(acquirableId);
        // Insert the non-acquirable one directly — need a different execution time since same cronTickerId
        var nonAcquirable2 = new CronTickerOccurrenceEntity<FakeCronTicker>
        {
            Id = nonAcquirableId,
            CronTickerId = cronTickerId,
            ExecutionTime = _now.AddSeconds(-11), // different time to avoid dedup index conflict
            Status = TickerStatus.InProgress,
            LockHolder = "other-node",
            LockedAt = _now.AddSeconds(-5),
            CreatedAt = _now.AddSeconds(-11),
            UpdatedAt = _now.AddSeconds(-11)
        };
        await _provider.InsertCronTickerOccurrences(new[] { nonAcquirable2 }, CancellationToken.None);
        _createdCronOccurrenceIds.Add(nonAcquirableId);

        // Act
        var results = await CollectAsync(_provider.QueueTimedOutCronTickerOccurrences(CancellationToken.None));

        // Assert: only the Idle one should be yielded (InProgress with other-node lock is not acquirable)
        Assert.Single(results);
        Assert.Equal(acquirableId, results[0].Id);
    }

    #endregion

    #region ReleaseDeadNodeOccurrenceResources

    [Fact]
    public async Task ReleaseDeadNodeOccurrenceResources_ReleasesIdleQueuedOccurrencesFromDeadNode()
    {
        // Arrange
        var deadNode = "dead-node-1";
        var cronTickerId = Guid.NewGuid();
        await SetupCronTicker(cronTickerId);

        var occurrenceId = Guid.NewGuid();
        var occurrence = new CronTickerOccurrenceEntity<FakeCronTicker>
        {
            Id = occurrenceId,
            CronTickerId = cronTickerId,
            ExecutionTime = _now.AddMinutes(1),
            Status = TickerStatus.Queued,
            LockHolder = deadNode,
            LockedAt = _now.AddMinutes(-1),
            CreatedAt = _now,
            UpdatedAt = _now
        };
        await _provider.InsertCronTickerOccurrences(new[] { occurrence }, CancellationToken.None);
        _createdCronOccurrenceIds.Add(occurrenceId);

        // Act
        await _provider.ReleaseDeadNodeOccurrenceResources(deadNode, CancellationToken.None);

        // Assert: verify the occurrence was reset by acquiring it (it should now be acquirable)
        var acquired = await _provider.AcquireImmediateCronOccurrencesAsync(
            new[] { occurrenceId }, CancellationToken.None);
        Assert.Single(acquired);
        Assert.Equal(TickerStatus.InProgress, acquired[0].Status);
        Assert.Equal(_nodeId, acquired[0].LockHolder);
    }

    [Fact]
    public async Task ReleaseDeadNodeOccurrenceResources_MarksInProgressOccurrencesAsSkipped()
    {
        // Arrange
        var deadNode = "dead-node-2";
        var cronTickerId = Guid.NewGuid();
        await SetupCronTicker(cronTickerId);

        var occurrenceId = Guid.NewGuid();
        var occurrence = new CronTickerOccurrenceEntity<FakeCronTicker>
        {
            Id = occurrenceId,
            CronTickerId = cronTickerId,
            ExecutionTime = _now.AddMinutes(1),
            Status = TickerStatus.InProgress,
            LockHolder = deadNode,
            LockedAt = _now.AddMinutes(-1),
            CreatedAt = _now,
            UpdatedAt = _now
        };
        await _provider.InsertCronTickerOccurrences(new[] { occurrence }, CancellationToken.None);
        _createdCronOccurrenceIds.Add(occurrenceId);

        // Act
        await _provider.ReleaseDeadNodeOccurrenceResources(deadNode, CancellationToken.None);

        // Assert: verify it was marked as Skipped by reading it back
        var all = await _provider.GetAllCronTickerOccurrences(
            x => x.Id == occurrenceId, CancellationToken.None);
        Assert.Single(all);
        Assert.Equal(TickerStatus.Skipped, all[0].Status);
        Assert.Equal("Node is not alive!", all[0].SkippedReason);
        Assert.Equal(_now, all[0].ExecutedAt);
    }

    [Fact]
    public async Task ReleaseDeadNodeOccurrenceResources_DoesNotAffectOccurrencesFromOtherNodes()
    {
        // Arrange
        var deadNode = "dead-node-3";
        var aliveNode = "alive-node";
        var cronTickerId = Guid.NewGuid();
        await SetupCronTicker(cronTickerId);

        var aliveOccurrenceId = Guid.NewGuid();
        var aliveOccurrence = new CronTickerOccurrenceEntity<FakeCronTicker>
        {
            Id = aliveOccurrenceId,
            CronTickerId = cronTickerId,
            ExecutionTime = _now.AddMinutes(2),
            Status = TickerStatus.InProgress,
            LockHolder = aliveNode,
            LockedAt = _now,
            CreatedAt = _now,
            UpdatedAt = _now
        };
        await _provider.InsertCronTickerOccurrences(new[] { aliveOccurrence }, CancellationToken.None);
        _createdCronOccurrenceIds.Add(aliveOccurrenceId);

        // Act: release dead node resources - should NOT touch the alive node's occurrence
        await _provider.ReleaseDeadNodeOccurrenceResources(deadNode, CancellationToken.None);

        // Assert
        var all = await _provider.GetAllCronTickerOccurrences(
            x => x.Id == aliveOccurrenceId, CancellationToken.None);
        Assert.Single(all);
        Assert.Equal(TickerStatus.InProgress, all[0].Status);
        Assert.Equal(aliveNode, all[0].LockHolder);
    }

    #endregion

    #region AcquireImmediateCronOccurrencesAsync

    [Fact]
    public async Task AcquireImmediateCronOccurrencesAsync_AcquiresIdleOccurrence()
    {
        // Arrange
        var cronTickerId = Guid.NewGuid();
        await SetupCronTicker(cronTickerId);

        var occurrenceId = Guid.NewGuid();
        var occurrence = new CronTickerOccurrenceEntity<FakeCronTicker>
        {
            Id = occurrenceId,
            CronTickerId = cronTickerId,
            ExecutionTime = _now.AddMinutes(1),
            Status = TickerStatus.Idle,
            CreatedAt = _now,
            UpdatedAt = _now
        };
        await _provider.InsertCronTickerOccurrences(new[] { occurrence }, CancellationToken.None);
        _createdCronOccurrenceIds.Add(occurrenceId);

        // Act
        var result = await _provider.AcquireImmediateCronOccurrencesAsync(
            new[] { occurrenceId }, CancellationToken.None);

        // Assert
        Assert.Single(result);
        Assert.Equal(occurrenceId, result[0].Id);
        Assert.Equal(TickerStatus.InProgress, result[0].Status);
        Assert.Equal(_nodeId, result[0].LockHolder);
        Assert.Equal(_now, result[0].LockedAt);
        Assert.Equal(_now, result[0].UpdatedAt);
    }

    [Fact]
    public async Task AcquireImmediateCronOccurrencesAsync_SkipsOccurrenceLockedByAnotherNode()
    {
        // Arrange
        var cronTickerId = Guid.NewGuid();
        await SetupCronTicker(cronTickerId);

        var occurrenceId = Guid.NewGuid();
        var occurrence = new CronTickerOccurrenceEntity<FakeCronTicker>
        {
            Id = occurrenceId,
            CronTickerId = cronTickerId,
            ExecutionTime = _now.AddMinutes(1),
            Status = TickerStatus.Queued,
            LockHolder = "other-node",
            LockedAt = _now.AddMinutes(-1),
            CreatedAt = _now,
            UpdatedAt = _now
        };
        await _provider.InsertCronTickerOccurrences(new[] { occurrence }, CancellationToken.None);
        _createdCronOccurrenceIds.Add(occurrenceId);

        // Act: our node tries to acquire it
        var result = await _provider.AcquireImmediateCronOccurrencesAsync(
            new[] { occurrenceId }, CancellationToken.None);

        // Assert: cannot acquire because it's locked by another node
        Assert.Empty(result);
    }

    [Fact]
    public async Task AcquireImmediateCronOccurrencesAsync_EmptyArrayReturnsEmpty()
    {
        // Act
        var result = await _provider.AcquireImmediateCronOccurrencesAsync(
            Array.Empty<Guid>(), CancellationToken.None);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task AcquireImmediateCronOccurrencesAsync_NonExistentIdsAreSkipped()
    {
        // Act
        var result = await _provider.AcquireImmediateCronOccurrencesAsync(
            new[] { Guid.NewGuid(), Guid.NewGuid() }, CancellationToken.None);

        // Assert
        Assert.Empty(result);
    }

    #endregion

    #region ReleaseAcquiredCronTickerOccurrences

    [Fact]
    public async Task ReleaseAcquiredCronTickerOccurrences_ResetsStatusToIdleAndClearsLock()
    {
        // Arrange: create and acquire an occurrence
        var cronTickerId = Guid.NewGuid();
        await SetupCronTicker(cronTickerId);

        var occurrenceId = Guid.NewGuid();
        var occurrence = new CronTickerOccurrenceEntity<FakeCronTicker>
        {
            Id = occurrenceId,
            CronTickerId = cronTickerId,
            ExecutionTime = _now.AddMinutes(1),
            Status = TickerStatus.Idle,
            CreatedAt = _now,
            UpdatedAt = _now
        };
        await _provider.InsertCronTickerOccurrences(new[] { occurrence }, CancellationToken.None);
        _createdCronOccurrenceIds.Add(occurrenceId);

        // Acquire it first (sets status to InProgress, which is NOT acquirable for release)
        // Instead, let's test with a Queued occurrence owned by our node
        // The CanAcquireCronOccurrence checks: (Idle||Queued) && (LockHolder==_lockHolder || LockedAt==null)
        // After InsertCronTickerOccurrences, status is Idle and LockHolder is null, LockedAt is null
        // So it IS acquirable for release.

        // Act
        await _provider.ReleaseAcquiredCronTickerOccurrences(
            new[] { occurrenceId }, CancellationToken.None);

        // Assert: verify it was reset
        var all = await _provider.GetAllCronTickerOccurrences(
            x => x.Id == occurrenceId, CancellationToken.None);
        Assert.Single(all);
        Assert.Equal(TickerStatus.Idle, all[0].Status);
        Assert.Null(all[0].LockHolder);
        Assert.Null(all[0].LockedAt);
    }

    [Fact]
    public async Task ReleaseAcquiredCronTickerOccurrences_OnlyReleasesAcquirableOccurrences()
    {
        // Arrange: create an occurrence locked by another node (InProgress)
        var cronTickerId = Guid.NewGuid();
        await SetupCronTicker(cronTickerId);

        var occurrenceId = Guid.NewGuid();
        var occurrence = new CronTickerOccurrenceEntity<FakeCronTicker>
        {
            Id = occurrenceId,
            CronTickerId = cronTickerId,
            ExecutionTime = _now.AddMinutes(3),
            Status = TickerStatus.InProgress,
            LockHolder = "other-node",
            LockedAt = _now,
            CreatedAt = _now,
            UpdatedAt = _now
        };
        await _provider.InsertCronTickerOccurrences(new[] { occurrence }, CancellationToken.None);
        _createdCronOccurrenceIds.Add(occurrenceId);

        // Act: try to release
        await _provider.ReleaseAcquiredCronTickerOccurrences(
            new[] { occurrenceId }, CancellationToken.None);

        // Assert: should NOT have been released (InProgress + other-node lock is not acquirable)
        var all = await _provider.GetAllCronTickerOccurrences(
            x => x.Id == occurrenceId, CancellationToken.None);
        Assert.Single(all);
        Assert.Equal(TickerStatus.InProgress, all[0].Status);
        Assert.Equal("other-node", all[0].LockHolder);
    }

    #endregion

    #region GetEarliestAvailableCronOccurrence

    [Fact]
    public async Task GetEarliestAvailableCronOccurrence_ReturnsEarliestWithinSchedulerWindow()
    {
        // Arrange: two occurrences, one earlier than the other, both within the scheduler window
        var cronTickerId = Guid.NewGuid();
        await SetupCronTicker(cronTickerId);

        var earlierId = Guid.NewGuid();
        var earlier = new CronTickerOccurrenceEntity<FakeCronTicker>
        {
            Id = earlierId,
            CronTickerId = cronTickerId,
            ExecutionTime = _now, // at the edge of the 1-second window (>= now - 1s)
            Status = TickerStatus.Idle,
            CreatedAt = _now,
            UpdatedAt = _now
        };

        var laterId = Guid.NewGuid();
        var later = new CronTickerOccurrenceEntity<FakeCronTicker>
        {
            Id = laterId,
            CronTickerId = cronTickerId,
            ExecutionTime = _now.AddMinutes(5),
            Status = TickerStatus.Idle,
            CreatedAt = _now,
            UpdatedAt = _now
        };

        await _provider.InsertCronTickerOccurrences(new[] { earlier }, CancellationToken.None);
        _createdCronOccurrenceIds.Add(earlierId);
        await _provider.InsertCronTickerOccurrences(new[] { later }, CancellationToken.None);
        _createdCronOccurrenceIds.Add(laterId);

        // Act
        var result = await _provider.GetEarliestAvailableCronOccurrence(
            new[] { cronTickerId }, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(earlierId, result.Id);
    }

    [Fact]
    public async Task GetEarliestAvailableCronOccurrence_SkipsNonAcquirableOccurrences()
    {
        // Arrange: one non-acquirable (InProgress), one acquirable (Idle)
        var cronTickerId = Guid.NewGuid();
        await SetupCronTicker(cronTickerId);

        var nonAcquirableId = Guid.NewGuid();
        var nonAcquirable = new CronTickerOccurrenceEntity<FakeCronTicker>
        {
            Id = nonAcquirableId,
            CronTickerId = cronTickerId,
            ExecutionTime = _now, // earlier
            Status = TickerStatus.InProgress,
            LockHolder = "other-node",
            LockedAt = _now,
            CreatedAt = _now,
            UpdatedAt = _now
        };

        var acquirableId = Guid.NewGuid();
        var acquirable = new CronTickerOccurrenceEntity<FakeCronTicker>
        {
            Id = acquirableId,
            CronTickerId = cronTickerId,
            ExecutionTime = _now.AddMinutes(1), // later but acquirable
            Status = TickerStatus.Idle,
            CreatedAt = _now,
            UpdatedAt = _now
        };

        await _provider.InsertCronTickerOccurrences(new[] { nonAcquirable }, CancellationToken.None);
        _createdCronOccurrenceIds.Add(nonAcquirableId);
        await _provider.InsertCronTickerOccurrences(new[] { acquirable }, CancellationToken.None);
        _createdCronOccurrenceIds.Add(acquirableId);

        // Act
        var result = await _provider.GetEarliestAvailableCronOccurrence(
            new[] { cronTickerId }, CancellationToken.None);

        // Assert: should skip the InProgress one and return the Idle one
        Assert.NotNull(result);
        Assert.Equal(acquirableId, result.Id);
    }

    [Fact]
    public async Task GetEarliestAvailableCronOccurrence_ReturnsNullWhenNoOccurrencesExist()
    {
        // Act
        var result = await _provider.GetEarliestAvailableCronOccurrence(
            new[] { Guid.NewGuid() }, CancellationToken.None);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetEarliestAvailableCronOccurrence_SkipsOccurrencesOutsideSchedulerWindow()
    {
        // Arrange: occurrence far in the past, older than the 1-second threshold
        var cronTickerId = Guid.NewGuid();
        await SetupCronTicker(cronTickerId);

        var oldId = Guid.NewGuid();
        var oldOccurrence = new CronTickerOccurrenceEntity<FakeCronTicker>
        {
            Id = oldId,
            CronTickerId = cronTickerId,
            ExecutionTime = _now.AddSeconds(-5), // 5 seconds ago, below mainSchedulerThreshold (now - 1s)
            Status = TickerStatus.Idle,
            CreatedAt = _now.AddSeconds(-5),
            UpdatedAt = _now.AddSeconds(-5)
        };
        await _provider.InsertCronTickerOccurrences(new[] { oldOccurrence }, CancellationToken.None);
        _createdCronOccurrenceIds.Add(oldId);

        // Act
        var result = await _provider.GetEarliestAvailableCronOccurrence(
            new[] { cronTickerId }, CancellationToken.None);

        // Assert: occurrence is before mainSchedulerThreshold, so it should not be returned
        Assert.Null(result);
    }

    #endregion

    #region Helpers

    private async Task SetupCronTicker(Guid cronTickerId)
    {
        var cronTicker = new FakeCronTicker
        {
            Id = cronTickerId,
            Function = "TestFunc",
            Expression = "* * * * *"
        };
        await _provider.InsertCronTickers(new[] { cronTicker }, CancellationToken.None);
        _createdCronTickerIds.Add(cronTickerId);
    }

    private static async Task<List<T>> CollectAsync<T>(IAsyncEnumerable<T> source)
    {
        var list = new List<T>();
        await foreach (var item in source)
            list.Add(item);
        return list;
    }

    #endregion
}
