namespace TickerQ.Utilities.Enums
{
    /// <summary>
    /// Specifies related entities to include when querying tickers.
    /// Each persistence provider translates these to its own loading strategy
    /// (e.g., EF Core uses Include/ThenInclude, Dapper uses JOINs).
    /// </summary>
    public enum TickerRelation
    {
        /// <summary>
        /// Include child time tickers (single level).
        /// </summary>
        Children,

        /// <summary>
        /// Include child time tickers and their children (two levels deep).
        /// </summary>
        ChildrenDeep,

        /// <summary>
        /// Include the related CronTicker navigation on CronTickerOccurrenceEntity.
        /// </summary>
        CronTicker
    }
}
