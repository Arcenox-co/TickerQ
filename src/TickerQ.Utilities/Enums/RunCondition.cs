namespace TickerQ.Utilities.Enums
{
    /// <summary>
    /// Defines under what conditions a dependent ticker/job should run
    /// based on the final status of its parent.
    /// </summary>
    public enum RunCondition
    {
        /// <summary>
        /// Run only if the parent completed successfully
        /// (i.e., status is Done or DueDone).
        /// </summary>
        OnSuccess,

        /// <summary>
        /// Run only if the parent failed.
        /// </summary>
        OnFailure,

        /// <summary>
        /// Run only if the parent was cancelled by the user/system.
        /// </summary>
        OnCancelled,

        /// <summary>
        /// Run if the parent failed or was cancelled.
        /// </summary>
        OnFailureOrCancelled,

        /// <summary>
        /// Run after the parent reaches any terminal state
        /// except skipped (Done, DueDone, Failed, Cancelled).
        /// This is like "finally" semantics but excludes Skipped.
        /// </summary>
        OnAnyCompletedStatus,
        
        /// <summary>
        /// Run in parallel with the parent (when parent status is InProgress).
        /// </summary>
        InProgress,

        /// <summary>
        /// Run after the parent reaches any terminal state, including Skipped.
        /// This is the durable-chain equivalent of a finally continuation.
        /// </summary>
        OnAnyTerminalStatus,

        /// <summary>
        /// A job inside a durable group that runs when the job before the group
        /// completed successfully. Group-prefixed values keep membership separate
        /// from the condition used by post-group continuations.
        /// </summary>
        GroupOnSuccess = 100,

        /// <summary>Run this grouped job when the job before the group failed.</summary>
        GroupOnFailure,

        /// <summary>Run this grouped job when the job before the group was cancelled.</summary>
        GroupOnCancelled,

        /// <summary>Run this grouped job when the job before the group failed or was cancelled.</summary>
        GroupOnFailureOrCancelled,

        /// <summary>Run this grouped job after any non-skipped terminal status.</summary>
        GroupOnAnyCompletedStatus,

        /// <summary>Run this grouped job after any terminal status, including Skipped.</summary>
        GroupOnAnyTerminalStatus
    }
}
