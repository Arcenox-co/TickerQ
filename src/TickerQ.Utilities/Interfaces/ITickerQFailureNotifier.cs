using TickerQ.Utilities.Models;

namespace TickerQ.Utilities.Interfaces
{
    /// <summary>
    /// Receives terminal failure events (job failed after exhausting retries,
    /// execution timeout, stale recovery) so operators hear about them without
    /// watching the dashboard. TickerQ ships a webhook implementation
    /// (<c>options.NotifyFailuresViaWebhook(url)</c>); register your own
    /// implementation for Slack/email/paging instead.
    /// Implementations must be non-blocking — Notify is called from the
    /// execution pipeline and should enqueue, not send inline.
    /// </summary>
    public interface ITickerQFailureNotifier
    {
        void Notify(TickerFailureEvent failureEvent);
    }
}
