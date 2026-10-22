using System;

namespace TickerQ.Utilities.Models
{
    /// <summary>One row in the assistant's "past chats" list.</summary>
    public sealed class AssistantConversationSummary
    {
        public Guid Id { get; set; }
        public string Title { get; set; }
        public string Model { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    /// <summary>One stored assistant chat turn.</summary>
    public sealed class AssistantHistoryMessage
    {
        public Guid Id { get; set; }

        /// <summary>"user", "assistant", or "proposal".</summary>
        public string Role { get; set; }

        public string Content { get; set; }
        public DateTime CreatedAt { get; set; }

        /// <summary>Serialized assistant proposal when <see cref="Role"/> is proposal.</summary>
        public string ProposalJson { get; set; } = string.Empty;

        /// <summary>Pending, applied, or dismissed for a stored proposal.</summary>
        public string ProposalStatus { get; set; } = string.Empty;

        /// <summary>Serialized application result, such as the saved draft link.</summary>
        public string ProposalResultJson { get; set; } = string.Empty;
    }

    /// <summary>A proposal emitted while producing one assistant exchange.</summary>
    public sealed class AssistantHistoryProposal
    {
        public Guid Id { get; set; }
        public string ProposalJson { get; set; } = string.Empty;
    }
}
