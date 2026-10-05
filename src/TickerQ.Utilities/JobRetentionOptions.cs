using System;

namespace TickerQ.Utilities
{
    /// <summary>
    /// Configures the built-in job-retention maintenance loop, which periodically deletes
    /// historical terminal ticker records (completed <see cref="TTimeTicker"/> chains and
    /// <c>CronTickerOccurrence</c> rows) whose <c>ExecutedAt</c> is older than a per-status window.
    /// <para>
    /// Disabled by default: retention runs only when at least one window is set (<see cref="IsEnabled"/>).
    /// A null window means "retain forever" for that status. Cron <b>definitions</b> are never deleted.
    /// </para>
    /// </summary>
    public sealed class JobRetentionOptions
    {
        /// <summary>
        /// Delete successfully-completed tickers (<c>Done</c> and <c>DueDone</c>) whose <c>ExecutedAt</c>
        /// is older than this window. Null retains them forever.
        /// </summary>
        public TimeSpan? DeleteSucceededAfter { get; set; }

        /// <summary>Delete <c>Failed</c> tickers older than this window. Null retains them forever.</summary>
        public TimeSpan? DeleteFailedAfter { get; set; }

        /// <summary>Delete <c>Cancelled</c> tickers older than this window. Null retains them forever.</summary>
        public TimeSpan? DeleteCancelledAfter { get; set; }

        /// <summary>Delete <c>Skipped</c> tickers older than this window. Null retains them forever.</summary>
        public TimeSpan? DeleteSkippedAfter { get; set; }

        /// <summary>How often the retention maintenance loop runs. Defaults to 1 hour.</summary>
        public TimeSpan SweepInterval { get; set; } = TimeSpan.FromHours(1);

        /// <summary>
        /// Maximum number of eligible aggregates (time-ticker chains or cron occurrences) a single
        /// provider delete call may remove. Bounds each batch so a sweep never loads/deletes unboundedly.
        /// Defaults to 500.
        /// </summary>
        public int BatchSize { get; set; } = 500;

        /// <summary>
        /// Maximum number of nodes retention may materialize or delete from one time-ticker chain.
        /// Oversized chains fail closed and remain intact. Defaults to 1,000.
        /// </summary>
        public int MaxNodesPerChain { get; set; } = 1_000;

        /// <summary>
        /// Maximum number of provider delete calls a single sweep issues across both retention streams
        /// before yielding until the next <see cref="SweepInterval"/>. Defaults to 10.
        /// </summary>
        public int MaxBatchesPerSweep { get; set; } = 10;

        /// <summary>
        /// True when retention is active — i.e. at least one status window is configured. There is
        /// deliberately no separate enable flag: configuring a window is the opt-in.
        /// </summary>
        public bool IsEnabled =>
            DeleteSucceededAfter.HasValue
            || DeleteFailedAfter.HasValue
            || DeleteCancelledAfter.HasValue
            || DeleteSkippedAfter.HasValue;

        /// <summary>
        /// Validates the configured values. Any window that is set must be strictly positive, and the
        /// sweep interval and batch bounds must be strictly positive. Throws
        /// <see cref="ArgumentOutOfRangeException"/> with a clear message otherwise. A fully-disabled
        /// (no windows) configuration is valid — retention simply stays off.
        /// </summary>
        public void Validate()
        {
            RequirePositiveWindow(DeleteSucceededAfter, nameof(DeleteSucceededAfter));
            RequirePositiveWindow(DeleteFailedAfter, nameof(DeleteFailedAfter));
            RequirePositiveWindow(DeleteCancelledAfter, nameof(DeleteCancelledAfter));
            RequirePositiveWindow(DeleteSkippedAfter, nameof(DeleteSkippedAfter));

            if (SweepInterval <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(SweepInterval), SweepInterval,
                    "JobRetentionOptions.SweepInterval must be a positive duration.");

            if (BatchSize < 1 || BatchSize > MaxBatchSize)
                throw new ArgumentOutOfRangeException(nameof(BatchSize), BatchSize,
                    $"JobRetentionOptions.BatchSize must be between 1 and {MaxBatchSize} records per batch.");

            if (MaxNodesPerChain < 1 || MaxNodesPerChain > MaxNodesPerChainLimit)
                throw new ArgumentOutOfRangeException(nameof(MaxNodesPerChain), MaxNodesPerChain,
                    $"JobRetentionOptions.MaxNodesPerChain must be between 1 and {MaxNodesPerChainLimit} nodes.");

            if (MaxBatchesPerSweep < 1 || MaxBatchesPerSweep > MaxBatchesPerSweepLimit)
                throw new ArgumentOutOfRangeException(nameof(MaxBatchesPerSweep), MaxBatchesPerSweep,
                    $"JobRetentionOptions.MaxBatchesPerSweep must be between 1 and {MaxBatchesPerSweepLimit} batches per sweep.");
        }

        /// <summary>Hard upper bound for <see cref="BatchSize"/>, keeping a single batch's work bounded.</summary>
        public const int MaxBatchSize = 10_000;

        /// <summary>Hard upper bound for one time-chain traversal.</summary>
        public const int MaxNodesPerChainLimit = 10_000;

        /// <summary>Hard upper bound for <see cref="MaxBatchesPerSweep"/>, keeping a single sweep's work bounded.</summary>
        public const int MaxBatchesPerSweepLimit = 1_000;

        private static void RequirePositiveWindow(TimeSpan? window, string name)
        {
            if (window.HasValue && window.Value <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(name, window.Value,
                    $"JobRetentionOptions.{name} must be a positive duration when set (null retains forever).");
        }
    }
}
