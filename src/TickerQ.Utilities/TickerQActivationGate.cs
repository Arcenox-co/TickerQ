using System;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Interfaces;

namespace TickerQ.Utilities
{
    /// <summary>
    /// Default <see cref="ITickerQActivationGate"/>. A single <see cref="TaskCompletionSource"/> latch
    /// that opens once via <see cref="SignalActivated"/>. It cannot be closed again once opened, and it
    /// never opens on its own, which is what makes the scheduler fail-closed during a failed startup.
    /// </summary>
    public sealed class TickerQActivationGate : ITickerQActivationGate
    {
        private readonly TaskCompletionSource _activated =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private volatile Exception _closedReason;

        public bool IsActivated => _activated.Task.IsCompletedSuccessfully;

        public Exception ClosedReason => _closedReason;

        public async Task WaitForActivationAsync(CancellationToken cancellationToken = default)
        {
            if (_activated.Task.IsCompletedSuccessfully)
                return;

            cancellationToken.ThrowIfCancellationRequested();

            var cancelTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using (cancellationToken.Register(static state => ((TaskCompletionSource)state).TrySetResult(), cancelTcs))
            {
                await Task.WhenAny(_activated.Task, cancelTcs.Task).ConfigureAwait(false);
            }

            if (_activated.Task.IsCompletedSuccessfully)
                return;

            cancellationToken.ThrowIfCancellationRequested();
        }

        public void SignalActivated() => _activated.TrySetResult();

        public void SignalClosed(Exception reason) => _closedReason = reason;
    }
}
