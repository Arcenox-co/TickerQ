namespace TickerQ.Utilities.Models
{
    /// <summary>
    /// Outcome of a single bounded provider retention delete call: how many rows it removed, and
    /// whether more eligible aggregates remain beyond this batch so the maintenance loop can decide
    /// whether to issue another batch (up to its per-sweep cap).
    /// </summary>
    public readonly struct RetentionBatchResult
    {
        public RetentionBatchResult(int deleted, bool hasMore)
        {
            Deleted = deleted;
            HasMore = hasMore;
        }

        /// <summary>Number of rows deleted by this batch (nodes across whole chains, for time tickers).</summary>
        public int Deleted { get; }

        /// <summary>True when more eligible aggregates remained after this batch's bound was reached.</summary>
        public bool HasMore { get; }

        public static RetentionBatchResult Empty => new RetentionBatchResult(0, false);
    }
}
