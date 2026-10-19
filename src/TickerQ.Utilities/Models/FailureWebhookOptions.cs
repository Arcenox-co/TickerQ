using System.Collections.Generic;
using System;

namespace TickerQ.Utilities.Models
{
    /// <summary>Config for the built-in webhook failure notifier.</summary>
    public class FailureWebhookOptions
    {
        public string Url { get; set; }
        /// <summary>Maximum time to drain queued alerts after job producers stop. Also bounded by the host shutdown deadline.</summary>
        public TimeSpan ShutdownDrainTimeout { get; set; } = TimeSpan.FromSeconds(10);
        /// <summary>Extra request headers, e.g. an auth token.</summary>
        public IDictionary<string, string> Headers { get; set; }

        /// <summary>
        /// Sanitizes failure reasons before they enter the webhook queue. The default
        /// removes line breaks and truncates to 2,048 characters. Set explicitly when
        /// application-specific secrets require redaction.
        /// </summary>
        public Func<string, string> ReasonSanitizer { get; set; } = DefaultReasonSanitizer;

        internal static string DefaultReasonSanitizer(string reason)
        {
            if (string.IsNullOrEmpty(reason)) return string.Empty;
            var sanitized = reason.Replace('\r', ' ').Replace('\n', ' ');
            return sanitized.Length <= 2048 ? sanitized : sanitized[..2048];
        }
    }
}
