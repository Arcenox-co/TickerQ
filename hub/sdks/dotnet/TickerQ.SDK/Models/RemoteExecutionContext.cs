using TickerQ.Utilities.Enums;

namespace TickerQ.SDK.Models;

public class RemoteExecutionContext
{
    public Guid Id { get; set; }
    public TickerType Type { get; set; }
    public int RetryCount { get; set; }
    public bool IsDue { get; set; }
    public DateTime ScheduledFor { get; set; }
    public string FunctionName { get; set; }
}