namespace TickerQ.Utilities.Enums
{
    public enum TickerTaskPriority
    {
        /// <summary>
        /// Uses the normal-priority queue and starts the delegate on a dedicated thread.
        /// Async continuations may run on the CLR thread pool. Counts toward global concurrency.
        /// </summary>
        LongRunning,
        /// <summary>
        /// Receives four turns per weighted scheduling cycle while all priority queues have work.
        /// </summary>
        High,
        /// <summary>
        /// Receives two turns per weighted scheduling cycle while all priority queues have work.
        /// </summary>
        Normal,
        /// <summary>
        /// Receives one turn per weighted scheduling cycle, preventing starvation by higher priorities.
        /// </summary>
        Low
    }
}
