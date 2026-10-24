using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using TickerQ.Utilities;
using TickerQ.Utilities.Cluster;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Models;

namespace TickerQ.BackgroundServices;

internal enum AutomaticEpochAction
{
    /// <summary>Claim <see cref="AutomaticEpochDecision.Epoch"/> and run reconciliation.</summary>
    Claim,
    /// <summary>The epoch was claimed with this manifest but not committed yet: run (or rejoin) reconciliation.</summary>
    Resume,
    /// <summary>The epoch is active with this manifest: execute against it without reconciling.</summary>
    Join,
    /// <summary>This host's manifest belongs to an earlier deployment: stay fenced.</summary>
    Fenced
}

internal readonly record struct AutomaticEpochDecision(AutomaticEpochAction Action, long Epoch)
{
    /// <summary>
    /// Chooses what a host does with an automatic epoch, from the durable state and its own startup
    /// manifest. A manifest the store has never seen is a new deployment and claims the next epoch; one
    /// the store has seen before belongs to an older deployment and never reconciles over a newer one.
    /// </summary>
    internal static AutomaticEpochDecision Decide(ActivationEpochState state, string manifest)
    {
        if (state == null || state.Epoch == 0)
            return new(AutomaticEpochAction.Claim, 1);
        if (state.Phase == ActivationEpochPhase.Pending)
            return new(AutomaticEpochAction.Claim, state.Epoch);
        if (string.Equals(state.Manifest, manifest, StringComparison.Ordinal))
            return new(state.Phase == ActivationEpochPhase.Activated
                ? AutomaticEpochAction.Join
                : AutomaticEpochAction.Resume, state.Epoch);
        if (state.ManifestHistory.Contains(manifest, StringComparer.Ordinal))
            return new(AutomaticEpochAction.Fenced, state.Epoch);
        return new(AutomaticEpochAction.Claim, state.Epoch + 1);
    }
}

/// <summary>
/// Runs the durable startup reconciliation sequence for one claimed epoch: provider bootstrap, chain
/// repair, policy and cron reconciliation, finalization, user seeding, provider startup actions, and the
/// commit. Shared by the initializer and the activation monitor, which can claim an epoch after startup
/// (taking over from a stopped deployment, or completing due retirements).
/// </summary>
internal sealed class TickerQReconciliationActivator
{
    /// <summary>How recently a node must have reported an epoch to count as running it.</summary>
    internal static readonly TimeSpan LiveNodeWindow = TimeSpan.FromSeconds(30);

    private readonly IServiceProvider _serviceProvider;
    private readonly TickerExecutionContext _executionContext;

    public TickerQReconciliationActivator(IServiceProvider serviceProvider, TickerExecutionContext executionContext)
    {
        _serviceProvider = serviceProvider;
        _executionContext = executionContext;
    }

    /// <summary>This host's startup manifest, computed once by the initializer.</summary>
    internal string Manifest { get; set; }

    /// <summary>
    /// Runs every phase and commits. Replicas with the same manifest may run the phases at the same time;
    /// when another one commits <paramref name="targetEpoch"/> first, this host stops, since the durable work
    /// is done, and only runs its own process-local provider startup.
    /// </summary>
    internal async Task RunAsync(IInternalTickerManager manager, ReconciliationActivationScope scope,
        long targetEpoch, CancellationToken cancellationToken)
    {
        var providerStartupDone = false;
        try
        {
            await RunPhasesAsync(manager, scope, targetEpoch, () => providerStartupDone = true, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (ActivationCompletedElsewhereException)
        {
            if (!providerStartupDone)
                await RunProviderStartupActionsAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task RunPhasesAsync(IInternalTickerManager manager, ReconciliationActivationScope scope,
        long targetEpoch, Action providerStartupDone, CancellationToken cancellationToken)
    {
        // Provider bootstrap runs before any migration, reconciliation, seeding, or scheduler query.
        foreach (var bootstrapper in _serviceProvider.GetServices<ITickerQPersistenceBootstrapper>())
            await bootstrapper.BootstrapAsync(cancellationToken).ConfigureAwait(false);
        await CheckpointAsync(manager, scope, targetEpoch, "01-bootstrap-complete", cancellationToken).ConfigureAwait(false);

        await manager.RepairTimeTickerChainsAsync(cancellationToken).ConfigureAwait(false);
        await CheckpointAsync(manager, scope, targetEpoch, "02-chain-repair-complete", cancellationToken).ConfigureAwait(false);

        var options = _executionContext.OptionsSeeding;
        // Schedule policies are seeded before crons so a seeded cron can use a seeded policy; seeded
        // crons are validated against the policies that exist at this point.
        if (options?.SchedulePolicySeedAction != null || options?.SchedulePolicySeederAction != null ||
            options == null || options.SeedDefinedCronTickers)
        {
            using var startupAdmission = StartupSeederAdmissionContext.Enter(scope.ScopeKey, targetEpoch);
            if (options?.SchedulePolicySeedAction != null)
                await options.SchedulePolicySeedAction(_serviceProvider, cancellationToken).ConfigureAwait(false);
            if (options?.SchedulePolicySeederAction != null)
                await options.SchedulePolicySeederAction(_serviceProvider, cancellationToken).ConfigureAwait(false);
            if (options == null || options.SeedDefinedCronTickers)
                await SeedDefinedCronTickers(manager, cancellationToken).ConfigureAwait(false);
        }
        await CheckpointAsync(manager, scope, targetEpoch, "03-definition-reconciliation-complete", cancellationToken)
            .ConfigureAwait(false);

        // Constraints/readiness that depend on reconciled data complete before consumer seeders.
        foreach (var finalizer in _serviceProvider.GetServices<ITickerQPersistenceFinalizer>())
            await finalizer.FinalizeAsync(cancellationToken).ConfigureAwait(false);
        await CheckpointAsync(manager, scope, targetEpoch, "04-finalization-complete", cancellationToken).ConfigureAwait(false);

        if (options?.TimeTickerSeedAction != null || options?.TimeSeederAction != null || options?.CronSeederAction != null)
        {
            using var startupAdmission = StartupSeederAdmissionContext.Enter(scope.ScopeKey, targetEpoch);
            if (options.TimeTickerSeedAction != null)
                await options.TimeTickerSeedAction(_serviceProvider, cancellationToken).ConfigureAwait(false);
            if (options.TimeSeederAction != null)
                await options.TimeSeederAction(_serviceProvider, cancellationToken).ConfigureAwait(false);
            if (options.CronSeederAction != null)
                await options.CronSeederAction(_serviceProvider, cancellationToken).ConfigureAwait(false);
        }
        await CheckpointAsync(manager, scope, targetEpoch, "05-user-seeding-complete", cancellationToken).ConfigureAwait(false);

        // Preserve the activation-protocol phase boundary used by rolling upgrades. Missed work is
        // now decided per ticker during normal acquisition/execution instead of by a startup sweep.
        await CheckpointAsync(manager, scope, targetEpoch, "06-stale-occurrence-handling-complete", cancellationToken)
            .ConfigureAwait(false);

        await RunProviderStartupActionsAsync(cancellationToken).ConfigureAwait(false);
        providerStartupDone();
        await CheckpointAsync(manager, scope, targetEpoch, "07-provider-startup-actions-complete", cancellationToken)
            .ConfigureAwait(false);

        var committed = await manager.CommitReconciliationActivationEpochAsync(scope, targetEpoch, cancellationToken)
            .ConfigureAwait(false);
        RequireExact(committed, targetEpoch, ActivationEpochPhase.Activated, "commit");
    }

    /// <summary>Another host committed the epoch this host was activating.</summary>
    private sealed class ActivationCompletedElsewhereException : Exception;

    /// <summary>
    /// Another deployment claimed a newer epoch while this host was activating. The newer claim already
    /// fences this host's writes; an automatic-epoch host stays fenced, an explicit one fails closed.
    /// </summary>
    internal sealed class ActivationSupersededException(string message) : InvalidOperationException(message);

    /// <summary>Checks that must pass before an automatic epoch is claimed, because a claim fences the running deployment.</summary>
    internal Task PreflightAsync(CancellationToken cancellationToken)
        => _executionContext.OptionsSeeding?.StartupPreflight?.Invoke(_serviceProvider, cancellationToken)
           ?? Task.CompletedTask;

    /// <summary>Process-local provider startup that every scheduler host runs once before its gate opens.</summary>
    internal async Task RunProviderStartupActionsAsync(CancellationToken cancellationToken)
    {
        foreach (var readiness in _serviceProvider.GetServices<ITickerQPersistenceReadinessProbe>())
            await readiness.ProbeAsync(cancellationToken).ConfigureAwait(false);

        var action = _executionContext.ExternalProviderApplicationAction;
        if (action == null)
            return;
        action(_serviceProvider);
        _executionContext.ExternalProviderApplicationAction = null;
    }

    /// <summary>
    /// Whether another node reported executing <paramref name="epoch"/> within <see cref="LiveNodeWindow"/>.
    /// A stopping node reports epoch 0, so a graceful shutdown is seen immediately.
    /// </summary>
    internal static async Task<bool> AnyLiveNodeAtAsync(IInternalTickerManager manager, long epoch,
        string ownNodeKey, DateTime nowUtc, CancellationToken cancellationToken)
    {
        IReadOnlyList<TickerQNodeRecord> nodes =
            await manager.GetClusterNodesAsync(cancellationToken).ConfigureAwait(false);
        return nodes.Any(node => node.ActivationEpoch == epoch &&
                                 node.LastSeenAtUtc >= nowUtc - LiveNodeWindow &&
                                 !string.Equals(node.NodeKey, ownNodeKey, StringComparison.Ordinal));
    }

    internal static bool IsExact(ActivationEpochState state, long epoch, ActivationEpochPhase phase)
        => state != null && state.Epoch == epoch && state.Phase == phase;

    internal static void RequireExact(ActivationEpochState state, long epoch, ActivationEpochPhase phase, string operation)
    {
        if (IsExact(state, epoch, phase))
            return;
        var message = $"Reconciliation activation {operation} returned '{state?.ToString() ?? "<null>"}'; " +
                      $"expected exact epoch={epoch}, phase={phase}. Startup remains fail-closed.";
        if (state != null && state.Epoch > epoch)
            throw new ActivationSupersededException(message);
        throw new InvalidOperationException(message);
    }

    private static async Task CheckpointAsync(IInternalTickerManager manager, ReconciliationActivationScope scope,
        long targetEpoch, string checkpoint, CancellationToken cancellationToken)
    {
        var state = await manager.AdvanceReconciliationCheckpointAsync(scope, targetEpoch, checkpoint, cancellationToken)
            .ConfigureAwait(false);
        if (IsExact(state, targetEpoch, ActivationEpochPhase.Activated))
            throw new ActivationCompletedElsewhereException();
        RequireExact(state, targetEpoch, ActivationEpochPhase.Activating, "checkpoint");
    }

    private async Task SeedDefinedCronTickers(IInternalTickerManager manager, CancellationToken cancellationToken)
    {
        var options = _executionContext.OptionsSeeding;
        var functionsToSeed = options?.CronTickerSeedFactory == null
            ? new List<DefinedCronTickerSeed>()
            : (await options.CronTickerSeedFactory(_serviceProvider, cancellationToken).ConfigureAwait(false)).ToList();
        var applicationNamespace = options?.ApplicationNamespace;
        if (applicationNamespace == null)
        {
            await manager.MigrateDefinedCronTickers(functionsToSeed.ToArray(), cancellationToken).ConfigureAwait(false);
            return;
        }

        var ownership = options.LegacyDefinedCronOwnership == null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(options.LegacyDefinedCronOwnership, StringComparer.Ordinal);
        if (options.AdoptAllLegacyDefinedCronTickers)
            foreach (var seed in functionsToSeed)
                ownership.TryAdd(seed.Function, applicationNamespace);

        await manager.MigrateDefinedCronTickers(
            new DefinedCronSeedManifest(applicationNamespace, functionsToSeed, ownership), cancellationToken)
            .ConfigureAwait(false);
    }
}
