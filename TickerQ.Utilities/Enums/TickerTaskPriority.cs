namespace TickerQ.Utilities.Enums
{
    public enum TickerTaskPriority
    {
        /// <summary>
        /// Long running tasks are executed in a separate thread.
        /// Using Default TaskScheduler.
        /// </summary>
        LongRunning,
        /// <summary>
        /// High Priority Tasks are executed first.
        /// Using TickerTaskScheduler
        /// </summary>
        High,
        /// <summary>
        /// Normal Priority Tasks are executed after high priority tasks.
        /// Using TickerTaskScheduler
        /// </summary>
        Normal,
        /// <summary>
        /// Low Priority Tasks are executed last.
        /// Using TickerTaskScheduler
        /// </summary>
        Low
    }
}
