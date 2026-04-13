#nullable disable
using System;
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
        var idStr = (RedisValue)ticker.Id.ToString();

        var t1 = batch.SetAddAsync(TimeTickerIdsKey, idStr);
        Task t2;
        if (ticker.ExecutionTime.HasValue && CanAcquire(ticker.Status, ticker.LockHolder, _lockHolder))
            t2 = batch.SortedSetAddAsync(TimeTickerPendingKey, idStr, ToScore(ticker.ExecutionTime.Value));
        else
            t2 = batch.SortedSetRemoveAsync(TimeTickerPendingKey, idStr);

        batch.Execute();
        return Task.WhenAll(t1, t2);
    }

    internal Task RemoveTimeTickerIndexesAsync(Guid id)
    {
        var batch = _db.CreateBatch();
        var idStr = (RedisValue)id.ToString();

        var t1 = batch.SetRemoveAsync(TimeTickerIdsKey, idStr);
        var t2 = batch.SortedSetRemoveAsync(TimeTickerPendingKey, idStr);

        batch.Execute();
        return Task.WhenAll(t1, t2);
    }

    internal Task AddCronIndexesAsync(TCronTicker ticker)
    {
        return _db.SetAddAsync(CronIdsKey, ticker.Id.ToString());
    }

    internal Task RemoveCronIndexesAsync(Guid id)
    {
        return _db.SetRemoveAsync(CronIdsKey, id.ToString());
    }

    internal Task AddCronOccurrenceIndexesAsync(CronTickerOccurrenceEntity<TCronTicker> occurrence)
    {
        var batch = _db.CreateBatch();
        var idStr = (RedisValue)occurrence.Id.ToString();

        var t1 = batch.SetAddAsync(CronOccurrenceIdsKey, idStr);
        var t2 = batch.SetAddAsync(CronOccurrencesByCronKey(occurrence.CronTickerId), idStr);
        Task t3;
        if (CanAcquire(occurrence.Status, occurrence.LockHolder, _lockHolder))
            t3 = batch.SortedSetAddAsync(CronOccurrencePendingKey, idStr, ToScore(occurrence.ExecutionTime));
        else
            t3 = batch.SortedSetRemoveAsync(CronOccurrencePendingKey, idStr);

        batch.Execute();
        return Task.WhenAll(t1, t2, t3);
    }

    internal Task RemoveCronOccurrenceIndexesAsync(Guid id, Guid cronTickerId)
    {
        var batch = _db.CreateBatch();
        var idStr = (RedisValue)id.ToString();

        var t1 = batch.SetRemoveAsync(CronOccurrenceIdsKey, idStr);
        var t2 = batch.SortedSetRemoveAsync(CronOccurrencePendingKey, idStr);
        var t3 = batch.SetRemoveAsync(CronOccurrencesByCronKey(cronTickerId), idStr);

        batch.Execute();
        return Task.WhenAll(t1, t2, t3);
    }

    internal async Task RemoveCronOccurrencesByParentAsync(Guid cronId)
    {
        var reverseKey = CronOccurrencesByCronKey(cronId);
        var members = await _db.SetMembersAsync(reverseKey).ConfigureAwait(false);
        var occurrenceIds = ParseGuidSet(members);

        foreach (var occId in occurrenceIds)
        {
            await RemoveCronOccurrenceIndexesAsync(occId, cronId).ConfigureAwait(false);
            await _db.KeyDeleteAsync(CronOccurrenceKey(occId)).ConfigureAwait(false);
        }

        await _db.KeyDeleteAsync(reverseKey).ConfigureAwait(false);
    }
}
