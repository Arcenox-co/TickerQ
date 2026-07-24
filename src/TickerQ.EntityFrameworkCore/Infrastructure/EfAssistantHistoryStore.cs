using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TickerQ.EntityFrameworkCore.Entities;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ.EntityFrameworkCore.Infrastructure
{
    /// <summary>
    /// EF-backed <see cref="IAssistantHistoryStore"/> over the customer's
    /// TickerQ DbContext. Works with any <typeparamref name="TContext"/> the
    /// operational store registered — the assistant tables are part of the
    /// same model (opt-in via <c>AddAssistantHistory()</c>), so they ride the
    /// customer's normal migrations flow.
    /// </summary>
    internal sealed class EfAssistantHistoryStore<TContext> : IAssistantHistoryStore
        where TContext : DbContext
    {
        private readonly TContext _db;
        private readonly AssistantHistoryOptions _options;

        public EfAssistantHistoryStore(TContext db, AssistantHistoryOptions options)
        {
            _db = db;
            _options = options;
        }

        private DbSet<AssistantConversationEntity> Conversations => _db.Set<AssistantConversationEntity>();
        private DbSet<AssistantMessageEntity> Messages => _db.Set<AssistantMessageEntity>();

        public async Task<IReadOnlyList<AssistantConversationSummary>> ListConversationsAsync(
            string userId, CancellationToken cancellationToken = default)
            => await Conversations.AsNoTracking()
                .Where(c => c.UserId == userId)
                .OrderByDescending(c => c.UpdatedAt)
                .Select(c => new AssistantConversationSummary
                {
                    Id = c.Id,
                    Title = c.Title,
                    Model = c.Model,
                    CreatedAt = c.CreatedAt,
                    UpdatedAt = c.UpdatedAt,
                })
                .ToListAsync(cancellationToken);

        public async Task<IReadOnlyList<AssistantHistoryMessage>> GetMessagesAsync(
            string userId, Guid conversationId, CancellationToken cancellationToken = default)
        {
            var owns = await Conversations.AsNoTracking()
                .AnyAsync(c => c.Id == conversationId && c.UserId == userId, cancellationToken);
            if (!owns) return null;

            return await Messages.AsNoTracking()
                .Where(m => m.ConversationId == conversationId)
                .OrderBy(m => m.Ordinal)
                .Select(m => new AssistantHistoryMessage
                {
                    Role = m.Role,
                    Content = m.Content,
                    CreatedAt = m.CreatedAt,
                })
                .ToListAsync(cancellationToken);
        }

        public async Task<Guid> AppendExchangeAsync(
            string userId, Guid? conversationId, string userMessage, string assistantMessage,
            string model, CancellationToken cancellationToken = default)
        {
            var now = DateTime.UtcNow;

            var conversation = conversationId.HasValue
                ? await Conversations.FirstOrDefaultAsync(
                    c => c.Id == conversationId && c.UserId == userId, cancellationToken)
                : null;

            if (conversation == null)
            {
                conversation = new AssistantConversationEntity
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    Title = userMessage.Length > 60 ? userMessage.Substring(0, 60) + "…" : userMessage,
                    Model = model ?? string.Empty,
                    CreatedAt = now,
                };
                Conversations.Add(conversation);
                await PruneConversationsAsync(userId, cancellationToken);
            }

            conversation.UpdatedAt = now;

            var nextOrdinal = await Messages
                .Where(m => m.ConversationId == conversation.Id)
                .Select(m => (int?)m.Ordinal)
                .MaxAsync(cancellationToken) ?? -1;

            Messages.Add(new AssistantMessageEntity
            {
                Id = Guid.NewGuid(),
                ConversationId = conversation.Id,
                Role = "user",
                Content = userMessage,
                Ordinal = nextOrdinal + 1,
                CreatedAt = now,
            });
            Messages.Add(new AssistantMessageEntity
            {
                Id = Guid.NewGuid(),
                ConversationId = conversation.Id,
                Role = "assistant",
                Content = assistantMessage,
                Ordinal = nextOrdinal + 2,
                CreatedAt = now,
            });

            await _db.SaveChangesAsync(cancellationToken);
            await PruneMessagesAsync(conversation.Id, cancellationToken);
            return conversation.Id;
        }

        public async Task DeleteConversationAsync(
            string userId, Guid conversationId, CancellationToken cancellationToken = default)
        {
            await Conversations
                .Where(c => c.Id == conversationId && c.UserId == userId)
                .ExecuteDeleteAsync(cancellationToken); // messages cascade
        }

        private async Task PruneConversationsAsync(string userId, CancellationToken cancellationToken)
        {
            var excess = await Conversations
                .Where(c => c.UserId == userId)
                .OrderByDescending(c => c.UpdatedAt)
                .Skip(Math.Max(1, _options.MaxConversationsPerUser) - 1)
                .Select(c => c.Id)
                .ToListAsync(cancellationToken);
            if (excess.Count > 0)
                await Conversations.Where(c => excess.Contains(c.Id)).ExecuteDeleteAsync(cancellationToken);
        }

        private async Task PruneMessagesAsync(Guid conversationId, CancellationToken cancellationToken)
        {
            var excess = await Messages
                .Where(m => m.ConversationId == conversationId)
                .OrderByDescending(m => m.Ordinal)
                .Skip(Math.Max(2, _options.MaxMessagesPerConversation))
                .Select(m => m.Id)
                .ToListAsync(cancellationToken);
            if (excess.Count > 0)
                await Messages.Where(m => excess.Contains(m.Id)).ExecuteDeleteAsync(cancellationToken);
        }
    }
}
