namespace TickerQ.Utilities.Enums
{
    /// <summary>
    /// Controls what happens when a periodic ticker with a chain template fires again
    /// while a previously materialized chain from the same periodic ticker is still running.
    /// </summary>
    public enum ChainOverlapBehavior
    {
        /// <summary>
        /// Always materialize a new, independent chain on every fire (default).
        /// Multiple chains from the same periodic ticker may run concurrently.
        /// </summary>
        Allow,

        /// <summary>
        /// Skip materializing a new chain if a previously materialized chain from the
        /// same periodic ticker has not yet reached a terminal state.
        /// </summary>
        Skip
    }
}

