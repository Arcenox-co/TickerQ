namespace TickerQ.Utilities.Models
{
    /// <summary>
    /// Progress from one bounded retention-index reconciliation step. Redis persists the
    /// underlying scan cursor atomically, so a later process can resume after a crash.
    /// </summary>
    public sealed class RetentionIndexReconciliationResult
    {
        public RetentionIndexReconciliationResult(int examined, bool hasMore, string nextCursor)
        {
            Examined = examined;
            HasMore = hasMore;
            NextCursor = nextCursor;
        }

        public int Examined { get; }
        public bool HasMore { get; }
        public string NextCursor { get; }

        public static RetentionIndexReconciliationResult Completed { get; } =
            new RetentionIndexReconciliationResult(0, false, "completed");
    }
}
