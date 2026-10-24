using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Cluster;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;

namespace TickerQ.Provider;

internal partial class TickerInMemoryPersistenceProvider<TTimeTicker, TCronTicker>
    where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
    where TCronTicker : CronTickerEntity, new()
{
    public bool SupportsClusterControl => true;

    public Task UpsertNodeAsync(TickerQNodeRecord node, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _partitionState.Nodes[node.NodeKey] = Clone(node);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<TickerQNodeRecord>> GetNodesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<TickerQNodeRecord> result = _partitionState.Nodes.Values
            .OrderByDescending(x => x.LastSeenAtUtc).Select(Clone).ToArray();
        return Task.FromResult(result);
    }

    public Task<TickerQExecutionOwnership> GetExecutionOwnershipAsync(
        TickerType type, Guid tickerId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (type == TickerType.TimeTicker && TimeTickers.TryGetValue(tickerId, out var time)
            && !string.IsNullOrWhiteSpace(time.LockHolder))
            return Task.FromResult(new TickerQExecutionOwnership
            {
                Type = type, TickerId = tickerId, ExecutionOwnerId = time.LockHolder,
                AcquisitionToken = time.ParentId.HasValue ? time.ChainGeneration : time.AcquisitionToken,
                Status = time.Status
            });
        if (type == TickerType.CronTickerOccurrence && CronOccurrences.TryGetValue(tickerId, out var cron)
            && !string.IsNullOrWhiteSpace(cron.LockHolder))
            return Task.FromResult(new TickerQExecutionOwnership
            {
                Type = type, TickerId = tickerId, ExecutionOwnerId = cron.LockHolder,
                AcquisitionToken = cron.AcquisitionToken, Status = cron.Status
            });
        return Task.FromResult<TickerQExecutionOwnership>(null);
    }

    public Task<bool> RequestCancellationAsync(TickerQControlCommand command, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _partitionState.ControlCommands.TryAdd(command.Id, Clone(command));
        return Task.FromResult(true);
    }

    public Task<IReadOnlyList<TickerQControlCommand>> GetPendingCommandsAsync(
        string executionOwnerId, int take, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<TickerQControlCommand> result = _partitionState.ControlCommands.Values
            .Where(x => x.Status == TickerQControlCommandStatus.Pending
                        && StringComparer.Ordinal.Equals(x.TargetExecutionOwnerId, executionOwnerId))
            .OrderBy(x => x.RequestedAtUtc).Take(take).Select(Clone).ToArray();
        return Task.FromResult(result);
    }

    public Task<bool> CompleteCommandAsync(Guid commandId, TickerQControlCommandStatus status,
        string outcome, DateTime completedAtUtc, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        while (_partitionState.ControlCommands.TryGetValue(commandId, out var current))
        {
            if (current.Status != TickerQControlCommandStatus.Pending) return Task.FromResult(false);
            var updated = Clone(current);
            updated.Status = status;
            updated.Outcome = outcome;
            updated.CompletedAtUtc = completedAtUtc;
            if (_partitionState.ControlCommands.TryUpdate(commandId, updated, current)) return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }

    public Task PruneClusterControlAsync(DateTime nodesBeforeUtc, DateTime commandsBeforeUtc,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var pair in _partitionState.Nodes)
            if (pair.Value.LastSeenAtUtc < nodesBeforeUtc)
                _partitionState.Nodes.TryRemove(pair.Key, out _);
        foreach (var pair in _partitionState.ControlCommands)
            if (pair.Value.ExpiresAtUtc < commandsBeforeUtc)
                _partitionState.ControlCommands.TryRemove(pair.Key, out _);
        return Task.CompletedTask;
    }

    private static TickerQNodeRecord Clone(TickerQNodeRecord x) => new()
    {
        NodeKey = x.NodeKey, ExecutionOwnerId = x.ExecutionOwnerId, NodeIdentifier = x.NodeIdentifier,
        DisplayName = x.DisplayName, DashboardUrl = x.DashboardUrl, Version = x.Version,
        StartedAtUtc = x.StartedAtUtc, LastSeenAtUtc = x.LastSeenAtUtc, IsRunning = x.IsRunning,
        MaxConcurrency = x.MaxConcurrency, FunctionCount = x.FunctionCount, ActiveExecutions = x.ActiveExecutions,
        QueuedExecutions = x.QueuedExecutions, ActiveWorkflows = x.ActiveWorkflows,
        WaitingForFunction = x.WaitingForFunction, RetryingExecutions = x.RetryingExecutions,
        ActivationEpoch = x.ActivationEpoch
    };

    private static TickerQControlCommand Clone(TickerQControlCommand x) => new()
    {
        Id = x.Id, TargetNodeKey = x.TargetNodeKey, TargetExecutionOwnerId = x.TargetExecutionOwnerId,
        TickerType = x.TickerType, TickerId = x.TickerId, AcquisitionToken = x.AcquisitionToken,
        RequestedAtUtc = x.RequestedAtUtc, ExpiresAtUtc = x.ExpiresAtUtc,
        CompletedAtUtc = x.CompletedAtUtc, Status = x.Status, Outcome = x.Outcome
    };
}
