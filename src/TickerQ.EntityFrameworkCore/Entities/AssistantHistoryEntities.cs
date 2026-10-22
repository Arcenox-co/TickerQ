using System;
using System.Collections.Generic;

namespace TickerQ.EntityFrameworkCore.Entities
{
    /// <summary>
    /// One AI-assistant chat thread, scoped by <see cref="UserId"/>. The table is
    /// part of the complete TickerQ EF schema, while storage services are enabled
    /// only through <c>TickerQEfCoreOptionBuilder.AddAssistantHistory()</c>.
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

        /// <summary>"user", "assistant", or a serialized "proposal" card.</summary>
        public string Role { get; set; }

        /// <summary>Final rendered text, or a proposal envelope for proposal rows.</summary>
        public string Content { get; set; }

        /// <summary>Explicit ordering — never rely on timestamps.</summary>
        public int Ordinal { get; set; }

        public DateTime CreatedAt { get; set; }

        /// <summary>Optional per-turn token accounting for cost tracking.</summary>
        public int? TokensIn { get; set; }
        public int? TokensOut { get; set; }
    }

    /// <summary>
    /// One shared operational investigation in an application namespace. It is intentionally
    /// separate from per-user chat messages so automatic investigations can be deduplicated
    /// across nodes and reviewed by another authorized operator.
    /// </summary>
    public class AssistantInvestigationEntity
    {
        public Guid Id { get; set; }
        public string ApplicationNamespace { get; set; }
        public Guid ExecutionId { get; set; }
        public string ExecutionType { get; set; }
        public Guid? ChainRootId { get; set; }
        public string FunctionName { get; set; }
        public string ExecutionStatus { get; set; }
        public string Classification { get; set; }
        public string Summary { get; set; }
        public string Model { get; set; }
        public string Trigger { get; set; }
        public string State { get; set; }
        public string ReviewState { get; set; }
        public string EvidenceFingerprint { get; set; }
        public string SourceRepository { get; set; }
        public string SourceRevision { get; set; }
        public string SourcePath { get; set; }
        public int? SourceLine { get; set; }
        public Guid? ConversationId { get; set; }
        public string StartedByUserId { get; set; }
        public string ReviewedByUserId { get; set; }
        public DateTime OccurredAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public DateTime? ReviewedAt { get; set; }
        public Guid? RecoveredByExecutionId { get; set; }
        public DateTime? RecoveredAt { get; set; }
        public string FailureMessage { get; set; }
    }

    /// <summary>
    /// Cluster-wide lease for one automatic-investigation evidence fingerprint. The lease is
    /// renewed while the model call is active and can be recovered after its owner disappears.
    /// </summary>
    public class AssistantInvestigationLeaseEntity
    {
        public string ApplicationNamespace { get; set; }
        public string EvidenceFingerprint { get; set; }
        public Guid OwnerToken { get; set; }
        public DateTime LeaseUntil { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    /// <summary>
    /// Namespace-wide rolling model-call budget. Starts are stored as UTC ticks and updated with
    /// an optimistic version check so all nodes enforce one shared hourly limit.
    /// </summary>
    public class AssistantInvestigationBudgetEntity
    {
        public string ApplicationNamespace { get; set; }
        public string StartedAtTicks { get; set; }
        public long Version { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
