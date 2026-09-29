using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using TickerQ.Utilities.Models;

namespace TickerQ.Dashboard.Assistant;

/// <summary>Chat turn sent by the SPA: prior messages + the new user message.</summary>
public sealed class ChatRequest
{
    public List<ChatTurn> Messages { get; set; } = new();

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

/// <summary>One SSE frame: {type: "delta"|"proposal"|"done"|"error", data}.</summary>
public sealed class ChatStreamEvent
{
    public string Type { get; set; } = "";
    public string Data { get; set; } = "";
}

/// <summary>
/// A mutation the model wants to make, surfaced to the user as a confirmation
/// card. The model NEVER executes writes — clicking the card's confirm button
/// calls the same REST endpoints the rest of the dashboard uses, so auth,
/// read-only guards, validation and SignalR notifications all apply normally.
/// </summary>
public sealed class AssistantProposal
{
    /// <summary>"createTimeTicker" | "createCronTicker" | "updateCronTicker".</summary>
    public string Kind { get; set; } = "";

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
    /// Normalized chain tree as JSON (createChain only) — a single root
    /// <see cref="AssistantChainNode"/>. The card renders it as a tree and
    /// confirm maps it onto the existing POST /time-tickers/chain contract.
    /// </summary>
    public string ChainJson { get; set; } = "";
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
[JsonSerializable(typeof(AssistantConversationSummary))]
[JsonSerializable(typeof(List<AssistantConversationSummary>))]
[JsonSerializable(typeof(AssistantHistoryMessage))]
[JsonSerializable(typeof(List<AssistantHistoryMessage>))]
internal partial class AssistantJsonContext : JsonSerializerContext
{
}
