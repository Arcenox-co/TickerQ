#nullable disable
using System.Linq.Expressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using static TickerQ.Caching.StackExchangeRedis.DependencyInjection.ServiceExtension;
using static TickerQ.Caching.StackExchangeRedis.Helpers.RedisKeyBuilder;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Infrastructure;
using TickerQ.Utilities.Models;

namespace TickerQ.Caching.StackExchangeRedis.Infrastructure;

internal sealed class TickerRedisPersistenceProvider<TTimeTicker, TCronTicker> :
    BaseRedisPersistenceProvider<TTimeTicker, TCronTicker>,
    ITickerPersistenceProvider<TTimeTicker, TCronTicker>
    where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
    where TCronTicker : CronTickerEntity, new()
{
    public TickerRedisPersistenceProvider(
        [FromKeyedServices("tickerq")] IDatabase db,
        ITickerClock clock,
        SchedulerOptionsBuilder optionsBuilder,
        TickerQRedisOptionBuilder redisOptions,
        ILogger<TickerRedisPersistenceProvider<TTimeTicker, TCronTicker>> logger)
        : base(db, clock, optionsBuilder, redisOptions, logger) { }

    #region Queryable_Methods
    public ITickerQueryable<TTimeTicker> TimeTickersQuery()
    {
        return new InMemoryTickerQueryable<TTimeTicker>(async ct =>
        {
            var list = await Serializer.LoadAllFromSetAsync<TTimeTicker>(
                TimeTickerIdsKey, TimeTickerKey, ct).ConfigureAwait(false);
            return list;
        });
    }

    public ITickerQueryable<TCronTicker> CronTickersQuery()
    {
        return new InMemoryTickerQueryable<TCronTicker>(async ct =>
        {
            var list = await Serializer.LoadAllFromSetAsync<TCronTicker>(
                CronIdsKey, CronKey, ct).ConfigureAwait(false);
            return list;
        });
    }

    public ITickerQueryable<CronTickerOccurrenceEntity<TCronTicker>> CronTickerOccurrencesQuery()
    {
        return new InMemoryTickerQueryable<CronTickerOccurrenceEntity<TCronTicker>>(async ct =>
        {
            var list = await Serializer.LoadAllFromSetAsync<CronTickerOccurrenceEntity<TCronTicker>>(
                CronOccurrenceIdsKey, CronOccurrenceKey, ct).ConfigureAwait(false);
            return list;
        });
    }
    #endregion

    #region Time_Ticker_Shared_Methods
    public async Task<TTimeTicker> GetTimeTickerById(Guid id, CancellationToken cancellationToken = default)
    {
        return await Serializer.GetAsync<TTimeTicker>(TimeTickerKey(id)).ConfigureAwait(false);
    }

    public async Task<TTimeTicker[]> GetTimeTickers(Expression<Func<TTimeTicker, bool>> predicate, CancellationToken cancellationToken)
    {
        var compiled = predicate?.Compile();

        var list = await Serializer.LoadAllFromSetAsync<TTimeTicker>(
            TimeTickerIdsKey, TimeTickerKey, cancellationToken,
            t => t.ParentId == null && (compiled == null || compiled(t))).ConfigureAwait(false);

        return list.OrderByDescending(x => x.ExecutionTime).ToArray();
    }

    public async Task<PaginationResult<TTimeTicker>> GetTimeTickersPaginated(Expression<Func<TTimeTicker, bool>> predicate, int pageNumber, int pageSize, CancellationToken cancellationToken = default)
    {
        var all = await GetTimeTickers(predicate, cancellationToken).ConfigureAwait(false);
        var total = all.Length;
        var items = all.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToArray();
        return new PaginationResult<TTimeTicker>(items, total, pageNumber, pageSize);
    }

    public async Task<int> AddTimeTickers(TTimeTicker[] tickers, CancellationToken cancellationToken = default)
    {
        var now = Clock.UtcNow;
        foreach (var ticker in tickers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ticker.CreatedAt = ticker.CreatedAt == default ? now : ticker.CreatedAt;
            ticker.UpdatedAt = ticker.UpdatedAt == default ? now : ticker.UpdatedAt;
            await Serializer.SetAsync(TimeTickerKey(ticker.Id), ticker).ConfigureAwait(false);
            await IndexManager.AddTimeTickerIndexesAsync(ticker).ConfigureAwait(false);
        }
        return tickers.Length;
    }

    public async Task<int> UpdateTimeTickers(TTimeTicker[] tickers, CancellationToken cancellationToken = default)
    {
        var now = Clock.UtcNow;
        foreach (var ticker in tickers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ticker.UpdatedAt = now;
            await Serializer.SetAsync(TimeTickerKey(ticker.Id), ticker).ConfigureAwait(false);
            await IndexManager.AddTimeTickerIndexesAsync(ticker).ConfigureAwait(false);
        }
        return tickers.Length;
    }

    public async Task<int> RemoveTimeTickers(Guid[] tickerIds, CancellationToken cancellationToken = default)
    {
        var count = 0;
        foreach (var id in tickerIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await IndexManager.RemoveTimeTickerIndexesAsync(id).ConfigureAwait(false);
            if (await Db.KeyDeleteAsync(TimeTickerKey(id)).ConfigureAwait(false))
                count++;
        }
        return count;
    }
    #endregion

    #region Cron_Ticker_Shared_Methods
    public async Task<TCronTicker> GetCronTickerById(Guid id, CancellationToken cancellationToken)
    {
        return await Serializer.GetAsync<TCronTicker>(CronKey(id)).ConfigureAwait(false);
    }

    public async Task<TCronTicker[]> GetCronTickers(Expression<Func<TCronTicker, bool>> predicate, CancellationToken cancellationToken)
    {
        var list = await Serializer.LoadAllFromSetAsync<TCronTicker>(
            CronIdsKey, CronKey, cancellationToken, predicate?.Compile()).ConfigureAwait(false);

        return list.OrderByDescending(x => x.CreatedAt).ToArray();
    }

    public async Task<PaginationResult<TCronTicker>> GetCronTickersPaginated(Expression<Func<TCronTicker, bool>> predicate, int pageNumber, int pageSize, CancellationToken cancellationToken = default)
    {
        var all = await GetCronTickers(predicate, cancellationToken).ConfigureAwait(false);
        var total = all.Length;
        var items = all.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToArray();
        return new PaginationResult<TCronTicker>(items, total, pageNumber, pageSize);
    }

    public async Task<int> InsertCronTickers(TCronTicker[] tickers, CancellationToken cancellationToken)
    {
        var now = Clock.UtcNow;
        foreach (var ticker in tickers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ticker.CreatedAt = ticker.CreatedAt == default ? now : ticker.CreatedAt;
            ticker.UpdatedAt = ticker.UpdatedAt == default ? now : ticker.UpdatedAt;
            await Serializer.SetAsync(CronKey(ticker.Id), ticker).ConfigureAwait(false);
            await IndexManager.AddCronIndexesAsync(ticker).ConfigureAwait(false);
        }
        return tickers.Length;
    }

    public async Task<int> UpdateCronTickers(TCronTicker[] cronTicker, CancellationToken cancellationToken)
    {
        var now = Clock.UtcNow;
        foreach (var ticker in cronTicker)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ticker.UpdatedAt = now;
            await Serializer.SetAsync(CronKey(ticker.Id), ticker).ConfigureAwait(false);
            await IndexManager.AddCronIndexesAsync(ticker).ConfigureAwait(false);
        }
        return cronTicker.Length;
    }

    public async Task<int> RemoveCronTickers(Guid[] cronTickerIds, CancellationToken cancellationToken)
    {
        var removed = 0;
        foreach (var id in cronTickerIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await IndexManager.RemoveCronIndexesAsync(id).ConfigureAwait(false);
            await IndexManager.RemoveCronOccurrencesByParentAsync(id).ConfigureAwait(false);
            if (await Db.KeyDeleteAsync(CronKey(id)).ConfigureAwait(false))
                removed++;
        }
        return removed;
    }
    #endregion

    #region Cron_TickerOccurrence_Shared_Methods
    public async Task<CronTickerOccurrenceEntity<TCronTicker>[]> GetAllCronTickerOccurrences(Expression<Func<CronTickerOccurrenceEntity<TCronTicker>, bool>> predicate, CancellationToken cancellationToken = default)
    {
        var list = await Serializer.LoadAllFromSetAsync<CronTickerOccurrenceEntity<TCronTicker>>(
            CronOccurrenceIdsKey, CronOccurrenceKey, cancellationToken, predicate?.Compile()).ConfigureAwait(false);

        return list.OrderByDescending(x => x.ExecutionTime).ToArray();
    }

    public async Task<PaginationResult<CronTickerOccurrenceEntity<TCronTicker>>> GetAllCronTickerOccurrencesPaginated(Expression<Func<CronTickerOccurrenceEntity<TCronTicker>, bool>> predicate, int pageNumber, int pageSize, CancellationToken cancellationToken = default)
    {
        var all = await GetAllCronTickerOccurrences(predicate, cancellationToken).ConfigureAwait(false);
        var total = all.Length;
        var items = all.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToArray();
        return new PaginationResult<CronTickerOccurrenceEntity<TCronTicker>>(items, total, pageNumber, pageSize);
    }

    public async Task<int> InsertCronTickerOccurrences(CronTickerOccurrenceEntity<TCronTicker>[] cronTickerOccurrences, CancellationToken cancellationToken)
    {
        foreach (var occurrence in cronTickerOccurrences)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Serializer.SetAsync(CronOccurrenceKey(occurrence.Id), occurrence).ConfigureAwait(false);
            await IndexManager.AddCronOccurrenceIndexesAsync(occurrence).ConfigureAwait(false);
        }
        return cronTickerOccurrences.Length;
    }

    public async Task<int> RemoveCronTickerOccurrences(Guid[] cronTickerOccurrences, CancellationToken cancellationToken)
    {
        var removed = 0;
        foreach (var id in cronTickerOccurrences)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var occurrence = await Serializer.GetAsync<CronTickerOccurrenceEntity<TCronTicker>>(CronOccurrenceKey(id)).ConfigureAwait(false);
            if (occurrence != null)
                await IndexManager.RemoveCronOccurrenceIndexesAsync(id, occurrence.CronTickerId).ConfigureAwait(false);
            if (await Db.KeyDeleteAsync(CronOccurrenceKey(id)).ConfigureAwait(false))
                removed++;
        }
        return removed;
    }

    public async Task<CronTickerOccurrenceEntity<TCronTicker>[]> AcquireImmediateCronOccurrencesAsync(Guid[] occurrenceIds, CancellationToken cancellationToken = default)
    {
        if (occurrenceIds == null || occurrenceIds.Length == 0)
            return [];

        var acquired = new List<CronTickerOccurrenceEntity<TCronTicker>>();
        foreach (var id in occurrenceIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var occurrence = await TryAcquireAsync<CronTickerOccurrenceEntity<TCronTicker>>(
                CronOccurrenceKey(id),
                TickerStatus.InProgress).ConfigureAwait(false);

            if (occurrence == null) continue;

            if (occurrence.CronTicker == null && occurrence.CronTickerId != Guid.Empty)
                occurrence.CronTicker = await Serializer.GetAsync<TCronTicker>(CronKey(occurrence.CronTickerId)).ConfigureAwait(false);

            await IndexManager.AddCronOccurrenceIndexesAsync(occurrence).ConfigureAwait(false);
            acquired.Add(occurrence);
        }

        return acquired.ToArray();
    }
    #endregion

    #region Retention
    public bool SupportsRetention => true;

    public async Task<RetentionChainBatchResult> DeleteEligibleTimeTickerChainsAsync(
        RetentionCutoffs cutoffs, int batchSize, RetentionCursor cursor,
        CancellationToken cancellationToken = default)
    {
        var candidates = await GetRetentionCandidatesAsync(
            true, cutoffs, batchSize + 1, cancellationToken).ConfigureAwait(false);
        var hasMore = candidates.Count > batchSize;
        var deleted = 0;

        foreach (var candidate in candidates.Take(batchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await Db.ScriptEvaluateAsync(DeleteTimeTickerForRetentionScript,
                [
                    (RedisKey)TimeTickerKey(candidate.Id),
                    TimeTickerIdsKey,
                    TimeTickerPendingKey,
                    TimeTickerRetentionSucceededKey,
                    TimeTickerRetentionFailedKey,
                    TimeTickerRetentionCancelledKey,
                    TimeTickerRetentionSkippedKey
                ],
                [
                    candidate.FirstStatus,
                    candidate.SecondStatus,
                    candidate.Cutoff.ToUniversalTime().ToString("O"),
                    Clock.UtcNow.ToUniversalTime().ToString("O"),
                    candidate.Id.ToString()
                ]).ConfigureAwait(false);

            if ((long)result == 1)
            {
                deleted++;
                continue;
            }

            // A reactivation or stale index won the race. Repair the indexes from the authoritative value.
            var current = await Serializer.GetAsync<TTimeTicker>(TimeTickerKey(candidate.Id)).ConfigureAwait(false);
            if (current == null)
                await IndexManager.RemoveTimeTickerIndexesAsync(candidate.Id).ConfigureAwait(false);
            else
                await IndexManager.AddTimeTickerIndexesAsync(current).ConfigureAwait(false);
        }

        // Redis retention indexes contain only independently safe standalone roots. Stale entries are
        // repaired above, so no blocked aggregate can starve later candidates and no cursor is required.
        return new RetentionChainBatchResult(deleted, hasMore, RetentionCursor.Start);
    }

    public async Task<RetentionBatchResult> DeleteEligibleCronTickerOccurrencesAsync(
        RetentionCutoffs cutoffs, int batchSize, CancellationToken cancellationToken = default)
    {
        var candidates = await GetRetentionCandidatesAsync(
            false, cutoffs, batchSize + 1, cancellationToken).ConfigureAwait(false);
        var hasMore = candidates.Count > batchSize;
        var deleted = 0;

        foreach (var candidate in candidates.Take(batchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var occurrence = await Serializer.GetAsync<CronTickerOccurrenceEntity<TCronTicker>>(
                CronOccurrenceKey(candidate.Id)).ConfigureAwait(false);
            if (occurrence == null)
            {
                await RemoveOccurrenceRetentionIndexesAsync(candidate.Id).ConfigureAwait(false);
                continue;
            }

            var result = await Db.ScriptEvaluateAsync(DeleteOccurrenceForRetentionScript,
                [
                    (RedisKey)CronOccurrenceKey(candidate.Id),
                    CronOccurrenceIdsKey,
                    CronOccurrencePendingKey,
                    CronOccurrenceRetentionSucceededKey,
                    CronOccurrenceRetentionFailedKey,
                    CronOccurrenceRetentionCancelledKey,
                    CronOccurrenceRetentionSkippedKey,
                    CronOccurrencesByCronKey(occurrence.CronTickerId)
                ],
                [
                    candidate.FirstStatus,
                    candidate.SecondStatus,
                    candidate.Cutoff.ToUniversalTime().ToString("O"),
                    Clock.UtcNow.ToUniversalTime().ToString("O"),
                    candidate.Id.ToString()
                ]).ConfigureAwait(false);

            if ((long)result == 1)
            {
                deleted++;
                continue;
            }

            var current = await Serializer.GetAsync<CronTickerOccurrenceEntity<TCronTicker>>(
                CronOccurrenceKey(candidate.Id)).ConfigureAwait(false);
            if (current == null)
                await RemoveOccurrenceRetentionIndexesAsync(candidate.Id).ConfigureAwait(false);
            else
                await IndexManager.AddCronOccurrenceIndexesAsync(current).ConfigureAwait(false);
        }

        return new RetentionBatchResult(deleted, hasMore);
    }

    private async Task<List<RetentionCandidate>> GetRetentionCandidatesAsync(
        bool timeTicker, RetentionCutoffs cutoffs, int take, CancellationToken cancellationToken)
    {
        var sources = new List<(RedisKey Key, DateTime Cutoff, int FirstStatus, int SecondStatus)>();
        AddRetentionSource(sources, timeTicker, cutoffs.SucceededBefore,
            TickerStatus.Done, TickerStatus.DueDone);
        AddRetentionSource(sources, timeTicker, cutoffs.FailedBefore,
            TickerStatus.Failed, TickerStatus.Failed);
        AddRetentionSource(sources, timeTicker, cutoffs.CancelledBefore,
            TickerStatus.Cancelled, TickerStatus.Cancelled);
        AddRetentionSource(sources, timeTicker, cutoffs.SkippedBefore,
            TickerStatus.Skipped, TickerStatus.Skipped);

        var candidates = new List<RetentionCandidate>(sources.Count * take);
        foreach (var source in sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entries = await Db.SortedSetRangeByScoreWithScoresAsync(
                source.Key, double.NegativeInfinity, ToScore(source.Cutoff),
                Exclude.Stop, Order.Ascending, 0, take).ConfigureAwait(false);
            foreach (var entry in entries)
            {
                if (!Guid.TryParse(entry.Element.ToString(), out var id))
                    continue;
                candidates.Add(new RetentionCandidate(
                    id,
                    new DateTime((long)entry.Score, DateTimeKind.Utc),
                    source.Cutoff,
                    source.FirstStatus,
                    source.SecondStatus));
            }
        }

        return candidates
            .OrderBy(x => x.ExecutedAt)
            .ThenBy(x => x.Id.ToString(), StringComparer.Ordinal)
            .GroupBy(x => x.Id)
            .Select(x => x.First())
            .Take(take)
            .ToList();
    }

    private static void AddRetentionSource(
        List<(RedisKey Key, DateTime Cutoff, int FirstStatus, int SecondStatus)> sources,
        bool timeTicker, DateTime? cutoff, TickerStatus firstStatus, TickerStatus secondStatus)
    {
        if (cutoff is not { } value)
            return;
        var key = timeTicker
            ? TimeRetentionKey(firstStatus)
            : OccurrenceRetentionKey(firstStatus);
        sources.Add((key, value, (int)firstStatus, (int)secondStatus));
    }

    private async Task RemoveOccurrenceRetentionIndexesAsync(Guid id)
    {
        var value = (RedisValue)id.ToString();
        var batch = Db.CreateBatch();
        var tasks = new[]
        {
            batch.SortedSetRemoveAsync(CronOccurrenceRetentionSucceededKey, value),
            batch.SortedSetRemoveAsync(CronOccurrenceRetentionFailedKey, value),
            batch.SortedSetRemoveAsync(CronOccurrenceRetentionCancelledKey, value),
            batch.SortedSetRemoveAsync(CronOccurrenceRetentionSkippedKey, value)
        };
        batch.Execute();
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private static string TimeRetentionKey(TickerStatus status) => status switch
    {
        TickerStatus.Done or TickerStatus.DueDone => TimeTickerRetentionSucceededKey,
        TickerStatus.Failed => TimeTickerRetentionFailedKey,
        TickerStatus.Cancelled => TimeTickerRetentionCancelledKey,
        TickerStatus.Skipped => TimeTickerRetentionSkippedKey,
        _ => throw new ArgumentOutOfRangeException(nameof(status))
    };

    private static string OccurrenceRetentionKey(TickerStatus status) => status switch
    {
        TickerStatus.Done or TickerStatus.DueDone => CronOccurrenceRetentionSucceededKey,
        TickerStatus.Failed => CronOccurrenceRetentionFailedKey,
        TickerStatus.Cancelled => CronOccurrenceRetentionCancelledKey,
        TickerStatus.Skipped => CronOccurrenceRetentionSkippedKey,
        _ => throw new ArgumentOutOfRangeException(nameof(status))
    };

    private sealed record RetentionCandidate(
        Guid Id, DateTime ExecutedAt, DateTime Cutoff, int FirstStatus, int SecondStatus);

    private const string DeleteTimeTickerForRetentionScript = """
        local raw = redis.call('GET', KEYS[1])
        if not raw then return 0 end
        local obj = cjson.decode(raw)
        local status = tonumber(obj.Status)
        if status ~= tonumber(ARGV[1]) and status ~= tonumber(ARGV[2]) then return 0 end
        if obj.ExecutedAt == nil or obj.ExecutedAt == cjson.null or obj.ExecutedAt >= ARGV[3] then return 0 end
        if obj.AcquisitionToken ~= nil and obj.AcquisitionToken ~= cjson.null and obj.AcquisitionToken ~= '' then return 0 end
        if obj.LeaseUntil ~= nil and obj.LeaseUntil ~= cjson.null and obj.LeaseUntil ~= '' and obj.LeaseUntil > ARGV[4] then return 0 end
        if obj.Children ~= nil and obj.Children ~= cjson.null and next(obj.Children) ~= nil then return 0 end
        redis.call('DEL', KEYS[1])
        redis.call('SREM', KEYS[2], ARGV[5])
        redis.call('ZREM', KEYS[3], ARGV[5])
        redis.call('ZREM', KEYS[4], ARGV[5])
        redis.call('ZREM', KEYS[5], ARGV[5])
        redis.call('ZREM', KEYS[6], ARGV[5])
        redis.call('ZREM', KEYS[7], ARGV[5])
        return 1
        """;

    private const string DeleteOccurrenceForRetentionScript = """
        local raw = redis.call('GET', KEYS[1])
        if not raw then return 0 end
        local obj = cjson.decode(raw)
        local status = tonumber(obj.Status)
        if status ~= tonumber(ARGV[1]) and status ~= tonumber(ARGV[2]) then return 0 end
        if obj.ExecutedAt == nil or obj.ExecutedAt == cjson.null or obj.ExecutedAt >= ARGV[3] then return 0 end
        if obj.AcquisitionToken ~= nil and obj.AcquisitionToken ~= cjson.null and obj.AcquisitionToken ~= '' then return 0 end
        if obj.LeaseUntil ~= nil and obj.LeaseUntil ~= cjson.null and obj.LeaseUntil ~= '' and obj.LeaseUntil > ARGV[4] then return 0 end
        redis.call('DEL', KEYS[1])
        redis.call('SREM', KEYS[2], ARGV[5])
        redis.call('ZREM', KEYS[3], ARGV[5])
        redis.call('ZREM', KEYS[4], ARGV[5])
        redis.call('ZREM', KEYS[5], ARGV[5])
        redis.call('ZREM', KEYS[6], ARGV[5])
        redis.call('ZREM', KEYS[7], ARGV[5])
        redis.call('SREM', KEYS[8], ARGV[5])
        return 1
        """;
    #endregion
}
