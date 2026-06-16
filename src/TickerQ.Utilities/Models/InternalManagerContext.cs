using System;

namespace TickerQ.Utilities.Models;

public class InternalManagerContext(Guid id)
{
    public Guid Id { get; set; } = id;
    public string FunctionName { get; set; }
    public string Expression { get; set; }
    public TimeSpan? Interval { get; set; }
    public int Retries { get; set; }
    public int[] RetryIntervals { get; set; }
    public NextCronOccurrence? NextCronOccurrence { get; set; }
    public NextPeriodicOccurrence? NextPeriodicOccurrence { get; set; }

    /// <summary>
    /// Periodic only: this item is in the group purely to keep the scheduler's wake-up time alive
    /// (so it doesn't sleep forever), but a new occurrence must NOT be materialized for it. Set when a
    /// <see cref="Enums.ChainOverlapBehavior.Skip"/> ticker already has an unfinished occurrence in
    /// flight — the next attempt is still scheduled, but overlapping materialization is suppressed.
    /// </summary>
    public bool SuppressMaterialization { get; set; }
}

public class NextCronOccurrence(Guid id, DateTime createdAt)
{
    public Guid Id { get; set; } = id;
    public DateTime CreatedAt { get; set; }
}

public class NextPeriodicOccurrence(Guid id, DateTime updatedAt)
{
    public Guid Id { get; set; } = id;
    public DateTime UpdatedAt { get; set; } = updatedAt;
}
