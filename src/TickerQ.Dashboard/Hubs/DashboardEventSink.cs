using System;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Events;
using TickerQ.Utilities.Interfaces;

namespace TickerQ.Dashboard.Hubs;

/// <summary>Tells open dashboards to refetch promises when one changes, with the <c>PromisesChanged</c> SignalR hint.</summary>
internal sealed class DashboardEventSink(ITickerQNotificationHubSender hub) : ITickerQEventSink
{
    public ValueTask PublishAsync(TickerQEvent e, CancellationToken ct)
        => e.Kind.StartsWith("promise.", StringComparison.Ordinal) ? new ValueTask(hub.PromisesChangedAsync()) : default;
}
