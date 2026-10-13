using System;

namespace TickerQ.Utilities.Exceptions
{
    /// <summary>
    /// An authenticated remote endpoint proved that the exact dispatch generation was
    /// rejected before user code or registry execution could begin.
    /// </summary>
    public sealed class RemoteExecutionNotStartedException : Exception
    {
        internal RemoteExecutionNotStartedException(string message) : base(message)
        {
            HasAuthenticatedNotStartedProof = true;
        }

        internal bool HasAuthenticatedNotStartedProof { get; }
    }
}
