using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces;

namespace TickerQ.RemoteExecutor;

/// <summary>
/// Provider-backed capability accessors required before Node callback delegates may enter the
/// shared registry. Capabilities are evaluated on every sync so startup readiness can change
/// without replacing the singleton persistence provider. Missing capabilities fail closed.
/// </summary>
internal sealed class RemoteExecutorPersistenceCapabilities
{
    private static readonly Func<bool> Unsupported = static () => false;
    private readonly Func<bool> _supportsAcknowledgedTerminalUpdates;
    private readonly Func<bool> _supportsDurableNodeFinalizationOutbox;

    internal RemoteExecutorPersistenceCapabilities(
        bool supportsAcknowledgedTerminalUpdates,
        bool supportsDurableNodeFinalizationOutbox)
        : this(() => supportsAcknowledgedTerminalUpdates, () => supportsDurableNodeFinalizationOutbox)
    {
    }

    internal RemoteExecutorPersistenceCapabilities(
        Func<bool>? supportsAcknowledgedTerminalUpdates,
        Func<bool>? supportsDurableNodeFinalizationOutbox)
    {
        _supportsAcknowledgedTerminalUpdates = supportsAcknowledgedTerminalUpdates ?? Unsupported;
        _supportsDurableNodeFinalizationOutbox = supportsDurableNodeFinalizationOutbox ?? Unsupported;
    }

    internal static RemoteExecutorPersistenceCapabilities From<TTimeTicker, TCronTicker>(
        ITickerPersistenceProvider<TTimeTicker, TCronTicker> provider)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        ArgumentNullException.ThrowIfNull(provider);
        return new(
            () => provider.SupportsAcknowledgedTerminalUpdates,
            () => provider.SupportsDurableNodeFinalizationOutbox);
    }

    internal bool SupportsAcknowledgedTerminalUpdates => _supportsAcknowledgedTerminalUpdates();
    internal bool SupportsDurableNodeFinalizationOutbox => _supportsDurableNodeFinalizationOutbox();

    internal bool SupportsNodeCallbacks
    {
        get
        {
            // Deliberately avoid short-circuiting: each sync observes both provider-backed values.
            return SupportsAcknowledgedTerminalUpdates & SupportsDurableNodeFinalizationOutbox;
        }
    }
}