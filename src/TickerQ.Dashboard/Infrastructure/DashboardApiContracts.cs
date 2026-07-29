using System.Collections.Generic;
using TickerQ.Utilities.Enums;

namespace TickerQ.Dashboard.Infrastructure;

// ===== Request bodies (writes) =====

internal sealed class AddTimeTickerRequest
{
    public string Function { get; set; } = string.Empty;
    public System.DateTime? ExecutionTime { get; set; }
    public string? Description { get; set; }
    public int? Retries { get; set; }
    public byte[]? Request { get; set; }
    public int[]? RetryIntervalsSeconds { get; set; }
    public StaleAction? OnStale { get; set; }
    public int? TimeoutSeconds { get; set; }
}

internal sealed class UpdateTimeTickerRequest
{
    public System.DateTime? ExecutionTime { get; set; }
    public string? Description { get; set; }
    public int? Retries { get; set; }
    public int[]? RetryIntervalsSeconds { get; set; }
    public byte[]? Request { get; set; }
    public string? Function { get; set; }
    public RunCondition? RunCondition { get; set; }
    public StaleAction? OnStale { get; set; }
    public int? TimeoutSeconds { get; set; }
}

internal sealed class DuplicateTimeTickerRequest
{
    public System.DateTime? ExecutionTime { get; set; }
}

internal sealed class AddCronTickerRequest
{
    public string Function { get; set; } = string.Empty;
    public string Expression { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int? Retries { get; set; }
    public byte[]? Request { get; set; }
    public int[]? RetryIntervalsSeconds { get; set; }
    public bool? IsEnabled { get; set; }
    public StaleAction? OnStale { get; set; }
    public int? TimeoutSeconds { get; set; }
}

internal sealed class UpdateCronTickerRequestApi
{
    public string? Function { get; set; }
    public string? Expression { get; set; }
    public string? Description { get; set; }
    public int? Retries { get; set; }
    public int[]? RetryIntervalsSeconds { get; set; }
    public byte[]? Request { get; set; }
    public bool? IsEnabled { get; set; }
    public StaleAction? OnStale { get; set; }
    public int? TimeoutSeconds { get; set; }
}

internal sealed class ToggleCronTickerBody
{
    public bool IsEnabled { get; set; }
}

internal sealed class TimeTickerNodeRequest
{
    public string Function { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int? Retries { get; set; }
    public byte[]? Request { get; set; }
    public int[]? RetryIntervalsSeconds { get; set; }
    public RunCondition? RunCondition { get; set; }
    public StaleAction? OnStale { get; set; }
    public int? TimeoutSeconds { get; set; }
    public IList<TimeTickerNodeRequest>? Children { get; set; }
}

internal sealed class AddTimeTickerChainRequest
{
    public System.DateTime ExecutionTime { get; set; }
    public TimeTickerNodeRequest? Root { get; set; }
}

internal sealed class BulkIdsRequest
{
    public List<System.Guid> Ids { get; set; } = new();
}

internal sealed class BulkRetryRequest
{
    public List<BulkRetryItem> Items { get; set; } = new();
}

internal sealed class BulkRetryItem
{
    public System.Guid Id { get; set; }
    /// <summary>"TimeTicker" | "CronOccurrence".</summary>
    public string Type { get; set; } = string.Empty;
    public System.Guid? CronTickerId { get; set; }
}

// ===== Response shapes =====

internal sealed class AddTickerResponseBody
{
    public string Id { get; set; } = string.Empty;
}

internal sealed class BulkActionResponseBody
{
    public int Affected { get; set; }
}

internal sealed class ErrorResponseBody
{
    public string Error { get; set; } = string.Empty;
}

internal sealed class AddTimeTickerChainResponseBody
{
    public string RootId { get; set; } = string.Empty;
    public int CreatedCount { get; set; }
}

internal sealed class StatusCountResponseBody
{
    public TickerStatus Status { get; set; }
    public int Count { get; set; }
}

internal sealed class NodeJobCountResponseBody
{
    public string NodeName { get; set; } = string.Empty;
    public int JobCount { get; set; }
}

internal sealed class TickerRequestPayloadResponse
{
    public byte[]? Payload { get; set; }
}

internal sealed class TickerLogLineResponse
{
    public long UnixMs { get; set; }
    public int Level { get; set; }
    public string Source { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string FunctionName { get; set; } = string.Empty;
}

internal sealed class TickerLogTailResponseBody
{
    public IList<TickerLogLineResponse> Lines { get; set; } = new List<TickerLogLineResponse>();
}
