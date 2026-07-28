using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;
using Xunit;

namespace TickerQ.EntityFrameworkCore.Tests.Infrastructure;

public class EfCoreRetentionTests : IAsyncLifetime
{
    private SqliteConnection _connection;
    private TestTickerQDbContext _seedContext;
    private DbContextOptions<TestTickerQDbContext> _options;
    private TestableProvider _provider;
    private ITickerClock _clock;
    private readonly DateTime _now = new(2026, 07, 28, 12, 0, 0, DateTimeKind.Utc);

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();

        _clock = Substitute.For<ITickerClock>();
        _clock.UtcNow.Returns(_now);

        var redisContext = Substitute.For<ITickerQRedisContext>();
        redisContext.HasRedisConnection.Returns(false);

        _options = new DbContextOptionsBuilder<TestTickerQDbContext>().UseSqlite(_connection).Options;
        _seedContext = new TestTickerQDbContext(_options);
        await _seedContext.Database.EnsureCreatedAsync();

        var schedulerOptions = new SchedulerOptionsBuilder { NodeIdentifier = "retention-node" };
        var services = new ServiceCollection();
        services.AddSingleton<IDbContextFactory<TestTickerQDbContext>>(
            new PooledDbContextFactory<TestTickerQDbContext>(_options));
        _provider = new TestableProvider(services.BuildServiceProvider(), _clock, schedulerOptions, redisContext);
    }

    public async Task DisposeAsync()
    {
        await _seedContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    // ---- helpers ----

    private DateTime Ago(double days) => _now - TimeSpan.FromDays(days);

    private static RetentionCutoffs Cutoffs(
        DateTime? succeeded = null, DateTime? failed = null, DateTime? cancelled = null, DateTime? skipped = null)
        => new(succeeded, failed, cancelled, skipped);

    private Task<RetentionChainBatchResult> Prune(RetentionCutoffs c, int batch, RetentionCursor cursor = default)
        => _provider.DeleteEligibleTimeTickerChainsAsync(c, batch, cursor, CancellationToken.None);

    private TimeTickerEntity Node(
        TickerStatus status, DateTime? executedAt, Guid? parentId = null,
        string lockHolder = null, Guid? acquisitionToken = null, DateTime? leaseUntil = null)
        => new()
        {
            Id = Guid.NewGuid(),
            Function = "F",
            ExecutionTime = _now.AddMinutes(-30),
            Status = status,
            ExecutedAt = executedAt,
            ParentId = parentId,
            LockHolder = lockHolder,
            AcquisitionToken = acquisitionToken,
            LeaseUntil = leaseUntil,
            CreatedAt = _now.AddDays(-40),
            UpdatedAt = _now.AddDays(-40),
            Request = Array.Empty<byte>()
        };

    private async Task Seed(params TimeTickerEntity[] tickers)
    {
        _seedContext.Set<TimeTickerEntity>().AddRange(tickers);
        await _seedContext.SaveChangesAsync();
        foreach (var e in _seedContext.ChangeTracker.Entries().ToList()) e.State = EntityState.Detached;
    }

    private CronTickerOccurrenceEntity<CronTickerEntity> Occurrence(
        Guid cronTickerId, TickerStatus status, DateTime? executedAt,
        string lockHolder = null, Guid? acquisitionToken = null, DateTime? leaseUntil = null)
        => new()
        {
            Id = Guid.NewGuid(),
            CronTickerId = cronTickerId,
            Status = status,
            ExecutedAt = executedAt,
            ExecutionTime = _now.AddMinutes(-30).AddSeconds(-Math.Abs(Guid.NewGuid().GetHashCode()) % 100000),
            LockHolder = lockHolder,
            AcquisitionToken = acquisitionToken,
            LeaseUntil = leaseUntil,
            CreatedAt = _now.AddDays(-40),
            UpdatedAt = _now.AddDays(-40)
        };

    private async Task<bool> TimeExists(Guid id)
    {
        await using var ctx = new TestTickerQDbContext(_options);
        return await ctx.Set<TimeTickerEntity>().AsNoTracking().AnyAsync(x => x.Id == id);
    }

    private async Task<int> TimeCount()
    {
        await using var ctx = new TestTickerQDbContext(_options);
        return await ctx.Set<TimeTickerEntity>().AsNoTracking().CountAsync();
    }

    // ---- tests ----

    [Fact]
    public void SupportsRetention_IsTrue()
    {
        Assert.True(((ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity>)_provider).SupportsRetention);
    }

    [Fact]
    public async Task Deletes_Done_And_DueDone_UnderSucceededWindow()
    {
        var done = Node(TickerStatus.Done, Ago(10));
        var dueDone = Node(TickerStatus.DueDone, Ago(10));
        await Seed(done, dueDone);

        var result = await Prune(Cutoffs(succeeded: Ago(7)), 100);

        Assert.Equal(2, result.Deleted);
        Assert.False(await TimeExists(done.Id));
        Assert.False(await TimeExists(dueDone.Id));
    }

    [Fact]
    public async Task MixedWindows_DeletePerStatus_RetainNullWindow()
    {
        var failed = Node(TickerStatus.Failed, Ago(10));
        var cancelled = Node(TickerStatus.Cancelled, Ago(10));
        var skipped = Node(TickerStatus.Skipped, Ago(10));
        var doneNoWindow = Node(TickerStatus.Done, Ago(100));
        await Seed(failed, cancelled, skipped, doneNoWindow);

        var result = await Prune(Cutoffs(failed: Ago(7), cancelled: Ago(7), skipped: Ago(7)), 100);

        Assert.Equal(3, result.Deleted);
        Assert.True(await TimeExists(doneNoWindow.Id));
    }

    [Fact]
    public async Task NullExecutedAt_IsRetained()
    {
        var noTs = Node(TickerStatus.Cancelled, executedAt: null);
        await Seed(noTs);

        var result = await Prune(Cutoffs(cancelled: Ago(1)), 100);

        Assert.Equal(0, result.Deleted);
        Assert.True(await TimeExists(noTs.Id));
    }

    [Fact]
    public async Task Owned_Or_LiveLeased_Retained_StaleLockHolderDeleted()
    {
        var owned = Node(TickerStatus.Done, Ago(10), acquisitionToken: Guid.NewGuid());
        var liveLeased = Node(TickerStatus.Done, Ago(10), leaseUntil: _now.AddMinutes(30));
        var staleLock = Node(TickerStatus.Done, Ago(10), lockHolder: "node-that-ran-it");
        var expiredLease = Node(TickerStatus.Done, Ago(10), leaseUntil: _now.AddMinutes(-30));
        await Seed(owned, liveLeased, staleLock, expiredLease);

        var result = await Prune(Cutoffs(succeeded: Ago(7)), 100);

        Assert.Equal(2, result.Deleted);
        Assert.True(await TimeExists(owned.Id));
        Assert.True(await TimeExists(liveLeased.Id));
        Assert.False(await TimeExists(staleLock.Id));
        Assert.False(await TimeExists(expiredLease.Id));
    }

    [Fact]
    public async Task ExactCutoffBoundary_Retained_StrictlyOlderDeleted()
    {
        var cutoff = Ago(7);
        var atCutoff = Node(TickerStatus.Done, cutoff);
        var older = Node(TickerStatus.Done, cutoff.AddTicks(-1));
        await Seed(atCutoff, older);

        var result = await Prune(Cutoffs(succeeded: cutoff), 100);

        Assert.Equal(1, result.Deleted);
        Assert.True(await TimeExists(atCutoff.Id));
        Assert.False(await TimeExists(older.Id));
    }

    [Fact]
    public async Task Chain_4Levels_AllEligible_DeletedEntirely()
    {
        var root = Node(TickerStatus.Done, Ago(10));
        var child = Node(TickerStatus.DueDone, Ago(10), parentId: root.Id);
        var grand = Node(TickerStatus.Failed, Ago(10), parentId: child.Id);
        var great = Node(TickerStatus.Skipped, Ago(10), parentId: grand.Id);
        await Seed(root, child, grand, great);

        var result = await Prune(Cutoffs(succeeded: Ago(7), failed: Ago(7), skipped: Ago(7)), 100);

        Assert.Equal(4, result.Deleted);
        Assert.Equal(0, await TimeCount());
    }

    [Fact]
    public async Task Chain_RetainedEntirely_WhenAnyDeepNodeIneligible()
    {
        var root = Node(TickerStatus.Done, Ago(10));
        var child = Node(TickerStatus.Done, Ago(10), parentId: root.Id);
        var grand = Node(TickerStatus.Done, Ago(10), parentId: child.Id);
        var great = Node(TickerStatus.Done, Ago(1), parentId: grand.Id); // too recent
        await Seed(root, child, grand, great);

        var result = await Prune(Cutoffs(succeeded: Ago(7)), 100);

        Assert.Equal(0, result.Deleted);
        Assert.Equal(4, await TimeCount());
    }

    [Fact]
    public async Task Chain_RetainedEntirely_WhenDeepNodeStatusHasNullWindow()
    {
        var root = Node(TickerStatus.Done, Ago(10));
        var child = Node(TickerStatus.Failed, Ago(10), parentId: root.Id); // failed window not configured
        await Seed(root, child);

        var result = await Prune(Cutoffs(succeeded: Ago(7)), 100);

        Assert.Equal(0, result.Deleted);
        Assert.Equal(2, await TimeCount());
    }

    [Fact]
    public async Task Batch_CapsRootChains_AndReportsHasMore_ThreadingCursor()
    {
        for (var i = 0; i < 5; i++) await Seed(Node(TickerStatus.Done, Ago(10 + i)));

        var first = await Prune(Cutoffs(succeeded: Ago(7)), 2, RetentionCursor.Start);
        Assert.Equal(2, first.Deleted);
        Assert.True(first.HasMore);

        var second = await Prune(Cutoffs(succeeded: Ago(7)), 2, first.NextCursor);
        Assert.Equal(2, second.Deleted);
        Assert.True(second.HasMore);

        var third = await Prune(Cutoffs(succeeded: Ago(7)), 2, second.NextCursor);
        Assert.Equal(1, third.Deleted);
        Assert.False(third.HasMore);
    }

    [Fact]
    public async Task OversizedChain_IsRetainedWhole()
    {
        var root = Node(TickerStatus.Done, Ago(10));
        var child = Node(TickerStatus.Done, Ago(10), root.Id);
        var grandchild = Node(TickerStatus.Done, Ago(10), child.Id);
        var greatGrandchild = Node(TickerStatus.Done, Ago(10), grandchild.Id);
        await Seed(root, child, grandchild, greatGrandchild);

        var result = await Prune(
            new RetentionCutoffs(Ago(7), null, null, null, maxNodesPerChain: 3), 100);

        Assert.Equal(0, result.Deleted);
        Assert.Equal(4, await CountTimeRows());
    }

    [Fact]
    public async Task BlockedOldestRoot_DoesNotStarve_LaterEligibleRoot()
    {
        // R1: oldest ExecutedAt, blocked by a too-recent child.
        var r1Root = Node(TickerStatus.Done, Ago(20));
        var r1Child = Node(TickerStatus.Done, Ago(1), parentId: r1Root.Id);
        await Seed(r1Root, r1Child);
        // R2: newer, fully eligible.
        var r2 = Node(TickerStatus.Done, Ago(10));
        await Seed(r2);

        var call1 = await Prune(Cutoffs(succeeded: Ago(7)), 1, RetentionCursor.Start);
        Assert.Equal(0, call1.Deleted);
        Assert.True(call1.HasMore);

        var call2 = await Prune(Cutoffs(succeeded: Ago(7)), 1, call1.NextCursor);
        Assert.Equal(1, call2.Deleted);

        Assert.True(await TimeExists(r1Root.Id));   // blocked chain retained whole
        Assert.True(await TimeExists(r1Child.Id));
        Assert.False(await TimeExists(r2.Id));       // later eligible root deleted
    }

    [Fact]
    public async Task CursorWraps_AtEnd_AndReconsidersNewlyEligibleRecords()
    {
        var r1 = Node(TickerStatus.Done, Ago(20));
        await Seed(r1);
        var r2Root = Node(TickerStatus.Done, Ago(10));
        var r2Child = Node(TickerStatus.Done, Ago(1), parentId: r2Root.Id);
        await Seed(r2Root, r2Child);

        var p1 = await Prune(Cutoffs(succeeded: Ago(7)), 1, RetentionCursor.Start);
        Assert.Equal(1, p1.Deleted);
        Assert.True(p1.HasMore);

        var p2 = await Prune(Cutoffs(succeeded: Ago(7)), 1, p1.NextCursor);
        Assert.Equal(0, p2.Deleted);
        Assert.False(p2.HasMore);
        Assert.False(p2.NextCursor.HasValue); // wrapped to Start

        // R2's child ages into eligibility.
        await using (var ctx = new TestTickerQDbContext(_options))
        {
            var child = await ctx.Set<TimeTickerEntity>().FirstAsync(x => x.Id == r2Child.Id);
            child.ExecutedAt = Ago(9);
            await ctx.SaveChangesAsync();
        }

        var p3 = await Prune(Cutoffs(succeeded: Ago(7)), 1, p2.NextCursor);
        Assert.Equal(2, p3.Deleted);
        Assert.False(await TimeExists(r2Root.Id));
        Assert.False(await TimeExists(r2Child.Id));
    }

    [Fact]
    public async Task CronOccurrences_DeletedByWindow_DefinitionNeverDeleted()
    {
        var cron = new CronTickerEntity
        {
            Id = Guid.NewGuid(), Function = "F", Expression = "* * * * *",
            CreatedAt = _now.AddDays(-40), UpdatedAt = _now.AddDays(-40), Request = Array.Empty<byte>()
        };
        _seedContext.Set<CronTickerEntity>().Add(cron);
        await _seedContext.SaveChangesAsync();

        var oldDone = Occurrence(cron.Id, TickerStatus.Done, Ago(10));
        var recent = Occurrence(cron.Id, TickerStatus.Done, Ago(1));
        var owned = Occurrence(cron.Id, TickerStatus.Done, Ago(10), acquisitionToken: Guid.NewGuid());
        _seedContext.Set<CronTickerOccurrenceEntity<CronTickerEntity>>().AddRange(oldDone, recent, owned);
        await _seedContext.SaveChangesAsync();
        foreach (var e in _seedContext.ChangeTracker.Entries().ToList()) e.State = EntityState.Detached;

        var result = await _provider.DeleteEligibleCronTickerOccurrencesAsync(Cutoffs(succeeded: Ago(7)), 100, CancellationToken.None);

        Assert.Equal(1, result.Deleted);
        await using var ctx = new TestTickerQDbContext(_options);
        Assert.True(await ctx.Set<CronTickerEntity>().AnyAsync(x => x.Id == cron.Id)); // definition preserved
        Assert.Equal(2, await ctx.Set<CronTickerOccurrenceEntity<CronTickerEntity>>().CountAsync());
        Assert.False(await ctx.Set<CronTickerOccurrenceEntity<CronTickerEntity>>().AnyAsync(x => x.Id == oldDone.Id));
    }

    [Fact]
    public async Task CronOccurrences_Batch_ReportsHasMore()
    {
        var cron = new CronTickerEntity
        {
            Id = Guid.NewGuid(), Function = "F", Expression = "* * * * *",
            CreatedAt = _now.AddDays(-40), UpdatedAt = _now.AddDays(-40), Request = Array.Empty<byte>()
        };
        _seedContext.Set<CronTickerEntity>().Add(cron);
        await _seedContext.SaveChangesAsync();
        var occ = Enumerable.Range(0, 3).Select(_ => Occurrence(cron.Id, TickerStatus.Failed, Ago(10))).ToArray();
        _seedContext.Set<CronTickerOccurrenceEntity<CronTickerEntity>>().AddRange(occ);
        await _seedContext.SaveChangesAsync();
        foreach (var e in _seedContext.ChangeTracker.Entries().ToList()) e.State = EntityState.Detached;

        var first = await _provider.DeleteEligibleCronTickerOccurrencesAsync(Cutoffs(failed: Ago(7)), batchSize: 2, CancellationToken.None);
        Assert.Equal(2, first.Deleted);
        Assert.True(first.HasMore);

        var second = await _provider.DeleteEligibleCronTickerOccurrencesAsync(Cutoffs(failed: Ago(7)), batchSize: 2, CancellationToken.None);
        Assert.Equal(1, second.Deleted);
        Assert.False(second.HasMore);
    }

    [Fact]
    public void Model_HasRetentionIndexes()
    {
        using var ctx = new TestTickerQDbContext(_options);
        var timeIndexes = ctx.Model.FindEntityType(typeof(TimeTickerEntity))!.GetIndexes()
            .Select(i => string.Join(",", i.Properties.Select(p => p.Name))).ToList();
        var occIndexes = ctx.Model.FindEntityType(typeof(CronTickerOccurrenceEntity<CronTickerEntity>))!.GetIndexes()
            .Select(i => string.Join(",", i.Properties.Select(p => p.Name))).ToList();

        Assert.Contains("Status,ExecutedAt", timeIndexes);
        Assert.Contains("Status,ExecutedAt", occIndexes);
    }
}
