using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using TickerQ.BackgroundServices;
using TickerQ.Provider;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Managers;
using TickerQ.Utilities.Models;
using Xunit;

namespace TickerQ.Tests;

public class TickerQRetentionBackgroundServiceTests
{
    private sealed class FixedClock : ITickerClock
    {
        public FixedClock(DateTime utcNow) => UtcNow = utcNow;
        public DateTime UtcNow { get; }
    }

    private static TickerQRetentionBackgroundService NewService(
        IInternalTickerManager manager, JobRetentionOptions options, ITickerClock clock,
        ILogger<TickerQRetentionBackgroundService> logger = null)
        => new TickerQRetentionBackgroundService(
            manager, options, clock,
            logger ?? NullLogger<TickerQRetentionBackgroundService>.Instance);

    private static void StubEmpty(IInternalTickerManager manager)
    {
        manager.SweepTimeChainsAsync(Arg.Any<RetentionCutoffs>(), Arg.Any<int>(), Arg.Any<RetentionCursor>(), Arg.Any<CancellationToken>())
            .Returns(RetentionChainBatchResult.Empty);
        manager.SweepCronOccurrencesAsync(Arg.Any<RetentionCutoffs>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(RetentionBatchResult.Empty);
    }

    [Fact]
    public async Task StartAsync_Throws_WhenConfiguredButProviderUnsupported()
    {
        var manager = Substitute.For<IInternalTickerManager>();
        manager.SupportsRetention.Returns(false);
        var options = new JobRetentionOptions { DeleteSucceededAfter = TimeSpan.FromDays(1) };

        var service = NewService(manager, options, new FixedClock(DateTime.UtcNow));

        await Assert.ThrowsAsync<NotSupportedException>(() => service.StartAsync(CancellationToken.None));
    }

    [Fact]
    public async Task StartAsync_Succeeds_WhenSupported()
    {
        var manager = Substitute.For<IInternalTickerManager>();
        manager.SupportsRetention.Returns(true);
        StubEmpty(manager);
        var options = new JobRetentionOptions
        {
            DeleteSucceededAfter = TimeSpan.FromDays(1),
            SweepInterval = TimeSpan.FromSeconds(30)
        };

        var service = NewService(manager, options, new FixedClock(DateTime.UtcNow));

        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public void ComputeCutoffs_MapsWindowsToAbsoluteInstants_NullStaysNull()
    {
        var now = new DateTime(2026, 07, 28, 12, 0, 0, DateTimeKind.Utc);
        var options = new JobRetentionOptions
        {
            DeleteSucceededAfter = TimeSpan.FromDays(7),
            DeleteFailedAfter = TimeSpan.FromDays(30),
        };
        var service = NewService(Substitute.For<IInternalTickerManager>(), options, new FixedClock(now));

        var cutoffs = service.ComputeCutoffs(now);

        Assert.Equal(now - TimeSpan.FromDays(7), cutoffs.SucceededBefore);
        Assert.Equal(now - TimeSpan.FromDays(30), cutoffs.FailedBefore);
        Assert.Null(cutoffs.CancelledBefore);
        Assert.Null(cutoffs.SkippedBefore);
    }

    [Fact]
    public async Task RunSweepAsync_CapsAtMaxBatchesPerSweep_WhenBothStreamsAlwaysHaveMore()
    {
        var manager = Substitute.For<IInternalTickerManager>();
        manager.SupportsRetention.Returns(true);
        manager.SweepTimeChainsAsync(Arg.Any<RetentionCutoffs>(), Arg.Any<int>(), Arg.Any<RetentionCursor>(), Arg.Any<CancellationToken>())
            .Returns(new RetentionChainBatchResult(1, hasMore: true, RetentionCursor.After(DateTime.UtcNow, Guid.NewGuid())));
        manager.SweepCronOccurrencesAsync(Arg.Any<RetentionCutoffs>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new RetentionBatchResult(1, hasMore: true));
        var options = new JobRetentionOptions { DeleteSucceededAfter = TimeSpan.FromDays(1), MaxBatchesPerSweep = 3 };
        var service = NewService(manager, options, new FixedClock(DateTime.UtcNow));

        await service.RunSweepAsync(CancellationToken.None);

        await manager.Received(2).SweepTimeChainsAsync(
            Arg.Any<RetentionCutoffs>(), Arg.Any<int>(), Arg.Any<RetentionCursor>(), Arg.Any<CancellationToken>());
        await manager.Received(1).SweepCronOccurrencesAsync(
            Arg.Any<RetentionCutoffs>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunSweepAsync_CheckpointsTimeCursorBeforePersistentCronFailure()
    {
        var manager = Substitute.For<IInternalTickerManager>();
        manager.SupportsRetention.Returns(true);
        var advanced = RetentionCursor.After(DateTime.UtcNow.AddMinutes(-10), Guid.NewGuid());
        var receivedCursors = new List<RetentionCursor>();
        manager.SweepTimeChainsAsync(
                Arg.Any<RetentionCutoffs>(), Arg.Any<int>(), Arg.Any<RetentionCursor>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                receivedCursors.Add(call.ArgAt<RetentionCursor>(2));
                return receivedCursors.Count == 1
                    ? new RetentionChainBatchResult(0, hasMore: true, advanced)
                    : RetentionChainBatchResult.Empty;
            });
        manager.SweepCronOccurrencesAsync(
                Arg.Any<RetentionCutoffs>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns<Task<RetentionBatchResult>>(_ => throw new InvalidOperationException("cron unavailable"));
        var options = new JobRetentionOptions
        {
            DeleteSucceededAfter = TimeSpan.FromDays(1),
            MaxBatchesPerSweep = 2
        };
        var service = NewService(manager, options, new FixedClock(DateTime.UtcNow));

        await service.RunSweepAsync(CancellationToken.None);
        await service.RunSweepAsync(CancellationToken.None);

        Assert.Equal(2, receivedCursors.Count);
        Assert.Equal(RetentionCursor.Start, receivedCursors[0]);
        Assert.Equal(advanced, receivedCursors[1]);
    }

    [Fact]
    public async Task RunSweepAsync_StopsEarly_WhenBothStreamsExhausted()
    {
        var manager = Substitute.For<IInternalTickerManager>();
        manager.SupportsRetention.Returns(true);
        StubEmpty(manager); // hasMore = false for both
        var options = new JobRetentionOptions { DeleteFailedAfter = TimeSpan.FromDays(1), MaxBatchesPerSweep = 10 };
        var service = NewService(manager, options, new FixedClock(DateTime.UtcNow));

        await service.RunSweepAsync(CancellationToken.None);

        await manager.Received(1).SweepTimeChainsAsync(
            Arg.Any<RetentionCutoffs>(), Arg.Any<int>(), Arg.Any<RetentionCursor>(), Arg.Any<CancellationToken>());
        await manager.Received(1).SweepCronOccurrencesAsync(
            Arg.Any<RetentionCutoffs>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunSweepAsync_DoesNotStop_WhenBatchDeletesNothingButHasMore()
    {
        // A batch that only skips blocked chains deletes nothing yet still makes progress (advancing the
        // cursor). The service must NOT stop on zero-deletions; it must keep going until HasMore is false.
        var manager = Substitute.For<IInternalTickerManager>();
        manager.SupportsRetention.Returns(true);
        var calls = 0;
        manager.SweepTimeChainsAsync(Arg.Any<RetentionCutoffs>(), Arg.Any<int>(), Arg.Any<RetentionCursor>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                calls++;
                // First two batches delete nothing but have more; third deletes and ends.
                if (calls < 3)
                    return new RetentionChainBatchResult(0, hasMore: true, RetentionCursor.After(DateTime.UtcNow.AddSeconds(calls), Guid.NewGuid()));
                return new RetentionChainBatchResult(5, hasMore: false, RetentionCursor.Start);
            });
        manager.SweepCronOccurrencesAsync(Arg.Any<RetentionCutoffs>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(RetentionBatchResult.Empty);
        var options = new JobRetentionOptions { DeleteSucceededAfter = TimeSpan.FromDays(1), MaxBatchesPerSweep = 10 };
        var service = NewService(manager, options, new FixedClock(DateTime.UtcNow));

        var result = await service.RunSweepAsync(CancellationToken.None);

        Assert.Equal(3, calls);                 // did not stop on the zero-deletion batches
        Assert.Equal(5, result.DeletedTimeTickers);
    }

    [Fact]
    public async Task RunSweepAsync_UsesSameCutoffsAcrossBatches()
    {
        var manager = Substitute.For<IInternalTickerManager>();
        manager.SupportsRetention.Returns(true);
        var captured = new List<RetentionCutoffs>();
        manager.SweepTimeChainsAsync(Arg.Any<RetentionCutoffs>(), Arg.Any<int>(), Arg.Any<RetentionCursor>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                captured.Add(ci.Arg<RetentionCutoffs>());
                return new RetentionChainBatchResult(1, hasMore: captured.Count < 3, RetentionCursor.After(DateTime.UtcNow.AddSeconds(captured.Count), Guid.NewGuid()));
            });
        manager.SweepCronOccurrencesAsync(Arg.Any<RetentionCutoffs>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(RetentionBatchResult.Empty);
        var options = new JobRetentionOptions { DeleteSucceededAfter = TimeSpan.FromDays(1), MaxBatchesPerSweep = 5 };
        var service = NewService(manager, options, new FixedClock(DateTime.UtcNow));

        await service.RunSweepAsync(CancellationToken.None);

        Assert.Equal(3, captured.Count);
        Assert.All(captured, c => Assert.Equal(captured[0].SucceededBefore, c.SucceededBefore));
    }

    [Fact]
    public async Task ExecuteAsync_ContinuesAfterSweepFailure()
    {
        var manager = Substitute.For<IInternalTickerManager>();
        manager.SupportsRetention.Returns(true);
        manager.SweepCronOccurrencesAsync(Arg.Any<RetentionCutoffs>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(RetentionBatchResult.Empty);
        var callCount = 0;
        var secondCall = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        manager.SweepTimeChainsAsync(Arg.Any<RetentionCutoffs>(), Arg.Any<int>(), Arg.Any<RetentionCursor>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                var n = Interlocked.Increment(ref callCount);
                if (n == 1) throw new InvalidOperationException("boom");
                secondCall.TrySetResult();
                return Task.FromResult(RetentionChainBatchResult.Empty);
            });
        var options = new JobRetentionOptions
        {
            DeleteSucceededAfter = TimeSpan.FromDays(1),
            SweepInterval = TimeSpan.FromMilliseconds(20)
        };
        var service = NewService(manager, options, new FixedClock(DateTime.UtcNow));

        await service.StartAsync(CancellationToken.None);
        await secondCall.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await service.StopAsync(CancellationToken.None);

        Assert.True(callCount >= 2);
    }

    // ---- Service + real manager + in-memory provider: starvation regression ----
    private sealed class RetTime2 : TimeTickerEntity<RetTime2> { }
    private sealed class RetCron2 : CronTickerEntity { }

    [Fact]
    public async Task Service_MakesBoundedProgress_WithoutStarvingLaterRoot_OrDeletingBlockedChain()
    {
        var now = new DateTime(2026, 07, 28, 12, 0, 0, DateTimeKind.Utc);
        var clock = new FixedClock(now);
        var schedulerOptions = new SchedulerOptionsBuilder { NodeIdentifier = "retention-node" };
        var services = new ServiceCollection();
        services.AddSingleton<ITickerClock>(clock);
        services.AddSingleton(schedulerOptions);
        var provider = new TickerInMemoryPersistenceProvider<RetTime2, RetCron2>(services.BuildServiceProvider());

        // clear any residual static state for this closed generic
        var existing = await provider.GetTimeTickers(_ => true, CancellationToken.None);
        if (existing.Length > 0)
            await provider.RemoveTimeTickers(Array.ConvertAll(existing, e => e.Id), CancellationToken.None);

        RetTime2 Node(TickerStatus status, DateTime executedAt, Guid? parentId = null) => new()
        {
            Id = Guid.NewGuid(), Function = "F", ExecutionTime = now.AddMinutes(-30),
            Status = status, ExecutedAt = executedAt, ParentId = parentId,
            CreatedAt = now.AddDays(-40), UpdatedAt = now.AddDays(-40)
        };

        // R1: oldest root, blocked by a too-recent child.
        var r1 = Node(TickerStatus.Done, now.AddDays(-20));
        var r1Child = Node(TickerStatus.Done, now.AddDays(-1), r1.Id);
        r1.Children.Add(r1Child);
        await provider.AddTimeTickers(new[] { r1 }, CancellationToken.None);
        // R2: newer, fully eligible.
        var r2 = Node(TickerStatus.Done, now.AddDays(-10));
        await provider.AddTimeTickers(new[] { r2 }, CancellationToken.None);

        var manager = new InternalTickerManager<RetTime2, RetCron2>(
            provider, clock, Substitute.For<ITickerQNotificationHubSender>(), schedulerOptions);
        var options = new JobRetentionOptions
        {
            DeleteSucceededAfter = TimeSpan.FromDays(7),
            BatchSize = 1,            // examine one root per batch
            MaxBatchesPerSweep = 5
        };
        var service = NewService(manager, options, clock);

        var result = await service.RunSweepAsync(CancellationToken.None);

        Assert.Equal(1, result.DeletedTimeTickers); // R2 (single node) deleted
        Assert.NotNull(await provider.GetTimeTickerById(r1.Id, CancellationToken.None));      // blocked chain retained
        Assert.NotNull(await provider.GetTimeTickerById(r1Child.Id, CancellationToken.None));
        Assert.Null(await provider.GetTimeTickerById(r2.Id, CancellationToken.None));         // later root deleted
    }
}
