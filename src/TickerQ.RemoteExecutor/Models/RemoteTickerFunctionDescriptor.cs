using TickerQ.Utilities.Enums;

namespace TickerQ.RemoteExecutor.Models;

public sealed class RemoteTickerFunctionDescriptor
{
    public string Name { get; set; }
    public string CronExpression { get; set; }
    public string Callback { get; set; }
    public TickerTaskPriority Priority { get; set; }
}
