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

        /// <summary>
        /// How long non-processing structured investigations are retained from creation
        /// (default 30 days). A configured state-specific window replaces this window while
        /// the investigation is in that state.
        /// </summary>
        public TimeSpan InvestigationRetention { get; set; } = TimeSpan.FromDays(30);

        /// <summary>
        /// Optional retention window for an investigation marked read, measured from
        /// the time it entered the Reviewed state. Null or a non-positive value disables it.
        /// </summary>
        public TimeSpan? ReadInvestigationRetention { get; set; }

        /// <summary>
        /// Optional retention window for an investigation marked resolved, measured
        /// from the time it entered the Resolved state. Null or a non-positive value disables it.
        /// </summary>
        public TimeSpan? ResolvedInvestigationRetention { get; set; }
    }
}
