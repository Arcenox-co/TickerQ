using TickerQ.Utilities.Models;

namespace TickerQ.Utilities.Base
{
    /// <summary>
    /// Mutable, single-execution target that captures the result a ticker function publishes
    /// via <see cref="TickerFunctionContext.SetResult{T}(T, System.Text.Json.Serialization.Metadata.JsonTypeInfo{T})"/>.
    /// The runtime creates one per execution, shares it by reference with the typed context
    /// wrapper, and resets it at the start of each attempt so an earlier failed attempt's
    /// output can never be mistaken for the successful attempt's result.
    /// <para>
    /// <see cref="HasResult"/> stays <c>false</c> until something is published, keeping the
    /// absence of a result distinguishable from a published JSON null.
    /// </para>
    /// </summary>
    internal sealed class TickerResultSink
    {
        public bool HasResult { get; private set; }
        public TickerResultEnvelope Envelope { get; private set; }

        public void Set(TickerResultEnvelope envelope)
        {
            Envelope = envelope;
            HasResult = true;
        }

        public void Reset()
        {
            Envelope = null;
            HasResult = false;
        }
    }
}
