using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using TickerQ.EntityFrameworkCore.Entities;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ.EntityFrameworkCore.Tests.Infrastructure;

public sealed class EfDefinitionRevisionFencingTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private DbContextOptions<TestTickerQDbContext> _options = null!;
    private TestableProvider _provider = null!;
    private DateTime _now;
    private string _owner = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();
        _now = new DateTime(2026, 7, 30, 12, 0, 0, DateTimeKind.Utc);
        var clock = Substitute.For<ITickerClock>();
        clock.UtcNow.Returns(_now);
        _options = new DbContextOptionsBuilder<TestTickerQDbContext>().UseSqlite(_connection).Options;
        await using (var context = new TestTickerQDbContext(_options))
            await context.Database.EnsureCreatedAsync();
        var scheduler = new SchedulerOptionsBuilder { NodeIdentifier = "ef-revision" };
        _owner = scheduler.ExecutionOwnerId;
        var redis = Substitute.For<ITickerQRedisContext>();
        redis.HasRedisConnection.Returns(false);
        var services = new ServiceCollection()
            .AddSingleton<IDbContextFactory<TestTickerQDbContext>>(new PooledDbContextFactory<TestTickerQDbContext>(_options))
            .BuildServiceProvider();
        _provider = new TestableProvider(services, clock, scheduler, redis);
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    [Fact]
    public async Task DirectOccurrenceInsert_StampsAuthoritativeRevision_AndQuarantinesExplicitStaleRevision()
    {
        var cron = Cron(3);
        await _provider.InsertCronTickers([cron], CancellationToken.None);
        var unstamped = Occurrence(cron.Id, 0, _now.AddMinutes(1));
        var stale = Occurrence(cron.Id, 2, _now.AddMinutes(2));

        await _provider.InsertCronTickerOccurrences([unstamped, stale], CancellationToken.None);

        var rows = await _provider.GetAllCronTickerOccurrences(x => x.CronTickerId == cron.Id, CancellationToken.None);
        Assert.Equal(3, Assert.Single(rows, x => x.Id == unstamped.Id).DefinitionRevision);
        Assert.Equal(TickerStatus.Skipped, Assert.Single(rows, x => x.Id == stale.Id).Status);
    }

    [Fact]
    public async Task DefinitionChange_QuarantinesUnleasedPendingOccurrenceWithoutDeletingIt()
    {
        await _provider.MigrateDefinedCronTickers(
            new DefinedCronSeedManifest("ef-revision", [new DefinedCronTickerSeed("quarantine", "*/5 * * * *")]),
            CancellationToken.None);
        var cron = Assert.Single(await _provider.GetCronTickers(x => x.Function == "quarantine", CancellationToken.None));
        var pending = Occurrence(cron.Id, cron.DefinitionRevision, _now.AddMinutes(1));
        await _provider.InsertCronTickerOccurrences([pending], CancellationToken.None);
        await using (var evidence = new TestTickerQDbContext(_options))
        {
            evidence.Add(new CronTickerOccurrenceResultEntity<CronTickerEntity>
            {
                TickerId = pending.Id,
                Payload = [4, 2],
                EnvelopeVersion = 1,
                MediaType = "application/octet-stream"
            });
            await evidence.SaveChangesAsync();
        }

        await _provider.MigrateDefinedCronTickers(
            new DefinedCronSeedManifest("ef-revision", [new DefinedCronTickerSeed("quarantine", "*/9 * * * *")]),
            CancellationToken.None);

        var stored = Assert.Single(await _provider.GetAllCronTickerOccurrences(x => x.Id == pending.Id, CancellationToken.None));
        Assert.Equal(TickerStatus.Skipped, stored.Status);
        Assert.Contains("revision", stored.SkippedReason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal([4, 2],
            (await _provider.GetCronTickerOccurrenceResultAsync(pending.Id, CancellationToken.None))!.ToPayloadArray());
    }

    [Fact]
    public async Task OldWriterQueuedOccurrence_CannotTransitionAfterSemanticPublication_ActualUpdateIsRevisionFenced()
    {
        await _provider.MigrateDefinedCronTickers(
            new DefinedCronSeedManifest("ef-race", [new DefinedCronTickerSeed("old-writer", "*/5 * * * *")]),
            CancellationToken.None);
        var beforePublication = Assert.Single(await _provider.GetCronTickers(
            x => x.Function == "old-writer", CancellationToken.None));
        var oldWriterOccurrence = Occurrence(beforePublication.Id, beforePublication.DefinitionRevision,
            _now.AddMinutes(1), TickerStatus.Queued);
        oldWriterOccurrence.LockHolder = _owner;
        oldWriterOccurrence.LockedAt = _now;
        oldWriterOccurrence.LeaseUntil = _now.AddMinutes(5);
        oldWriterOccurrence.AcquisitionToken = Guid.NewGuid();
        await SeedRaw(oldWriterOccurrence);

        await _provider.MigrateDefinedCronTickers(
            new DefinedCronSeedManifest("ef-race", [new DefinedCronTickerSeed("old-writer", "*/9 * * * *")]),
            CancellationToken.None);

        Assert.Empty(await _provider.TransitionQueuedCronOccurrencesToInProgressAsync(
            [new AcquisitionLease(oldWriterOccurrence.Id, oldWriterOccurrence.AcquisitionToken)],
            CancellationToken.None));
        await using var verify = new TestTickerQDbContext(_options);
        var stored = await verify.Set<CronTickerOccurrenceEntity<CronTickerEntity>>()
            .AsNoTracking().SingleAsync(x => x.Id == oldWriterOccurrence.Id);
        Assert.Equal(TickerStatus.Queued, stored.Status);
        Assert.Equal(oldWriterOccurrence.AcquisitionToken, stored.AcquisitionToken);
    }

    [Fact]
    public async Task ExpiredStaleRevision_WithRestartPolicy_IsQuarantinedInsteadOfRetried()
    {
        var cron = Cron(2);
        cron.OnStale = StaleAction.Restart;
        await _provider.InsertCronTickers([cron], CancellationToken.None);
        var timedOut = Occurrence(cron.Id, 1, _now.AddMinutes(-5), TickerStatus.InProgress);
        timedOut.LockHolder = _owner;
        timedOut.LockedAt = _now.AddMinutes(-5);
        timedOut.LeaseUntil = _now.AddMinutes(-1);
        timedOut.AcquisitionToken = Guid.NewGuid();
        await SeedRaw(timedOut);

        var recovery = await _provider.RecoverStaleTickers(3, CancellationToken.None);

        Assert.Equal(0, recovery.RestartedCronOccurrences);
        await using var verify = new TestTickerQDbContext(_options);
        var stored = await verify.Set<CronTickerOccurrenceEntity<CronTickerEntity>>()
            .AsNoTracking().SingleAsync(x => x.Id == timedOut.Id);
        Assert.Equal(TickerStatus.Skipped, stored.Status);
        Assert.Contains("revision", stored.SkippedReason, StringComparison.OrdinalIgnoreCase);
        Assert.Null(stored.LockHolder);
        Assert.Null(stored.AcquisitionToken);
        Assert.Null(stored.LeaseUntil);
    }

    [Fact]
    public async Task StaleOccurrence_IsRejectedByDiscoveryQueueImmediateTransitionAndTimedOutRecovery()
    {
        var cron = Cron(2);
        await _provider.InsertCronTickers([cron], CancellationToken.None);
        var discovery = Occurrence(cron.Id, 1, _now.AddMilliseconds(100));
        var queued = Occurrence(cron.Id, 1, _now.AddMinutes(1), TickerStatus.Queued);
        queued.LockHolder = _owner;
        queued.AcquisitionToken = Guid.NewGuid();
        var immediate = Occurrence(cron.Id, 1, _now.AddMinutes(2));
        var timedOut = Occurrence(cron.Id, 1, _now.AddMinutes(-2));
        await SeedRaw(discovery, queued, immediate, timedOut);

        Assert.Null(await _provider.GetEarliestAvailableCronOccurrence([cron.Id], CancellationToken.None));
        Assert.Empty(await _provider.AcquireImmediateCronOccurrencesAsync([immediate.Id], CancellationToken.None));
        Assert.Empty(await _provider.TransitionQueuedCronOccurrencesToInProgressAsync(
            [new AcquisitionLease(queued.Id, queued.AcquisitionToken)], CancellationToken.None));
        Assert.Empty(await ToListAsync(_provider.QueueTimedOutCronTickerOccurrences(CancellationToken.None)));
        Assert.Empty(await ToListAsync(_provider.QueueCronTickerOccurrences(
            (_now.AddMinutes(3), [new InternalManagerContext(cron.Id)
            {
                FunctionName = cron.Function, Expression = cron.Expression, DefinitionRevision = 1
            }]), CancellationToken.None)));

        await using var verify = new TestTickerQDbContext(_options);
        var recoveredTimedOut = await verify.Set<CronTickerOccurrenceEntity<CronTickerEntity>>()
            .AsNoTracking().SingleAsync(x => x.Id == timedOut.Id);
        Assert.Equal(TickerStatus.Skipped, recoveredTimedOut.Status);
        Assert.Contains("revision", recoveredTimedOut.SkippedReason, StringComparison.OrdinalIgnoreCase);
        var recoveredDiscovery = await verify.Set<CronTickerOccurrenceEntity<CronTickerEntity>>()
            .AsNoTracking().SingleAsync(x => x.Id == discovery.Id);
        Assert.Equal(TickerStatus.Skipped, recoveredDiscovery.Status);
        Assert.False(await verify.Set<CronTickerOccurrenceEntity<CronTickerEntity>>()
            .AnyAsync(x => x.CronTickerId == cron.Id && x.Status == TickerStatus.InProgress));
    }

    private CronTickerEntity Cron(long revision) => new()
    {
        Id = Guid.NewGuid(), Function = "cron-" + Guid.NewGuid().ToString("N"), Expression = "*/5 * * * *",
        DefinitionRevision = revision, Request = [], IsEnabled = true, CreatedAt = _now, UpdatedAt = _now
    };

    private CronTickerOccurrenceEntity<CronTickerEntity> Occurrence(
        Guid cronId, long revision, DateTime execution, TickerStatus status = TickerStatus.Idle) => new()
    {
        Id = Guid.NewGuid(), CronTickerId = cronId, DefinitionRevision = revision,
        ExecutionTime = execution, Status = status, CreatedAt = _now, UpdatedAt = _now
    };

    private async Task SeedRaw(params CronTickerOccurrenceEntity<CronTickerEntity>[] occurrences)
    {
        await using var context = new TestTickerQDbContext(_options);
        context.AddRange(occurrences);
        await context.SaveChangesAsync();
    }

    private static async Task<List<T>> ToListAsync<T>(IAsyncEnumerable<T> source)
    {
        var result = new List<T>();
        await foreach (var item in source) result.Add(item);
        return result;
    }
}
