namespace TickerQ.Utilities.Enums
{
    /// <summary>Why an execution was cancelled. Reported in logs and the stored exception details.</summary>
    internal enum TickerCancellationCause
    {
        /// <summary>Requested from the dashboard or a manager, on this node or forwarded from another.</summary>
        Request,
        /// <summary>The function called <c>RequestCancellation</c> on its own context.</summary>
        Function,
        /// <summary>The node lost the execution's lease to another node.</summary>
        LeaseLost,
        /// <summary>The execution ran past its timeout.</summary>
        Timeout,
        /// <summary>The node stopped before the execution finished.</summary>
        Shutdown,
        /// <summary>The function threw a cancellation exception that nothing in TickerQ requested.</summary>
        FunctionException
    }

    internal static class TickerCancellationReasons
    {
        internal const string BeforeExecution = "Cancelled before execution.";
        internal const string WhileRunning = "Task was cancelled";

        /// <summary>
        /// What a pending job cancelled through a manager or the dashboard records. It differs from
        /// <see cref="BeforeExecutionBecause"/>, which a node writes for work it had already picked up.
        /// </summary>
        internal static readonly string PendingRequest =
            $"Cancelled before any node picked it up: {Clause(TickerCancellationCause.Request)}.";

        internal static string BeforeExecutionBecause(TickerCancellationCause? cause)
            => cause is { } known ? $"Cancelled before execution: {Clause(known)}." : BeforeExecution;

        internal static string WhileRunningBecause(TickerCancellationCause? cause)
            => cause is { } known ? $"Cancelled: {Clause(known)}." : WhileRunning;

        private static string Clause(TickerCancellationCause cause) => cause switch
        {
            TickerCancellationCause.Request => "requested from the dashboard or a manager",
            TickerCancellationCause.Function => "requested by the function itself",
            TickerCancellationCause.LeaseLost => "the lease was lost to another node",
            TickerCancellationCause.Timeout => "the execution timed out",
            TickerCancellationCause.Shutdown => "the node stopped before it finished",
            _ => "the function threw a cancellation exception that TickerQ did not request"
        };
    }
}
