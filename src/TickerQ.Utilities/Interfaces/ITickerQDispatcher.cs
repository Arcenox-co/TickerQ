using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Models;

namespace TickerQ.Utilities.Interfaces
{
    public interface ITickerQDispatcher
    {
        Task DispatchAsync(InternalFunctionContext[] contexts, CancellationToken cancellationToken = default);
    }
}

