using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
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
    /// stable model, so they ride the customer's normal migrations flow.
    /// <c>AddAssistantHistory()</c> enables the services that write to them.
    /// </summary>
    internal sealed class EfAssistantHistoryStore<TContext> : IAssistantHistoryStore, IAssistantProposalHistoryStore
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

            var rows = await Messages.AsNoTracking()
                .Where(m => m.ConversationId == conversationId)
                .OrderBy(m => m.Ordinal)
                .ToListAsync(cancellationToken);

            return rows.Select(m =>
            {
                var result = new AssistantHistoryMessage
                {
                    Id = m.Id,
                    Role = m.Role,
                    Content = m.Content,
                    CreatedAt = m.CreatedAt,
                };
                if (string.Equals(m.Role, ProposalRole, StringComparison.Ordinal))
                {
                    var envelope = DeserializeEnvelope(m.Content);
                    result.Content = string.Empty;
                    result.ProposalJson = envelope?.ProposalJson ?? string.Empty;
                    result.ProposalStatus = envelope?.Status ?? PendingStatus;
                    result.ProposalResultJson = envelope?.ResultJson ?? string.Empty;
                }
                return result;
            }).ToList();
        }

        public async Task<Guid> AppendExchangeAsync(
            string userId, Guid? conversationId, string userMessage, string assistantMessage,
            string model, CancellationToken cancellationToken = default)
            => await AppendExchangeWithProposalsAsync(userId, conversationId, userMessage,
                assistantMessage, model, Array.Empty<AssistantHistoryProposal>(), cancellationToken);

        public async Task<Guid> AppendExchangeWithProposalsAsync(
            string userId, Guid? conversationId, string userMessage, string assistantMessage,
            string model, IReadOnlyList<AssistantHistoryProposal> proposals,
            CancellationToken cancellationToken = default)
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

            var ordinal = nextOrdinal + 2;
            foreach (var proposal in proposals)
            {
                if (proposal == null || proposal.Id == Guid.Empty ||
                    string.IsNullOrWhiteSpace(proposal.ProposalJson))
                    continue;

                Messages.Add(new AssistantMessageEntity
                {
                    Id = proposal.Id,
                    ConversationId = conversation.Id,
                    Role = ProposalRole,
                    Content = JsonSerializer.Serialize(new ProposalEnvelope
                    {
                        ProposalJson = proposal.ProposalJson,
                        Status = PendingStatus,
                    }),
                    Ordinal = ordinal++,
                    CreatedAt = now,
                });
            }

            Messages.Add(new AssistantMessageEntity
            {
                Id = Guid.NewGuid(),
                ConversationId = conversation.Id,
                Role = "assistant",
                Content = assistantMessage,
                Ordinal = ordinal,
                CreatedAt = now,
            });

            await _db.SaveChangesAsync(cancellationToken);
            await PruneMessagesAsync(conversation.Id, cancellationToken);
            return conversation.Id;
        }

        public async Task<bool> SetProposalStateAsync(
            string userId, Guid proposalId, string status, string resultJson,
            CancellationToken cancellationToken = default)
        {
            if (status is not (AppliedStatus or DismissedStatus)) return false;

            var conversationId = await Messages.AsNoTracking()
                .Where(m => m.Id == proposalId && m.Role == ProposalRole)
                .Select(m => (Guid?)m.ConversationId)
                .FirstOrDefaultAsync(cancellationToken);
            if (!conversationId.HasValue) return false;

            var conversation = await Conversations.FirstOrDefaultAsync(c =>
                c.Id == conversationId.Value && c.UserId == userId, cancellationToken);
            if (conversation == null) return false;

            var message = await Messages.FirstAsync(m => m.Id == proposalId, cancellationToken);

            var envelope = DeserializeEnvelope(message.Content);
            if (envelope == null || string.IsNullOrWhiteSpace(envelope.ProposalJson)) return false;
            if (envelope.Status != PendingStatus && envelope.Status != status) return false;

            envelope.Status = status;
            envelope.ResultJson = resultJson ?? string.Empty;
            message.Content = JsonSerializer.Serialize(envelope);
            conversation.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
            return true;
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
                await Conversations
                    .Where(EfPredicateBuilder.Contains<AssistantConversationEntity, Guid>(excess, c => c.Id))
                    .ExecuteDeleteAsync(cancellationToken);
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
                await Messages
                    .Where(EfPredicateBuilder.Contains<AssistantMessageEntity, Guid>(excess, m => m.Id))
                    .ExecuteDeleteAsync(cancellationToken);
        }

        private static ProposalEnvelope DeserializeEnvelope(string json)
        {
            try { return JsonSerializer.Deserialize<ProposalEnvelope>(json); }
            catch (JsonException) { return null; }
        }

        private const string ProposalRole = "proposal";
        private const string PendingStatus = "pending";
        private const string AppliedStatus = "applied";
        private const string DismissedStatus = "dismissed";

        private sealed class ProposalEnvelope
        {
            public string ProposalJson { get; set; } = string.Empty;
            public string Status { get; set; } = PendingStatus;
            public string ResultJson { get; set; } = string.Empty;
        }
    }
}
