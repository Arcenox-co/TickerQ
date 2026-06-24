using MongoDB.Driver;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Models;

namespace TickerQ.MongoDB.Tests;

[Collection("Mongo")]
public class MongoPersistenceProviderTests : IClassFixture<MongoTestFixture>, IAsyncLifetime
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

        var ctx = new InternalFunctionContext
        {
            TickerId = ticker.Id,
            FunctionName = "test-fn",
            Type = TickerType.TimeTicker,
            ExecutedAt = _f.FixedNow,
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
