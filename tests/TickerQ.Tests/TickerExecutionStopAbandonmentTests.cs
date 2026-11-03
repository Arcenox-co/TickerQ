using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using TickerQ.TickerQThreadPool;
using TickerQ.Utilities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Instrumentation;
using TickerQ.Utilities.Models;

namespace TickerQ.Tests;

/// <summary>
/// A stopping node cancels the tokens of the work it admitted. That hands the work back — a job
/// that never started is released, one that started is left for stale recovery — instead of
/// recording a Cancelled the user never asked for.
/// </summary>
[Collection("TickerCancellationTokenState")]
public sealed class TickerExecutionStopAbandonmentTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private readonly IInternalTickerManager _manager = Substitute.For<IInternalTickerManager>();
    private readonly SlotScheduler _scheduler = new();

    [Theory]
    [InlineData(TickerType.TimeTicker)]
    [InlineData(TickerType.CronTickerOccurrence)]
    public async Task CancelledByTheStopBeforeItStarts_IsReleased_NotCancelled(TickerType type)
    {
        await using var services = Services();
        var context = Context(type);
        using var source = new CancellationTokenSource();
        _scheduler.Freeze();
        source.Cancel();

        await Handler(services).ExecuteRegisteredTaskAsync(context, false, source, source.Token);

        await _manager.Received(1).ReleaseAcquiredResources(
            Arg.Is<InternalFunctionContext[]>(x => x.Single() == context), CancellationToken.None);
        await _manager.DidNotReceiveWithAnyArgs().UpdateTickerAsync(default!, default);
        Assert.NotEqual(TickerStatus.Cancelled, context.Status);
    }

    [Fact]
    public async Task WaitingForAWorkerSlot_WhenTheNodeStops_IsReleasedWithoutRunningUserCode()
    {
        await using var services = Services();
        var context = Context(TickerType.TimeTicker);
        context.CachedDelegate = (_, _, _) => throw new InvalidOperationException("User code must not run.");
        using var source = new CancellationTokenSource();
        _scheduler.HoldSlots = true;

        var execution = Handler(services).ExecuteRegisteredTaskAsync(context, false, source, source.Token);
        await _scheduler.Waiting.Task.WaitAsync(Timeout);
        _scheduler.Freeze();
        source.Cancel();
        await execution.WaitAsync(Timeout);

        await _manager.Received(1).ReleaseAcquiredResources(
            Arg.Is<InternalFunctionContext[]>(x => x.Single() == context), CancellationToken.None);
        await _manager.DidNotReceiveWithAnyArgs().UpdateTickerAsync(default!, default);
    }

    [Fact]
    public async Task RunningWhenTheNodeStops_IsLeftInProgressForStaleRecovery()
    {
        await using var services = Services();
        var context = Context(TickerType.TimeTicker);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        context.CachedDelegate = async (cancellationToken, _, _) =>
        {
            started.TrySetResult();
            await Task.Delay(System.Threading.Timeout.Infinite, cancellationToken);
        };
        using var source = new CancellationTokenSource();

        var execution = Handler(services).ExecuteRegisteredTaskAsync(context, false, source, source.Token);
        await started.Task.WaitAsync(Timeout);
        _scheduler.Freeze();
        source.Cancel();
        await execution.WaitAsync(Timeout);

        // No write at all: the row keeps its in-progress lease until it expires, and recovery
        // applies the job's OnStale policy.
        await _manager.DidNotReceiveWithAnyArgs().ReleaseAcquiredResources(default!, default);
        await _manager.DidNotReceiveWithAnyArgs().UpdateTickerAsync(default!, default);
        Assert.Equal(TickerStatus.InProgress, context.Status);
    }

    [Fact]
    public async Task CancelledWhileTheNodeRuns_IsStillPersistedAsCancelled()
    {
        await using var services = Services();
        var context = Context(TickerType.TimeTicker);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        context.CachedDelegate = async (cancellationToken, _, _) =>
        {
            started.TrySetResult();
            await Task.Delay(System.Threading.Timeout.Infinite, cancellationToken);
        };
        using var source = new CancellationTokenSource();

        var execution = Handler(services).ExecuteRegisteredTaskAsync(context, false, source, source.Token);
        await started.Task.WaitAsync(Timeout);
        source.Cancel();
        await execution.WaitAsync(Timeout);

        Assert.Equal(TickerStatus.Cancelled, context.Status);
        await _manager.Received(1).UpdateTickerAsync(context, CancellationToken.None);
        await _manager.DidNotReceiveWithAnyArgs().ReleaseAcquiredResources(default!, default);
    }

    [Fact]
    public async Task CancellationRequestedWhileTheNodeDrains_IsStillPersistedAsCancelled()
    {
        await using var services = Services();
        var context = Context(TickerType.TimeTicker);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        context.CachedDelegate = async (cancellationToken, _, _) =>
        {
            started.TrySetResult();
            await Task.Delay(System.Threading.Timeout.Infinite, cancellationToken);
        };
        using var source = new CancellationTokenSource();

        var execution = Handler(services).ExecuteRegisteredTaskAsync(context, false, source, source.Token);
        await started.Task.WaitAsync(Timeout);
        // The node has begun stopping and is draining; then a user cancels the running job.
        _scheduler.Freeze();
        TickerCancellationTokenManager.RequestCancellation(source);
        await execution.WaitAsync(Timeout);

        Assert.Equal(TickerStatus.Cancelled, context.Status);
        await _manager.Received(1).UpdateTickerAsync(context, CancellationToken.None);
        await _manager.DidNotReceiveWithAnyArgs().ReleaseAcquiredResources(default!, default);
    }

    [Fact]
    public async Task JobThatCancelsItselfWhileTheNodeDrains_IsStillPersistedAsCancelled()
    {
        await using var services = Services();
        var context = Context(TickerType.TimeTicker);
        context.CachedDelegate = async (cancellationToken, _, functionContext) =>
        {
            _scheduler.Freeze();
            functionContext.RequestCancellation();
            await Task.Delay(System.Threading.Timeout.Infinite, cancellationToken);
        };
        using var source = new CancellationTokenSource();

        await Handler(services).ExecuteRegisteredTaskAsync(context, false, source, source.Token).WaitAsync(Timeout);

        Assert.Equal(TickerStatus.Cancelled, context.Status);
        await _manager.Received(1).UpdateTickerAsync(context, CancellationToken.None);
    }

    [Fact]
    public async Task CancellationRequestedBeforeItStarts_WhileTheNodeDrains_IsStillCancelled()
    {
        await using var services = Services();
        var context = Context(TickerType.TimeTicker);
        using var source = new CancellationTokenSource();
        _scheduler.Freeze();
        TickerCancellationTokenManager.RequestCancellation(source);

        await Handler(services).ExecuteRegisteredTaskAsync(context, false, source, source.Token);

        Assert.Equal(TickerStatus.Cancelled, context.Status);
        await _manager.Received(1).UpdateTickerAsync(context, CancellationToken.None);
        await _manager.DidNotReceiveWithAnyArgs().ReleaseAcquiredResources(default!, default);
    }

    private ServiceProvider Services() => new ServiceCollection()
        .AddSingleton<ITickerQTaskScheduler>(_scheduler)
        .BuildServiceProvider();

    private TickerExecutionTaskHandler Handler(IServiceProvider services)
    {
        var clock = Substitute.For<ITickerClock>();
        clock.UtcNow.Returns(_ => DateTime.UtcNow);
        return new TickerExecutionTaskHandler(services, clock, Substitute.For<ITickerQInstrumentation>(), _manager,
            new SchedulerOptionsBuilder(), Substitute.For<ITickerQFailureNotifier>(), LicenseTestState.Active(),
            new TickerFunctionConcurrencyGate());
    }

    private static InternalFunctionContext Context(TickerType type) => new()
    {
        TickerId = Guid.NewGuid(),
        AcquisitionToken = Guid.NewGuid(),
        Type = type,
        FunctionName = "StopAbandonment",
        CachedDelegate = (_, _, _) => Task.CompletedTask,
    };

    /// <summary>Worker slots that can be held, so a job can be caught waiting for one.</summary>
    internal sealed class SlotScheduler : ITickerQTaskScheduler, ITickerQExecutionScheduler
    {
        internal bool HoldSlots { get; set; }
        internal TaskCompletionSource Waiting { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task ExecuteAsync(Func<CancellationToken, Task> work, TickerTaskPriority priority,
            CancellationToken token = default)
        {
            if (HoldSlots)
            {
                Waiting.TrySetResult();
                await Task.Delay(System.Threading.Timeout.Infinite, token);
            }
            await work(token);
        }

        public bool TryQueueOrchestration(Func<CancellationToken, Task> work, CancellationToken admissionToken = default)
            => throw new NotSupportedException();
        public Task WaitForOrchestrationCapacityAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public ValueTask QueueAsync(Func<CancellationToken, Task> work, TickerTaskPriority priority,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void Freeze() => IsFrozen = true;
        public void Resume() => IsFrozen = false;
        public bool IsFrozen { get; private set; }
        public int ActiveWorkers => 0;
        public int ActiveOrchestrationCount => 0;
        public int TotalQueuedTasks => 0;
        public bool IsDisposed => false;
        public string GetDiagnostics() => nameof(SlotScheduler);
        public Task<bool> WaitForRunningTasksAsync(TimeSpan? timeout = null) => Task.FromResult(true);
        public IDisposable BeginFunctionWait() => new Noop();
        public IDisposable BeginRetryDelay() => new Noop();

        private sealed class Noop : IDisposable
        {
            public void Dispose() { }
        }
    }
}
