namespace TickerQ.Utilities.Interfaces;

/// <summary>
/// Coalesced in-process hint that durable Node finalization work may now be due. Correctness
/// never depends on delivery: the provider-backed reconciler also drains at startup and polls.
/// </summary>
public interface INodeFinalizationWakeSignal
{
    void Wake();
}
