using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TickerQ.Utilities;
using TickerQ.Utilities.Cluster;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Models;

namespace TickerQ.BackgroundServices;

/// <summary>
/// Keeps an automatic-epoch scheduler on the right epoch after startup. Every few seconds it reads the
/// durable activation state and:
/// <list type="bullet">
/// <item>follows a newer epoch claimed with this host's own startup manifest (a maintenance claim, or a
/// takeover by another host of the same deployment);</item>
/// <item>resumes a claim of its own that did not finish;</item>
/// <item>takes over when this host is fenced and no node runs the current epoch any more (a rollback, or
/// the newer deployment stopped), or when a claim by another deployment stalled;</item>
/// <item>claims a maintenance epoch with the same manifest when a removed seeded cron's retirement grace
/// has elapsed, because retirement completes only during reconciliation.</item>
/// </list>
/// Only registered for scheduler hosts without an explicit <c>UseReconciliationEpoch</c>.
/// </summary>
internal sealed class TickerQActivationMonitorBackgroundService(
    IInternalTickerManager manager,
    SchedulerOptionsBuilder options,
    TickerQReconciliationActivator activator,
    ITickerQActivationGate activationGate,
    ITickerQHostScheduler hostScheduler,
    ITickerClock clock,
    ILogger<TickerQActivationMonitorBackgroundService> logger) : BackgroundService
{
    internal static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    internal static readonly TimeSpan StalledActivationTimeout = TimeSpan.FromMinutes(2);
    internal static readonly TimeSpan RetirementCheckInterval = TimeSpan.FromMinutes(5);

    private readonly string _nodeKey = TickerQClusterIdentity.NodeKey(options.ExecutionOwnerId);
    private (long Epoch, ActivationEpochPhase Phase, string Checkpoint)? _observedActivation;
    private DateTime _observedSinceUtc;
    private DateTime _nextRetirementCheckUtc;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await activationGate.WaitForActivationAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }

        _nextRetirementCheckUtc = clock.UtcNow + RetirementCheckInterval;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "TickerQ automatic epoch monitoring failed; it will retry.");
            }

            try { await Task.Delay(PollInterval, stoppingToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    internal async Task TickAsync(CancellationToken cancellationToken)
    {
        var manifest = activator.Manifest;
        var scope = options.RuntimeActivationScope;
        if (manifest == null || scope == null || !options.AutomaticReconciliationEpoch)
            return;

        var now = clock.UtcNow;
        var state = await manager.GetReconciliationActivationStateAsync(scope, cancellationToken).ConfigureAwait(false);
        var current = options.RuntimeActivationEpoch;
        var ownManifest = string.Equals(state.Manifest, manifest, StringComparison.Ordinal);

        if (ownManifest && state.Epoch > current && state.Phase == ActivationEpochPhase.Activated)
        {
            Follow(state.Epoch);
            return;
        }

        if (ownManifest && current == state.Epoch && state.Phase == ActivationEpochPhase.Activating)
        {
            // A claim of ours stopped part-way (for example a transient store error). Every phase is
            // idempotent, so finishing it is safe.
            await activator.RunAsync(manager, scope, state.Epoch, cancellationToken).ConfigureAwait(false);
            Follow(state.Epoch);
            return;
        }

        if (current == state.Epoch && ownManifest)
        {
            if (state.Phase == ActivationEpochPhase.Activated && now >= _nextRetirementCheckUtc)
            {
                _nextRetirementCheckUtc = now + RetirementCheckInterval;
                if (await manager.HasDueSeedRetirementsAsync(now - options.DefinedCronRetirementGracePeriod,
                        cancellationToken).ConfigureAwait(false))
                {
                    logger.LogInformation(
                        "A removed seeded cron reached the end of its retirement grace; reconciling under epoch {Epoch}.",
                        state.Epoch + 1);
                    await ClaimAsync(scope, state.Epoch + 1, manifest, cancellationToken).ConfigureAwait(false);
                }
            }
            return;
        }

        // This host is fenced: another deployment owns the current epoch.
        if (!manager.SupportsClusterControl)
            return;
        var observed = (state.Epoch, state.Phase, state.Checkpoint);
        if (_observedActivation != observed)
        {
            _observedActivation = observed;
            _observedSinceUtc = now;
        }

        if (await TickerQReconciliationActivator.AnyLiveNodeAtAsync(manager, state.Epoch, _nodeKey, now,
                cancellationToken).ConfigureAwait(false))
            return;
        // An Activating claim may belong to a host that is still starting and has not written a heartbeat
        // yet; take over only once it has made no progress for a while.
        if (state.Phase == ActivationEpochPhase.Activating && now - _observedSinceUtc < StalledActivationTimeout)
            return;

        logger.LogInformation(
            "No TickerQ node runs reconciliation epoch {Epoch}; this host takes over with its own startup inputs.",
            state.Epoch);
        await ClaimAsync(scope, state.Epoch + 1, manifest, cancellationToken).ConfigureAwait(false);
    }

    private async Task ClaimAsync(ReconciliationActivationScope scope, long target, string manifest,
        CancellationToken cancellationToken)
    {
        try
        {
            await activator.PreflightAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException exception)
        {
            // Claiming would fence every node and then fail; keep the current epoch running instead.
            logger.LogWarning(exception,
                "TickerQ did not claim reconciliation epoch {Epoch} because a seed would fail to reconcile.", target);
            return;
        }

        var begin = await manager.BeginReconciliationActivationEpochAsync(scope, target, manifest, cancellationToken)
            .ConfigureAwait(false);
        if (begin.Epoch != target || !string.Equals(begin.Manifest, manifest, StringComparison.Ordinal))
            return; // Someone else claimed; the next tick decides from the new state.

        options.ResolveRuntimeActivationEpoch(target);
        if (begin.Phase != ActivationEpochPhase.Activated)
        {
            try
            {
                await activator.RunAsync(manager, scope, target, cancellationToken).ConfigureAwait(false);
            }
            catch (TickerQReconciliationActivator.ActivationSupersededException)
            {
                logger.LogWarning("Another deployment claimed a newer epoch while this host was activating epoch {Epoch}.", target);
                return;
            }
        }
        hostScheduler.Restart();
    }

    private void Follow(long epoch)
    {
        options.ResolveRuntimeActivationEpoch(epoch);
        logger.LogInformation("TickerQ now executes against reconciliation epoch {Epoch}.", epoch);
        hostScheduler.Restart();
    }
}
