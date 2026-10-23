namespace TickerQ.Utilities.Enums;

/// <summary>Action taken when an acquired execution is older than its allowed start window.</summary>
public enum MissedExecutionAction
{
    /// <summary>Execute the persisted job once when capacity becomes available.</summary>
    RunOnce = 0,

    /// <summary>Do not invoke user code after the configured lateness limit.</summary>
    Skip = 1,
}

/// <summary>How a cron definition handles slots that passed while no scheduler was available.</summary>
public enum CronCatchUpMode
{
    /// <summary>Do not materialize cron slots that were never published.</summary>
    Skip = 0,

    /// <summary>Materialize at most the latest missed slot within the configured catch-up window.</summary>
    RunLatest = 1,
}
