using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using TickerQ.Provider;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;
using Xunit;

namespace TickerQ.Tests;

/// <summary>
/// Durable reconciliation activation epoch on the in-memory provider. The epoch is process-wide
/// static state (the in-memory analogue of a shared store), guarded by an in-process lock, so these
/// tests reset it before each case. Verifies the fail-closed phase machine, idempotent commit,
/// concurrent convergence on one epoch, and crash-then-restart checkpoint resume.
/// </summary>
public sealed class TickerInMemoryActivationEpochTests : IDisposable
{
    private sealed class FakeTimeTicker : TimeTickerEntity<FakeTimeTicker> { }
    private sealed class FakeCronTicker : CronTickerEntity { }

    private readonly TickerInMemoryPersistenceProvider<FakeTimeTicker, FakeCronTicker> _provider;
    private readonly DateTime _now = new(2025, 6, 15, 12, 0, 0, DateTimeKind.Utc);

    public TickerInMemoryActivationEpochTests()
    {
        TickerInMemoryPersistenceProvider<FakeTimeTicker, FakeCronTicker>.ResetActivationEpochForTests();
        var clock = Substitute.For<ITickerClock>();
        clock.UtcNow.Returns(_now);
        var services = new ServiceCollection();
        services.AddSingleton(clock);
        services.AddSingleton(new SchedulerOptionsBuilder { ReconciliationEpoch = 2 });
        _provider = new TickerInMemoryPersistenceProvider<FakeTimeTicker, FakeCronTicker>(services.BuildServiceProvider());
    }

    public void Dispose() =>
        TickerInMemoryPersistenceProvider<FakeTimeTicker, FakeCronTicker>.ResetActivationEpochForTests();

    private ITickerPersistenceProvider<FakeTimeTicker, FakeCronTicker> Provider => _provider;

    [Fact]
    public void Provider_AdvertisesActivationEpochSupport()
    {
        Assert.True(Provider.SupportsReconciliationActivationEpoch);
    }

    [Fact]
    public async Task FreshStore_IsPreEpoch()
    {
        var state = await Provider.GetReconciliationActivationStateAsync(CancellationToken.None);
        Assert.Equal(ActivationEpochPhase.Pending, state.Phase);
    }

    [Fact]
    public async Task Begin_MovesToActivating_ThenCommit_MovesToActivated()
    {
        var begun = await Provider.BeginReconciliationActivationEpochAsync(1, CancellationToken.None);
        Assert.Equal(ActivationEpochPhase.Activating, begun.Phase);
        Assert.Equal(1, begun.Epoch);
        Assert.False(begun.IsActivatedFor(1));

        var committed = await Provider.CommitReconciliationActivationEpochAsync(1, CancellationToken.None);
        Assert.Equal(ActivationEpochPhase.Activated, committed.Phase);
        Assert.True(committed.IsActivatedFor(1));
    }

    [Fact]
    public async Task Commit_IsIdempotent()
    {
        await Provider.BeginReconciliationActivationEpochAsync(1, CancellationToken.None);
        await Provider.CommitReconciliationActivationEpochAsync(1, CancellationToken.None);
        var again = await Provider.CommitReconciliationActivationEpochAsync(1, CancellationToken.None);
        Assert.True(again.IsActivatedFor(1));
    }

    [Fact]
    public async Task Checkpoint_IsPersisted_AcrossBeginResume()
    {
        await Provider.BeginReconciliationActivationEpochAsync(1, CancellationToken.None);
        await Provider.AdvanceReconciliationCheckpointAsync(1, "chain-repair:batch-3", CancellationToken.None);

        // Simulate a crash between checkpoint and commit: a new begin resumes the same epoch/checkpoint.
        var resumed = await Provider.BeginReconciliationActivationEpochAsync(1, CancellationToken.None);
        Assert.Equal(ActivationEpochPhase.Activating, resumed.Phase);
        Assert.Equal("chain-repair:batch-3", resumed.Checkpoint);
        Assert.False(resumed.IsActivatedFor(1));
    }

    [Fact]
    public async Task ConcurrentStarters_ConvergeOnOneActivatedEpoch()
    {
        var barrier = new Barrier(8);
        var tasks = Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
        {
            barrier.SignalAndWait();
            await Provider.BeginReconciliationActivationEpochAsync(1, CancellationToken.None);
            return await Provider.CommitReconciliationActivationEpochAsync(1, CancellationToken.None);
        })).ToArray();

        var results = await Task.WhenAll(tasks);

        Assert.All(results, r => Assert.True(r.IsActivatedFor(1)));
        var final = await Provider.GetReconciliationActivationStateAsync(CancellationToken.None);
        Assert.Equal(1, final.Epoch);
        Assert.Equal(ActivationEpochPhase.Activated, final.Phase);
    }

    [Fact]
    public async Task HigherEpochBegin_AfterActivation_StartsNewReconciliation()
    {
        await Provider.BeginReconciliationActivationEpochAsync(1, CancellationToken.None);
        await Provider.CommitReconciliationActivationEpochAsync(1, CancellationToken.None);

        var bumped = await Provider.BeginReconciliationActivationEpochAsync(2, CancellationToken.None);

        Assert.Equal(2, bumped.Epoch);
        Assert.Equal(ActivationEpochPhase.Activating, bumped.Phase);
        Assert.Null(bumped.Checkpoint);
    }

    [Fact]
    public async Task LowerCommit_DoesNotReplaceHigherActivatingEpoch()
    {
        await Provider.BeginReconciliationActivationEpochAsync(18, CancellationToken.None);

        var staleCommit = await Provider.CommitReconciliationActivationEpochAsync(17, CancellationToken.None);

        Assert.Equal(18, staleCommit.Epoch);
        Assert.Equal(ActivationEpochPhase.Activating, staleCommit.Phase);
        var persisted = await Provider.GetReconciliationActivationStateAsync(CancellationToken.None);
        Assert.Equal(18, persisted.Epoch);
        Assert.Equal(ActivationEpochPhase.Activating, persisted.Phase);
    }

    [Theory]
    [InlineData("begin")]
    [InlineData("checkpoint")]
    [InlineData("commit")]
    public async Task ActivationMutation_RejectsPreCancelledTokenWithoutMutation(string operation)
    {
        if (operation != "begin")
            await Provider.BeginReconciliationActivationEpochAsync(7, CancellationToken.None);
        var before = await Provider.GetReconciliationActivationStateAsync(CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Func<Task> action = operation switch
        {
            "begin" => () => Provider.BeginReconciliationActivationEpochAsync(7, cancellation.Token),
            "checkpoint" => () => Provider.AdvanceReconciliationCheckpointAsync(7, "unsafe", cancellation.Token),
            _ => () => Provider.CommitReconciliationActivationEpochAsync(7, cancellation.Token)
        };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(action);

        var after = await Provider.GetReconciliationActivationStateAsync(CancellationToken.None);
        Assert.Equal(before.Epoch, after.Epoch);
        Assert.Equal(before.Phase, after.Phase);
        Assert.Equal(before.Checkpoint, after.Checkpoint);
    }

    [Fact]
    public async Task ActivatedStore_IsNeverRolledBackToLowerEpoch()
    {
        await Provider.BeginReconciliationActivationEpochAsync(5, CancellationToken.None);
        await Provider.CommitReconciliationActivationEpochAsync(5, CancellationToken.None);

        // An older rolling node begins a lower target: the store keeps the higher activated epoch.
        var state = await Provider.BeginReconciliationActivationEpochAsync(3, CancellationToken.None);
        Assert.Equal(5, state.Epoch);
        Assert.Equal(ActivationEpochPhase.Activated, state.Phase);
    }

    [Fact]
    public async Task PreEpoch_RemainsCompatibleWithImmediateAcquisition()
    {
        var occurrence = await InsertPendingOccurrence();

        var acquired = await Provider.AcquireImmediateCronOccurrencesAsync([occurrence.Id], CancellationToken.None);

        Assert.Single(acquired);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(3, true)]
    public async Task ImmediateAcquisition_RequiresExactConfiguredActivatedEpoch(long epoch, bool commit)
    {
        var occurrence = await InsertPendingOccurrence();
        await Provider.BeginReconciliationActivationEpochAsync(epoch, CancellationToken.None);
        if (commit)
            await Provider.CommitReconciliationActivationEpochAsync(epoch, CancellationToken.None);

        var acquired = await Provider.AcquireImmediateCronOccurrencesAsync([occurrence.Id], CancellationToken.None);

        Assert.Empty(acquired);
    }

    [Fact]
    public async Task ActivationTransition_WinsAtomicBoundaryAgainstImmediateAcquisition()
    {
        var occurrence = await InsertPendingOccurrence();
        await Provider.BeginReconciliationActivationEpochAsync(2, CancellationToken.None);
        await Provider.CommitReconciliationActivationEpochAsync(2, CancellationToken.None);
        var providerType = typeof(TickerInMemoryPersistenceProvider<FakeTimeTicker, FakeCronTicker>);
        var hook = providerType.GetField("BeforeCronOccurrenceMutationLockHook",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        hook.SetValue(null, (Action<Guid>)(id =>
        {
            if (id != occurrence.Id) return;
            hook.SetValue(null, null);
            Provider.BeginReconciliationActivationEpochAsync(3, CancellationToken.None).GetAwaiter().GetResult();
        }));

        try
        {
            var acquired = await Provider.AcquireImmediateCronOccurrencesAsync([occurrence.Id], CancellationToken.None);
            Assert.Empty(acquired);
            var stored = Assert.Single(await Provider.GetAllCronTickerOccurrences(x => x.Id == occurrence.Id));
            Assert.Equal(TickerStatus.Idle, stored.Status);
        }
        finally
        {
            hook.SetValue(null, null);
        }
    }

    [Fact]
    public async Task DifferentApplicationPartitions_DoNotFenceOrSerializeEachOther()
    {
        var appA = new ReconciliationActivationScope("global-mutation-app-a");
        var appB = new ReconciliationActivationScope("global-mutation-app-b");
        var optionsA = new SchedulerOptionsBuilder { ReconciliationEpoch = 2 };
        optionsA.BindRuntimeActivationScope(appA.ApplicationNamespace, 2, schedulerEnabled: true);
        var optionsB = new SchedulerOptionsBuilder { ReconciliationEpoch = 2 };
        optionsB.BindRuntimeActivationScope(appB.ApplicationNamespace, 2, schedulerEnabled: true);
        var providerA = new TickerInMemoryPersistenceProvider<FakeTimeTicker, FakeCronTicker>(
            new ServiceCollection().AddSingleton(optionsA).BuildServiceProvider());
        var providerB = new TickerInMemoryPersistenceProvider<FakeTimeTicker, FakeCronTicker>(
            new ServiceCollection().AddSingleton(optionsB).BuildServiceProvider());
        await providerA.BeginReconciliationActivationEpochAsync(appA, 2, CancellationToken.None);
        await providerA.CommitReconciliationActivationEpochAsync(appA, 2, CancellationToken.None);
        await providerB.BeginReconciliationActivationEpochAsync(appB, 2, CancellationToken.None);
        await providerB.CommitReconciliationActivationEpochAsync(appB, 2, CancellationToken.None);
        var cron = new FakeCronTicker
        {
            Id = Guid.NewGuid(), Function = "partition-b", Expression = "*/5 * * * *",
            DefinitionRevision = 1, Request = [], CreatedAt = _now, UpdatedAt = _now, IsEnabled = true
        };
        await providerB.InsertCronTickers([cron], CancellationToken.None);
        var occurrence = new CronTickerOccurrenceEntity<FakeCronTicker>
        {
            Id = Guid.NewGuid(), CronTickerId = cron.Id, DefinitionRevision = 1,
            ExecutionTime = _now.AddMinutes(1), Status = TickerStatus.Idle,
            CreatedAt = _now, UpdatedAt = _now
        };
        await providerB.InsertCronTickerOccurrences([occurrence], CancellationToken.None);

        var entered = new ManualResetEventSlim();
        var release = new ManualResetEventSlim();
        var acquisitionReachedLock = new ManualResetEventSlim();
        var providerType = typeof(TickerInMemoryPersistenceProvider<FakeTimeTicker, FakeCronTicker>);
        var repairHook = providerType
            .GetField("DuringGlobalRepairMutationHook", BindingFlags.Static | BindingFlags.NonPublic)!;
        var acquisitionHook = providerType
            .GetField("BeforeCronOccurrenceMutationLockHook", BindingFlags.Static | BindingFlags.NonPublic)!;
        repairHook.SetValue(null, (Action)(() => { entered.Set(); release.Wait(); }));
        acquisitionHook.SetValue(null, (Action<Guid>)(id =>
        {
            if (id == occurrence.Id)
                acquisitionReachedLock.Set();
        }));
        try
        {
            var repair = Task.Run(() => providerA.RepairTimeTickerChainsAsync(CancellationToken.None));
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));

            var acquisition = Task.Run(() => providerB.AcquireImmediateCronOccurrencesAsync(
                [occurrence.Id], CancellationToken.None));
            Assert.True(acquisitionReachedLock.Wait(TimeSpan.FromSeconds(5)));
            Assert.Single(await acquisition.WaitAsync(TimeSpan.FromSeconds(2)));

            release.Set();
            await repair;
        }
        finally
        {
            release.Set();
            repairHook.SetValue(null, null);
            acquisitionHook.SetValue(null, null);
        }
    }

    private async Task<CronTickerOccurrenceEntity<FakeCronTicker>> InsertPendingOccurrence()
    {
        var cron = new FakeCronTicker
        {
            Id = Guid.NewGuid(), Function = "activation-fence", Expression = "*/5 * * * *",
            DefinitionRevision = 1, Request = [], CreatedAt = _now, UpdatedAt = _now, IsEnabled = true
        };
        await Provider.InsertCronTickers([cron], CancellationToken.None);
        var occurrence = new CronTickerOccurrenceEntity<FakeCronTicker>
        {
            Id = Guid.NewGuid(), CronTickerId = cron.Id, DefinitionRevision = 1,
            ExecutionTime = _now.AddMinutes(1), Status = TickerStatus.Idle,
            CreatedAt = _now, UpdatedAt = _now
        };
        await Provider.InsertCronTickerOccurrences([occurrence], CancellationToken.None);
        return occurrence;
    }
}
