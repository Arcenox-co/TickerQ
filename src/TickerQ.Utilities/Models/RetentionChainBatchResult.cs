namespace TickerQ.Utilities.Models
{
    public readonly struct RetentionChainBatchResult
    {
        public RetentionChainBatchResult(int deleted, bool hasMore, RetentionCursor nextCursor)
        {
            Deleted = deleted;
            HasMore = hasMore;
            NextCursor = nextCursor;
        }

        public int Deleted { get; }
        public bool HasMore { get; }
        public RetentionCursor NextCursor { get; }
        public static RetentionChainBatchResult Empty => new(0, false, RetentionCursor.Start);
    }
}
