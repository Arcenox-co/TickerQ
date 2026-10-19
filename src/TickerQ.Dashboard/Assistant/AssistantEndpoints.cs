using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;
using TickerQ.Utilities;
using TickerQ.Utilities.Infrastructure;
using TickerQ.Utilities.Integrations;

namespace TickerQ.Dashboard.Assistant;

/// <summary>
/// Maps <c>POST /api/assistant/chat</c> — a streaming (SSE) chat endpoint.
/// Only mapped when a chat client is configured. It lives under <c>/api</c>
/// so <c>AuthMiddleware</c> gates it exactly like every other dashboard API,
/// and it is NOT under the write-endpoint group because the assistant is
/// strictly read-only (its tools only read).
/// </summary>
internal static class AssistantEndpoints
{
    public static void MapAssistantEndpoints<TTimeTicker, TCronTicker>(
        this IEndpointRouteBuilder api, DashboardOptionsBuilder config)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        if (config.Assistant?.IsEnabled != true) return;

        api.MapPost("/assistant/chat", ChatAsync<TTimeTicker, TCronTicker>)
            .WithTags("TickerQ Dashboard");

        // Per-user chat history — active only when the operator registered an
        // IAssistantHistoryStore; otherwise these answer 404 and the SPA
        // falls back to per-browser storage.
        // (Delegate) cast matters: a (HttpContext) → Task<IResult> method group
        // otherwise binds to the RequestDelegate overload, which discards the
        // IResult and returns an empty 200.
        api.MapGet("/assistant/conversations", (Delegate)ListConversationsAsync).WithTags("TickerQ Dashboard");
        api.MapGet("/assistant/conversations/{id:guid}", GetConversationAsync).WithTags("TickerQ Dashboard");
        api.MapDelete("/assistant/conversations/{id:guid}", DeleteConversationAsync).WithTags("TickerQ Dashboard");
    }

    /// <summary>
    /// The stable identity chats are scoped to. Prefers the dashboard's own
    /// auth (AuthMiddleware sets Items["auth.username"]); host-mode setups
    /// fall back to the host identity's subject claim, then name.
    /// </summary>
    private static string ResolveUserId(HttpContext ctx)
        => ctx.Items["auth.username"] as string
           ?? ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
           ?? ctx.User.Identity?.Name
           ?? "anonymous";

    private static async Task<IResult> ListConversationsAsync(HttpContext ctx)
    {
        var store = ctx.RequestServices.GetService<IAssistantHistoryStore>();
        if (store == null) return Results.NotFound();

        var items = await store.ListConversationsAsync(ResolveUserId(ctx), ctx.RequestAborted);
        return Results.Json(items.ToList(), AssistantJsonContext.Default.ListAssistantConversationSummary);
    }

    private static async Task<IResult> GetConversationAsync(HttpContext ctx, Guid id)
    {
        var store = ctx.RequestServices.GetService<IAssistantHistoryStore>();
        if (store == null) return Results.NotFound();

        var messages = await store.GetMessagesAsync(ResolveUserId(ctx), id, ctx.RequestAborted);
        if (messages == null) return Results.NotFound();
        return Results.Json(messages.ToList(), AssistantJsonContext.Default.ListAssistantHistoryMessage);
    }

    private static async Task<IResult> DeleteConversationAsync(HttpContext ctx, Guid id)
    {
        var store = ctx.RequestServices.GetService<IAssistantHistoryStore>();
        if (store == null) return Results.NotFound();

        await store.DeleteConversationAsync(ResolveUserId(ctx), id, ctx.RequestAborted);
        return Results.NoContent();
    }

    private static async Task ChatAsync<TTimeTicker, TCronTicker>(HttpContext ctx)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var assistant = ctx.RequestServices.GetRequiredService<TickerAssistantService>();
        var data = ctx.RequestServices.GetRequiredService<ITickerDashboardDataService<TTimeTicker, TCronTicker>>();

        ChatRequest request;
        try { request = await ReadChatRequest(ctx); }
        catch (BadHttpRequestException ex)
        {
            ctx.Response.StatusCode = ex.StatusCode;
            await ctx.Response.WriteAsync(ex.Message, ctx.RequestAborted);
            return;
        }
        catch (JsonException)
        {
            ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
            await ctx.Response.WriteAsync("Invalid chat JSON.", ctx.RequestAborted);
            return;
        }

        var opts = assistant.Options;
        var history = new List<ChatMessage>
        {
            new(ChatRole.System, opts.SystemPrompt),
        };
        // Cap history to keep context (and cost) bounded on long threads.
        foreach (var m in request.Messages.TakeLast(20))
        {
            var role = string.Equals(m.Role, "assistant", StringComparison.OrdinalIgnoreCase)
                ? ChatRole.Assistant : ChatRole.User;
            history.Add(new ChatMessage(role, m.Content ?? ""));
        }

        ctx.Response.Headers.ContentType = "text/event-stream";
        ctx.Response.Headers.CacheControl = "no-cache";
        ctx.Response.Headers["X-Accel-Buffering"] = "no"; // disable nginx buffering

        // Proposal tools are excluded for read-only users — they can neither
        // execute nor even draft mutations. Proposals stream as their own SSE
        // event so the SPA can render a confirmation card inline.
        var dashboardConfig = ctx.RequestServices.GetRequiredService<DashboardOptionsBuilder>();
        var allowProposals = !dashboardConfig.IsReadOnlyFor(ctx);
        Func<AssistantProposal, Task> emitProposal = proposal =>
            WriteEvent(ctx, "proposal",
                JsonSerializer.Serialize(proposal, AssistantJsonContext.Default.AssistantProposal));

        var chatOptions = new ChatOptions
        {
            Tools = TickerAssistantTools.Build(data, allowProposals, emitProposal, ctx.RequestAborted,
                readLogs: ctx.RequestServices.GetService<ExecutionLogReader>() is { } reader ? reader.ReadAsync : null,
                integrations: dashboardConfig.CanManageIntegrations(ctx)
                    ? ctx.RequestServices.GetService<ITickerIntegrationAdminService>() : null,
                schedulerTimeZone: ctx.RequestServices.GetService<SchedulerOptionsBuilder>()?.SchedulerTimeZone.Id ?? "UTC",
                diagnostics: ctx.RequestServices.GetService<TickerQRuntimeDiagnostics>()),
            MaxOutputTokens = 1500,
        };

        var assistantText = new System.Text.StringBuilder();
        try
        {
            await foreach (var update in assistant.Client.GetStreamingResponseAsync(history, chatOptions, ctx.RequestAborted))
            {
                var text = update.Text;
                if (string.IsNullOrEmpty(text)) continue;
                assistantText.Append(text);
                await WriteEvent(ctx, "delta", text);
            }

            // Persist the exchange when a history store is registered. The
            // "done" event carries the conversation id so the SPA can keep
            // appending to the same thread.
            var conversationId = "";
            var store = ctx.RequestServices.GetService<IAssistantHistoryStore>();
            var lastUserMessage = request.Messages.LastOrDefault(m =>
                string.Equals(m.Role, "user", StringComparison.OrdinalIgnoreCase))?.Content;
            if (store != null && !string.IsNullOrEmpty(lastUserMessage) && assistantText.Length > 0)
            {
                var id = await store.AppendExchangeAsync(
                    ResolveUserId(ctx), request.ConversationId, lastUserMessage,
                    assistantText.ToString(), opts.ModelName, CancellationToken.None);
                conversationId = id.ToString();
            }

            await WriteEvent(ctx, "done", conversationId);
        }
        catch (OperationCanceledException)
        {
            // client navigated away / aborted — nothing to send.
        }
        catch (Exception ex)
        {
            await WriteStreamingFailure(ctx, ex);
        }
    }

    internal static async Task<ChatRequest> ReadChatRequest(HttpContext ctx)
    {
        if (!ctx.Request.HasJsonContentType())
            throw new BadHttpRequestException("Use application/json.", StatusCodes.Status415UnsupportedMediaType);
        const int limit = 128 * 1024;
        if (ctx.Request.ContentLength > limit)
            throw new BadHttpRequestException("Chat request exceeds 128 KB.", StatusCodes.Status413PayloadTooLarge);
        using var body = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = await ctx.Request.Body.ReadAsync(buffer, ctx.RequestAborted)) > 0)
        {
            if (body.Length + count > limit)
                throw new BadHttpRequestException("Chat request exceeds 128 KB.", StatusCodes.Status413PayloadTooLarge);
            body.Write(buffer, 0, count);
        }
        var request = JsonSerializer.Deserialize(body.ToArray(), AssistantJsonContext.Default.ChatRequest);
        if (request?.Messages == null || request.Messages.Count is < 1 or > 20 ||
            request.Messages.Any(m => m == null || m.Role is not ("user" or "assistant") ||
                string.IsNullOrWhiteSpace(m.Content) || m.Content.Length > 16384) ||
            request.Messages.Sum(m => m.Content.Length) > 65536 || request.Messages[^1].Role != "user")
            throw new BadHttpRequestException("Send up to 20 user/assistant messages, ending with a user message; limit each message to 16,384 characters and the total to 65,536.", StatusCodes.Status400BadRequest);
        return request;
    }

    private static async Task WriteStreamingFailure(HttpContext ctx, Exception exception)
    {
        ctx.RequestServices.GetService<ILoggerFactory>()?
            .CreateLogger("TickerQ.Dashboard.Assistant")
            .LogError(exception, "Assistant stream failed. TraceId: {TraceIdentifier}", ctx.TraceIdentifier);

        await WriteEvent(
            ctx,
            "error",
            $"The assistant request failed unexpectedly. Reference: {ctx.TraceIdentifier}");
    }

    private static async Task WriteEvent(HttpContext ctx, string type, string data)
    {
        var payload = JsonSerializer.Serialize(
            new ChatStreamEvent { Type = type, Data = data }, AssistantJsonContext.Default.ChatStreamEvent);
        await ctx.Response.WriteAsync($"data: {payload}\n\n", ctx.RequestAborted);
        await ctx.Response.Body.FlushAsync(ctx.RequestAborted);
    }
}
