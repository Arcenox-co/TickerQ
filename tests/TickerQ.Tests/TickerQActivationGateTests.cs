using System;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities;
using TickerQ.Utilities.Interfaces;
using Xunit;

namespace TickerQ.Tests;

/// <summary>
/// Contract for the in-process reconciliation activation gate. Scheduler/fallback/stale-recovery
/// loops await it before touching the store; it opens exactly once, after the durable epoch is
/// activated, and stays fail-closed (never opens) when activation crashes, cancels, or fails.
/// </summary>
public sealed class TickerQActivationGateTests
{
    [Fact]
    public void FreshGate_IsNotActivated()
    {
        var gate = new TickerQActivationGate();
        Assert.False(gate.IsActivated);
    }

    [Fact]
    public async Task WaitForActivation_BlocksUntilSignalled_ThenReturnsImmediately()
    {
        var gate = new TickerQActivationGate();

        var wait = gate.WaitForActivationAsync(CancellationToken.None);
        Assert.False(wait.IsCompleted);

        gate.SignalActivated();

        await wait.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(gate.IsActivated);

        // A second wait after activation completes synchronously without blocking.
        await gate.WaitForActivationAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task WaitForActivation_ObservesCancellation_WhenNeverActivated()
    {
        var gate = new TickerQActivationGate();
        using var cts = new CancellationTokenSource();

        var wait = gate.WaitForActivationAsync(cts.Token);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait);
        Assert.False(gate.IsActivated);
    }

    [Fact]
    public async Task SignalClosed_RecordsReason_AndLeavesGateClosed()
    {
        var gate = new TickerQActivationGate();
        var reason = new InvalidOperationException("reconciliation failed");

        gate.SignalClosed(reason);

        Assert.False(gate.IsActivated);
        Assert.Same(reason, gate.ClosedReason);

        // A closed gate never opens on its own; a wait keeps parking (fail-closed).
        var wait = gate.WaitForActivationAsync(CancellationToken.None);
        await Task.Delay(50);
        Assert.False(wait.IsCompleted);
    }

    [Fact]
    public void SignalActivated_IsIdempotent()
    {
        var gate = new TickerQActivationGate();
        gate.SignalActivated();
        gate.SignalActivated();
        Assert.True(gate.IsActivated);
    }
}
