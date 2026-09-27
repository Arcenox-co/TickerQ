#nullable disable
using System.Linq.Expressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using TickerQ.Caching.StackExchangeRedis.Helpers;
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
    private static readonly string AddTimeTickerOnceScript = LuaScriptLoader.Load("AddTimeTickerOnce");
    private static readonly string DeleteTimeTickerScript = LuaScriptLoader.Load("DeleteTimeTicker");
    private static readonly string UpdateCronDefinitionQueueOnlyScript =
        LuaScriptLoader.Load("UpdateCronDefinitionQueueOnly");

    public TickerRedisPersistenceProvider(
        [FromKeyedServices("tickerq")] IDatabase db,
        ITickerClock clock,
        SchedulerOptionsBuilder optionsBuilder,
        TickerQRedisOptionBuilder redisOptions,
        ILogger<TickerRedisPersistenceProvider<TTimeTicker, TCronTicker>> logger)
        : base(db, clock, optionsBuilder, redisOptions, logger) { }

    public bool SupportsResultPublication => true;
    public bool SupportsTimeTickerChainRepair => true;

    public async Task<TimeTickerChainRepairResult> RepairTimeTickerChainsAsync(
        CancellationToken cancellationToken = default)
    {
        var roots = await Serializer.LoadAllFromSetAsync<TTimeTicker>(
            TimeTickerIdsKey, TimeTickerKey, cancellationToken).ConfigureAwait(false);
        var expectedJson = new Dictionary<Guid, RedisValue>(roots.Count);
        foreach (var root in roots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            expectedJson[root.Id] = await Db.StringGetAsync(TimeTickerKey(root.Id)).ConfigureAwait(false);
        }

        var rows = roots.SelectMany(EnumerateAggregate).ToArray();
        var plan = TimeTickerChainRepairPlanner.Create(rows, cancellationToken);
        plan.ThrowIfMalformed();
        if (plan.Updates.Count == 0) return plan.Result;

        var changed = plan.Updates.Select(x => x.Row).ToHashSet();
        var updatedAt = Clock.UtcNow;
        foreach (var update in plan.Updates)
        {
            update.Row.ChainRootId = update.ChainRootId;
            update.Row.ChainGeneration = update.ChainGeneration;
            update.Row.UpdatedAt = updatedAt;
        }
        foreach (var root in roots.Where(root => EnumerateAggregate(root).Any(changed.Contains)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key = TimeTickerKey(root.Id);
            var transaction = Db.CreateTransaction();
            transaction.AddCondition(Condition.StringEqual(key, expectedJson[root.Id]));
            _ = transaction.StringSetAsync(key, Serializer.Serialize(root));
            if (!await transaction.ExecuteAsync().ConfigureAwait(false))
                throw new InvalidOperationException(
                    $"TimeTicker chain repair lost an optimistic write race for aggregate '{root.Id}'. Retry the idempotent repair.");
        }
        return plan.Result;
    }

    public Task<TickerResultEnvelope> GetTimeTickerResultAsync(Guid id, CancellationToken cancellationToken = default)
        => GetResultAsync(TimeTickerResultKey(id), cancellationToken);

    public Task<TickerResultEnvelope> GetCronTickerOccurrenceResultAsync(Guid id, CancellationToken cancellationToken = default)
        => GetResultAsync(CronOccurrenceResultKey(id), cancellationToken);

    private async Task<TickerResultEnvelope> GetResultAsync(string key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var value = await Db.StringGetAsync(key).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return value.IsNull ? null : RedisResultEnvelopeCodec.TryDeserialize((byte[])value);
    }

    #region Queryable_Methods
    public ITickerQueryable<TTimeTicker> TimeTickersQuery()
    {
        return new InMemoryTickerQueryable<TTimeTicker>(async ct =>
        {
            if (!await IsActivationPublicationVisibleAsync(ct).ConfigureAwait(false)) return [];
            var list = await Serializer.LoadAllFromSetAsync<TTimeTicker>(
                TimeTickerIdsKey, TimeTickerKey, ct).ConfigureAwait(false);
            return list;
        });
    }

    public ITickerQueryable<TCronTicker> CronTickersQuery()
    {
        return new InMemoryTickerQueryable<TCronTicker>(async ct =>
        {
            if (!await IsActivationPublicationVisibleAsync(ct).ConfigureAwait(false)) return [];
            var list = await Serializer.LoadAllFromSetAsync<TCronTicker>(
                CronIdsKey, CronKey, ct).ConfigureAwait(false);
            return list;
        });
    }

    public ITickerQueryable<CronTickerOccurrenceEntity<TCronTicker>> CronTickerOccurrencesQuery()
    {
        return new InMemoryTickerQueryable<CronTickerOccurrenceEntity<TCronTicker>>(async ct =>
        {
            if (!await IsActivationPublicationVisibleAsync(ct).ConfigureAwait(false)) return [];
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
        if (RuntimeAdmissionMisconfigured) return 0;
        if (IsClusterTopology(Db))
        {
            if (HasRuntimeActivationScopeBinding)
                throw new NotSupportedException(
                    "Scoped scheduler enqueue is unsupported on Redis Cluster because ticker, index, result, and activation keys do not share one hash slot. Use standalone Redis for atomic scheduler admission.");
            return await AddTimeTickersQueueOnlyClusterAsync(tickers, cancellationToken).ConfigureAwait(false);
        }

        var now = Clock.UtcNow;
        var insertedCount = 0;
        foreach (var ticker in tickers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            NormalizeAggregate(ticker, ticker.Id, null);
            ticker.CreatedAt = ticker.CreatedAt == default ? now : ticker.CreatedAt;
            ticker.UpdatedAt = ticker.UpdatedAt == default ? now : ticker.UpdatedAt;

            var pendingScore = ticker.ExecutionTime.HasValue &&
                               CanAcquire(ticker.Status, ticker.LockHolder, LockHolder)
                ? ToScore(ticker.ExecutionTime.Value).ToString(System.Globalization.CultureInfo.InvariantCulture)
                : string.Empty;
            if (!string.IsNullOrEmpty(ticker.InitIdentifier))
            {
                var result = await Db.ScriptEvaluateAsync(
                    AddTimeTickerOnceScript,
                    [(RedisKey)TimeTickerKey(ticker.Id), TimeTickerIdsKey, TimeTickerPendingKey,
                        RuntimeActivationMetadataKey, TerminalMutationEvidenceKey],
                    [(RedisValue)ticker.Id.ToString(), Serializer.Serialize(ticker), pendingScore,
                        "once", RuntimeActivationEpoch, RuntimeAdmissionMode,
                        $"{(int)TickerType.TimeTicker}:{ticker.Id:D}"])
                    .ConfigureAwait(false);
                if ((long)result == 0) continue;
                await IndexManager.AddTimeTickerIndexesAsync(ticker).ConfigureAwait(false);
                insertedCount++;
                continue;
            }

            var existing = await Serializer.GetAsync<TTimeTicker>(TimeTickerKey(ticker.Id)).ConfigureAwait(false);
            var aggregateIds = EnumerateAggregateIds(ticker).Concat(
                existing == null ? [] : EnumerateAggregateIds(existing)).Distinct().ToArray();
            var keys = new List<RedisKey>
            {
                TimeTickerKey(ticker.Id), TimeTickerIdsKey, TimeTickerPendingKey,
                RuntimeActivationMetadataKey, TerminalMutationEvidenceKey
            };
            keys.AddRange(aggregateIds.Select(TimeTickerResultKey).Select(x => (RedisKey)x));
            var inserted = await Db.ScriptEvaluateAsync(
                AddTimeTickerOnceScript, keys.ToArray(),
                new RedisValue[]
                {
                    ticker.Id.ToString(), Serializer.Serialize(ticker), pendingScore,
                    "upsert", RuntimeActivationEpoch, RuntimeAdmissionMode
                }.Concat(aggregateIds.Select(id =>
                    (RedisValue)$"{(int)TickerType.TimeTicker}:{id:D}")).ToArray()).ConfigureAwait(false);
            if ((long)inserted == 0) continue;
            await IndexManager.AddTimeTickerIndexesAsync(ticker).ConfigureAwait(false);
            insertedCount++;
        }
        return insertedCount;
    }

    private async Task<int> AddTimeTickersQueueOnlyClusterAsync(
        TTimeTicker[] tickers, CancellationToken cancellationToken)
    {
        // Queue-only Cluster producers predate activation fencing. Preserve that producer surface
        // with explicitly non-atomic, single-key commands; scheduler/scoped hosts must use the
        // standalone Lua path above so admission and publication remain one atomic operation.
        var now = Clock.UtcNow;
        var inserted = 0;
        foreach (var ticker in tickers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            NormalizeAggregate(ticker, ticker.Id, null);
            ticker.CreatedAt = ticker.CreatedAt == default ? now : ticker.CreatedAt;
            ticker.UpdatedAt = ticker.UpdatedAt == default ? now : ticker.UpdatedAt;
            var documentKey = (RedisKey)TimeTickerKey(ticker.Id);
            var serialized = (RedisValue)Serializer.Serialize(ticker);
            var written = string.IsNullOrEmpty(ticker.InitIdentifier)
                ? await Db.StringSetAsync(documentKey, serialized).ConfigureAwait(false)
                : await Db.StringSetAsync(documentKey, serialized, when: When.NotExists).ConfigureAwait(false);
            if (!written) continue;

            if (string.IsNullOrEmpty(ticker.InitIdentifier))
            {
                foreach (var id in EnumerateAggregateIds(ticker).Distinct())
                    await Db.KeyDeleteAsync(TimeTickerResultKey(id)).ConfigureAwait(false);
            }

            var idValue = (RedisValue)ticker.Id.ToString();
            await Db.SetAddAsync(TimeTickerIdsKey, idValue).ConfigureAwait(false);
            if (ticker.ExecutionTime.HasValue && CanAcquire(ticker.Status, ticker.LockHolder, LockHolder))
                await Db.SortedSetAddAsync(TimeTickerPendingKey, idValue, ToScore(ticker.ExecutionTime.Value))
                    .ConfigureAwait(false);
            else
                await Db.SortedSetRemoveAsync(TimeTickerPendingKey, idValue).ConfigureAwait(false);
            inserted++;
        }
        return inserted;
    }

    public async Task<int> UpdateTimeTickers(TTimeTicker[] tickers, CancellationToken cancellationToken = default)
    {
        if (RuntimeAdmissionMisconfigured) return 0;
        if (IsClusterTopology(Db))
        {
            if (HasRuntimeActivationScopeBinding)
                throw new NotSupportedException(
                    "Scoped scheduler update is unsupported on Redis Cluster without a shared partition hash slot.");
            return await UpdateTimeTickersQueueOnlyClusterAsync(tickers, cancellationToken).ConfigureAwait(false);
        }
        var now = Clock.UtcNow;
        var updated = 0;
        foreach (var ticker in tickers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            NormalizeAggregate(ticker, ticker.Id, ticker.ChainGeneration);
            ticker.UpdatedAt = now;
            var existing = await Serializer.GetAsync<TTimeTicker>(TimeTickerKey(ticker.Id)).ConfigureAwait(false);
            if (existing == null) continue;
            var aggregateIds = EnumerateAggregateIds(ticker)
                .Concat(EnumerateAggregateIds(existing)).Distinct().ToArray();
            var keys = new List<RedisKey>
            {
                TimeTickerKey(ticker.Id), TimeTickerIdsKey, TimeTickerPendingKey,
                RuntimeActivationMetadataKey, TerminalMutationEvidenceKey
            };
            keys.AddRange(aggregateIds.Select(TimeTickerResultKey).Select(x => (RedisKey)x));
            var pendingScore = ticker.ExecutionTime.HasValue &&
                               CanAcquire(ticker.Status, ticker.LockHolder, LockHolder)
                ? ToScore(ticker.ExecutionTime.Value).ToString(
                    System.Globalization.CultureInfo.InvariantCulture)
                : string.Empty;
            var result = await Db.ScriptEvaluateAsync(AddTimeTickerOnceScript, keys.ToArray(),
                new RedisValue[]
                {
                    ticker.Id.ToString(), Serializer.Serialize(ticker), pendingScore,
                    "upsert", RuntimeActivationEpoch, RuntimeAdmissionMode
                }.Concat(aggregateIds.Select(id =>
                    (RedisValue)$"{(int)TickerType.TimeTicker}:{id:D}")).ToArray()).ConfigureAwait(false);
            if ((long)result == 0) continue;
            await IndexManager.AddTimeTickerIndexesAsync(ticker).ConfigureAwait(false);
            updated++;
        }
        return updated;
    }

    private async Task<int> UpdateTimeTickersQueueOnlyClusterAsync(
        TTimeTicker[] tickers, CancellationToken cancellationToken)
    {
        var now = Clock.UtcNow;
        var updated = 0;
        foreach (var ticker in tickers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            NormalizeAggregate(ticker, ticker.Id, ticker.ChainGeneration);
            ticker.UpdatedAt = now;
            if (!await Db.KeyExistsAsync(TimeTickerKey(ticker.Id)).ConfigureAwait(false))
                continue;
            await Serializer.SetAsync(TimeTickerKey(ticker.Id), ticker).ConfigureAwait(false);
            foreach (var id in EnumerateAggregateIds(ticker).Distinct())
                await Db.KeyDeleteAsync(TimeTickerResultKey(id)).ConfigureAwait(false);
            await IndexManager.AddTimeTickerIndexesAsync(ticker).ConfigureAwait(false);
            updated++;
        }
        return updated;
    }

    public async Task<int> RemoveTimeTickers(Guid[] tickerIds, CancellationToken cancellationToken = default)
    {
        var count = 0;
        foreach (var id in tickerIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var ticker = await Serializer.GetAsync<TTimeTicker>(TimeTickerKey(id)).ConfigureAwait(false);
            var aggregateIds = ticker == null ? [id] : EnumerateAggregateIds(ticker).Distinct().ToArray();
            var keys = new List<RedisKey>
            {
                TimeTickerKey(id), TimeTickerIdsKey, TimeTickerPendingKey, TerminalMutationEvidenceKey
            };
            keys.AddRange(aggregateIds.Select(TimeTickerResultKey).Select(x => (RedisKey)x));
            var arguments = new List<RedisValue> { id.ToString() };
            arguments.AddRange(aggregateIds.Select(entityId =>
                (RedisValue)$"{(int)TickerType.TimeTicker}:{entityId:D}"));
            var removed = await Db.ScriptEvaluateAsync(
                DeleteTimeTickerScript, keys.ToArray(), arguments.ToArray()).ConfigureAwait(false);
            if ((long)removed == 1)
                count++;
        }
        return count;
    }

    private static IEnumerable<TTimeTicker> EnumerateAggregate(TTimeTicker root)
    {
        var pending = new Stack<TTimeTicker>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            yield return current;
            foreach (var child in current.Children ?? [])
                pending.Push(child);
        }
    }

    private static IEnumerable<Guid> EnumerateAggregateIds(TTimeTicker root)
    {
        var pending = new Stack<TTimeTicker>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            yield return current.Id;
            foreach (var child in current.Children ?? [])
                pending.Push(child);
        }
    }

    private static void NormalizeAggregate(TTimeTicker node, Guid rootId, Guid? generation)
    {
        node.ChainRootId = rootId;
        node.ChainGeneration = generation;
        foreach (var child in node.Children ?? [])
            NormalizeAggregate(child, rootId, generation);
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
        if (RuntimeAdmissionMisconfigured)
            throw new InvalidOperationException("Redis runtime admission is invalid for a scheduler without a namespace.");
        if (IsClusterTopology(Db))
        {
            if (HasRuntimeActivationScopeBinding)
                throw new NotSupportedException(
                    "Scoped scheduler Cron publication is unsupported on Redis Cluster without a shared partition hash slot.");
            return await InsertCronTickersQueueOnlyClusterAsync(tickers, cancellationToken).ConfigureAwait(false);
        }
        var now = Clock.UtcNow;
        var inserted = 0;
        foreach (var ticker in tickers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ticker.CreatedAt = ticker.CreatedAt == default ? now : ticker.CreatedAt;
            ticker.UpdatedAt = ticker.UpdatedAt == default ? now : ticker.UpdatedAt;
            if (ticker.DefinitionRevision <= 0)
                ticker.DefinitionRevision = 1;
            if (await WriteCronDefinitionAtomicAsync(ticker, cancellationToken,
                    expectedDefinitionRevision: 0).ConfigureAwait(false))
                inserted++;
        }
        return inserted;
    }

    private async Task<int> InsertCronTickersQueueOnlyClusterAsync(
        TCronTicker[] tickers, CancellationToken cancellationToken)
    {
        var now = Clock.UtcNow;
        var inserted = 0;
        foreach (var ticker in tickers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ticker.CreatedAt = ticker.CreatedAt == default ? now : ticker.CreatedAt;
            ticker.UpdatedAt = ticker.UpdatedAt == default ? now : ticker.UpdatedAt;
            if (ticker.DefinitionRevision <= 0)
                ticker.DefinitionRevision = 1;
            if (!await Db.StringSetAsync(
                    CronKey(ticker.Id), Serializer.Serialize(ticker), when: When.NotExists)
                .ConfigureAwait(false))
                continue;
            await Db.SetAddAsync(CronIdsKey, ticker.Id.ToString()).ConfigureAwait(false);
            inserted++;
        }
        return inserted;
    }

    public async Task<int> UpdateCronTickers(TCronTicker[] cronTicker, CancellationToken cancellationToken)
    {
        if (RuntimeAdmissionMisconfigured) return 0;
        if (IsClusterTopology(Db))
        {
            if (HasRuntimeActivationScopeBinding)
                throw new NotSupportedException(
                    "Scoped scheduler Cron update is unsupported on Redis Cluster without a shared partition hash slot.");
            return await UpdateCronTickersQueueOnlyClusterAsync(cronTicker, cancellationToken)
                .ConfigureAwait(false);
        }
        var now = Clock.UtcNow;
        var updated = 0;
        foreach (var ticker in cronTicker)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = await Serializer.GetAsync<TCronTicker>(CronKey(ticker.Id)).ConfigureAwait(false);
            if (current == null) continue;
            var expectedRevision = current.DefinitionRevision;
            ticker.DefinitionRevision = Math.Max(1, expectedRevision + 1);
            ticker.UpdatedAt = now;
            if (await WriteCronDefinitionAtomicAsync(ticker, cancellationToken,
                    quarantinePending: true, mutationTime: now,
                    expectedDefinitionRevision: expectedRevision).ConfigureAwait(false))
                updated++;
        }
        return updated;
    }

    private async Task<int> UpdateCronTickersQueueOnlyClusterAsync(
        TCronTicker[] tickers, CancellationToken cancellationToken)
    {
        // Queue-only Cluster producers cannot atomically couple the definition key to the global
        // discoverability set. Keep every command single-key and publish the document before repairing
        // discoverability; schedulers remain unsupported on this topology.
        var now = Clock.UtcNow;
        var updated = 0;
        foreach (var ticker in tickers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key = (RedisKey)CronKey(ticker.Id);
            var currentJson = await Db.StringGetAsync(key).ConfigureAwait(false);
            var current = currentJson.IsNullOrEmpty
                ? null
                : Serializer.DeserializeOrNull<TCronTicker>(currentJson.ToString());
            if (current == null) continue;
            ticker.DefinitionRevision = Math.Max(1, current.DefinitionRevision + 1);
            ticker.UpdatedAt = now;
            var cas = await Db.ScriptEvaluateAsync(UpdateCronDefinitionQueueOnlyScript,
                [key], [currentJson, (RedisValue)Serializer.Serialize(ticker)]).ConfigureAwait(false);
            if ((long)cas != 1)
                continue;
            await Db.SetAddAsync(CronIdsKey, ticker.Id.ToString()).ConfigureAwait(false);
            updated++;
        }
        return updated;
    }

    public async Task<int> RemoveCronTickers(Guid[] cronTickerIds, CancellationToken cancellationToken)
    {
        if (RuntimeAdmissionMisconfigured) return 0;
        if (IsClusterTopology(Db))
        {
            if (HasRuntimeActivationScopeBinding)
                throw new NotSupportedException(
                    "Scoped scheduler Cron removal is unsupported on Redis Cluster without a shared partition hash slot.");
            return await RemoveCronTickersQueueOnlyClusterAsync(cronTickerIds, cancellationToken)
                .ConfigureAwait(false);
        }
        var removed = 0;
        foreach (var id in cronTickerIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!await MarkCronDefinitionDeletingAtomicAsync(id, cancellationToken).ConfigureAwait(false))
                continue;
            var occurrenceIds = ParseGuidSet(await Db.SetMembersAsync(
                CronOccurrencesByCronKey(id)).ConfigureAwait(false));
            foreach (var occurrenceId in occurrenceIds)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var occurrence = await Serializer.GetAsync<CronTickerOccurrenceEntity<TCronTicker>>(
                    CronOccurrenceKey(occurrenceId)).ConfigureAwait(false);
                occurrence ??= new CronTickerOccurrenceEntity<TCronTicker>
                {
                    Id = occurrenceId, CronTickerId = id, ExecutionTime = default
                };
                if (occurrence.CronTickerId == id)
                    await DeleteCronOccurrenceAtomicAsync(occurrence).ConfigureAwait(false);
                else
                    await Db.SetRemoveAsync(CronOccurrencesByCronKey(id), occurrenceId.ToString()).ConfigureAwait(false);
            }
            if (await DeleteCronDefinitionAtomicAsync(id, cancellationToken).ConfigureAwait(false))
                removed++;
        }
        return removed;
    }

    private async Task<int> RemoveCronTickersQueueOnlyClusterAsync(
        Guid[] cronTickerIds, CancellationToken cancellationToken)
    {
        var removed = 0;
        foreach (var id in cronTickerIds.Distinct())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await Db.SetLengthAsync(CronOccurrencesByCronKey(id)).ConfigureAwait(false) != 0)
                throw new NotSupportedException(
                    $"Queue-only Redis Cluster cannot safely remove Cron definition '{id}' while occurrences reference it; remove those occurrences on a standalone scheduler topology first.");
            if (!await Db.KeyDeleteAsync(CronKey(id)).ConfigureAwait(false)) continue;
            await Db.SetRemoveAsync(CronIdsKey, id.ToString()).ConfigureAwait(false);
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
        var inserted = 0;
        foreach (var occurrence in cronTickerOccurrences)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await InsertCronOccurrenceAtomicAsync(occurrence, cancellationToken).ConfigureAwait(false))
                inserted++;
        }
        return inserted;
    }

    public async Task<int> RemoveCronTickerOccurrences(Guid[] cronTickerOccurrences, CancellationToken cancellationToken)
    {
        var removed = 0;
        foreach (var id in cronTickerOccurrences)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var occurrence = await Serializer.GetAsync<CronTickerOccurrenceEntity<TCronTicker>>(CronOccurrenceKey(id)).ConfigureAwait(false);
            if (occurrence != null && await DeleteCronOccurrenceAtomicAsync(occurrence).ConfigureAwait(false))
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

            var candidate = await Serializer.GetAsync<CronTickerOccurrenceEntity<TCronTicker>>(
                CronOccurrenceKey(id)).ConfigureAwait(false);
            if (candidate == null || candidate.CronTickerId == Guid.Empty)
                continue;
            var occurrence = await TryAcquireAsync<CronTickerOccurrenceEntity<TCronTicker>>(
                CronOccurrenceKey(id), CronOccurrenceResultKey(id),
                TickerStatus.InProgress, authoritativeCronKey: CronKey(candidate.CronTickerId)).ConfigureAwait(false);

            if (occurrence == null) continue;

            var authoritative = await Serializer.GetAsync<TCronTicker>(
                CronKey(occurrence.CronTickerId)).ConfigureAwait(false);
            if (authoritative == null || occurrence.DefinitionRevision != authoritative.DefinitionRevision)
                continue;
            occurrence.CronTicker = authoritative;

            await IndexManager.AddCronOccurrenceIndexesAsync(occurrence).ConfigureAwait(false);
            acquired.Add(occurrence);
        }

        return acquired.ToArray();
    }
    #endregion

    #region Retention
    public bool SupportsRetention => true;

    public async Task<RetentionIndexReconciliationResult> ReconcileRetentionIndexesAsync(
        int batchSize, CancellationToken cancellationToken = default)
    {
        if (batchSize <= 0)
            return RetentionIndexReconciliationResult.Completed;

        cancellationToken.ThrowIfCancellationRequested();
        var result = await Db.ScriptEvaluateAsync(ReconcileRetentionIndexesScript,
            [
                TimeTickerIdsKey,
                CronOccurrenceIdsKey,
                RetentionReconciliationPhaseKey,
                RetentionReconciliationCursorKey,
                TimeTickerRetentionSucceededKey,
                TimeTickerRetentionFailedKey,
                TimeTickerRetentionCancelledKey,
                TimeTickerRetentionSkippedKey,
                CronOccurrenceRetentionSucceededKey,
                CronOccurrenceRetentionFailedKey,
                CronOccurrenceRetentionCancelledKey,
                CronOccurrenceRetentionSkippedKey,
                RetentionReconciliationPendingKey
            ],
            [
                batchSize,
                $"{Prefix}:tt:",
                $"{Prefix}:co:",
                (int)TickerStatus.Done,
                (int)TickerStatus.DueDone,
                (int)TickerStatus.Failed,
                (int)TickerStatus.Cancelled,
                (int)TickerStatus.Skipped,
                Clock.UtcNow.ToUniversalTime().ToString("O")
            ]).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        var values = (RedisResult[])result;
        var examined = (int)(long)values[0];
        var hasMore = (long)values[1] == 1;
        return new RetentionIndexReconciliationResult(examined, hasMore, values[2].ToString());
    }

    public async Task<RetentionChainBatchResult> DeleteEligibleTimeTickerChainsAsync(
        RetentionCutoffs cutoffs, int batchSize, RetentionCursor cursor,
        CancellationToken cancellationToken = default)
    {
        var reconciliation = await ReconcileRetentionIndexesAsync(batchSize, cancellationToken).ConfigureAwait(false);
        var candidates = await GetRetentionCandidatesAsync(
            true, cutoffs, batchSize + 1, cancellationToken).ConfigureAwait(false);
        var hasMore = reconciliation.HasMore || candidates.Count > batchSize;
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
                    TimeTickerRetentionSkippedKey,
                    TimeTickerResultKey(candidate.Id),
                    TerminalMutationEvidenceKey
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
        var reconciliation = await ReconcileRetentionIndexesAsync(batchSize, cancellationToken).ConfigureAwait(false);
        var candidates = await GetRetentionCandidatesAsync(
            false, cutoffs, batchSize + 1, cancellationToken).ConfigureAwait(false);
        var hasMore = reconciliation.HasMore || candidates.Count > batchSize;
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

            if (await DeleteCronOccurrenceAtomicAsync(occurrence, candidate.FirstStatus,
                    candidate.SecondStatus, candidate.Cutoff, Clock.UtcNow).ConfigureAwait(false))
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
                Exclude.None, Order.Ascending, 0, take).ConfigureAwait(false);
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

    private void AddRetentionSource(
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

    private string TimeRetentionKey(TickerStatus status) => status switch
    {
        TickerStatus.Done or TickerStatus.DueDone => TimeTickerRetentionSucceededKey,
        TickerStatus.Failed => TimeTickerRetentionFailedKey,
        TickerStatus.Cancelled => TimeTickerRetentionCancelledKey,
        TickerStatus.Skipped => TimeTickerRetentionSkippedKey,
        _ => throw new ArgumentOutOfRangeException(nameof(status))
    };

    private string OccurrenceRetentionKey(TickerStatus status) => status switch
    {
        TickerStatus.Done or TickerStatus.DueDone => CronOccurrenceRetentionSucceededKey,
        TickerStatus.Failed => CronOccurrenceRetentionFailedKey,
        TickerStatus.Cancelled => CronOccurrenceRetentionCancelledKey,
        TickerStatus.Skipped => CronOccurrenceRetentionSkippedKey,
        _ => throw new ArgumentOutOfRangeException(nameof(status))
    };

    private sealed record RetentionCandidate(
        Guid Id, DateTime ExecutedAt, DateTime Cutoff, int FirstStatus, int SecondStatus);

    // Bounded, resumable set scans repair known IDs and bounded keyspace scans recover JSON
    // documents written before their authoritative ID/index updates. Four checkpointed phases make
    // crash recovery idempotent without an unbounded KEYS operation.
    private const string ReconcileRetentionIndexesScript = """
        -- retention index reconciliation
        local phase = redis.call('GET', KEYS[3]) or 'time_ids'
        if phase == 'time' then phase = 'time_ids' end
        if phase == 'occurrence' then phase = 'occurrence_ids' end
        local cursor = redis.call('GET', KEYS[4]) or '0'
        local isTime = string.sub(phase, 1, 4) == 'time'
        local scanKeys = string.sub(phase, -5) == '_keys'
        local idsKey = isTime and KEYS[1] or KEYS[2]
        local prefix = isTime and ARGV[2] or ARGV[3]
        local firstIndex = isTime and 5 or 9
        local maxRecords = tonumber(ARGV[1])
        local members = {}
        local nextCursor = cursor

        while #members < maxRecords and redis.call('LLEN', KEYS[13]) > 0 do
            table.insert(members, redis.call('LPOP', KEYS[13]))
        end
        if #members == 0 then
            local scan
            if scanKeys then
                scan = redis.call('SCAN', cursor, 'MATCH', prefix .. '????????-????-????-????-????????????',
                    'COUNT', maxRecords)
            else
                scan = redis.call('SSCAN', idsKey, cursor, 'COUNT', maxRecords)
            end
            nextCursor = scan[1]
            for index, value in ipairs(scan[2]) do
                local id = scanKeys and string.sub(value, string.len(prefix) + 1) or value
                if index <= maxRecords then table.insert(members, id)
                else redis.call('RPUSH', KEYS[13], id) end
            end
        end

        local function normalizedDateTime(value)
            local year, month, day, hour, minute, second, fraction = string.match(
                value or '', '^(%d%d%d%d)%-(%d%d)%-(%d%d)T(%d%d):(%d%d):(%d%d)%.?(%d*)')
            if not year then return nil end
            fraction = string.sub((fraction or '') .. '0000000', 1, 7)
            return year .. month .. day .. hour .. minute .. second .. fraction
        end

        local function dateTimeTicks(value)
            local year, month, day, hour, minute, second, fraction = string.match(
                value, '^(%d%d%d%d)%-(%d%d)%-(%d%d)T(%d%d):(%d%d):(%d%d)%.?(%d*)')
            if not year then return nil end
            year, month, day = tonumber(year), tonumber(month), tonumber(day)
            hour, minute, second = tonumber(hour), tonumber(minute), tonumber(second)
            local priorYear = year - 1
            local days = priorYear * 365 + math.floor(priorYear / 4)
                - math.floor(priorYear / 100) + math.floor(priorYear / 400)
            local beforeMonth = {0,31,59,90,120,151,181,212,243,273,304,334}
            days = days + beforeMonth[month] + day - 1
            if month > 2 and (year % 400 == 0 or (year % 4 == 0 and year % 100 ~= 0)) then
                days = days + 1
            end
            fraction = string.sub((fraction or '') .. '0000000', 1, 7)
            return days * 864000000000 + hour * 36000000000 + minute * 600000000
                + second * 10000000 + tonumber(fraction)
        end

        local now = normalizedDateTime(ARGV[9])
        for _, id in ipairs(members) do
            for index = firstIndex, firstIndex + 3 do
                redis.call('ZREM', KEYS[index], id)
            end

            local raw = redis.call('GET', prefix .. id)
            if raw then
                local ok, obj = pcall(cjson.decode, raw)
                if ok then
                    if scanKeys then redis.call('SADD', idsKey, id) end
                    local lease = obj.LeaseUntil ~= nil and obj.LeaseUntil ~= cjson.null
                        and obj.LeaseUntil ~= '' and normalizedDateTime(obj.LeaseUntil) or nil
                    local eligible = obj.ExecutedAt ~= nil and obj.ExecutedAt ~= cjson.null
                        and (obj.AcquisitionToken == nil or obj.AcquisitionToken == cjson.null or obj.AcquisitionToken == '')
                        and (lease == nil or lease <= now)
                    if isTime and ((obj.ParentId ~= nil and obj.ParentId ~= cjson.null and obj.ParentId ~= '')
                        or (obj.Children ~= nil and obj.Children ~= cjson.null and next(obj.Children) ~= nil)) then
                        eligible = false
                    end

                    if eligible then
                        local status = tonumber(obj.Status)
                        local offset = nil
                        if status == tonumber(ARGV[4]) or status == tonumber(ARGV[5]) then offset = 0
                        elseif status == tonumber(ARGV[6]) then offset = 1
                        elseif status == tonumber(ARGV[7]) then offset = 2
                        elseif status == tonumber(ARGV[8]) then offset = 3 end
                        if offset ~= nil then
                            local ticks = dateTimeTicks(obj.ExecutedAt)
                            if ticks ~= nil then redis.call('ZADD', KEYS[firstIndex + offset], ticks, id) end
                        end
                    end
                end
            end
        end

        local completed = 0
        if nextCursor == '0' and redis.call('LLEN', KEYS[13]) == 0 then
            if phase == 'time_ids' then phase = 'time_keys'
            elseif phase == 'time_keys' then phase = 'occurrence_ids'
            elseif phase == 'occurrence_ids' then phase = 'occurrence_keys'
            else phase = 'time_ids'; completed = 1 end
        end
        redis.call('SET', KEYS[3], phase)
        redis.call('SET', KEYS[4], nextCursor)
        return {#members, completed == 0 and 1 or 0,
            phase .. ':' .. nextCursor .. ':pending=' .. redis.call('LLEN', KEYS[13])}
        """;

    private const string DeleteTimeTickerForRetentionScript = """
        local function normalizedDateTime(value)
            local year, month, day, hour, minute, second, fraction = string.match(
                value or '', '^(%d%d%d%d)%-(%d%d)%-(%d%d)T(%d%d):(%d%d):(%d%d)%.?(%d*)')
            if not year then return nil end
            return year .. month .. day .. hour .. minute .. second
                .. string.sub((fraction or '') .. '0000000', 1, 7)
        end
        local raw = redis.call('GET', KEYS[1])
        if not raw then return 0 end
        local obj = cjson.decode(raw)
        local status = tonumber(obj.Status)
        if status ~= tonumber(ARGV[1]) and status ~= tonumber(ARGV[2]) then return 0 end
        local executedAt = obj.ExecutedAt ~= nil and obj.ExecutedAt ~= cjson.null
            and normalizedDateTime(obj.ExecutedAt) or nil
        if executedAt == nil or executedAt >= normalizedDateTime(ARGV[3]) then return 0 end
        if obj.AcquisitionToken ~= nil and obj.AcquisitionToken ~= cjson.null and obj.AcquisitionToken ~= '' then return 0 end
        local leaseUntil = obj.LeaseUntil ~= nil and obj.LeaseUntil ~= cjson.null
            and obj.LeaseUntil ~= '' and normalizedDateTime(obj.LeaseUntil) or nil
        if leaseUntil ~= nil and leaseUntil > normalizedDateTime(ARGV[4]) then return 0 end
        if obj.ParentId ~= nil and obj.ParentId ~= cjson.null and obj.ParentId ~= '' then return 0 end
        if obj.Children ~= nil and obj.Children ~= cjson.null and next(obj.Children) ~= nil then return 0 end
        redis.call('DEL', KEYS[1])
        redis.call('SREM', KEYS[2], ARGV[5])
        redis.call('ZREM', KEYS[3], ARGV[5])
        redis.call('ZREM', KEYS[4], ARGV[5])
        redis.call('ZREM', KEYS[5], ARGV[5])
        redis.call('ZREM', KEYS[6], ARGV[5])
        redis.call('ZREM', KEYS[7], ARGV[5])
        redis.call('DEL', KEYS[8])
        redis.call('HDEL', KEYS[9], '1:' .. string.lower(ARGV[5]))
        return 1
        """;

    private const string DeleteOccurrenceForRetentionScript = """
        local function normalizedDateTime(value)
            local year, month, day, hour, minute, second, fraction = string.match(
                value or '', '^(%d%d%d%d)%-(%d%d)%-(%d%d)T(%d%d):(%d%d):(%d%d)%.?(%d*)')
            if not year then return nil end
            return year .. month .. day .. hour .. minute .. second
                .. string.sub((fraction or '') .. '0000000', 1, 7)
        end
        local raw = redis.call('GET', KEYS[1])
        if not raw then return 0 end
        local obj = cjson.decode(raw)
        local status = tonumber(obj.Status)
        if status ~= tonumber(ARGV[1]) and status ~= tonumber(ARGV[2]) then return 0 end
        local executedAt = obj.ExecutedAt ~= nil and obj.ExecutedAt ~= cjson.null
            and normalizedDateTime(obj.ExecutedAt) or nil
        if executedAt == nil or executedAt >= normalizedDateTime(ARGV[3]) then return 0 end
        if obj.AcquisitionToken ~= nil and obj.AcquisitionToken ~= cjson.null and obj.AcquisitionToken ~= '' then return 0 end
        local leaseUntil = obj.LeaseUntil ~= nil and obj.LeaseUntil ~= cjson.null
            and obj.LeaseUntil ~= '' and normalizedDateTime(obj.LeaseUntil) or nil
        if leaseUntil ~= nil and leaseUntil > normalizedDateTime(ARGV[4]) then return 0 end
        redis.call('DEL', KEYS[1])
        redis.call('SREM', KEYS[2], ARGV[5])
        redis.call('ZREM', KEYS[3], ARGV[5])
        redis.call('ZREM', KEYS[4], ARGV[5])
        redis.call('ZREM', KEYS[5], ARGV[5])
        redis.call('ZREM', KEYS[6], ARGV[5])
        redis.call('ZREM', KEYS[7], ARGV[5])
        redis.call('SREM', KEYS[8], ARGV[5])
        redis.call('DEL', KEYS[9])
        return 1
        """;
    #endregion
}
