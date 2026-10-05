using System;
using Microsoft.Extensions.AI;

namespace TickerQ.Dashboard.Assistant;

/// <summary>
/// Fluent configuration for the dashboard AI assistant. The operator supplies
/// a <see cref="IChatClient"/> (Anthropic, OpenAI, Azure OpenAI, a local
/// Ollama, …) — the key/endpoint lives on the server and never reaches the
/// browser. When no chat client is configured the assistant feature stays
/// completely off: the SPA never renders the chat UI and the endpoint isn't
/// mapped.
/// </summary>
public sealed class AssistantOptionsBuilder
{
    internal Func<IServiceProvider, IChatClient>? ChatClientFactory { get; private set; }

    /// <summary>Model label surfaced to the SPA (cosmetic — e.g. "claude-fable-5").</summary>
    internal string ModelName { get; private set; } = "";

    /// <summary>
    /// Max tool-call round-trips per user turn — bounds cost / runaway loops.
    /// </summary>
    internal int MaxToolIterations { get; private set; } = 8;

    /// <summary>System prompt prepended to every conversation.</summary>
    internal string SystemPrompt { get; private set; } = DefaultSystemPrompt;

    /// <summary>
    /// Register the chat client used to answer questions. The factory runs
    /// once (singleton) with the app's <see cref="IServiceProvider"/>.
    /// </summary>
    public AssistantOptionsBuilder UseChatClient(Func<IServiceProvider, IChatClient> factory)
    {
        ChatClientFactory = factory ?? throw new ArgumentNullException(nameof(factory));
        return this;
    }

    /// <summary>Register an already-constructed chat client.</summary>
    public AssistantOptionsBuilder UseChatClient(IChatClient client)
    {
        if (client == null) throw new ArgumentNullException(nameof(client));
        ChatClientFactory = _ => client;
        return this;
    }

    /// <summary>Set the cosmetic model label shown in the chat panel header.</summary>
    public AssistantOptionsBuilder WithModelName(string modelName)
    {
        ModelName = modelName ?? "";
        return this;
    }

    /// <summary>Cap the number of tool round-trips per user turn (default 8).</summary>
    public AssistantOptionsBuilder WithMaxToolIterations(int max)
    {
        if (max < 1) throw new ArgumentOutOfRangeException(nameof(max));
        MaxToolIterations = max;
        return this;
    }

    /// <summary>Override the system prompt (advanced).</summary>
    public AssistantOptionsBuilder WithSystemPrompt(string systemPrompt)
    {
        SystemPrompt = systemPrompt ?? DefaultSystemPrompt;
        return this;
    }

    internal bool IsEnabled => ChatClientFactory != null;

    private const string DefaultSystemPrompt =
        """
        You are the TickerQ Dashboard assistant. You help operators understand their
        scheduled jobs: why executions failed, what a cron ticker does, which nodes are
        healthy, and general scheduler state.

        Rules:
        - Answer ONLY from data returned by the provided read-only tools. Never invent
          ticker ids, statuses, timestamps, or error messages.
        - When asked why something failed, fetch the relevant executions, read the
          exception message and retry history, and explain in plain language. Point out
          patterns (all retries exhausted with the same error, skipped because a node
          was offline, started failing after a schedule change, etc.).
        - You cannot execute changes directly. When the user asks to create or modify a
          schedule and propose_* tools are available, ALWAYS respond by DRAFTING it with
          a propose_* tool — a confirmation card is shown and a human must click
          Confirm. Never refuse a create request and never claim a ticker was created
          or updated; say it awaits the user's confirmation.
        - Draft immediately with sensible defaults (retries 0, empty payload) rather
          than interrogating the user. If the request is ambiguous — e.g. the function
          name doesn't match exactly — briefly list the closest registered functions as
          options and ask ONE short question, or draft your best interpretation and
          state the assumptions under the card.
        - For multi-step workflows ("run A then B", "B only if A fails") use
          propose_chain: children run after their parent per runCondition (OnSuccess,
          OnFailure, OnCancelled, OnFailureOrCancelled, OnAnyCompletedStatus,
          InProgress). Explain the flow in one sentence under the card.
        - Before proposing an update, look up the exact cron ticker
          (list_cron_tickers) and tell the user which one will be modified. If propose
          tools are unavailable (read-only mode), direct the user to the dashboard UI
          instead.
        - For retry/cancel/delete, tell the user which dashboard button to use.
        - Be concise. Prefer a short diagnosis + the concrete evidence over long prose.
        - Timestamps from tools are UTC. Say so if you quote one.
        """;
}
