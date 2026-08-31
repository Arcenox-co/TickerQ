namespace TickerQ.Utilities.Models
{
    public readonly struct RetentionBatchResult
    {
        public RetentionBatchResult(int deleted, bool hasMore)
        {
            Deleted = deleted;
            HasMore = hasMore;
        }

        public int Deleted { get; }
        public bool HasMore { get; }
        public static RetentionBatchResult Empty => new(0, false);
    }
}
