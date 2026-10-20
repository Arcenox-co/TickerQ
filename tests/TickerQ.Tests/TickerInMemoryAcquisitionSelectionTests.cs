using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using TickerQ.Provider;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;

namespace TickerQ.Tests;

public sealed class TickerInMemoryAcquisitionSelectionTests : IDisposable
{
    public sealed class Job : TimeTickerEntity<Job> { }
    public sealed class Cron : CronTickerEntity { }

    private static readonly DateTime Now = new(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);
    private readonly List<ServiceProvider> _services = [];

    public TickerInMemoryAcquisitionSelectionTests()
        => TickerInMemoryPersistenceProvider<Job, Cron>.ResetAllStateForTests();

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(100)]
    public async Task EarliestSecond_IsBoundedAndOrderedByExactTimeThenId_AmongRetainedHistory(int batchSize)
    {
        var provider = CreateProvider(new SchedulerOptionsBuilder { AcquisitionBatchSize = batchSize });
        var earliest = Now.AddMinutes(-5);
        var pending = Enumerable.Range(1, 45).Select(index => Row(index,
            earliest.AddTicks((index % 5) * 1_000 + 123))).ToArray();
        var later = Enumerable.Range(100, 20).Select(index => Row(index,
            earliest.AddSeconds(1).AddTicks(index))).ToArray();
        var history = Enumerable.Range(1_000, 1_000).Select(index =>
        {
            var row = Row(index, earliest.AddDays(-1));
            row.Status = TickerStatus.Done;
            row.ExecutedAt = earliest.AddHours(-1);
            return row;
        }).ToArray();
        var inlineOnly = Enumerable.Range(3_000, 20).Select(index => Row(index, null)).ToArray();
        var expected = pending.OrderBy(row => row.ExecutionTime).ThenBy(row => row.Id)
            .Take(batchSize).Select(row => (row.Id, row.ExecutionTime)).ToArray();
        // Mix all seconds/statuses and reverse the expected tie order at insertion.
        await provider.AddTimeTickers(later.Concat(history).Concat(pending.Reverse()).Concat(inlineOnly).ToArray());

        var selected = await provider.GetEarliestTimeTickers();

        Assert.Equal(expected, selected.Select(row => (row.Id, row.ExecutionTime)));
        Assert.All(selected, row => Assert.Empty(row.Children));
        Assert.Equal(pending.Length + later.Length + history.Length + inlineOnly.Length,
            provider.TimeTickersForTests.Count);
        Assert.Equal(expected, (await provider.GetEarliestTimeTickers())
            .Select(row => (row.Id, row.ExecutionTime)));
    }

    [Fact]
    public async Task Discovery_ReplacesLaterCandidates_AndDiscardsAPreviouslySelectedSecond()
    {
        var provider = CreateProvider(new SchedulerOptionsBuilder { AcquisitionBatchSize = 2 });
        await provider.AddTimeTickers(Enumerable.Range(1, 9).Select(index => Row(index, Now)).ToArray());
        // With no structural writes, dictionary traversal stays fixed. Assign times in
        // that order so both seconds encounter a full batch, a later row, then an earlier row.
        var traversal = provider.TimeTickersForTests.Select(pair => pair.Value).ToArray();
        var offsets = new[] { 1_050, 1_060, 1_070, 1_040, 90, 100, 110, 80, 2_000 };
        for (var i = 0; i < traversal.Length; i++)
            traversal[i].ExecutionTime = Now.AddMilliseconds(offsets[i]);

        var selected = await provider.GetEarliestTimeTickers();

        Assert.Equal(new[] { traversal[7].Id, traversal[4].Id }, selected.Select(row => row.Id));
        Assert.Equal(new DateTime?[] { Now.AddMilliseconds(80), Now.AddMilliseconds(90) },
            selected.Select(row => row.ExecutionTime));
    }

    [Fact]
    public async Task Discovery_PreservesStatusAndOwnerEligibility()
    {
        var options = new SchedulerOptionsBuilder { AcquisitionBatchSize = 100 };
        var provider = CreateProvider(options);
        var states = new[]
        {
            (TickerStatus.Idle, (string?)null, (DateTime?)null, true),
            (TickerStatus.Queued, (string?)null, (DateTime?)null, true),
            (TickerStatus.Idle, options.ExecutionOwnerId, (DateTime?)Now, true),
            (TickerStatus.Queued, options.ExecutionOwnerId, (DateTime?)Now, true),
            (TickerStatus.Idle, "foreign-owner", (DateTime?)null, true),
            (TickerStatus.Queued, "foreign-owner", (DateTime?)null, true),
            (TickerStatus.Idle, "foreign-owner", (DateTime?)Now, false),
            (TickerStatus.Queued, "foreign-owner", (DateTime?)Now, false),
            (TickerStatus.InProgress, options.ExecutionOwnerId, (DateTime?)Now, false),
            (TickerStatus.Done, (string?)null, (DateTime?)null, false),
            (TickerStatus.DueDone, (string?)null, (DateTime?)null, false),
            (TickerStatus.Failed, (string?)null, (DateTime?)null, false),
            (TickerStatus.Cancelled, (string?)null, (DateTime?)null, false),
            (TickerStatus.Skipped, (string?)null, (DateTime?)null, false)
        };
        var rows = states.Select((state, index) =>
        {
            var row = Row(index + 1, Now);
            (row.Status, row.LockHolder, row.LockedAt) = (state.Item1, state.Item2, state.Item3);
            return row;
        }).ToArray();
        await provider.AddTimeTickers(rows);

        var selected = await provider.GetEarliestTimeTickers();

        Assert.Equal(rows.Where((_, index) => states[index].Item4).Select(row => row.Id).Order(),
            selected.Select(row => row.Id));
        for (var i = 0; i < rows.Length; i++)
        {
            var stored = provider.TimeTickersForTests[rows[i].Id];
            Assert.Equal(states[i].Item1, stored.Status);
            Assert.Equal(states[i].Item2, stored.LockHolder);
            Assert.Equal(states[i].Item3, stored.LockedAt);
            Assert.Null(stored.AcquisitionToken);
        }
    }

    [Fact]
    public async Task Discovery_RequiresMatchingActivatedEpoch_AndKeepsNamespacesIsolated()
    {
        var optionsA = new SchedulerOptionsBuilder { AcquisitionBatchSize = 2 };
        optionsA.BindRuntimeActivationScope("selection-a", 1, schedulerEnabled: true);
        var optionsB = new SchedulerOptionsBuilder { AcquisitionBatchSize = 2 };
        optionsB.BindRuntimeActivationScope("selection-b", 1, schedulerEnabled: true);
        var providerA = CreateProvider(optionsA);
        var providerB = CreateProvider(optionsB);
        await providerA.BeginReconciliationActivationEpochAsync(1);
        await providerA.CommitReconciliationActivationEpochAsync(1);
        await providerB.BeginReconciliationActivationEpochAsync(1);
        await providerB.CommitReconciliationActivationEpochAsync(1);
        await providerA.AddTimeTickers([Row(1, Now)]);
        await providerB.AddTimeTickers([Row(1, Now.AddDays(-1))]);

        await providerA.BeginReconciliationActivationEpochAsync(2);
        Assert.Empty(await providerA.GetEarliestTimeTickers());
        Assert.Equal(Now.AddDays(-1), Assert.Single(await providerB.GetEarliestTimeTickers()).ExecutionTime);
        await providerA.CommitReconciliationActivationEpochAsync(2);
        Assert.Empty(await providerA.GetEarliestTimeTickers());

        var restartedOptions = new SchedulerOptionsBuilder();
        restartedOptions.BindRuntimeActivationScope("selection-a", 2, schedulerEnabled: true);
        var restarted = CreateProvider(restartedOptions);
        Assert.Equal(Now, Assert.Single(await restarted.GetEarliestTimeTickers()).ExecutionTime);
        Assert.Equal(Now.AddDays(-1), Assert.Single(await providerB.GetEarliestTimeTickers()).ExecutionTime);
    }

    [Fact]
    public async Task QueueProjection_PreservesDeepChains_AndSchedulesTimedDescendantsSeparately()
    {
        var provider = CreateProvider(new SchedulerOptionsBuilder());
        var root = Row(1, Now);
        root.ChainGeneration = Guid.NewGuid();
        var expectedChain = new List<Job> { root };
        for (var depth = 1; depth <= 2_048; depth++)
        {
            var child = Row(depth + 1, null);
            child.TimeoutSeconds = depth;
            expectedChain[^1].Children.Add(child);
            expectedChain.Add(child);
        }
        var scheduled = Row(10_000, Now.AddSeconds(1).AddTicks(123));
        var scheduledChild = Row(10_001, null);
        scheduled.Children.Add(scheduledChild);
        expectedChain[1_024].Children.Add(scheduled);
        await provider.AddTimeTickers([root]);

        var projected = Assert.Single(await provider.GetEarliestTimeTickers());
        foreach (var expected in expectedChain)
        {
            Assert.Equal(expected.Id, projected.Id);
            Assert.Equal(root.Id, projected.ChainRootId);
            Assert.Equal(root.ChainGeneration, projected.ChainGeneration);
            Assert.Equal(expected.TimeoutSeconds, projected.TimeoutSeconds);
            if (expected != expectedChain[^1])
                projected = Assert.Single(projected.Children);
        }
        Assert.Empty(projected.Children);

        Assert.Single(await provider.AcquireImmediateTimeTickersAsync([root.Id]));
        var next = Assert.Single(await provider.GetEarliestTimeTickers());
        Assert.Equal(scheduled.Id, next.Id);
        Assert.Equal(scheduled.ExecutionTime, next.ExecutionTime);
        Assert.Equal(expectedChain[1_024].Id, next.ParentId);
        Assert.Equal(scheduled.Id, next.ChainRootId);
        Assert.Equal(scheduledChild.Id, Assert.Single(next.Children).Id);
        Assert.Equal(scheduled.Id, Assert.Single(next.Children).ChainRootId);
    }

    [Fact]
    public async Task QueueProjection_OwnsIndependentMutableChildrenCollections()
    {
        var provider = CreateProvider(new SchedulerOptionsBuilder());
        var first = Row(1, Now);
        var second = Row(2, Now);
        first.Children.Add(Row(3, null));
        await provider.AddTimeTickers([first, second]);

        var selected = await provider.GetEarliestTimeTickers();
        var leaf = Assert.Single(selected[0].Children);
        Assert.Empty(leaf.Children);
        Assert.Empty(selected[1].Children);
        leaf.Children.Add(new TimeTickerEntity());
        selected[0].Children.Clear();
        Assert.Empty(selected[1].Children);

        var repeated = await provider.GetEarliestTimeTickers();
        Assert.Equal(first.Id, repeated[0].Id);
        Assert.Equal(new Guid(3, 0, 0, new byte[8]), Assert.Single(repeated[0].Children).Id);
        Assert.Empty(Assert.Single(repeated[0].Children).Children);
        Assert.Empty(repeated[1].Children);
        Assert.Single((await provider.GetTimeTickerById(first.Id)).Children);
    }

    private TickerInMemoryPersistenceProvider<Job, Cron> CreateProvider(SchedulerOptionsBuilder options)
    {
        var clock = Substitute.For<ITickerClock>();
        clock.UtcNow.Returns(Now);
        var services = new ServiceCollection().AddSingleton(options).AddSingleton(clock).BuildServiceProvider();
        _services.Add(services);
        return new TickerInMemoryPersistenceProvider<Job, Cron>(services);
    }

    private static Job Row(int id, DateTime? executionTime) => new()
    {
        Id = new Guid(id, 0, 0, new byte[8]),
        Function = "selection",
        ExecutionTime = executionTime,
        CreatedAt = Now.AddDays(-1),
        UpdatedAt = Now.AddDays(-1)
    };

    public void Dispose()
    {
        TickerInMemoryPersistenceProvider<Job, Cron>.ResetAllStateForTests();
        foreach (var serviceProvider in _services)
            serviceProvider.Dispose();
    }
}
