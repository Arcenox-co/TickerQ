using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using TickerQ.BackgroundServices;
using TickerQ.Utilities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Models;

namespace TickerQ.Tests;

[Collection("TickerCancellationTokenState")]
public sealed class SchedulerDueTimeTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Di_UsesSystemClockEvenWhenTheHostRegistersAnUnrelatedTimeProvider()
    {
        var unrelatedClock = new ManualTimeProvider(Start.AddYears(50));
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(unrelatedClock);
        services.AddSingleton(new TickerExecutionContext());
        services.AddSingleton(Substitute.For<ITickerExecutionTaskHandler>());
        services.AddSingleton(Substitute.For<ITickerQTaskScheduler>());
        services.AddSingleton(Substitute.For<IInternalTickerManager>());
        services.AddSingleton(new SchedulerOptionsBuilder());
        services.AddSingleton(LicenseTestState.Active());
        services.AddSingleton<TickerQSchedulerBackgroundService>();
        using var provider = services.BuildServiceProvider();

        var service = provider.GetRequiredService<TickerQSchedulerBackgroundService>();
        var clock = typeof(TickerQSchedulerBackgroundService)
            .GetField("_timeProvider", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(service);

        Assert.Same(unrelatedClock, provider.GetRequiredService<TimeProvider>());
        Assert.Same(TimeProvider.System, clock);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(10_000, 1)]
    [InlineData(10_001, 2)]
    [InlineData(19_999, 2)]
    public async Task DueWait_RoundsPositiveFractionalMillisecondsUp(long ticks, int expectedMilliseconds)
    {
        var clock = new ManualTimeProvider(Start);
        var due = Start.AddTicks(ticks);
        var wait = TickerQSchedulerBackgroundService.WaitUntilDueAsync(
            due.UtcDateTime, clock, CancellationToken.None);

        var timer = await clock.NextTimerAsync();
        Assert.Equal(TimeSpan.FromMilliseconds(expectedMilliseconds), timer.DueTime);
        Assert.False(wait.IsCompleted);

        clock.SetUtcNow(due);
        timer.Fire();
        await wait;
    }

    [Fact]
    public async Task DueWait_RechecksAbsoluteTimeAfterAnEarlyTimerWake()
    {
        var clock = new ManualTimeProvider(Start);
        var due = Start.AddTicks(15_000);
        var wait = TickerQSchedulerBackgroundService.WaitUntilDueAsync(
            due.UtcDateTime, clock, CancellationToken.None);
        var first = await clock.NextTimerAsync();
        Assert.Equal(TimeSpan.FromMilliseconds(2), first.DueTime);

        clock.SetUtcNow(Start.AddMilliseconds(1));
        first.Fire();
        var second = await clock.NextTimerAsync();
        Assert.Equal(TimeSpan.FromMilliseconds(1), second.DueTime);
        Assert.False(wait.IsCompleted);

        clock.SetUtcNow(due);
        second.Fire();
        await wait;
    }

    [Fact]
    public async Task DueWait_RechecksRepeatedEarlyWakesAndABackwardClockChange()
    {
        var clock = new ManualTimeProvider(Start);
        var due = Start.AddMilliseconds(10);
        var wait = TickerQSchedulerBackgroundService.WaitUntilDueAsync(
            due.UtcDateTime, clock, CancellationToken.None);
        var first = await clock.NextTimerAsync();
        Assert.Equal(TimeSpan.FromMilliseconds(10), first.DueTime);

        clock.SetUtcNow(Start.AddTicks(99_000));
        first.Fire();
        var second = await clock.NextTimerAsync();
        Assert.Equal(TimeSpan.FromMilliseconds(1), second.DueTime);

        clock.SetUtcNow(Start.AddMilliseconds(8));
        second.Fire();
        var third = await clock.NextTimerAsync();
        Assert.Equal(TimeSpan.FromMilliseconds(2), third.DueTime);

        clock.SetUtcNow(Start.AddMilliseconds(9));
        third.Fire();
        var fourth = await clock.NextTimerAsync();
        Assert.Equal(TimeSpan.FromMilliseconds(1), fourth.DueTime);
        Assert.False(wait.IsCompleted);

        clock.SetUtcNow(due);
        fourth.Fire();
        await wait;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-10_000_000)]
    public void DueWait_AlreadyDueCompletesSynchronouslyWithoutATimer(long ticks)
    {
        var clock = new ManualTimeProvider(Start);
        var wait = TickerQSchedulerBackgroundService.WaitUntilDueAsync(
            Start.AddTicks(ticks).UtcDateTime, clock, CancellationToken.None);

        Assert.True(wait.IsCompletedSuccessfully);
        Assert.Equal(0, clock.TimerCount);
    }

    [Fact]
    public async Task DueWait_CapsLongWaitsAndRechecksAfterEachDay()
    {
        var clock = new ManualTimeProvider(Start);
        var due = Start.AddDays(100);
        var wait = TickerQSchedulerBackgroundService.WaitUntilDueAsync(
            due.UtcDateTime, clock, CancellationToken.None);
        var first = await clock.NextTimerAsync();
        Assert.Equal(TimeSpan.FromDays(1), first.DueTime);

        clock.SetUtcNow(Start.AddDays(1));
        first.Fire();
        var second = await clock.NextTimerAsync();
        Assert.Equal(TimeSpan.FromDays(1), second.DueTime);
        Assert.False(wait.IsCompleted);

        clock.SetUtcNow(due);
        second.Fire();
        await wait;
    }

    [Fact]
    public async Task DueWait_CancellationDisposesTheTimerWithoutWaitingForItsDueTime()
    {
        var clock = new ManualTimeProvider(Start);
        using var cancellation = new CancellationTokenSource();
        var wait = TickerQSchedulerBackgroundService.WaitUntilDueAsync(
            Start.AddHours(1).UtcDateTime, clock, cancellation.Token).AsTask();
        var timer = await clock.NextTimerAsync();

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait);
        Assert.True(timer.IsDisposed);
        Assert.Equal(1, clock.TimerCount);
    }

    [Theory]
    [InlineData(TickerType.TimeTicker)]
    [InlineData(TickerType.CronTickerOccurrence)]
    public async Task Scheduler_KeepsTheWholeAcquiredBatchQueuedUntilItsLatestDueTime(TickerType type)
    {
        var clock = new ManualTimeProvider(Start);
        var context = new TickerExecutionContext();
        var earlier = Context(type, Start.AddMilliseconds(1).UtcDateTime);
        var later = Context(type, Start.AddTicks(15_000).UtcDateTime);
        // The due-time guard must run even if acquisition returned an elapsed/rounded delay.
        var functions = new[] { later, earlier };
        var manager = Substitute.For<IInternalTickerManager>();
        var handler = Substitute.For<ITickerExecutionTaskHandler>();
        var scheduler = Substitute.For<ITickerQTaskScheduler>();
        var transitioned = new TaskCompletionSource<DateTimeOffset>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads = 0;
        manager.GetNextTickers(Arg.Any<CancellationToken>()).Returns(_ =>
            Interlocked.Increment(ref reads) == 1
                ? (TimeSpan.Zero, functions)
                : (Timeout.InfiniteTimeSpan, Array.Empty<InternalFunctionContext>()));
        manager.SetTickersInProgress(Arg.Any<InternalFunctionContext[]>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                transitioned.TrySetResult(clock.GetUtcNow());
                // Model lost leases after the boundary; no user delegate needs to run in this test.
                return Array.Empty<InternalFunctionContext>();
            });

        using var service = new TickerQSchedulerBackgroundService(
            clock, context, handler, scheduler, manager,
            new SchedulerOptionsBuilder { MinPollingInterval = TimeSpan.FromMinutes(1), ShutdownDrainTimeout = TimeSpan.Zero },
            LicenseTestState.Active());
        await service.StartAsync(CancellationToken.None);
        try
        {
            var first = await clock.NextTimerAsync();
            Assert.Equal(TimeSpan.FromMilliseconds(2), first.DueTime);
            Assert.False(transitioned.Task.IsCompleted);
            Assert.Equal(functions, context.Functions);

            clock.SetUtcNow(Start.AddMilliseconds(1));
            first.Fire();
            var second = await clock.NextTimerAsync();
            Assert.Equal(TimeSpan.FromMilliseconds(1), second.DueTime);
            Assert.False(transitioned.Task.IsCompleted);

            clock.SetUtcNow(Start.AddTicks(15_000));
            second.Fire();
            Assert.Equal(clock.GetUtcNow(), await transitioned.Task.WaitAsync(TimeSpan.FromSeconds(5)));
            await manager.Received(1).SetTickersInProgress(
                Arg.Is<InternalFunctionContext[]>(batch => batch.SequenceEqual(functions)), Arg.Any<CancellationToken>());
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Theory]
    [InlineData(TickerType.TimeTicker)]
    [InlineData(TickerType.CronTickerOccurrence)]
    public async Task Scheduler_ShutdownWhileWaitingReleasesTheExactAcquiredBatch(TickerType type)
    {
        var clock = new ManualTimeProvider(Start);
        var context = new TickerExecutionContext();
        var functions = new[] { Context(type, Start.AddHours(1).UtcDateTime) };
        var manager = Substitute.For<IInternalTickerManager>();
        var handler = Substitute.For<ITickerExecutionTaskHandler>();
        var scheduler = Substitute.For<ITickerQTaskScheduler>();
        manager.GetNextTickers(Arg.Any<CancellationToken>()).Returns((TimeSpan.Zero, functions));
        using var service = new TickerQSchedulerBackgroundService(
            clock, context, handler, scheduler, manager,
            new SchedulerOptionsBuilder { ShutdownDrainTimeout = TimeSpan.Zero },
            LicenseTestState.Active());

        await service.StartAsync(CancellationToken.None);
        var timer = await clock.NextTimerAsync();
        var acquired = context.Functions;
        Assert.Equal(functions, acquired);
        await service.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(timer.IsDisposed);
        await manager.Received(1).ReleaseAcquiredResources(acquired, CancellationToken.None);
        await manager.DidNotReceive().SetTickersInProgress(
            Arg.Any<InternalFunctionContext[]>(), Arg.Any<CancellationToken>());
        await scheduler.DidNotReceive().QueueAsync(
            Arg.Any<Func<CancellationToken, Task>>(), Arg.Any<TickerTaskPriority>(), Arg.Any<CancellationToken>());
        Assert.Empty(context.Functions);
    }

    private static InternalFunctionContext Context(TickerType type, DateTime due) => new()
    {
        TickerId = Guid.NewGuid(), FunctionName = "DueTimeTest", Type = type,
        ParentId = type == TickerType.CronTickerOccurrence ? Guid.NewGuid() : null,
        AcquisitionToken = Guid.NewGuid(), ExecutionTime = due, TimeTickerChildren = []
    };

    // Callbacks fire only when the test asks them to, independently of UTC advancement.
    // This models early timer completion and wall-clock changes without timing races.
    private sealed class ManualTimeProvider(DateTimeOffset initial) : TimeProvider
    {
        private readonly object gate = new();
        private DateTimeOffset now = initial;
        private readonly Channel<ManualTimer> timers = Channel.CreateUnbounded<ManualTimer>();
        private int timerCount;
        public int TimerCount => Volatile.Read(ref timerCount);
        public override DateTimeOffset GetUtcNow() { lock (gate) return now; }
        public void SetUtcNow(DateTimeOffset value) { lock (gate) now = value; }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Assert.Equal(Timeout.InfiniteTimeSpan, period);
            var timer = new ManualTimer(callback, state, dueTime);
            Interlocked.Increment(ref timerCount);
            Assert.True(timers.Writer.TryWrite(timer));
            return timer;
        }

        public Task<ManualTimer> NextTimerAsync() =>
            timers.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        public sealed class ManualTimer(TimerCallback callback, object? state, TimeSpan dueTime) : ITimer
        {
            private int disposed;
            private int fired;
            public TimeSpan DueTime { get; private set; } = dueTime;
            public bool IsDisposed => Volatile.Read(ref disposed) != 0;

            public bool Change(TimeSpan value, TimeSpan period)
            {
                if (IsDisposed) return false;
                Assert.Equal(Timeout.InfiniteTimeSpan, period);
                DueTime = value;
                return true;
            }

            public void Fire()
            {
                if (!IsDisposed && Interlocked.Exchange(ref fired, 1) == 0)
                    callback(state);
            }

            public void Dispose() => Interlocked.Exchange(ref disposed, 1);
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
