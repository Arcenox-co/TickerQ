using System.Threading;
using System.Threading.Tasks;

namespace TickerQ.Utilities.Interfaces
{
    /// <summary>
    /// Completes persistence readiness after framework-owned definition and data reconciliation,
    /// but before user seeders or scheduler services can touch the store. Providers use this phase
    /// for constraints that require legacy data to converge first, such as MongoDB's unique SeedKey index.
    /// </summary>
    public interface ITickerQPersistenceFinalizer
    {
        Task FinalizeAsync(CancellationToken cancellationToken = default);
    }
}
