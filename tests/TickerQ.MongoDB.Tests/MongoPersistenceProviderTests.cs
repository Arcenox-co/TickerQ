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

    [Fact]
    public async Task ConfigurationEdit_RejectsStaleIdleAndDeletedSnapshots()
    {
        var ticker = NewTimeTicker();
        await _f.Provider.AddTimeTickers([ticker], CancellationToken.None);
        ticker.Function = "edited";
        Assert.Equal(1, await _f.Provider.UpdateTimeTickers([ticker]));
        var acquired = Assert.Single(await _f.Provider.AcquireImmediateTimeTickersAsync([ticker.Id]));
        ticker.Function = "stale";
        Assert.Equal(0, await _f.Provider.UpdateTimeTickers([ticker]));
        var persisted = await _f.Provider.GetTimeTickerById(ticker.Id);
        Assert.Equal("edited", persisted.Function);
        Assert.Equal(acquired.AcquisitionToken, persisted.AcquisitionToken);
        await _f.TimeTickers.DeleteOneAsync(x => x.Id == ticker.Id);
        Assert.Equal(0, await _f.Provider.UpdateTimeTickers([ticker]));
        Assert.Null(await _f.Provider.GetTimeTickerById(ticker.Id));
    }

    [Fact]
    public async Task ConfigurationEdit_RejectsStaleNestedLifecycleAndPreservesSeparateChildIdentity()
    {
        var root = NewTimeTicker();
        root.Children = [NewTimeTicker()];
        await _f.Provider.AddTimeTickers([root], CancellationToken.None);
        var stale = await _f.Provider.GetTimeTickerById(root.Id);
        var child = Assert.Single(stale.Children);
        child.Function = "child-edited";
        Assert.Equal(1, await _f.Provider.UpdateTimeTickers([child]));
        var updatedChild = await _f.Provider.GetTimeTickerById(child.Id);
        Assert.Equal(root.Id, updatedChild.ChainRootId);
        Assert.Equal(root.Id, updatedChild.ParentId);
        Assert.Equal("child-edited", updatedChild.Function);
        var token = Guid.NewGuid();
        await _f.TimeTickers.UpdateOneAsync(x => x.Id == child.Id,
            Builders<TimeTickerEntity>.Update.Set(x => x.Status, TickerStatus.InProgress)
                .Set(x => x.AcquisitionToken, token).Set(x => x.LeaseUntil, _f.FixedNow.AddMinutes(5)));
        stale.Function = "must-not-write";
        Assert.Equal(0, await _f.Provider.UpdateTimeTickers([stale]));
        var persisted = await _f.Provider.GetTimeTickerById(root.Id);
        Assert.Equal(root.Function, persisted.Function);
        Assert.Equal(token, Assert.Single(persisted.Children).AcquisitionToken);
    }

    [Fact]
    public async Task ConfigurationReparent_UsesDestinationRootGenerationAndRejectsCyclesForeignParentsAndStaleSnapshots()
    {
        var source = NewTimeTicker(status: TickerStatus.Done);
        var child = NewTimeTicker(status: TickerStatus.Done);
        var leaf = NewTimeTicker(status: TickerStatus.Done);
        child.Children = [leaf];
        source.Children = [child];
        var destination = NewTimeTicker(status: TickerStatus.Done);
        await _f.Provider.AddTimeTickers([source, destination], CancellationToken.None);
        var generation = Guid.NewGuid();
        await _f.TimeTickers.UpdateOneAsync(row => row.Id == destination.Id,
            Builders<TimeTickerEntity>.Update.Set(row => row.ChainGeneration, generation));
        var stale = await _f.Provider.GetTimeTickerById(child.Id);
        var moved = await _f.Provider.GetTimeTickerById(child.Id);
        moved.ParentId = leaf.Id;
        Assert.Equal(0, await _f.Provider.UpdateTimeTickers([moved]));
        moved.ParentId = Guid.NewGuid();
        await _f.TimeTickers.InsertOneAsync(new TimeTickerEntity
        {
            Id = moved.ParentId.Value, ApplicationNamespaceKey = "foreign", Status = TickerStatus.Done
        });
        Assert.Equal(0, await _f.Provider.UpdateTimeTickers([moved]));
        moved.ParentId = destination.Id;
        Assert.Equal(2, await _f.Provider.UpdateTimeTickers([moved]));
        var persisted = await _f.Provider.GetTimeTickerById(child.Id);
        Assert.Equal(destination.Id, persisted.ParentId);
        Assert.Equal(destination.Id, persisted.ChainRootId);
        Assert.Equal(generation, persisted.ChainGeneration);
        Assert.Equal(destination.Id, Assert.Single(persisted.Children).ChainRootId);
        Assert.Equal(generation, Assert.Single(persisted.Children).ChainGeneration);
        stale.Function = "must-not-write";
        Assert.Equal(0, await _f.Provider.UpdateTimeTickers([stale]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Acquisition_batches_bound_roots_keep_complete_children_and_leave_remaining_work_eligible(bool overdue)
    {
        _f.Options.AcquisitionBatchSize = 2;
        _f.Options.FallbackBatchSize = 2;
        var roots = Enumerable.Range(0, 5).Select(i => new TimeTickerEntity
        {
            Function = "batch-root", ExecutionTime = _f.FixedNow.AddMilliseconds(overdue ? -5000 : 500),
            Children = [new TimeTickerEntity { Function = "batch-child",
                Children = [new TimeTickerEntity { Function = "batch-leaf" }] }]
        }).ToArray();
        await _f.Provider.AddTimeTickers(roots, CancellationToken.None);
        var seen = new HashSet<Guid>();
        for (var batch = 0; batch < 3; batch++)
        {
            var rows = new List<TimeTickerEntity>();
            if (overdue)
                await foreach (var row in _f.Provider.QueueTimedOutTimeTickers()) rows.Add(row);
            else rows.AddRange(await _f.Provider.GetEarliestTimeTickers());
            Assert.Equal(batch < 2 ? 2 : 1, rows.Count);
            Assert.All(rows, row =>
            {
                Assert.True(seen.Add(row.Id));
                Assert.Single(Assert.Single(row.Children).Children);
            });
            if (!overdue)
            {
                var requested = new[] { Guid.NewGuid(), rows[0].Id, rows[0].Id }
                    .Concat(rows.Skip(1).Select(x => x.Id)).Concat(roots.Select(x => x.Id)).ToArray();
                Assert.Equal(rows.Count, (await _f.Provider.AcquireImmediateTimeTickersAsync(requested)).Length);
            }
            Assert.Equal(5 - seen.Count, await _f.TimeTickers.CountDocumentsAsync(
                x => x.ParentId == null && x.Status == TickerStatus.Idle));
        }
        Assert.Equal(5, seen.Count);
    }

    [Fact]
    public async Task Dashboard_metadata_preserves_child_filters_counts_and_partitioned_chain_summaries()
    {
        var root = new TimeTickerEntity { Function = "chain", Request = new byte[1024 * 1024],
            Children = [new TimeTickerEntity { Function = "child", Request = new byte[1024 * 1024],
                Children = [new TimeTickerEntity { Function = "leaf" }] }] };
        await _f.Provider.AddTimeTickers([root, new TimeTickerEntity { Function = "single" }], CancellationToken.None);
        await _f.TimeTickers.InsertOneAsync(new TimeTickerEntity { Function = "foreign", ApplicationNamespaceKey = "other" });
        var dashboard = new TickerQ.Utilities.Infrastructure.TickerDashboardDataService<TimeTickerEntity, CronTickerEntity>(_f.Provider);
        var chains = await dashboard.GetTimeTickersFlatAsync(new TickerQ.Utilities.DashboardDtos.TimeTickerQueryFilter { HasChildren = true });
        var chain = Assert.Single(chains.Items);
        Assert.Equal(root.Id, chain.Id);
        Assert.Equal(1, chains.TotalCount);
        Assert.Equal(1, chain.ChildCount);
        Assert.Equal(3, chain.ChainSummary.TotalCount);
        var singles = await dashboard.GetTimeTickersFlatAsync(new TickerQ.Utilities.DashboardDtos.TimeTickerQueryFilter { HasChildren = false });
        Assert.Equal("single", Assert.Single(singles.Items).FunctionName);
        Assert.Equal(1, singles.TotalCount);
        Assert.Equal(1024 * 1024, (await _f.Provider.GetTimeTickerById(root.Id, default)).Request.Length);
        Assert.Equal(4, (await _f.ConcreteProvider.GetOverallJobStatusCountsAsync())[TickerStatus.Idle]);
    }

    [Fact]
    public async Task DropAllAsync_CreatesFreshProviderAndSchedulerOptions()
    {
        var originalProvider = _f.ConcreteProvider;
        var originalOptions = _f.Options;
        originalOptions.ReconciliationEpoch = 42;
        originalProvider.AfterRunnableAdmissionFenceForTestAsync = _ => Task.CompletedTask;

        await _f.DropAllAsync();

        Assert.NotSame(originalProvider, _f.ConcreteProvider);
        Assert.NotSame(originalOptions, _f.Options);
        Assert.Equal(1, _f.Options.ReconciliationEpoch);
        Assert.Null(_f.ConcreteProvider.AfterRunnableAdmissionFenceForTestAsync);
        Assert.True(_f.Provider.SupportsDurableNodeFinalizationOutbox);
    }

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
    public async Task RepairTimeTickerChains_PersistsGeneration_AndRejectsMalformedBeforeMutation()
    {
        var generation = Guid.NewGuid();
        var root = NewTimeTicker();
        root.ChainRootId = Guid.NewGuid();
        root.ChainGeneration = generation;
        var child = NewTimeTicker();
        child.ParentId = root.Id;
        child.ChainRootId = child.Id;
        child.ChainGeneration = Guid.NewGuid();
        await _f.TimeTickers.InsertManyAsync([child, root]);

        Assert.Equal(new TimeTickerChainRepairResult(2, 2, 0),
            await _f.Provider.RepairTimeTickerChainsAsync());
        var persisted = (await _f.TimeTickers.Find(Builders<TimeTickerEntity>.Filter.Empty).ToListAsync())
            .ToDictionary(x => x.Id);
        Assert.Equal((root.Id, generation),
            (persisted[root.Id].ChainRootId, persisted[root.Id].ChainGeneration));
        Assert.Equal((root.Id, generation),
            (persisted[child.Id].ChainRootId, persisted[child.Id].ChainGeneration));

        var orphan = NewTimeTicker();
        orphan.ParentId = Guid.NewGuid();
        await _f.TimeTickers.InsertOneAsync(orphan);
        var before = persisted[root.Id].UpdatedAt;

        var error = await Assert.ThrowsAsync<TimeTickerChainRepairException>(
            () => _f.Provider.RepairTimeTickerChainsAsync());
        Assert.Equal(TimeTickerChainMalformedKind.Orphan, error.Kind);
        Assert.Equal(before, (await _f.TimeTickers.Find(x => x.Id == root.Id).SingleAsync()).UpdatedAt);
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
    public async Task ReleaseAcquiredResources_EmptyIds_PreserveForeignOwnerWithNullLockedAt()
    {
        var timeTicker = NewTimeTicker(status: TickerStatus.Queued);
        await _f.Provider.AddTimeTickers([timeTicker], CancellationToken.None);
        var timeToken = Guid.NewGuid();
        await _f.TimeTickers.UpdateOneAsync(
            Builders<TimeTickerEntity>.Filter.Eq(x => x.Id, timeTicker.Id),
            Builders<TimeTickerEntity>.Update
                .Set(x => x.Status, TickerStatus.Queued)
                .Set(x => x.LockHolder, "other-node")
                .Set(x => x.LockedAt, (DateTime?)null)
                .Set(x => x.AcquisitionToken, timeToken)
                .Set(x => x.ChainGeneration, timeToken));

        var cron = NewCron();
        await _f.Provider.InsertCronTickers([cron], CancellationToken.None);
        var occurrence = new CronTickerOccurrenceEntity<CronTickerEntity>
        {
            Id = Guid.NewGuid(),
            CronTickerId = cron.Id,
            ExecutionTime = _f.FixedNow.AddMinutes(1),
            Status = TickerStatus.Idle
        };
        await _f.Provider.InsertCronTickerOccurrences([occurrence], CancellationToken.None);
        var occurrenceToken = Guid.NewGuid();
        await _f.CronTickerOccurrences.UpdateOneAsync(
            Builders<CronTickerOccurrenceEntity<CronTickerEntity>>.Filter.Eq(x => x.Id, occurrence.Id),
            Builders<CronTickerOccurrenceEntity<CronTickerEntity>>.Update
                .Set(x => x.Status, TickerStatus.Queued)
                .Set(x => x.LockHolder, "other-node")
                .Set(x => x.LockedAt, (DateTime?)null)
                .Set(x => x.AcquisitionToken, occurrenceToken));

        await _f.Provider.ReleaseAcquiredTimeTickers(Array.Empty<Guid>(), CancellationToken.None);
        await _f.Provider.ReleaseAcquiredCronTickerOccurrences(Array.Empty<Guid>(), CancellationToken.None);

        var savedTime = await _f.Provider.GetTimeTickerById(timeTicker.Id, CancellationToken.None);
        Assert.Equal(TickerStatus.Queued, savedTime!.Status);
        Assert.Equal("other-node", savedTime.LockHolder);
        Assert.Equal(timeToken, savedTime.AcquisitionToken);
        Assert.Equal(timeToken, savedTime.ChainGeneration);

        var savedOccurrence = await _f.CronTickerOccurrences
            .Find(x => x.Id == occurrence.Id)
            .SingleAsync();
        Assert.Equal(TickerStatus.Queued, savedOccurrence.Status);
        Assert.Equal("other-node", savedOccurrence.LockHolder);
        Assert.Equal(occurrenceToken, savedOccurrence.AcquisitionToken);
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
                .Set(x => x.AcquisitionToken, Guid.NewGuid())
                .Set(x => x.Status, TickerStatus.Queued));

        await _f.Provider.ReleaseDeadNodeTimeTickerResources("dead-node", CancellationToken.None);

        var after = await _f.Provider.GetTimeTickerById(ticker.Id, CancellationToken.None);
        Assert.Equal(TickerStatus.Idle, after!.Status);
        Assert.Null(after.LockHolder);
    }

    [Fact]
    public async Task ReleaseDeadNodeResources_PreserveForeignOwnerWithNullLockedAt()
    {
        var ticker = NewTimeTicker(status: TickerStatus.Queued);
        await _f.Provider.AddTimeTickers([ticker], CancellationToken.None);
        var timeToken = Guid.NewGuid();
        await _f.TimeTickers.UpdateOneAsync(
            Builders<TimeTickerEntity>.Filter.Eq(x => x.Id, ticker.Id),
            Builders<TimeTickerEntity>.Update
                .Set(x => x.Status, TickerStatus.Queued)
                .Set(x => x.LockHolder, "healthy-node")
                .Set(x => x.LockedAt, (DateTime?)null)
                .Set(x => x.AcquisitionToken, timeToken)
                .Set(x => x.ChainGeneration, timeToken));

        var cron = NewCron();
        await _f.Provider.InsertCronTickers([cron], CancellationToken.None);
        var occurrence = new CronTickerOccurrenceEntity<CronTickerEntity>
        {
            Id = Guid.NewGuid(),
            CronTickerId = cron.Id,
            ExecutionTime = _f.FixedNow.AddMinutes(1),
            Status = TickerStatus.Idle
        };
        await _f.Provider.InsertCronTickerOccurrences([occurrence], CancellationToken.None);
        var occurrenceToken = Guid.NewGuid();
        await _f.CronTickerOccurrences.UpdateOneAsync(
            Builders<CronTickerOccurrenceEntity<CronTickerEntity>>.Filter.Eq(x => x.Id, occurrence.Id),
            Builders<CronTickerOccurrenceEntity<CronTickerEntity>>.Update
                .Set(x => x.Status, TickerStatus.Queued)
                .Set(x => x.LockHolder, "healthy-node")
                .Set(x => x.LockedAt, (DateTime?)null)
                .Set(x => x.AcquisitionToken, occurrenceToken));

        await _f.Provider.ReleaseDeadNodeTimeTickerResources("dead-node", CancellationToken.None);
        await _f.Provider.ReleaseDeadNodeOccurrenceResources("dead-node", CancellationToken.None);

        var savedTime = await _f.Provider.GetTimeTickerById(ticker.Id, CancellationToken.None);
        Assert.Equal(TickerStatus.Queued, savedTime!.Status);
        Assert.Equal("healthy-node", savedTime.LockHolder);
        Assert.Equal(timeToken, savedTime.AcquisitionToken);
        Assert.Equal(timeToken, savedTime.ChainGeneration);

        var savedOccurrence = await _f.CronTickerOccurrences.Find(x => x.Id == occurrence.Id).SingleAsync();
        Assert.Equal(TickerStatus.Queued, savedOccurrence.Status);
        Assert.Equal("healthy-node", savedOccurrence.LockHolder);
        Assert.Equal(occurrenceToken, savedOccurrence.AcquisitionToken);
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
        var acquired = Assert.Single(await _f.Provider.AcquireImmediateTimeTickersAsync(
            [parent.Id], CancellationToken.None));

        var update = new InternalFunctionContext
        {
            TickerId = child.Id,
            ParentId = parent.Id,
            ChainRootId = parent.Id,
            ChainGeneration = acquired.ChainGeneration,
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
    public async Task AcquireImmediateTimeTicker_PreUpgradeAggregate_NormalizesDescendantsBeforeFencedWrite()
    {
        var root = NewTimeTicker();
        var child = NewTimeTicker();
        typeof(TimeTickerEntity).GetProperty(nameof(child.ParentId))!.SetValue(child, root.Id);
        await _f.Provider.AddTimeTickers([root, child], CancellationToken.None);
        await RemoveChainFenceFieldsAsync(root, child);

        var acquired = Assert.Single(await _f.Provider.AcquireImmediateTimeTickersAsync(
            [root.Id], CancellationToken.None));

        await AssertChildTerminalWriteAcceptedAsync(root, child, acquired.ChainGeneration!.Value);
    }

    [Fact]
    public async Task QueueTimeTickers_PreUpgradeAggregate_NormalizesDescendantsBeforeFencedWrite()
    {
        var root = NewTimeTicker();
        var child = NewTimeTicker();
        typeof(TimeTickerEntity).GetProperty(nameof(child.ParentId))!.SetValue(child, root.Id);
        await _f.Provider.AddTimeTickers([root, child], CancellationToken.None);
        await RemoveChainFenceFieldsAsync(root, child);
        var observed = await _f.Provider.GetTimeTickerById(root.Id, CancellationToken.None);

        var queued = new List<TimeTickerEntity>();
        await foreach (var item in _f.Provider.QueueTimeTickers(
                           [new TimeTickerEntity
                           {
                               Id = root.Id,
                               UpdatedAt = observed!.UpdatedAt,
                               AcquisitionToken = observed.AcquisitionToken
                           }], CancellationToken.None))
            queued.Add(item);

        var acquired = Assert.Single(queued);
        await AssertChildTerminalWriteAcceptedAsync(root, child, acquired.ChainGeneration!.Value);
    }

    [Fact]
    public async Task QueueTimedOutTimeTickers_PreUpgradeAggregate_NormalizesDescendantsBeforeFencedWrite()
    {
        var root = NewTimeTicker(_f.FixedNow.AddSeconds(-5));
        var child = NewTimeTicker();
        typeof(TimeTickerEntity).GetProperty(nameof(child.ParentId))!.SetValue(child, root.Id);
        await _f.Provider.AddTimeTickers([root, child], CancellationToken.None);
        await RemoveChainFenceFieldsAsync(root, child);

        var queued = new List<TimeTickerEntity>();
        await foreach (var item in _f.Provider.QueueTimedOutTimeTickers(CancellationToken.None))
            queued.Add(item);

        var acquired = Assert.Single(queued);
        await AssertChildTerminalWriteAcceptedAsync(root, child, acquired.ChainGeneration!.Value);
    }

    private Task RemoveChainFenceFieldsAsync(params TimeTickerEntity[] tickers)
        => _f.TimeTickers.UpdateManyAsync(
            Builders<TimeTickerEntity>.Filter.In(x => x.Id, tickers.Select(x => x.Id)),
            Builders<TimeTickerEntity>.Update.Unset(nameof(TimeTickerEntity.ChainRootId))
                .Unset(nameof(TimeTickerEntity.ChainGeneration)));

    private async Task AssertChildTerminalWriteAcceptedAsync(
        TimeTickerEntity root, TimeTickerEntity child, Guid generation)
    {
        var update = new InternalFunctionContext
        {
            TickerId = child.Id, ParentId = root.Id, ChainRootId = root.Id,
            ChainGeneration = generation, Type = TickerType.TimeTicker
        }.SetProperty(x => x.Status, TickerStatus.Done);

        Assert.Equal(1, await _f.Provider.UpdateTimeTicker(update, CancellationToken.None));
        var persistedChild = await _f.Provider.GetTimeTickerById(child.Id, CancellationToken.None);
        Assert.Equal(root.Id, persistedChild!.ChainRootId);
        Assert.Equal(generation, persistedChild.ChainGeneration);
        Assert.Equal(TickerStatus.Done, persistedChild.Status);
    }

    [Fact]
    public async Task UpdateTimeTicker_ChildTerminalWrite_ClearsLegacyLease()
    {
        var parent = NewTimeTicker();
        var child = NewTimeTicker(status: TickerStatus.InProgress);
        typeof(TimeTickerEntity).GetProperty(nameof(child.ParentId))!.SetValue(child, parent.Id);
        child.LeaseUntil = _f.FixedNow.AddMinutes(-1);
        await _f.Provider.AddTimeTickers([parent, child], CancellationToken.None);
        var acquired = Assert.Single(await _f.Provider.AcquireImmediateTimeTickersAsync(
            [parent.Id], CancellationToken.None));

        var update = new InternalFunctionContext
        {
            TickerId = child.Id,
            ParentId = parent.Id,
            ChainRootId = parent.Id,
            ChainGeneration = acquired.ChainGeneration,
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
