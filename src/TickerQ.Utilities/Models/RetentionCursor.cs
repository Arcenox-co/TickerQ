using System;

namespace TickerQ.Utilities.Models
{
    public readonly struct RetentionCursor : IEquatable<RetentionCursor>
    {
        private RetentionCursor(DateTime executedAt, Guid id, bool hasValue)
        {
            ExecutedAt = executedAt;
            Id = id;
            HasValue = hasValue;
        }

        public static RetentionCursor Start => default;
        public static RetentionCursor After(DateTime executedAt, Guid id) => new(executedAt, id, true);
        public bool HasValue { get; }
        public DateTime ExecutedAt { get; }
        public Guid Id { get; }

        public bool IsBefore(DateTime executedAt, Guid id)
            => !HasValue || executedAt > ExecutedAt || executedAt == ExecutedAt && id.CompareTo(Id) > 0;

        public bool Equals(RetentionCursor other)
            => HasValue == other.HasValue && ExecutedAt == other.ExecutedAt && Id == other.Id;
        public override bool Equals(object obj) => obj is RetentionCursor other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(HasValue, ExecutedAt, Id);
    }
}
