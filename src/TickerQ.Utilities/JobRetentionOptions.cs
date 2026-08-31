using System;

namespace TickerQ.Utilities
{
    /// <summary>Configures automatic cleanup of historical terminal ticker records.</summary>
    public sealed class JobRetentionOptions
    {
        public TimeSpan? DeleteSucceededAfter { get; set; }
        public TimeSpan? DeleteFailedAfter { get; set; }
        public TimeSpan? DeleteCancelledAfter { get; set; }
        public TimeSpan? DeleteSkippedAfter { get; set; }
        public TimeSpan SweepInterval { get; set; } = TimeSpan.FromHours(1);
        public int BatchSize { get; set; } = 500;
        public int MaxNodesPerChain { get; set; } = 1_000;
        public int MaxBatchesPerSweep { get; set; } = 10;

        public bool IsEnabled => DeleteSucceededAfter.HasValue
            || DeleteFailedAfter.HasValue
            || DeleteCancelledAfter.HasValue
            || DeleteSkippedAfter.HasValue;

        public void Validate()
        {
            RequirePositive(DeleteSucceededAfter, nameof(DeleteSucceededAfter));
            RequirePositive(DeleteFailedAfter, nameof(DeleteFailedAfter));
            RequirePositive(DeleteCancelledAfter, nameof(DeleteCancelledAfter));
            RequirePositive(DeleteSkippedAfter, nameof(DeleteSkippedAfter));

            if (SweepInterval <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(SweepInterval));
            if (BatchSize is < 1 or > MaxBatchSize)
                throw new ArgumentOutOfRangeException(nameof(BatchSize));
            if (MaxNodesPerChain is < 1 or > MaxNodesPerChainLimit)
                throw new ArgumentOutOfRangeException(nameof(MaxNodesPerChain));
            if (MaxBatchesPerSweep is < 1 or > MaxBatchesPerSweepLimit)
                throw new ArgumentOutOfRangeException(nameof(MaxBatchesPerSweep));
        }

        public const int MaxBatchSize = 10_000;
        public const int MaxNodesPerChainLimit = 10_000;
        public const int MaxBatchesPerSweepLimit = 1_000;

        private static void RequirePositive(TimeSpan? value, string name)
        {
            if (value is { } duration && duration <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(name);
        }
    }
}
