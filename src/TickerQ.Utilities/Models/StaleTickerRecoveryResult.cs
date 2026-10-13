namespace TickerQ.Utilities.Models
{
    /// <summary>Counts of what one stale-job watchdog sweep recovered.</summary>
    public class StaleTickerRecoveryResult
    {
        public int RestartedTimeTickers { get; set; }
        public int CancelledTimeTickers { get; set; }
        public int RestartedCronOccurrences { get; set; }
        public int CancelledCronOccurrences { get; set; }

        public int Total => RestartedTimeTickers + CancelledTimeTickers
                            + RestartedCronOccurrences + CancelledCronOccurrences;
    }
}
