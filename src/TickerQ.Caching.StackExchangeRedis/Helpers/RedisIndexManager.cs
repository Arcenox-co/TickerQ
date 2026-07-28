#nullable disable
using System;
using System.Linq;
using System.Threading.Tasks;
using StackExchange.Redis;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using static TickerQ.Caching.StackExchangeRedis.Helpers.RedisKeyBuilder;

namespace TickerQ.Caching.StackExchangeRedis.Helpers;

internal sealed class RedisIndexManager<TTimeTicker, TCronTicker>
    where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
    where TCronTicker : CronTickerEntity, new()
{
    private readonly IDatabase _db;
    private readonly string _lockHolder;

    internal RedisIndexManager(IDatabase db, string lockHolder)
    {
        _db = db;
        _lockHolder = lockHolder;
    }

    internal Task AddTimeTickerIndexesAsync(TTimeTicker ticker)
    {
        var batch = _db.CreateBatch();
        var id = (RedisValue)ticker.Id.ToString();
        var tasks = new[]
        {
            batch.SetAddAsync(TimeTickerIdsKey, id),
            ticker.ExecutionTime.HasValue && CanAcquire(ticker.Status, ticker.LockHolder, _lockHolder)
                ? batch.SortedSetAddAsync(TimeTickerPendingKey, id, ToScore(ticker.ExecutionTime.Value))
                : batch.SortedSetRemoveAsync(TimeTickerPendingKey, id)
        }.Cast<Task>().Concat(UpdateTimeRetentionIndexes(batch, ticker, id)).ToArray();
        batch.Execute();
        return Task.WhenAll(tasks);
    }

    internal Task RemoveTimeTickerIndexesAsync(Guid id)
    {
        var batch = _db.CreateBatch();
        var value = (RedisValue)id.ToString();
        var tasks = new[]
        {
            batch.SetRemoveAsync(TimeTickerIdsKey, value),
            batch.SortedSetRemoveAsync(TimeTickerPendingKey, value)
        }.Cast<Task>().Concat(RemoveTimeRetentionIndexes(batch, value)).ToArray();
        batch.Execute();
        return Task.WhenAll(tasks);
    }

    internal Task AddCronIndexesAsync(TCronTicker ticker)
        => _db.SetAddAsync(CronIdsKey, ticker.Id.ToString());

    internal Task RemoveCronIndexesAsync(Guid id)
        => _db.SetRemoveAsync(CronIdsKey, id.ToString());

    internal Task AddCronOccurrenceIndexesAsync(CronTickerOccurrenceEntity<TCronTicker> occurrence)
    {
        var batch = _db.CreateBatch();
        var id = (RedisValue)occurrence.Id.ToString();
        var tasks = new[]
        {
            batch.SetAddAsync(CronOccurrenceIdsKey, id),
            batch.SetAddAsync(CronOccurrencesByCronKey(occurrence.CronTickerId), id),
            CanAcquire(occurrence.Status, occurrence.LockHolder, _lockHolder)
                ? batch.SortedSetAddAsync(CronOccurrencePendingKey, id, ToScore(occurrence.ExecutionTime))
                : batch.SortedSetRemoveAsync(CronOccurrencePendingKey, id)
        }.Cast<Task>().Concat(UpdateOccurrenceRetentionIndexes(batch, occurrence, id)).ToArray();
        batch.Execute();
        return Task.WhenAll(tasks);
    }

    internal Task RemoveCronOccurrenceIndexesAsync(Guid id, Guid cronTickerId)
    {
        var batch = _db.CreateBatch();
        var value = (RedisValue)id.ToString();
        var tasks = new[]
        {
            batch.SetRemoveAsync(CronOccurrenceIdsKey, value),
            batch.SortedSetRemoveAsync(CronOccurrencePendingKey, value),
            batch.SetRemoveAsync(CronOccurrencesByCronKey(cronTickerId), value)
        }.Cast<Task>().Concat(RemoveOccurrenceRetentionIndexes(batch, value)).ToArray();
        batch.Execute();
        return Task.WhenAll(tasks);
    }

    internal async Task RemoveCronOccurrencesByParentAsync(Guid cronId)
    {
        var reverseKey = CronOccurrencesByCronKey(cronId);
        var members = await _db.SetMembersAsync(reverseKey).ConfigureAwait(false);
        foreach (var occurrenceId in ParseGuidSet(members))
        {
            await RemoveCronOccurrenceIndexesAsync(occurrenceId, cronId).ConfigureAwait(false);
            await _db.KeyDeleteAsync(CronOccurrenceKey(occurrenceId)).ConfigureAwait(false);
        }
        await _db.KeyDeleteAsync(reverseKey).ConfigureAwait(false);
    }

    private static Task[] UpdateTimeRetentionIndexes(IBatch batch, TTimeTicker ticker, RedisValue id)
    {
        var tasks = RemoveTimeRetentionIndexes(batch, id);
        // A Redis time-ticker chain is one root JSON document. Child state is not yet
        // independently persisted, so chained roots fail closed and are never indexed.
        if (ticker.Children is { Count: > 0 } || ticker.ExecutedAt is not { } executedAt ||
            ticker.AcquisitionToken.HasValue || ticker.LeaseUntil.HasValue)
            return tasks;

        var key = TimeRetentionKey(ticker.Status);
        return key == null
            ? tasks
            : tasks.Append((Task)batch.SortedSetAddAsync(key, id, ToScore(executedAt))).ToArray();
    }

    private static Task[] UpdateOccurrenceRetentionIndexes(
        IBatch batch, CronTickerOccurrenceEntity<TCronTicker> occurrence, RedisValue id)
    {
        var tasks = RemoveOccurrenceRetentionIndexes(batch, id);
        if (occurrence.ExecutedAt is not { } executedAt || occurrence.AcquisitionToken.HasValue ||
            occurrence.LeaseUntil.HasValue)
            return tasks;

        var key = OccurrenceRetentionKey(occurrence.Status);
        return key == null
            ? tasks
            : tasks.Append((Task)batch.SortedSetAddAsync(key, id, ToScore(executedAt))).ToArray();
    }

    private static Task[] RemoveTimeRetentionIndexes(IBatch batch, RedisValue id) =>
    [
        batch.SortedSetRemoveAsync(TimeTickerRetentionSucceededKey, id),
        batch.SortedSetRemoveAsync(TimeTickerRetentionFailedKey, id),
        batch.SortedSetRemoveAsync(TimeTickerRetentionCancelledKey, id),
        batch.SortedSetRemoveAsync(TimeTickerRetentionSkippedKey, id)
    ];

    private static Task[] RemoveOccurrenceRetentionIndexes(IBatch batch, RedisValue id) =>
    [
        batch.SortedSetRemoveAsync(CronOccurrenceRetentionSucceededKey, id),
        batch.SortedSetRemoveAsync(CronOccurrenceRetentionFailedKey, id),
        batch.SortedSetRemoveAsync(CronOccurrenceRetentionCancelledKey, id),
        batch.SortedSetRemoveAsync(CronOccurrenceRetentionSkippedKey, id)
    ];

    private static string TimeRetentionKey(TickerStatus status) => status switch
    {
        TickerStatus.Done or TickerStatus.DueDone => TimeTickerRetentionSucceededKey,
        TickerStatus.Failed => TimeTickerRetentionFailedKey,
        TickerStatus.Cancelled => TimeTickerRetentionCancelledKey,
        TickerStatus.Skipped => TimeTickerRetentionSkippedKey,
        _ => null
    };

    private static string OccurrenceRetentionKey(TickerStatus status) => status switch
    {
        TickerStatus.Done or TickerStatus.DueDone => CronOccurrenceRetentionSucceededKey,
        TickerStatus.Failed => CronOccurrenceRetentionFailedKey,
        TickerStatus.Cancelled => CronOccurrenceRetentionCancelledKey,
        TickerStatus.Skipped => CronOccurrenceRetentionSkippedKey,
        _ => null
    };
}
