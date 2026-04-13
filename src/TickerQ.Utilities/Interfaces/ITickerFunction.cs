using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Base;

namespace TickerQ.Utilities.Interfaces
{
    /// <summary>
    /// Marker interface for all ticker functions.
    /// Used as a constraint on MapTicker&lt;T&gt;().
    /// </summary>
    public interface ITickerFunctionBase { }

    /// <summary>
    /// Defines a ticker function without a request payload.
    /// Implement this interface and register via app.MapTicker&lt;T&gt;().
    /// </summary>
    public interface ITickerFunction : ITickerFunctionBase
    {
        Task ExecuteAsync(TickerFunctionContext context, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Defines a ticker function with a typed request payload.
    /// Implement this interface and register via app.MapTicker&lt;T&gt;().
    /// </summary>
    /// <typeparam name="TRequest">The request payload type.</typeparam>
    public interface ITickerFunction<TRequest> : ITickerFunctionBase
    {
        Task ExecuteAsync(TickerFunctionContext<TRequest> context, CancellationToken cancellationToken = default);
    }
}
