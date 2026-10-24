namespace TickerQ.Utilities.Enums;

/// <summary>Action taken when a schedule policy blocks a requested execution time.</summary>
public enum BlockedOccurrenceBehavior
{
    /// <summary>Reject creation or update instead of silently changing the requested time.</summary>
    Reject = 0,

    /// <summary>Keep the occurrence as an auditable skipped execution.</summary>
    Skip = 1,

    /// <summary>Move a one-time ticker to the first instant allowed by the policy.</summary>
    MoveForward = 2,
}
