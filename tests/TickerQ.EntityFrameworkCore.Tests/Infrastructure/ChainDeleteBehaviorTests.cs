using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using TickerQ.Tests.Shared;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;

namespace TickerQ.EntityFrameworkCore.Tests.Infrastructure;

public sealed class ChainDeleteBehaviorTests : ChainDeleteBehaviorContract<TimeTickerEntity, CronTickerEntity>, IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private ServiceProvider _services = null!;
    private TestableProvider _provider = null!;
    protected override ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity> Provider => _provider;
    protected override DateTime Now => new(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc);
    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TestTickerQDbContext>().UseSqlite(_connection).Options;
        await using var db = new TestTickerQDbContext(options);
        await db.Database.EnsureCreatedAsync();
        _services = new ServiceCollection().AddSingleton<IDbContextFactory<TestTickerQDbContext>>(new PooledDbContextFactory<TestTickerQDbContext>(options)).BuildServiceProvider();
        var clock = Substitute.For<ITickerClock>(); clock.UtcNow.Returns(Now);
        _provider = new TestableProvider(_services, clock, new SchedulerOptionsBuilder(), Substitute.For<ITickerQRedisContext>());
    }
    public async Task DisposeAsync() { await _services.DisposeAsync(); await _connection.DisposeAsync(); }

    protected override async Task SetDescendantStatusAsync(Guid rootId, Guid id, TickerStatus status)
    {
        // Simulate the worker's terminal transition directly; generic updates deliberately reject unfenced lifecycle writes.
        var options = new DbContextOptionsBuilder<TestTickerQDbContext>().UseSqlite(_connection).Options;
        await using var db = new TestTickerQDbContext(options);
        Assert.Equal(1, await db.Set<TimeTickerEntity>().Where(x => x.Id == id && x.ChainRootId == rootId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, status)));
    }

    [Fact]
    public async Task Explicit_delete_rechecks_policy_after_discovering_descendants()
    {
        var root = Chain(ChainDeleteBehavior.Cascade);
        await Provider.AddTimeTickers([root]);
        _provider.AfterExplicitTimeTickerDeleteDiscoveryAsync = (db, _, ct) => db.Set<TimeTickerEntity>()
            .Where(x => x.Id == root.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.DeleteBehavior, ChainDeleteBehavior.Restrict), ct);
        Assert.Equal(0, await Provider.RemoveTimeTickers([root.Id]));
        var saved = await Provider.GetTimeTickerById(root.Id);
        Assert.Equal(ChainDeleteBehavior.Restrict, saved.DeleteBehavior);
        Assert.Single(Assert.Single(saved.Children).Children);
    }

    [Fact]
    public async Task Explicit_delete_rechecks_a_finished_descendant_that_is_requeued()
    {
        var root = Chain(ChainDeleteBehavior.Restrict);
        var grandchild = Assert.Single(Assert.Single(root.Children).Children);
        await Provider.AddTimeTickers([root]);
        _provider.AfterExplicitTimeTickerDeleteDiscoveryAsync = (db, _, ct) => db.Set<TimeTickerEntity>()
            .Where(x => x.Id == grandchild.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, TickerStatus.Queued), ct);
        Assert.Equal(0, await Provider.RemoveTimeTickers([root.Id]));
        var saved = await Provider.GetTimeTickerById(root.Id);
        Assert.Equal(TickerStatus.Queued, Assert.Single(Assert.Single(saved.Children).Children).Status);
        Assert.Equal(ChainDeleteBehavior.Restrict, saved.DeleteBehavior);
    }

    [Fact]
    public async Task Restrict_blocks_replacement_until_descendants_finish_then_retention_resumes()
    {
        var root = Chain(ChainDeleteBehavior.Restrict);
        var replacement = Node();
        Set(Assert.Single(Assert.Single(root.Children).Children), nameof(root.Status), TickerStatus.Idle);
        await Provider.AddTimeTickers([root]);
        Assert.Equal(0, await Provider.ReplaceTimeTickerChainAsync(root.Id, replacement));
        Assert.Null(await Provider.GetTimeTickerById(replacement.Id));
        var finished = await Provider.GetTimeTickerById(root.Id);
        await SetDescendantStatusAsync(root.Id, Assert.Single(Assert.Single(finished.Children).Children).Id, TickerStatus.Done);
        Assert.Equal(ChainDeleteBehavior.Restrict, (await Provider.GetTimeTickerById(root.Id)).DeleteBehavior);
        var batch = await Provider.DeleteEligibleTimeTickerChainsAsync(new(Now.AddDays(-7), null, null, null), 10, default);
        Assert.Equal(3, batch.Deleted);
        Assert.Null(await Provider.GetTimeTickerById(root.Id));
    }

    [Fact]
    public async Task Restrict_allows_replacement_when_all_descendants_are_terminal()
    {
        var root = Chain(ChainDeleteBehavior.Restrict);
        var replacement = Node();
        await Provider.AddTimeTickers([root]);
        Assert.True(await Provider.ReplaceTimeTickerChainAsync(root.Id, replacement) > 0);
        Assert.Null(await Provider.GetTimeTickerById(root.Id));
        Assert.NotNull(await Provider.GetTimeTickerById(replacement.Id));
    }
}
