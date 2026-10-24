using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using TickerQ.Utilities.Models;
using TickerQ.Utilities.Integrations;

namespace TickerQ.Dashboard.Assistant;

/// <summary>Chat turn sent by the SPA: prior messages + the new user message.</summary>
public sealed class ChatRequest
{
    public List<ChatTurn> Messages { get; set; } = new();

    /// <summary>
    /// Saved investigation explicitly selected by the operator. The server resolves this id
    /// inside the active application namespace and supplies its diagnosis as bounded context.
    /// Null means this is an ordinary, unscoped chat.
    /// </summary>
    public Guid? InvestigationId { get; set; }

    /// <summary>
    /// Existing conversation to append to (history mode). Null starts a new
    /// conversation; the server returns the id on the "done" stream event.
    /// </summary>
    public Guid? ConversationId { get; set; }
}

public sealed class ChatTurn
{
    /// <summary>"user" or "assistant".</summary>
    public string Role { get; set; } = "user";
    public string Content { get; set; } = "";
}

/// <summary>One SSE frame: {type: "delta"|"proposal"|"investigations"|"done"|"error", data}.</summary>
public sealed class ChatStreamEvent
{
    public string Type { get; set; } = "";
    public string Data { get; set; } = "";
}

public sealed class AssistantInvestigationReviewRequest
{
    public string ReviewState { get; set; } = "";
}

public sealed class AssistantProposalStateRequest
{
    public string Status { get; set; } = "";
    public string ResultJson { get; set; } = "";
}

/// <summary>
/// A mutation the model wants to make, surfaced to the user as a confirmation
/// card. The model NEVER executes writes — clicking the card's confirm button
/// calls the same REST endpoints the rest of the dashboard uses, so auth,
/// read-only guards, validation and SignalR notifications all apply normally.
/// </summary>
public sealed class AssistantProposal
{
    /// <summary>Stable history id assigned by the server before the card is streamed.</summary>
    public Guid? ProposalId { get; set; }

    /// <summary>
    /// Proposal kind. One-time jobs and chains use createTimeTickerDraft and
    /// createChainDraft so confirmation saves a non-executable draft.
    /// </summary>
    public string Kind { get; set; } = "";

    /// <summary>Name shown in the saved drafts list.</summary>
    public string DraftName { get; set; } = "";

    public string Function { get; set; } = "";

    /// <summary>Cron expression (create/update cron).</summary>
    public string Expression { get; set; } = "";

    /// <summary>The cron's current expression (update only — lets the card show a diff).</summary>
    public string CurrentExpression { get; set; } = "";

    /// <summary>ISO-8601 UTC execution time (time ticker only).</summary>
    public string ExecutionTime { get; set; } = "";

    public int? Retries { get; set; }
    public string Description { get; set; } = "";

    /// <summary>Raw JSON payload for the job, or empty.</summary>
    public string RequestJson { get; set; } = "";

    /// <summary>The cron ticker being modified (update only).</summary>
    public Guid? TargetId { get; set; }

    public bool? IsEnabled { get; set; }

    /// <summary>
    /// Normalized chain tree as JSON (createChain/createChainDraft only) — a single root
    /// <see cref="AssistantChainNode"/>. The card renders it as a tree and
    /// confirm maps it onto the existing POST /time-tickers/chain contract.
    /// </summary>
    public string ChainJson { get; set; } = "";

    public bool? SkipIfAlreadyRunning { get; set; }
    public string TargetType { get; set; } = "";
    public string ExpectedStatus { get; set; } = "";
    public DateTime? ExpectedExecutedAt { get; set; }
    public DateTime? ExpectedLockedAt { get; set; }
    public Guid? CronTickerId { get; set; }
    public string Warning { get; set; } = "";
    public Guid? ConfigurationVersion { get; set; }
    public IntegrationRule? Rule { get; set; }
    public IntegrationMonitor? Monitor { get; set; }
    public List<string> DestinationNames { get; set; } = new();

    /// <summary>Proposed schedule policy (createSchedulePolicy/updateSchedulePolicy).</summary>
    public AssistantSchedulePolicyDraft? Policy { get; set; }

    /// <summary>The policy as it was when proposed (updateSchedulePolicy only), so the card can show changes.</summary>
    public AssistantSchedulePolicyDraft? CurrentPolicy { get; set; }

    /// <summary>Optimistic-concurrency version the update was proposed against.</summary>
    public long? ExpectedVersion { get; set; }

    /// <summary>Policy to attach to a cron ticker; empty removes it (setCronSchedulePolicy, createCronTicker).</summary>
    public string SchedulePolicyName { get; set; } = "";

    /// <summary>The cron ticker's policy when proposed (setCronSchedulePolicy only).</summary>
    public string CurrentSchedulePolicyName { get; set; } = "";
}

/// <summary>
/// A schedule policy in the same shape as the dashboard's save request, so confirming the card can
/// send it to the normal create or update endpoint unchanged.
/// </summary>
public sealed class AssistantSchedulePolicyDraft
{
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string TimeZoneId { get; set; } = "UTC";
    public string AllowedWeekdays { get; set; } = "All";
    public List<AssistantScheduleWindow> AllowedTimeWindows { get; set; } = new();
    public List<string> ExcludedDates { get; set; } = new();
    public List<ScheduleAnnualDate> ExcludedAnnualDates { get; set; } = new();
    public List<string> DateProviderNames { get; set; } = new();
    public List<string> RequiredPolicyNames { get; set; } = new();
    public List<string> ExcludedWhenPolicyAllowsNames { get; set; } = new();

    internal static AssistantSchedulePolicyDraft From(SchedulePolicy policy) => new()
    {
        Name = policy.Name,
        Description = policy.Description,
        TimeZoneId = policy.TimeZoneId,
        AllowedWeekdays = policy.AllowedWeekdays.ToString(),
        AllowedTimeWindows = (policy.AllowedTimeWindows ?? new()).Select(w => new AssistantScheduleWindow
        {
            Start = w.Start.ToString(@"hh\:mm\:ss", System.Globalization.CultureInfo.InvariantCulture),
            End = w.End.ToString(@"hh\:mm\:ss", System.Globalization.CultureInfo.InvariantCulture),
        }).ToList(),
        ExcludedDates = (policy.ExcludedDates ?? new()).Select(d => d.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)).ToList(),
        ExcludedAnnualDates = (policy.ExcludedAnnualDates ?? new()).Select(d => new ScheduleAnnualDate(d.Month, d.Day)).ToList(),
        DateProviderNames = (policy.DateProviderNames ?? new()).ToList(),
        RequiredPolicyNames = (policy.RequiredPolicyNames ?? new()).ToList(),
        ExcludedWhenPolicyAllowsNames = (policy.ExcludedWhenPolicyAllowsNames ?? new()).ToList(),
    };
}

/// <summary>A daily window as HH:mm:ss strings, matching the dashboard API.</summary>
public sealed class AssistantScheduleWindow
{
    public string Start { get; set; } = "";
    public string End { get; set; } = "";
}

/// <summary>One step of a proposed chain (createChain proposals).</summary>
public sealed class AssistantChainNode
{
    public string Function { get; set; } = "";
    public int? Retries { get; set; }
    public string Description { get; set; } = "";
    public string RequestJson { get; set; } = "";

    /// <summary>
    /// When this step runs relative to its parent: OnSuccess, OnFailure,
    /// OnCancelled, OnFailureOrCancelled, OnAnyCompletedStatus, InProgress.
    /// Null on the root.
    /// </summary>
    public string RunCondition { get; set; }

    public List<AssistantChainNode> Children { get; set; } = new();
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(ChatRequest))]
[JsonSerializable(typeof(ChatTurn))]
[JsonSerializable(typeof(ChatStreamEvent))]
[JsonSerializable(typeof(AssistantProposal))]
[JsonSerializable(typeof(AssistantChainNode))]
[JsonSerializable(typeof(AssistantSchedulePolicyDraft))]
[JsonSerializable(typeof(AssistantConversationSummary))]
[JsonSerializable(typeof(List<AssistantConversationSummary>))]
[JsonSerializable(typeof(AssistantHistoryMessage))]
[JsonSerializable(typeof(List<AssistantHistoryMessage>))]
[JsonSerializable(typeof(AssistantInvestigationRecord))]
[JsonSerializable(typeof(AssistantInvestigationPage))]
[JsonSerializable(typeof(AssistantInvestigationOverview))]
[JsonSerializable(typeof(AssistantInvestigationReviewRequest))]
[JsonSerializable(typeof(AssistantProposalStateRequest))]
internal partial class AssistantJsonContext : JsonSerializerContext
{
}
