namespace TickerQ.Utilities.DashboardDtos
{
    internal class CronTickerDto : BaseTickerDto
    {
        public string Expression { get; set; }
        public string RequestType { get; set; }
        public string Description { get; set; }
        public int Retries { get; set; }
        public int[] RetryIntervals { get; set; }
        public string InitIdentifier { get; set; }
        public string ExpressionReadable { get; set; }
    }
}