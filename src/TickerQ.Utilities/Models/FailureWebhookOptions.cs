using System.Collections.Generic;

namespace TickerQ.Utilities.Models
{
    /// <summary>Config for the built-in webhook failure notifier.</summary>
    public class FailureWebhookOptions
    {
        public string Url { get; set; }
        /// <summary>Extra request headers, e.g. an auth token.</summary>
        public IDictionary<string, string> Headers { get; set; }
    }
}
