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
        /// <summary>"user" or "assistant".</summary>
        public string Role { get; set; }

        public string Content { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
