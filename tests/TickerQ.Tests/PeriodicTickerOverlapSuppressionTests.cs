using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using TickerQ.Provider;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Models;
using Xunit;

namespace TickerQ.Tests;

/// <summary>
/// Regression tests for the long-running-occurrence refire bug (ScheduledCalculations): the in-memory
/// provider must anchor the next interval on the occurrence START (LastStartedAt) and must expose
/// unfinished-occurrence state so the scheduler can suppress overlapping fires for Skip tickers.
/// </summary>
public class PeriodicTickerOverlapSuppressionTests
{
    private static PeriodicTickerInMemoryPersistenceProvider<PeriodicTickerEntity> CreateProvider()
    {
        var services = new ServiceCollection();
        return new PeriodicTickerInMemoryPersistenceProvider<PeriodicTickerEntity>(services.BuildServiceProvider());
    }

    private static PeriodicTickerEntity NewTicker(ChainOverlapBehavior overlap = ChainOverlapBehavior.Allow)
        => new()
        {
            Id = Guid.NewGuid(),
            Function = "PollPort",
            Interval = TimeSpan.FromMinutes(5),
            ChainOverlapBehavior = overlap
        };

    private static (DateTime Key, InternalManagerContext[] Items) QueueInput(PeriodicTickerEntity ticker, DateTime key)
        => (key, new[] { new InternalManagerContext(ticker.Id) { FunctionName = ticker.Function, Interval = ticker.Interval } });

    [Fact]
    public async Task QueueOccurrence_AdvancesLastStartedAt_OnParent()
    {
        var provider = CreateProvider();
        var ticker = NewTicker();
        await provider.InsertPeriodicTickers(new[] { ticker }, default);

        var executionTime = DateTime.UtcNow;

        // Drain the async stream so the new occurrence is actually created.
        await foreach (var _ in provider.QueuePeriodicTickerOccurrences(QueueInput(ticker, executionTime), default)) { }

        var stored = await provider.GetPeriodicTickerById(ticker.Id, default);

        Assert.NotNull(stored);
        Assert.Equal(executionTime, stored.LastStartedAt);
        // LastExecutedAt must remain untouched — the occurrence only started, it has not completed.
        Assert.Null(stored.LastExecutedAt);
    }

    [Fact]
    public async Task GetUnfinishedIds_ReportsTickerWithQueuedOccurrence()
    {
        var provider = CreateProvider();
        var ticker = NewTicker();
        await provider.InsertPeriodicTickers(new[] { ticker }, default);

        await foreach (var _ in provider.QueuePeriodicTickerOccurrences(QueueInput(ticker, DateTime.UtcNow), default)) { }

        var unfinished = await provider.GetPeriodicTickerIdsWithUnfinishedOccurrence(new[] { ticker.Id }, default);

        Assert.Contains(ticker.Id, unfinished);
    }

    [Fact]
    public async Task GetUnfinishedIds_ExcludesTickerWhoseOccurrenceCompleted()
    {
        var provider = CreateProvider();
        var ticker = NewTicker();
        await provider.InsertPeriodicTickers(new[] { ticker }, default);

        PeriodicTickerOccurrenceEntity<PeriodicTickerEntity> created = null;
        await foreach (var occ in provider.QueuePeriodicTickerOccurrences(QueueInput(ticker, DateTime.UtcNow), default))
            created = occ;

        Assert.NotNull(created);

        // Complete the occurrence (terminal Done).
        await provider.UpdatePeriodicTickerOccurrence(
            new InternalFunctionContext { TickerId = created.Id }
                .SetProperty(x => x.Status, TickerStatus.Done),
            default);

        var unfinished = await provider.GetPeriodicTickerIdsWithUnfinishedOccurrence(new[] { ticker.Id }, default);

        Assert.DoesNotContain(ticker.Id, unfinished);
    }

    // Regression for the dashboard "list periodic tickers" 500: these endpoints pass a null predicate,
    // which used to NRE in predicate.Compile(). Null must mean "match all".
    [Fact]
    public async Task GetPeriodicTickers_NullPredicate_ReturnsAll()
    {
        var provider = CreateProvider();
        var ticker = NewTicker();
        await provider.InsertPeriodicTickers(new[] { ticker }, default);

        var all = await provider.GetPeriodicTickers(null, default);

        Assert.Contains(all, t => t.Id == ticker.Id);
    }

    [Fact]
    public async Task GetAllPeriodicTickerOccurrences_NullPredicate_DoesNotThrow()
    {
        var provider = CreateProvider();

        var occurrences = await provider.GetAllPeriodicTickerOccurrences(null, default);

        Assert.NotNull(occurrences);
    }

    // Regression for the dashboard-edit schedule wipe: updating a periodic ticker (e.g. a dashboard edit
    // carrying null/0 schedule state) must not reset LastExecutedAt / LastStartedAt / ExecutionCount.
    [Fact]
    public async Task UpdatePeriodicTickers_PreservesScheduleState()
    {
        var provider = CreateProvider();
        var ticker = NewTicker();
        await provider.InsertPeriodicTickers(new[] { ticker }, default);

        // Advance schedule state as the scheduler would.
        var executedAt = DateTime.UtcNow;
        await provider.UpdatePeriodicTickerAfterExecution(ticker.Id, executedAt, succeeded: true);

        // Simulate a dashboard edit: a fresh entity with the same Id but no schedule state.
        var edited = NewTicker();
        edited.Id = ticker.Id;
        edited.Description = "edited";
        await provider.UpdatePeriodicTickers(new[] { edited }, default);

        var stored = await provider.GetPeriodicTickerById(ticker.Id, default);

        Assert.NotNull(stored);
        Assert.Equal("edited", stored.Description);
        Assert.Equal(executedAt, stored.LastExecutedAt);
        Assert.Equal(1, stored.ExecutionCount);
    }
}
