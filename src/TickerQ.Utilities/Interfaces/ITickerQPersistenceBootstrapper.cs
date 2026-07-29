using System.Threading;
using System.Threading.Tasks;

namespace TickerQ.Utilities.Interfaces
{
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
}
