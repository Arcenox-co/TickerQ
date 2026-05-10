using TickerQ.Utilities.Enums;

namespace TickerQ.RemoteExecutor.Models;

public sealed class RegisteredFunctionsResponse
{
    public string ApplicationId { get; set; } = string.Empty;
    public string EnvironmentId { get; set; } = string.Empty;
    public string EnvironmentName { get; set; } = string.Empty;
    public string WebhookSignature { get; set; } = string.Empty;
    public List<Node> Nodes { get; set; } = new();
}

public sealed class Node
{
    public string Id { get; set; } = string.Empty;
    public string NodeName { get; set; } = string.Empty;
    public string CallbackUrl { get; set; } = string.Empty;
    public bool AutoMigrateExpressions { get; set; }
    public DateTime? LastSyncedAt { get; set; }
    public List<Function> Functions { get; set; } = new();
}

public sealed class Function
{
    public string Id { get; set; } = string.Empty;
    public string FunctionName { get; set; } = string.Empty;
    public string RequestType { get; set; } = string.Empty;
    public string? RequestExampleJson { get; set; }
    public string? NodeExpression { get; set; }
    public TickerTaskPriority TaskPriority { get; set; }
    public DateTime? AppliedAt { get; set; }
    public bool IsActive { get; set; }
}
