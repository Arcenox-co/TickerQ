using System.Collections.Generic;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;

namespace TickerQ.Utilities.Infrastructure
{
    /// <summary>Determines whether a root's deletion policy still protects its descendants.</summary>
    internal static class ChainDeletionPolicy
    {
        public static bool IsTerminal(TickerStatus status) => status is
            TickerStatus.Done or TickerStatus.DueDone or TickerStatus.Failed or TickerStatus.Cancelled or TickerStatus.Skipped;

        /// <summary>
        /// Whether a node belongs to work that has started: it runs, holds an unexpired lease, or holds
        /// an acquisition generation past Queued. A Queued node's generation is only the scheduler's claim
        /// on work that has not started; deleting it is safe, because moving to InProgress is fenced on that
        /// generation and finds no row once it is gone. Pending cancellation treats Queued work the same way.
        /// </summary>
        public static bool IsStarted(TickerStatus status, System.Guid? acquisitionToken, System.DateTime? leaseUntil,
            System.DateTime now)
            => status == TickerStatus.InProgress ||
               (acquisitionToken.HasValue && status != TickerStatus.Queued) ||
               leaseUntil > now;

        /// <summary>Use the complete descendant set from the same snapshot as the root.</summary>
        public static bool BlocksDeletion(ChainDeleteBehavior behavior, IEnumerable<TickerStatus> descendantStatuses)
        {
            if (behavior == ChainDeleteBehavior.Cascade) return false;
            foreach (var status in descendantStatuses)
                if (behavior != ChainDeleteBehavior.Restrict || !IsTerminal(status)) return true;
            return false;
        }

        /// <summary>Checks all descendants in a loaded hierarchy. Malformed cycles retain the chain.</summary>
        public static bool BlocksDeletion<TTimeTicker>(TTimeTicker root)
            where TTimeTicker : TimeTickerEntity<TTimeTicker>
        {
            if (root.DeleteBehavior == ChainDeleteBehavior.Cascade) return false;
            var pending = new Stack<TTimeTicker>(root.GetChildrenForRead() ?? System.Array.Empty<TTimeTicker>());
            var seen = new HashSet<System.Guid> { root.Id };
            while (pending.Count > 0)
            {
                var child = pending.Pop();
                if (!seen.Add(child.Id) || root.DeleteBehavior != ChainDeleteBehavior.Restrict || !IsTerminal(child.Status))
                    return true;
                var descendants = child.GetChildrenForRead();
                if (descendants != null)
                    foreach (var descendant in descendants) pending.Push(descendant);
            }
            return false;
        }
    }
}
