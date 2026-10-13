using System;

namespace TickerQ.Utilities.Models
{
    /// <summary>
    /// Deterministic keyset continuation position for retention time-chain traversal, ordered by
    /// <c>(ExecutedAt, Id)</c> ascending. <see cref="Start"/> (the <c>default</c>) means "from the
    /// beginning". A provider selects candidate roots strictly after this position, so a chain that was
    /// examined-but-retained (a blocked aggregate) is not reselected on the next call and therefore cannot
    /// starve later eligible aggregates.
    /// <para>
    /// Provider-neutral: the same typed keyset works for EF Core (SQL keyset + LIMIT), MongoDB, Redis
    /// (sorted-set range), and the in-memory provider. It carries no provider-specific string. When a
    /// traversal reaches the end, the provider returns <see cref="Start"/> as the next cursor so a later
    /// sweep wraps around and reconsiders records that became eligible behind the previous position.
    /// </para>
    /// </summary>
    public readonly struct RetentionCursor : IEquatable<RetentionCursor>
    {
        private RetentionCursor(DateTime executedAt, Guid id, bool hasValue)
        {
            ExecutedAt = executedAt;
            Id = id;
            HasValue = hasValue;
        }

        /// <summary>The start-of-traversal position (no lower bound). This is the <c>default</c> value.</summary>
        public static RetentionCursor Start => default;

        /// <summary>A keyset position immediately after the row with this <c>(ExecutedAt, Id)</c>.</summary>
        public static RetentionCursor After(DateTime executedAt, Guid id) => new(executedAt, id, true);

        /// <summary>False for <see cref="Start"/>; true once positioned after a specific row.</summary>
        public bool HasValue { get; }

        /// <summary>The <c>ExecutedAt</c> of the last examined row (UTC). Meaningful only when <see cref="HasValue"/>.</summary>
        public DateTime ExecutedAt { get; }

        /// <summary>The <c>Id</c> of the last examined row (tie-breaker). Meaningful only when <see cref="HasValue"/>.</summary>
        public Guid Id { get; }

        /// <summary>
        /// True when the row identified by <paramref name="executedAt"/>/<paramref name="id"/> sorts strictly
        /// after this cursor under <c>(ExecutedAt, Id)</c> ascending ordering. Used by the in-memory provider;
        /// relational/Redis providers express the same predicate in their query language.
        /// </summary>
        public bool IsBefore(DateTime executedAt, Guid id)
        {
            if (!HasValue)
                return true;
            if (executedAt > ExecutedAt)
                return true;
            if (executedAt < ExecutedAt)
                return false;
            return id.CompareTo(Id) > 0;
        }

        public bool Equals(RetentionCursor other)
            => HasValue == other.HasValue && ExecutedAt == other.ExecutedAt && Id == other.Id;

        public override bool Equals(object obj) => obj is RetentionCursor other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(HasValue, ExecutedAt, Id);
        public override string ToString() => HasValue ? $"After({ExecutedAt:O},{Id})" : "Start";
    }
}
