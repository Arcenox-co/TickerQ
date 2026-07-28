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
using TickerQ.EntityFrameworkCore.Infrastructure;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;
using Xunit;

namespace TickerQ.EntityFrameworkCore.Tests.Infrastructure;

// Deterministic barrier tests for the retention chain-delete race (defect A):
// A concurrent supported graph mutation (reparent of an eligible child to a live aggregate, or
// insert of a phantom child into the doomed aggregate) is interleaved at the exact point between
// subtree discovery/eligibility validation and the destructive delete, via the
// OnChainDiscoveredForTestAsync seam. The invariant: the chain delete must revalidate exact parent
// edges and topology, so it never deletes an adopted child nor orphans a phantom child — retention
// either retains the whole chain or the mutation wins; never partial data loss.
public class EfCoreRetentionRaceTests : IAsyncLifetime
{
    private SqliteConnection _connection;
    private TestTickerQDbContext _seedContext;
    private DbContextOptions<TestTickerQDbContext> _options;
    private ITickerClock _clock;
    private ITickerQRedisContext _redisContext;
    private SchedulerOptionsBuilder _schedulerOptions;
    private ServiceProvider _services;
    private readonly DateTime _now = new(2026, 07, 28, 12, 0, 0, DateTimeKind.Utc);

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();

        _clock = Substitute.For<ITickerClock>();
        _clock.UtcNow.Returns(_now);

        _redisContext = Substitute.For<ITickerQRedisContext>();
        _redisContext.HasRedisConnection.Returns(false);

        _options = new DbContextOptionsBuilder<TestTickerQDbContext>().UseSqlite(_connection).Options;
        _seedContext = new TestTickerQDbContext(_options);
        await _seedContext.Database.EnsureCreatedAsync();

        _schedulerOptions = new SchedulerOptionsBuilder { NodeIdentifier = "retention-node" };
        var services = new ServiceCollection();
        services.AddSingleton<IDbContextFactory<TestTickerQDbContext>>(
            new PooledDbContextFactory<TestTickerQDbContext>(_options));
        _services = services.BuildServiceProvider();
    }

    public async Task DisposeAsync()
    {
        _services.Dispose();
        await _seedContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private DateTime Ago(double days) => _now - TimeSpan.FromDays(days);

    private TimeTickerEntity Node(TickerStatus status, DateTime? executedAt, Guid? parentId = null)
        => new()
        {
            Id = Guid.NewGuid(),
            Function = "F",
            ExecutionTime = _now.AddMinutes(-30),
            Status = status,
            ExecutedAt = executedAt,
            ParentId = parentId,
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

    private async Task<bool> Exists(Guid id)
    {
        await using var ctx = new TestTickerQDbContext(_options);
        return await ctx.Set<TimeTickerEntity>().AsNoTracking().AnyAsync(x => x.Id == id);
    }

    private async Task<int> OrphanCount()
    {
        await using var ctx = new TestTickerQDbContext(_options);
        var all = await ctx.Set<TimeTickerEntity>().AsNoTracking().ToListAsync();
        var ids = all.Select(x => x.Id).ToHashSet();
        return all.Count(x => x.ParentId.HasValue && !ids.Contains(x.ParentId.Value));
    }

    // A provider whose seam performs a single deterministic mutation the first time the chain rooted
    // at a monitored root is discovered — simulating a graph mutation committing in the race window.
    private sealed class InterleavingProvider :
        TickerEfCorePersistenceProvider<TestTickerQDbContext, TimeTickerEntity, CronTickerEntity>
    {
        private readonly Func<TestTickerQDbContext, Guid, IReadOnlyCollection<Guid>, CancellationToken, Task> _interleave;
        private int _fired;

        public InterleavingProvider(
            IServiceProvider sp, ITickerClock clock, SchedulerOptionsBuilder opts, ITickerQRedisContext redis,
            Func<TestTickerQDbContext, Guid, IReadOnlyCollection<Guid>, CancellationToken, Task> interleave)
            : base(sp, clock, opts, redis) => _interleave = interleave;

        protected internal override async Task OnChainDiscoveredForTestAsync(
            TestTickerQDbContext dbContext, Guid rootId, IReadOnlyCollection<Guid> discoveredIds,
            CancellationToken cancellationToken)
        {
            if (Interlocked.Exchange(ref _fired, 1) == 0)
                await _interleave(dbContext, rootId, discoveredIds, cancellationToken);
        }
    }

    private InterleavingProvider ProviderThatInterleaves(
        Func<TestTickerQDbContext, Guid, IReadOnlyCollection<Guid>, CancellationToken, Task> interleave)
        => new(_services, _clock, _schedulerOptions, _redisContext, interleave);

    private static RetentionCutoffs Cutoffs(DateTime succeeded) => new(succeeded, null, null, null);

    [Fact]
    public async Task ReparentAwayDuringDiscovery_DoesNotDeleteAdoptedChild()
    {
        // Aggregate under retention: root R (eligible) with eligible child C.
        var root = Node(TickerStatus.Done, Ago(10));
        var child = Node(TickerStatus.Done, Ago(10), parentId: root.Id);
        // A separate, live (recent → ineligible) aggregate B that will adopt C.
        var liveRoot = Node(TickerStatus.Done, Ago(1));
        await Seed(root, child, liveRoot);

        // In the race window, reparent C from R to the live aggregate B.
        var provider = ProviderThatInterleaves(async (ctx, rootId, ids, ct) =>
        {
            await ctx.Set<TimeTickerEntity>()
                .Where(x => x.Id == child.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.ParentId, liveRoot.Id), ct);
        });

        var result = await provider.DeleteEligibleTimeTickerChainsAsync(
            Cutoffs(Ago(7)), 100, RetentionCursor.Start, CancellationToken.None);

        // The adopted child must survive: it now belongs to a live aggregate.
        Assert.True(await Exists(child.Id), "adopted child was deleted by retention (data loss)");
        Assert.Equal(0, await OrphanCount());
        // Whole-chain atomicity: retention retained the chain rather than partially deleting it.
        Assert.True(await Exists(root.Id));
        Assert.Equal(0, result.Deleted);
    }

    [Fact]
    public async Task PhantomChildInsertedDuringDiscovery_DoesNotOrphan()
    {
        // Aggregate under retention: root R (eligible) with eligible child C.
        var root = Node(TickerStatus.Done, Ago(10));
        var child = Node(TickerStatus.Done, Ago(10), parentId: root.Id);
        await Seed(root, child);

        // In the race window, a new (recent → ineligible) child is attached under the doomed chain.
        var phantomId = Guid.NewGuid();
        var provider = ProviderThatInterleaves(async (ctx, rootId, ids, ct) =>
        {
            ctx.Set<TimeTickerEntity>().Add(new TimeTickerEntity
            {
                Id = phantomId,
                Function = "F",
                ExecutionTime = _now.AddMinutes(-30),
                Status = TickerStatus.Done,
                ExecutedAt = Ago(1),
                ParentId = child.Id,
                CreatedAt = _now,
                UpdatedAt = _now,
                Request = Array.Empty<byte>()
            });
            await ctx.SaveChangesAsync(ct);
        });

        var result = await provider.DeleteEligibleTimeTickerChainsAsync(
            Cutoffs(Ago(7)), 100, RetentionCursor.Start, CancellationToken.None);

        // No node may be orphaned by the delete: the chain is retained whole.
        Assert.Equal(0, await OrphanCount());
        Assert.True(await Exists(root.Id), "root deleted while a freshly-attached child remained (orphan)");
        Assert.True(await Exists(child.Id));
        Assert.Equal(0, result.Deleted);
    }
}
