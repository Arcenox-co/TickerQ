using System;
using System.Collections.Generic;

namespace TickerQ.Utilities.Models
{
    /// <summary>
    /// A durable, structured diagnosis of one terminal execution. Unlike a chat message,
    /// this record can be searched, reviewed, linked back to the execution, and marked as
    /// recovered when a later run of the same function succeeds.
    /// </summary>
    public sealed class AssistantInvestigationRecord
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

    public sealed class AssistantInvestigationQuery
    {
        public string Search { get; set; }
        public string State { get; set; }
        public string ReviewState { get; set; }
        public string Classification { get; set; }
        /// <summary>
        /// Pages distinct function groups while returning the investigations that belong to
        /// those groups. This keeps one function's history on one dashboard card instead of
        /// splitting it across pages.
        /// </summary>
        public bool GroupByFunction { get; set; }
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 20;
    }

    public sealed class AssistantInvestigationPage
    {
        public IReadOnlyList<AssistantInvestigationRecord> Items { get; set; } = Array.Empty<AssistantInvestigationRecord>();
        public int TotalCount { get; set; }
        public int PageNumber { get; set; }
        public int PageSize { get; set; }
        public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
    }

    public sealed class AssistantInvestigationPattern
    {
        public string FunctionName { get; set; }
        public string Classification { get; set; }
        public int Count { get; set; }
        public DateTime LatestAt { get; set; }
    }

    public sealed class AssistantInvestigationOverview
    {
        public int Total { get; set; }
        public int AwaitingReview { get; set; }
        public int CompletedToday { get; set; }
        public int Recovered { get; set; }
        public int Processing { get; set; }
        public IReadOnlyList<AssistantInvestigationRecord> Recent { get; set; } = Array.Empty<AssistantInvestigationRecord>();
        public IReadOnlyList<AssistantInvestigationPattern> Patterns { get; set; } = Array.Empty<AssistantInvestigationPattern>();
    }

    /// <summary>Stable string values persisted by investigation stores and exposed to dashboard clients.</summary>
    public static class AssistantInvestigationValues
    {
        public const string Processing = "Processing";
        public const string Completed = "Completed";
        public const string Failed = "Failed";
        /// <summary>
        /// Hidden tombstone retained to prevent an automatic sweep from investigating the same
        /// execution again after an operator deletes its saved diagnosis.
        /// </summary>
        public const string Dismissed = "Dismissed";
        public const string New = "New";
        public const string Reviewed = "Reviewed";
        public const string Resolved = "Resolved";
        public const string User = "User";
        public const string Automatic = "Automatic";
        public const string AutomaticReuse = "Automatic reuse";
    }
}
