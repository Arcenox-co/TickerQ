using System;
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
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Models;
using Xunit;

namespace TickerQ.Tests;

/// <summary>
/// Regression for the "periodics fire once at startup then never again" defect: drives the real
/// InternalTickerManagerWithPeriodic.GetNextTickers against the real in-memory periodic provider,
/// emulating the scheduler loop (queue → run → complete → advance clock → repeat), and asserts the
/// schedule keeps progressing across multiple intervals.
/// </summary>
public class PeriodicSchedulerProgressionTests
{
    private sealed class MutableClock : ITickerClock
    {
        public DateTime UtcNow { get; set; }
    }

    public class FakeTimeTicker : TimeTickerEntity<FakeTimeTicker> { }
    public class FakeCronTicker : CronTickerEntity { }

    private static IInternalTickerManager BuildManager(
        ITickerClock clock,
        IPeriodicTickerPersistenceProvider<PeriodicTickerEntity> periodicProvider)
    {
        var timePersistence = Substitute.For<ITickerPersistenceProvider<FakeTimeTicker, FakeCronTicker>>();
        timePersistence.GetAllCronTickerExpressions(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Array.Empty<CronTickerEntity>()));
        timePersistence.GetEarliestAvailableCronOccurrence(Arg.Any<Guid[]>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<CronTickerOccurrenceEntity<FakeCronTicker>>(null));
        timePersistence.GetEarliestTimeTickers(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Array.Empty<TimeTickerEntity>()));

        var notificationHub = Substitute.For<ITickerQNotificationHubSender>();

        var services = new ServiceCollection().BuildServiceProvider();

        var managerType = typeof(IInternalTickerManager).Assembly
            .GetType("TickerQ.Utilities.Managers.InternalTickerManagerWithPeriodic`3")!
            .MakeGenericType(typeof(FakeTimeTicker), typeof(FakeCronTicker), typeof(PeriodicTickerEntity));

        return (IInternalTickerManager)Activator.CreateInstance(
            managerType,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public,
            binder: null,
            args: new object[] { timePersistence, periodicProvider, clock, notificationHub, services },
            culture: null)!;
    }

    private static PeriodicTickerInMemoryPersistenceProvider<PeriodicTickerEntity> NewProvider()
        => new(new ServiceCollection().BuildServiceProvider());

    // Emulates one scheduler turn: take the previously-queued functions, complete them as the executor
    // would (Done + advance parent schedule), then ask for the next batch and advance the clock to it.
    private static async Task<int> RunSchedulerLoop(
        IInternalTickerManager manager,
        IPeriodicTickerPersistenceProvider<PeriodicTickerEntity> provider,
        MutableClock clock,
        int turns)
    {
        var fireCount = 0;
        InternalFunctionContext[] pending = Array.Empty<InternalFunctionContext>();

        for (var i = 0; i < turns; i++)
        {
            // Complete whatever was queued on the previous turn.
            foreach (var fn in pending.Where(f => f.Type == TickerType.PeriodicTickerOccurrence))
            {
                fireCount++;
                var ctx = new InternalFunctionContext
                {
                    TickerId = fn.TickerId,
                    ParentId = fn.ParentId,
                    Type = TickerType.PeriodicTickerOccurrence,
                    ExecutedAt = clock.UtcNow
                }.SetProperty(x => x.Status, TickerStatus.Done);

                await manager.UpdateTickerAsync(ctx);
            }

            var (timeRemaining, functions) = await manager.GetNextTickers();
            pending = functions ?? Array.Empty<InternalFunctionContext>();

            if (timeRemaining == Timeout.InfiniteTimeSpan || timeRemaining > TimeSpan.FromDays(1))
            {
                // Scheduler would sleep for a day — schedule is dead. Advance a tiny bit to keep the
                // loop going so the test can observe the lack of progress instead of hanging.
                clock.UtcNow = clock.UtcNow.AddSeconds(1);
            }
            else
            {
                clock.UtcNow = clock.UtcNow.Add(timeRemaining <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(1) : timeRemaining);
            }
        }

        return fireCount;
    }

    [Fact]
    public async Task SinglePeriodic_FiresRepeatedly_AcrossIntervals()
    {
        var start = new DateTime(2026, 6, 15, 12, 0, 0, DateTimeKind.Utc);
        var clock = new MutableClock { UtcNow = start };
        var provider = NewProvider();
        var manager = BuildManager(clock, provider);

        var ticker = new PeriodicTickerEntity
        {
            Id = Guid.NewGuid(),
            Function = "Poll",
            Interval = TimeSpan.FromSeconds(60),
            IsActive = true
        };
        await provider.InsertPeriodicTickers(new[] { ticker }, default);

        // Enough turns to cover several 60s intervals.
        var fires = await RunSchedulerLoop(manager, provider, clock, turns: 12);

        // Expected behaviour: the periodic fires once per interval — several times across the run.
        Assert.True(fires >= 3, $"periodic should keep firing across intervals, but fired {fires} time(s)");
    }

    [Fact]
    public async Task SkipPeriodic_FiresRepeatedly_AcrossIntervals()
    {
        // Faithful to the real loop: SetTickersInProgress runs on the queued batch BEFORE the next
        // GetNextTickers, so the occurrence is InProgress when the next planning pass happens. With
        // ChainOverlapBehavior.Skip this is exactly where the "fires once then stalls" regression bites.
        var start = new DateTime(2026, 6, 15, 12, 0, 0, DateTimeKind.Utc);
        var clock = new MutableClock { UtcNow = start };
        var provider = NewProvider();
        var manager = BuildManager(clock, provider);

        var ticker = new PeriodicTickerEntity
        {
            Id = Guid.NewGuid(),
            Function = "Poll",
            Interval = TimeSpan.FromSeconds(60),
            IsActive = true,
            ChainOverlapBehavior = ChainOverlapBehavior.Skip
        };
        await provider.InsertPeriodicTickers(new[] { ticker }, default);

        var fireCount = 0;
        InternalFunctionContext[] pending = Array.Empty<InternalFunctionContext>();

        for (var i = 0; i < 12; i++)
        {
            if (pending.Length > 0)
            {
                // Mirror the real loop: mark in progress, then complete.
                await manager.SetTickersInProgress(pending);
                foreach (var fn in pending.Where(f => f.Type == TickerType.PeriodicTickerOccurrence))
                {
                    fireCount++;
                    var ctx = new InternalFunctionContext
                    {
                        TickerId = fn.TickerId,
                        ParentId = fn.ParentId,
                        Type = TickerType.PeriodicTickerOccurrence,
                        ExecutedAt = clock.UtcNow
                    }.SetProperty(x => x.Status, TickerStatus.Done);
                    await manager.UpdateTickerAsync(ctx);
                }
            }

            var (timeRemaining, functions) = await manager.GetNextTickers();
            pending = functions ?? Array.Empty<InternalFunctionContext>();

            clock.UtcNow = (timeRemaining == Timeout.InfiniteTimeSpan || timeRemaining > TimeSpan.FromDays(1))
                ? clock.UtcNow.AddSeconds(1)
                : clock.UtcNow.Add(timeRemaining <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(1) : timeRemaining);
        }

        Assert.True(fireCount >= 3, $"Skip periodic should keep firing across intervals, but fired {fireCount} time(s)");
    }

    [Fact]
    public async Task MultiplePeriodics_AllKeepFiring()
    {
        var start = new DateTime(2026, 6, 15, 12, 0, 0, DateTimeKind.Utc);
        var clock = new MutableClock { UtcNow = start };
        var provider = NewProvider();
        var manager = BuildManager(clock, provider);

        var fast = new PeriodicTickerEntity { Id = Guid.NewGuid(), Function = "Fast", Interval = TimeSpan.FromSeconds(60), IsActive = true };
        var slow = new PeriodicTickerEntity { Id = Guid.NewGuid(), Function = "Slow", Interval = TimeSpan.FromSeconds(180), IsActive = true };
        await provider.InsertPeriodicTickers(new[] { fast, slow }, default);

        await RunSchedulerLoop(manager, provider, clock, turns: 20);

        // After the run, both tickers must have advanced (LastExecutedAt set and moved past start).
        var fastStored = await provider.GetPeriodicTickerById(fast.Id, default);
        var slowStored = await provider.GetPeriodicTickerById(slow.Id, default);

        Assert.True(fastStored.ExecutionCount >= 3, $"fast periodic stalled: {fastStored.ExecutionCount} runs");
        Assert.True(slowStored.ExecutionCount >= 1, $"slow periodic stalled: {slowStored.ExecutionCount} runs");
    }

    /// <summary>
    /// Reproduces the live-stand stall that the loops above miss. The real scheduler
    /// (<c>TickerQSchedulerBackgroundService</c>) dispatches execution fire-and-forget and calls
    /// <c>GetNextTickers</c> AGAIN immediately — while the just-queued occurrence is still
    /// InProgress and NOT yet Done. For a <see cref="ChainOverlapBehavior.Skip"/> ticker this hits
    /// the window where:
    ///   - the occurrence is in `unfinished` (InProgress) → fresh candidate is overlap-suppressed, and
    ///   - the occurrence is no longer `Idle|Queued` → not surfaced via earliestStored either.
    /// The planner then has nothing to return and the scheduler sleeps for a day. The existing
    /// SkipPeriodic test completes the occurrence (Done) BEFORE planning, so it never sees this gap.
    /// </summary>
    [Fact]
    public async Task SkipPeriodic_InProgressDuringPlanning_StillSchedulesNext()
    {
        var start = new DateTime(2026, 6, 15, 12, 0, 0, DateTimeKind.Utc);
        var clock = new MutableClock { UtcNow = start };
        var provider = NewProvider();
        var manager = BuildManager(clock, provider);

        var ticker = new PeriodicTickerEntity
        {
            Id = Guid.NewGuid(),
            Function = "Poll",
            Interval = TimeSpan.FromSeconds(60),
            IsActive = true,
            ChainOverlapBehavior = ChainOverlapBehavior.Skip
        };
        await provider.InsertPeriodicTickers(new[] { ticker }, default);

        // Turn 1: plan + materialize the first occurrence (as the scheduler's first pass does).
        var (_, first) = await manager.GetNextTickers();
        Assert.NotEmpty(first); // sanity: first occurrence is created

        // Mirror the real loop EXACTLY: mark the queued batch InProgress, then — without completing
        // it — ask for the next batch. Execution is async/in-flight on the real stand at this point.
        await manager.SetTickersInProgress(first);

        // The occurrence has NOT completed yet. The planner must still keep the schedule alive:
        // either surface the in-flight occurrence or schedule the next interval. It must NOT
        // return "nothing for a day".
        var (timeRemaining, second) = await manager.GetNextTickers();

        var deadScheduler = timeRemaining == Timeout.InfiniteTimeSpan || timeRemaining > TimeSpan.FromDays(1);
        Assert.False(
            deadScheduler && (second is null || second.Length == 0),
            $"planner stalled while occurrence was InProgress: timeRemaining={timeRemaining}, " +
            $"functions={second?.Length ?? 0}. This is the live-stand 'fires once then never again' bug.");
    }
}
