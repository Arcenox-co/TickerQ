using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using TickerQ.Provider;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;

namespace TickerQ.Tests;

/// <summary>
/// The in-memory provider replaces a published row on every state change. The replacement is a separate
/// object with its own children and no parent navigation, and it keeps the row's application namespace:
/// for the built-in entity (copied whole) and for a custom entity type (constructed afresh).
/// </summary>
public sealed class TickerInMemoryCloneFidelityTests : IDisposable
{
    // Static provider state is per closed generic type: these types isolate this class.
    private sealed class CustomTicker : TimeTickerEntity<CustomTicker> { }

    private sealed class CustomCron : CronTickerEntity { }

    private sealed class BuiltInCron : CronTickerEntity { }

    private readonly DateTime _now = new(2026, 6, 15, 12, 0, 0, DateTimeKind.Utc);
    private readonly IServiceProvider _services;

    public TickerInMemoryCloneFidelityTests()
    {
        var clock = Substitute.For<ITickerClock>();
        clock.UtcNow.Returns(_now);
        _services = new ServiceCollection()
            .AddSingleton(clock)
            .AddSingleton(new SchedulerOptionsBuilder { NodeIdentifier = "clone-node" })
            .BuildServiceProvider();
    }

    public void Dispose()
    {
        TickerInMemoryPersistenceProvider<CustomTicker, CustomCron>.ResetAllStateForTests();
        TickerInMemoryPersistenceProvider<TimeTickerEntity, BuiltInCron>.ResetAllStateForTests();
    }

    [Fact]
    public Task BuiltInEntity_ReplacementKeepsTheWholeRowAndNamespace()
        => AssertQueuedReplacementAsync(
            new TickerInMemoryPersistenceProvider<TimeTickerEntity, BuiltInCron>(_services), new TimeTickerEntity(),
            provider => provider.TimeTickersForTests);

    [Fact]
    public Task CustomEntity_ReplacementKeepsTheNamespace()
        => AssertQueuedReplacementAsync(
            new TickerInMemoryPersistenceProvider<CustomTicker, CustomCron>(_services), new CustomTicker(),
            provider => provider.TimeTickersForTests);

    private async Task AssertQueuedReplacementAsync<TTicker, TCron>(
        TickerInMemoryPersistenceProvider<TTicker, TCron> provider, TTicker ticker,
        Func<TickerInMemoryPersistenceProvider<TTicker, TCron>, IReadOnlyDictionary<Guid, TTicker>> rows)
        where TTicker : TimeTickerEntity<TTicker>, new()
        where TCron : CronTickerEntity, new()
    {
        ticker.Id = Guid.NewGuid();
        ticker.Function = "clone";
        ticker.Description = "kept";
        ticker.Retries = 3;
        ticker.ExecutionTime = _now.AddSeconds(-1);
        ticker.CreatedAt = _now;
        ticker.UpdatedAt = _now;
        ticker.ApplicationNamespaceKey = "orders";
        await provider.AddTimeTickers([ticker], CancellationToken.None);
        var published = rows(provider)[ticker.Id];

        var queued = new List<TimeTickerEntity>();
        await foreach (var row in provider.QueueTimeTickers(
                           [new TimeTickerEntity { Id = ticker.Id, UpdatedAt = published.UpdatedAt }], CancellationToken.None))
            queued.Add(row);

        Assert.Single(queued);
        var current = rows(provider)[ticker.Id];
        Assert.NotSame(published, current);
        Assert.Equal(TickerStatus.Idle, published.Status);
        Assert.Equal(TickerStatus.Queued, current.Status);
        Assert.Equal("orders", current.ApplicationNamespaceKey);
        Assert.Equal("kept", current.Description);
        Assert.Equal(3, current.Retries);
        Assert.Equal(_now, current.CreatedAt);
        Assert.Null(current.Parent);
        Assert.Empty(current.Children);
        Assert.NotSame(published.Children, current.Children);
    }
}
