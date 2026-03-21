using TickerQ.Utilities.Enums;

namespace TickerQ.SDK.Models;

public sealed class NodeFunction
{
    public string FunctionName { get; set; }
    public string RequestType { get; set; }
    public string RequestExampleJson { get; set; } = string.Empty;
    public TickerTaskPriority TaskPriority { get; set; }
    public string Expression { get; set; }
}
