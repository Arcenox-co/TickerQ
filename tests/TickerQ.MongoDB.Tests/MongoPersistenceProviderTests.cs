using MongoDB.Driver;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Models;

namespace TickerQ.MongoDB.Tests;

[Collection("Mongo")]
public class MongoPersistenceProviderTests : IAsyncLifetime
{
    private readonly MongoTestFixture _f;

    public MongoPersistenceProviderTests(MongoTestFixture fixture) => _f = fixture;

    public Task InitializeAsync() => _f.DropAllAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private TimeTickerEntity NewTimeTicker(DateTime? executionTime = null, TickerStatus status = TickerStatus.Idle)
    {
        var t = new TimeTickerEntity
        {
            Id = Guid.NewGuid(),
            Function = "test-fn",
            Request = Array.Empty<byte>(),
            ExecutionTime = executionTime ?? _f.FixedNow.AddSeconds(1)
        };
        typeof(TimeTickerEntity).GetProperty(nameof(t.Status))!.SetValue(t, status);
        typeof(TimeTickerEntity).GetProperty(nameof(t.CreatedAt))!.SetValue(t, _f.FixedNow);
        typeof(TimeTickerEntity).GetProperty(nameof(t.UpdatedAt))!.SetValue(t, _f.FixedNow);
        return t;
    }

    private CronTickerEntity NewCron(string function = "cron-fn", string expression = "* * * * *")
        => new()
        {
            Id = Guid.NewGuid(),
            Function = function,
            Expression = expression,
            Request = Array.Empty<byte>(),
            IsEnabled = true
        };

    [Fact]
    public async Task InsertAndGetTimeTicker_RoundTrips()
    {
        var ticker = NewTimeTicker();
        await _f.Provider.AddTimeTickers([ticker], CancellationToken.None);

        var fetched = await _f.Provider.GetTimeTickerById(ticker.Id, CancellationToken.None);
        Assert.NotNull(fetched);
        Assert.Equal(ticker.Function, fetched!.Function);
        Assert.Equal(TickerStatus.Idle, fetched.Status);
    }

    [Fact]
    public async Task GetEarliestTimeTickers_ReturnsIdleWithinOneSecond()
    {
        var ticker = NewTimeTicker(executionTime: _f.FixedNow.AddMilliseconds(500));
        await _f.Provider.AddTimeTickers([ticker], CancellationToken.None);

        var earliest = await _f.Provider.GetEarliestTimeTickers(CancellationToken.None);
        Assert.Single(earliest);
        Assert.Equal(ticker.Id, earliest[0].Id);
    }

    [Fact]
    public async Task GetEarliestTimeTickers_SkipsStaleRows()
    {
        var stale = NewTimeTicker(executionTime: _f.FixedNow.AddSeconds(-5));
        await _f.Provider.AddTimeTickers([stale], CancellationToken.None);

        var earliest = await _f.Provider.GetEarliestTimeTickers(CancellationToken.None);
        Assert.Empty(earliest);
    }

    [Fact]
    public async Task QueueTimeTickers_AcquiresLockOnce_RejectsStaleUpdatedAt()
    {
        var ticker = NewTimeTicker();
        await _f.Provider.AddTimeTickers([ticker], CancellationToken.None);

        var fresh = await _f.Provider.GetTimeTickerById(ticker.Id, CancellationToken.None);
        Assert.NotNull(fresh);

        var queued1 = new List<TimeTickerEntity>();
        await foreach (var t in _f.Provider.QueueTimeTickers([new TimeTickerEntity { Id = fresh!.Id, UpdatedAt = fresh.UpdatedAt }], CancellationToken.None))
            queued1.Add(t);
        Assert.Single(queued1);

        // Second attempt with the old UpdatedAt should win nothing — CAS fails.
        var queued2 = new List<TimeTickerEntity>();
        await foreach (var t in _f.Provider.QueueTimeTickers([new TimeTickerEntity { Id = fresh.Id, UpdatedAt = fresh.UpdatedAt }], CancellationToken.None))
            queued2.Add(t);
        Assert.Empty(queued2);
    }

    [Fact]
    public async Task ReleaseAcquiredTimeTickers_ClearsLockAndResetsStatus()
    {
        var ticker = NewTimeTicker();
        await _f.Provider.AddTimeTickers([ticker], CancellationToken.None);

        var fresh = await _f.Provider.GetTimeTickerById(ticker.Id, CancellationToken.None);
        await foreach (var _ in _f.Provider.QueueTimeTickers([new TimeTickerEntity { Id = fresh!.Id, UpdatedAt = fresh.UpdatedAt }], CancellationToken.None)) { }

        await _f.Provider.ReleaseAcquiredTimeTickers([ticker.Id], CancellationToken.None);

        var after = await _f.Provider.GetTimeTickerById(ticker.Id, CancellationToken.None);
        Assert.Equal(TickerStatus.Idle, after!.Status);
        Assert.Null(after.LockHolder);
        Assert.Null(after.LockedAt);
    }

    [Fact]
    public async Task UpdateTimeTicker_AppliesStatusAndElapsedTime()
    {
        var ticker = NewTimeTicker();
        await _f.Provider.AddTimeTickers([ticker], CancellationToken.None);
        var acquired = Assert.Single(
            await _f.Provider.AcquireImmediateTimeTickersAsync([ticker.Id], CancellationToken.None));

        var ctx = new InternalFunctionContext
        {
            TickerId = ticker.Id,
            FunctionName = "test-fn",
            Type = TickerType.TimeTicker,
            ExecutedAt = _f.FixedNow,
            AcquisitionToken = acquired.AcquisitionToken,
        }
        .SetProperty(c => c.Status, TickerStatus.Done)
        .SetProperty(c => c.ExecutedAt, _f.FixedNow)
        .SetProperty(c => c.ElapsedTime, 123L)
        .SetProperty(c => c.ReleaseLock, true);

        var modified = await _f.Provider.UpdateTimeTicker(ctx, CancellationToken.None);
        Assert.Equal(1, modified);

        var after = await _f.Provider.GetTimeTickerById(ticker.Id, CancellationToken.None);
        Assert.Equal(TickerStatus.Done, after!.Status);
        Assert.Equal(123L, after.ElapsedTime);
        Assert.Null(after.LockHolder);
    }

    [Fact]
    public async Task ReleaseDeadNodeTimeTickerResources_ClearsLocksForThatNode()
    {
        var ticker = NewTimeTicker();
        await _f.Provider.AddTimeTickers([ticker], CancellationToken.None);

        // Force-lock under another node id by direct write to simulate a dead node.
        await _f.TimeTickers.UpdateOneAsync(
            Builders<TimeTickerEntity>.Filter.Eq(x => x.Id, ticker.Id),
            Builders<TimeTickerEntity>.Update
                .Set(x => x.LockHolder, "dead-node")
                .Set(x => x.LockedAt, _f.FixedNow)
                .Set(x => x.Status, TickerStatus.Queued));

        await _f.Provider.ReleaseDeadNodeTimeTickerResources("dead-node", CancellationToken.None);

        var after = await _f.Provider.GetTimeTickerById(ticker.Id, CancellationToken.None);
        Assert.Equal(TickerStatus.Idle, after!.Status);
        Assert.Null(after.LockHolder);
    }

    [Fact]
    public async Task InsertCronTickerOccurrences_UniqueIndexBlocksDuplicateSlots()
    {
        var cron = NewCron();
        await _f.Provider.InsertCronTickers([cron], CancellationToken.None);

        var execTime = _f.FixedNow.AddSeconds(5);
        var occ1 = new CronTickerOccurrenceEntity<CronTickerEntity>
        {
            Id = Guid.NewGuid(),
            CronTickerId = cron.Id,
            ExecutionTime = execTime,
            Status = TickerStatus.Queued,
            CreatedAt = _f.FixedNow,
            UpdatedAt = _f.FixedNow
        };
        var occ2 = new CronTickerOccurrenceEntity<CronTickerEntity>
        {
            Id = Guid.NewGuid(),
            CronTickerId = cron.Id,
            ExecutionTime = execTime,
            Status = TickerStatus.Queued,
            CreatedAt = _f.FixedNow,
            UpdatedAt = _f.FixedNow
        };

        var inserted = await _f.Provider.InsertCronTickerOccurrences([occ1, occ2], CancellationToken.None);
        Assert.Equal(1, inserted);
    }

    [Fact]
    public async Task SkipStaleCronOccurrences_MarksOldRowsAsSkipped()
    {
        var cron = NewCron();
        await _f.Provider.InsertCronTickers([cron], CancellationToken.None);

        var occ = new CronTickerOccurrenceEntity<CronTickerEntity>
        {
            Id = Guid.NewGuid(),
            CronTickerId = cron.Id,
            ExecutionTime = _f.FixedNow.AddSeconds(-60),
            Status = TickerStatus.Idle,
            CreatedAt = _f.FixedNow.AddSeconds(-120),
            UpdatedAt = _f.FixedNow.AddSeconds(-120)
        };
        await _f.Provider.InsertCronTickerOccurrences([occ], CancellationToken.None);

        var skipped = await _f.Provider.SkipStaleCronOccurrencesAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
        Assert.Equal(1, skipped);

        var after = await _f.CronTickerOccurrences
            .Find(Builders<CronTickerOccurrenceEntity<CronTickerEntity>>.Filter.Eq(x => x.Id, occ.Id))
            .FirstOrDefaultAsync();
        Assert.Equal(TickerStatus.Skipped, after.Status);
    }

    [Fact]
    public async Task IndexProvisioner_IsIdempotent_OnRepeatedCalls()
    {
        // Fixture already ran StartAsync once. Run again — should not throw.
        var provisioner = _f.NewProvisioner();
        await provisioner.StartAsync(CancellationToken.None);
        await provisioner.StartAsync(CancellationToken.None);
    }

    [Fact]
    public async Task AcquireImmediateTimeTicker_StampsAndRenewsLease()
    {
        var ticker = NewTimeTicker();
        await _f.Provider.AddTimeTickers([ticker], CancellationToken.None);

        var acquired = await _f.Provider.AcquireImmediateTimeTickersAsync([ticker.Id], CancellationToken.None);
        Assert.Single(acquired);

        var afterAcquire = await _f.Provider.GetTimeTickerById(ticker.Id, CancellationToken.None);
        Assert.Equal(_f.FixedNow.AddSeconds(45), afterAcquire!.LeaseUntil);

        var renewedUntil = _f.FixedNow.AddMinutes(2);
        Assert.Equal(1, await _f.Provider.RenewTimeTickerLeases([ticker.Id], renewedUntil, CancellationToken.None));
        Assert.Contains(ticker.Id, await _f.Provider.GetStillHeldTickerIds([ticker.Id], [], CancellationToken.None));

        var afterRenew = await _f.Provider.GetTimeTickerById(ticker.Id, CancellationToken.None);
        Assert.Equal(renewedUntil, afterRenew!.LeaseUntil);
    }

    [Fact]
    public async Task TransitionQueuedTimeTicker_RejectsStaleGenerationAndReturnsExactWinner()
    {
        var ticker = NewTimeTicker();
        var currentToken = Guid.NewGuid();
        await _f.Provider.AddTimeTickers([ticker], CancellationToken.None);
        await _f.TimeTickers.UpdateOneAsync(
            Builders<TimeTickerEntity>.Filter.Eq(x => x.Id, ticker.Id),
            Builders<TimeTickerEntity>.Update
                .Set(x => x.Status, TickerStatus.Queued)
                .Set(x => x.LockHolder, _f.OwnerId)
                .Set(x => x.LockedAt, _f.FixedNow)
                .Set(x => x.AcquisitionToken, currentToken));

        var staleWinners = await _f.Provider.TransitionQueuedTimeTickersToInProgressAsync(
            [new AcquisitionLease(ticker.Id, Guid.NewGuid())], CancellationToken.None);
        Assert.Empty(staleWinners);

        var winners = await _f.Provider.TransitionQueuedTimeTickersToInProgressAsync(
            [new AcquisitionLease(ticker.Id, currentToken)], CancellationToken.None);
        Assert.Equal(ticker.Id, Assert.Single(winners));

        var after = await _f.Provider.GetTimeTickerById(ticker.Id, CancellationToken.None);
        Assert.Equal(TickerStatus.InProgress, after!.Status);
        Assert.Equal(currentToken, after.AcquisitionToken);
    }

    [Fact]
    public async Task AcquireTimeTickerOnDemand_ConcurrentTerminalCallsProduceOneFreshWinner()
    {
        var ticker = NewTimeTicker();
        var staleToken = Guid.NewGuid();
        ticker.Status = TickerStatus.Failed;
        ticker.AcquisitionToken = staleToken;
        ticker.LockHolder = "old-node";
        ticker.LockedAt = _f.FixedNow.AddMinutes(-2);
        ticker.LeaseUntil = _f.FixedNow.AddMinutes(-1);
        ticker.ExecutedAt = _f.FixedNow.AddMinutes(-1);
        ticker.ExceptionMessage = "old failure";
        ticker.RetryCount = 3;
        await _f.Provider.AddTimeTickers([ticker], CancellationToken.None);

        var calls = await Task.WhenAll(
            _f.Provider.AcquireTimeTickerOnDemandAsync(ticker.Id, _f.FixedNow, CancellationToken.None),
            _f.Provider.AcquireTimeTickerOnDemandAsync(ticker.Id, _f.FixedNow, CancellationToken.None));

        var winner = Assert.Single(calls, x => x is not null)!;
        Assert.Equal(TickerStatus.InProgress, winner.Status);
        Assert.NotNull(winner.AcquisitionToken);
        Assert.NotEqual(staleToken, winner.AcquisitionToken);
        Assert.Null(winner.ExceptionMessage);
        Assert.Null(winner.ExecutedAt);
        Assert.Equal(0, winner.RetryCount);
        Assert.Equal(_f.OwnerId, winner.LockHolder);
    }

    [Fact]
    public async Task RecoverStaleTickers_AppliesRestartAndCancelPolicies()
    {
        var restart = NewTimeTicker();
        restart.OnStale = StaleAction.Restart;
        var cancel = NewTimeTicker();
        cancel.OnStale = StaleAction.Cancel;
        await _f.Provider.AddTimeTickers([restart, cancel], CancellationToken.None);

        var staleUpdate = Builders<TimeTickerEntity>.Update
            .Set(x => x.Status, TickerStatus.InProgress)
            .Set(x => x.LockHolder, "dead-node")
            .Set(x => x.LockedAt, _f.FixedNow.AddMinutes(-2))
            .Set(x => x.LeaseUntil, _f.FixedNow.AddSeconds(-1));
        await _f.TimeTickers.UpdateManyAsync(
            Builders<TimeTickerEntity>.Filter.In(x => x.Id, new[] { restart.Id, cancel.Id }),
            staleUpdate);

        var result = await _f.Provider.RecoverStaleTickers(2, CancellationToken.None);

        Assert.Equal(1, result.RestartedTimeTickers);
        Assert.Equal(1, result.CancelledTimeTickers);
        var restarted = await _f.Provider.GetTimeTickerById(restart.Id, CancellationToken.None);
        var cancelled = await _f.Provider.GetTimeTickerById(cancel.Id, CancellationToken.None);
        Assert.Equal(TickerStatus.Idle, restarted!.Status);
        Assert.Equal(1, restarted.StaleRestartCount);
        Assert.Null(restarted.LeaseUntil);
        Assert.Equal(TickerStatus.Cancelled, cancelled!.Status);
        Assert.Null(cancelled.LeaseUntil);
    }

    [Fact]
    public async Task UpdateTimeTicker_ChildInProgress_DoesNotCreateStandaloneLease()
    {
        var parent = NewTimeTicker();
        var child = NewTimeTicker();
        typeof(TimeTickerEntity).GetProperty(nameof(child.ParentId))!.SetValue(child, parent.Id);
        await _f.Provider.AddTimeTickers([parent, child], CancellationToken.None);

        var update = new InternalFunctionContext
        {
            TickerId = child.Id,
            ParentId = parent.Id,
            Type = TickerType.TimeTicker
        }.SetProperty(x => x.Status, TickerStatus.InProgress);

        Assert.Equal(1, await _f.Provider.UpdateTimeTicker(update, CancellationToken.None));
        var persisted = await _f.Provider.GetTimeTickerById(child.Id, CancellationToken.None);
        Assert.Equal(TickerStatus.InProgress, persisted!.Status);
        Assert.Null(persisted.LeaseUntil);
        Assert.Null(persisted.AcquisitionToken);
        Assert.Null(persisted.LockHolder);
    }

    [Fact]
    public async Task UpdateTimeTicker_ChildTerminalWrite_ClearsLegacyLease()
    {
        var parent = NewTimeTicker();
        var child = NewTimeTicker(status: TickerStatus.InProgress);
        typeof(TimeTickerEntity).GetProperty(nameof(child.ParentId))!.SetValue(child, parent.Id);
        child.LeaseUntil = _f.FixedNow.AddMinutes(-1);
        await _f.Provider.AddTimeTickers([parent, child], CancellationToken.None);

        var update = new InternalFunctionContext
        {
            TickerId = child.Id,
            ParentId = parent.Id,
            Type = TickerType.TimeTicker
        }.SetProperty(x => x.Status, TickerStatus.Done);

        Assert.Equal(1, await _f.Provider.UpdateTimeTicker(update, CancellationToken.None));
        var persisted = await _f.Provider.GetTimeTickerById(child.Id, CancellationToken.None);
        Assert.Equal(TickerStatus.Done, persisted!.Status);
        Assert.Null(persisted.LeaseUntil);
    }

    [Fact]
    public async Task RecoverStaleTickers_DoesNotRecoverChainChildrenIndependently()
    {
        var parent = NewTimeTicker();
        var child = NewTimeTicker(status: TickerStatus.InProgress);
        typeof(TimeTickerEntity).GetProperty(nameof(child.ParentId))!.SetValue(child, parent.Id);
        child.OnStale = StaleAction.Restart;
        child.LockHolder = "dead-node";
        child.LockedAt = _f.FixedNow.AddMinutes(-2);
        child.LeaseUntil = _f.FixedNow.AddSeconds(-1);
        await _f.Provider.AddTimeTickers([parent, child], CancellationToken.None);

        var result = await _f.Provider.RecoverStaleTickers(2, CancellationToken.None);

        Assert.Equal(0, result.RestartedTimeTickers);
        Assert.Equal(0, result.CancelledTimeTickers);
        var persisted = await _f.Provider.GetTimeTickerById(child.Id, CancellationToken.None);
        Assert.Equal(TickerStatus.InProgress, persisted!.Status);
        Assert.Equal("dead-node", persisted.LockHolder);
    }

    [Fact]
    public async Task TerminalWrite_IsRejectedAfterLeaseOwnershipChanges()
    {
        var ticker = NewTimeTicker();
        await _f.Provider.AddTimeTickers([ticker], CancellationToken.None);
        Assert.Single(await _f.Provider.AcquireImmediateTimeTickersAsync([ticker.Id], CancellationToken.None));
        await _f.TimeTickers.UpdateOneAsync(
            Builders<TimeTickerEntity>.Filter.Eq(x => x.Id, ticker.Id),
            Builders<TimeTickerEntity>.Update
                .Set(x => x.LockHolder, "other-node")
                .Set(x => x.LeaseUntil, _f.FixedNow.AddMinutes(1)));

        var context = new InternalFunctionContext { TickerId = ticker.Id, Type = TickerType.TimeTicker }
            .SetProperty(x => x.Status, TickerStatus.Done)
            .SetProperty(x => x.ReleaseLock, true);

        Assert.Equal(0, await _f.Provider.UpdateTimeTicker(context, CancellationToken.None));
        var after = await _f.Provider.GetTimeTickerById(ticker.Id, CancellationToken.None);
        Assert.Equal(TickerStatus.InProgress, after!.Status);
        Assert.Equal("other-node", after.LockHolder);
    }

    [Fact]
    public async Task ImmediateTimeAcquisition_ReturnsOnlyRowsAcquiredByThisCall()
    {
        var alreadyRunning = NewTimeTicker();
        var ready = NewTimeTicker();
        await _f.Provider.AddTimeTickers([alreadyRunning, ready], CancellationToken.None);
        Assert.Single(await _f.Provider.AcquireImmediateTimeTickersAsync([alreadyRunning.Id], CancellationToken.None));

        var acquired = await _f.Provider.AcquireImmediateTimeTickersAsync(
            [alreadyRunning.Id, ready.Id], CancellationToken.None);

        Assert.Equal(ready.Id, Assert.Single(acquired).Id);
    }

    [Fact]
    public async Task ImmediateCronAcquisition_HydratesParentAndTimeout()
    {
        var cron = NewCron();
        cron.TimeoutSeconds = 17;
        await _f.Provider.InsertCronTickers([cron], CancellationToken.None);
        var occurrence = new CronTickerOccurrenceEntity<CronTickerEntity>
        {
            Id = Guid.NewGuid(),
            CronTickerId = cron.Id,
            ExecutionTime = _f.FixedNow,
            Status = TickerStatus.Queued,
            CreatedAt = _f.FixedNow,
            UpdatedAt = _f.FixedNow
        };
        await _f.Provider.InsertCronTickerOccurrences([occurrence], CancellationToken.None);

        var acquired = await _f.Provider.AcquireImmediateCronOccurrencesAsync([occurrence.Id], CancellationToken.None);

        var result = Assert.Single(acquired);
        Assert.NotNull(result.CronTicker);
        Assert.Equal(cron.Function, result.CronTicker.Function);
        Assert.Equal(17, result.CronTicker.TimeoutSeconds);
    }

    [Fact]
    public async Task RecoverStaleCronOccurrences_EnforcesParentPolicyAndRestartCap()
    {
        var restartCron = NewCron("restart-cron");
        restartCron.OnStale = StaleAction.Restart;
        var cancelCron = NewCron("cancel-cron");
        cancelCron.OnStale = StaleAction.Cancel;
        await _f.Provider.InsertCronTickers([restartCron, cancelCron], CancellationToken.None);

        var restart = new CronTickerOccurrenceEntity<CronTickerEntity>
        {
            Id = Guid.NewGuid(), CronTickerId = restartCron.Id, ExecutionTime = _f.FixedNow,
            Status = TickerStatus.InProgress, LeaseUntil = _f.FixedNow.AddSeconds(-1),
            StaleRestartCount = 0, CreatedAt = _f.FixedNow, UpdatedAt = _f.FixedNow
        };
        var exhausted = new CronTickerOccurrenceEntity<CronTickerEntity>
        {
            Id = Guid.NewGuid(), CronTickerId = restartCron.Id, ExecutionTime = _f.FixedNow.AddSeconds(1),
            Status = TickerStatus.InProgress, LeaseUntil = _f.FixedNow.AddSeconds(-1),
            StaleRestartCount = 2, CreatedAt = _f.FixedNow, UpdatedAt = _f.FixedNow
        };
        var cancelled = new CronTickerOccurrenceEntity<CronTickerEntity>
        {
            Id = Guid.NewGuid(), CronTickerId = cancelCron.Id, ExecutionTime = _f.FixedNow,
            Status = TickerStatus.InProgress, LeaseUntil = _f.FixedNow.AddSeconds(-1),
            CreatedAt = _f.FixedNow, UpdatedAt = _f.FixedNow
        };
        await _f.Provider.InsertCronTickerOccurrences([restart, exhausted, cancelled], CancellationToken.None);

        var result = await _f.Provider.RecoverStaleTickers(2, CancellationToken.None);

        Assert.Equal(1, result.RestartedCronOccurrences);
        Assert.Equal(2, result.CancelledCronOccurrences);
        var rows = await _f.Provider.GetAllCronTickerOccurrences(_ => true, CancellationToken.None);
        Assert.Equal(TickerStatus.Idle, rows.Single(x => x.Id == restart.Id).Status);
        Assert.Equal(1, rows.Single(x => x.Id == restart.Id).StaleRestartCount);
        Assert.Equal(TickerStatus.Cancelled, rows.Single(x => x.Id == exhausted.Id).Status);
        Assert.Equal(TickerStatus.Cancelled, rows.Single(x => x.Id == cancelled.Id).Status);
    }

    [Fact]
    public async Task GetAllCronTickerExpressions_ExcludesDisabledAndPaused()
    {
        var enabled = NewCron("enabled-fn");
        var disabled = NewCron("disabled-fn");
        disabled.IsEnabled = false;
        var paused = NewCron("paused-fn");
        paused.IsSystemPaused = true;

        await _f.Provider.InsertCronTickers([enabled, disabled, paused], CancellationToken.None);

        var result = await _f.Provider.GetAllCronTickerExpressions(CancellationToken.None);
        Assert.Single(result);
        Assert.Equal("enabled-fn", result[0].Function);
    }
}
