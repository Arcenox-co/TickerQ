using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities;

namespace TickerQ.EntityFrameworkCore.Infrastructure;

/// <summary>
/// Runs mandatory version validation and idempotent provider-owned data migrations after any
/// schema-only prerequisite bootstrap has completed and the durable activation epoch has begun.
/// </summary>
internal sealed class EfCoreStoreUpgradeBootstrapper<TContext, TTimeTicker, TCronTicker>
    : ITickerQPersistenceBootstrapper
    where TContext : DbContext
    where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
    where TCronTicker : CronTickerEntity, new()
{
    private readonly IServiceProvider _serviceProvider;
    private readonly TickerQEfCoreOptionBuilder<TTimeTicker, TCronTicker> _options;
    private readonly SchedulerOptionsBuilder _schedulerOptions;
    private readonly ILogger<EfCoreStoreUpgradeBootstrapper<TContext, TTimeTicker, TCronTicker>> _logger;

    public EfCoreStoreUpgradeBootstrapper(
        IServiceProvider serviceProvider,
        TickerQEfCoreOptionBuilder<TTimeTicker, TCronTicker> options,
        SchedulerOptionsBuilder schedulerOptions,
        ILogger<EfCoreStoreUpgradeBootstrapper<TContext, TTimeTicker, TCronTicker>> logger = null)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _schedulerOptions = schedulerOptions ?? throw new ArgumentNullException(nameof(schedulerOptions));
        _logger = logger ?? NullLogger<EfCoreStoreUpgradeBootstrapper<TContext, TTimeTicker, TCronTicker>>.Instance;
    }

    public async Task BootstrapAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _serviceProvider.CreateScope();
        var factory = scope.ServiceProvider.GetService<IDbContextFactory<TContext>>();
        if (factory != null)
        {
            await using var context = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            await UpgradeAsync(context, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var context = scope.ServiceProvider.GetRequiredService<TContext>();
            await UpgradeAsync(context, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task UpgradeAsync(TContext context, CancellationToken cancellationToken)
    {
        var partition = _schedulerOptions.RuntimePartition ?? TickerQ.Utilities.Models.TickerQRuntimePartition.LegacyGlobal;
        var pipeline = new EfCoreDataMigrationPipeline(
            [new TimeTickerChainRootDataMigration<TTimeTicker>(partition)], partition);
        await pipeline.RunAsync(context, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation(
            "TickerQ EF store is ready for {Context} at schema version {SchemaVersion}, data version {DataVersion}.",
            typeof(TContext).Name, EfCoreDataMigrationPipeline.CurrentSchemaVersion,
            TimeTickerChainRootDataMigration<TTimeTicker>.Version);
    }
}
