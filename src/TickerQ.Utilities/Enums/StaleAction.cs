namespace TickerQ.Utilities.Enums
{
    /// <summary>
    /// What the stale-job watchdog does with an <c>InProgress</c> ticker whose
    /// lease expired (the node running it died or stopped renewing).
    /// </summary>
    public enum StaleAction
    {
        /// <summary>
        /// Reset the ticker to Idle so any node can re-acquire and run it again.
        /// At-least-once semantics: the function may have partially executed on
        /// the dead node, so it should be idempotent. Bounded by
        /// <c>SchedulerOptionsBuilder.MaxStaleRestarts</c> — once exceeded the
        /// ticker falls through to Cancel to avoid poison-job crash loops.
        /// </summary>
        Restart = 0,

        /// <summary>
        /// Mark the ticker Cancelled with a stale reason. Use for jobs that must
        /// never run twice (non-idempotent work like payments).
        /// </summary>
        Cancel = 1
    }
}
