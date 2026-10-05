namespace TickerQ.Utilities.Models
{
    /// <summary>
    /// Aggregate outcome of one retention sweep across both record kinds: time-ticker chains and
    /// cron-ticker occurrences. Reported by the provider-agnostic manager so the maintenance loop
    /// can log counts and decide whether another batch is warranted.
    /// </summary>
    public sealed class RetentionSweepResult
    {
        public RetentionSweepResult(int deletedTimeTickers, int deletedCronOccurrences, bool hasMore)
        {
            DeletedTimeTickers = deletedTimeTickers;
            DeletedCronOccurrences = deletedCronOccurrences;
            HasMore = hasMore;
        }

        /// <summary>Rows deleted from time-ticker chains this batch.</summary>
        public int DeletedTimeTickers { get; }

        /// <summary>Cron-ticker occurrence rows deleted this batch.</summary>
        public int DeletedCronOccurrences { get; }

        /// <summary>True when either record kind reported more eligible aggregates remaining.</summary>
        public bool HasMore { get; }

        /// <summary>Total rows deleted this batch across both record kinds.</summary>
        public int Total => DeletedTimeTickers + DeletedCronOccurrences;

        public static RetentionSweepResult Empty => new RetentionSweepResult(0, 0, false);
    }
}
