using TickerQ.Caching.StackExchangeRedis.Helpers;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Models;

namespace TickerQ.Caching.StackExchangeRedis.Tests.Infrastructure;

/// <summary>
/// Chain lease: a time-ticker root whose descendants run after its own terminal write keeps its
/// lease (and lock holder / chain generation) as the chain's ownership record until the tree
/// finishes. Driven through the real Lua scripts.
/// </summary>
public sealed partial class RedisRealScriptTests
{
    private string? _chainLeaseHolder;

    private async Task<(TimeTickerEntity Root, TimeTickerEntity Child, Guid Generation)> AcquireChainAsync()
    {
        var root = NewIdleTicker();
        var child = NewIdleTicker();
        child.ExecutionTime = null;
        child.ParentId = root.Id;
        root.Children.Add(child);
        await _provider.AddTimeTickers([root], CancellationToken.None);
        var acquired = Assert.Single(await _provider.AcquireImmediateTimeTickersAsync(
            [root.Id], CancellationToken.None));
        Assert.Equal(acquired.AcquisitionToken, acquired.ChainGeneration);
        Assert.False(string.IsNullOrWhiteSpace(acquired.LockHolder));
        _chainLeaseHolder = acquired.LockHolder;
        return (root, child, acquired.ChainGeneration!.Value);
    }

    private static InternalFunctionContext RootTerminal(Guid rootId, Guid generation, TickerStatus status,
        bool retainsChainLease, DateTime executedAt)
        => new InternalFunctionContext
        {
            TickerId = rootId, ChainRootId = rootId, ChainGeneration = generation,
            AcquisitionToken = generation, Type = TickerType.TimeTicker,
            RetainsChainLease = retainsChainLease
        }.SetProperty(x => x.Status, status)
         .SetProperty(x => x.ExecutedAt, executedAt);

    private Task<int> StartChildAsync(TimeTickerEntity root, TimeTickerEntity child, Guid generation)
        => _provider.UpdateTimeTicker(new InternalFunctionContext
        {
            TickerId = child.Id, ParentId = root.Id, ChainRootId = root.Id,
            ChainGeneration = generation, Type = TickerType.TimeTicker
        }.SetProperty(x => x.Status, TickerStatus.InProgress), CancellationToken.None);

    [Fact]
    public async Task ChainLease_TerminalRootWrite_RetainsLeaseOnlyWhenFlagged_RealLua()
    {
        _now = BaseNow.AddSeconds(3);
        var (retained, _, retainedGeneration) = await AcquireChainAsync();
        var (plain, _, plainGeneration) = await AcquireChainAsync();
        var (committed, _, committedGeneration) = await AcquireChainAsync();

        Assert.Equal(1, await _provider.UpdateTimeTicker(
            RootTerminal(retained.Id, retainedGeneration, TickerStatus.Failed, true, _now), CancellationToken.None));
        Assert.Equal(1, await _provider.UpdateTimeTicker(
            RootTerminal(plain.Id, plainGeneration, TickerStatus.Failed, false, _now), CancellationToken.None));
        // The acknowledged result-publishing path keeps the lease as well.
        Assert.True(await _provider.CommitSuccessfulTickerAsync(
            RootTerminal(committed.Id, committedGeneration, TickerStatus.Done, true, _now)
                .SetProperty(x => x.ResultEnvelope, null)));

        foreach (var (id, generation) in new[] { (retained.Id, retainedGeneration), (committed.Id, committedGeneration) })
        {
            var row = (await _provider.GetTimeTickerById(id))!;
            Assert.Equal(_now.Add(_options.LeaseDuration), row.LeaseUntil);
            Assert.Equal(_chainLeaseHolder, row.LockHolder);
            Assert.Equal(generation, row.ChainGeneration);
            Assert.Null(row.AcquisitionToken);
            // A root still holding its chain lease is not a retention candidate.
            Assert.Null(await _db.SortedSetScoreAsync(
                row.Status == TickerStatus.Done
                    ? _provider.RuntimeKeysForTest.TimeTickerRetentionSucceeded
                    : _provider.RuntimeKeysForTest.TimeTickerRetentionFailed, id.ToString()));
        }

        var cleared = (await _provider.GetTimeTickerById(plain.Id))!;
        Assert.Null(cleared.LeaseUntil);
        Assert.Null(cleared.AcquisitionToken);
        Assert.Equal(_chainLeaseHolder, cleared.LockHolder);
        Assert.Equal(plainGeneration, cleared.ChainGeneration);
    }

    [Fact]
    public async Task ChainLease_RenewAndStillHeld_AcceptOnlyTheRootsChainGeneration_RealLua()
    {
        var (root, child, generation) = await AcquireChainAsync();
        Assert.Equal(1, await _provider.UpdateTimeTicker(
            RootTerminal(root.Id, generation, TickerStatus.Done, true, _now), CancellationToken.None));
        Assert.Equal(1, await StartChildAsync(root, child, generation));

        var lease = new AcquisitionLease(root.Id, generation);
        var otherLease = new AcquisitionLease(root.Id, Guid.NewGuid());
        var renewedUntil = _now.AddMinutes(7);
        Assert.Equal(0, await _provider.RenewTimeTickerLeases([otherLease], renewedUntil.AddMinutes(1)));
        Assert.Equal(1, await _provider.RenewTimeTickerLeases([lease], renewedUntil));
        Assert.Equal(renewedUntil, (await _provider.GetTimeTickerById(root.Id))!.LeaseUntil);
        Assert.Equal([root.Id], await _provider.GetStillHeldTickerIds([lease], []));
        Assert.Empty(await _provider.GetStillHeldTickerIds([otherLease], []));

        // A finished leaf root is still held under its generation, but has no lease to renew.
        var leaf = NewIdleTicker();
        await _provider.AddTimeTickers([leaf], CancellationToken.None);
        var leafGeneration = Assert.Single(await _provider.AcquireImmediateTimeTickersAsync([leaf.Id]))
            .ChainGeneration!.Value;
        Assert.Equal(1, await _provider.UpdateTimeTicker(
            RootTerminal(leaf.Id, leafGeneration, TickerStatus.Done, false, _now), CancellationToken.None));
        var leafLease = new AcquisitionLease(leaf.Id, leafGeneration);
        Assert.Equal([leaf.Id], await _provider.GetStillHeldTickerIds([leafLease], []));
        Assert.Empty(await _provider.GetStillHeldTickerIds([new AcquisitionLease(leaf.Id, Guid.NewGuid())], []));
        Assert.Equal(0, await _provider.RenewTimeTickerLeases([leafLease], renewedUntil));
        Assert.Null((await _provider.GetTimeTickerById(leaf.Id))!.LeaseUntil);
    }

    [Fact]
    public async Task ChainLease_Release_ClearsOnlyTheMatchingGeneration_RealLua()
    {
        var (root, _, generation) = await AcquireChainAsync();
        _now = BaseNow.AddSeconds(2);
        Assert.Equal(1, await _provider.UpdateTimeTicker(
            RootTerminal(root.Id, generation, TickerStatus.Done, true, _now), CancellationToken.None));
        var before = (await _provider.GetTimeTickerById(root.Id))!;

        Assert.Equal(0, await _provider.ReleaseChainLeaseAsync(new AcquisitionLease(root.Id, null)));
        Assert.Equal(0, await _provider.ReleaseChainLeaseAsync(new AcquisitionLease(root.Id, Guid.NewGuid())));
        Assert.Equal(before.LeaseUntil, (await _provider.GetTimeTickerById(root.Id))!.LeaseUntil);

        Assert.Equal(1, await _provider.ReleaseChainLeaseAsync(new AcquisitionLease(root.Id, generation)));
        var released = (await _provider.GetTimeTickerById(root.Id))!;
        Assert.Null(released.LeaseUntil);
        Assert.True(released.UpdatedAt > before.UpdatedAt);
        Assert.Equal(generation, released.ChainGeneration);
        Assert.Equal(_chainLeaseHolder, released.LockHolder);
        Assert.NotNull(await _db.SortedSetScoreAsync(
            _provider.RuntimeKeysForTest.TimeTickerRetentionSucceeded, root.Id.ToString()));
        Assert.Equal(0, await _provider.ReleaseChainLeaseAsync(new AcquisitionLease(root.Id, generation)));
    }

    [Fact]
    public async Task ChainLease_DeadChainRecovery_CancelsRunningStepOfExpiredChainOnly_RealLua()
    {
        _now = BaseNow;
        var (dead, deadChild, deadGeneration) = await AcquireChainAsync();
        Assert.Equal(1, await _provider.UpdateTimeTicker(
            RootTerminal(dead.Id, deadGeneration, TickerStatus.Done, true, _now), CancellationToken.None));
        Assert.Equal(1, await StartChildAsync(dead, deadChild, deadGeneration));

        _now = BaseNow.Add(_options.LeaseDuration);
        var (live, liveChild, liveGeneration) = await AcquireChainAsync();
        Assert.Equal(1, await _provider.UpdateTimeTicker(
            RootTerminal(live.Id, liveGeneration, TickerStatus.Done, true, _now), CancellationToken.None));
        Assert.Equal(1, await StartChildAsync(live, liveChild, liveGeneration));

        _now = BaseNow.Add(_options.LeaseDuration).AddSeconds(30);
        var result = await _provider.RecoverStaleTickers(maxStaleRestarts: 3, CancellationToken.None);
        Assert.Equal(1, result.CancelledTimeTickers);

        var fenced = (await _provider.GetTimeTickerById(dead.Id))!;
        Assert.Equal(TickerStatus.Done, fenced.Status);
        Assert.Null(fenced.LeaseUntil);
        Assert.Null(fenced.ChainGeneration);
        var cancelled = fenced.Children.Single();
        Assert.Equal(TickerStatus.Cancelled, cancelled.Status);
        Assert.Equal(_now, cancelled.ExecutedAt);
        Assert.Null(cancelled.AcquisitionToken);
        Assert.Null(cancelled.LeaseUntil);
        Assert.StartsWith("Stale:", cancelled.ExceptionMessage);
        // The dead owner's late step write is fenced by the cleared generation.
        Assert.Equal(0, await _provider.UpdateTimeTicker(new InternalFunctionContext
        {
            TickerId = deadChild.Id, ParentId = dead.Id, ChainRootId = dead.Id,
            ChainGeneration = deadGeneration, Type = TickerType.TimeTicker
        }.SetProperty(x => x.Status, TickerStatus.Done), CancellationToken.None));

        var untouched = (await _provider.GetTimeTickerById(live.Id))!;
        Assert.Equal(liveGeneration, untouched.ChainGeneration);
        Assert.NotNull(untouched.LeaseUntil);
        Assert.Equal(TickerStatus.InProgress, untouched.Children.Single().Status);

        Assert.Equal(0, (await _provider.RecoverStaleTickers(maxStaleRestarts: 3, CancellationToken.None))
            .CancelledTimeTickers);
    }

    [Fact]
    public async Task ChainLease_Ownership_ResolvesStepsThroughTheRootAndReportsChainActive_RealLua()
    {
        var (root, child, generation) = await AcquireChainAsync();
        Assert.Equal(1, await _provider.UpdateTimeTicker(
            RootTerminal(root.Id, generation, TickerStatus.Done, true, _now), CancellationToken.None));
        Assert.Equal(1, await StartChildAsync(root, child, generation));

        var step = await _provider.GetExecutionOwnershipAsync(TickerType.TimeTicker, child.Id);
        Assert.NotNull(step);
        Assert.Equal(_chainLeaseHolder, step!.ExecutionOwnerId);
        Assert.Equal(generation, step.AcquisitionToken);
        Assert.Equal(TickerStatus.InProgress, step.Status);
        Assert.True(step.IsCancellable);

        var owner = await _provider.GetExecutionOwnershipAsync(TickerType.TimeTicker, root.Id);
        Assert.NotNull(owner);
        Assert.True(owner!.ChainActive);
        Assert.True(owner.IsCancellable);
        Assert.Equal(TickerStatus.Done, owner.Status);
        Assert.Equal(generation, owner.AcquisitionToken);

        Assert.Equal(1, await _provider.ReleaseChainLeaseAsync(new AcquisitionLease(root.Id, generation)));
        var finished = await _provider.GetExecutionOwnershipAsync(TickerType.TimeTicker, root.Id);
        Assert.False(finished!.ChainActive);
        Assert.False(finished.IsCancellable);
        Assert.Null(await _provider.GetExecutionOwnershipAsync(TickerType.TimeTicker, child.Id));
    }
}
