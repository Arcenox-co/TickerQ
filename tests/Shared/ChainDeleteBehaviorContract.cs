using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;
using Xunit;

namespace TickerQ.Tests.Shared;

public abstract class ChainDeleteBehaviorContract<TTime, TCron>
    where TTime : TimeTickerEntity<TTime>, new()
    where TCron : CronTickerEntity, new()
{
    protected abstract ITickerPersistenceProvider<TTime, TCron> Provider { get; }
    protected abstract DateTime Now { get; }

    protected TTime Node(ChainDeleteBehavior behavior = ChainDeleteBehavior.Cascade)
    {
        var node = new TTime { Id = Guid.NewGuid(), Function = "PolicyTest", Request = Array.Empty<byte>(), ExecutionTime = Now.AddDays(-20), DeleteBehavior = behavior };
        Set(node, nameof(node.Status), TickerStatus.Done);
        Set(node, nameof(node.ExecutedAt), Now.AddDays(-20));
        Set(node, nameof(node.CreatedAt), Now.AddDays(-30));
        Set(node, nameof(node.UpdatedAt), Now.AddDays(-20));
        return node;
    }

    protected static void Set(TTime node, string property, object value) => typeof(TTime).GetProperty(property)!.SetValue(node, value);
    protected virtual async Task SetDescendantStatusAsync(Guid rootId, Guid id, TickerStatus status)
    {
        var root = await Provider.GetTimeTickerById(rootId);
        var pending = new Stack<TTime>(root.Children);
        while (pending.Count > 0)
        {
            var node = pending.Pop();
            if (node.Id == id)
            {
                Set(node, nameof(node.Status), status);
                Assert.True(await Provider.UpdateTimeTickers([root]) > 0);
                return;
            }
            foreach (var child in node.Children) pending.Push(child);
        }
        Assert.Fail("Test descendant not found.");
    }
    protected TTime Chain(ChainDeleteBehavior behavior)
    {
        var root = Node(behavior);
        var child = Node();
        child.Children.Add(Node());
        root.Children.Add(child);
        return root;
    }

    [Theory]
    [InlineData(ChainDeleteBehavior.Cascade)]
    [InlineData(ChainDeleteBehavior.Restrict)]
    public async Task Explicit_delete_uses_root_policy_for_the_whole_tree(ChainDeleteBehavior behavior)
    {
        var root = Chain(behavior);
        Set(Assert.Single(Assert.Single(root.Children).Children), nameof(root.Status), TickerStatus.Idle);
        await Provider.AddTimeTickers([root]);
        var saved = await Provider.GetTimeTickerById(root.Id);
        Assert.Equal(behavior, saved.DeleteBehavior);
        var removed = await Provider.RemoveTimeTickers([root.Id]);
        if (behavior == ChainDeleteBehavior.Cascade)
        {
            Assert.True(removed > 0);
            Assert.Null(await Provider.GetTimeTickerById(root.Id));
        }
        else
        {
            Assert.Equal(0, removed);
            saved = await Provider.GetTimeTickerById(root.Id);
            Assert.NotNull(saved);
            Assert.Single(saved.Children);
            Assert.Single(Assert.Single(saved.Children).Children);
        }
    }

    [Theory]
    [InlineData(TickerStatus.Idle)]
    [InlineData(TickerStatus.Queued)]
    [InlineData(TickerStatus.InProgress)]
    [InlineData((TickerStatus)99)]
    public async Task Restrict_lifts_after_the_last_unfinished_descendant_finishes(TickerStatus pendingStatus)
    {
        var root = Chain(ChainDeleteBehavior.Restrict);
        var grandchild = Assert.Single(Assert.Single(root.Children).Children);
        Set(grandchild, nameof(root.Status), pendingStatus);
        await Provider.AddTimeTickers([root]);
        Assert.Equal(0, await Provider.RemoveTimeTickers([root.Id]));
        var saved = await Provider.GetTimeTickerById(root.Id);
        Assert.NotNull(saved);
        grandchild = Assert.Single(Assert.Single(saved.Children).Children);
        Assert.Equal(pendingStatus, grandchild.Status);
        await SetDescendantStatusAsync(root.Id, grandchild.Id, TickerStatus.Done);
        Assert.Equal(ChainDeleteBehavior.Restrict, (await Provider.GetTimeTickerById(root.Id)).DeleteBehavior);
        Assert.True(await Provider.RemoveTimeTickers([root.Id]) > 0);
        Assert.Null(await Provider.GetTimeTickerById(root.Id));
    }

    [Theory]
    [InlineData(TickerStatus.Done)]
    [InlineData(TickerStatus.DueDone)]
    [InlineData(TickerStatus.Failed)]
    [InlineData(TickerStatus.Cancelled)]
    [InlineData(TickerStatus.Skipped)]
    public async Task Restrict_accepts_every_terminal_descendant_status(TickerStatus status)
    {
        var root = Chain(ChainDeleteBehavior.Restrict);
        Set(Assert.Single(root.Children), nameof(root.Status), status);
        Set(Assert.Single(Assert.Single(root.Children).Children), nameof(root.Status), status);
        await Provider.AddTimeTickers([root]);
        Assert.True(await Provider.RemoveTimeTickers([root.Id]) > 0);
        Assert.Null(await Provider.GetTimeTickerById(root.Id));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Terminal_descendants_with_ownership_are_still_protected(bool hasToken)
    {
        var root = Chain(ChainDeleteBehavior.Restrict);
        var grandchild = Assert.Single(Assert.Single(root.Children).Children);
        if (hasToken) grandchild.AcquisitionToken = Guid.NewGuid();
        else grandchild.LeaseUntil = Now.AddMinutes(1);
        await Provider.AddTimeTickers([root]);
        Assert.Equal(0, await Provider.RemoveTimeTickers([root.Id]));
        Assert.NotNull(await Provider.GetTimeTickerById(root.Id));
    }

    [Fact]
    public async Task Policy_can_change_on_completed_root_without_changing_execution_or_children()
    {
        var root = Chain(ChainDeleteBehavior.Restrict);
        root.Description = "Keep history";
        root.Request = [1, 2, 3];
        await Provider.AddTimeTickers([root]);
        var before = await Provider.GetTimeTickerById(root.Id);
        Assert.Equal(1, await Provider.SetTimeTickerDeleteBehaviorAsync(root.Id, ChainDeleteBehavior.Cascade));
        var after = await Provider.GetTimeTickerById(root.Id);
        Assert.Equal(ChainDeleteBehavior.Cascade, after.DeleteBehavior);
        Assert.Equal(before.Status, after.Status);
        Assert.Equal(before.ExecutedAt, after.ExecutedAt);
        Assert.Equal(before.ExecutionTime, after.ExecutionTime);
        Assert.Equal(before.ChainGeneration, after.ChainGeneration);
        Assert.Equal(before.Request, after.Request);
        Assert.Equal(before.Description, after.Description);
        Assert.Equal(Assert.Single(before.Children).Id, Assert.Single(after.Children).Id);
        Assert.True(await Provider.RemoveTimeTickers([root.Id]) > 0);
        Assert.Null(await Provider.GetTimeTickerById(root.Id));
    }

    [Fact]
    public async Task Restrict_without_children_does_not_block_deletion()
    {
        var leaf = Node(ChainDeleteBehavior.Restrict);
        await Provider.AddTimeTickers([leaf]);
        Assert.True(await Provider.RemoveTimeTickers([leaf.Id]) > 0);
        Assert.Null(await Provider.GetTimeTickerById(leaf.Id));
    }

    [Fact]
    public async Task Policy_change_preserves_a_running_roots_lease_and_generation()
    {
        var root = Chain(ChainDeleteBehavior.Restrict);
        await Provider.AddTimeTickers([root]);
        Assert.NotNull(await Provider.AcquireTimeTickerOnDemandAsync(root.Id, Now, CancellationToken.None));
        var before = await Provider.GetTimeTickerById(root.Id);
        Assert.Equal(ChainDeleteBehavior.Restrict, before.DeleteBehavior);
        Assert.Equal(1, await Provider.SetTimeTickerDeleteBehaviorAsync(root.Id, ChainDeleteBehavior.Cascade));
        var after = await Provider.GetTimeTickerById(root.Id);
        Assert.Equal(before.Status, after.Status);
        Assert.Equal(before.AcquisitionToken, after.AcquisitionToken);
        Assert.Equal(before.LeaseUntil, after.LeaseUntil);
        Assert.Equal(before.ChainGeneration, after.ChainGeneration);
        Assert.Equal(0, await Provider.RemoveTimeTickers([root.Id]));
        Assert.NotNull(await Provider.GetTimeTickerById(root.Id));
    }

    [Fact]
    public async Task Provider_bulk_delete_keeps_protected_chain_intact()
    {
        var root = Chain(ChainDeleteBehavior.Restrict);
        Set(Assert.Single(Assert.Single(root.Children).Children), nameof(root.Status), TickerStatus.Idle);
        var other = Node();
        await Provider.AddTimeTickers([root, other]);
        Assert.True(await Provider.RemoveTimeTickers([root.Id, other.Id]) > 0);
        Assert.NotNull(await Provider.GetTimeTickerById(root.Id));
        Assert.Null(await Provider.GetTimeTickerById(other.Id));
    }

    [Fact]
    public async Task Retention_keeps_protected_chains_but_continues_to_other_roots()
    {
        var root = Chain(ChainDeleteBehavior.Restrict);
        Set(Assert.Single(Assert.Single(root.Children).Children), nameof(root.Status), TickerStatus.Idle);
        var leaf = Node(ChainDeleteBehavior.Restrict);
        await Provider.AddTimeTickers([root, leaf]);
        var cutoffs = new RetentionCutoffs(Now.AddDays(-7), null, null, null);
        var cursor = RetentionCursor.Start;
        for (var i = 0; i < 12; i++)
        {
            var batch = await Provider.DeleteEligibleTimeTickerChainsAsync(cutoffs, 1, cursor);
            cursor = batch.NextCursor;
            if (!batch.HasMore) break;
        }
        Assert.Null(await Provider.GetTimeTickerById(leaf.Id));
        var saved = await Provider.GetTimeTickerById(root.Id);
        Assert.Equal(ChainDeleteBehavior.Restrict, saved.DeleteBehavior);
        Assert.Single(Assert.Single(saved.Children).Children);
    }

    [Fact]
    public async Task Policy_update_rejects_invalid_values_and_missing_or_child_ids()
    {
        var root = Chain(ChainDeleteBehavior.Cascade);
        await Provider.AddTimeTickers([root]);
        Assert.Equal(0, await Provider.SetTimeTickerDeleteBehaviorAsync(Guid.NewGuid(), ChainDeleteBehavior.Restrict));
        var saved = await Provider.GetTimeTickerById(root.Id);
        Assert.Equal(0, await Provider.SetTimeTickerDeleteBehaviorAsync(Assert.Single(saved.Children).Id, ChainDeleteBehavior.Restrict));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Provider.SetTimeTickerDeleteBehaviorAsync(root.Id, (ChainDeleteBehavior)42));
        Assert.Equal(ChainDeleteBehavior.Cascade, (await Provider.GetTimeTickerById(root.Id)).DeleteBehavior);
    }
}
