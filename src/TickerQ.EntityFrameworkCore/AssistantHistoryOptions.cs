using System;

namespace TickerQ.EntityFrameworkCore
{
    /// <summary>
    /// Retention settings for EF-backed assistant conversations and structured
    /// run investigations (<c>TickerQEfCoreOptionBuilder.AddAssistantHistory()</c>).
    /// Oldest records beyond the caps are pruned automatically.
    /// </summary>
    public sealed class AssistantHistoryOptions
    {
        /// <summary>Max conversations kept per user (default 50).</summary>
        public int MaxConversationsPerUser { get; set; } = 50;

        /// <summary>Max messages kept per conversation (default 80).</summary>
        public int MaxMessagesPerConversation { get; set; } = 80;

        /// <summary>Max structured run investigations kept per application namespace (default 500).</summary>
        public int MaxInvestigationsPerNamespace { get; set; } = 500;

        /// <summary>How long completed structured investigations are retained (default 30 days).</summary>
        public TimeSpan InvestigationRetention { get; set; } = TimeSpan.FromDays(30);

        /// <summary>
        /// Fallback for design-time model building (dotnet ef migrations),
        /// where the option builder may not be resolvable from DI. Set when
        /// AddAssistantHistory() is called — same lifecycle as the schema
        /// fallback in TickerQDbContext.
        /// </summary>
        internal static AssistantHistoryOptions Current { get; set; }
    }
}
