namespace TickerQ.Utilities.Enums
{
    public enum TickerQStartMode
    {
        /// <summary>
        /// Start job processing immediately when UseTickerQ is called.
        /// Background services are registered and start automatically.
        /// </summary>
        Immediate,
        
        /// <summary>
        /// Background services are registered but skip the first run.
        /// Job processing needs to be started manually via ITickerQHostScheduler.
        /// </summary>
        Manual
    }
}