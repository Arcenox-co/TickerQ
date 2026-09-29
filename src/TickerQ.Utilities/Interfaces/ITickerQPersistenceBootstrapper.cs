using System.Threading;
using System.Threading.Tasks;

namespace TickerQ.Utilities.Interfaces
{
    /// <summary>
    /// Schema-only preparation that must complete before a provider can persist the durable
    /// reconciliation epoch. Implementations must not reconcile or mutate ticker data.
    /// The process-local activation gate remains closed while this phase runs.
    /// </summary>
    public interface ITickerQPersistencePrerequisiteBootstrapper
    {
        Task BootstrapAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// One-time persistence preparation run by the TickerQ initializer at host
    /// startup, before any seeding or scheduling touches the store. Providers
    /// register implementations for things like schema migration — e.g. the EF
    /// Core package's <c>AutoMigrateDatabase()</c> opt-in.
    /// </summary>
    public interface ITickerQPersistenceBootstrapper
    {
        Task BootstrapAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Repeatable, process-local readiness work. Unlike durable bootstrap, every scheduler process runs
    /// these probes on every startup, including followers of an already activated epoch.
    /// </summary>
    public interface ITickerQPersistenceReadinessProbe
    {
        Task ProbeAsync(CancellationToken cancellationToken = default);
    }
}
