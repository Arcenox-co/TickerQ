using System;
using System.Collections.Generic;

namespace TickerQ.EntityFrameworkCore.Entities
{
    /// <summary>
    /// One AI-assistant chat thread, scoped by <see cref="UserId"/>. Mapped
    /// only when the operator opts in via
    /// <c>TickerQEfCoreOptionBuilder.AddAssistantHistory()</c> — headless
    /// setups get no new tables.
    /// </summary>
    public class AssistantConversationEntity
    {
        public Guid Id { get; set; }

        /// <summary>Stable auth subject (JWT sub / username) — never the display name.</summary>
        public string UserId { get; set; }

        /// <summary>Auto-derived from the first user message; shown in the history list.</summary>
        public string Title { get; set; }

        /// <summary>Model label the thread ran on (display only).</summary>
        public string Model { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        public List<AssistantMessageEntity> Messages { get; set; } = new();
    }

    /// <summary>One stored assistant chat turn.</summary>
    public class AssistantMessageEntity
    {
        public Guid Id { get; set; }
        public Guid ConversationId { get; set; }

        /// <summary>"user" or "assistant".</summary>
        public string Role { get; set; }

        /// <summary>Final rendered text (tool-call intermediates are not stored).</summary>
        public string Content { get; set; }

        /// <summary>Explicit ordering — never rely on timestamps.</summary>
        public int Ordinal { get; set; }

        public DateTime CreatedAt { get; set; }

        /// <summary>Optional per-turn token accounting for cost tracking.</summary>
        public int? TokensIn { get; set; }
        public int? TokensOut { get; set; }
    }
}
