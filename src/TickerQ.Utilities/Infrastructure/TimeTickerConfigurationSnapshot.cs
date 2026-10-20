using System;
using System.Collections.Generic;
using TickerQ.Utilities.Entities;

namespace TickerQ.Utilities.Infrastructure;

// Configuration writes may change configuration only. Runtime transitions use the
// acquisition-fenced provider methods, never a detached full-row replacement.
internal static class TimeTickerConfigurationSnapshot
{
    internal static bool Matches<T>(T current, T incoming, bool compareParent = true) where T : TimeTickerEntity<T>
        => current.Id == incoming.Id && (!compareParent || current.ParentId == incoming.ParentId) &&
           current.Status == incoming.Status && current.LockHolder == incoming.LockHolder &&
           current.LockedAt == incoming.LockedAt && current.LeaseUntil == incoming.LeaseUntil &&
           current.AcquisitionToken == incoming.AcquisitionToken && current.ExecutedAt == incoming.ExecutedAt &&
           current.ExceptionMessage == incoming.ExceptionMessage && current.SkippedReason == incoming.SkippedReason &&
           current.ElapsedTime == incoming.ElapsedTime && current.RetryCount == incoming.RetryCount &&
           current.ChainRootId == incoming.ChainRootId && current.ChainGeneration == incoming.ChainGeneration &&
           current.StaleRestartCount == incoming.StaleRestartCount;

    internal static bool TryFlatten<T>(IEnumerable<T> roots, out Dictionary<Guid, T> rows)
        where T : TimeTickerEntity<T>
    {
        rows = new Dictionary<Guid, T>();
        if (roots == null) return false;
        var pending = new Stack<T>(roots);
        while (pending.Count > 0)
        {
            var row = pending.Pop();
            if (row == null || row.Id == Guid.Empty || !rows.TryAdd(row.Id, row)) return false;
            if (row.Children == null) continue;
            foreach (var child in row.Children)
            {
                if (child == null || child.ParentId != row.Id) return false;
                pending.Push(child);
            }
        }
        return true;
    }
}
