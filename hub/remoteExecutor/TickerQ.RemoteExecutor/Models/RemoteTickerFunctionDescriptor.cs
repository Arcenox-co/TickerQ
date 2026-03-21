using TickerQ.Utilities.Enums;

namespace TickerQ.RemoteExecutor.Models;

public sealed class RemoteTickerFunctionDescriptor
{
    public string Name { get; set; } = string.Empty;
    public string CronExpression { get; set; } = string.Empty;
    public string Callback { get; set; } = string.Empty;
    public string RequestType { get; set; } = string.Empty;
    public string RequestExampleJson { get; set; } = string.Empty;
    public TickerTaskPriority Priority { get; set; }
    public bool IsActive { get; set; } = true;
}
