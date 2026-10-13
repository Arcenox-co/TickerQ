namespace TickerQ.EntityFrameworkCore
{
    /// <summary>
    /// Retention settings for the EF-backed assistant chat history
    /// (<c>TickerQEfCoreOptionBuilder.AddAssistantHistory()</c>). Oldest
    /// conversations/messages beyond the caps are pruned automatically so the
    /// tables can't grow unbounded.
    /// </summary>
    public sealed class AssistantHistoryOptions
    {
        /// <summary>Max conversations kept per user (default 50).</summary>
        public int MaxConversationsPerUser { get; set; } = 50;

        /// <summary>Max messages kept per conversation (default 80).</summary>
        public int MaxMessagesPerConversation { get; set; } = 80;

        /// <summary>
        /// Fallback for design-time model building (dotnet ef migrations),
        /// where the option builder may not be resolvable from DI. Set when
        /// AddAssistantHistory() is called — same lifecycle as the schema
        /// fallback in TickerQDbContext.
        /// </summary>
        internal static AssistantHistoryOptions Current { get; set; }
    }
}
