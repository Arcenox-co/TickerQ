using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using TickerQ.Utilities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Models;
using Xunit;

namespace TickerQ.Tests;

[Collection("TickerCancellationTokenState")]
public class TickerCancellationTokenManagerTests : IDisposable
{
    public void Dispose()
    {
        TickerCancellationTokenManager.CleanUpTickerCancellationTokens();
    }

    [Fact]
    public void RequestCancellationById_Cancels_The_Token()
    {
        var cts = new CancellationTokenSource();
        var tickerId = Guid.NewGuid();
        var context = MakeContext(tickerId);

        TickerCancellationTokenManager.AddTickerCancellationToken(cts, context, isDue: false);

        var token = cts.Token;
        Assert.False(token.IsCancellationRequested);

        var result = TickerCancellationTokenManager.RequestTickerCancellationById(tickerId);

        Assert.True(result);
        Assert.True(token.IsCancellationRequested);
    }

    [Fact]
    public void RequestCancellationById_Returns_False_For_Unknown_Id()
    {
        var result = TickerCancellationTokenManager.RequestTickerCancellationById(Guid.NewGuid());

        Assert.False(result);
    }

    [Fact]
    public void IsParentRunning_Returns_True_When_Ticker_Registered()
    {
        var cts = new CancellationTokenSource();
        var parentId = Guid.NewGuid();
        var tickerId = Guid.NewGuid();
        var context = MakeContext(tickerId, parentId);

        TickerCancellationTokenManager.AddTickerCancellationToken(cts, context, isDue: false);

        Assert.True(TickerCancellationTokenManager.IsParentRunning(parentId));
    }

    [Fact]
    public void IsParentRunning_Returns_False_After_Removal()
    {
        var cts = new CancellationTokenSource();
        var parentId = Guid.NewGuid();
        var tickerId = Guid.NewGuid();
        var context = MakeContext(tickerId, parentId);

        TickerCancellationTokenManager.AddTickerCancellationToken(cts, context, isDue: false);
        TickerCancellationTokenManager.RemoveTickerCancellationToken(tickerId);

        Assert.False(TickerCancellationTokenManager.IsParentRunning(parentId));
    }

    [Fact]
    public void IsParentRunningExcludingSelf_Returns_False_When_Only_Self()
    {
        var cts = new CancellationTokenSource();
        var parentId = Guid.NewGuid();
        var tickerId = Guid.NewGuid();
        var context = MakeContext(tickerId, parentId);

        TickerCancellationTokenManager.AddTickerCancellationToken(cts, context, isDue: false);

        Assert.False(TickerCancellationTokenManager.IsParentRunningExcludingSelf(parentId, tickerId));
    }

    [Fact]
    public void IsParentRunningExcludingSelf_Returns_True_When_Sibling_Exists()
    {
        var parentId = Guid.NewGuid();
        var ticker1 = Guid.NewGuid();
        var ticker2 = Guid.NewGuid();

        TickerCancellationTokenManager.AddTickerCancellationToken(
            new CancellationTokenSource(), MakeContext(ticker1, parentId), isDue: false);
        TickerCancellationTokenManager.AddTickerCancellationToken(
            new CancellationTokenSource(), MakeContext(ticker2, parentId), isDue: false);

        Assert.True(TickerCancellationTokenManager.IsParentRunningExcludingSelf(parentId, ticker1));
    }

    [Fact]
    public void CancelById_Keeps_Entry_Tracked_During_Cancellation()
    {
        // Verifies cancel-before-remove: IsParentRunning should be true
        // at the moment Cancel fires on the CTS
        var parentId = Guid.NewGuid();
        var tickerId = Guid.NewGuid();
        var cts = new CancellationTokenSource();
        var context = MakeContext(tickerId, parentId);

        bool wasTrackedDuringCancel = false;
        cts.Token.Register(() =>
        {
            wasTrackedDuringCancel = TickerCancellationTokenManager.IsParentRunning(parentId);
        });

        TickerCancellationTokenManager.AddTickerCancellationToken(cts, context, isDue: false);

        TickerCancellationTokenManager.RequestTickerCancellationById(tickerId);

        Assert.True(wasTrackedDuringCancel, "the entry should still be in the dictionary when Cancel fires");
    }

    [Fact]
    public void DelayedRemoval_DoesNotEraseReplacementParentMembership()
    {
        var parentId = Guid.NewGuid();
        var tickerId = Guid.NewGuid();
        var context = MakeContext(tickerId, parentId);
        var sourceA = TickerCancellationTokenManager.TryRegisterAcquired(context, isDue: false);
        Assert.NotNull(sourceA);

        var entriesField = typeof(TickerCancellationTokenManager).GetField(
            "TickerCancellationTokens", BindingFlags.Static | BindingFlags.NonPublic);
        var entries = Assert.IsType<ConcurrentDictionary<TickerExecutionKey, TickerCancellationTokenDetails>>(
            entriesField?.GetValue(null));
        var key = new TickerExecutionKey(context.Type, tickerId);
        var detailsA = entries[key];

        Assert.True(((ICollection<KeyValuePair<TickerExecutionKey, TickerCancellationTokenDetails>>)entries)
            .Remove(new KeyValuePair<TickerExecutionKey, TickerCancellationTokenDetails>(key, detailsA)));

        var sourceB = TickerCancellationTokenManager.TryRegisterAcquired(context, isDue: false);
        Assert.NotNull(sourceB);

        var delayedRemoval = typeof(TickerCancellationTokenManager).GetMethod(
            "RemoveFromParentIndex", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(delayedRemoval);
        delayedRemoval!.Invoke(null, new object[] { parentId, key, detailsA });

        Assert.True(TickerCancellationTokenManager.IsParentRunning(parentId));
        Assert.True(TickerCancellationTokenManager.RemoveTickerCancellationToken(tickerId, sourceB!));
        sourceA!.Dispose();
    }

    [Fact]
    public void UnequalAcquisitionGeneration_ReportsConflictAndPreservesLocalOwner()
    {
        var tickerId = Guid.NewGuid();
        var firstContext = MakeContext(tickerId);
        firstContext.AcquisitionToken = Guid.NewGuid();
        var conflictingContext = MakeContext(tickerId);
        conflictingContext.AcquisitionToken = Guid.NewGuid();
        var firstSource = TickerCancellationTokenManager.TryRegisterAcquired(firstContext, isDue: false)!;

        var conflictingSource = TickerCancellationTokenManager.TryRegisterAcquired(
            conflictingContext, isDue: false, out var generationConflict);

        Assert.Null(conflictingSource);
        Assert.True(generationConflict);
        Assert.False(firstSource.IsCancellationRequested);
        var timeLeases = new List<AcquisitionLease>();
        var cronLeases = new List<AcquisitionLease>();
        TickerCancellationTokenManager.SnapshotRunningForLeaseRenewal(timeLeases, cronLeases);
        Assert.Equal(firstContext.AcquisitionToken, Assert.Single(cronLeases).AcquisitionToken);
        Assert.True(TickerCancellationTokenManager.RemoveTickerCancellationToken(
            new TickerExecutionKey(firstContext.Type, tickerId), firstSource));
    }

    [Fact]
    public void DuplicateChildWithSameChainGeneration_IsSameOwner()
    {
        var tickerId = Guid.NewGuid();
        var parentId = Guid.NewGuid();
        var generation = Guid.NewGuid();
        var first = MakeContext(tickerId, parentId);
        first.ChainGeneration = generation;
        var duplicate = MakeContext(tickerId, parentId);
        duplicate.ChainGeneration = generation;
        var liveSource = TickerCancellationTokenManager.TryRegisterAcquired(first, isDue: false)!;

        var duplicateSource = TickerCancellationTokenManager.TryRegisterAcquired(
            duplicate, isDue: false, out var generationConflict);

        Assert.Null(duplicateSource);
        Assert.False(generationConflict);
        Assert.False(liveSource.IsCancellationRequested);
        Assert.True(TickerCancellationTokenManager.RemoveTickerCancellationToken(
            new TickerExecutionKey(first.Type, tickerId), liveSource));
    }

    [Fact]
    public void DuplicateChildWithDifferentChainGeneration_ConflictsWithoutReleasingLiveOwner()
    {
        var tickerId = Guid.NewGuid();
        var parentId = Guid.NewGuid();
        var first = MakeContext(tickerId, parentId);
        first.ChainGeneration = Guid.NewGuid();
        var stale = MakeContext(tickerId, parentId);
        stale.ChainGeneration = Guid.NewGuid();
        var liveSource = TickerCancellationTokenManager.TryRegisterAcquired(first, isDue: false)!;

        var staleSource = TickerCancellationTokenManager.TryRegisterAcquired(
            stale, isDue: false, out var generationConflict);

        Assert.Null(staleSource);
        Assert.True(generationConflict);
        Assert.False(liveSource.IsCancellationRequested);
        Assert.Equal(1, TickerCancellationTokenManager.ActiveCount);
        Assert.True(TickerCancellationTokenManager.RemoveTickerCancellationToken(
            new TickerExecutionKey(first.Type, tickerId), liveSource));
    }

    [Fact]
    public void StaleLeaseGeneration_DoesNotCancelCurrentOwnerAfterTurnover()
    {
        var tickerId = Guid.NewGuid();
        var staleContext = MakeContext(tickerId);
        staleContext.AcquisitionToken = Guid.NewGuid();
        var currentContext = MakeContext(tickerId);
        currentContext.AcquisitionToken = Guid.NewGuid();
        var staleSource = TickerCancellationTokenManager.TryRegisterAcquired(staleContext, isDue: false)!;
        Assert.True(TickerCancellationTokenManager.RemoveTickerCancellationToken(
            new TickerExecutionKey(staleContext.Type, tickerId), staleSource));
        var currentSource = TickerCancellationTokenManager.TryRegisterAcquired(currentContext, isDue: false)!;

        var cancelled = TickerCancellationTokenManager.RequestTickerCancellation(
            new TickerExecutionLease(staleContext.Type, tickerId, staleContext.AcquisitionToken));

        Assert.False(cancelled);
        Assert.False(currentSource.IsCancellationRequested);
        TickerCancellationTokenManager.RemoveTickerCancellationToken(
            new TickerExecutionKey(currentContext.Type, tickerId), currentSource);
    }

    [Fact]
    public async Task DelayedParentPublication_DoesNotSurviveConcurrentOwnerRemoval()
    {
        var tickerId = Guid.NewGuid();
        var parentId = Guid.NewGuid();
        var context = MakeContext(tickerId, parentId);
        context.AcquisitionToken = Guid.NewGuid();
        var locks = (object[])typeof(TickerCancellationTokenManager)
            .GetField("ParentIndexLocks", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null)!;
        var parentLock = locks[(int)((uint)parentId.GetHashCode() % locks.Length)];
        var registry = (System.Collections.Concurrent.ConcurrentDictionary<
            TickerExecutionKey, TickerCancellationTokenDetails>)typeof(TickerCancellationTokenManager)
            .GetField("TickerCancellationTokens", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null)!;
        var key = new TickerExecutionKey(context.Type, tickerId);
        Task registration;
        Task removal;

        lock (parentLock)
        {
            registration = Task.Factory.StartNew(
                () => TickerCancellationTokenManager.TryRegisterAcquired(context, isDue: false),
                CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            Assert.True(SpinWait.SpinUntil(() => registry.TryGetValue(key, out _), TimeSpan.FromSeconds(5)));
            var details = registry[key];
            removal = Task.Factory.StartNew(
                () => TickerCancellationTokenManager.RemoveTickerCancellationToken(key, details.CancellationSource),
                CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            Assert.True(SpinWait.SpinUntil(() => !registry.ContainsKey(key), TimeSpan.FromSeconds(5)));
        }

        await Task.WhenAll(registration, removal);
        Assert.False(TickerCancellationTokenManager.IsParentRunning(parentId));
    }

    [Fact]
    public void SameParentGuidAcrossTickerTypes_DoesNotCreateFalseCronSibling()
    {
        var parentId = Guid.NewGuid();
        var timeContext = MakeContext(Guid.NewGuid(), parentId);
        timeContext.Type = TickerType.TimeTicker;
        var cronContext = MakeContext(Guid.NewGuid(), parentId);
        TickerCancellationTokenManager.AddTickerCancellationToken(
            new CancellationTokenSource(), timeContext, isDue: false);
        TickerCancellationTokenManager.AddTickerCancellationToken(
            new CancellationTokenSource(), cronContext, isDue: false);

        Assert.False(TickerCancellationTokenManager.IsParentRunningExcludingSelf(
            parentId, cronContext.TickerId));
    }

    [Fact]
    public void RequestCancellationById_CancelsBothTickerTypesWhenGuidsCollide()
    {
        var tickerId = Guid.NewGuid();
        var timeContext = MakeContext(tickerId);
        timeContext.Type = TickerType.TimeTicker;
        timeContext.ParentId = null;
        var cronContext = MakeContext(tickerId, Guid.NewGuid());
        var timeSource = TickerCancellationTokenManager.TryRegisterAcquired(timeContext, isDue: false)!;
        var cronSource = TickerCancellationTokenManager.TryRegisterAcquired(cronContext, isDue: false)!;

        Assert.True(TickerCancellationTokenManager.RequestTickerCancellationById(tickerId));
        Assert.True(timeSource.IsCancellationRequested);
        Assert.True(cronSource.IsCancellationRequested);
    }

    [Fact]
    public void SameGuidAcrossTickerTypes_RegistersAndTracksBothExecutions()
    {
        var tickerId = Guid.NewGuid();
        var timeContext = MakeContext(tickerId);
        timeContext.Type = TickerType.TimeTicker;
        timeContext.ParentId = null;
        var cronContext = MakeContext(tickerId, Guid.NewGuid());

        var timeSource = TickerCancellationTokenManager.TryRegisterAcquired(timeContext, isDue: false);
        var cronSource = TickerCancellationTokenManager.TryRegisterAcquired(cronContext, isDue: false);

        Assert.NotNull(timeSource);
        Assert.NotNull(cronSource);
        Assert.Equal(2, TickerCancellationTokenManager.ActiveCount);

        var timeLeases = new List<AcquisitionLease>();
        var cronLeases = new List<AcquisitionLease>();
        TickerCancellationTokenManager.SnapshotRunningForLeaseRenewal(timeLeases, cronLeases);
        Assert.Contains(timeLeases, lease => lease.TickerId == tickerId);
        Assert.Contains(cronLeases, lease => lease.TickerId == tickerId);

        Assert.True(TickerCancellationTokenManager.RemoveTickerCancellationToken(tickerId, timeSource!));
        Assert.True(TickerCancellationTokenManager.RemoveTickerCancellationToken(tickerId, cronSource!));
    }

    private static InternalFunctionContext MakeContext(Guid tickerId, Guid? parentId = null)
    {
        return new InternalFunctionContext
        {
            TickerId = tickerId,
            ParentId = parentId,
            FunctionName = "Test",
            Type = TickerType.CronTickerOccurrence
        };
    }
}
