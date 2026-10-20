#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis;
using TickerQ.Utilities.Cluster;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;

namespace TickerQ.Caching.StackExchangeRedis.Infrastructure;

internal sealed partial class TickerRedisPersistenceProvider<TTimeTicker, TCronTicker>
    where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
    where TCronTicker : CronTickerEntity, new()
{
    public bool SupportsClusterControl => true;
    private string ClusterNodesKey => Prefix + ":cluster:nodes";
    private string ClusterCommandKey(Guid id) => Prefix + ":cluster:command:" + id.ToString("N");
    private string ClusterOwnerCommandsKey(string ownerId) => Prefix + ":cluster:commands:" + TickerQClusterIdentity.NodeKey(ownerId);

    public async Task UpsertNodeAsync(TickerQNodeRecord node, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Db.HashSetAsync(ClusterNodesKey, node.NodeKey,
            JsonSerializer.Serialize(node, ClusterControlJsonContext.Default.TickerQNodeRecord)).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<TickerQNodeRecord>> GetNodesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var values = await Db.HashGetAllAsync(ClusterNodesKey).ConfigureAwait(false);
        return values.Select(x => DeserializeNode(x.Value))
            .Where(x => x != null).OrderByDescending(x => x!.LastSeenAtUtc).ToArray()!;
    }

    public async Task<TickerQExecutionOwnership?> GetExecutionOwnershipAsync(
        TickerType type, Guid tickerId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (type == TickerType.TimeTicker)
        {
            var row = await Serializer.GetAsync<TTimeTicker>(Keys.TimeTicker(tickerId)).ConfigureAwait(false);
            return row == null || string.IsNullOrWhiteSpace(row.LockHolder) ? null : new TickerQExecutionOwnership
            {
                Type = type, TickerId = tickerId, ExecutionOwnerId = row.LockHolder,
                AcquisitionToken = row.ParentId.HasValue ? row.ChainGeneration : row.AcquisitionToken,
                Status = row.Status
            };
        }
        var occurrence = await Serializer.GetAsync<CronTickerOccurrenceEntity<TCronTicker>>(Keys.CronOccurrence(tickerId)).ConfigureAwait(false);
        return occurrence == null || string.IsNullOrWhiteSpace(occurrence.LockHolder) ? null : new TickerQExecutionOwnership
        {
            Type = type, TickerId = tickerId, ExecutionOwnerId = occurrence.LockHolder,
            AcquisitionToken = occurrence.AcquisitionToken, Status = occurrence.Status
        };
    }

    public async Task<bool> RequestCancellationAsync(TickerQControlCommand command, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = ClusterCommandKey(command.Id);
        var expiry = command.ExpiresAtUtc - DateTime.UtcNow + TimeSpan.FromDays(1);
        if (expiry < TimeSpan.FromMinutes(1)) expiry = TimeSpan.FromMinutes(1);
        await Db.StringSetAsync(key,
            JsonSerializer.Serialize(command, ClusterControlJsonContext.Default.TickerQControlCommand),
            expiry, When.NotExists).ConfigureAwait(false);
        await Db.SetAddAsync(ClusterOwnerCommandsKey(command.TargetExecutionOwnerId), command.Id.ToString("N")).ConfigureAwait(false);
        return true;
    }

    public async Task<IReadOnlyList<TickerQControlCommand>> GetPendingCommandsAsync(
        string executionOwnerId, int take, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var ownerKey = ClusterOwnerCommandsKey(executionOwnerId);
        var ids = await Db.SetMembersAsync(ownerKey).ConfigureAwait(false);
        var result = new List<TickerQControlCommand>(Math.Min(ids.Length, take));
        foreach (var value in ids)
        {
            if (result.Count >= take) break;
            if (!Guid.TryParse(value.ToString(), out var id))
            {
                await Db.SetRemoveAsync(ownerKey, value).ConfigureAwait(false);
                continue;
            }
            var json = await Db.StringGetAsync(ClusterCommandKey(id)).ConfigureAwait(false);
            var command = DeserializeCommand(json);
            if (command == null || command.Status != TickerQControlCommandStatus.Pending)
            {
                await Db.SetRemoveAsync(ownerKey, value).ConfigureAwait(false);
                continue;
            }
            if (StringComparer.Ordinal.Equals(command.TargetExecutionOwnerId, executionOwnerId)) result.Add(command);
        }
        return result.OrderBy(x => x.RequestedAtUtc).ToArray();
    }

    public async Task<bool> CompleteCommandAsync(Guid commandId, TickerQControlCommandStatus status,
        string outcome, DateTime completedAtUtc, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = ClusterCommandKey(commandId);
        var json = await Db.StringGetAsync(key).ConfigureAwait(false);
        var command = DeserializeCommand(json);
        if (command == null || command.Status != TickerQControlCommandStatus.Pending) return false;
        command.Status = status;
        command.Outcome = outcome;
        command.CompletedAtUtc = completedAtUtc;
        await Db.StringSetAsync(key,
            JsonSerializer.Serialize(command, ClusterControlJsonContext.Default.TickerQControlCommand),
            TimeSpan.FromDays(1)).ConfigureAwait(false);
        await Db.SetRemoveAsync(ClusterOwnerCommandsKey(command.TargetExecutionOwnerId), commandId.ToString("N")).ConfigureAwait(false);
        return true;
    }

    public async Task PruneClusterControlAsync(DateTime nodesBeforeUtc, DateTime commandsBeforeUtc,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var nodes = await Db.HashGetAllAsync(ClusterNodesKey).ConfigureAwait(false);
        var stale = nodes.Where(x => DeserializeNode(x.Value)?.LastSeenAtUtc < nodesBeforeUtc)
            .Select(x => x.Name).ToArray();
        if (stale.Length > 0) await Db.HashDeleteAsync(ClusterNodesKey, stale).ConfigureAwait(false);
    }

    private static TickerQNodeRecord? DeserializeNode(RedisValue value)
    {
        if (value.IsNullOrEmpty) return null;
        try { return JsonSerializer.Deserialize(value.ToString(), ClusterControlJsonContext.Default.TickerQNodeRecord); }
        catch (JsonException) { return null; }
    }

    private static TickerQControlCommand? DeserializeCommand(RedisValue value)
    {
        if (value.IsNullOrEmpty) return null;
        try { return JsonSerializer.Deserialize(value.ToString(), ClusterControlJsonContext.Default.TickerQControlCommand); }
        catch (JsonException) { return null; }
    }
}
