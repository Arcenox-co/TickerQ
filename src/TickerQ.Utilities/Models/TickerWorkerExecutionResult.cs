using System;
using TickerQ.Utilities.Enums;

namespace TickerQ.Utilities.Models
{
    /// <summary>
    /// Immutable terminal outcome produced by the worker-only execution primitive.
    /// It contains no scheduler ownership identity and must never be persisted by the worker.
    /// </summary>
    public sealed class TickerWorkerExecutionResult
    {
        public TickerWorkerExecutionResult(
            TickerStatus status,
            string error,
            TickerResultEnvelope resultEnvelope)
        {
            if (status is not (TickerStatus.Done or TickerStatus.DueDone or TickerStatus.Failed
                or TickerStatus.Cancelled or TickerStatus.Skipped))
                throw new ArgumentOutOfRangeException(
                    nameof(status), status, "Worker execution outcomes must be terminal.");

            Status = status;
            Error = error;
            ResultEnvelope = status is TickerStatus.Done or TickerStatus.DueDone
                ? resultEnvelope
                : null;
        }

        public TickerStatus Status { get; }
        public string Error { get; }
        public TickerResultEnvelope ResultEnvelope { get; }
        public bool Success => Status is TickerStatus.Done or TickerStatus.DueDone;
        public bool Cancelled => Status == TickerStatus.Cancelled;
    }
}
