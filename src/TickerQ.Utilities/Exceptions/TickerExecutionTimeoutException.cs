using System;

namespace TickerQ.Utilities.Exceptions
{
    /// <summary>
    /// Thrown internally by the execution task handler when a ticker function
    /// exceeds its per-attempt execution timeout and did not react to the
    /// cancellation signal within the grace period — the execution is abandoned
    /// (its task keeps running unobserved) and the ticker lands on Cancelled
    /// with the timeout reason.
    /// </summary>
    public sealed class TickerExecutionTimeoutException : Exception
    {
        public TickerExecutionTimeoutException(string message) : base(message)
        {
        }
    }
}
