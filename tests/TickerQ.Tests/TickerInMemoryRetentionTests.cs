using System;
using System.Collections.Generic;
using System.Linq;
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

// Uses dedicated entity types so the generic in-memory provider's STATIC store is isolated
// from every other test class. Methods within this class run sequentially; the constructor
// clears the store so each test starts empty and counts/HasMore are deterministic.
public class TickerInMemoryRetentionTests
{
    private sealed class RetTime : TimeTickerEntity<RetTime> { }
    private sealed class RetCron : CronTickerEntity { }

    private readonly DateTime _now = new(2026, 07, 28, 12, 0, 0, DateTimeKind.Utc);
    private readonly TickerInMemoryPersistenceProvider<RetTime, RetCron> _provider;

    public TickerInMemoryRetentionTests()
    {
        var clock = Substitute.For<ITickerClock>();
        clock.UtcNow.Returns(_now);
        var services = new ServiceCollection();
        services.AddSingleton(clock);
        services.AddSingleton(new SchedulerOptionsBuilder { NodeIdentifier = "retention-node" });
        _provider = new TickerInMemoryPersistenceProvider<RetTime, RetCron>(services.BuildServiceProvider());
        ClearAll().GetAwaiter().GetResult();
    }

    private async Task ClearAll()
    {
        var all = await _provider.GetTimeTickers(_ => true, CancellationToken.None);
        if (all.Length > 0)
            await _provider.RemoveTimeTickers(all.Select(t => t.Id).ToArray(), CancellationToken.None);
        var occ = await _provider.GetAllCronTickerOccurrences(_ => true, CancellationToken.None);
        if (occ.Length > 0)
            await _provider.RemoveCronTickerOccurrences(occ.Select(o => o.Id).ToArray(), CancellationToken.None);
    }

    private DateTime Ago(double days) => _now - TimeSpan.FromDays(days);

    private static RetentionCutoffs Cutoffs(
        DateTime? succeeded = null, DateTime? failed = null, DateTime? cancelled = null, DateTime? skipped = null)
        => new(succeeded, failed, cancelled, skipped);

    private Task<RetentionChainBatchResult> Prune(RetentionCutoffs c, int batch, RetentionCursor cursor = default)
        => _provider.DeleteEligibleTimeTickerChainsAsync(c, batch, cursor, CancellationToken.None);

    private RetTime Node(
        TickerStatus status, DateTime? executedAt, Guid? parentId = null,
        string lockHolder = null, Guid? acquisitionToken = null, DateTime? leaseUntil = null)
        => new()
        {
            Id = Guid.NewGuid(),
            Function = "F",
            ExecutionTime = _now.AddMinutes(-30),
            Status = status,
            ExecutedAt = executedAt,
            ParentId = parentId,
            LockHolder = lockHolder,
            AcquisitionToken = acquisitionToken,
            LeaseUntil = leaseUntil,
            CreatedAt = _now.AddDays(-40),
            UpdatedAt = _now.AddDays(-40)
        };

    private async Task Insert(RetTime root) => await _provider.AddTimeTickers(new[] { root }, CancellationToken.None);

    private async Task<bool> Exists(Guid id) => await _provider.GetTimeTickerById(id, CancellationToken.None) != null;

    [Fact]
    public void SupportsRetention_IsTrue()
    {
        Assert.True(_provider.SupportsRetention);
    }

    [Fact]
    public async Task Deletes_Done_And_DueDone_UnderSucceededWindow()
    {
        var done = Node(TickerStatus.Done, Ago(10));
        var dueDone = Node(TickerStatus.DueDone, Ago(10));
        await Insert(done);
        await Insert(dueDone);

        var result = await Prune(Cutoffs(succeeded: Ago(7)), 100);

        Assert.Equal(2, result.Deleted);
        Assert.False(await Exists(done.Id));
        Assert.False(await Exists(dueDone.Id));
    }

    [Fact]
    public async Task Retains_Status_WithNullWindow()
    {
        var failed = Node(TickerStatus.Failed, Ago(100));
        await Insert(failed);

        // Only succeeded window configured; failed has null policy → retained.
        var result = await Prune(Cutoffs(succeeded: Ago(7)), 100);

        Assert.Equal(0, result.Deleted);
        Assert.True(await Exists(failed.Id));
    }

    [Fact]
    public async Task Deletes_Failed_Cancelled_Skipped_PerOwnWindow()
    {
        var failed = Node(TickerStatus.Failed, Ago(10));
        var cancelled = Node(TickerStatus.Cancelled, Ago(10));
        var skipped = Node(TickerStatus.Skipped, Ago(10));
        await Insert(failed); await Insert(cancelled); await Insert(skipped);

        var result = await Prune(Cutoffs(failed: Ago(7), cancelled: Ago(7), skipped: Ago(7)), 100);

        Assert.Equal(3, result.Deleted);
    }

    [Fact]
    public async Task ExactCutoffBoundary_IsRetained_StrictlyOlderIsDeleted()
    {
        var cutoff = Ago(7);
        var exactlyAtCutoff = Node(TickerStatus.Done, cutoff);
        var oneTickOlder = Node(TickerStatus.Done, cutoff.AddTicks(-1));
        await Insert(exactlyAtCutoff); await Insert(oneTickOlder);

        var result = await Prune(Cutoffs(succeeded: cutoff), 100);

        Assert.Equal(1, result.Deleted);
        Assert.True(await Exists(exactlyAtCutoff.Id));
        Assert.False(await Exists(oneTickOlder.Id));
    }

    [Fact]
    public async Task NullExecutedAt_IsRetained()
    {
        var noTimestamp = Node(TickerStatus.Cancelled, executedAt: null);
        await Insert(noTimestamp);

        var result = await Prune(Cutoffs(cancelled: Ago(1)), 100);

        Assert.Equal(0, result.Deleted);
        Assert.True(await Exists(noTimestamp.Id));
    }

    [Fact]
    public async Task Owned_Or_LiveLeased_AreRetained_StaleLockHolderIsNot()
    {
        var owned = Node(TickerStatus.Done, Ago(10), acquisitionToken: Guid.NewGuid());
        var liveLeased = Node(TickerStatus.Done, Ago(10), leaseUntil: _now.AddMinutes(30));
        var staleLockHolderOnly = Node(TickerStatus.Done, Ago(10), lockHolder: "node-that-ran-it");
        var eligible = Node(TickerStatus.Done, Ago(10));
        await Insert(owned); await Insert(liveLeased); await Insert(staleLockHolderOnly); await Insert(eligible);

        var result = await Prune(Cutoffs(succeeded: Ago(7)), 100);

        Assert.Equal(2, result.Deleted);
        Assert.True(await Exists(owned.Id));
        Assert.True(await Exists(liveLeased.Id));
        Assert.False(await Exists(staleLockHolderOnly.Id));
        Assert.False(await Exists(eligible.Id));
    }

    [Fact]
    public async Task ExpiredLease_IsEligible()
    {
        var expiredLease = Node(TickerStatus.Done, Ago(10), leaseUntil: _now.AddMinutes(-30));
        await Insert(expiredLease);

        var result = await Prune(Cutoffs(succeeded: Ago(7)), 100);

        Assert.Equal(1, result.Deleted);
        Assert.False(await Exists(expiredLease.Id));
    }

    [Fact]
    public async Task WholeChain_4Levels_AllEligible_DeletedEntirely()
    {
        var root = Node(TickerStatus.Done, Ago(10));
        var child = Node(TickerStatus.DueDone, Ago(10), parentId: root.Id);
        var grand = Node(TickerStatus.Failed, Ago(10), parentId: child.Id);
        var great = Node(TickerStatus.Skipped, Ago(10), parentId: grand.Id);
        grand.Children.Add(great); child.Children.Add(grand); root.Children.Add(child);
        await Insert(root);

        var result = await Prune(Cutoffs(succeeded: Ago(7), failed: Ago(7), skipped: Ago(7)), 100);

        Assert.Equal(4, result.Deleted);
        foreach (var id in new[] { root.Id, child.Id, grand.Id, great.Id })
            Assert.False(await Exists(id));
    }

    [Fact]
    public async Task WholeChain_RetainedEntirely_WhenAnyNodeIneligible()
    {
        var root = Node(TickerStatus.Done, Ago(10));
        var child = Node(TickerStatus.Done, Ago(10), parentId: root.Id);
        var grand = Node(TickerStatus.Done, Ago(10), parentId: child.Id);
        var great = Node(TickerStatus.Done, Ago(1), parentId: grand.Id); // too recent
        grand.Children.Add(great); child.Children.Add(grand); root.Children.Add(child);
        await Insert(root);

        var result = await Prune(Cutoffs(succeeded: Ago(7)), 100);

        Assert.Equal(0, result.Deleted);
        foreach (var id in new[] { root.Id, child.Id, grand.Id, great.Id })
            Assert.True(await Exists(id));
    }

    [Fact]
    public async Task WholeChain_RetainedEntirely_WhenAnyNodeStatusHasNullWindow()
    {
        var root = Node(TickerStatus.Done, Ago(10));
        var child = Node(TickerStatus.Failed, Ago(10), parentId: root.Id); // failed window not configured
        root.Children.Add(child);
        await Insert(root);

        var result = await Prune(Cutoffs(succeeded: Ago(7)), 100);

        Assert.Equal(0, result.Deleted);
        Assert.True(await Exists(root.Id));
        Assert.True(await Exists(child.Id));
    }

    [Fact]
    public async Task Batch_CapsChainsPerCall_AndReportsHasMore_ThreadingCursor()
    {
        for (var i = 0; i < 5; i++)
            await Insert(Node(TickerStatus.Done, Ago(10 + i))); // distinct ExecutedAt for a total keyset order

        var first = await Prune(Cutoffs(succeeded: Ago(7)), 2, RetentionCursor.Start);
        Assert.Equal(2, first.Deleted);
        Assert.True(first.HasMore);

        var second = await Prune(Cutoffs(succeeded: Ago(7)), 2, first.NextCursor);
        Assert.Equal(2, second.Deleted);
        Assert.True(second.HasMore);

        var third = await Prune(Cutoffs(succeeded: Ago(7)), 2, second.NextCursor);
        Assert.Equal(1, third.Deleted);
        Assert.False(third.HasMore);
    }

    // ---- Starvation regression: batchSize 1, oldest root blocked, later root fully eligible ----
    [Fact]
    public async Task BlockedOldestRoot_DoesNotStarve_LaterEligibleRoot()
    {
        // R1: oldest ExecutedAt, but has an ineligible (too-recent) child → chain blocked.
        var r1Root = Node(TickerStatus.Done, Ago(20));
        var r1Child = Node(TickerStatus.Done, Ago(1), parentId: r1Root.Id); // too recent
        r1Root.Children.Add(r1Child);
        await Insert(r1Root);

        // R2: newer root, fully eligible single-node chain.
        var r2 = Node(TickerStatus.Done, Ago(10));
        await Insert(r2);

        // batchSize 1: the first call examines only the oldest root (R1) and must skip it (deletes nothing)
        // while advancing the cursor; the second call must reach and delete R2 — never touching R1.
        var call1 = await Prune(Cutoffs(succeeded: Ago(7)), 1, RetentionCursor.Start);
        Assert.Equal(0, call1.Deleted);
        Assert.True(call1.HasMore);

        var call2 = await Prune(Cutoffs(succeeded: Ago(7)), 1, call1.NextCursor);
        Assert.Equal(1, call2.Deleted);

        Assert.True(await Exists(r1Root.Id));   // blocked chain retained whole
        Assert.True(await Exists(r1Child.Id));
        Assert.False(await Exists(r2.Id));       // later eligible root deleted
    }

    // ---- Cursor wrap: reaching the end resets to Start; a newly-eligible record is reconsidered ----
    [Fact]
    public async Task CursorWraps_AtEnd_AndReconsidersNewlyEligibleRecords()
    {
        // Two roots: R1 (oldest, eligible), R2 (newer, blocked by a too-recent child).
        var r1 = Node(TickerStatus.Done, Ago(20));
        await Insert(r1);
        var r2Root = Node(TickerStatus.Done, Ago(10));
        var r2Child = Node(TickerStatus.Done, Ago(1), parentId: r2Root.Id);
        r2Root.Children.Add(r2Child);
        await Insert(r2Root);

        // Pass 1: delete R1, advance cursor.
        var p1 = await Prune(Cutoffs(succeeded: Ago(7)), 1, RetentionCursor.Start);
        Assert.Equal(1, p1.Deleted);
        Assert.True(p1.HasMore);

        // Pass 2: examine R2 (blocked) → delete nothing; end of traversal → cursor wraps to Start.
        var p2 = await Prune(Cutoffs(succeeded: Ago(7)), 1, p1.NextCursor);
        Assert.Equal(0, p2.Deleted);
        Assert.False(p2.HasMore);
        Assert.False(p2.NextCursor.HasValue); // wrapped to Start

        // R2's child ages into eligibility.
        var child = await _provider.GetTimeTickerById(r2Child.Id, CancellationToken.None);
        child.ExecutedAt = Ago(9);
        await _provider.UpdateTimeTickers(new[] { child }, CancellationToken.None);

        // Pass 3 from the wrapped Start cursor: R2 is now fully eligible and gets deleted.
        var p3 = await Prune(Cutoffs(succeeded: Ago(7)), 1, p2.NextCursor);
        Assert.Equal(2, p3.Deleted);
        Assert.False(await Exists(r2Root.Id));
        Assert.False(await Exists(r2Child.Id));
    }

    [Fact]
    public async Task OversizedChain_IsRetainedWhole()
    {
        var root = Node(TickerStatus.Done, Ago(10));
        var child = Node(TickerStatus.Done, Ago(10), root.Id);
        var grandchild = Node(TickerStatus.Done, Ago(10), child.Id);
        var greatGrandchild = Node(TickerStatus.Done, Ago(10), grandchild.Id);
        grandchild.Children.Add(greatGrandchild);
        child.Children.Add(grandchild);
        root.Children.Add(child);
        await Insert(root);

        var result = await Prune(new RetentionCutoffs(Ago(7), null, null, null, maxNodesPerChain: 3), 100);

        Assert.Equal(0, result.Deleted);
        foreach (var id in new[] { root.Id, child.Id, grandchild.Id, greatGrandchild.Id })
            Assert.True(await Exists(id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentReactivation_CompletesBeforeRetention_AndPreservesWholeChain(bool reactivateDeepChild)
    {
        var root = Node(TickerStatus.Done, Ago(10));
        var child = Node(TickerStatus.Done, Ago(10), root.Id);
        var grandchild = Node(TickerStatus.Done, Ago(10), child.Id);
        var generation = Guid.NewGuid();
        root.ChainRootId = root.Id;
        root.ChainGeneration = generation;
        child.Children.Add(grandchild);
        root.Children.Add(child);
        await Insert(root);

        var target = reactivateDeepChild ? grandchild : root;
        var context = new InternalFunctionContext
        {
            TickerId = target.Id,
            ParentId = target.ParentId,
            ChainRootId = root.Id,
            ChainGeneration = generation,
            Type = TickerType.TimeTicker
        };
        context.SetProperty(c => c.Status, TickerStatus.InProgress);

        using var mutationEntered = new ManualResetEventSlim();
        using var allowMutation = new ManualResetEventSlim();
        TickerInMemoryPersistenceProvider<RetTime, RetCron>.EligibilityMutationLockHook = id =>
        {
            if (id != target.Id) return;
            mutationEntered.Set();
            Assert.True(allowMutation.Wait(TimeSpan.FromSeconds(5)));
        };

        try
        {
            var mutation = Task.Run(() => _provider.UpdateTimeTicker(context));
            Assert.True(mutationEntered.Wait(TimeSpan.FromSeconds(5)));
            var retention = Task.Run(() => Prune(Cutoffs(succeeded: Ago(7)), 100));
            await Task.Delay(50);
            Assert.False(retention.IsCompleted);

            allowMutation.Set();
            Assert.Equal(1, await mutation);
            var result = await retention;

            Assert.Equal(0, result.Deleted);
            foreach (var id in new[] { root.Id, child.Id, grandchild.Id })
                Assert.True(await Exists(id));
            var persistedTarget = await _provider.GetTimeTickerById(target.Id, CancellationToken.None);
            Assert.Equal(TickerStatus.InProgress, persistedTarget.Status);
        }
        finally
        {
            allowMutation.Set();
            TickerInMemoryPersistenceProvider<RetTime, RetCron>.EligibilityMutationLockHook = null;
        }
    }

    // ----- Cron occurrences -----

    private int _occurrenceSeq;

    private CronTickerOccurrenceEntity<RetCron> Occurrence(
        Guid cronTickerId, TickerStatus status, DateTime? executedAt,
        string lockHolder = null, Guid? acquisitionToken = null, DateTime? leaseUntil = null)
        => new()
        {
            Id = Guid.NewGuid(),
            CronTickerId = cronTickerId,
            Status = status,
            ExecutedAt = executedAt,
            // Distinct execution times: the provider enforces a unique (ExecutionTime, CronTickerId) index.
            ExecutionTime = _now.AddMinutes(-30).AddSeconds(-(++_occurrenceSeq)),
            LockHolder = lockHolder,
            AcquisitionToken = acquisitionToken,
            LeaseUntil = leaseUntil,
            CreatedAt = _now.AddDays(-40),
            UpdatedAt = _now.AddDays(-40)
        };

    [Fact]
    public async Task CronOccurrences_DeletedByWindow_DefinitionsNeverDeleted()
    {
        var cron = new RetCron { Id = Guid.NewGuid(), Function = "F", Expression = "* * * * *" };
        await _provider.InsertCronTickers(new[] { cron }, CancellationToken.None);

        var oldDone = Occurrence(cron.Id, TickerStatus.Done, Ago(10));
        var recentDone = Occurrence(cron.Id, TickerStatus.Done, Ago(1)); // too recent → retained
        var ownedDone = Occurrence(cron.Id, TickerStatus.Done, Ago(10), acquisitionToken: Guid.NewGuid()); // live generation → retained
        await _provider.InsertCronTickerOccurrences(
            new[] { oldDone, recentDone, ownedDone }, CancellationToken.None);

        var result = await _provider.DeleteEligibleCronTickerOccurrencesAsync(Cutoffs(succeeded: Ago(7)), 100, CancellationToken.None);

        Assert.Equal(1, result.Deleted);
        Assert.NotNull(await _provider.GetCronTickerById(cron.Id, CancellationToken.None));
        var remaining = await _provider.GetAllCronTickerOccurrences(o => o.CronTickerId == cron.Id, CancellationToken.None);
        Assert.Equal(2, remaining.Length);
        Assert.DoesNotContain(remaining, o => o.Id == oldDone.Id);
    }

    [Fact]
    public async Task ConcurrentCronReactivation_CompletesBeforeRetention_AndIsPreserved()
    {
        var cron = new RetCron { Id = Guid.NewGuid(), Function = "F", Expression = "* * * * *" };
        await _provider.InsertCronTickers(new[] { cron }, CancellationToken.None);
        var occurrence = Occurrence(cron.Id, TickerStatus.Done, Ago(10));
        await _provider.InsertCronTickerOccurrences(new[] { occurrence }, CancellationToken.None);

        var context = new InternalFunctionContext
        {
            TickerId = occurrence.Id,
            Type = TickerType.CronTickerOccurrence
        };
        context.SetProperty(c => c.Status, TickerStatus.InProgress);

        using var mutationEntered = new ManualResetEventSlim();
        using var allowMutation = new ManualResetEventSlim();
        TickerInMemoryPersistenceProvider<RetTime, RetCron>.EligibilityMutationLockHook = id =>
        {
            if (id != occurrence.Id) return;
            mutationEntered.Set();
            Assert.True(allowMutation.Wait(TimeSpan.FromSeconds(5)));
        };

        try
        {
            var mutation = Task.Run(() => _provider.UpdateCronTickerOccurrence(context));
            Assert.True(mutationEntered.Wait(TimeSpan.FromSeconds(5)));
            var retention = Task.Run(() => _provider.DeleteEligibleCronTickerOccurrencesAsync(
                Cutoffs(succeeded: Ago(7)), 100, CancellationToken.None));
            await Task.Delay(50);
            Assert.False(retention.IsCompleted);

            allowMutation.Set();
            await mutation;
            var result = await retention;

            Assert.Equal(0, result.Deleted);
            var persisted = (await _provider.GetAllCronTickerOccurrences(
                o => o.Id == occurrence.Id, CancellationToken.None)).SingleOrDefault();
            Assert.NotNull(persisted);
            Assert.Equal(TickerStatus.InProgress, persisted.Status);
        }
        finally
        {
            allowMutation.Set();
            TickerInMemoryPersistenceProvider<RetTime, RetCron>.EligibilityMutationLockHook = null;
        }
    }

    [Fact]
    public async Task CronOccurrences_Batch_CapsAndReportsHasMore()
    {
        var cron = new RetCron { Id = Guid.NewGuid(), Function = "F", Expression = "* * * * *" };
        await _provider.InsertCronTickers(new[] { cron }, CancellationToken.None);
        var occ = new List<CronTickerOccurrenceEntity<RetCron>>();
        for (var i = 0; i < 3; i++) occ.Add(Occurrence(cron.Id, TickerStatus.Failed, Ago(10)));
        await _provider.InsertCronTickerOccurrences(occ.ToArray(), CancellationToken.None);

        var first = await _provider.DeleteEligibleCronTickerOccurrencesAsync(Cutoffs(failed: Ago(7)), batchSize: 2, CancellationToken.None);
        Assert.Equal(2, first.Deleted);
        Assert.True(first.HasMore);

        var second = await _provider.DeleteEligibleCronTickerOccurrencesAsync(Cutoffs(failed: Ago(7)), batchSize: 2, CancellationToken.None);
        Assert.Equal(1, second.Deleted);
        Assert.False(second.HasMore);
    }

    // A near-10k-deep single chain must be traversed without overflowing the call stack.
    // A recursive subtree walk overflows here (each level is a frame); an explicit
    // stack/queue traversal collects and deletes the whole chain.
    [Fact]
    public async Task DeepChain_NearTenThousand_IsCollectedWithoutStackOverflow()
    {
        const int depth = 9_999;
        var nodes = new List<RetTime>(depth + 1);
        var root = Node(TickerStatus.Done, Ago(10));
        nodes.Add(root);
        var parentId = root.Id;
        for (var i = 0; i < depth; i++)
        {
            var child = Node(TickerStatus.Done, Ago(10), parentId: parentId);
            nodes.Add(child);
            parentId = child.Id;
        }
        await _provider.AddTimeTickers(nodes.ToArray(), CancellationToken.None);

        var cutoffs = new RetentionCutoffs(Ago(7), null, null, null, maxNodesPerChain: depth + 10);
        var result = await Prune(cutoffs, batch: 10);

        Assert.Equal(depth + 1, result.Deleted);
        Assert.False(await Exists(root.Id));
    }

    // A chain strictly larger than MaxNodesPerChain must fail closed: the entire chain is
    // retained (never a partial delete) once the bound is exceeded.
    [Fact]
    public async Task Chain_LargerThanMaxNodesPerChain_IsRetainedIntact()
    {
        var root = Node(TickerStatus.Done, Ago(10));
        var nodes = new List<RetTime> { root };
        var parentId = root.Id;
        for (var i = 0; i < 9; i++)
        {
            var child = Node(TickerStatus.Done, Ago(10), parentId: parentId);
            nodes.Add(child);
            parentId = child.Id;
        }
        await _provider.AddTimeTickers(nodes.ToArray(), CancellationToken.None);

        var cutoffs = new RetentionCutoffs(Ago(7), null, null, null, maxNodesPerChain: 5);
        var result = await Prune(cutoffs, batch: 10);

        Assert.Equal(0, result.Deleted);
        foreach (var n in nodes)
            Assert.True(await Exists(n.Id));
    }

    [Fact]
    public async Task DeepChain_CancelledTraversal_ThrowsAndRetainsEveryNode()
    {
        var root = Node(TickerStatus.Done, Ago(10));
        var nodes = new List<RetTime> { root };
        var parentId = root.Id;
        for (var i = 0; i < 1_000; i++)
        {
            var child = Node(TickerStatus.Done, Ago(10), parentId: parentId);
            nodes.Add(child);
            parentId = child.Id;
        }
        await _provider.AddTimeTickers(nodes.ToArray(), CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _provider.DeleteEligibleTimeTickerChainsAsync(
                new RetentionCutoffs(Ago(7), null, null, null, maxNodesPerChain: 2_000),
                10, RetentionCursor.Start, cancellation.Token));

        foreach (var node in nodes)
            Assert.True(await Exists(node.Id));
    }
}
