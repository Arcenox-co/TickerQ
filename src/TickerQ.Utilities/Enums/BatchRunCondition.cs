namespace TickerQ.Utilities.Enums
{
    public enum RunCondition
    {
        /// <summary>
        /// Job will execute even if the parent job failed, Cancelled or Succeeded.
        /// </summary>
        OnAnyCompletedStatus,

        /// <summary>
        /// Job will only execute when parent job succeeds.
        /// </summary>
        OnSuccess
    }
}