using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

using Xunit;

namespace TickerQ.Tests.Shared.ProviderReliability;

/// <summary>
/// One authoritative behavioral contract that every reliability-capable
/// <see cref="ITickerPersistenceProvider{TTimeTicker,TCronTicker}"/> must satisfy,
/// exercised as real runtime behavior against a live store (SQLite for EF Core,
/// a MongoDB container for Mongo). The source lives under <c>tests/Shared</c> and is
/// <c>&lt;Compile Include ... Link&gt;</c>-linked into both provider test projects so the
/// EF and Mongo suites run byte-identical assertions rather than provider-private,
/// reflection-only "method exists" checks.
///
/// A concrete subclass supplies the four provider seams below (<see cref="Provider"/>,
/// <see cref="Now"/>, <see cref="OwnerId"/>, <see cref="Options"/>) and the per-test
/// clean-slate lifecycle. Everything else — seeding, mutation, and read-back — is driven
/// through the public provider interface, so no assertion here depends on a provider's
/// storage internals.
///
/// Contract cases (from the reliability hardening plan, Task 22):
///   1. Acquisition race: N concurrent acquirers, exactly one winner.
///   2. Same-node mixed immediate acquisition returns only newly acquired rows.
///   3. Lease stamped on acquisition.
///   4. Lease renewal count reflects actual rows.
///   5. Ownership transfer rejects stale terminal write.
///   6. Restart/cancel stale policy and restart cap (time tickers and cron occurrences).
///   7. Lease ratio validation (renewal headroom vs. configured ratio).
///   8. Cron parent hydration preserves function/retries/timeout.
///   9. Root → child → grandchild execution projection.
///  10. Release clears ownership and generation.
/// </summary>
public abstract class ProviderReliabilityContractTests
{
    /// <summary>The live provider under test, backed by a real store.</summary>
    protected abstract ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity> Provider { get; }

    /// <summary>The fixed "now" the provider's clock returns for the duration of a test.</summary>
    protected abstract DateTime Now { get; }

    /// <summary>The execution owner id the provider stamps as <c>LockHolder</c> when it acquires a row.</summary>
    protected abstract string OwnerId { get; }

    /// <summary>The scheduler options the provider was constructed with (lease duration/interval, restart cap).</summary>
    protected abstract SchedulerOptionsBuilder Options { get; }

    private TimeSpan LeaseDuration => Options.LeaseDuration;

    /// <summary>
    /// How many concurrent acquirers the race contract fans out. Relational providers
    /// backed by a single shared in-memory connection serialize writes, so they override
    /// this to a small value; a real networked store can push it higher.
    /// </summary>
    protected virtual int RaceParallelism => 4;

    // ---------------------------------------------------------------------
    // 1. Acquisition race — exactly one concurrent acquirer wins.
    // ---------------------------------------------------------------------

    [Fact]
    public async Task AcquisitionRace_ConcurrentOnDemandAcquirers_ExactlyOneWins()
    {
        var ticker = NewTimeTicker(status: TickerStatus.Failed);
        ticker.AcquisitionToken = Guid.NewGuid();
        await Provider.AddTimeTickers([ticker], CancellationToken.None);

        var attempts = Enumerable.Range(0, RaceParallelism)
            .Select(_ => Provider.AcquireTimeTickerOnDemandAsync(ticker.Id, Now, CancellationToken.None))
            .ToArray();
        var results = await Task.WhenAll(attempts);

        // Exactly one caller acquires the row; the returned identity/generation is the winner's.
        // (Only fields both providers project onto the returned entity are asserted here —
        // Status/LockHolder are verified below against the persisted row, which both return in full.)
        var winner = Assert.Single(results, r => r is not null)!;
        Assert.Equal(ticker.Id, winner.Id);
        Assert.NotNull(winner.AcquisitionToken);

        // The store agrees there is exactly one live owner under a fresh generation.
        var persisted = await Provider.GetTimeTickerById(ticker.Id, CancellationToken.None);
        Assert.Equal(TickerStatus.InProgress, persisted!.Status);
        Assert.Equal(OwnerId, persisted.LockHolder);
        Assert.Equal(winner.AcquisitionToken, persisted.AcquisitionToken);
    }

    // ---------------------------------------------------------------------
    // 2. Same-node mixed immediate acquisition — only newly acquired rows come back.
    // ---------------------------------------------------------------------

    [Fact]
    public async Task ImmediateAcquisition_MixedWithAlreadyRunningRow_ReturnsOnlyNewlyAcquired()
    {
        var alreadyRunning = NewTimeTicker();
        var ready = NewTimeTicker();
        await Provider.AddTimeTickers([alreadyRunning, ready], CancellationToken.None);

        // This node already holds the first row from an earlier acquisition.
        Assert.Single(await Provider.AcquireImmediateTimeTickersAsync([alreadyRunning.Id], CancellationToken.None));

        var acquired = await Provider.AcquireImmediateTimeTickersAsync(
            [alreadyRunning.Id, ready.Id], CancellationToken.None);

        // Re-surfacing the running row would re-dispatch a running job — the contract forbids it.
        var single = Assert.Single(acquired);
        Assert.Equal(ready.Id, single.Id);
    }

    // ---------------------------------------------------------------------
    // 3. Lease stamped on acquisition.
    // ---------------------------------------------------------------------

    [Fact]
    public async Task ImmediateAcquisition_StampsLeaseUntilOneLeaseDurationAhead()
    {
        var ticker = NewTimeTicker();
        await Provider.AddTimeTickers([ticker], CancellationToken.None);

        Assert.Single(await Provider.AcquireImmediateTimeTickersAsync([ticker.Id], CancellationToken.None));

        var persisted = await Provider.GetTimeTickerById(ticker.Id, CancellationToken.None);
        Assert.Equal(TickerStatus.InProgress, persisted!.Status);
        Assert.Equal(OwnerId, persisted.LockHolder);
        Assert.Equal(Now.Add(LeaseDuration), persisted.LeaseUntil);
    }

    // ---------------------------------------------------------------------
    // 4. Lease renewal count reflects the rows actually held.
    // ---------------------------------------------------------------------

    [Fact]
    public async Task RenewLeases_CountReflectsRowsActuallyHeldUnderCurrentGeneration()
    {
        var first = NewTimeTicker();
        var second = NewTimeTicker();
        await Provider.AddTimeTickers([first, second], CancellationToken.None);
        Assert.Equal(2, (await Provider.AcquireImmediateTimeTickersAsync([first.Id, second.Id], CancellationToken.None)).Length);

        var firstLease = await LeaseFor(first.Id);
        var secondLease = await LeaseFor(second.Id);
        var renewedUntil = Now.AddMinutes(2);

        var renewedBoth = await Provider.RenewTimeTickerLeases(
            [firstLease, secondLease], renewedUntil, CancellationToken.None);
        Assert.Equal(2, renewedBoth);
        Assert.Equal(renewedUntil, (await Provider.GetTimeTickerById(first.Id, CancellationToken.None))!.LeaseUntil);
        Assert.Equal(renewedUntil, (await Provider.GetTimeTickerById(second.Id, CancellationToken.None))!.LeaseUntil);

        // A batch mixing one genuinely-held lease with an unheld id renews only the held one.
        var laterUntil = Now.AddMinutes(3);
        var renewedPartial = await Provider.RenewTimeTickerLeases(
            [firstLease, new AcquisitionLease(Guid.NewGuid(), Guid.NewGuid())], laterUntil, CancellationToken.None);
        Assert.Equal(1, renewedPartial);

        // A stale generation for a still-held row renews nothing.
        var staleGeneration = await Provider.RenewTimeTickerLeases(
            [new AcquisitionLease(second.Id, Guid.NewGuid())], laterUntil, CancellationToken.None);
        Assert.Equal(0, staleGeneration);
        Assert.Equal(renewedUntil, (await Provider.GetTimeTickerById(second.Id, CancellationToken.None))!.LeaseUntil);
    }

    // ---------------------------------------------------------------------
    // 5. Ownership transfer rejects a stale terminal write.
    // ---------------------------------------------------------------------

    [Fact]
    public async Task TerminalWrite_UnderStaleGeneration_IsRejectedAfterOwnershipMoves()
    {
        var ticker = NewTimeTicker();
        await Provider.AddTimeTickers([ticker], CancellationToken.None);

        Assert.Single(await Provider.AcquireImmediateTimeTickersAsync([ticker.Id], CancellationToken.None));
        var firstGeneration = (await Provider.GetTimeTickerById(ticker.Id, CancellationToken.None))!.AcquisitionToken;
        Assert.NotNull(firstGeneration);

        // Ownership moves: the watchdog releases this node, then the row is re-acquired
        // under a fresh generation (same-node ABA — the hardest case to fence).
        await Provider.ReleaseDeadNodeTimeTickerResources(OwnerId, CancellationToken.None);
        Assert.Single(await Provider.AcquireImmediateTimeTickersAsync([ticker.Id], CancellationToken.None));
        var secondGeneration = (await Provider.GetTimeTickerById(ticker.Id, CancellationToken.None))!.AcquisitionToken;
        Assert.NotNull(secondGeneration);
        Assert.NotEqual(firstGeneration, secondGeneration);

        // A terminal completion carrying the stale generation must not land.
        var staleCompletion = new InternalFunctionContext { TickerId = ticker.Id, Type = TickerType.TimeTicker }
            .SetProperty(x => x.AcquisitionToken, firstGeneration)
            .SetProperty(x => x.Status, TickerStatus.Done)
            .SetProperty(x => x.ReleaseLock, true);
        var written = await Provider.UpdateTimeTicker(staleCompletion, CancellationToken.None);
        Assert.Equal(0, written);

        var persisted = await Provider.GetTimeTickerById(ticker.Id, CancellationToken.None);
        Assert.Equal(TickerStatus.InProgress, persisted!.Status);
        Assert.Equal(secondGeneration, persisted.AcquisitionToken);
    }

    // ---------------------------------------------------------------------
    // 6. Restart/cancel stale policy and restart cap — time tickers.
    // ---------------------------------------------------------------------

    [Fact]
    public async Task RecoverStaleTickers_TimeTickers_AppliesRestartCancelPolicyAndRestartCap()
    {
        const int maxStaleRestarts = 2;

        var restart = NewStaleInProgressTimeTicker(StaleAction.Restart, staleRestartCount: 0);
        var exhausted = NewStaleInProgressTimeTicker(StaleAction.Restart, staleRestartCount: maxStaleRestarts);
        var cancel = NewStaleInProgressTimeTicker(StaleAction.Cancel, staleRestartCount: 0);
        await Provider.AddTimeTickers([restart, exhausted, cancel], CancellationToken.None);

        var result = await Provider.RecoverStaleTickers(maxStaleRestarts, CancellationToken.None);

        Assert.Equal(1, result.RestartedTimeTickers);
        Assert.Equal(2, result.CancelledTimeTickers);

        var restarted = await Provider.GetTimeTickerById(restart.Id, CancellationToken.None);
        Assert.Equal(TickerStatus.Idle, restarted!.Status);
        Assert.Equal(1, restarted.StaleRestartCount);
        Assert.Null(restarted.LeaseUntil);
        Assert.Null(restarted.LockHolder);

        Assert.Equal(TickerStatus.Cancelled, (await Provider.GetTimeTickerById(exhausted.Id, CancellationToken.None))!.Status);
        Assert.Equal(TickerStatus.Cancelled, (await Provider.GetTimeTickerById(cancel.Id, CancellationToken.None))!.Status);
    }

    // ---------------------------------------------------------------------
    // 6b. Restart/cancel stale policy and restart cap — cron occurrences take
    //     their policy from the parent cron template.
    // ---------------------------------------------------------------------

    [Fact]
    public async Task RecoverStaleTickers_CronOccurrences_EnforceParentPolicyAndRestartCap()
    {
        const int maxStaleRestarts = 2;

        var restartCron = NewCron("restart-cron");
        restartCron.OnStale = StaleAction.Restart;
        var cancelCron = NewCron("cancel-cron");
        cancelCron.OnStale = StaleAction.Cancel;
        await Provider.InsertCronTickers([restartCron, cancelCron], CancellationToken.None);

        // Distinct execution times keep the unique (CronTickerId, ExecutionTime) slot index happy.
        var restart = NewStaleInProgressOccurrence(restartCron.Id, staleRestartCount: 0, executionTime: Now);
        var exhausted = NewStaleInProgressOccurrence(restartCron.Id, staleRestartCount: maxStaleRestarts, executionTime: Now.AddSeconds(1));
        var cancel = NewStaleInProgressOccurrence(cancelCron.Id, staleRestartCount: 0, executionTime: Now.AddSeconds(2));
        await Provider.InsertCronTickerOccurrences([restart, exhausted, cancel], CancellationToken.None);

        var result = await Provider.RecoverStaleTickers(maxStaleRestarts, CancellationToken.None);

        Assert.Equal(1, result.RestartedCronOccurrences);
        Assert.Equal(2, result.CancelledCronOccurrences);

        var rows = await Provider.GetAllCronTickerOccurrences(_ => true, CancellationToken.None);
        var restarted = rows.Single(x => x.Id == restart.Id);
        Assert.Equal(TickerStatus.Idle, restarted.Status);
        Assert.Equal(1, restarted.StaleRestartCount);
        Assert.Null(restarted.LeaseUntil);
        Assert.Equal(TickerStatus.Cancelled, rows.Single(x => x.Id == exhausted.Id).Status);
        Assert.Equal(TickerStatus.Cancelled, rows.Single(x => x.Id == cancel.Id).Status);
    }

    // ---------------------------------------------------------------------
    // 7. Lease ratio validation — the configured ratio plus the runtime headroom
    //    the provider actually stamps.
    // ---------------------------------------------------------------------

    [Fact]
    public async Task LeaseRatio_ConfiguredRatioAndStampedHeadroomGiveRenewalRoomToRecover()
    {
        // Design invariant: a lease must outlive several renewal cycles so a transient
        // DB hiccup or GC pause never lets a live job be treated as stale.
        Assert.True(
            Options.LeaseDuration >= Options.LeaseRenewalInterval + Options.LeaseRenewalInterval + Options.LeaseRenewalInterval,
            $"LeaseDuration ({Options.LeaseDuration}) must be at least 3x LeaseRenewalInterval ({Options.LeaseRenewalInterval}).");

        var ticker = NewTimeTicker();
        await Provider.AddTimeTickers([ticker], CancellationToken.None);
        Assert.Single(await Provider.AcquireImmediateTimeTickersAsync([ticker.Id], CancellationToken.None));

        var persisted = await Provider.GetTimeTickerById(ticker.Id, CancellationToken.None);
        Assert.NotNull(persisted!.LeaseUntil);
        var stampedHeadroom = persisted.LeaseUntil!.Value - Now;
        // The stamped lease must give at least two renewal cycles of headroom.
        Assert.True(
            stampedHeadroom >= Options.LeaseRenewalInterval + Options.LeaseRenewalInterval,
            $"Stamped lease headroom ({stampedHeadroom}) must cover at least 2 renewal intervals ({Options.LeaseRenewalInterval}).");
    }

    // ---------------------------------------------------------------------
    // 8. Cron parent hydration preserves function / retries / timeout.
    // ---------------------------------------------------------------------

    [Fact]
    public async Task ImmediateCronAcquisition_HydratesParentFunctionRetriesAndTimeout()
    {
        var cron = NewCron("hydrated-cron");
        cron.Retries = 5;
        cron.TimeoutSeconds = 17;
        await Provider.InsertCronTickers([cron], CancellationToken.None);

        var occurrence = NewOccurrence(cron.Id, status: TickerStatus.Queued, executionTime: Now);
        await Provider.InsertCronTickerOccurrences([occurrence], CancellationToken.None);

        var acquired = await Provider.AcquireImmediateCronOccurrencesAsync([occurrence.Id], CancellationToken.None);

        var result = Assert.Single(acquired);
        Assert.NotNull(result.CronTicker);
        Assert.Equal("hydrated-cron", result.CronTicker.Function);
        Assert.Equal(5, result.CronTicker.Retries);
        Assert.Equal(17, result.CronTicker.TimeoutSeconds);
    }

    // ---------------------------------------------------------------------
    // 9. Root → child → grandchild execution projection.
    // ---------------------------------------------------------------------

    [Fact]
    public async Task ImmediateAcquisition_ProjectsRootChildGrandchildChain()
    {
        var root = NewTimeTicker();
        var child = NewChildDefinition();
        var grandchild = NewChildDefinition();
        grandchild.TimeoutSeconds = 37;
        child.ParentId = root.Id;
        grandchild.ParentId = child.Id;
        child.Children = [grandchild];
        root.Children = [child];
        await Provider.AddTimeTickers([root], CancellationToken.None);

        var acquired = await Provider.AcquireImmediateTimeTickersAsync([root.Id], CancellationToken.None);

        var acquiredRoot = Assert.Single(acquired);
        var acquiredChild = Assert.Single(acquiredRoot.Children);
        Assert.Equal(child.Id, acquiredChild.Id);
        Assert.Equal(root.Id, acquiredChild.ParentId);
        var acquiredGrandchild = Assert.Single(acquiredChild.Children);
        Assert.Equal(grandchild.Id, acquiredGrandchild.Id);
        Assert.Equal(child.Id, acquiredGrandchild.ParentId);
        Assert.Equal(37, acquiredGrandchild.TimeoutSeconds);
    }

    // ---------------------------------------------------------------------
    // 10. Release clears ownership and generation.
    // ---------------------------------------------------------------------

    [Fact]
    public async Task ReleaseAcquiredTimeTickers_ClearsOwnershipAndGeneration()
    {
        var ticker = NewTimeTicker();
        await Provider.AddTimeTickers([ticker], CancellationToken.None);

        // Release targets the short Queued handoff lock (the state a scheduler releases when it
        // backs out before executing); it deliberately does not touch live InProgress rows.
        await QueueForOwnership(ticker.Id);
        var queued = await Provider.GetTimeTickerById(ticker.Id, CancellationToken.None);
        Assert.Equal(TickerStatus.Queued, queued!.Status);
        Assert.Equal(OwnerId, queued.LockHolder);

        await Provider.ReleaseAcquiredTimeTickers([ticker.Id], CancellationToken.None);

        var persisted = await Provider.GetTimeTickerById(ticker.Id, CancellationToken.None);
        Assert.Equal(TickerStatus.Idle, persisted!.Status);
        Assert.Null(persisted.LockHolder);
        Assert.Null(persisted.LockedAt);
        Assert.Null(persisted.LeaseUntil);
        Assert.Null(persisted.AcquisitionToken);
    }

    // ---------------------------------------------------------------------
    // Capability — reliability providers must advertise lease-based recovery.
    // ---------------------------------------------------------------------

    [Fact]
    public void Provider_AdvertisesLeaseBasedRecovery()
        => Assert.True(Provider.SupportsLeaseBasedRecovery);

    // =====================================================================
    // Builders — construct entities through public/internal setters that are
    // visible to both linked test assemblies via InternalsVisibleTo.
    // =====================================================================

    private TimeTickerEntity NewTimeTicker(
        Guid? id = null,
        DateTime? executionTime = null,
        TickerStatus status = TickerStatus.Idle,
        string function = "contract-fn")
        => new()
        {
            Id = id ?? Guid.NewGuid(),
            Function = function,
            Request = Array.Empty<byte>(),
            ExecutionTime = executionTime ?? Now.AddSeconds(1),
            Status = status,
            CreatedAt = Now,
            UpdatedAt = Now,
        };

    private TimeTickerEntity NewChildDefinition()
        => new()
        {
            Id = Guid.NewGuid(),
            Function = "child-fn",
            Request = Array.Empty<byte>(),
            ExecutionTime = null, // chain-child definitions carry no ExecutionTime
            Status = TickerStatus.Idle,
            CreatedAt = Now,
            UpdatedAt = Now,
        };

    private TimeTickerEntity NewStaleInProgressTimeTicker(StaleAction onStale, int staleRestartCount)
    {
        var ticker = NewTimeTicker(status: TickerStatus.InProgress);
        ticker.OnStale = onStale;
        ticker.StaleRestartCount = staleRestartCount;
        ticker.LockHolder = "dead-node";
        ticker.LockedAt = Now.AddMinutes(-2);
        ticker.LeaseUntil = Now.AddSeconds(-1); // expired lease → stale
        return ticker;
    }

    private CronTickerEntity NewCron(string function = "cron-fn", string expression = "* * * * *")
        => new()
        {
            Id = Guid.NewGuid(),
            Function = function,
            Expression = expression,
            Request = Array.Empty<byte>(),
            IsEnabled = true,
        };

    private CronTickerOccurrenceEntity<CronTickerEntity> NewOccurrence(
        Guid cronTickerId,
        TickerStatus status = TickerStatus.Idle,
        DateTime? executionTime = null)
        => new()
        {
            Id = Guid.NewGuid(),
            CronTickerId = cronTickerId,
            ExecutionTime = executionTime ?? Now.AddSeconds(1),
            Status = status,
            CreatedAt = Now,
            UpdatedAt = Now,
        };

    private CronTickerOccurrenceEntity<CronTickerEntity> NewStaleInProgressOccurrence(
        Guid cronTickerId, int staleRestartCount, DateTime executionTime)
    {
        var occurrence = NewOccurrence(cronTickerId, status: TickerStatus.InProgress, executionTime: executionTime);
        occurrence.StaleRestartCount = staleRestartCount;
        occurrence.LeaseUntil = Now.AddSeconds(-1); // expired lease → stale
        return occurrence;
    }

    private async Task QueueForOwnership(Guid timeTickerId)
    {
        var fresh = await Provider.GetTimeTickerById(timeTickerId, CancellationToken.None);
        Assert.NotNull(fresh);
        var input = new TimeTickerEntity { Id = fresh!.Id, UpdatedAt = fresh.UpdatedAt };
        var queued = new List<TimeTickerEntity>();
        await foreach (var t in Provider.QueueTimeTickers([input], CancellationToken.None))
            queued.Add(t);
        Assert.Single(queued);
    }

    private async Task<AcquisitionLease> LeaseFor(Guid timeTickerId)
    {
        var ticker = await Provider.GetTimeTickerById(timeTickerId, CancellationToken.None);
        Assert.NotNull(ticker);
        Assert.NotNull(ticker!.AcquisitionToken);
        return new AcquisitionLease(timeTickerId, ticker.AcquisitionToken);
    }
}
