using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
        var targetEpoch = schedulerOptions.HasRuntimeActivationScopeBinding
            ? schedulerOptions.RuntimeActivationEpoch
            : _executionContext.OptionsSeeding?.ReconciliationEpoch ?? 1;
        if (targetEpoch <= 0)
            throw new InvalidOperationException("Scheduler startup requires an explicit positive UseReconciliationEpoch value.");
        var activationScope = schedulerOptions.HasRuntimeActivationScopeBinding
            ? schedulerOptions.RuntimeActivationScope
            : new Utilities.Models.ReconciliationActivationScope(
                _executionContext.OptionsSeeding?.DefinedCronApplicationNamespace);
        if (activationScope == null)
            throw new InvalidOperationException("Scheduler startup requires a defined-Cron application namespace.");
        if ((_executionContext.OptionsSeeding?.SeedDefinedCronTickers ?? true)
            && !internalTickerManager.SupportsAuthoritativeCronReconciliation)
            throw new NotSupportedException(
                "The persistence provider does not support authoritative defined-Cron manifest reconciliation; startup stopped before invoking a lossy legacy fallback.");

        // Schema-only prerequisites (notably EF AutoMigrateDatabase) must run before durable Begin,
        // because a pre-protocol store does not yet contain the activation metadata table. This phase
        // cannot reconcile ticker data, and the local scheduler gate remains closed throughout.
        foreach (var prerequisite in _serviceProvider.GetServices<ITickerQPersistencePrerequisiteBootstrapper>())
            await prerequisite.BootstrapAsync(cancellationToken).ConfigureAwait(false);

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

        var begin = await internalTickerManager.BeginReconciliationActivationEpochAsync(
            activationScope, targetEpoch, cancellationToken).ConfigureAwait(false);
        if (IsExact(begin, targetEpoch, Utilities.Models.ActivationEpochPhase.Activated))
        {
            // Without durable manifest identity, the only safe same-epoch restart is to skip durable
            // mutations. Process-local provider startup still has to run for this host before its gate opens.
            foreach (var readiness in _serviceProvider.GetServices<ITickerQPersistenceReadinessProbe>())
                await readiness.ProbeAsync(cancellationToken).ConfigureAwait(false);
            RunExternalProviderApplicationAction();
            activationGate?.SignalActivated();
            return;
        }
        RequireExact(begin, targetEpoch, Utilities.Models.ActivationEpochPhase.Activating, "begin");

        // Provider bootstrap runs before any migration, reconciliation, seeding, or scheduler query.
        foreach (var bootstrapper in _serviceProvider.GetServices<ITickerQPersistenceBootstrapper>())
            await bootstrapper.BootstrapAsync(cancellationToken).ConfigureAwait(false);
        await CheckpointAsync(internalTickerManager, activationScope, targetEpoch,
            "01-bootstrap-complete", cancellationToken).ConfigureAwait(false);

        await internalTickerManager.RepairTimeTickerChainsAsync(cancellationToken).ConfigureAwait(false);
        await CheckpointAsync(internalTickerManager, activationScope, targetEpoch,
            "02-chain-repair-complete", cancellationToken).ConfigureAwait(false);

        var options = _executionContext.OptionsSeeding;
        if (options == null || options.SeedDefinedCronTickers)
        {
            using var startupAdmission = Utilities.Models.StartupSeederAdmissionContext.Enter(
                activationScope.ScopeKey, targetEpoch);
            await SeedDefinedCronTickers(_serviceProvider, cancellationToken).ConfigureAwait(false);
        }
        await CheckpointAsync(internalTickerManager, activationScope, targetEpoch,
            "03-definition-reconciliation-complete", cancellationToken).ConfigureAwait(false);

        // Constraints/readiness that depend on reconciled data complete before consumer seeders.
        foreach (var finalizer in _serviceProvider.GetServices<ITickerQPersistenceFinalizer>())
            await finalizer.FinalizeAsync(cancellationToken).ConfigureAwait(false);
        await CheckpointAsync(internalTickerManager, activationScope, targetEpoch,
            "04-finalization-complete", cancellationToken).ConfigureAwait(false);

        if (options?.TimeSeederAction != null || options?.CronSeederAction != null)
        {
            using var startupAdmission = Utilities.Models.StartupSeederAdmissionContext.Enter(
                activationScope.ScopeKey, targetEpoch);
            if (options.TimeSeederAction != null)
                await options.TimeSeederAction(_serviceProvider, cancellationToken).ConfigureAwait(false);
            if (options.CronSeederAction != null)
                await options.CronSeederAction(_serviceProvider, cancellationToken).ConfigureAwait(false);
        }
        await CheckpointAsync(internalTickerManager, activationScope, targetEpoch,
            "05-user-seeding-complete", cancellationToken).ConfigureAwait(false);

        // Prevent restart catch-up of stale pending cron occurrences (#776).
        await SkipStaleCronOccurrencesAsync(_serviceProvider, cancellationToken).ConfigureAwait(false);
        await CheckpointAsync(internalTickerManager, activationScope, targetEpoch,
            "06-stale-occurrence-handling-complete", cancellationToken).ConfigureAwait(false);

        foreach (var readiness in _serviceProvider.GetServices<ITickerQPersistenceReadinessProbe>())
            await readiness.ProbeAsync(cancellationToken).ConfigureAwait(false);
        RunExternalProviderApplicationAction();
        await CheckpointAsync(internalTickerManager, activationScope, targetEpoch,
            "07-provider-startup-actions-complete", cancellationToken).ConfigureAwait(false);

        var committed = await internalTickerManager.CommitReconciliationActivationEpochAsync(
            activationScope, targetEpoch, cancellationToken).ConfigureAwait(false);
        RequireExact(committed, targetEpoch, Utilities.Models.ActivationEpochPhase.Activated, "commit");

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

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private void RunExternalProviderApplicationAction()
    {
        var action = _executionContext.ExternalProviderApplicationAction;
        if (action == null)
            return;

        action(_serviceProvider);
        _executionContext.ExternalProviderApplicationAction = null;
    }

    private static async Task CheckpointAsync(
        IInternalTickerManager manager, Utilities.Models.ReconciliationActivationScope scope,
        long targetEpoch, string checkpoint, CancellationToken cancellationToken)
    {
        var state = await manager.AdvanceReconciliationCheckpointAsync(
            scope, targetEpoch, checkpoint, cancellationToken).ConfigureAwait(false);
        RequireExact(state, targetEpoch, Utilities.Models.ActivationEpochPhase.Activating, "checkpoint");
    }

    private static bool IsExact(Utilities.Models.ActivationEpochState state, long epoch,
        Utilities.Models.ActivationEpochPhase phase)
        => state != null && state.Epoch == epoch && state.Phase == phase;

    private static void RequireExact(Utilities.Models.ActivationEpochState state, long epoch,
        Utilities.Models.ActivationEpochPhase phase, string operation)
    {
        if (!IsExact(state, epoch, phase))
            throw new InvalidOperationException(
                $"Reconciliation activation {operation} returned '{state?.ToString() ?? "<null>"}'; " +
                $"expected exact epoch={epoch}, phase={phase}. Startup remains fail-closed.");
    }

    private static async Task SeedDefinedCronTickers(IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        var internalTickerManager = serviceProvider.GetRequiredService<IInternalTickerManager>();

        var descriptors = TickerFunctionProvider.TickerFunctionDescriptors;

        var functionsToSeed = new List<Utilities.Models.DefinedCronTickerSeed>();
        foreach (var (function, value) in TickerFunctionProvider.TickerFunctions)
        {
            if (string.IsNullOrEmpty(value.cronExpression))
                continue;

            // Resolve the authoritative contract identity for this code-defined cron so seeded rows
            // carry it and stay under execution-time drift enforcement (Build() has already run, so a
            // descriptor exists for every executable function; request-less functions have Request == null).
            int? contractVersion = null;
            string fingerprint = null;
            var canSeed = true;
            if (descriptors.TryGetValue(function, out var descriptor))
            {
                var contract = descriptor.Request;

                // Code-defined seeds have no payload. Required contracts cannot be satisfied, and an
                // incomplete optional identity cannot participate in drift enforcement. Skip both
                // rather than persist a row that is either invalid or only partially authoritative.
                canSeed = contract is not { Required: true }
                    && (contract == null || !string.IsNullOrWhiteSpace(contract.Fingerprint));

                contractVersion = descriptor.ContractVersion;
                fingerprint = contract?.Fingerprint;
            }

            functionsToSeed.Add(new Utilities.Models.DefinedCronTickerSeed(
                function, value.cronExpression, contractVersion, fingerprint, canSeed,
                stableDefinitionId: function, retries: 0, retryIntervals: null, timeoutSeconds: null));
        }

        var options = serviceProvider.GetRequiredService<TickerExecutionContext>().OptionsSeeding;
        var applicationNamespace = options?.DefinedCronApplicationNamespace;
        if (applicationNamespace == null)
            await internalTickerManager.MigrateDefinedCronTickers(functionsToSeed.ToArray(), cancellationToken);
        else
        {
            var ownership = options.LegacyDefinedCronOwnership == null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : new Dictionary<string, string>(options.LegacyDefinedCronOwnership, StringComparer.Ordinal);
            if (options.AdoptAllLegacyDefinedCronTickers)
                foreach (var seed in functionsToSeed)
                    ownership.TryAdd(seed.Function, applicationNamespace);

            await internalTickerManager.MigrateDefinedCronTickers(
                new Utilities.Models.DefinedCronSeedManifest(applicationNamespace, functionsToSeed, ownership), cancellationToken);
        }
    }

    private static async Task SkipStaleCronOccurrencesAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        var schedulerOptions = serviceProvider.GetRequiredService<SchedulerOptionsBuilder>();
        if (schedulerOptions.StaleCronOccurrenceThreshold <= TimeSpan.Zero)
            return;

        var internalTickerManager = serviceProvider.GetRequiredService<IInternalTickerManager>();
        await internalTickerManager.SkipStaleCronOccurrencesAsync(schedulerOptions.StaleCronOccurrenceThreshold, cancellationToken);
    }
}
