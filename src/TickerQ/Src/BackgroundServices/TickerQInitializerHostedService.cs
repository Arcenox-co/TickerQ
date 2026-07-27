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
using TickerQ.Utilities.Interfaces.Managers;

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
        IConfiguration configuration)
    {
        _executionContext = executionContext;
        _serviceProvider = serviceProvider;
        _configuration = configuration;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!InitializationRequested)
            return;

        TickerFunctionProvider.UpdateCronExpressionsFromIConfiguration(_configuration);
        TickerFunctionProvider.Build();

        // Persistence bootstrap (e.g. EF Core AutoMigrateDatabase) runs before any
        // seeding or scheduling query touches the store.
        foreach (var bootstrapper in _serviceProvider.GetServices<Utilities.Interfaces.ITickerQPersistenceBootstrapper>())
            await bootstrapper.BootstrapAsync(cancellationToken);

        var options = _executionContext.OptionsSeeding;

        if (options == null || options.SeedDefinedCronTickers)
        {
            await SeedDefinedCronTickers(_serviceProvider);
        }

        if (options?.TimeSeederAction != null)
        {
            await options.TimeSeederAction(_serviceProvider);
        }

        if (options?.CronSeederAction != null)
        {
            await options.CronSeederAction(_serviceProvider);
        }

        // Skip stale cron occurrences that were pending before this restart.
        // This prevents the fallback service from catching up past-due occurrences
        // from a previous application lifecycle, which would cause unexpected
        // duplicate executions at startup (see #776).
        await SkipStaleCronOccurrencesAsync(_serviceProvider, cancellationToken);

        if (_executionContext.ExternalProviderApplicationAction != null)
        {
            _executionContext.ExternalProviderApplicationAction(_serviceProvider);
            _executionContext.ExternalProviderApplicationAction = null;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static async Task SeedDefinedCronTickers(IServiceProvider serviceProvider)
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
                function, value.cronExpression, contractVersion, fingerprint, canSeed));
        }

        await internalTickerManager.MigrateDefinedCronTickers(functionsToSeed.ToArray());
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
