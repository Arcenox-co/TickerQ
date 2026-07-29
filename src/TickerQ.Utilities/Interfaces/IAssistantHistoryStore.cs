using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Models;

namespace TickerQ.Utilities.Interfaces
{
    /// <summary>
    /// Optional per-user chat history storage for the dashboard AI assistant.
    /// The dashboard stores nothing by default — register an implementation in
    /// DI (e.g. via TickerQ.EntityFrameworkCore's <c>AddAssistantHistory()</c>)
    /// and the assistant persists conversations through it, with the SPA
    /// showing a history panel automatically. Every method receives the
    /// authenticated user id, derived server-side from the request;
    /// implementations MUST scope all reads and writes to it.
    /// </summary>
    public interface IAssistantHistoryStore
    {
        /// <summary>List the user's conversations, most recently updated first.</summary>
        Task<IReadOnlyList<AssistantConversationSummary>> ListConversationsAsync(
            string userId, CancellationToken cancellationToken = default);

        /// <summary>Get one conversation's messages in order. Null when it doesn't exist or belongs to another user.</summary>
        Task<IReadOnlyList<AssistantHistoryMessage>> GetMessagesAsync(
            string userId, Guid conversationId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Append a user/assistant exchange. Pass null <paramref name="conversationId"/>
        /// to start a new conversation; returns the (new or existing) conversation id.
        /// </summary>
        Task<Guid> AppendExchangeAsync(
            string userId, Guid? conversationId, string userMessage, string assistantMessage,
            string model, CancellationToken cancellationToken = default);

        /// <summary>Delete a conversation and its messages (no-op when not the user's).</summary>
        Task DeleteConversationAsync(
            string userId, Guid conversationId, CancellationToken cancellationToken = default);
    }
}
