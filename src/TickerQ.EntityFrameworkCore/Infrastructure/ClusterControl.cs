using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.EntityFrameworkCore.Entities;
using TickerQ.Utilities.Cluster;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;

namespace TickerQ.EntityFrameworkCore.Infrastructure;

internal abstract partial class BasePersistenceProvider<TDbContext, TTimeTicker, TCronTicker>
    where TDbContext : DbContext
    where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
    where TCronTicker : CronTickerEntity, new()
{
    public bool SupportsClusterControl => true;

    public async Task UpsertNodeAsync(TickerQNodeRecord node, CancellationToken cancellationToken = default)
    {
        using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var rows = session.Context.Set<TickerQNodeEntity>();
        var changed = await rows.Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey && x.NodeKey == node.NodeKey)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.ExecutionOwnerId, node.ExecutionOwnerId)
                .SetProperty(x => x.NodeIdentifier, node.NodeIdentifier)
                .SetProperty(x => x.DisplayName, node.DisplayName)
                .SetProperty(x => x.DashboardUrl, node.DashboardUrl)
                .SetProperty(x => x.Version, node.Version)
                .SetProperty(x => x.StartedAtUtc, node.StartedAtUtc)
                .SetProperty(x => x.LastSeenAtUtc, node.LastSeenAtUtc)
                .SetProperty(x => x.IsRunning, node.IsRunning)
                .SetProperty(x => x.MaxConcurrency, node.MaxConcurrency)
                .SetProperty(x => x.FunctionCount, node.FunctionCount)
                .SetProperty(x => x.ActiveExecutions, node.ActiveExecutions)
                .SetProperty(x => x.QueuedExecutions, node.QueuedExecutions)
                .SetProperty(x => x.ActiveWorkflows, node.ActiveWorkflows)
                .SetProperty(x => x.WaitingForFunction, node.WaitingForFunction)
                .SetProperty(x => x.RetryingExecutions, node.RetryingExecutions)
                .SetProperty(x => x.ActivationEpoch, node.ActivationEpoch), cancellationToken)
            .ConfigureAwait(false);
        if (changed != 0) return;

        rows.Add(ToEntity(node));
        try { await session.Context.SaveChangesAsync(cancellationToken).ConfigureAwait(false); }
        catch (DbUpdateException)
        {
            // A concurrent first heartbeat inserted the node. The next heartbeat refreshes it.
            if (!await rows.AsNoTracking().AnyAsync(x => x.ApplicationNamespaceKey == _runtimePartitionKey
                    && x.NodeKey == node.NodeKey, cancellationToken).ConfigureAwait(false)) throw;
        }
    }

    public async Task<IReadOnlyList<TickerQNodeRecord>> GetNodesAsync(CancellationToken cancellationToken = default)
    {
        using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var rows = await session.Context.Set<TickerQNodeEntity>().AsNoTracking()
            .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey)
            .OrderByDescending(x => x.LastSeenAtUtc)
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(ToRecord).ToArray();
    }

    public async Task<TickerQExecutionOwnership> GetExecutionOwnershipAsync(
        TickerType type, Guid tickerId, CancellationToken cancellationToken = default)
    {
        using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        if (type == TickerType.TimeTicker)
        {
            var row = await session.Context.Set<TTimeTicker>().AsNoTracking()
                .Where(x => x.Id == tickerId)
                .Select(x => new { x.LockHolder, x.AcquisitionToken, x.ChainGeneration, x.ParentId, x.Status })
                .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            return row == null || string.IsNullOrWhiteSpace(row.LockHolder) ? null : new TickerQExecutionOwnership
            {
                Type = type, TickerId = tickerId, ExecutionOwnerId = row.LockHolder,
                AcquisitionToken = row.ParentId.HasValue ? row.ChainGeneration : row.AcquisitionToken,
                Status = row.Status
            };
        }

        var occurrence = await session.Context.Set<CronTickerOccurrenceEntity<TCronTicker>>().AsNoTracking()
            .Where(x => x.Id == tickerId)
            .Select(x => new { x.LockHolder, x.AcquisitionToken, x.Status })
            .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return occurrence == null || string.IsNullOrWhiteSpace(occurrence.LockHolder) ? null : new TickerQExecutionOwnership
        {
            Type = type, TickerId = tickerId, ExecutionOwnerId = occurrence.LockHolder,
            AcquisitionToken = occurrence.AcquisitionToken, Status = occurrence.Status
        };
    }

    public async Task<bool> RequestCancellationAsync(TickerQControlCommand command, CancellationToken cancellationToken = default)
    {
        using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var rows = session.Context.Set<TickerQControlCommandEntity>();
        if (await rows.AsNoTracking().AnyAsync(x => x.ApplicationNamespaceKey == _runtimePartitionKey
                && x.Id == command.Id, cancellationToken).ConfigureAwait(false)) return true;
        rows.Add(ToEntity(command));
        try { await session.Context.SaveChangesAsync(cancellationToken).ConfigureAwait(false); return true; }
        catch (DbUpdateException)
        {
            return await rows.AsNoTracking().AnyAsync(x => x.ApplicationNamespaceKey == _runtimePartitionKey
                && x.Id == command.Id, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyList<TickerQControlCommand>> GetPendingCommandsAsync(
        string executionOwnerId, int take, CancellationToken cancellationToken = default)
    {
        var nodeKey = TickerQClusterIdentity.NodeKey(executionOwnerId);
        using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var rows = await session.Context.Set<TickerQControlCommandEntity>().AsNoTracking()
            .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey && x.TargetNodeKey == nodeKey
                        && x.Status == TickerQControlCommandStatus.Pending)
            .OrderBy(x => x.RequestedAtUtc).Take(take).ToArrayAsync(cancellationToken).ConfigureAwait(false);
        return rows.Where(x => StringComparer.Ordinal.Equals(x.TargetExecutionOwnerId, executionOwnerId))
            .Select(ToRecord).ToArray();
    }

    public async Task<bool> CompleteCommandAsync(Guid commandId, TickerQControlCommandStatus status,
        string outcome, DateTime completedAtUtc, CancellationToken cancellationToken = default)
    {
        using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await session.Context.Set<TickerQControlCommandEntity>()
            .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey && x.Id == commandId
                        && x.Status == TickerQControlCommandStatus.Pending)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, status)
                    .SetProperty(x => x.Outcome, outcome)
                    .SetProperty(x => x.CompletedAtUtc, completedAtUtc), cancellationToken)
            .ConfigureAwait(false) == 1;
    }

    public async Task PruneClusterControlAsync(DateTime nodesBeforeUtc, DateTime commandsBeforeUtc,
        CancellationToken cancellationToken = default)
    {
        using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await session.Context.Set<TickerQNodeEntity>()
            .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey && x.LastSeenAtUtc < nodesBeforeUtc)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await session.Context.Set<TickerQControlCommandEntity>()
            .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey && x.ExpiresAtUtc < commandsBeforeUtc)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
    }

    private TickerQNodeEntity ToEntity(TickerQNodeRecord x) => new()
    {
        ApplicationNamespaceKey = _runtimePartitionKey, NodeKey = x.NodeKey,
        ExecutionOwnerId = x.ExecutionOwnerId, NodeIdentifier = x.NodeIdentifier,
        DisplayName = x.DisplayName, DashboardUrl = x.DashboardUrl, Version = x.Version,
        StartedAtUtc = x.StartedAtUtc, LastSeenAtUtc = x.LastSeenAtUtc, IsRunning = x.IsRunning,
        MaxConcurrency = x.MaxConcurrency, FunctionCount = x.FunctionCount, ActiveExecutions = x.ActiveExecutions,
        QueuedExecutions = x.QueuedExecutions, ActiveWorkflows = x.ActiveWorkflows,
        WaitingForFunction = x.WaitingForFunction, RetryingExecutions = x.RetryingExecutions,
        ActivationEpoch = x.ActivationEpoch
    };

    private static TickerQNodeRecord ToRecord(TickerQNodeEntity x) => new()
    {
        NodeKey = x.NodeKey, ExecutionOwnerId = x.ExecutionOwnerId, NodeIdentifier = x.NodeIdentifier,
        DisplayName = x.DisplayName, DashboardUrl = x.DashboardUrl, Version = x.Version,
        StartedAtUtc = x.StartedAtUtc, LastSeenAtUtc = x.LastSeenAtUtc, IsRunning = x.IsRunning,
        MaxConcurrency = x.MaxConcurrency, FunctionCount = x.FunctionCount, ActiveExecutions = x.ActiveExecutions,
        QueuedExecutions = x.QueuedExecutions, ActiveWorkflows = x.ActiveWorkflows,
        WaitingForFunction = x.WaitingForFunction, RetryingExecutions = x.RetryingExecutions,
        ActivationEpoch = x.ActivationEpoch
    };

    private TickerQControlCommandEntity ToEntity(TickerQControlCommand x) => new()
    {
        ApplicationNamespaceKey = _runtimePartitionKey, Id = x.Id, TargetNodeKey = x.TargetNodeKey,
        TargetExecutionOwnerId = x.TargetExecutionOwnerId, TickerType = x.TickerType,
        TickerId = x.TickerId, AcquisitionToken = x.AcquisitionToken,
        RequestedAtUtc = x.RequestedAtUtc, ExpiresAtUtc = x.ExpiresAtUtc,
        CompletedAtUtc = x.CompletedAtUtc, Status = x.Status, Outcome = x.Outcome
    };

    private static TickerQControlCommand ToRecord(TickerQControlCommandEntity x) => new()
    {
        Id = x.Id, TargetNodeKey = x.TargetNodeKey, TargetExecutionOwnerId = x.TargetExecutionOwnerId,
        TickerType = x.TickerType, TickerId = x.TickerId, AcquisitionToken = x.AcquisitionToken,
        RequestedAtUtc = x.RequestedAtUtc, ExpiresAtUtc = x.ExpiresAtUtc,
        CompletedAtUtc = x.CompletedAtUtc, Status = x.Status, Outcome = x.Outcome
    };
}
