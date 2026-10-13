using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ.Utilities.Temps
{
    /// <summary>Default when no failure notification is configured.</summary>
    internal sealed class NoOpTickerQFailureNotifier : ITickerQFailureNotifier
    {
        public void Notify(TickerFailureEvent failureEvent)
        {
        }
    }
}
