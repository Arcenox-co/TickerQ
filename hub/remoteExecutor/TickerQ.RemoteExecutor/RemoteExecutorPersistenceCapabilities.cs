using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces;

namespace TickerQ.RemoteExecutor;

/// <summary>
/// Immutable startup snapshot of the singleton persistence provider capabilities required before
/// Node callback delegates may enter the shared registry. A missing snapshot fails closed.
/// </summary>
internal sealed record RemoteExecutorPersistenceCapabilities(
    bool SupportsAcknowledgedTerminalUpdates,
    bool SupportsDurableNodeFinalizationOutbox)
{
    internal static RemoteExecutorPersistenceCapabilities From<TTimeTicker, TCronTicker>(
        ITickerPersistenceProvider<TTimeTicker, TCronTicker> provider)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        ArgumentNullException.ThrowIfNull(provider);
        return new(provider.SupportsAcknowledgedTerminalUpdates, provider.SupportsDurableNodeFinalizationOutbox);
    }

    internal bool SupportsNodeCallbacks =>
        SupportsAcknowledgedTerminalUpdates && SupportsDurableNodeFinalizationOutbox;
}