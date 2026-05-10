using System;

namespace TickerQ.Utilities.Exceptions
{
    /// <summary>
    /// Signals that a ticker can't run right now because its target SDK node
    /// is offline, and the framework should record the run as <c>Skipped</c>
    /// (with this exception's message as the SkippedReason) instead of
    /// <c>Failed</c>. Throwing this skips the user's <c>Retries</c> budget —
    /// retrying is pointless while the node is down.
    ///
    /// Lives in <see cref="TickerQ.Utilities"/> rather than <see cref="TickerQ"/>
    /// so the RemoteExecutor (which references Utilities, not the core
    /// scheduler) can throw it from its dispatch delegate without taking a
    /// new project reference.
    /// </summary>
    public sealed class SdkOfflineSkipException : Exception
    {
        public SdkOfflineSkipException(string message) : base(message) { }
    }
}
