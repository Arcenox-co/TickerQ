using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TickerQ.EntityFrameworkCore.Entities;
using TickerQ.Utilities.Interfaces;

namespace TickerQ.EntityFrameworkCore.Infrastructure;

/// <summary>Provider-local latch set only after the mapped outbox table is successfully probed.</summary>
internal interface IEfCoreNodeFinalizationOutboxReadiness
{
    bool IsReady { get; }
}

internal sealed class EfCoreNodeFinalizationOutboxReadiness : IEfCoreNodeFinalizationOutboxReadiness
{
    private int _ready;
    public bool IsReady => Volatile.Read(ref _ready) == 1;
    internal void MarkNotReady() => Volatile.Write(ref _ready, 0);
    internal void MarkReady() => Volatile.Write(ref _ready, 1);
}

/// <summary>
/// Enables Node dispatch only when the configured EF model and installed database schema expose the
/// exact durable finalization outbox. Probe failures do not disable ordinary EF persistence hosts.
/// </summary>
internal sealed class EfCoreNodeFinalizationOutboxReadinessProbe<TContext> : ITickerQPersistenceBootstrapper
    where TContext : DbContext
{
    private readonly IServiceProvider _serviceProvider;
    private readonly EfCoreNodeFinalizationOutboxReadiness _readiness;
    private readonly ILogger<EfCoreNodeFinalizationOutboxReadinessProbe<TContext>> _logger;

    public EfCoreNodeFinalizationOutboxReadinessProbe(
        IServiceProvider serviceProvider,
        EfCoreNodeFinalizationOutboxReadiness readiness,
        ILogger<EfCoreNodeFinalizationOutboxReadinessProbe<TContext>> logger = null)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _readiness = readiness ?? throw new ArgumentNullException(nameof(readiness));
        _logger = logger ?? NullLogger<EfCoreNodeFinalizationOutboxReadinessProbe<TContext>>.Instance;
    }

    public async Task BootstrapAsync(CancellationToken cancellationToken = default)
    {
        _readiness.MarkNotReady();
        using var scope = _serviceProvider.CreateScope();
        var factory = scope.ServiceProvider.GetService<IDbContextFactory<TContext>>();
        try
        {
            if (factory != null)
            {
                await using var context = await factory.CreateDbContextAsync(cancellationToken)
                    .ConfigureAwait(false);
                await ProbeAsync(context, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                var context = scope.ServiceProvider.GetRequiredService<TContext>();
                await ProbeAsync(context, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "TickerQ durable Node finalization outbox is not ready for {Context}. " +
                "Node dispatch remains disabled, but ordinary non-Node EF persistence remains available. " +
                "Install the required NodeFinalizationOutbox EF migration and verify the application " +
                "DbContext applies the TickerQ model configuration before enabling Node functions.",
                typeof(TContext).Name);
        }
    }

    private async Task ProbeAsync(TContext context, CancellationToken cancellationToken)
    {
        var entity = context.Model.FindEntityType(typeof(NodeFinalizationOutboxEntity));
        if (entity == null || string.IsNullOrWhiteSpace(entity.GetTableName()) ||
            entity.FindPrimaryKey()?.Properties.Count != 1 ||
            entity.FindPrimaryKey()!.Properties[0].Name != nameof(NodeFinalizationOutboxEntity.OutboxId) ||
            entity.FindProperty(nameof(NodeFinalizationOutboxEntity.CreatedAtUtcTicks)) == null ||
            entity.FindProperty(nameof(NodeFinalizationOutboxEntity.TerminalMutationDigest)) == null)
        {
            throw new InvalidOperationException(
                $"TickerQ durable Node finalization startup probe found that the {typeof(TContext).Name} model " +
                "does not contain the required NodeFinalizationOutbox mapping. Apply the TickerQ model customizer/configuration.");
        }

        // A bounded key lookup references the exact mapped table while returning no row for a valid
        // outbox (empty GUID keys are rejected by NodeFinalizationIntent). This proves schema access
        // without scanning or draining operational data.
        await context.Set<NodeFinalizationOutboxEntity>()
            .AsNoTracking()
            .Where(x => x.OutboxId == Guid.Empty)
            .Select(x => x.OutboxId)
            .Take(1)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        _readiness.MarkReady();
        _logger.LogInformation(
            "TickerQ durable Node finalization outbox is ready for {Context} ({Schema}.{Table}).",
            typeof(TContext).Name, entity.GetSchema() ?? "<default>", entity.GetTableName());
    }
}
