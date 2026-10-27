using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Core.Servers;
using TickerQ.Utilities.Cluster;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Infrastructure;

namespace TickerQ.MongoDB.Infrastructure;

internal sealed partial class TickerMongoPersistenceProvider<TTimeTicker, TCronTicker>
    where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
    where TCronTicker : CronTickerEntity, new()
{
    private IMongoCollection<BsonDocument> ClusterControl => _context.Database.GetCollection<BsonDocument>(
        _context.TimeTickers.CollectionNamespace.CollectionName + "_ClusterControl");

    public bool SupportsClusterControl => true;

    public Task UpsertNodeAsync(TickerQNodeRecord node, CancellationToken cancellationToken = default)
        => ClusterControl.ReplaceOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", NodeDocumentId(node.NodeKey)),
            NodeDocument(node), new ReplaceOptions { IsUpsert = true }, cancellationToken);

    public async Task<IReadOnlyList<TickerQNodeRecord>> GetNodesAsync(CancellationToken cancellationToken = default)
    {
        var rows = await ClusterControl.Find(Builders<BsonDocument>.Filter.Eq("Partition", _runtimePartitionKey)
                                             & Builders<BsonDocument>.Filter.Eq("Kind", "node"))
            .Sort(Builders<BsonDocument>.Sort.Descending("LastSeenAtUtc"))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(NodeRecord).ToArray();
    }

    public async Task<TickerQExecutionOwnership> GetExecutionOwnershipAsync(
        TickerType type, Guid tickerId, CancellationToken cancellationToken = default)
    {
        if (type == TickerType.TimeTicker)
        {
            var row = await _context.TimeTickers.Find(x => x.Id == tickerId)
                .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (row == null)
                return null;
            if (row.ParentId.HasValue)
            {
                // Chain steps carry no lock of their own: the root's chain lease names their owner.
                if (!row.ChainRootId.HasValue || !row.ChainGeneration.HasValue)
                    return null;
                var fb = Builders<TTimeTicker>.Filter;
                var root = await _context.TimeTickers.Find(fb.And(
                        fb.Eq(x => x.ApplicationNamespaceKey, _runtimePartitionKey),
                        fb.Eq(x => x.Id, row.ChainRootId.Value),
                        fb.Eq(x => x.ParentId, (Guid?)null),
                        fb.Eq(x => x.ChainGeneration, row.ChainGeneration)))
                    .Project(x => new { x.LockHolder, x.LeaseUntil, x.Status })
                    .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
                return root == null || string.IsNullOrWhiteSpace(root.LockHolder) ||
                       (root.Status != TickerStatus.InProgress && root.LeaseUntil == null)
                    ? null
                    : new TickerQExecutionOwnership
                    {
                        Type = type, TickerId = tickerId, ExecutionOwnerId = root.LockHolder,
                        AcquisitionToken = row.ChainGeneration, Status = row.Status
                    };
            }
            if (string.IsNullOrWhiteSpace(row.LockHolder))
                return null;
            var chainActive = row.LeaseUntil != null && row.ChainGeneration.HasValue &&
                              ChainDeletionPolicy.IsTerminal(row.Status);
            return new TickerQExecutionOwnership
            {
                Type = type, TickerId = tickerId, ExecutionOwnerId = row.LockHolder,
                AcquisitionToken = chainActive ? row.ChainGeneration : row.AcquisitionToken,
                Status = row.Status, ChainActive = chainActive
            };
        }
        var occurrence = await _context.CronTickerOccurrences.Find(x => x.Id == tickerId)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return occurrence == null || string.IsNullOrWhiteSpace(occurrence.LockHolder) ? null : new TickerQExecutionOwnership
        {
            Type = type, TickerId = tickerId, ExecutionOwnerId = occurrence.LockHolder,
            AcquisitionToken = occurrence.AcquisitionToken, Status = occurrence.Status
        };
    }

    public async Task<bool> RequestCancellationAsync(TickerQControlCommand command, CancellationToken cancellationToken = default)
    {
        try
        {
            await ClusterControl.InsertOneAsync(CommandDocument(command), cancellationToken: cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            return true;
        }
    }

    public async Task<IReadOnlyList<TickerQControlCommand>> GetPendingCommandsAsync(
        string executionOwnerId, int take, CancellationToken cancellationToken = default)
    {
        var rows = await ClusterControl.Find(
                Builders<BsonDocument>.Filter.Eq("Partition", _runtimePartitionKey)
                & Builders<BsonDocument>.Filter.Eq("Kind", "command")
                & Builders<BsonDocument>.Filter.Eq("TargetNodeKey", TickerQClusterIdentity.NodeKey(executionOwnerId))
                & Builders<BsonDocument>.Filter.Eq("Status", (int)TickerQControlCommandStatus.Pending))
            .Sort(Builders<BsonDocument>.Sort.Ascending("RequestedAtUtc")).Limit(take)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(CommandRecord)
            .Where(x => StringComparer.Ordinal.Equals(x.TargetExecutionOwnerId, executionOwnerId)).ToArray();
    }

    public async Task<bool> CompleteCommandAsync(Guid commandId, TickerQControlCommandStatus status,
        string outcome, DateTime completedAtUtc, CancellationToken cancellationToken = default)
    {
        var result = await ClusterControl.UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", CommandDocumentId(commandId))
            & Builders<BsonDocument>.Filter.Eq("Status", (int)TickerQControlCommandStatus.Pending),
            Builders<BsonDocument>.Update.Set("Status", (int)status).Set("Outcome", outcome)
                .Set("CompletedAtUtc", completedAtUtc), cancellationToken: cancellationToken).ConfigureAwait(false);
        return result.ModifiedCount == 1;
    }

    public async Task PruneClusterControlAsync(DateTime nodesBeforeUtc, DateTime commandsBeforeUtc,
        CancellationToken cancellationToken = default)
    {
        await ClusterControl.DeleteManyAsync(
            Builders<BsonDocument>.Filter.Eq("Partition", _runtimePartitionKey)
            & Builders<BsonDocument>.Filter.Eq("Kind", "node")
            & Builders<BsonDocument>.Filter.Lt("LastSeenAtUtc", nodesBeforeUtc), cancellationToken).ConfigureAwait(false);
        await ClusterControl.DeleteManyAsync(
            Builders<BsonDocument>.Filter.Eq("Partition", _runtimePartitionKey)
            & Builders<BsonDocument>.Filter.Eq("Kind", "command")
            & Builders<BsonDocument>.Filter.Lt("ExpiresAtUtc", commandsBeforeUtc), cancellationToken).ConfigureAwait(false);
    }

    private string NodeDocumentId(string key) => $"node:{_runtimePartitionKey}:{key}";
    private string CommandDocumentId(Guid id) => $"command:{_runtimePartitionKey}:{id:N}";

    private BsonDocument NodeDocument(TickerQNodeRecord x) => new()
    {
        ["_id"] = NodeDocumentId(x.NodeKey), ["Partition"] = _runtimePartitionKey, ["Kind"] = "node",
        ["NodeKey"] = x.NodeKey, ["ExecutionOwnerId"] = x.ExecutionOwnerId,
        ["NodeIdentifier"] = x.NodeIdentifier, ["DisplayName"] = x.DisplayName,
        ["DashboardUrl"] = x.DashboardUrl == null ? BsonNull.Value : x.DashboardUrl,
        ["Version"] = x.Version, ["StartedAtUtc"] = x.StartedAtUtc, ["LastSeenAtUtc"] = x.LastSeenAtUtc,
        ["IsRunning"] = x.IsRunning, ["MaxConcurrency"] = x.MaxConcurrency,
        ["FunctionCount"] = x.FunctionCount,
        ["ActiveExecutions"] = x.ActiveExecutions, ["QueuedExecutions"] = x.QueuedExecutions,
        ["ActiveWorkflows"] = x.ActiveWorkflows, ["WaitingForFunction"] = x.WaitingForFunction,
        ["RetryingExecutions"] = x.RetryingExecutions, ["ActivationEpoch"] = x.ActivationEpoch
    };

    private BsonDocument CommandDocument(TickerQControlCommand x) => new()
    {
        ["_id"] = CommandDocumentId(x.Id), ["Partition"] = _runtimePartitionKey, ["Kind"] = "command",
        ["Id"] = x.Id.ToString(), ["TargetNodeKey"] = x.TargetNodeKey,
        ["TargetExecutionOwnerId"] = x.TargetExecutionOwnerId, ["TickerType"] = (int)x.TickerType,
        ["TickerId"] = x.TickerId.ToString(),
        ["AcquisitionToken"] = x.AcquisitionToken.HasValue ? x.AcquisitionToken.Value.ToString() : BsonNull.Value,
        ["RequestedAtUtc"] = x.RequestedAtUtc, ["ExpiresAtUtc"] = x.ExpiresAtUtc,
        ["CompletedAtUtc"] = BsonNull.Value, ["Status"] = (int)x.Status, ["Outcome"] = BsonNull.Value
    };

    private static TickerQNodeRecord NodeRecord(BsonDocument x) => new()
    {
        NodeKey = x["NodeKey"].AsString, ExecutionOwnerId = x["ExecutionOwnerId"].AsString,
        NodeIdentifier = x["NodeIdentifier"].AsString, DisplayName = x["DisplayName"].AsString,
        DashboardUrl = x["DashboardUrl"].IsBsonNull ? null : x["DashboardUrl"].AsString,
        Version = x["Version"].AsString, StartedAtUtc = x["StartedAtUtc"].ToUniversalTime(),
        LastSeenAtUtc = x["LastSeenAtUtc"].ToUniversalTime(), IsRunning = x["IsRunning"].AsBoolean,
        MaxConcurrency = x["MaxConcurrency"].AsInt32,
        FunctionCount = x.TryGetValue("FunctionCount", out var functionCount) ? functionCount.AsInt32 : 0,
        ActiveExecutions = x["ActiveExecutions"].AsInt32,
        QueuedExecutions = x["QueuedExecutions"].AsInt32, ActiveWorkflows = x["ActiveWorkflows"].AsInt32,
        WaitingForFunction = x["WaitingForFunction"].AsInt32, RetryingExecutions = x["RetryingExecutions"].AsInt32,
        ActivationEpoch = x.TryGetValue("ActivationEpoch", out var epoch) ? epoch.ToInt64() : 0
    };

    private static TickerQControlCommand CommandRecord(BsonDocument x) => new()
    {
        Id = Guid.Parse(x["Id"].AsString), TargetNodeKey = x["TargetNodeKey"].AsString,
        TargetExecutionOwnerId = x["TargetExecutionOwnerId"].AsString,
        TickerType = (TickerType)x["TickerType"].AsInt32, TickerId = Guid.Parse(x["TickerId"].AsString),
        AcquisitionToken = x["AcquisitionToken"].IsBsonNull ? null : Guid.Parse(x["AcquisitionToken"].AsString),
        RequestedAtUtc = x["RequestedAtUtc"].ToUniversalTime(), ExpiresAtUtc = x["ExpiresAtUtc"].ToUniversalTime(),
        CompletedAtUtc = x["CompletedAtUtc"].IsBsonNull ? null : x["CompletedAtUtc"].ToUniversalTime(),
        Status = (TickerQControlCommandStatus)x["Status"].AsInt32,
        Outcome = x["Outcome"].IsBsonNull ? null : x["Outcome"].AsString
    };
}
