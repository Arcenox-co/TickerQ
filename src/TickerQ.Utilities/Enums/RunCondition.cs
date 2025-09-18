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
        InProgress
    }
}