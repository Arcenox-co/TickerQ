using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TickerQ.Provider;
using TickerQ.Utilities;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Licensing;

namespace TickerQ.BackgroundServices;

/// <summary>
/// Performs TickerQ startup initialization (function discovery, cron ticker seeding,
/// external provider setup) as part of the host lifecycle.
///
/// By running inside <see cref="IHostedService.StartAsync"/> instead of inline in
/// <c>UseTickerQ</c>, this service is naturally skipped by design-time tools
/// (OpenAPI generators, EF migrations, etc.) that build the host but never start it.
/// Registered before the scheduler services to guarantee seeding completes first.
/// </summary>
internal sealed class TickerQInitializerHostedService : IHostedService
{
    private readonly TickerExecutionContext _executionContext;
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;
    private readonly TickerQLicenseStateProvider _licenseState;
    private readonly ILogger<TickerQInitializerHostedService> _logger;
    private readonly ITickerClock _clock;

    /// <summary>
    /// Set to true by <c>UseTickerQ</c> to signal that this hosted service
    /// should perform startup I/O when the host starts. When false (default),
    /// <see cref="StartAsync"/> is a no-op — this naturally prevents initialization
    /// in design-time tool contexts where UseTickerQ is never called.
    /// </summary>
    internal bool InitializationRequested { get; set; }

    public TickerQInitializerHostedService(
        TickerExecutionContext executionContext,
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        TickerQLicenseStateProvider licenseState)
    {
        _executionContext = executionContext;
        _serviceProvider = serviceProvider;
        _configuration = configuration;
        _licenseState = licenseState;
        _logger = serviceProvider.GetService<ILogger<TickerQInitializerHostedService>>();
        _clock = serviceProvider.GetService<ITickerClock>() ?? new TickerSystemClock();
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!InitializationRequested)
            return;

        if (!_licenseState.ExecutionAllowed)
            return;

        // Function discovery is process-local readiness, not persistence activation. Queue-only hosts
        // expose producer managers whose validation/serialization depends on this immutable registry.
        TickerFunctionProvider.UpdateCronExpressionsFromIConfiguration(_configuration);
        TickerFunctionProvider.Build();

        // Queue-only hosts run no scheduler or persistence-mutating maintenance loops. Discovery above is
        // required, but durable bootstrap/repair/reconciliation/finalization must remain completely skipped.
        if (_executionContext.OptionsSeeding is { RegisterBackgroundServices: false })
            return;

        var activationGate = _serviceProvider.GetService<ITickerQActivationGate>();

        try
        {

        var internalTickerManager = _serviceProvider.GetRequiredService<IInternalTickerManager>();
        if (!internalTickerManager.SupportsReconciliationActivationEpoch)
            throw new NotSupportedException(
                "Scheduler startup requires a persistence provider with durable reconciliation activation epoch support.");

        var schedulerOptions = _serviceProvider.GetRequiredService<SchedulerOptionsBuilder>();
        var automatic = schedulerOptions.AutomaticReconciliationEpoch;
        var targetEpoch = schedulerOptions.HasRuntimeActivationScopeBinding
            ? schedulerOptions.RuntimeActivationEpoch
            : _executionContext.OptionsSeeding?.ReconciliationEpoch ?? 1;
        if (targetEpoch <= 0 && !automatic)
            throw new InvalidOperationException("Scheduler startup requires an explicit positive UseReconciliationEpoch value.");
        var activationScope = schedulerOptions.HasRuntimeActivationScopeBinding
            ? schedulerOptions.RuntimeActivationScope
            : new Utilities.Models.ReconciliationActivationScope(
                _executionContext.OptionsSeeding?.ApplicationNamespace);
        if (activationScope == null)
            throw new InvalidOperationException("Scheduler startup requires an application namespace configured with UseApplicationNamespace(...).");
        if ((_executionContext.OptionsSeeding?.SeedDefinedCronTickers ?? true)
            && !internalTickerManager.SupportsAuthoritativeCronReconciliation)
            throw new NotSupportedException(
                "The persistence provider does not support authoritative defined-Cron manifest reconciliation; startup stopped before invoking a lossy legacy fallback.");
        if (automatic && !internalTickerManager.SupportsActivationManifest)
            throw new NotSupportedException(
                "The persistence provider cannot record startup manifests, so TickerQ cannot manage the reconciliation epoch automatically. Configure UseReconciliationEpoch(...) explicitly.");

        // Schema-only prerequisites (notably EF AutoMigrateDatabase) must run before durable Begin,
        // because a pre-protocol store does not yet contain the activation metadata table. This phase
        // cannot reconcile ticker data, and the local scheduler gate remains closed throughout.
        foreach (var prerequisite in _serviceProvider.GetServices<ITickerQPersistencePrerequisiteBootstrapper>())
            await prerequisite.BootstrapAsync(cancellationToken).ConfigureAwait(false);

        // Seed declarations are built and checked here, before any epoch is claimed, so a broken
        // declaration fails this host without fencing the deployment that is running.
        var activator = _serviceProvider.GetService<TickerQReconciliationActivator>()
                        ?? new TickerQReconciliationActivator(_serviceProvider, _executionContext);
        var manifestFactory = _executionContext.OptionsSeeding?.StartupManifestFactory;
        var manifest = manifestFactory == null
            ? null
            : await manifestFactory(_serviceProvider, cancellationToken).ConfigureAwait(false);
        activator.Manifest = manifest;

        if (automatic)
        {
            await StartWithAutomaticEpochAsync(internalTickerManager, schedulerOptions, activator, activationScope,
                manifest, activationGate, cancellationToken).ConfigureAwait(false);
            return;
        }

        var legacyAdoption = _executionContext.OptionsSeeding?.LegacyRuntimePartitionAdoption;
        if (legacyAdoption != null)
        {
            if (!internalTickerManager.SupportsLegacyRuntimePartitionAdoption)
                throw new NotSupportedException(
                    "Legacy runtime partition adoption was requested, but the configured persistence provider does not support it.");
            if (!legacyAdoption.TargetPartition.Equals(activationScope.RuntimePartition) ||
                legacyAdoption.Epoch != targetEpoch)
                throw new InvalidOperationException(
                    "Legacy runtime partition adoption must exactly match the bound runtime partition and reconciliation epoch.");
            await internalTickerManager.AdoptLegacyRuntimePartitionAsync(legacyAdoption, cancellationToken)
                .ConfigureAwait(false);
        }

        var begin = await BeginAsync(internalTickerManager, activationScope, targetEpoch, manifest, cancellationToken)
            .ConfigureAwait(false);
        if (TickerQReconciliationActivator.IsExact(begin, targetEpoch, Utilities.Models.ActivationEpochPhase.Activated))
        {
            // An explicit epoch skips durable mutations on a same-epoch restart. Process-local provider
            // startup still has to run for this host before its gate opens.
            if (manifest != null && begin.Manifest != null &&
                !string.Equals(begin.Manifest, manifest, StringComparison.Ordinal))
                _logger?.LogWarning(
                    "TickerQ startup inputs (functions, request contracts, or seeds) changed since reconciliation epoch {Epoch} was activated, but UseReconciliationEpoch({Epoch}) was not increased, so the changes are not applied. Increase the epoch, or remove UseReconciliationEpoch to let TickerQ manage it.",
                    targetEpoch, targetEpoch);
            await activator.RunProviderStartupActionsAsync(cancellationToken).ConfigureAwait(false);
            activationGate?.SignalActivated();
            return;
        }
        TickerQReconciliationActivator.RequireExact(begin, targetEpoch, Utilities.Models.ActivationEpochPhase.Activating, "begin");

        await activator.RunAsync(internalTickerManager, activationScope, targetEpoch, cancellationToken)
            .ConfigureAwait(false);

        // This in-process latch is opened only after every required durable startup phase succeeded.
        // There is deliberately no cancellation check after this point: reporting cancellation after
        // durable activation would create a false failed-startup signal.
        activationGate?.SignalActivated();
        }
        catch (Exception ex)
        {
            activationGate?.SignalClosed(ex);
            throw;
        }
    }

    /// <summary>
    /// Startup without a configured epoch. The startup manifest decides: the same manifest joins (or
    /// resumes) the current epoch, a new one claims the next epoch and reconciles, and one from an earlier
    /// deployment stays fenced, unless no node runs the current epoch any more, in which case it takes over.
    /// </summary>
    private async Task StartWithAutomaticEpochAsync(
        IInternalTickerManager manager, SchedulerOptionsBuilder schedulerOptions,
        TickerQReconciliationActivator activator, Utilities.Models.ReconciliationActivationScope scope,
        string manifest, ITickerQActivationGate activationGate, CancellationToken cancellationToken)
    {
        if (manifest == null)
            throw new InvalidOperationException("Automatic reconciliation epochs require a startup manifest.");

        const int maxAttempts = 16;
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            var state = await manager.GetReconciliationActivationStateAsync(scope, cancellationToken).ConfigureAwait(false);
            var decision = AutomaticEpochDecision.Decide(state, manifest);

            if (decision.Action == AutomaticEpochAction.Fenced)
            {
                if (state.Phase == Utilities.Models.ActivationEpochPhase.Activated && manager.SupportsClusterControl &&
                    !await TickerQReconciliationActivator.AnyLiveNodeAtAsync(
                        manager, state.Epoch, null, _clock.UtcNow, cancellationToken).ConfigureAwait(false))
                {
                    _logger?.LogInformation(
                        "No TickerQ node runs reconciliation epoch {Epoch} any more; this host takes over with its own startup inputs.",
                        state.Epoch);
                    decision = new AutomaticEpochDecision(AutomaticEpochAction.Claim, state.Epoch + 1);
                }
                else
                {
                    _logger?.LogWarning(
                        "This host's startup inputs belong to an earlier deployment than reconciliation epoch {Epoch}. It stays fenced and processes no jobs until it is replaced, or until no node runs epoch {Epoch} any more.",
                        state.Epoch, state.Epoch);
                    await activator.RunProviderStartupActionsAsync(cancellationToken).ConfigureAwait(false);
                    activationGate?.SignalActivated();
                    return;
                }
            }

            if (decision.Action == AutomaticEpochAction.Join)
            {
                schedulerOptions.ResolveRuntimeActivationEpoch(decision.Epoch);
                _logger?.LogInformation("TickerQ joined reconciliation epoch {Epoch}.", decision.Epoch);
                await activator.RunProviderStartupActionsAsync(cancellationToken).ConfigureAwait(false);
                activationGate?.SignalActivated();
                return;
            }

            if (decision.Action == AutomaticEpochAction.Claim)
                await activator.PreflightAsync(cancellationToken).ConfigureAwait(false);
            var begin = await manager.BeginReconciliationActivationEpochAsync(
                scope, decision.Epoch, manifest, cancellationToken).ConfigureAwait(false);
            if (begin.Epoch != decision.Epoch || !string.Equals(begin.Manifest, manifest, StringComparison.Ordinal))
                continue; // Another deployment claimed first; decide again from the new state.

            schedulerOptions.ResolveRuntimeActivationEpoch(decision.Epoch);
            if (begin.Phase == Utilities.Models.ActivationEpochPhase.Activated)
            {
                await activator.RunProviderStartupActionsAsync(cancellationToken).ConfigureAwait(false);
                activationGate?.SignalActivated();
                return;
            }

            TickerQReconciliationActivator.RequireExact(
                begin, decision.Epoch, Utilities.Models.ActivationEpochPhase.Activating, "begin");
            if (decision.Action == AutomaticEpochAction.Claim)
                _logger?.LogInformation(
                    "TickerQ startup inputs changed; reconciling under automatic epoch {Epoch}.", decision.Epoch);
            try
            {
                await activator.RunAsync(manager, scope, decision.Epoch, cancellationToken).ConfigureAwait(false);
            }
            catch (TickerQReconciliationActivator.ActivationSupersededException)
            {
                // Another deployment started at the same time and claimed a newer epoch. Its claim already
                // fences this host, so stay fenced like any host of an earlier deployment.
                _logger?.LogWarning(
                    "Another deployment claimed a newer reconciliation epoch while this host was activating epoch {Epoch}. It stays fenced and processes no jobs until no node runs the newer epoch.",
                    decision.Epoch);
                await activator.RunProviderStartupActionsAsync(cancellationToken).ConfigureAwait(false);
            }
            activationGate?.SignalActivated();
            return;
        }

        throw new InvalidOperationException(
            "TickerQ could not settle on a reconciliation epoch because other deployments kept claiming newer ones.");
    }

    private static Task<Utilities.Models.ActivationEpochState> BeginAsync(
        IInternalTickerManager manager, Utilities.Models.ReconciliationActivationScope scope, long targetEpoch,
        string manifest, CancellationToken cancellationToken)
        => manifest != null && manager.SupportsActivationManifest
            ? manager.BeginReconciliationActivationEpochAsync(scope, targetEpoch, manifest, cancellationToken)
            : manager.BeginReconciliationActivationEpochAsync(scope, targetEpoch, cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
