#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using StackExchange.Redis;
using TickerQ.Caching.StackExchangeRedis.Infrastructure;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using static TickerQ.Caching.StackExchangeRedis.Helpers.RedisKeyBuilder;

namespace TickerQ.Caching.StackExchangeRedis.Helpers;

internal sealed class RedisIndexManager<TTimeTicker, TCronTicker>
    where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
    where TCronTicker : CronTickerEntity, new()
{
    private static readonly string MutateDerivedIndexesScript = LuaScriptLoader.Load("MutateDerivedIndexes");
    private readonly IDatabase _db;
    private readonly string _lockHolder;
    private readonly ITickerClock _clock;
    private readonly RedisKeyBuilder _keys;
    private readonly string _activationMetadataKey;

    private string TimeTickerIdsKey => _keys.TimeTickerIds;
    private string TimeTickerPendingKey => _keys.TimeTickerPending;
    private string CronIdsKey => _keys.CronIds;
    private string CronOccurrenceIdsKey => _keys.CronOccurrenceIds;
    private string CronOccurrencePendingKey => _keys.CronOccurrencePending;
    private string TimeTickerRetentionSucceededKey => _keys.TimeTickerRetentionSucceeded;
    private string TimeTickerRetentionFailedKey => _keys.TimeTickerRetentionFailed;
    private string TimeTickerRetentionCancelledKey => _keys.TimeTickerRetentionCancelled;
    private string TimeTickerRetentionSkippedKey => _keys.TimeTickerRetentionSkipped;
    private string CronOccurrenceRetentionSucceededKey => _keys.CronOccurrenceRetentionSucceeded;
    private string CronOccurrenceRetentionFailedKey => _keys.CronOccurrenceRetentionFailed;
    private string CronOccurrenceRetentionCancelledKey => _keys.CronOccurrenceRetentionCancelled;
    private string CronOccurrenceRetentionSkippedKey => _keys.CronOccurrenceRetentionSkipped;
    private string CronOccurrenceKey(Guid id) => _keys.CronOccurrence(id);
    private string CronOccurrenceResultKey(Guid id) => _keys.CronOccurrenceResult(id);
    private string CronOccurrencesByCronKey(Guid id) => _keys.CronOccurrencesByCron(id);

    internal RedisIndexManager(
        IDatabase db, string lockHolder, ITickerClock clock, RedisKeyBuilder keys,
        string activationMetadataKey)
    {
        _db = db;
        _lockHolder = lockHolder;
        _clock = clock;
        _keys = keys;
        _activationMetadataKey = activationMetadataKey;
    }

    internal Task AddTimeTickerIndexesAsync(TTimeTicker ticker)
    {
        var operations = new List<IndexOperation>
        {
            new("SADD", TimeTickerIdsKey),
            ticker.ExecutionTime.HasValue && CanAcquire(ticker.Status, ticker.LockHolder, _lockHolder)
                ? new IndexOperation("ZADD", TimeTickerPendingKey, ToScore(ticker.ExecutionTime.Value))
                : new IndexOperation("ZREM", TimeTickerPendingKey)
        };
        RemoveTimeRetentionIndexes(operations);
        AddTimeRetentionIndex(operations, ticker, _clock.UtcNow);
        return MutateAsync(ticker.Id, operations);
    }

    internal Task RemoveTimeTickerIndexesAsync(Guid id)
    {
        var operations = new List<IndexOperation>
        {
            new("SREM", TimeTickerIdsKey),
            new("ZREM", TimeTickerPendingKey)
        };
        RemoveTimeRetentionIndexes(operations);
        return MutateAsync(id, operations);
    }

    internal Task AddCronIndexesAsync(TCronTicker ticker)
        => MutateAsync(ticker.Id, [new("SADD", CronIdsKey)]);

    internal Task RemoveCronIndexesAsync(Guid id)
        => MutateAsync(id, [new("SREM", CronIdsKey)]);

    internal Task AddCronOccurrenceIndexesAsync(CronTickerOccurrenceEntity<TCronTicker> occurrence)
    {
        var operations = new List<IndexOperation>
        {
            new("SADD", CronOccurrenceIdsKey),
            new("SADD", CronOccurrencesByCronKey(occurrence.CronTickerId)),
            CanAcquire(occurrence.Status, occurrence.LockHolder, _lockHolder)
                ? new IndexOperation("ZADD", CronOccurrencePendingKey, ToScore(occurrence.ExecutionTime))
                : new IndexOperation("ZREM", CronOccurrencePendingKey)
        };
        RemoveOccurrenceRetentionIndexes(operations);
        AddOccurrenceRetentionIndex(operations, occurrence, _clock.UtcNow);
        return MutateAsync(occurrence.Id, operations);
    }

    internal Task RemoveCronOccurrenceIndexesAsync(Guid id, Guid cronTickerId)
    {
        var operations = new List<IndexOperation>
        {
            new("SREM", CronOccurrenceIdsKey),
            new("ZREM", CronOccurrencePendingKey),
            new("SREM", CronOccurrencesByCronKey(cronTickerId))
        };
        RemoveOccurrenceRetentionIndexes(operations);
        return MutateAsync(id, operations);
    }

    internal Task RemoveCronOccurrenceRetentionIndexesAsync(Guid id)
    {
        var operations = new List<IndexOperation>();
        RemoveOccurrenceRetentionIndexes(operations);
        return MutateAsync(id, operations);
    }

    internal Task RemoveCronOccurrenceFromParentIndexAsync(Guid id, Guid cronTickerId)
        => MutateAsync(id, [new("SREM", CronOccurrencesByCronKey(cronTickerId))]);

    internal async Task RemoveCronOccurrencesByParentAsync(Guid cronId)
    {
        var reverseKey = CronOccurrencesByCronKey(cronId);
        var members = await _db.SetMembersAsync(reverseKey).ConfigureAwait(false);
        foreach (var occurrenceId in ParseGuidSet(members))
        {
            await RemoveCronOccurrenceIndexesAsync(occurrenceId, cronId).ConfigureAwait(false);
            await _db.KeyDeleteAsync([CronOccurrenceKey(occurrenceId), CronOccurrenceResultKey(occurrenceId)])
                .ConfigureAwait(false);
        }
        await MutateAsync(Guid.Empty, [new("DELSET", reverseKey)]).ConfigureAwait(false);
    }

    private Task MutateAsync(Guid id, IReadOnlyList<IndexOperation> operations)
    {
        var keys = new RedisKey[operations.Count + 1];
        keys[0] = _activationMetadataKey;
        var arguments = new RedisValue[2 + operations.Count * 3];
        arguments[0] = id.ToString();
        arguments[1] = operations.Count;
        for (var index = 0; index < operations.Count; index++)
        {
            var operation = operations[index];
            keys[index + 1] = operation.Key;
            var offset = 2 + index * 3;
            arguments[offset] = operation.Command;
            arguments[offset + 1] = index + 2;
            arguments[offset + 2] = operation.Score?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        }
        return _db.ScriptEvaluateAsync(MutateDerivedIndexesScript, keys, arguments);
    }

    private void AddTimeRetentionIndex(
        ICollection<IndexOperation> operations, TTimeTicker ticker, DateTime now)
    {
        // A Redis time-ticker chain is one root JSON document. Child state is not yet
        // independently persisted, so chained roots fail closed and are never indexed.
        if (ticker.ParentId.HasValue || ticker.Children is { Count: > 0 } ||
            ticker.ExecutedAt is not { } executedAt || ticker.AcquisitionToken.HasValue ||
            ticker.LeaseUntil is { } leaseUntil && leaseUntil > now)
            return;

        var key = TimeRetentionKey(ticker.Status);
        if (key != null) operations.Add(new IndexOperation("ZADD", key, ToScore(executedAt)));
    }

    private void AddOccurrenceRetentionIndex(
        ICollection<IndexOperation> operations,
        CronTickerOccurrenceEntity<TCronTicker> occurrence, DateTime now)
    {
        if (occurrence.ExecutedAt is not { } executedAt || occurrence.AcquisitionToken.HasValue ||
            occurrence.LeaseUntil is { } leaseUntil && leaseUntil > now)
            return;

        var key = OccurrenceRetentionKey(occurrence.Status);
        if (key != null) operations.Add(new IndexOperation("ZADD", key, ToScore(executedAt)));
    }

    private void RemoveTimeRetentionIndexes(ICollection<IndexOperation> operations)
    {
        operations.Add(new IndexOperation("ZREM", TimeTickerRetentionSucceededKey));
        operations.Add(new IndexOperation("ZREM", TimeTickerRetentionFailedKey));
        operations.Add(new IndexOperation("ZREM", TimeTickerRetentionCancelledKey));
        operations.Add(new IndexOperation("ZREM", TimeTickerRetentionSkippedKey));
    }

    private void RemoveOccurrenceRetentionIndexes(ICollection<IndexOperation> operations)
    {
        operations.Add(new IndexOperation("ZREM", CronOccurrenceRetentionSucceededKey));
        operations.Add(new IndexOperation("ZREM", CronOccurrenceRetentionFailedKey));
        operations.Add(new IndexOperation("ZREM", CronOccurrenceRetentionCancelledKey));
        operations.Add(new IndexOperation("ZREM", CronOccurrenceRetentionSkippedKey));
    }

    private string TimeRetentionKey(TickerStatus status) => status switch
    {
        TickerStatus.Done or TickerStatus.DueDone => TimeTickerRetentionSucceededKey,
        TickerStatus.Failed => TimeTickerRetentionFailedKey,
        TickerStatus.Cancelled => TimeTickerRetentionCancelledKey,
        TickerStatus.Skipped => TimeTickerRetentionSkippedKey,
        _ => null
    };

    private string OccurrenceRetentionKey(TickerStatus status) => status switch
    {
        TickerStatus.Done or TickerStatus.DueDone => CronOccurrenceRetentionSucceededKey,
        TickerStatus.Failed => CronOccurrenceRetentionFailedKey,
        TickerStatus.Cancelled => CronOccurrenceRetentionCancelledKey,
        TickerStatus.Skipped => CronOccurrenceRetentionSkippedKey,
        _ => null
    };

    private sealed record IndexOperation(string Command, string Key, double? Score = null);
}
