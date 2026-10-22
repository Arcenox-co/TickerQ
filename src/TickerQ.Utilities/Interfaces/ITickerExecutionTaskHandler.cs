using System;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Models;

namespace TickerQ.Utilities.Interfaces
{
    public interface ITickerExecutionTaskHandler
    {
        /// <summary>
        /// Executes a root and its eligible descendants. Implementations own per-function
        /// concurrency at the invocation boundary, sharing ITickerFunctionConcurrencyGate
        /// across roots and children; dispatchers do not hold a permit around the entire tree.
        /// </summary>
        Task ExecuteTaskAsync(InternalFunctionContext context, bool isDue, CancellationToken cancellationToken = default);

        /// <summary>
        /// Executes one remotely dispatched worker function, including its retry policy, without
        /// performing scheduler lifecycle persistence. The scheduler remains the sole authority that
        /// maps the immutable terminal outcome onto its acquired, generation-fenced context.
        /// The SDK transport owns the worker's per-function concurrency permit.
        /// </summary>
        Task<TickerWorkerExecutionResult> ExecuteWorkerTaskAsync(
            InternalFunctionContext context, bool isDue, CancellationToken cancellationToken = default)
            => Task.FromException<TickerWorkerExecutionResult>(new NotSupportedException(
                "This execution handler does not support the worker-only execution primitive."));

        /// <summary>
        /// Executes an in-memory test graph using normal chain conditions, retries, timeouts and
        /// parent-result propagation. No ticker, progress, result or execution-log rows are written.
        /// The supplied contexts are updated in place so the caller can present each step outcome.
        /// </summary>
        Task ExecuteTestChainAsync(
            InternalFunctionContext context, CancellationToken cancellationToken = default)
            => Task.FromException(new NotSupportedException(
                "This execution handler does not support ephemeral chain tests."));

        /// <summary>
        /// Overload for callers that registered the ticker with the cancellation manager at
        /// acquisition time (the caller owns removal/disposal of <paramref name="registeredSource"/>).
        /// The built-in handler reuses the supplied source for the root execution instead of
        /// self-registering. Default implementation forwards to the token-only overload so existing
        /// third-party implementers keep compiling. The handler owns per-function concurrency
        /// as described by ExecuteTaskAsync; the caller owns the root's cancellation registration.
        /// </summary>
        Task ExecuteRegisteredTaskAsync(InternalFunctionContext context, bool isDue,
            CancellationTokenSource registeredSource, CancellationToken cancellationToken = default)
            => ExecuteTaskAsync(context, isDue, cancellationToken);
    }
}

