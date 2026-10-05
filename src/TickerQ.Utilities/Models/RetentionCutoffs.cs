using System;
using TickerQ.Utilities.Enums;

namespace TickerQ.Utilities.Models
{
    /// <summary>
    /// Absolute UTC cutoff instants for one retention sweep, one per terminal status class. A row is
    /// eligible for deletion only when its <c>ExecutedAt</c> is strictly older than the cutoff matching
    /// its status. A null cutoff means that status is retained forever (never eligible).
    /// <para>
    /// Computed once per sweep from <c>ITickerClock.UtcNow</c> minus the configured window, so every
    /// candidate in a sweep is judged against the same instant. Never derived from CreatedAt/UpdatedAt/
    /// ExecutionTime.
    /// </para>
    /// </summary>
    public sealed class RetentionCutoffs
    {
        public RetentionCutoffs(
            DateTime? succeededBefore,
            DateTime? failedBefore,
            DateTime? cancelledBefore,
            DateTime? skippedBefore,
            int maxNodesPerChain = 1_000)
        {
            SucceededBefore = succeededBefore;
            FailedBefore = failedBefore;
            CancelledBefore = cancelledBefore;
            SkippedBefore = skippedBefore;
            MaxNodesPerChain = maxNodesPerChain;
        }

        /// <summary>Cutoff for successfully-completed rows (<see cref="TickerStatus.Done"/> and <see cref="TickerStatus.DueDone"/>).</summary>
        public DateTime? SucceededBefore { get; }

        /// <summary>Cutoff for <see cref="TickerStatus.Failed"/> rows.</summary>
        public DateTime? FailedBefore { get; }

        /// <summary>Cutoff for <see cref="TickerStatus.Cancelled"/> rows.</summary>
        public DateTime? CancelledBefore { get; }

        /// <summary>Cutoff for <see cref="TickerStatus.Skipped"/> rows.</summary>
        public DateTime? SkippedBefore { get; }

        /// <summary>Maximum nodes a provider may traverse for one chain before retaining it intact.</summary>
        public int MaxNodesPerChain { get; }

        /// <summary>True when at least one status class has a configured cutoff.</summary>
        public bool HasAny =>
            SucceededBefore.HasValue || FailedBefore.HasValue
            || CancelledBefore.HasValue || SkippedBefore.HasValue;

        /// <summary>
        /// The cutoff for a given status, or null when that status is not a retention target (non-terminal)
        /// or is configured to be retained forever. Centralizes the Done+DueDone → succeeded mapping so
        /// every provider applies identical status semantics.
        /// </summary>
        public DateTime? ForStatus(TickerStatus status) => status switch
        {
            TickerStatus.Done => SucceededBefore,
            TickerStatus.DueDone => SucceededBefore,
            TickerStatus.Failed => FailedBefore,
            TickerStatus.Cancelled => CancelledBefore,
            TickerStatus.Skipped => SkippedBefore,
            _ => null
        };
    }
}
