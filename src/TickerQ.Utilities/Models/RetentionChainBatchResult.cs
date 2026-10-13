namespace TickerQ.Utilities.Models
{
    /// <summary>
    /// Outcome of one bounded time-chain retention batch: rows removed, whether more candidate roots
    /// remain beyond the examined window, and the keyset cursor to resume from. When traversal reaches the
    /// end, <see cref="NextCursor"/> is <see cref="RetentionCursor.Start"/> so a later sweep wraps around.
    /// </summary>
    public readonly struct RetentionChainBatchResult
    {
        public RetentionChainBatchResult(int deleted, bool hasMore, RetentionCursor nextCursor)
        {
            Deleted = deleted;
            HasMore = hasMore;
            NextCursor = nextCursor;
        }

        /// <summary>Number of rows deleted this batch (nodes across whole, fully-eligible chains).</summary>
        public int Deleted { get; }

        /// <summary>True when more candidate roots remained after the examined window.</summary>
        public bool HasMore { get; }

        /// <summary>Keyset position to pass to the next call; <see cref="RetentionCursor.Start"/> on wrap.</summary>
        public RetentionCursor NextCursor { get; }

        public static RetentionChainBatchResult Empty => new RetentionChainBatchResult(0, false, RetentionCursor.Start);
    }
}
