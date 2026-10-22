using System;
using System.Collections.Generic;
using System.Linq;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Exceptions;
using TickerQ.Utilities.Models;

namespace TickerQ.Utilities.Infrastructure;

/// <summary>
/// Computes and applies the durable state transition for an in-place chain retry. Persistence
/// providers invoke this only inside their graph transaction/CAS boundary.
/// </summary>
internal static class TimeTickerChainRetryPlanner
{
    internal sealed class Plan<TTimeTicker> where TTimeTicker : TimeTickerEntity<TTimeTicker>
    {
        internal required TTimeTicker Root { get; init; }
        internal required Guid SelectedId { get; init; }
        internal required Guid Generation { get; init; }
        internal required TickerStatus PreviousRootStatus { get; init; }
        internal required HashSet<Guid> ResetIds { get; init; }
        internal required HashSet<Guid> PassThroughIds { get; init; }
        internal required HashSet<Guid> ReusedTerminalIds { get; init; }
    }

    internal static Plan<TTimeTicker> Prepare<TTimeTicker>(
        TTimeTicker root,
        Guid selectedId,
        string lockHolder,
        DateTime executionTime,
        DateTime now,
        DateTime? leaseUntil)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>
    {
        ArgumentNullException.ThrowIfNull(root);
        if (root.ParentId.HasValue)
            throw new TickerValidatorException("The persisted retry owner is not a chain root.");

        var byId = new Dictionary<Guid, TTimeTicker>();
        var parentById = new Dictionary<Guid, Guid?>();
        var pending = new Stack<(TTimeTicker Node, Guid? Parent)>();
        pending.Push((root, null));
        while (pending.TryPop(out var item))
        {
            var node = item.Node;
            if (node == null || node.Id == Guid.Empty || !byId.TryAdd(node.Id, node))
                throw new TickerValidatorException("The selected chain contains a null, duplicate, or cyclic job identity.");
            parentById[node.Id] = item.Parent;
            var children = node.GetChildrenForRead();
            if (children == null) continue;
            foreach (var child in children)
            {
                if (child == null || child.ParentId != node.Id)
                    throw new TickerValidatorException("The selected chain contains an invalid parent relationship.");
                pending.Push((child, node.Id));
            }
        }

        if (!byId.ContainsKey(selectedId))
            throw new TickerValidatorException("The selected chain job was not found in its owning chain.");
        if (byId.Count > 1_000)
            throw new TickerValidatorException("The selected chain exceeds the safe retry limit of 1,000 jobs.");
        if (byId.Values.Any(node => !ChainDeletionPolicy.IsTerminal(node.Status) ||
                                    node.AcquisitionToken.HasValue ||
                                    node.LeaseUntil is { } lease && lease > now))
            throw new TickerValidatorException(
                "The chain is still active or has unfinished jobs. Wait for it to settle before retrying a step.");

        var path = new List<Guid>();
        for (Guid? current = selectedId; current.HasValue; current = parentById[current.Value])
            path.Add(current.Value);
        path.Reverse();
        if (path.Count == 0 || path[0] != root.Id)
            throw new TickerValidatorException("The selected job is not connected to its persisted chain root.");
        if (path.Count > 50)
            throw new TickerValidatorException("The selected chain exceeds the safe retry depth of 50 levels.");

        var pathSet = path.ToHashSet();
        var passThrough = path.Take(path.Count - 1).ToHashSet();
        var reset = new HashSet<Guid>();
        AddSubtree(byId[selectedId], reset);
        var recalculatedGroups = GetRecalculatedGroups(path, byId);

        // A grouped branch feeds an aggregate coordinator whose conditional continuations are
        // siblings of that branch in storage. Reset those continuations as downstream work and
        // retain other completed branch outcomes for the aggregate recalculation. When the
        // selected path is already below a group outcome, the settled group is only an ancestor:
        // keep it as pass-through and do not replay its other outcome jobs.
        foreach (var id in recalculatedGroups)
        {
            var node = byId[id];
            reset.Add(id);
            passThrough.Remove(id);
            foreach (var child in node.GetChildrenForRead())
            {
                if (pathSet.Contains(child.Id)) continue;
                var condition = child.RunCondition ?? RunCondition.OnAnyCompletedStatus;
                if (TickerChainInfrastructure.IsParallelBranchCondition(condition))
                    continue;
                AddSubtree(child, reset);
            }
        }

        var reused = new HashSet<Guid>();
        foreach (var groupId in recalculatedGroups)
        {
            foreach (var child in byId[groupId].GetChildrenForRead())
            {
                var condition = child.RunCondition ?? RunCondition.OnAnyCompletedStatus;
                if (!pathSet.Contains(child.Id) && !reset.Contains(child.Id) &&
                    TickerChainInfrastructure.IsParallelBranchCondition(condition))
                    reused.Add(child.Id);
            }
        }

        var previousRootStatus = root.Status;
        var generation = Guid.NewGuid();
        foreach (var node in byId.Values)
        {
            node.ChainRootId = root.Id;
            node.ChainGeneration = generation;
            node.AcquisitionToken = null;
            if (reset.Contains(node.Id))
                ResetPending(node, now);
        }

        // The original root owns the retry lease even when its function is only a pass-through.
        root.Status = TickerStatus.InProgress;
        root.ExecutionTime = executionTime;
        root.LockHolder = lockHolder;
        root.LockedAt = now;
        root.LeaseUntil = leaseUntil;
        root.AcquisitionToken = generation;
        root.ChainRootId = root.Id;
        root.ChainGeneration = generation;
        root.ChainRetryFromId = selectedId;
        root.ChainRetryPreviousStatus = previousRootStatus;
        root.RetryCount = 0;
        root.ExceptionMessage = null;
        root.SkippedReason = null;
        root.ExecutedAt = null;
        root.ElapsedTime = 0;
        root.ProgressPercent = null;
        root.ProgressMessage = null;
        root.ProgressUpdatedAt = null;
        root.UpdatedAt = now;

        return new Plan<TTimeTicker>
        {
            Root = root,
            SelectedId = selectedId,
            Generation = generation,
            PreviousRootStatus = previousRootStatus,
            ResetIds = reset,
            PassThroughIds = passThrough,
            ReusedTerminalIds = reused,
        };
    }

    internal static InternalFunctionContext BuildExecutionContext<TTimeTicker>(
        Plan<TTimeTicker> plan,
        string runtimePartitionKey)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>
    {
        var pathChildren = BuildPathChildren(plan.Root, plan.SelectedId);
        InternalFunctionContext Build(TTimeTicker node)
        {
            var context = new InternalFunctionContext
            {
                RuntimePartitionKey = runtimePartitionKey,
                FunctionName = node.Function,
                RequestContractVersion = node.RequestContractVersion,
                RequestContractFingerprint = node.RequestContractFingerprint,
                TickerId = node.Id,
                Type = TickerType.TimeTicker,
                Status = node.Status,
                Retries = node.Retries,
                RetryCount = node.RetryCount,
                RetryIntervals = node.RetryIntervals,
                TimeoutSeconds = node.TimeoutSeconds,
                ParentId = node.ParentId,
                AcquisitionToken = plan.Generation,
                ChainRootId = plan.Root.Id,
                ChainGeneration = plan.Generation,
                ExecutionTime = node.ExecutionTime ?? DateTime.UtcNow,
                RunCondition = node.RunCondition ?? RunCondition.OnAnyCompletedStatus,
                IsChainRetryPassThrough = plan.PassThroughIds.Contains(node.Id),
                IsChainRetryReusedTerminal = plan.ReusedTerminalIds.Contains(node.Id),
                IsChainRetryRoot = node.Id == plan.Root.Id,
                IsChainRetryCoordinator = node.Id == plan.Root.Id && node.Id != plan.SelectedId,
                ChainRetryPreviousStatus = node.Id == plan.Root.Id
                    ? plan.PreviousRootStatus
                    : null,
            };
            return context;
        }

        InternalFunctionContext Copy(TTimeTicker node)
        {
            var context = Build(node);
            IEnumerable<TTimeTicker> children;
            if (plan.PassThroughIds.Contains(node.Id))
            {
                children = pathChildren.TryGetValue(node.Id, out var next)
                    ? node.GetChildrenForRead().Where(child => child.Id == next)
                    : Array.Empty<TTimeTicker>();
            }
            else if (plan.ReusedTerminalIds.Contains(node.Id))
            {
                children = Array.Empty<TTimeTicker>();
            }
            else if (TickerChainInfrastructure.IsParallelGroup(node.Function))
            {
                children = node.GetChildrenForRead().Where(child =>
                    plan.ResetIds.Contains(child.Id) || plan.PassThroughIds.Contains(child.Id) ||
                    plan.ReusedTerminalIds.Contains(child.Id));
            }
            else
            {
                children = node.GetChildrenForRead().Where(child => plan.ResetIds.Contains(child.Id));
            }

            foreach (var child in children)
            {
                var childContext = Copy(child);
                // Every edge leading to the user-selected step is unconditional for this retry.
                // This includes a grouped branch: clicking that branch (or one of its descendants)
                // must run it even when its original group predicate would not match the prior run.
                if (pathChildren.TryGetValue(node.Id, out var next) && next == child.Id)
                    childContext.RunCondition = RunCondition.InProgress;
                context.TimeTickerChildren.Add(childContext);
            }
            return context;
        }

        return Copy(plan.Root);
    }

    internal static InternalFunctionContext BuildExecutionContext<TTimeTicker>(
        TimeTickerChainRetryAcquisition<TTimeTicker> acquisition,
        string runtimePartitionKey)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>
    {
        var preparedRoot = acquisition.Root;
        var selectedId = acquisition.SelectedId;
        ArgumentNullException.ThrowIfNull(preparedRoot);
        if (!preparedRoot.ChainGeneration.HasValue || preparedRoot.Status != TickerStatus.InProgress)
            throw new TickerValidatorException("The chain retry was not acquired under a durable generation.");

        var byId = new Dictionary<Guid, TTimeTicker>();
        var parentById = new Dictionary<Guid, Guid?>();
        var pending = new Stack<(TTimeTicker Node, Guid? Parent)>();
        pending.Push((preparedRoot, null));
        while (pending.TryPop(out var item))
        {
            if (item.Node == null || !byId.TryAdd(item.Node.Id, item.Node))
                throw new TickerValidatorException("The acquired retry graph is malformed.");
            parentById[item.Node.Id] = item.Parent;
            foreach (var child in item.Node.GetChildrenForRead())
                pending.Push((child, item.Node.Id));
        }
        if (!byId.ContainsKey(selectedId))
            throw new TickerValidatorException("The acquired retry graph does not contain the selected job.");

        var path = new List<Guid>();
        for (Guid? current = selectedId; current.HasValue; current = parentById[current.Value])
            path.Add(current.Value);
        path.Reverse();
        var pathSet = path.ToHashSet();
        var passThrough = path.Take(path.Count - 1).ToHashSet();
        var reset = new HashSet<Guid>();
        AddSubtree(byId[selectedId], reset);
        var recalculatedGroups = GetRecalculatedGroups(path, byId);
        foreach (var id in recalculatedGroups)
        {
            var node = byId[id];
            reset.Add(id);
            passThrough.Remove(id);
            foreach (var child in node.GetChildrenForRead())
            {
                if (pathSet.Contains(child.Id)) continue;
                var condition = child.RunCondition ?? RunCondition.OnAnyCompletedStatus;
                if (!TickerChainInfrastructure.IsParallelBranchCondition(condition)) AddSubtree(child, reset);
            }
        }
        var reused = new HashSet<Guid>();
        foreach (var groupId in recalculatedGroups)
            foreach (var child in byId[groupId].GetChildrenForRead())
            {
                var condition = child.RunCondition ?? RunCondition.OnAnyCompletedStatus;
                if (!pathSet.Contains(child.Id) && !reset.Contains(child.Id) &&
                    TickerChainInfrastructure.IsParallelBranchCondition(condition))
                    reused.Add(child.Id);
            }

        return BuildExecutionContext(new Plan<TTimeTicker>
        {
            Root = preparedRoot,
            SelectedId = selectedId,
            Generation = preparedRoot.ChainGeneration.Value,
            PreviousRootStatus = acquisition.PreviousRootStatus,
            ResetIds = reset,
            PassThroughIds = passThrough,
            ReusedTerminalIds = reused,
        }, runtimePartitionKey);
    }

    private static HashSet<Guid> GetRecalculatedGroups<TTimeTicker>(
        IReadOnlyList<Guid> path,
        IReadOnlyDictionary<Guid, TTimeTicker> byId)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>
    {
        var result = new HashSet<Guid>();
        for (var index = 0; index < path.Count; index++)
        {
            var id = path[index];
            if (!TickerChainInfrastructure.IsParallelGroup(byId[id].Function))
                continue;

            // Selecting the group means rerunning the whole group. Selecting a grouped branch
            // means recomputing its aggregate and outcome jobs. An outcome job is already after
            // the aggregate, so the group remains a settled pass-through ancestor.
            if (index + 1 < path.Count)
            {
                var pathChild = byId[path[index + 1]];
                var condition = pathChild.RunCondition ?? RunCondition.OnAnyCompletedStatus;
                if (!TickerChainInfrastructure.IsParallelBranchCondition(condition))
                    continue;
            }

            result.Add(id);
        }

        return result;
    }

    internal static bool Abort<TTimeTicker>(
        TTimeTicker root,
        Guid generation,
        string reason,
        DateTime abortedAt)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>
    {
        ArgumentNullException.ThrowIfNull(root);
        if (generation == Guid.Empty || root.ParentId.HasValue ||
            root.ChainGeneration != generation ||
            ChainDeletionPolicy.IsTerminal(root.Status))
            return false;

        var nodes = new List<TTimeTicker>();
        var pending = new Stack<TTimeTicker>();
        var seen = new HashSet<Guid>();
        pending.Push(root);
        while (pending.TryPop(out var node))
        {
            if (node == null || !seen.Add(node.Id) || node.ChainRootId != root.Id ||
                node.ChainGeneration != generation)
                return false;
            nodes.Add(node);
            foreach (var child in node.GetChildrenForRead()) pending.Push(child);
        }

        foreach (var node in nodes.Where(node => !ChainDeletionPolicy.IsTerminal(node.Status)))
        {
            var isRoot = node.Id == root.Id;
            node.Status = isRoot ? TickerStatus.Failed : TickerStatus.Skipped;
            node.LockHolder = null;
            node.LockedAt = null;
            node.LeaseUntil = null;
            node.AcquisitionToken = null;
            node.ExecutedAt = abortedAt;
            node.ElapsedTime = 0;
            node.ProgressPercent = null;
            node.ProgressMessage = null;
            node.ProgressUpdatedAt = null;
            node.ExceptionMessage = isRoot ? reason : null;
            node.SkippedReason = isRoot ? null : "Chain retry did not start.";
            node.UpdatedAt = abortedAt;
        }

        root.ChainRetryFromId = null;
        root.ChainRetryPreviousStatus = null;

        return true;
    }

    private static Dictionary<Guid, Guid> BuildPathChildren<TTimeTicker>(TTimeTicker root, Guid selectedId)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>
    {
        var parent = new Dictionary<Guid, Guid>();
        var byId = new Dictionary<Guid, TTimeTicker> { [root.Id] = root };
        var pending = new Stack<TTimeTicker>();
        pending.Push(root);
        while (pending.TryPop(out var node))
            foreach (var child in node.GetChildrenForRead())
            {
                parent[child.Id] = node.Id;
                byId[child.Id] = child;
                pending.Push(child);
            }
        var result = new Dictionary<Guid, Guid>();
        for (var current = selectedId; current != root.Id;)
        {
            if (!parent.TryGetValue(current, out var owner)) break;
            result[owner] = current;
            current = owner;
        }
        return result;
    }

    private static void AddSubtree<TTimeTicker>(TTimeTicker root, HashSet<Guid> ids)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>
    {
        var pending = new Stack<TTimeTicker>();
        pending.Push(root);
        while (pending.TryPop(out var node))
        {
            if (!ids.Add(node.Id)) continue;
            foreach (var child in node.GetChildrenForRead()) pending.Push(child);
        }
    }

    private static void ResetPending<TTimeTicker>(TTimeTicker node, DateTime now)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>
    {
        node.Status = TickerStatus.Idle;
        node.LockHolder = null;
        node.LockedAt = null;
        node.LeaseUntil = null;
        node.AcquisitionToken = null;
        node.RetryCount = 0;
        node.ExceptionMessage = null;
        node.SkippedReason = null;
        node.ExecutedAt = null;
        node.ElapsedTime = 0;
        node.ProgressPercent = null;
        node.ProgressMessage = null;
        node.ProgressUpdatedAt = null;
        node.UpdatedAt = now;
    }
}
