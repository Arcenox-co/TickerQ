namespace TickerQ.Utilities.Models.Ticker
{
    public class CronTicker : BaseTicker
    {
        public string Expression { get; set; }
        public byte[] Request { get; set; }
        public int Retries { get; set; }
        public int[] RetryIntervals { get; set; }
    }
}