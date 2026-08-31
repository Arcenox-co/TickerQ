using System;
using TickerQ.Utilities.Enums;

namespace TickerQ.Utilities.Models
{
    public sealed class RetentionCutoffs
    {
        public RetentionCutoffs(DateTime? succeededBefore, DateTime? failedBefore, DateTime? cancelledBefore, DateTime? skippedBefore, int maxNodesPerChain = 1_000)
        {
            SucceededBefore = succeededBefore;
            FailedBefore = failedBefore;
            CancelledBefore = cancelledBefore;
            SkippedBefore = skippedBefore;
            MaxNodesPerChain = maxNodesPerChain;
        }

        public DateTime? SucceededBefore { get; }
        public DateTime? FailedBefore { get; }
        public DateTime? CancelledBefore { get; }
        public DateTime? SkippedBefore { get; }
        public int MaxNodesPerChain { get; }
        public bool HasAny => SucceededBefore.HasValue || FailedBefore.HasValue || CancelledBefore.HasValue || SkippedBefore.HasValue;

        public DateTime? ForStatus(TickerStatus status) => status switch
        {
            TickerStatus.Done or TickerStatus.DueDone => SucceededBefore,
            TickerStatus.Failed => FailedBefore,
            TickerStatus.Cancelled => CancelledBefore,
            TickerStatus.Skipped => SkippedBefore,
            _ => null
        };
    }
}
