using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Models;

namespace TickerQ.Utilities.Interfaces
{
    public interface ITickerQDispatcher
    {
        /// <summary>
        /// Indicates whether the dispatcher is functional (background services enabled).
        /// When false, DispatchAsync will be a no-op.
        /// </summary>
        bool IsEnabled { get; }
        
        Task DispatchAsync(InternalFunctionContext[] contexts, CancellationToken cancellationToken = default);
    }
}

