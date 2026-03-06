#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis;
using TickerQ.Caching.StackExchangeRedis.Converter;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ.Caching.StackExchangeRedis.Infrastructure;

/// <summary>
/// Redis-native persistence provider that stores all ticker data directly in Redis
/// (hashes/sets/zsets via string serialization) rather than keeping in-memory snapshots.
/// This mirrors the EF Core provider’s behavior: optimistic concurrency on UpdatedAt,
/// lock-holder semantics, and scheduling via execution time ordering.
/// </summary>
internal sealed class TickerRedisPersistenceProvider<TTimeTicker, TCronTicker> : ITickerPersistenceProvider<TTimeTicker, TCronTicker>
    where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
    where TCronTicker : CronTickerEntity, new()
{
    private readonly IDatabase _db;
    private readonly ITickerClock _clock;
    private readonly string _lockHolder;
    private readonly JsonSerializerOptions _jsonOptions;

    private const string Prefix = "tq";
    private const string TimeTickerIdsKey = $"{Prefix}:tt:ids";
    private const string TimeTickerPendingKey = $"{Prefix}:tt:pending";
    private const string CronIdsKey = $"{Prefix}:cron:ids";
    private const string CronOccurrenceIdsKey = $"{Prefix}:co:ids";
    private const string CronOccurrencePendingKey = $"{Prefix}:co:pending";

    public TickerRedisPersistenceProvider(IDatabase db, ITickerClock clock, SchedulerOptionsBuilder optionsBuilder)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _lockHolder = optionsBuilder?.NodeIdentifier ?? Environment.MachineName;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = false,
            Converters = { new TimeTickerEntityConverter() }
        };
    }

    #region Key helpers
    private static string TimeTickerKey(Guid id) => $"{Prefix}:tt:{id}";
    private static string CronKey(Guid id) => $"{Prefix}:cron:{id}";
    private static string CronOccurrenceKey(Guid id) => $"{Prefix}:co:{id}";
    private static double ToScore(DateTime utc) => utc.ToUniversalTime().Ticks;
    #endregion

    #region Serialization helpers
    private async Task<T> GetAsync<T>(string key) where T : class
    {
        var val = await _db.StringGetAsync(key).ConfigureAwait(false);
        if (val.IsNullOrEmpty) return null;
        try { return JsonSerializer.Deserialize<T>((string)val, _jsonOptions); }
        catch { return null; }
    }

    private Task SetAsync<T>(string key, T value) where T : class
    {
        var payload = JsonSerializer.Serialize(value, _jsonOptions);
        return _db.StringSetAsync(key, payload);
    }
    #endregion

    #region Common helpers
    private static bool CanAcquire(TickerStatus status, string currentHolder, string lockHolder)
    {
        return status is TickerStatus.Idle or TickerStatus.Queued &&
               (string.IsNullOrEmpty(currentHolder) || string.Equals(currentHolder, lockHolder, StringComparison.Ordinal));
    }

    private static TimeTickerEntity MapForQueue(TTimeTicker ticker)
    {
        return new TimeTickerEntity
        {
            Id = ticker.Id,
            Function = ticker.Function,
            Retries = ticker.Retries,
            RetryIntervals = ticker.RetryIntervals,
            UpdatedAt = ticker.UpdatedAt,
            ParentId = ticker.ParentId,
            ExecutionTime = ticker.ExecutionTime,
            Status = ticker.Status,
            LockHolder = ticker.LockHolder,
            LockedAt = ticker.LockedAt,
            Children = ticker.Children?.OfType<TTimeTicker>().Select(ch => new TimeTickerEntity
            {
                Id = ch.Id,
                Function = ch.Function,
                Retries = ch.Retries,
                RetryIntervals = ch.RetryIntervals,
                RunCondition = ch.RunCondition,
                ParentId = ch.ParentId,
                Children = ch.Children?.OfType<TTimeTicker>().Select(gch => new TimeTickerEntity
                {
                    Id = gch.Id,
                    Function = gch.Function,
                    Retries = gch.Retries,
                    RetryIntervals = gch.RetryIntervals,
                    RunCondition = gch.RunCondition,
                    ParentId = gch.ParentId
                }).ToArray()
            }).ToArray()
        };
    }

    // Batched index updates — single round-trip per call via IBatch
    private Task AddTimeTickerIndexesAsync(TTimeTicker ticker)
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

    private Task RemoveTimeTickerIndexesAsync(Guid id)
    {
        var batch = _db.CreateBatch();
        var idStr = (RedisValue)id.ToString();

        var t1 = batch.SetRemoveAsync(TimeTickerIdsKey, idStr);
        var t2 = batch.SortedSetRemoveAsync(TimeTickerPendingKey, idStr);

        batch.Execute();
        return Task.WhenAll(t1, t2);
    }

    private Task AddCronIndexesAsync(TCronTicker ticker)
    {
        return _db.SetAddAsync(CronIdsKey, ticker.Id.ToString());
    }

    private Task RemoveCronIndexesAsync(Guid id)
    {
        return _db.SetRemoveAsync(CronIdsKey, id.ToString());
    }

    private Task AddCronOccurrenceIndexesAsync(CronTickerOccurrenceEntity<TCronTicker> occurrence)
    {
        var batch = _db.CreateBatch();
        var idStr = (RedisValue)occurrence.Id.ToString();

        var t1 = batch.SetAddAsync(CronOccurrenceIdsKey, idStr);
        Task t2;
        if (CanAcquire(occurrence.Status, occurrence.LockHolder, _lockHolder))
            t2 = batch.SortedSetAddAsync(CronOccurrencePendingKey, idStr, ToScore(occurrence.ExecutionTime));
        else
            t2 = batch.SortedSetRemoveAsync(CronOccurrencePendingKey, idStr);

        batch.Execute();
        return Task.WhenAll(t1, t2);
    }

    private Task RemoveCronOccurrenceIndexesAsync(Guid id)
    {
        var batch = _db.CreateBatch();
        var idStr = (RedisValue)id.ToString();

        var t1 = batch.SetRemoveAsync(CronOccurrenceIdsKey, idStr);
        var t2 = batch.SortedSetRemoveAsync(CronOccurrencePendingKey, idStr);

        batch.Execute();
        return Task.WhenAll(t1, t2);
    }
    #endregion

    #region Time_Ticker_Core_Methods
    public async IAsyncEnumerable<TimeTickerEntity> QueueTimeTickers(TimeTickerEntity[] timeTickers, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;

        foreach (var timeTicker in timeTickers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var stored = await GetAsync<TTimeTicker>(TimeTickerKey(timeTicker.Id)).ConfigureAwait(false);
            if (stored == null) continue;
            if (stored.UpdatedAt != timeTicker.UpdatedAt) continue;
            if (!CanAcquire(stored.Status, stored.LockHolder, _lockHolder)) continue;

            stored.LockHolder = _lockHolder;
            stored.LockedAt = now;
            stored.UpdatedAt = now;
            stored.Status = TickerStatus.Queued;

            timeTicker.LockHolder = _lockHolder;
            timeTicker.LockedAt = now;
            timeTicker.UpdatedAt = now;
            timeTicker.Status = TickerStatus.Queued;

            await SetAsync(TimeTickerKey(stored.Id), stored).ConfigureAwait(false);
            await AddTimeTickerIndexesAsync(stored).ConfigureAwait(false);

            yield return timeTicker;
        }
    }

    public async IAsyncEnumerable<TimeTickerEntity> QueueTimedOutTimeTickers([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        var threshold = now.AddMilliseconds(-100);

        // Fetch due tickers by score
        var dueIds = await _db.SortedSetRangeByScoreAsync(TimeTickerPendingKey, double.NegativeInfinity, ToScore(threshold)).ConfigureAwait(false);

        foreach (var redisValue in dueIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Guid.TryParse(redisValue.ToString(), out var id)) continue;

            var ticker = await GetAsync<TTimeTicker>(TimeTickerKey(id)).ConfigureAwait(false);
            if (ticker == null) continue;
            if (!CanAcquire(ticker.Status, ticker.LockHolder, _lockHolder)) continue;

            ticker.LockHolder = _lockHolder;
            ticker.LockedAt = now;
            ticker.UpdatedAt = now;
            ticker.Status = TickerStatus.InProgress;

            await SetAsync(TimeTickerKey(ticker.Id), ticker).ConfigureAwait(false);
            await AddTimeTickerIndexesAsync(ticker).ConfigureAwait(false);

            yield return MapForQueue(ticker);
        }
    }

    public async Task ReleaseAcquiredTimeTickers(Guid[] timeTickerIds, CancellationToken cancellationToken = default)
    {
        var ids = timeTickerIds.Length == 0
            ? (await _db.SetMembersAsync(TimeTickerIdsKey).ConfigureAwait(false)).Select(x => Guid.Parse(x.ToString())).ToArray()
            : timeTickerIds;

        var now = _clock.UtcNow;
        foreach (var id in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var ticker = await GetAsync<TTimeTicker>(TimeTickerKey(id)).ConfigureAwait(false);
            if (ticker == null) continue;
            if (!CanAcquire(ticker.Status, ticker.LockHolder, _lockHolder)) continue;

            ticker.LockHolder = null;
            ticker.LockedAt = null;
            ticker.Status = TickerStatus.Idle;
            ticker.UpdatedAt = now;

            await SetAsync(TimeTickerKey(id), ticker).ConfigureAwait(false);
            await AddTimeTickerIndexesAsync(ticker).ConfigureAwait(false);
        }
    }

    public async Task<TimeTickerEntity[]> GetEarliestTimeTickers(CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        var oneSecondAgoScore = ToScore(now.AddSeconds(-1));

        // Get the earliest entry in window
        var first = await _db.SortedSetRangeByScoreWithScoresAsync(TimeTickerPendingKey, oneSecondAgoScore, double.PositiveInfinity, Exclude.None, Order.Ascending, 0, 1).ConfigureAwait(false);
        if (first.Length == 0) return [];

        var earliestScore = first[0].Score;
        var minSecond = new DateTime((long)earliestScore, DateTimeKind.Utc);
        var maxScore = ToScore(minSecond.AddSeconds(1));

        // Fetch all tickers within that second
        var ids = await _db.SortedSetRangeByScoreAsync(TimeTickerPendingKey, earliestScore, maxScore).ConfigureAwait(false);
        var result = new List<TimeTickerEntity>();
        foreach (var redisValue in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Guid.TryParse(redisValue.ToString(), out var id)) continue;
            var ticker = await GetAsync<TTimeTicker>(TimeTickerKey(id)).ConfigureAwait(false);
            if (ticker == null) continue;
            if (!CanAcquire(ticker.Status, ticker.LockHolder, _lockHolder)) continue;
            result.Add(MapForQueue(ticker));
        }

        return result.ToArray();
    }

    public async Task<int> UpdateTimeTicker(InternalFunctionContext functionContexts, CancellationToken cancellationToken = default)
    {
        var ticker = await GetAsync<TTimeTicker>(TimeTickerKey(functionContexts.TickerId)).ConfigureAwait(false);
        if (ticker == null) return 0;

        ApplyFunctionContextToTicker(ticker, functionContexts);
        await SetAsync(TimeTickerKey(ticker.Id), ticker).ConfigureAwait(false);
        await AddTimeTickerIndexesAsync(ticker).ConfigureAwait(false);
        return 1;
    }

    public async Task<byte[]> GetTimeTickerRequest(Guid id, CancellationToken cancellationToken)
    {
        var ticker = await GetAsync<TTimeTicker>(TimeTickerKey(id)).ConfigureAwait(false);
        return ticker?.Request;
    }

    public async Task UpdateTimeTickersWithUnifiedContext(Guid[] timeTickerIds, InternalFunctionContext functionContext, CancellationToken cancellationToken = default)
    {
        foreach (var id in timeTickerIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var ticker = await GetAsync<TTimeTicker>(TimeTickerKey(id)).ConfigureAwait(false);
            if (ticker == null) continue;
            ApplyFunctionContextToTicker(ticker, functionContext);
            await SetAsync(TimeTickerKey(id), ticker).ConfigureAwait(false);
            await AddTimeTickerIndexesAsync(ticker).ConfigureAwait(false);
        }
    }

    public async Task<TimeTickerEntity[]> AcquireImmediateTimeTickersAsync(Guid[] ids, CancellationToken cancellationToken = default)
    {
        if (ids == null || ids.Length == 0) return Array.Empty<TimeTickerEntity>();

        var now = _clock.UtcNow;
        var acquired = new List<TimeTickerEntity>();
        foreach (var id in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var ticker = await GetAsync<TTimeTicker>(TimeTickerKey(id)).ConfigureAwait(false);
            if (ticker == null) continue;
            if (!CanAcquire(ticker.Status, ticker.LockHolder, _lockHolder)) continue;

            ticker.LockHolder = _lockHolder;
            ticker.LockedAt = now;
            ticker.Status = TickerStatus.InProgress;
            ticker.UpdatedAt = now;

            await SetAsync(TimeTickerKey(id), ticker).ConfigureAwait(false);
            await AddTimeTickerIndexesAsync(ticker).ConfigureAwait(false);

            acquired.Add(MapForQueue(ticker));
        }

        return acquired.ToArray();
    }
    #endregion

    #region Cron_Ticker_Core_Methods
    public async Task MigrateDefinedCronTickers((string Function, string Expression)[] cronTickers, CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        const string seedPrefix = "MemoryTicker_Seeded_";

        // Build the complete set of registered function names to detect orphaned tickers.
        // This covers functions whose InitIdentifier was cleared by a dashboard edit (#517).
        var allRegisteredFunctions = TickerFunctionProvider.TickerFunctions.Keys
            .ToHashSet(StringComparer.Ordinal);

        // Load all existing cron tickers
        var existingIds = await _db.SetMembersAsync(CronIdsKey).ConfigureAwait(false);
        var existingList = new List<TCronTicker>();
        foreach (var redisValue in existingIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Guid.TryParse(redisValue.ToString(), out var id)) continue;
            var cron = await GetAsync<TCronTicker>(CronKey(id)).ConfigureAwait(false);
            if (cron != null) existingList.Add(cron);
        }

        // Delete all cron tickers whose function no longer exists in the code definitions
        var orphanedToDelete = existingList
            .Where(c => !allRegisteredFunctions.Contains(c.Function))
            .Select(c => c.Id).ToArray();

        foreach (var id in orphanedToDelete)
        {
            await RemoveCronIndexesAsync(id).ConfigureAwait(false);
            await RemoveCronOccurrenceIndexesAsyncForCron(id).ConfigureAwait(false);
            await _db.KeyDeleteAsync(CronKey(id)).ConfigureAwait(false);
        }

        var orphanedSet = orphanedToDelete.ToHashSet();
        var existingByFunction = existingList
            .Where(c => !orphanedSet.Contains(c.Id))
            .ToDictionary(c => c.Function, c => c, StringComparer.Ordinal);
        foreach (var (function, expression) in cronTickers)
        {
            if (existingByFunction.TryGetValue(function, out var cron))
            {
                if (!string.Equals(cron.Expression, expression, StringComparison.Ordinal))
                {
                    cron.Expression = expression;
                    cron.UpdatedAt = now;
                    await SetAsync(CronKey(cron.Id), cron).ConfigureAwait(false);
                }
            }
            else
            {
                var entity = new TCronTicker
                {
                    Id = Guid.NewGuid(),
                    Function = function,
                    Expression = expression,
                    InitIdentifier = $"{seedPrefix}{function}",
                    CreatedAt = now,
                    UpdatedAt = now,
                    Request = []
                };
                await SetAsync(CronKey(entity.Id), entity).ConfigureAwait(false);
                await AddCronIndexesAsync(entity).ConfigureAwait(false);
            }
        }
    }

    public async Task<CronTickerEntity[]> GetAllCronTickerExpressions(CancellationToken cancellationToken = default)
    {
        var ids = await _db.SetMembersAsync(CronIdsKey).ConfigureAwait(false);
        var list = new List<CronTickerEntity>();
        foreach (var redisValue in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Guid.TryParse(redisValue.ToString(), out var id)) continue;
            var cron = await GetAsync<TCronTicker>(CronKey(id)).ConfigureAwait(false);
            if (cron == null) continue;
            list.Add(new CronTickerEntity
            {
                Id = cron.Id,
                Expression = cron.Expression,
                Function = cron.Function,
                RetryIntervals = cron.RetryIntervals,
                Retries = cron.Retries
            });
        }
        return list.ToArray();
    }

    public async Task ReleaseDeadNodeTimeTickerResources(string instanceIdentifier, CancellationToken cancellationToken = default)
    {
        var ids = await _db.SetMembersAsync(TimeTickerIdsKey).ConfigureAwait(false);
        var now = _clock.UtcNow;

        foreach (var redisValue in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Guid.TryParse(redisValue.ToString(), out var id)) continue;
            var ticker = await GetAsync<TTimeTicker>(TimeTickerKey(id)).ConfigureAwait(false);
            if (ticker == null) continue;

            if (ticker.LockHolder == instanceIdentifier &&
                ticker.Status is TickerStatus.InProgress or TickerStatus.Idle or TickerStatus.Queued)
            {
                // Reset to Idle so another node can pick up the work
                ticker.LockHolder = null;
                ticker.LockedAt = null;
                ticker.Status = TickerStatus.Idle;
                ticker.UpdatedAt = now;
            }

            await SetAsync(TimeTickerKey(id), ticker).ConfigureAwait(false);
            await AddTimeTickerIndexesAsync(ticker).ConfigureAwait(false);
        }
    }
    #endregion

    #region Cron_TickerOccurrence_Core_Methods
    public async Task<CronTickerOccurrenceEntity<TCronTicker>> GetEarliestAvailableCronOccurrence(Guid[] ids, CancellationToken cancellationToken = default)
    {
        if (ids == null || ids.Length == 0) return null;

        // Scan earliest pending occurrences and pick the first that belongs to provided ids
        var candidates = await _db.SortedSetRangeByScoreAsync(CronOccurrencePendingKey, double.NegativeInfinity, double.PositiveInfinity, Exclude.None, Order.Ascending, 0, 200).ConfigureAwait(false);
        foreach (var redisValue in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Guid.TryParse(redisValue.ToString(), out var id)) continue;
            var occurrence = await GetAsync<CronTickerOccurrenceEntity<TCronTicker>>(CronOccurrenceKey(id)).ConfigureAwait(false);
            if (occurrence == null) continue;
            if (!ids.Contains(occurrence.CronTickerId)) continue;
            if (!CanAcquire(occurrence.Status, occurrence.LockHolder, _lockHolder)) continue;
            return occurrence;
        }

        return null;
    }

    public async IAsyncEnumerable<CronTickerOccurrenceEntity<TCronTicker>> QueueCronTickerOccurrences((DateTime Key, InternalManagerContext[] Items) cronTickerOccurrences, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        var executionTime = cronTickerOccurrences.Key;

        foreach (var item in cronTickerOccurrences.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (item.NextCronOccurrence is null)
            {
                var occurrence = new CronTickerOccurrenceEntity<TCronTicker>
                {
                    Id = Guid.NewGuid(),
                    Status = TickerStatus.Queued,
                    LockHolder = _lockHolder,
                    ExecutionTime = executionTime,
                    CronTickerId = item.Id,
                    LockedAt = now,
                    CreatedAt = now,
                    UpdatedAt = now,
                    CronTicker = new TCronTicker
                    {
                        Id = item.Id,
                        Function = item.FunctionName,
                        Expression = item.Expression,
                        Retries = item.Retries,
                        RetryIntervals = item.RetryIntervals
                    }
                };

                await SetAsync(CronOccurrenceKey(occurrence.Id), occurrence).ConfigureAwait(false);
                await AddCronOccurrenceIndexesAsync(occurrence).ConfigureAwait(false);
                yield return occurrence;
            }
            else
            {
                var existing = await GetAsync<CronTickerOccurrenceEntity<TCronTicker>>(CronOccurrenceKey(item.NextCronOccurrence.Id)).ConfigureAwait(false);
                if (existing == null) continue;
                if (!CanAcquire(existing.Status, existing.LockHolder, _lockHolder)) continue;

                existing.LockHolder = _lockHolder;
                existing.LockedAt = now;
                existing.UpdatedAt = now;
                existing.Status = TickerStatus.Queued;
                existing.ExecutionTime = executionTime;

                if (existing.CronTicker == null)
                {
                    existing.CronTicker = new TCronTicker
                    {
                        Id = item.Id,
                        Function = item.FunctionName,
                        Expression = item.Expression,
                        Retries = item.Retries,
                        RetryIntervals = item.RetryIntervals
                    };
                }

                await SetAsync(CronOccurrenceKey(existing.Id), existing).ConfigureAwait(false);
                await AddCronOccurrenceIndexesAsync(existing).ConfigureAwait(false);
                yield return existing;
            }
        }
    }

    public async IAsyncEnumerable<CronTickerOccurrenceEntity<TCronTicker>> QueueTimedOutCronTickerOccurrences([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        var threshold = now.AddMilliseconds(-100);

        var dueIds = await _db.SortedSetRangeByScoreAsync(CronOccurrencePendingKey, double.NegativeInfinity, ToScore(threshold)).ConfigureAwait(false);
        foreach (var redisValue in dueIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Guid.TryParse(redisValue.ToString(), out var id)) continue;
            var occurrence = await GetAsync<CronTickerOccurrenceEntity<TCronTicker>>(CronOccurrenceKey(id)).ConfigureAwait(false);
            if (occurrence == null) continue;
            if (!CanAcquire(occurrence.Status, occurrence.LockHolder, _lockHolder)) continue;

            occurrence.LockHolder = _lockHolder;
            occurrence.LockedAt = now;
            occurrence.UpdatedAt = now;
            occurrence.Status = TickerStatus.InProgress;

            await SetAsync(CronOccurrenceKey(id), occurrence).ConfigureAwait(false);
            await AddCronOccurrenceIndexesAsync(occurrence).ConfigureAwait(false);

            yield return occurrence;
        }
    }

    public async Task UpdateCronTickerOccurrence(InternalFunctionContext functionContext, CancellationToken cancellationToken = default)
    {
        var occurrence = await GetAsync<CronTickerOccurrenceEntity<TCronTicker>>(CronOccurrenceKey(functionContext.TickerId)).ConfigureAwait(false);
        if (occurrence == null) return;

        ApplyFunctionContextToCronOccurrence(occurrence, functionContext);
        await SetAsync(CronOccurrenceKey(occurrence.Id), occurrence).ConfigureAwait(false);
        await AddCronOccurrenceIndexesAsync(occurrence).ConfigureAwait(false);
    }

    public async Task ReleaseAcquiredCronTickerOccurrences(Guid[] occurrenceIds, CancellationToken cancellationToken = default)
    {
        var ids = occurrenceIds.Length == 0
            ? (await _db.SetMembersAsync(CronOccurrenceIdsKey).ConfigureAwait(false)).Select(x => Guid.Parse(x.ToString())).ToArray()
            : occurrenceIds;

        var now = _clock.UtcNow;
        foreach (var id in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var occurrence = await GetAsync<CronTickerOccurrenceEntity<TCronTicker>>(CronOccurrenceKey(id)).ConfigureAwait(false);
            if (occurrence == null) continue;
            if (!CanAcquire(occurrence.Status, occurrence.LockHolder, _lockHolder)) continue;

            occurrence.LockHolder = null;
            occurrence.LockedAt = null;
            occurrence.Status = TickerStatus.Idle;
            occurrence.UpdatedAt = now;

            await SetAsync(CronOccurrenceKey(id), occurrence).ConfigureAwait(false);
            await AddCronOccurrenceIndexesAsync(occurrence).ConfigureAwait(false);
        }
    }

    public async Task<byte[]> GetCronTickerOccurrenceRequest(Guid tickerId, CancellationToken cancellationToken = default)
    {
        var occurrence = await GetAsync<CronTickerOccurrenceEntity<TCronTicker>>(CronOccurrenceKey(tickerId)).ConfigureAwait(false);
        return occurrence?.CronTicker?.Request;
    }

    public async Task UpdateCronTickerOccurrencesWithUnifiedContext(Guid[] timeTickerIds, InternalFunctionContext functionContext, CancellationToken cancellationToken = default)
    {
        foreach (var id in timeTickerIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var occurrence = await GetAsync<CronTickerOccurrenceEntity<TCronTicker>>(CronOccurrenceKey(id)).ConfigureAwait(false);
            if (occurrence == null) continue;
            ApplyFunctionContextToCronOccurrence(occurrence, functionContext);
            await SetAsync(CronOccurrenceKey(id), occurrence).ConfigureAwait(false);
            await AddCronOccurrenceIndexesAsync(occurrence).ConfigureAwait(false);
        }
    }

    public async Task ReleaseDeadNodeOccurrenceResources(string instanceIdentifier, CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        var ids = await _db.SetMembersAsync(CronOccurrenceIdsKey).ConfigureAwait(false);
        foreach (var redisValue in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Guid.TryParse(redisValue.ToString(), out var id)) continue;
            var occurrence = await GetAsync<CronTickerOccurrenceEntity<TCronTicker>>(CronOccurrenceKey(id)).ConfigureAwait(false);
            if (occurrence == null) continue;

            if (occurrence.LockHolder == instanceIdentifier &&
                occurrence.Status is TickerStatus.InProgress or TickerStatus.Idle or TickerStatus.Queued)
            {
                // Reset to Idle so another node can pick up the work
                occurrence.LockHolder = null;
                occurrence.LockedAt = null;
                occurrence.Status = TickerStatus.Idle;
                occurrence.UpdatedAt = now;
            }

            await SetAsync(CronOccurrenceKey(id), occurrence).ConfigureAwait(false);
            await AddCronOccurrenceIndexesAsync(occurrence).ConfigureAwait(false);
        }
    }
    #endregion

    #region Time_Ticker_Shared_Methods
    public async Task<TTimeTicker> GetTimeTickerById(Guid id, CancellationToken cancellationToken = default)
    {
        return await GetAsync<TTimeTicker>(TimeTickerKey(id)).ConfigureAwait(false);
    }

    public async Task<TTimeTicker[]> GetTimeTickers(Expression<Func<TTimeTicker, bool>> predicate, CancellationToken cancellationToken)
    {
        var ids = await _db.SetMembersAsync(TimeTickerIdsKey).ConfigureAwait(false);
        var list = new List<TTimeTicker>();
        foreach (var redisValue in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Guid.TryParse(redisValue.ToString(), out var id)) continue;
            var ticker = await GetAsync<TTimeTicker>(TimeTickerKey(id)).ConfigureAwait(false);
            if (ticker != null) list.Add(ticker);
        }

        var query = list.Where(x => x.ParentId == null).AsQueryable();
        if (predicate != null) query = query.Where(predicate);

        return query.OrderByDescending(x => x.ExecutionTime).ToArray();
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
        var now = _clock.UtcNow;
        foreach (var ticker in tickers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ticker.CreatedAt = ticker.CreatedAt == default ? now : ticker.CreatedAt;
            ticker.UpdatedAt = ticker.UpdatedAt == default ? now : ticker.UpdatedAt;
            await SetAsync(TimeTickerKey(ticker.Id), ticker).ConfigureAwait(false);
            await AddTimeTickerIndexesAsync(ticker).ConfigureAwait(false);
        }
        return tickers.Length;
    }

    public async Task<int> UpdateTimeTickers(TTimeTicker[] tickers, CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        foreach (var ticker in tickers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ticker.UpdatedAt = now;
            await SetAsync(TimeTickerKey(ticker.Id), ticker).ConfigureAwait(false);
            await AddTimeTickerIndexesAsync(ticker).ConfigureAwait(false);
        }
        return tickers.Length;
    }

    public async Task<int> RemoveTimeTickers(Guid[] tickerIds, CancellationToken cancellationToken = default)
    {
        var count = 0;
        foreach (var id in tickerIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await RemoveTimeTickerIndexesAsync(id).ConfigureAwait(false);
            if (await _db.KeyDeleteAsync(TimeTickerKey(id)).ConfigureAwait(false))
                count++;
        }
        return count;
    }
    #endregion

    #region Cron_Ticker_Shared_Methods
    public async Task<TCronTicker> GetCronTickerById(Guid id, CancellationToken cancellationToken)
    {
        return await GetAsync<TCronTicker>(CronKey(id)).ConfigureAwait(false);
    }

    public async Task<TCronTicker[]> GetCronTickers(Expression<Func<TCronTicker, bool>> predicate, CancellationToken cancellationToken)
    {
        var ids = await _db.SetMembersAsync(CronIdsKey).ConfigureAwait(false);
        var list = new List<TCronTicker>();
        foreach (var redisValue in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Guid.TryParse(redisValue.ToString(), out var id)) continue;
            var cron = await GetAsync<TCronTicker>(CronKey(id)).ConfigureAwait(false);
            if (cron != null) list.Add(cron);
        }

        var query = list.AsQueryable();
        if (predicate != null) query = query.Where(predicate);

        return query.OrderByDescending(x => x.CreatedAt).ToArray();
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
        var now = _clock.UtcNow;
        foreach (var ticker in tickers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ticker.CreatedAt = ticker.CreatedAt == default ? now : ticker.CreatedAt;
            ticker.UpdatedAt = ticker.UpdatedAt == default ? now : ticker.UpdatedAt;
            await SetAsync(CronKey(ticker.Id), ticker).ConfigureAwait(false);
            await AddCronIndexesAsync(ticker).ConfigureAwait(false);
        }
        return tickers.Length;
    }

    public async Task<int> UpdateCronTickers(TCronTicker[] cronTicker, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        foreach (var ticker in cronTicker)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ticker.UpdatedAt = now;
            await SetAsync(CronKey(ticker.Id), ticker).ConfigureAwait(false);
            await AddCronIndexesAsync(ticker).ConfigureAwait(false);
        }
        return cronTicker.Length;
    }

    public async Task<int> RemoveCronTickers(Guid[] cronTickerIds, CancellationToken cancellationToken)
    {
        var removed = 0;
        foreach (var id in cronTickerIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await RemoveCronIndexesAsync(id).ConfigureAwait(false);
            await RemoveCronOccurrenceIndexesAsyncForCron(id).ConfigureAwait(false);
            if (await _db.KeyDeleteAsync(CronKey(id)).ConfigureAwait(false))
                removed++;
        }
        return removed;
    }
    #endregion

    #region Cron_TickerOccurrence_Shared_Methods
    public async Task<CronTickerOccurrenceEntity<TCronTicker>[]> GetAllCronTickerOccurrences(Expression<Func<CronTickerOccurrenceEntity<TCronTicker>, bool>> predicate, CancellationToken cancellationToken = default)
    {
        var ids = await _db.SetMembersAsync(CronOccurrenceIdsKey).ConfigureAwait(false);
        var list = new List<CronTickerOccurrenceEntity<TCronTicker>>();
        foreach (var redisValue in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Guid.TryParse(redisValue.ToString(), out var id)) continue;
            var occurrence = await GetAsync<CronTickerOccurrenceEntity<TCronTicker>>(CronOccurrenceKey(id)).ConfigureAwait(false);
            if (occurrence != null) list.Add(occurrence);
        }

        var query = list.AsQueryable();
        if (predicate != null) query = query.Where(predicate);
        return query.OrderByDescending(x => x.ExecutionTime).ToArray();
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
            await SetAsync(CronOccurrenceKey(occurrence.Id), occurrence).ConfigureAwait(false);
            await AddCronOccurrenceIndexesAsync(occurrence).ConfigureAwait(false);
        }
        return cronTickerOccurrences.Length;
    }

    public async Task<int> RemoveCronTickerOccurrences(Guid[] cronTickerOccurrences, CancellationToken cancellationToken)
    {
        var removed = 0;
        foreach (var id in cronTickerOccurrences)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await RemoveCronOccurrenceIndexesAsync(id).ConfigureAwait(false);
            if (await _db.KeyDeleteAsync(CronOccurrenceKey(id)).ConfigureAwait(false))
                removed++;
        }
        return removed;
    }

    public async Task<CronTickerOccurrenceEntity<TCronTicker>[]> AcquireImmediateCronOccurrencesAsync(Guid[] occurrenceIds, CancellationToken cancellationToken = default)
    {
        if (occurrenceIds == null || occurrenceIds.Length == 0)
            return Array.Empty<CronTickerOccurrenceEntity<TCronTicker>>();

        var now = _clock.UtcNow;
        var acquired = new List<CronTickerOccurrenceEntity<TCronTicker>>();
        foreach (var id in occurrenceIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var occurrence = await GetAsync<CronTickerOccurrenceEntity<TCronTicker>>(CronOccurrenceKey(id)).ConfigureAwait(false);
            if (occurrence == null) continue;
            if (!CanAcquire(occurrence.Status, occurrence.LockHolder, _lockHolder)) continue;

            occurrence.LockHolder = _lockHolder;
            occurrence.LockedAt = now;
            occurrence.Status = TickerStatus.InProgress;
            occurrence.UpdatedAt = now;

            await SetAsync(CronOccurrenceKey(id), occurrence).ConfigureAwait(false);
            await AddCronOccurrenceIndexesAsync(occurrence).ConfigureAwait(false);

            acquired.Add(occurrence);
        }

        return acquired.ToArray();
    }
    #endregion

    #region Apply FunctionContext
    private void ApplyFunctionContextToTicker(TTimeTicker ticker, InternalFunctionContext context)
    {
        var propsToUpdate = context.GetPropsToUpdate();

        if (propsToUpdate.Contains(nameof(InternalFunctionContext.Status)) &&
            context.Status != TickerStatus.Skipped)
        {
            ticker.Status = context.Status;
        }
        else if (propsToUpdate.Contains(nameof(InternalFunctionContext.Status)))
        {
            ticker.Status = context.Status;
            ticker.SkippedReason = context.ExceptionDetails;
        }

        if (propsToUpdate.Contains(nameof(InternalFunctionContext.ExecutedAt)))
            ticker.ExecutedAt = context.ExecutedAt;

        if (propsToUpdate.Contains(nameof(InternalFunctionContext.ExceptionDetails)) &&
            context.Status != TickerStatus.Skipped)
            ticker.ExceptionMessage = context.ExceptionDetails;

        if (propsToUpdate.Contains(nameof(InternalFunctionContext.ElapsedTime)))
            ticker.ElapsedTime = context.ElapsedTime;

        if (propsToUpdate.Contains(nameof(InternalFunctionContext.RetryCount)))
            ticker.RetryCount = context.RetryCount;

        if (propsToUpdate.Contains(nameof(InternalFunctionContext.ReleaseLock)))
        {
            ticker.LockHolder = null;
            ticker.LockedAt = null;
        }

        ticker.UpdatedAt = _clock.UtcNow;
    }

    private void ApplyFunctionContextToCronOccurrence(CronTickerOccurrenceEntity<TCronTicker> occurrence, InternalFunctionContext context)
    {
        var propsToUpdate = context.GetPropsToUpdate();

        if (propsToUpdate.Contains(nameof(InternalFunctionContext.Status)) &&
            context.Status != TickerStatus.Skipped)
        {
            occurrence.Status = context.Status;
        }
        else if (propsToUpdate.Contains(nameof(InternalFunctionContext.Status)))
        {
            occurrence.Status = context.Status;
            occurrence.SkippedReason = context.ExceptionDetails;
        }

        if (propsToUpdate.Contains(nameof(InternalFunctionContext.ExecutedAt)))
            occurrence.ExecutedAt = context.ExecutedAt;

        if (propsToUpdate.Contains(nameof(InternalFunctionContext.ExceptionDetails)) &&
            context.Status != TickerStatus.Skipped)
            occurrence.ExceptionMessage = context.ExceptionDetails;

        if (propsToUpdate.Contains(nameof(InternalFunctionContext.ElapsedTime)))
            occurrence.ElapsedTime = context.ElapsedTime;

        if (propsToUpdate.Contains(nameof(InternalFunctionContext.RetryCount)))
            occurrence.RetryCount = context.RetryCount;

        if (propsToUpdate.Contains(nameof(InternalFunctionContext.ReleaseLock)))
        {
            occurrence.LockHolder = null;
            occurrence.LockedAt = null;
        }

        occurrence.UpdatedAt = _clock.UtcNow;
    }
    #endregion

    #region Helpers for cascade cleanup
    private async Task RemoveCronOccurrenceIndexesAsyncForCron(Guid cronId)
    {
        var ids = await _db.SetMembersAsync(CronOccurrenceIdsKey).ConfigureAwait(false);
        foreach (var redisValue in ids)
        {
            if (!Guid.TryParse(redisValue.ToString(), out var occId)) continue;
            var occurrence = await GetAsync<CronTickerOccurrenceEntity<TCronTicker>>(CronOccurrenceKey(occId)).ConfigureAwait(false);
            if (occurrence == null || occurrence.CronTickerId != cronId) continue;
            await RemoveCronOccurrenceIndexesAsync(occId).ConfigureAwait(false);
            await _db.KeyDeleteAsync(CronOccurrenceKey(occId)).ConfigureAwait(false);
        }
    }
    #endregion
}
