using System;
using System.Threading;
using System.Threading.Tasks;

namespace TickerQ.Utilities.Interfaces
{
    /// <summary>
    /// In-process reflection of the durable reconciliation activation epoch. The scheduler, fallback,
    /// and stale-recovery loops all await <see cref="WaitForActivationAsync"/> before they poll or
    /// dispatch, so no work runs until the initializer has durably activated the epoch (schema upgrade,
    /// discoverability/chain repair, defined-Cron reconciliation, and durable finalization all complete).
    ///
    /// The gate is <b>fail-closed</b>: if activation never completes — crash, cancellation, or failure —
    /// it is never opened. Awaiting loops park (they never dispatch unreconciled work) rather than
    /// treating an unfinished startup as ready.
    /// </summary>
    public interface ITickerQActivationGate
    {
        /// <summary>True once, and only once, the epoch has been durably activated.</summary>
        bool IsActivated { get; }

        /// <summary>The fail-closed reason recorded by <see cref="SignalClosed"/>, or null.</summary>
        Exception ClosedReason { get; }

        /// <summary>
        /// Completes when the epoch is activated. Honours <paramref name="cancellationToken"/>
        /// (host shutdown) by throwing <see cref="OperationCanceledException"/> so a parked loop
        /// can unwind cleanly instead of dispatching before activation.
        /// </summary>
        Task WaitForActivationAsync(CancellationToken cancellationToken = default);

        /// <summary>Opens the gate exactly once, after the durable epoch has been activated.</summary>
        void SignalActivated();

        /// <summary>Records a fail-closed reason for diagnostics. Never opens the gate.</summary>
        void SignalClosed(Exception reason);
    }
}
