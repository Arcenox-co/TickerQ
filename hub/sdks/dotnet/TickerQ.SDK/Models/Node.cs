namespace TickerQ.SDK.Models;

public sealed class Node
{
    public string NodeName { get; set; }
    public string CallbackUrl { get; set; }
    public bool IsProduction { get; set; }
    public List<NodeFunction> Functions { get; set; } = new();
}

