namespace TickerQ.Utilities.Models
{
    public sealed class RetentionSweepResult
    {
        public RetentionSweepResult(int deletedTimeTickers, int deletedCronOccurrences, bool hasMore)
        {
            DeletedTimeTickers = deletedTimeTickers;
            DeletedCronOccurrences = deletedCronOccurrences;
            HasMore = hasMore;
        }

        public int DeletedTimeTickers { get; }
        public int DeletedCronOccurrences { get; }
        public bool HasMore { get; }
        public int Total => DeletedTimeTickers + DeletedCronOccurrences;
        public static RetentionSweepResult Empty => new(0, 0, false);
    }
}
