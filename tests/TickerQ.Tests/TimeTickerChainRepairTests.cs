using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using TickerQ.Provider;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Managers;
using TickerQ.Utilities.Models;

namespace TickerQ.Tests;

public sealed class TimeTickerChainRepairTests : IDisposable
{
    public sealed class TestTimeTicker : TimeTickerEntity<TestTimeTicker> { }
    public sealed class TestCronTicker : CronTickerEntity { }

    private readonly DateTime _now = new(2026, 7, 30, 12, 0, 0, DateTimeKind.Utc);
    private readonly TickerInMemoryPersistenceProvider<TestTimeTicker, TestCronTicker> _provider;
    private readonly ConcurrentDictionary<Guid, TestTimeTicker> _rows;

    public TimeTickerChainRepairTests()
    {
        var clock = Substitute.For<ITickerClock>();
        clock.UtcNow.Returns(_now);
        var services = new ServiceCollection().AddSingleton(clock).BuildServiceProvider();
        _provider = new TickerInMemoryPersistenceProvider<TestTimeTicker, TestCronTicker>(services);
        _rows = _provider.TimeTickersForTests;
        _rows.Clear();
    }

    public void Dispose() => _rows.Clear();

    [Fact]
    public async Task Repair_RepairsDeepChains_PreservesRootGeneration_AndIsIdempotent()
    {
        var generation = Guid.NewGuid();
        var root = Row(Guid.NewGuid(), null, Guid.NewGuid(), generation, "root");
        var child = Row(Guid.NewGuid(), root.Id, Guid.NewGuid(), Guid.NewGuid(), "child");
        var deep = Row(Guid.NewGuid(), child.Id, child.Id, Guid.NewGuid(), "deep");
        Seed(deep, child, root);

        var first = await _provider.RepairTimeTickerChainsAsync();

        Assert.Equal(new TimeTickerChainRepairResult(3, 3, 0), first);
        AssertCanonical(root, root.Id, generation);
        AssertCanonical(child, root.Id, generation);
        AssertCanonical(deep, root.Id, generation);
        Assert.Equal("deep", deep.Description);
        Assert.Equal(child.Id, deep.ParentId);
        Assert.Equal(new TimeTickerChainRepairResult(3, 0, 3),
            await _provider.RepairTimeTickerChainsAsync());
    }

    [Fact]
    public async Task Repair_Orphan_ThrowsTypedErrorBeforeMutatingAnyRow()
    {
        var validRoot = Row(Guid.NewGuid(), null, Guid.NewGuid(), Guid.NewGuid(), "valid");
        var validChild = Row(Guid.NewGuid(), validRoot.Id, Guid.NewGuid(), Guid.NewGuid(), "valid-child");
        var orphan = Row(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "orphan");
        var before = Snapshot(validRoot, validChild, orphan);
        Seed(validChild, orphan, validRoot);

        var error = await Assert.ThrowsAsync<TimeTickerChainRepairException>(
            () => _provider.RepairTimeTickerChainsAsync());

        Assert.Equal(TimeTickerChainMalformedKind.Orphan, error.Kind);
        Assert.Equal(orphan.Id, error.TickerId);
        AssertSnapshot(before);
    }

    [Fact]
    public async Task Repair_Cycle_ThrowsTypedErrorBeforeMutatingAnyRow()
    {
        var a = Row(Guid.NewGuid(), null, Guid.NewGuid(), Guid.NewGuid(), "a");
        var b = Row(Guid.NewGuid(), a.Id, Guid.NewGuid(), Guid.NewGuid(), "b");
        a.ParentId = b.Id;
        var before = Snapshot(a, b);
        Seed(a, b);

        var error = await Assert.ThrowsAsync<TimeTickerChainRepairException>(
            () => _provider.RepairTimeTickerChainsAsync());

        Assert.Equal(TimeTickerChainMalformedKind.Cycle, error.Kind);
        AssertSnapshot(before);
    }

    [Fact]
    public async Task Repair_RespectsCancellation_AndManagerDelegates()
    {
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _provider.RepairTimeTickerChainsAsync(new CancellationToken(canceled: true)));

        var persistence = Substitute.For<ITickerPersistenceProvider<TestTimeTicker, TestCronTicker>>();
        var expected = new TimeTickerChainRepairResult(1, 1, 0);
        persistence.RepairTimeTickerChainsAsync(Arg.Any<CancellationToken>()).Returns(expected);
        var manager = new InternalTickerManager<TestTimeTicker, TestCronTicker>(
            persistence, Substitute.For<ITickerClock>(), Substitute.For<ITickerQNotificationHubSender>(),
            new SchedulerOptionsBuilder());

        Assert.Equal(expected, await manager.RepairTimeTickerChainsAsync());
        await persistence.Received(1).RepairTimeTickerChainsAsync(Arg.Any<CancellationToken>());
    }

    private TestTimeTicker Row(Guid id, Guid? parentId, Guid? rootId, Guid? generation, string description) => new()
    {
        Id = id, ParentId = parentId, ChainRootId = rootId, ChainGeneration = generation,
        Description = description, Request = [1, 2, 3], CreatedAt = _now.AddDays(-2), UpdatedAt = _now.AddDays(-1)
    };

    private void Seed(params TestTimeTicker[] rows)
    {
        foreach (var row in rows) _rows[row.Id] = row;
    }

    private static Dictionary<Guid, (Guid? Root, Guid? Generation, DateTime Updated)> Snapshot(
        params TestTimeTicker[] rows) => rows.ToDictionary(
        x => x.Id, x => (x.ChainRootId, x.ChainGeneration, x.UpdatedAt));

    private void AssertSnapshot(Dictionary<Guid, (Guid? Root, Guid? Generation, DateTime Updated)> expected)
    {
        foreach (var (id, state) in expected)
            Assert.Equal(state, (_rows[id].ChainRootId, _rows[id].ChainGeneration, _rows[id].UpdatedAt));
    }

    private static void AssertCanonical(TestTimeTicker row, Guid rootId, Guid? generation)
    {
        Assert.Equal(rootId, row.ChainRootId);
        Assert.Equal(generation, row.ChainGeneration);
    }
}
