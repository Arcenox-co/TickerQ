using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using TickerQ.Provider;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Infrastructure;
using TickerQ.Utilities.Interfaces;

namespace TickerQ.Tests;

/// <summary>
/// The in-memory provider against the behavior of the EF Core provider and its own immutability
/// contract: published rows are replaced, never edited in place.
/// </summary>
public sealed class TickerInMemoryProviderParityTests : IDisposable
{
    // Static provider state is per closed generic type: these types isolate this class.
    private sealed class ParityTicker : TimeTickerEntity<ParityTicker> { }

    private sealed class ParityCron : CronTickerEntity { }

    private readonly DateTime _now = new(2026, 6, 15, 12, 0, 0, DateTimeKind.Utc);
    private readonly TickerInMemoryPersistenceProvider<ParityTicker, ParityCron> _provider;

    public TickerInMemoryProviderParityTests()
    {
        var clock = Substitute.For<ITickerClock>();
        clock.UtcNow.Returns(_now);
        var services = new ServiceCollection()
            .AddSingleton(clock)
            .AddSingleton(new SchedulerOptionsBuilder { NodeIdentifier = "parity-node" })
            .BuildServiceProvider();
        _provider = new TickerInMemoryPersistenceProvider<ParityTicker, ParityCron>(services);
    }

    public void Dispose() => TickerInMemoryPersistenceProvider<ParityTicker, ParityCron>.ResetAllStateForTests();

    [Fact]
    public async Task QueueProjection_CarriesMissedRunPolicy_AndChildStatuses_AsEfCoreDoes()
    {
        var child = NewTicker(executionTime: null);
        child.Status = TickerStatus.Done;
        child.DeleteBehavior = ChainDeleteBehavior.Restrict;
        var root = NewTicker(_now.AddSeconds(-1));
        root.MissedExecutionAction = MissedExecutionAction.Skip;
        root.MaximumLateness = TimeSpan.FromSeconds(30);
        root.Children = [child];
        await _provider.AddTimeTickers([root], CancellationToken.None);

        var projected = Assert.Single(await _provider.GetEarliestTimeTickers(CancellationToken.None));

        Assert.Equal(MissedExecutionAction.Skip, projected.MissedExecutionAction);
        Assert.Equal(TimeSpan.FromSeconds(30), projected.MaximumLateness);
        var projectedChild = Assert.Single(projected.Children);
        Assert.Equal(TickerStatus.Done, projectedChild.Status);
        Assert.Equal(ChainDeleteBehavior.Restrict, projectedChild.DeleteBehavior);
    }

    [Fact]
    public async Task Queueing_ReplacesChildRowsToClearProgress_InsteadOfEditingPublishedRows()
    {
        var child = NewTicker(executionTime: null);
        child.ProgressPercent = 50;
        child.ProgressMessage = "half";
        var root = NewTicker(_now.AddSeconds(-1));
        root.Children = [child];
        await _provider.AddTimeTickers([root], CancellationToken.None);
        var publishedChild = _provider.TimeTickersForTests[child.Id];

        await QueueAsync(root.Id);

        // A reader that already held the published row still sees it unchanged.
        Assert.Equal(50, publishedChild.ProgressPercent);
        Assert.Equal("half", publishedChild.ProgressMessage);
        var current = _provider.TimeTickersForTests[child.Id];
        Assert.NotSame(publishedChild, current);
        Assert.Null(current.ProgressPercent);
        Assert.Null(current.ProgressMessage);
    }

    private async Task QueueAsync(Guid id)
    {
        var current = _provider.TimeTickersForTests[id];
        var queued = new List<TimeTickerEntity>();
        await foreach (var row in _provider.QueueTimeTickers(
                           [new TimeTickerEntity { Id = id, UpdatedAt = current.UpdatedAt }], CancellationToken.None))
            queued.Add(row);
        Assert.Single(queued);
    }

    private ParityTicker NewTicker(DateTime? executionTime) => new()
    {
        Id = Guid.NewGuid(),
        Function = "parity",
        ExecutionTime = executionTime,
        CreatedAt = _now,
        UpdatedAt = _now,
    };
}

public sealed class ExecutionLogEvictionTests
{
    [Fact]
    public void FullStore_EvictsTheLeastRecentlyWrittenExecution()
    {
        var store = new InMemoryTickerExecutionLogStore(new ExecutionLogOptions { MaxBufferedExecutions = 3 });
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var third = Guid.NewGuid();
        store.Add(Line(first));
        store.Add(Line(second));
        store.Add(Line(third));
        store.Add(Line(first)); // first is now the most recently written

        store.Add(Line(Guid.NewGuid()));

        Assert.Null(store.Snapshot(second));
        Assert.NotNull(store.Snapshot(first));
        Assert.NotNull(store.Snapshot(third));
        Assert.Equal(1, store.EvictedExecutions);
    }

    private static TickerExecutionLogLine Line(Guid id) => new()
    {
        TickerId = id, Message = "line", FunctionName = "Example", Category = "Test",
        UnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), Level = 2
    };
}
