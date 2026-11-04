using TickerQ.TickerQThreadPool;
using TickerQ.Utilities.Enums;

namespace TickerQ.Tests;

[Collection("TickerQTaskScheduler")]
/// <summary>
/// Work queued with a token that already follows the scheduler's stop runs on that token directly. Any other
/// caller keeps the scheduler's guarantee that the work's token fires when the scheduler stops.
/// </summary>
public sealed class TickerQTaskSchedulerStopBoundTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task StopBoundWork_RunsOnTheCallersToken_AndIsCancelledWhenTheSchedulerStops()
    {
        var scheduler = new TickerQTaskScheduler(2);
        var execution = (ITickerQExecutionScheduler)scheduler;
        var started = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken callerToken = default;

        // As AcquiredExecutionLifecycle does: the job's source is cancelled from the orchestration's stop token.
        Assert.True(execution.TryQueueOrchestration(async stopToken =>
        {
            using var source = new CancellationTokenSource();
            using var registration = stopToken.Register(static state => ((CancellationTokenSource)state!).Cancel(), source);
            callerToken = source.Token;
            await execution.ExecuteStopBoundAsync(async token =>
            {
                started.TrySetResult(token);
                try { await Task.Delay(System.Threading.Timeout.Infinite, token); }
                catch (OperationCanceledException) { cancelled.TrySetResult(); throw; }
            }, TickerTaskPriority.Normal, source.Token, stopToken);
        }));

        var workToken = await started.Task.WaitAsync(Timeout);
        Assert.Equal(callerToken, workToken);

        await scheduler.DisposeAsync();
        await cancelled.Task.WaitAsync(Timeout);
    }

    [Fact]
    public async Task AnyOtherStopToken_StillLinksTheWorkToSchedulerStop()
    {
        var scheduler = new TickerQTaskScheduler(2);
        var execution = (ITickerQExecutionScheduler)scheduler;
        var started = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var callerSource = new CancellationTokenSource();
        using var unrelatedStop = new CancellationTokenSource();

        // The caller's token never fires and the stop token is not this scheduler's: the work must still
        // observe the scheduler stopping, through the link the scheduler adds.
        var run = execution.ExecuteStopBoundAsync(async token =>
        {
            started.TrySetResult(token);
            try { await Task.Delay(System.Threading.Timeout.Infinite, token); }
            catch (OperationCanceledException) { cancelled.TrySetResult(); throw; }
        }, TickerTaskPriority.Normal, callerSource.Token, unrelatedStop.Token);

        var workToken = await started.Task.WaitAsync(Timeout);
        Assert.NotEqual(callerSource.Token, workToken);

        await scheduler.DisposeAsync();
        await cancelled.Task.WaitAsync(Timeout);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(Timeout));
        Assert.False(callerSource.IsCancellationRequested);
    }

    [Fact]
    public async Task StopBoundWork_QueuedBehindAFullScheduler_IsCancelledBeforeStartWhenTheSchedulerStops()
    {
        var scheduler = new TickerQTaskScheduler(1);
        var execution = (ITickerQExecutionScheduler)scheduler;
        var occupied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var waitingRun = new TaskCompletionSource<Task>(TaskCreationOptions.RunContinuationsAsynchronously);
        var queuedRan = false;

        // Fill the single execution slot, then queue stop-bound work behind it.
        var blocker = execution.ExecuteAsync(async token =>
        {
            occupied.TrySetResult();
            await Task.Delay(System.Threading.Timeout.Infinite, token);
        }, TickerTaskPriority.Normal);
        await occupied.Task.WaitAsync(Timeout);

        Assert.True(execution.TryQueueOrchestration(async stopToken =>
        {
            using var source = new CancellationTokenSource();
            using var registration = stopToken.Register(static state => ((CancellationTokenSource)state!).Cancel(), source);
            var run = execution.ExecuteStopBoundAsync(_ => { queuedRan = true; return Task.CompletedTask; },
                TickerTaskPriority.Normal, source.Token, stopToken);
            waitingRun.TrySetResult(run);
            try { await run; } catch (OperationCanceledException) { }
        }));
        var queued = await waitingRun.Task.WaitAsync(Timeout);

        await scheduler.DisposeAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued.WaitAsync(Timeout));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => blocker.WaitAsync(Timeout));
        Assert.False(queuedRan);
    }
}
