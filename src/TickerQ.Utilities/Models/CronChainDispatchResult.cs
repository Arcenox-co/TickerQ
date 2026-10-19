namespace TickerQ.Utilities.Models;

public enum CronChainDispatchResult
{
    NotConfigured = 0,
    Created = 1,
    AlreadyCreated = 2,
    Skipped = 3,
    LostOwnership = 4
}
