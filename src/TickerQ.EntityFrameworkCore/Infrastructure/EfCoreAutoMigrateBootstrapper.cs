using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using TickerQ.Utilities.Interfaces;

namespace TickerQ.EntityFrameworkCore.Infrastructure;

/// <summary>
/// Applies pending EF Core migrations for the TickerQ DbContext at host startup,
/// before seeding/scheduling touch the store. Registered by
/// <c>efOptions.AutoMigrateDatabase()</c> — strictly opt-in, since teams that
/// migrate via CI/CD pipelines must not have the app mutate schema on boot.
/// </summary>
internal sealed class EfCoreAutoMigrateBootstrapper<TContext> : ITickerQPersistenceBootstrapper
    where TContext : DbContext
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<EfCoreAutoMigrateBootstrapper<TContext>> _logger;

    public EfCoreAutoMigrateBootstrapper(
        IServiceProvider serviceProvider,
        ILogger<EfCoreAutoMigrateBootstrapper<TContext>> logger = null)
    {
        _serviceProvider = serviceProvider;
        _logger = logger ?? NullLogger<EfCoreAutoMigrateBootstrapper<TContext>>.Instance;
    }

    public async Task BootstrapAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _serviceProvider.CreateScope();

        // UseTickerQDbContext registers a pooled factory; UseApplicationDbContext
        // relies on the app's own scoped registration. Support both.
        var factory = scope.ServiceProvider.GetService<IDbContextFactory<TContext>>();
        if (factory != null)
        {
            await using var dbContext = await factory.CreateDbContextAsync(cancellationToken);
            await MigrateAsync(dbContext, cancellationToken);
        }
        else
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<TContext>();
            await MigrateAsync(dbContext, cancellationToken);
        }
    }

    private async Task MigrateAsync(TContext dbContext, CancellationToken cancellationToken)
    {
        _logger.LogInformation("TickerQ auto-migrate: applying pending migrations for {Context}…", typeof(TContext).Name);
        await dbContext.Database.MigrateAsync(cancellationToken);
        _logger.LogInformation("TickerQ auto-migrate: {Context} is up to date", typeof(TContext).Name);
    }
}
