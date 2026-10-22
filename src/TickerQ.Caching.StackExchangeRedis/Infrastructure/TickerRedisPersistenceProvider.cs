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

internal sealed partial class TickerRedisPersistenceProvider<TTimeTicker, TCronTicker> :
    BaseRedisPersistenceProvider<TTimeTicker, TCronTicker>,
    ITickerPersistenceProvider<TTimeTicker, TCronTicker>, ITickerDefinitionStore
    where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
    where TCronTicker : CronTickerEntity, new()
{
    public bool IntegrationSettingsArePersistent => true;

    public bool SupportsIntegrationMonitoring => true;
    public Task<TickerQ.Utilities.Integrations.IntegrationSettingsRecord> ReadIntegrationSettingsAsync(CancellationToken cancellationToken = default)
        => ReadIntegrationRecordAsync(Prefix + "integrations:settings", cancellationToken);
    public Task<bool> SaveIntegrationSettingsAsync(TickerQ.Utilities.Integrations.IntegrationSettingsRecord record, Guid expectedVersion, CancellationToken cancellationToken = default)
        => SaveIntegrationRecordAsync(Prefix + "integrations:settings", record, expectedVersion, cancellationToken);
    public Task<TickerQ.Utilities.Integrations.IntegrationSettingsRecord> ReadIntegrationMonitoringAsync(CancellationToken cancellationToken = default)
        => ReadIntegrationRecordAsync(Prefix + "integrations:monitoring", cancellationToken);
    public Task<bool> SaveIntegrationMonitoringAsync(TickerQ.Utilities.Integrations.IntegrationSettingsRecord record, Guid expectedVersion, CancellationToken cancellationToken = default)
        => SaveIntegrationRecordAsync(Prefix + "integrations:monitoring", record, expectedVersion, cancellationToken);

    private async Task<TickerQ.Utilities.Integrations.IntegrationSettingsRecord> ReadIntegrationRecordAsync(string key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var values = await Db.HashGetAsync(key, new RedisValue[] { "version", "payload" }).ConfigureAwait(false);
        return values[0].IsNull ? null : new(Guid.Parse(values[0].ToString()), values[1].ToString());
    }

    private async Task<bool> SaveIntegrationRecordAsync(string key, TickerQ.Utilities.Integrations.IntegrationSettingsRecord record, Guid expectedVersion, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        const string script = "local old = redis.call('HGET', KEYS[1], 'version'); if (old or '') ~= ARGV[1] then return 0 end; redis.call('HSET', KEYS[1], 'version', ARGV[2], 'payload', ARGV[3]); return 1";
        return (long)await Db.ScriptEvaluateAsync(script, new RedisKey[] { key },
            new RedisValue[] { expectedVersion == Guid.Empty ? "" : expectedVersion.ToString(), record.Version.ToString(), record.ProtectedConfiguration }).ConfigureAwait(false) == 1;
    }

    private static readonly string UpdateTimeTickerConfigurationScript = LuaScriptLoader.Load("UpdateTimeTickerConfiguration");
    private static readonly string AddTimeTickerOnceScript = LuaScriptLoader.Load("AddTimeTickerOnce");
    private static readonly string DeleteTimeTickerScript = LuaScriptLoader.Load("DeleteTimeTicker");
    private static readonly string SetChainDeleteBehaviorScript = LuaScriptLoader.Load("SetChainDeleteBehavior");
    private static readonly string RetryTimeTickerChainScript = LuaScriptLoader.Load("RetryTimeTickerChain");
    private static readonly string AcquireCronOccurrenceOnDemandScript =
        LuaScriptLoader.Load("AcquireCronOccurrenceOnDemand");
    private static readonly string UpdateCronDefinitionQueueOnlyScript =
        LuaScriptLoader.Load("UpdateCronDefinitionQueueOnly");
    internal Func<Guid, CancellationToken, Task> BeforeTimeTickerConfigurationWriteForTestAsync { get; set; }
    internal Func<Guid, CancellationToken, Task> BeforeTimeTickerRetentionDeleteForTestAsync { get; set; }
    internal Func<CronTickerOccurrenceEntity<TCronTicker>, CancellationToken, Task>
        BeforeCronOccurrenceDeleteForTestAsync { get; set; }

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
        => new RedisTickerQueryable<TTimeTicker>((predicates, ct) =>
            ScanQueryAsync(TimeTickerIdsKey, TimeTickerKey, predicates, ct));

    public ITickerQueryable<TTimeTicker> TimeTickersMonitoringQuery()
        => new RedisTickerQueryable<TTimeTicker>((_, ct) => ScanMonitoringQueryAsync(ct));

    public ITickerQueryable<TCronTicker> CronTickersQuery()
        => new RedisTickerQueryable<TCronTicker>((predicates, ct) =>
            ScanQueryAsync(CronIdsKey, CronKey, predicates, ct));

    public ITickerQueryable<CronTickerOccurrenceEntity<TCronTicker>> CronTickerOccurrencesQuery()
        => new RedisTickerQueryable<CronTickerOccurrenceEntity<TCronTicker>>((predicates, ct) =>
            ScanQueryAsync(CronOccurrenceIdsKey, CronOccurrenceKey, predicates, ct));
    #endregion

    #region Time_Ticker_Shared_Methods
    public async Task<TTimeTicker> GetTimeTickerById(Guid id, CancellationToken cancellationToken = default)
    {
        var root = await Serializer.GetAsync<TTimeTicker>(TimeTickerKey(id)).ConfigureAwait(false);
        if (root != null) return root;
        var rootId = await FindTimeTickerAggregateIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (!rootId.HasValue) return null;
        root = await Serializer.GetAsync<TTimeTicker>(TimeTickerKey(rootId.Value)).ConfigureAwait(false);
        return root == null ? null : FindEmbeddedTimeTicker(root.Children, id);
    }

    public async Task<TTimeTicker[]> GetTimeTickers(Expression<Func<TTimeTicker, bool>> predicate, CancellationToken cancellationToken)
    {
        var compiled = predicate?.Compile();

        var list = await Serializer.LoadAllFromSetAsync<TTimeTicker>(
            TimeTickerIdsKey, TimeTickerKey, cancellationToken,
            t => t.ParentId == null && (compiled == null || compiled(t))).ConfigureAwait(false);

        return list.OrderByDescending(x => x.ExecutionTime).ToArray();
    }

    public Task<PaginationResult<TTimeTicker>> GetTimeTickersPaginated(Expression<Func<TTimeTicker, bool>> predicate, int pageNumber, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = TimeTickersQuery().Where(row => row.ParentId == null);
        if (predicate != null) query = query.Where(predicate);
        return query.OrderByDescending(row => row.ExecutionTime)
            .ToPaginatedAsync(pageNumber, pageSize, cancellationToken);
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
        var cluster = IsClusterTopology(Db);
        if (cluster && HasRuntimeActivationScopeBinding)
            throw new NotSupportedException("Scoped scheduler update is unsupported on Redis Cluster without a shared partition hash slot.");
        var updated = 0;
        foreach (var ticker in tickers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TimeTickerConfigurationSnapshot.TryFlatten(new[] { ticker }, out var incoming)) continue;
            var rootId = ticker.ParentId.HasValue ? ticker.ChainRootId : ticker.Id;
            if (!rootId.HasValue) continue;
            var raw = await Db.StringGetAsync(TimeTickerKey(rootId.Value)).ConfigureAwait(false);
            if (raw.IsNullOrEmpty) continue;
            var existing = Serializer.DeserializeOrNull<TTimeTicker>(raw.ToString());
            if (existing == null || !TimeTickerConfigurationSnapshot.TryFlatten(new[] { existing }, out var current) ||
                !current.TryGetValue(ticker.Id, out var previous) ||
                !TimeTickerConfigurationSnapshot.TryFlatten(new[] { previous }, out var previousSubtree) ||
                incoming.Count != previousSubtree.Count || incoming.Values.Any(row =>
                    !previousSubtree.TryGetValue(row.Id, out var old) || !TimeTickerConfigurationSnapshot.Matches(old, row)))
                continue;
            ticker.UpdatedAt = Clock.UtcNow;
            TTimeTicker replacement;
            if (ticker.ParentId.HasValue)
            {
                if (!current.TryGetValue(ticker.ParentId.Value, out var parent) ||
                    ticker.ChainGeneration != existing.ChainGeneration) continue;
                parent.Children = parent.Children.Select(child => child.Id == ticker.Id ? ticker : child).ToList();
                replacement = existing;
            }
            else replacement = ticker;
            var pendingScore = replacement.ExecutionTime.HasValue && CanAcquire(replacement.Status, replacement.LockHolder, LockHolder)
                ? ToScore(replacement.ExecutionTime.Value).ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty;
            var retentionIndex = !replacement.ParentId.HasValue && replacement.ExecutedAt.HasValue &&
                                 !replacement.AcquisitionToken.HasValue &&
                                 (!replacement.LeaseUntil.HasValue || replacement.LeaseUntil <= Clock.UtcNow)
                ? replacement.Status switch
                {
                    TickerStatus.Done or TickerStatus.DueDone => 1,
                    TickerStatus.Failed => 2,
                    TickerStatus.Cancelled => 3,
                    TickerStatus.Skipped => 4,
                    _ => 0
                } : 0;
            var retentionScore = retentionIndex == 0 ? string.Empty
                : ToScore(replacement.ExecutedAt.Value).ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (BeforeTimeTickerConfigurationWriteForTestAsync != null)
                await BeforeTimeTickerConfigurationWriteForTestAsync(rootId.Value, cancellationToken).ConfigureAwait(false);
            var result = await Db.ScriptEvaluateAsync(cluster
                    ? "if redis.call('GET', KEYS[1]) ~= ARGV[1] then return 0 end redis.call('SET', KEYS[1], ARGV[2]) return 1"
                    : UpdateTimeTickerConfigurationScript,
                cluster ? new RedisKey[] { TimeTickerKey(rootId.Value) }
                    : new RedisKey[] { TimeTickerKey(rootId.Value), RuntimeActivationMetadataKey, TimeTickerPendingKey,
                        TimeTickerIdsKey, TimeTickerRetentionSucceededKey, TimeTickerRetentionFailedKey,
                        TimeTickerRetentionCancelledKey, TimeTickerRetentionSkippedKey },
                new RedisValue[] { raw, Serializer.Serialize(replacement), RuntimeActivationEpoch,
                    RuntimeAdmissionMode, rootId.Value.ToString(), pendingScore, retentionIndex, retentionScore }).ConfigureAwait(false);
            if ((long)result != 1) continue;
            if (cluster) await IndexManager.AddTimeTickerIndexesAsync(replacement).ConfigureAwait(false);
            updated++;
        }
        return updated;
    }

    public async Task<TimeTickerChainRetryAcquisition<TTimeTicker>> AcquireTimeTickerChainRetryAsync(
        Guid id, DateTime executionTime, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (RuntimeAdmissionMisconfigured) return null;
        if (IsClusterTopology(Db))
            throw new NotSupportedException(
                "Atomic Redis chain retry requires standalone Redis because the aggregate, results, and indexes must share one transaction boundary.");

        var rootId = await FindTimeTickerAggregateIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (!rootId.HasValue)
        {
            var direct = await Db.KeyExistsAsync(TimeTickerKey(id)).ConfigureAwait(false);
            if (!direct) return null;
            rootId = id;
        }
        var raw = await Db.StringGetAsync(TimeTickerKey(rootId.Value)).ConfigureAwait(false);
        if (raw.IsNullOrEmpty) return null;
        var root = Serializer.DeserializeOrNull<TTimeTicker>(raw.ToString());
        if (root == null) return null;

        var now = Clock.UtcNow;
        var plan = TimeTickerChainRetryPlanner.Prepare(
            root, id, LockHolder, executionTime, now, NextLeaseUntil(now));
        var resetIds = plan.ResetIds.OrderBy(value => value).ToArray();
        var keys = new List<RedisKey>
        {
            TimeTickerKey(rootId.Value), RuntimeActivationMetadataKey, TimeTickerPendingKey,
            TimeTickerIdsKey, TimeTickerRetentionSucceededKey, TimeTickerRetentionFailedKey,
            TimeTickerRetentionCancelledKey, TimeTickerRetentionSkippedKey,
            TerminalMutationEvidenceKey
        };
        keys.AddRange(resetIds.Select(value => (RedisKey)TimeTickerResultKey(value)));
        var args = new List<RedisValue>
        {
            raw, Serializer.Serialize(plan.Root), RuntimeActivationEpoch, RuntimeAdmissionMode,
            rootId.Value.ToString(), resetIds.Length
        };
        args.AddRange(resetIds.Select(value => (RedisValue)$"{(int)TickerType.TimeTicker}:{value:D}"));
        var changed = (long)await Db.ScriptEvaluateAsync(
            RetryTimeTickerChainScript, keys.ToArray(), args.ToArray()).ConfigureAwait(false);
        if (changed != 1) return null;
        await IndexManager.AddTimeTickerIndexesAsync(plan.Root).ConfigureAwait(false);
        return new TimeTickerChainRetryAcquisition<TTimeTicker>
        {
            Root = plan.Root,
            SelectedId = plan.SelectedId,
            PreviousRootStatus = plan.PreviousRootStatus
        };
    }

    public async Task<bool> AbortTimeTickerChainRetryAsync(
        Guid rootId, Guid generation, string reason, DateTime abortedAt,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (RuntimeAdmissionMisconfigured) return false;
        if (IsClusterTopology(Db))
            throw new NotSupportedException(
                "Atomic Redis chain retry abort requires standalone Redis.");

        var raw = await Db.StringGetAsync(TimeTickerKey(rootId)).ConfigureAwait(false);
        if (raw.IsNullOrEmpty) return false;
        var root = Serializer.DeserializeOrNull<TTimeTicker>(raw.ToString());
        if (root == null || !TimeTickerChainRetryPlanner.Abort(
                root, generation, reason, abortedAt))
            return false;

        var result = await Db.ScriptEvaluateAsync(
            UpdateTimeTickerConfigurationScript,
            new RedisKey[]
            {
                TimeTickerKey(rootId), RuntimeActivationMetadataKey, TimeTickerPendingKey,
                TimeTickerIdsKey, TimeTickerRetentionSucceededKey, TimeTickerRetentionFailedKey,
                TimeTickerRetentionCancelledKey, TimeTickerRetentionSkippedKey
            },
            new RedisValue[]
            {
                raw, Serializer.Serialize(root), RuntimeActivationEpoch, RuntimeAdmissionMode,
                rootId.ToString(), string.Empty, 2,
                ToScore(abortedAt).ToString(System.Globalization.CultureInfo.InvariantCulture)
            }).ConfigureAwait(false);
        if ((long)result != 1) return false;
        await IndexManager.AddTimeTickerIndexesAsync(root).ConfigureAwait(false);
        return true;
    }

    public async Task<int> SetTimeTickerDeleteBehaviorAsync(Guid rootId, ChainDeleteBehavior behavior, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(typeof(ChainDeleteBehavior), behavior)) throw new ArgumentOutOfRangeException(nameof(behavior));
        if (RuntimeAdmissionMisconfigured) return 0;
        var cluster = IsClusterTopology(Db);
        if (cluster && HasRuntimeActivationScopeBinding)
            throw new NotSupportedException(
                "Scoped scheduler update is unsupported on Redis Cluster without a shared partition hash slot.");
        // Compare the complete document so a concurrent execution update is never overwritten.
        for (var attempt = 0; attempt < 8; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var raw = await Db.StringGetAsync(TimeTickerKey(rootId)).ConfigureAwait(false);
            if (raw.IsNullOrEmpty) return 0;
            var root = Serializer.DeserializeOrNull<TTimeTicker>(raw.ToString());
            if (root == null || root.ParentId.HasValue) return 0;
            root.DeleteBehavior = behavior;
            root.UpdatedAt = Clock.UtcNow;
            // Keep queue-only Cluster updates on one key, as with UpdateTimeTickers.
            // Scoped standalone updates must check activation and CAS in one atomic operation.
            var result = await Db.ScriptEvaluateAsync(cluster
                    ? "if redis.call('GET', KEYS[1]) ~= ARGV[1] then return 0 end redis.call('SET', KEYS[1], ARGV[2]) return 1"
                    : SetChainDeleteBehaviorScript,
                cluster ? new RedisKey[] { TimeTickerKey(rootId) }
                    : new RedisKey[] { TimeTickerKey(rootId), RuntimeActivationMetadataKey },
                new RedisValue[] { raw, Serializer.Serialize(root), RuntimeActivationEpoch, RuntimeAdmissionMode }).ConfigureAwait(false);
            if ((long)result == 1) return 1;
        }
        return 0;
    }

    public async Task<int> RemoveTimeTickers(Guid[] tickerIds, CancellationToken cancellationToken = default)
    {
        var count = 0;
        foreach (var id in tickerIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var currentJson = await Db.StringGetAsync(TimeTickerKey(id)).ConfigureAwait(false);
            var ticker = currentJson.IsNullOrEmpty
                ? null
                : Serializer.DeserializeOrNull<TTimeTicker>(currentJson.ToString());
            if (ticker == null) continue;
            var aggregateIds = ticker == null ? [id] : EnumerateAggregateIds(ticker).Distinct().ToArray();
            var keys = new List<RedisKey>
            {
                TimeTickerKey(id), TimeTickerIdsKey, TimeTickerPendingKey, TerminalMutationEvidenceKey
            };
            keys.AddRange(aggregateIds.Select(TimeTickerResultKey).Select(x => (RedisKey)x));
            var arguments = new List<RedisValue>
            {
                id.ToString(), currentJson, Clock.UtcNow.ToString("O"), (int)TickerStatus.InProgress
            };
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
        var pending = new Stack<(TTimeTicker Row, Guid? Parent)>();
        var seen = new HashSet<Guid>();
        pending.Push((node, node.ParentId));
        while (pending.Count > 0)
        {
            var (current, parent) = pending.Pop();
            if (!seen.Add(current.Id)) throw new InvalidOperationException("A time ticker graph contains a duplicate or cyclic identity.");
            current.ParentId = parent;
            current.ChainRootId = rootId;
            current.ChainGeneration = generation;
            foreach (var child in current.Children ?? []) pending.Push((child, current.Id));
        }
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

    public Task<PaginationResult<TCronTicker>> GetCronTickersPaginated(Expression<Func<TCronTicker, bool>> predicate, int pageNumber, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = CronTickersQuery();
        if (predicate != null) query = query.Where(predicate);
        return query.OrderByDescending(row => row.CreatedAt)
            .ToPaginatedAsync(pageNumber, pageSize, cancellationToken);
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
            var canDeleteDefinition = true;
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
                    canDeleteDefinition &= await DeleteCronOccurrenceAtomicAsync(occurrence).ConfigureAwait(false);
                else
                    await IndexManager.RemoveCronOccurrenceFromParentIndexAsync(occurrenceId, id)
                        .ConfigureAwait(false);
            }
            if (!canDeleteDefinition) continue;
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

    public Task<PaginationResult<CronTickerOccurrenceEntity<TCronTicker>>> GetAllCronTickerOccurrencesPaginated(Expression<Func<CronTickerOccurrenceEntity<TCronTicker>, bool>> predicate, int pageNumber, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = CronTickerOccurrencesQuery();
        if (predicate != null) query = query.Where(predicate);
        return query.OrderByDescending(row => row.ExecutionTime)
            .ToPaginatedAsync(pageNumber, pageSize, cancellationToken);
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
            if (occurrence != null && BeforeCronOccurrenceDeleteForTestAsync != null)
                await BeforeCronOccurrenceDeleteForTestAsync(occurrence, cancellationToken).ConfigureAwait(false);
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

    public async Task<CronTickerOccurrenceEntity<TCronTicker>> AcquireCronOccurrenceOnDemandAsync(
        Guid occurrenceId, DateTime executionTime, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureReconciliationActivationTopology();

        var candidate = await Serializer.GetAsync<CronTickerOccurrenceEntity<TCronTicker>>(
            CronOccurrenceKey(occurrenceId)).ConfigureAwait(false);
        if (candidate == null || candidate.CronTickerId == Guid.Empty)
            return null;

        var now = Clock.UtcNow;
        var token = Guid.NewGuid();
        var result = await Db.ScriptEvaluateAsync(AcquireCronOccurrenceOnDemandScript,
            [(RedisKey)CronOccurrenceKey(occurrenceId),
             (RedisKey)CronOccurrenceResultKey(occurrenceId),
             (RedisKey)CronKey(candidate.CronTickerId),
             TerminalMutationEvidenceKey,
             (RedisKey)ActivationMetadataKey],
            [(RedisValue)LockHolder,
             (RedisValue)now.ToString("O"),
             (RedisValue)NextLeaseUntil(now).ToString("O"),
             (RedisValue)token.ToString(),
             (RedisValue)(int)TickerStatus.InProgress,
             (RedisValue)executionTime.ToUniversalTime().ToString("O"),
             (RedisValue)(int)TickerStatus.Idle,
             (RedisValue)(int)TickerStatus.Queued,
             (RedisValue)(int)TickerStatus.Done,
             (RedisValue)(int)TickerStatus.DueDone,
             (RedisValue)(int)TickerStatus.Failed,
             (RedisValue)(int)TickerStatus.Cancelled,
             (RedisValue)(int)TickerStatus.Skipped,
             (RedisValue)RuntimeActivationEpoch,
             RuntimeAdmissionMode]).ConfigureAwait(false);
        if (result.IsNull) return null;

        var occurrence = Serializer.DeserializeOrNull<CronTickerOccurrenceEntity<TCronTicker>>(
            (string)result);
        if (occurrence == null) return null;
        occurrence.CronTicker = await Serializer.GetAsync<TCronTicker>(
            CronKey(occurrence.CronTickerId)).ConfigureAwait(false);
        if (occurrence.CronTicker == null) return null;

        await IndexManager.AddCronOccurrenceIndexesAsync(occurrence).ConfigureAwait(false);
        return occurrence;
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
                RetentionReconciliationPendingKey,
                ActivationMetadataKey
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
        if ((long)values[0] == -3)
            throw new InvalidOperationException(
                "Redis retention index reconciliation was rejected because legacy partition adoption has started.");
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
            true, cutoffs, batchSize + 1, cancellationToken, cursor).ConfigureAwait(false);
        var hasMoreCandidates = candidates.Count > batchSize;
        var deleted = 0;
        var nextCursor = RetentionCursor.Start;
        foreach (var candidate in candidates.Take(batchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();
            nextCursor = RetentionCursor.After(candidate.ExecutedAt, candidate.Id);
            var raw = await Db.StringGetAsync(TimeTickerKey(candidate.Id)).ConfigureAwait(false);
            if (raw.IsNullOrEmpty)
            {
                await IndexManager.RemoveTimeTickerIndexesAsync(candidate.Id).ConfigureAwait(false);
                continue;
            }
            var root = Serializer.DeserializeOrNull<TTimeTicker>(raw.ToString());
            if (root == null || root.ParentId.HasValue || root.Id != candidate.Id) continue;
            var nodes = new List<TTimeTicker>();
            var seen = new HashSet<Guid>();
            var pending = new Stack<TTimeTicker>();
            pending.Push(root);
            var safe = true;
            var now = Clock.UtcNow;
            while (pending.Count > 0)
            {
                var node = pending.Pop();
                var cutoff = cutoffs.ForStatus(node.Status);
                if (node.Id == Guid.Empty || !seen.Add(node.Id) || nodes.Count >= cutoffs.MaxNodesPerChain ||
                    !cutoff.HasValue || !node.ExecutedAt.HasValue || node.ExecutedAt.Value >= cutoff.Value ||
                    node.AcquisitionToken.HasValue || node.LeaseUntil > now)
                { safe = false; break; }
                nodes.Add(node);
                if (node.Children == null) continue;
                foreach (var child in node.Children)
                {
                    if (child == null || child.ParentId != node.Id ||
                        nodes.Count + pending.Count >= cutoffs.MaxNodesPerChain) { safe = false; break; }
                    pending.Push(child);
                }
                if (!safe) break;
            }
            if (!safe) continue;
            var keys = new List<RedisKey>
            {
                TimeTickerKey(candidate.Id), TimeTickerIdsKey, TimeTickerPendingKey,
                TimeTickerRetentionSucceededKey, TimeTickerRetentionFailedKey,
                TimeTickerRetentionCancelledKey, TimeTickerRetentionSkippedKey, TerminalMutationEvidenceKey
            };
            keys.AddRange(nodes.Select(node => (RedisKey)TimeTickerResultKey(node.Id)));
            var arguments = new List<RedisValue> { raw, candidate.Id.ToString() };
            arguments.AddRange(nodes.Select(node => (RedisValue)$"{(int)TickerType.TimeTicker}:{node.Id:D}"));
            // Eligibility was checked on the complete bounded graph. Comparing the exact document
            // inside Lua rechecks that snapshot atomically with graph/result/evidence deletion.
            if (BeforeTimeTickerRetentionDeleteForTestAsync != null)
                await BeforeTimeTickerRetentionDeleteForTestAsync(candidate.Id, cancellationToken).ConfigureAwait(false);
            deleted += (int)(long)await Db.ScriptEvaluateAsync(DeleteTimeTickerForRetentionScript,
                keys.ToArray(), arguments.ToArray()).ConfigureAwait(false);
        }
        return new RetentionChainBatchResult(deleted, reconciliation.HasMore || hasMoreCandidates,
            hasMoreCandidates ? nextCursor : RetentionCursor.Start);
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

            var result = await Db.ScriptEvaluateAsync(DeleteOccurrenceForRetentionScript,
                [
                    (RedisKey)CronOccurrenceKey(candidate.Id), CronOccurrenceResultKey(candidate.Id),
                    CronOccurrenceIdsKey, CronOccurrencePendingKey,
                    CronOccurrencesByCronKey(occurrence.CronTickerId),
                    CronOccurrenceRetentionSucceededKey, CronOccurrenceRetentionFailedKey,
                    CronOccurrenceRetentionCancelledKey, CronOccurrenceRetentionSkippedKey,
                    (RedisKey)CronOccurrenceSlotKey(occurrence.CronTickerId, occurrence.ExecutionTime),
                    TerminalMutationEvidenceKey
                ],
                [
                    candidate.Id.ToString(), occurrence.CronTickerId.ToString(),
                    candidate.FirstStatus, candidate.SecondStatus,
                    candidate.Cutoff.ToUniversalTime().ToString("O"),
                    Clock.UtcNow.ToUniversalTime().ToString("O")
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
        bool timeTicker, RetentionCutoffs cutoffs, int take, CancellationToken cancellationToken,
        RetentionCursor cursor = default)
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
            SortedSetEntry[] entries;
            if (cursor.HasValue)
            {
                // A temporary lexicographic sentinel gives O(log N + batch) keyset pagination,
                // including arbitrarily many equal-score roots and a cursor row already deleted.
                const string keyset = """
                    local marker = ARGV[2] .. ':'
                    redis.call('ZADD', KEYS[1], ARGV[1], marker)
                    local rank = redis.call('ZRANK', KEYS[1], marker)
                    local rows = redis.call('ZRANGE', KEYS[1], rank + 1, rank + tonumber(ARGV[3]), 'WITHSCORES')
                    redis.call('ZREM', KEYS[1], marker)
                    return rows
                    """;
                var result = (RedisResult[])await Db.ScriptEvaluateAsync(keyset, new[] { source.Key },
                    new RedisValue[] { ToScore(cursor.ExecutedAt), cursor.Id.ToString(), take }).ConfigureAwait(false);
                entries = Enumerable.Range(0, result.Length / 2)
                    .Select(i => new SortedSetEntry(result[i * 2].ToString(), (double)result[i * 2 + 1]))
                    .Where(entry => entry.Score <= ToScore(source.Cutoff)).ToArray();
            }
            else
                entries = await Db.SortedSetRangeByScoreWithScoresAsync(
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
        => await IndexManager.RemoveCronOccurrenceRetentionIndexesAsync(id).ConfigureAwait(false);

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
        if #KEYS ~= 14 or #ARGV ~= 9 then
            return redis.error_reply('invalid retention reconciliation shape')
        end
        local maxRecords = tonumber(ARGV[1])
        if not maxRecords or maxRecords <= 0 or maxRecords % 1 ~= 0 or
           ARGV[2] == '' or ARGV[3] == '' then
            return redis.error_reply('invalid retention reconciliation arguments')
        end
        local expectedTypes = {'set', 'set', 'string', 'string',
            'zset', 'zset', 'zset', 'zset', 'zset', 'zset', 'zset', 'zset', 'list', 'hash'}
        for index = 1, 14 do
            local actualType = redis.call('TYPE', KEYS[index])['ok']
            if actualType ~= 'none' and actualType ~= expectedTypes[index] then
                return redis.error_reply('retention reconciliation key has an incompatible Redis type')
            end
        end
        if redis.call('HGET', KEYS[14], 'legacyAdoptionState') then
            return {-3, 0, 'fenced'}
        end
        for index = 4, 8 do
            local status = tonumber(ARGV[index])
            if not status or status % 1 ~= 0 then
                return redis.error_reply('invalid retention reconciliation status')
            end
        end
        local phase = redis.call('GET', KEYS[3]) or 'time_ids'
        if phase == 'time' then phase = 'time_ids' end
        if phase == 'occurrence' then phase = 'occurrence_ids' end
        if phase ~= 'time_ids' and phase ~= 'time_keys' and
           phase ~= 'occurrence_ids' and phase ~= 'occurrence_keys' then
            return redis.error_reply('invalid retention reconciliation phase')
        end
        local cursor = redis.call('GET', KEYS[4]) or '0'
        if not string.match(cursor, '^%d+$') then
            return redis.error_reply('invalid retention reconciliation cursor')
        end
        local isTime = string.sub(phase, 1, 4) == 'time'
        local scanKeys = string.sub(phase, -5) == '_keys'
        local idsKey = isTime and KEYS[1] or KEYS[2]
        local prefix = isTime and ARGV[2] or ARGV[3]
        local firstIndex = isTime and 5 or 9
        local members = {}
        local nextCursor = cursor
        local overflow = {}
        local fromPending = false

        local queued = redis.call('LRANGE', KEYS[13], 0, maxRecords - 1)
        for _, member in ipairs(queued) do
            table.insert(members, member)
        end
        fromPending = #members > 0
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
                else table.insert(overflow, id) end
            end
        end

        local function dateTimeParts(value)
            if type(value) ~= 'string' then return nil end
            local year, month, day, hour, minute, second, rest = string.match(value,
                '^(%d%d%d%d)%-(%d%d)%-(%d%d)T(%d%d):(%d%d):(%d%d)(.*)$')
            if not year then return nil end
            year, month, day = tonumber(year), tonumber(month), tonumber(day)
            hour, minute, second = tonumber(hour), tonumber(minute), tonumber(second)
            if year < 1 or year > 9999 or month < 1 or month > 12 or
               hour > 23 or minute > 59 or second > 59 then return nil end
            local leap = year % 400 == 0 or (year % 4 == 0 and year % 100 ~= 0)
            local monthDays = {31, leap and 29 or 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31}
            if day < 1 or day > monthDays[month] then return nil end
            local fraction = ''
            local suffix = rest
            if string.sub(rest, 1, 1) == '.' then
                fraction, suffix = string.match(rest, '^%.(%d+)(.*)$')
                if not fraction or string.len(fraction) > 7 then return nil end
            end
            local offsetMinutes = 0
            if suffix ~= 'Z' then
                local sign, offsetHour, offsetMinute = string.match(suffix, '^([%+%-])(%d%d):(%d%d)$')
                if not sign then return nil end
                offsetHour, offsetMinute = tonumber(offsetHour), tonumber(offsetMinute)
                if offsetHour > 14 or offsetMinute > 59 or (offsetHour == 14 and offsetMinute ~= 0) then
                    return nil
                end
                offsetMinutes = offsetHour * 60 + offsetMinute
                if sign == '-' then offsetMinutes = -offsetMinutes end
            end
            local priorYear = year - 1
            local days = priorYear * 365 + math.floor(priorYear / 4)
                - math.floor(priorYear / 100) + math.floor(priorYear / 400)
            local beforeMonth = {0,31,59,90,120,151,181,212,243,273,304,334}
            days = days + beforeMonth[month] + day - 1
            if month > 2 and leap then days = days + 1 end
            local utcSeconds = days * 86400 + hour * 3600 + minute * 60 + second - offsetMinutes * 60
            if utcSeconds < 0 or utcSeconds > 315537897599 then return nil end
            fraction = string.sub(fraction .. '0000000', 1, 7)
            return utcSeconds, tonumber(fraction), utcSeconds * 10000000 + tonumber(fraction)
        end

        local nowSeconds, nowFraction = dateTimeParts(ARGV[9])
        if not nowSeconds then return redis.error_reply('invalid retention reconciliation timestamp') end
        local function isArray(value)
            if type(value) ~= 'table' then return false end
            local count = 0
            for key, _ in pairs(value) do
                if type(key) ~= 'number' or key < 1 or key % 1 ~= 0 then return false end
                count = count + 1
            end
            for index = 1, count do if value[index] == nil then return false end end
            return true
        end
        local function validNode(obj)
            if type(obj) ~= 'table' or type(obj.Id) ~= 'string' or obj.Id == '' or
               type(obj.Status) ~= 'number' or obj.Status % 1 ~= 0 then return false end
            if obj.ExecutedAt ~= nil and obj.ExecutedAt ~= cjson.null and
               (type(obj.ExecutedAt) ~= 'string' or
                (obj.ExecutedAt ~= '' and not dateTimeParts(obj.ExecutedAt))) then return false end
            if obj.LeaseUntil ~= nil and obj.LeaseUntil ~= cjson.null and
               (type(obj.LeaseUntil) ~= 'string' or
                (obj.LeaseUntil ~= '' and not dateTimeParts(obj.LeaseUntil))) then return false end
            if obj.AcquisitionToken ~= nil and obj.AcquisitionToken ~= cjson.null and
               type(obj.AcquisitionToken) ~= 'string' then return false end
            if obj.ParentId ~= nil and obj.ParentId ~= cjson.null and type(obj.ParentId) ~= 'string' then
                return false
            end
            if obj.Children ~= nil and obj.Children ~= cjson.null then
                if not isArray(obj.Children) then return false end
                for _, child in ipairs(obj.Children) do if not validNode(child) then return false end end
            end
            return true
        end

        local function childrenContainersAreArrays(raw)
            local index, length = 1, string.len(raw)
            while index <= length do
                if string.sub(raw, index, index) == '"' then
                    local start = index
                    index = index + 1
                    while index <= length do
                        local char = string.sub(raw, index, index)
                        if char == '\\' then index = index + 2
                        elseif char == '"' then break
                        else index = index + 1 end
                    end
                    if index > length then return false end
                    local token = string.sub(raw, start, index)
                    local nextIndex = index + 1
                    while string.match(string.sub(raw, nextIndex, nextIndex), '%s') do nextIndex = nextIndex + 1 end
                    if string.sub(raw, nextIndex, nextIndex) == ':' then
                        local ok, key = pcall(cjson.decode, token)
                        if not ok then return false end
                        if key == 'Children' then
                            nextIndex = nextIndex + 1
                            while string.match(string.sub(raw, nextIndex, nextIndex), '%s') do nextIndex = nextIndex + 1 end
                            if string.sub(raw, nextIndex, nextIndex) ~= '[' and
                               string.sub(raw, nextIndex, nextIndex + 3) ~= 'null' then return false end
                        end
                    end
                end
                index = index + 1
            end
            return true
        end

        local plans = {}
        for index, id in ipairs(members) do
            if id == '' then return redis.error_reply('invalid retention reconciliation member') end
            local documentType = redis.call('TYPE', prefix .. id)['ok']
            if documentType ~= 'none' and documentType ~= 'string' then
                return redis.error_reply('retention reconciliation document has an incompatible Redis type')
            end
            local plan = {id = id, addId = false, retentionOffset = nil, retentionScore = nil}
            local raw = redis.call('GET', prefix .. id)
            if raw then
                local ok, obj = pcall(cjson.decode, raw)
                if not ok or (isTime and not childrenContainersAreArrays(raw)) or
                   not validNode(obj) or string.lower(obj.Id) ~= string.lower(id) then
                    return redis.error_reply('retention reconciliation document has an invalid object shape')
                end

                plan.addId = scanKeys
                local leaseSeconds, leaseFraction = nil, nil
                if obj.LeaseUntil ~= nil and obj.LeaseUntil ~= cjson.null and obj.LeaseUntil ~= '' then
                    leaseSeconds, leaseFraction = dateTimeParts(obj.LeaseUntil)
                end
                local executedSeconds, executedFraction, executedTicks = nil, nil, nil
                if obj.ExecutedAt ~= nil and obj.ExecutedAt ~= cjson.null and obj.ExecutedAt ~= '' then
                    executedSeconds, executedFraction, executedTicks = dateTimeParts(obj.ExecutedAt)
                end
                local leaseExpired = leaseSeconds == nil or leaseSeconds < nowSeconds or
                    (leaseSeconds == nowSeconds and leaseFraction <= nowFraction)
                local eligible = executedSeconds ~= nil
                    and (obj.AcquisitionToken == nil or obj.AcquisitionToken == cjson.null or obj.AcquisitionToken == '')
                    and leaseExpired
                if isTime and (obj.ParentId ~= nil and obj.ParentId ~= cjson.null and obj.ParentId ~= '') then
                    eligible = false
                end
                if eligible then
                    local status = obj.Status
                    if status == tonumber(ARGV[4]) or status == tonumber(ARGV[5]) then plan.retentionOffset = 0
                    elseif status == tonumber(ARGV[6]) then plan.retentionOffset = 1
                    elseif status == tonumber(ARGV[7]) then plan.retentionOffset = 2
                    elseif status == tonumber(ARGV[8]) then plan.retentionOffset = 3 end
                    if plan.retentionOffset ~= nil then plan.retentionScore = executedTicks end
                end
            end
            plans[index] = plan
        end
        if fromPending then redis.call('LTRIM', KEYS[13], #members, -1) end
        for _, id in ipairs(overflow) do redis.call('RPUSH', KEYS[13], id) end
        for _, plan in ipairs(plans) do
            local id = plan.id
            for index = firstIndex, firstIndex + 3 do
                redis.call('ZREM', KEYS[index], id)
            end
            if plan.addId then redis.call('SADD', idsKey, id) end
            if plan.retentionOffset ~= nil then
                redis.call('ZADD', KEYS[firstIndex + plan.retentionOffset], plan.retentionScore, id)
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
        -- Bounded aggregate retention CAS: validate every key before deleting any data.
        if #KEYS < 9 or #ARGV ~= #KEYS - 6 or ARGV[1] == '' or ARGV[2] == '' then
            return redis.error_reply('invalid time retention deletion shape')
        end
        local expectedTypes = {'string','set','zset','zset','zset','zset','zset','hash'}
        for index = 1, #KEYS do
            local expected = expectedTypes[index] or 'string'
            local actual = redis.call('TYPE', KEYS[index])['ok']
            if actual ~= 'none' and actual ~= expected then
                return redis.error_reply('time retention deletion key has an incompatible Redis type')
            end
        end
        if redis.call('GET', KEYS[1]) ~= ARGV[1] then return 0 end
        redis.call('DEL', KEYS[1])
        redis.call('SREM', KEYS[2], ARGV[2])
        for index = 3, 7 do redis.call('ZREM', KEYS[index], ARGV[2]) end
        for index = 9, #KEYS do redis.call('DEL', KEYS[index]) end
        for index = 3, #ARGV do redis.call('HDEL', KEYS[8], ARGV[index]) end
        return #KEYS - 8
        """;

    private const string DeleteOccurrenceForRetentionScript = """
        if #KEYS ~= 11 or #ARGV ~= 6 or ARGV[1] == '' or ARGV[2] == '' then
            return redis.error_reply('invalid occurrence retention deletion shape')
        end
        local expectedTypes = {'string','string','set','zset','set','zset','zset','zset','zset','string','hash'}
        for index = 1, #KEYS do
            local actualType = redis.call('TYPE', KEYS[index])['ok']
            if actualType ~= 'none' and actualType ~= expectedTypes[index] then
                return redis.error_reply('occurrence retention deletion key has an incompatible Redis type')
            end
        end
        local firstStatus, secondStatus = tonumber(ARGV[3]), tonumber(ARGV[4])
        if not firstStatus or firstStatus % 1 ~= 0 or not secondStatus or secondStatus % 1 ~= 0 then
            return redis.error_reply('invalid occurrence retention deletion status')
        end
        local function dateTimeParts(value)
            if type(value) ~= 'string' then return nil end
            local year, month, day, hour, minute, second, rest = string.match(value,
                '^(%d%d%d%d)%-(%d%d)%-(%d%d)T(%d%d):(%d%d):(%d%d)(.*)$')
            if not year then return nil end
            year, month, day = tonumber(year), tonumber(month), tonumber(day)
            hour, minute, second = tonumber(hour), tonumber(minute), tonumber(second)
            if year < 1 or year > 9999 or month < 1 or month > 12 or
               hour > 23 or minute > 59 or second > 59 then return nil end
            local leap = year % 400 == 0 or (year % 4 == 0 and year % 100 ~= 0)
            local monthDays = {31, leap and 29 or 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31}
            if day < 1 or day > monthDays[month] then return nil end
            local fraction, suffix = '', rest
            if string.sub(rest, 1, 1) == '.' then
                fraction, suffix = string.match(rest, '^%.(%d+)(.*)$')
                if not fraction or string.len(fraction) > 7 then return nil end
            end
            local offsetMinutes = 0
            if suffix ~= 'Z' then
                local sign, offsetHour, offsetMinute = string.match(suffix, '^([%+%-])(%d%d):(%d%d)$')
                if not sign then return nil end
                offsetHour, offsetMinute = tonumber(offsetHour), tonumber(offsetMinute)
                if offsetHour > 14 or offsetMinute > 59 or (offsetHour == 14 and offsetMinute ~= 0) then return nil end
                offsetMinutes = offsetHour * 60 + offsetMinute
                if sign == '-' then offsetMinutes = -offsetMinutes end
            end
            local priorYear = year - 1
            local days = priorYear * 365 + math.floor(priorYear / 4)
                - math.floor(priorYear / 100) + math.floor(priorYear / 400)
            local beforeMonth = {0,31,59,90,120,151,181,212,243,273,304,334}
            days = days + beforeMonth[month] + day - 1
            if month > 2 and leap then days = days + 1 end
            local utcSeconds = days * 86400 + hour * 3600 + minute * 60 + second - offsetMinutes * 60
            if utcSeconds < 0 or utcSeconds > 315537897599 then return nil end
            return utcSeconds, tonumber(string.sub(fraction .. '0000000', 1, 7))
        end
        local cutoffSeconds, cutoffFraction = dateTimeParts(ARGV[5])
        local nowSeconds, nowFraction = dateTimeParts(ARGV[6])
        if not cutoffSeconds or not nowSeconds then
            return redis.error_reply('invalid occurrence retention deletion timestamp')
        end
        local raw = redis.call('GET', KEYS[1])
        if not raw then return 0 end
        local ok, obj = pcall(cjson.decode, raw)
        if not ok or type(obj) ~= 'table' or type(obj.Id) ~= 'string' or
           string.lower(obj.Id) ~= string.lower(ARGV[1]) or type(obj.CronTickerId) ~= 'string' or
           string.lower(obj.CronTickerId) ~= string.lower(ARGV[2]) or
           type(obj.Status) ~= 'number' or obj.Status % 1 ~= 0 then
            return redis.error_reply('cannot delete invalid occurrence document for retention')
        end
        if obj.ExecutedAt ~= nil and obj.ExecutedAt ~= cjson.null and
           (type(obj.ExecutedAt) ~= 'string' or (obj.ExecutedAt ~= '' and not dateTimeParts(obj.ExecutedAt))) then
            return redis.error_reply('invalid occurrence ExecutedAt shape')
        end
        if obj.LeaseUntil ~= nil and obj.LeaseUntil ~= cjson.null and
           (type(obj.LeaseUntil) ~= 'string' or (obj.LeaseUntil ~= '' and not dateTimeParts(obj.LeaseUntil))) then
            return redis.error_reply('invalid occurrence LeaseUntil shape')
        end
        if obj.AcquisitionToken ~= nil and obj.AcquisitionToken ~= cjson.null and type(obj.AcquisitionToken) ~= 'string' then
            return redis.error_reply('invalid occurrence AcquisitionToken shape')
        end
        if obj.Status ~= firstStatus and obj.Status ~= secondStatus then return 0 end
        local executedSeconds, executedFraction = nil, nil
        if obj.ExecutedAt ~= nil and obj.ExecutedAt ~= cjson.null and obj.ExecutedAt ~= '' then
            executedSeconds, executedFraction = dateTimeParts(obj.ExecutedAt)
        end
        if executedSeconds == nil or executedSeconds > cutoffSeconds or
           (executedSeconds == cutoffSeconds and executedFraction >= cutoffFraction) then return 0 end
        if obj.AcquisitionToken ~= nil and obj.AcquisitionToken ~= cjson.null and obj.AcquisitionToken ~= '' then return 0 end
        local leaseSeconds, leaseFraction = nil, nil
        if obj.LeaseUntil ~= nil and obj.LeaseUntil ~= cjson.null and obj.LeaseUntil ~= '' then
            leaseSeconds, leaseFraction = dateTimeParts(obj.LeaseUntil)
        end
        if leaseSeconds ~= nil and (leaseSeconds > nowSeconds or
           (leaseSeconds == nowSeconds and leaseFraction > nowFraction)) then return 0 end
        redis.call('DEL', KEYS[1], KEYS[2])
        redis.call('SREM', KEYS[3], ARGV[1])
        redis.call('ZREM', KEYS[4], ARGV[1])
        redis.call('SREM', KEYS[5], ARGV[1])
        for index = 6, 9 do redis.call('ZREM', KEYS[index], ARGV[1]) end
        if redis.call('GET', KEYS[10]) == ARGV[1] then redis.call('DEL', KEYS[10]) end
        redis.call('HDEL', KEYS[11], '0:' .. string.lower(ARGV[1]))
        return 1
        """;
    #endregion
}
