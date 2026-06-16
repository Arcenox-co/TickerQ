using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using TickerQ.Provider;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
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

    // ---- S1: a stale InProgress occurrence must stop suppressing a Skip ticker ----

    private sealed class MutableClock : ITickerClock
    {
        public DateTime UtcNow { get; set; }
    }

    private static (PeriodicTickerInMemoryPersistenceProvider<PeriodicTickerEntity> Provider, MutableClock Clock)
        CreateProviderWithClock(TimeSpan staleThreshold)
    {
        var clock = new MutableClock { UtcNow = new DateTime(2026, 6, 15, 12, 0, 0, DateTimeKind.Utc) };
        var services = new ServiceCollection();
        services.AddSingleton<ITickerClock>(clock);
        services.AddSingleton(new SchedulerOptionsBuilder { PeriodicOverlapStaleThreshold = staleThreshold });
        return (new PeriodicTickerInMemoryPersistenceProvider<PeriodicTickerEntity>(services.BuildServiceProvider()), clock);
    }

    // Drives an occurrence into InProgress (locked at the current clock time) via the provider API.
    private static async Task<Guid> CreateInProgressOccurrence(
        PeriodicTickerInMemoryPersistenceProvider<PeriodicTickerEntity> provider,
        PeriodicTickerEntity ticker,
        DateTime executionTime)
    {
        Guid occId = Guid.Empty;
        await foreach (var occ in provider.QueuePeriodicTickerOccurrences(
            (executionTime, new[] { new InternalManagerContext(ticker.Id) { FunctionName = ticker.Function, Interval = ticker.Interval } }),
            default))
        {
            occId = occ.Id;
        }

        await provider.UpdatePeriodicTickerOccurrencesWithUnifiedContext(
            new[] { occId },
            new InternalFunctionContext().SetProperty(x => x.Status, TickerStatus.InProgress),
            default);

        return occId;
    }

    [Fact]
    public async Task FreshInProgress_StillSuppresses_Skip()
    {
        var (provider, clock) = CreateProviderWithClock(TimeSpan.FromMinutes(10));
        var ticker = NewTicker(ChainOverlapBehavior.Skip);
        await provider.InsertPeriodicTickers(new[] { ticker }, default);

        await CreateInProgressOccurrence(provider, ticker, clock.UtcNow);

        // Only a little time passes — the occurrence is still legitimately running.
        clock.UtcNow = clock.UtcNow.AddSeconds(30);

        var unfinished = await provider.GetPeriodicTickerIdsWithUnfinishedOccurrence(new[] { ticker.Id }, default);
        Assert.Contains(ticker.Id, unfinished);
    }

    [Fact]
    public async Task StaleInProgress_NoLongerSuppresses_Skip()
    {
        var (provider, clock) = CreateProviderWithClock(TimeSpan.FromMinutes(10));
        var ticker = NewTicker(ChainOverlapBehavior.Skip);
        await provider.InsertPeriodicTickers(new[] { ticker }, default);

        await CreateInProgressOccurrence(provider, ticker, clock.UtcNow);

        // The owning node crashed/hung: the occurrence sits InProgress untouched well past the threshold.
        clock.UtcNow = clock.UtcNow.AddMinutes(20);

        var unfinished = await provider.GetPeriodicTickerIdsWithUnfinishedOccurrence(new[] { ticker.Id }, default);
        Assert.DoesNotContain(ticker.Id, unfinished);
    }

    [Fact]
    public async Task Reaper_RecoversStaleInProgress_BreakingPermanentSuppression()
    {
        var (provider, clock) = CreateProviderWithClock(TimeSpan.FromMinutes(10));
        var ticker = NewTicker(ChainOverlapBehavior.Skip);
        await provider.InsertPeriodicTickers(new[] { ticker }, default);

        var occId = await CreateInProgressOccurrence(provider, ticker, clock.UtcNow.AddMinutes(-1));

        // The owning node hung: occurrence sits InProgress untouched past the threshold, so it stops
        // suppressing the Skip ticker.
        clock.UtcNow = clock.UtcNow.AddMinutes(20);
        Assert.DoesNotContain(ticker.Id,
            await provider.GetPeriodicTickerIdsWithUnfinishedOccurrence(new[] { ticker.Id }, default));

        // The fallback reaper recovers it (releases the stale lock and re-acquires with a fresh one) so
        // the occurrence is no longer an orphan with a stale LockedAt. This is what breaks the permanent
        // suppression on a real stand without needing a process restart / dead-node release.
        await foreach (var _ in provider.QueueTimedOutPeriodicTickerOccurrences(default)) { }

        var recovered = await provider.GetAllPeriodicTickerOccurrences(o => o.Id == occId, default);
        Assert.Single(recovered);
        Assert.NotNull(recovered[0].LockedAt);
        Assert.True(recovered[0].LockedAt >= clock.UtcNow.AddMinutes(-10),
            "reaper must refresh the stale lock so the occurrence is no longer treated as abandoned");
    }
}
