using System;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Models;

namespace TickerQ.Utilities.Interfaces
{
    public interface ITickerExecutionTaskHandler
    {
        Task ExecuteTaskAsync(InternalFunctionContext context, bool isDue, CancellationToken cancellationToken = default);

        /// <summary>
        /// Executes one remotely dispatched worker function, including its retry policy, without
        /// performing scheduler lifecycle persistence. The scheduler remains the sole authority that
        /// maps the immutable terminal outcome onto its acquired, generation-fenced context.
        /// </summary>
        Task<TickerWorkerExecutionResult> ExecuteWorkerTaskAsync(
            InternalFunctionContext context, bool isDue, CancellationToken cancellationToken = default)
            => Task.FromException<TickerWorkerExecutionResult>(new NotSupportedException(
                "This execution handler does not support the worker-only execution primitive."));

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

