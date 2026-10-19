namespace TickerQ.Utilities.Models
{
    /// <summary>
    /// Phase of the durable reconciliation activation epoch. The epoch only ever moves forward through
    /// these phases (and forward across epoch numbers); a lower phase/epoch never overwrites a higher one.
    /// </summary>
    public enum ActivationEpochPhase
    {
        /// <summary>No epoch has been claimed on this store yet (pre-epoch, first adoption).</summary>
        Pending = 0,

        /// <summary>An epoch has been claimed and reconciliation is in progress but not yet durable.</summary>
        Activating = 1,

        /// <summary>
        /// The epoch is durably activated: schema upgrade, discoverability/chain repair, defined-Cron
        /// reconciliation, and durable finalization all completed. This is the single atomic boundary
        /// that old rolling nodes observe — they see either a pre-epoch/activating store or this one.
        /// </summary>
        Activated = 2
    }

    /// <summary>
    /// Immutable snapshot of a store's durable activation epoch. Persisted per provider (EF metadata
    /// singleton, Mongo metadata document, Redis epoch key, in-memory latch) and read at startup so the
    /// initializer can fence the scheduler until <see cref="ActivationEpochPhase.Activated"/> is reached.
    /// </summary>
    public sealed class ActivationEpochState
    {
        public long Epoch { get; init; }
        public ActivationEpochPhase Phase { get; init; }

        /// <summary>Opaque, provider-persisted resume token for the in-progress phase (idempotent restart).</summary>
        public string Checkpoint { get; init; }

        /// <summary>True when the store is durably activated at or beyond <paramref name="targetEpoch"/>.</summary>
        public bool IsActivatedFor(long targetEpoch)
            => Phase == ActivationEpochPhase.Activated && Epoch >= targetEpoch;

        /// <summary>The initial state of a store that has never seen an activation epoch.</summary>
        public static ActivationEpochState PreEpoch { get; } =
            new() { Epoch = 0, Phase = ActivationEpochPhase.Pending, Checkpoint = null };

        public override string ToString() => $"epoch={Epoch}, phase={Phase}, checkpoint={Checkpoint ?? "<none>"}";
    }
}
