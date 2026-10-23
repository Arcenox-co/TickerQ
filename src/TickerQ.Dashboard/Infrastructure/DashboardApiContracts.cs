using System.Collections.Generic;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Models;

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
    public string? SchedulePolicyName { get; set; }
    public BlockedOccurrenceBehavior? BlockedOccurrenceBehavior { get; set; }
    public MissedExecutionAction? MissedExecutionAction { get; set; }
    public long? MaximumLatenessSeconds { get; set; }
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
    public string? SchedulePolicyName { get; set; }
    public bool ReplaceSchedulePolicy { get; set; }
    public BlockedOccurrenceBehavior? BlockedOccurrenceBehavior { get; set; }
    public MissedExecutionAction? MissedExecutionAction { get; set; }
    public long? MaximumLatenessSeconds { get; set; }
}

internal sealed class DuplicateTimeTickerRequest
{
    public System.DateTime? ExecutionTime { get; set; }
}

internal sealed class AddCronTickerRequest
{
    public TickerQ.Utilities.Models.CronChainTemplate? Chain { get; set; }
    public string Function { get; set; } = string.Empty;
    public string Expression { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int? Retries { get; set; }
    public byte[]? Request { get; set; }
    public int[]? RetryIntervalsSeconds { get; set; }
    public bool? SkipIfAlreadyRunning { get; set; }
    public bool? IsEnabled { get; set; }
    public StaleAction? OnStale { get; set; }
    public int? TimeoutSeconds { get; set; }
    public string? TimeZoneId { get; set; }
    public string? SchedulePolicyName { get; set; }
    public MissedExecutionAction? MissedExecutionAction { get; set; }
    public long? MaximumLatenessSeconds { get; set; }
    public CronCatchUpMode? CatchUpMode { get; set; }
}

internal sealed class UpdateCronTickerRequestApi
{
    public TickerQ.Utilities.Models.CronChainTemplate? Chain { get; set; }
    public bool ReplaceChain { get; set; }
    public string? Function { get; set; }
    public string? Expression { get; set; }
    public string? Description { get; set; }
    public int? Retries { get; set; }
    public int[]? RetryIntervalsSeconds { get; set; }
    public byte[]? Request { get; set; }
    public bool? SkipIfAlreadyRunning { get; set; }
    public bool? IsEnabled { get; set; }
    public StaleAction? OnStale { get; set; }
    public int? TimeoutSeconds { get; set; }
    public string? TimeZoneId { get; set; }
    public string? SchedulePolicyName { get; set; }
    public bool ReplaceSchedulePolicy { get; set; }
    public MissedExecutionAction? MissedExecutionAction { get; set; }
    public long? MaximumLatenessSeconds { get; set; }
    public CronCatchUpMode? CatchUpMode { get; set; }
}

internal sealed class ToggleCronTickerBody
{
    public bool IsEnabled { get; set; }
}

internal sealed class TimeTickerNodeRequest
{
    public ChainDeleteBehavior? DeleteBehavior { get; set; }
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

internal sealed class SetChainDeleteBehaviorRequest
{
    public ChainDeleteBehavior? DeleteBehavior { get; set; }
}

internal sealed class AddTimeTickerChainRequest
{
    public System.DateTime ExecutionTime { get; set; }
    public TimeTickerNodeRequest? Root { get; set; }
    public string? SchedulePolicyName { get; set; }
    public BlockedOccurrenceBehavior? BlockedOccurrenceBehavior { get; set; }
    public MissedExecutionAction? MissedExecutionAction { get; set; }
    public long? MaximumLatenessSeconds { get; set; }
}

internal class SaveSchedulePolicyRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string TimeZoneId { get; set; } = "UTC";
    public ScheduleWeekdays AllowedWeekdays { get; set; } = ScheduleWeekdays.All;
    public List<ScheduleTimeWindow> AllowedTimeWindows { get; set; } = new();
    public List<System.DateOnly> ExcludedDates { get; set; } = new();
    public List<ScheduleAnnualDate> ExcludedAnnualDates { get; set; } = new();
    public List<string> DateProviderNames { get; set; } = new();
    public List<string> RequiredPolicyNames { get; set; } = new();
    public List<string> ExcludedWhenPolicyAllowsNames { get; set; } = new();
}

internal sealed class UpdateSchedulePolicyRequest : SaveSchedulePolicyRequest
{
    public long Version { get; set; }
}

internal sealed class SchedulePolicyPreviewRequest
{
    public string Expression { get; set; } = string.Empty;
    public System.DateTime FromUtc { get; set; }
    public int Count { get; set; } = 20;
    public string? TimeZoneId { get; set; }
}

internal sealed class SchedulePolicyUsageResponse
{
    public int TimeTickerCount { get; set; }
    public int CronTickerCount { get; set; }
}

internal sealed class ScheduleDateProvidersResponse
{
    public string[] Names { get; set; } = System.Array.Empty<string>();
}

internal sealed class SavedDefinitionPayload
{
    public System.DateTime? ExecutionTime { get; set; }
    public string? Expression { get; set; }
    public bool? SkipIfAlreadyRunning { get; set; }
    public string? TimeZoneId { get; set; }
    public string? SchedulePolicyName { get; set; }
    public BlockedOccurrenceBehavior? BlockedOccurrenceBehavior { get; set; }
    public MissedExecutionAction? MissedExecutionAction { get; set; }
    public long? MaximumLatenessSeconds { get; set; }
    public CronCatchUpMode? CatchUpMode { get; set; }
    public TimeTickerNodeRequest? Root { get; set; }
}

internal class SaveTickerDefinitionRequest
{
    public SavedTickerDefinitionKind Kind { get; set; }
    public SavedTickerDefinitionTarget Target { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IncludeRequestData { get; set; }
    public SavedDefinitionPayload? Payload { get; set; }
}

internal sealed class UpdateTickerDefinitionRequest : SaveTickerDefinitionRequest
{
    public long Version { get; set; }
}

internal sealed class SaveExistingTickerDefinitionRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IncludeRequestData { get; set; }
}

internal sealed class ActivateTickerDraftRequest
{
    public long Version { get; set; }
    public System.DateTime? ExecutionTime { get; set; }
}

internal sealed class SavedTickerDefinitionResponse
{
    public System.Guid Id { get; set; }
    public SavedTickerDefinitionKind Kind { get; set; }
    public SavedTickerDefinitionTarget Target { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IncludesRequestData { get; set; }
    public int SchemaVersion { get; set; }
    public System.DateTime CreatedAtUtc { get; set; }
    public System.DateTime UpdatedAtUtc { get; set; }
    public long Version { get; set; }
    public System.Guid? ActivatedRootTickerId { get; set; }
    public System.DateTime? ActivatedAtUtc { get; set; }
    public bool ActivationInProgress { get; set; }
    public SavedDefinitionPayload? Payload { get; set; }
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

internal sealed class TestRunRequest
{
    public string Function { get; set; } = string.Empty;
    public byte[]? Request { get; set; }
    public int? TimeoutSeconds { get; set; }
}

internal sealed class TestRunResponse
{
    public System.Guid ExecutionId { get; set; }
    public TickerStatus Status { get; set; }
    public bool Success { get; set; }
    public string? Error { get; set; }
    public long ElapsedMilliseconds { get; set; }
    public byte[]? Result { get; set; }
    public string? ResultMediaType { get; set; }
    public string? ResultContractId { get; set; }
}

internal sealed class TestRunChainResponse
{
    public System.Guid RootExecutionId { get; set; }
    public bool Success { get; set; }
    public bool Completed { get; set; }
    public System.DateTime StartedAtUtc { get; set; }
    public System.DateTime? FinishedAtUtc { get; set; }
    public string? Error { get; set; }
    public List<TestRunChainStepResponse> Steps { get; set; } = new();
}

internal sealed class TestRunChainStepResponse
{
    public System.Guid ExecutionId { get; set; }
    public System.Guid? ParentExecutionId { get; set; }
    public string Function { get; set; } = string.Empty;
    public string? Description { get; set; }
    public TickerStatus Status { get; set; }
    public RunCondition? RunCondition { get; set; }
    public TickerTaskPriority Priority { get; set; }
    public int Retries { get; set; }
    public int RetryCount { get; set; }
    public System.DateTime CreatedAtUtc { get; set; }
    public System.DateTime? ExecutedAtUtc { get; set; }
    public System.DateTime? ProgressUpdatedAtUtc { get; set; }
    public int ChildCount { get; set; }
    public string? Error { get; set; }
    public long ElapsedMilliseconds { get; set; }
    public double? ProgressPercent { get; set; }
    public string? ProgressMessage { get; set; }
    public byte[]? Result { get; set; }
    public string? ResultMediaType { get; set; }
    public string? ResultContractId { get; set; }
    public bool IsGroup { get; set; }
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

internal sealed class CancellationResponseBody
{
    public string Outcome { get; set; } = string.Empty;
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

internal sealed class TickerResultPayloadResponse
{
    public byte[]? Payload { get; set; }
    public int? Version { get; set; }
    public string? MediaType { get; set; }
    public string? ContractId { get; set; }
    public string? ContractType { get; set; }
}

internal sealed class TickerLogLineResponse
{
    public string? LifecycleEvent { get; set; }
    public string? Status { get; set; }
    public int Attempt { get; set; }
    public System.Guid Id { get; set; }
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
    public string Storage { get; set; } = "memory";
    public bool Truncated { get; set; }
    public bool PersistenceAvailable { get; set; } = true;
    public ExecutionLogHealthResponse? WriteHealth { get; set; }
}

internal sealed class TickerLogBatchRequest
{
    public List<TickerLogCursorRequest> Items { get; set; } = new();
}
internal sealed class TickerLogCursorRequest
{
    public System.Guid TickerId { get; set; }
    public string? Cursor { get; set; }
}
internal sealed class TickerLogBatchResponse
{
    public List<TickerLogBatchItemResponse> Items { get; set; } = new();
}
internal sealed class TickerLogBatchItemResponse
{
    public System.Guid TickerId { get; set; }
    public string Cursor { get; set; } = string.Empty;
    public bool Reset { get; set; }
    public TickerLogTailResponseBody Tail { get; set; } = new();
}
