using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Models;

namespace TickerQ.Utilities.Interfaces
{
    public interface ITickerExecutionTaskHandler
    {
        Task ExecuteTaskAsync(InternalFunctionContext context, bool isDue, CancellationToken cancellationToken = default);

        /// <summary>
        /// Overload for callers that registered the ticker with the cancellation manager at
        /// acquisition time (the caller owns removal/disposal of <paramref name="registeredSource"/>).
        /// The built-in handler reuses the supplied source for the root execution instead of
        /// self-registering. Default implementation forwards to the token-only overload so existing
        /// third-party implementers keep compiling and behaving exactly as before.
        /// </summary>
        Task ExecuteRegisteredTaskAsync(InternalFunctionContext context, bool isDue,
            CancellationTokenSource registeredSource, CancellationToken cancellationToken = default)
            => ExecuteTaskAsync(context, isDue, cancellationToken);
    }
}

