using System;

namespace TickerQ.Utilities.Exceptions
{
    /// <summary>
    /// Thrown when a successful terminal write that carries a published result did NOT win ownership
    /// fencing at the persistence layer — the fenced/stale-token write affected zero rows or the result
    /// was not durably stored. The execution runtime must treat this as an unacknowledged finalization:
    /// it must not notify success and must not release/queue this ticker's deferred children, because the
    /// durable parent result they would read never landed. Another owner remains responsible for the row.
    /// </summary>
    public sealed class TickerResultNotAcknowledgedException : Exception
    {
        public TickerResultNotAcknowledgedException(string message) : base(message) { }
    }
}
