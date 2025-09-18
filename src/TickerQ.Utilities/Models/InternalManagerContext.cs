using System;

namespace TickerQ.Utilities.Models;

public class InternalManagerContext(Guid id)
{
    public Guid Id { get; set; } = id;
    public string FunctionName { get; set; }
    public string Expression { get; set; }
    public int Retries { get; set; }
    public int[] RetryIntervals { get; set; }
    public NextCronOccurrence NextCronOccurrence { get; set; }
}

public class NextCronOccurrence(Guid id, DateTime createdAt)
{
    public Guid Id { get; set; } = id;
    public DateTime CreatedAt { get; set; }
}