using System;

namespace TickerQ.Utilities.Models
{
    /// <summary>
    /// A compact ownership record for generation-fenced lease operations: the id of a root
    /// ticker (time ticker or cron occurrence) this node is executing, paired with the
    /// <see cref="AcquisitionToken"/> generation it was acquired under.
    ///
    /// The token pins the exact InProgress generation. Lease renewal and held-id checks match
    /// on <c>id + token</c> so that a row this node re-acquired under a newer generation (or one
    /// another node recovered and re-acquired) is not renewed/confirmed under a stale owner's
    /// snapshot. A null <see cref="AcquisitionToken"/> means "generation unknown" and reliability-
    /// capable providers treat it as fail-closed (never renewed / never confirmed held).
    /// </summary>
    public readonly struct AcquisitionLease : IEquatable<AcquisitionLease>
    {
        public AcquisitionLease(Guid tickerId, Guid? acquisitionToken)
        {
            TickerId = tickerId;
            AcquisitionToken = acquisitionToken;
        }

        public Guid TickerId { get; }
        public Guid? AcquisitionToken { get; }

        public bool Equals(AcquisitionLease other)
            => TickerId == other.TickerId && AcquisitionToken == other.AcquisitionToken;

        public override bool Equals(object obj) => obj is AcquisitionLease other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(TickerId, AcquisitionToken);
    }
}
