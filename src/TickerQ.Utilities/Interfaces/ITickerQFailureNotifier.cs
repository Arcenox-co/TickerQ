using TickerQ.Utilities.Models;

namespace TickerQ.Utilities.Interfaces
{
    /// <summary>
    /// Receives terminal failure events (job failed after exhausting retries,
    /// execution timeout, stale recovery) so operators hear about them without
    /// watching the dashboard. The optional Integrations package supplies
    /// managed Slack, Teams, and webhook delivery; applications can also
    /// register their own implementation.
    /// Implementations must be non-blocking — Notify is called from the
    /// execution pipeline and should enqueue, not send inline.
    /// </summary>
    public interface ITickerQFailureNotifier
    {
        void Notify(TickerFailureEvent failureEvent);
    }
}
