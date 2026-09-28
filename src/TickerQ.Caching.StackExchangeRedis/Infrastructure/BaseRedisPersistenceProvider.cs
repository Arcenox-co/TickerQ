#nullable disable
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using TickerQ.Caching.StackExchangeRedis.Helpers;
using static TickerQ.Caching.StackExchangeRedis.DependencyInjection.ServiceExtension;
using static TickerQ.Caching.StackExchangeRedis.Helpers.RedisKeyBuilder;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ.Caching.StackExchangeRedis.Infrastructure;

internal abstract class BaseRedisPersistenceProvider<TTimeTicker, TCronTicker>
    where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
    where TCronTicker : CronTickerEntity, new()
{
    protected readonly IDatabase Db;
    protected readonly ITickerClock Clock;
    protected readonly string LockHolder;
    protected readonly RedisSerializer Serializer;
    protected readonly RedisIndexManager<TTimeTicker, TCronTicker> IndexManager;
    protected readonly RedisKeyBuilder Keys;
    private readonly SchedulerOptionsBuilder _schedulerOptions;
    private readonly string _boundActivationMetadataKey;
    private readonly bool _requiresActivatedRuntimeAdmission;
    private readonly bool _runtimeAdmissionMisconfigured;
    private readonly long _runtimeActivationEpoch;
    protected string RuntimeActivationMetadataKey => _boundActivationMetadataKey;
    protected long RuntimeActivationEpoch => _runtimeActivationEpoch;
    protected bool HasRuntimeActivationScopeBinding => _requiresActivatedRuntimeAdmission;
    protected bool RuntimeAdmissionMisconfigured => _runtimeAdmissionMisconfigured;
    protected bool StartupSeederAdmissionActive => _requiresActivatedRuntimeAdmission &&
        StartupSeederAdmissionContext.Matches(
            _schedulerOptions.RuntimeActivationScope.ScopeKey, _runtimeActivationEpoch);
    protected RedisValue RuntimeAdmissionMode => _runtimeAdmissionMisconfigured
        ? "invalid"
        : _requiresActivatedRuntimeAdmission
            ? StartupSeederAdmissionActive
                ? "startup-seeder"
                : "scoped"
            : "legacy";
    protected string ActivationMetadataKey => RuntimeActivationMetadataKey;
    internal string RuntimeActivationMetadataKeyForTest => RuntimeActivationMetadataKey;
    internal RedisKeyBuilder RuntimeKeysForTest => Keys;

    protected string Prefix => Keys.PartitionPrefix;
    protected string TimeTickerIdsKey => Keys.TimeTickerIds;
    protected string TimeTickerPendingKey => Keys.TimeTickerPending;
    protected string CronIdsKey => Keys.CronIds;
    protected string CronRepairPhaseKey => Keys.CronRepairPhase;
    protected string CronRepairCursorKey => Keys.CronRepairCursor;
    protected string CronRepairPendingKey => Keys.CronRepairPending;
    protected string CronRepairQuarantineKey => Keys.CronRepairQuarantine;
    protected string CronOccurrenceRepairQuarantineKey => Keys.CronOccurrenceRepairQuarantine;
    protected string CronOccurrenceIdsKey => Keys.CronOccurrenceIds;
    protected string CronOccurrencePendingKey => Keys.CronOccurrencePending;
    protected string TimeTickerRetentionSucceededKey => Keys.TimeTickerRetentionSucceeded;
    protected string TimeTickerRetentionFailedKey => Keys.TimeTickerRetentionFailed;
    protected string TimeTickerRetentionCancelledKey => Keys.TimeTickerRetentionCancelled;
    protected string TimeTickerRetentionSkippedKey => Keys.TimeTickerRetentionSkipped;
    protected string CronOccurrenceRetentionSucceededKey => Keys.CronOccurrenceRetentionSucceeded;
    protected string CronOccurrenceRetentionFailedKey => Keys.CronOccurrenceRetentionFailed;
    protected string CronOccurrenceRetentionCancelledKey => Keys.CronOccurrenceRetentionCancelled;
    protected string CronOccurrenceRetentionSkippedKey => Keys.CronOccurrenceRetentionSkipped;
    protected string RetentionReconciliationPhaseKey => Keys.RetentionReconciliationPhase;
    protected string RetentionReconciliationCursorKey => Keys.RetentionReconciliationCursor;
    protected string RetentionReconciliationPendingKey => Keys.RetentionReconciliationPending;
    protected string NodeFinalizationRecordsKey => Keys.NodeFinalizationRecords;
    protected string NodeFinalizationDueKey => Keys.NodeFinalizationDue;
    protected string TerminalMutationEvidenceKey => Keys.TerminalMutationEvidence;
    protected string TimeTickerKey(Guid id) => Keys.TimeTicker(id);
    protected string TimeTickerResultKey(Guid id) => Keys.TimeTickerResult(id);
    protected string CronKey(Guid id) => Keys.Cron(id);
    protected string CronOccurrenceKey(Guid id) => Keys.CronOccurrence(id);
    protected string CronOccurrenceResultKey(Guid id) => Keys.CronOccurrenceResult(id);
    protected string CronOccurrencesByCronKey(Guid id) => Keys.CronOccurrencesByCron(id);
    protected string CronOccurrenceSlotKey(Guid id, DateTime time) => Keys.CronOccurrenceSlot(id, time);

    // Lua status constants matching TickerStatus enum values
    private static readonly RedisValue LuaStatusIdle = (int)TickerStatus.Idle;
    private static readonly RedisValue LuaStatusQueued = (int)TickerStatus.Queued;
    private static readonly RedisValue LuaStatusInProgress = (int)TickerStatus.InProgress;
    private static readonly RedisValue LuaStatusCancelled = (int)TickerStatus.Cancelled;
    private static readonly RedisValue LuaStatusSkipped = (int)TickerStatus.Skipped;

    // Raw script strings with KEYS[]/ARGV[] notation (AOT-safe)
    private static readonly string AcquireScript = LuaScriptLoader.Load("Acquire");
    private static readonly string TransitionQueuedScript = LuaScriptLoader.Load("TransitionQueued");
    private static readonly string ReleaseScript = LuaScriptLoader.Load("Release");
    private static readonly string RecoverDeadNodeScript = LuaScriptLoader.Load("RecoverDeadNode");
    private static readonly string CasReplaceScript = LuaScriptLoader.Load("CasReplace");
    private static readonly string RenewLeaseScript = LuaScriptLoader.Load("RenewLease");
    private static readonly string CheckLeaseScript = LuaScriptLoader.Load("CheckLease");
    private static readonly string RecoverStaleScript = LuaScriptLoader.Load("RecoverStale");
    private static readonly string AcquireOnDemandScript = LuaScriptLoader.Load("AcquireOnDemand");
    private static readonly string ClaimNodeFinalizationsScript = LuaScriptLoader.Load("ClaimNodeFinalizations");
    private static readonly string DiscardClaimedNodeFinalizationScript = LuaScriptLoader.Load("DiscardClaimedNodeFinalization");
    private static readonly string CompleteNodeFinalizationScript = LuaScriptLoader.Load("CompleteNodeFinalization");
    private static readonly string RescheduleNodeFinalizationScript = LuaScriptLoader.Load("RescheduleNodeFinalization");
    private static readonly string MutateCronDefinitionScript = LuaScriptLoader.Load("MutateCronDefinition");
    private static readonly string UpdateTimeTickerWithIndexesScript =
        LuaScriptLoader.Load("UpdateTimeTickerWithIndexes");
    private static readonly string RepairCronDiscoverabilityScript = LuaScriptLoader.Load("RepairCronDiscoverability");
    private static readonly string DeletePendingCronOccurrenceScript = LuaScriptLoader.Load("DeletePendingCronOccurrence");
    private static readonly string AddCronOccurrenceOnceScript = LuaScriptLoader.Load("AddCronOccurrenceOnce");
    private static readonly string ReleaseCronOccurrenceSlotScript = LuaScriptLoader.Load("ReleaseCronOccurrenceSlot");
    private static readonly string DeleteCronOccurrenceScript = LuaScriptLoader.Load("DeleteCronOccurrence");
    private static readonly string MutateReconciliationActivationScript =
        LuaScriptLoader.Load("MutateReconciliationActivation");
    internal Func<Task> AfterNodeFinalizationClaimScriptEvaluatedAsync { get; set; }
    internal Func<Task> AfterCronDefinitionMutationScriptEvaluatedAsync { get; set; }
    internal Func<CancellationToken, Task> AfterLegacyAdoptionFenceForTestAsync { get; set; }
    internal Func<Task> AfterActivationMutationScriptEvaluatedAsync { get; set; }
    internal Func<Task> AfterTerminalMutationScriptEvaluatedAsync { get; set; }
    internal Func<Task> AfterUnifiedContextDocumentsLoadedAsync { get; set; }
    internal Func<Task> AfterCronTerminalCleanupAsync { get; set; }
    internal Func<Task> AfterNodeFinalizationMutationScriptEvaluatedAsync { get; set; }

    protected BaseRedisPersistenceProvider(
        IDatabase db,
        ITickerClock clock,
        SchedulerOptionsBuilder optionsBuilder,
        TickerQRedisOptionBuilder redisOptions,
        ILogger logger)
    {
        Db = db ?? throw new ArgumentNullException(nameof(db));
        Clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _schedulerOptions = optionsBuilder ?? new SchedulerOptionsBuilder();
        Keys = new RedisKeyBuilder(_schedulerOptions.RuntimePartition ?? TickerQRuntimePartition.LegacyGlobal);
        LockHolder = _schedulerOptions.ExecutionOwnerId;
        _requiresActivatedRuntimeAdmission = _schedulerOptions.RuntimeSchedulerEnabled &&
                                             _schedulerOptions.RuntimeActivationScope != null;
        _runtimeAdmissionMisconfigured = _schedulerOptions.RuntimeSchedulerEnabled &&
                                        _schedulerOptions.RuntimeActivationScope == null;
        _runtimeActivationEpoch = _requiresActivatedRuntimeAdmission
            ? _schedulerOptions.RuntimeActivationEpoch
            : _schedulerOptions.ReconciliationEpoch;
        if (_requiresActivatedRuntimeAdmission)
        {
            _boundActivationMetadataKey = Keys.ReconciliationActivationMetadataForScope(
                _schedulerOptions.RuntimeActivationScope.ScopeKey);
        }
        else
        {
            _boundActivationMetadataKey = Keys.ReconciliationActivationMetadata;
        }

        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = false,
            // Canonical, fixed-width ("O") timestamps so the Lua CAS scripts' exact-string
            // UpdatedAt compare and RecoverStale's lexicographic LockedAt/LeaseUntil ordering
            // stay correct. Without this, System.Text.Json trims trailing fractional zeros and
            // consecutive fenced writes silently fail. See CanonicalDateTimeConverter.
            Converters =
            {
                new CanonicalDateTimeConverter(),
                new CanonicalNullableDateTimeConverter()
            },
            TypeInfoResolverChain = { RedisContextJsonSerializerContext.Default }
        };

        if (redisOptions?.JsonSerializerContext != null)
            jsonOptions.TypeInfoResolverChain.Insert(0, redisOptions.JsonSerializerContext);

        Serializer = new RedisSerializer(db, jsonOptions, logger ?? throw new ArgumentNullException(nameof(logger)));
        IndexManager = new RedisIndexManager<TTimeTicker, TCronTicker>(
            db, LockHolder, Clock, Keys, ActivationMetadataKey);
    }

    public bool SupportsLeaseBasedRecovery => true;
    public bool SupportsReconciliationActivationEpoch => true;
    public bool SupportsAuthoritativeCronReconciliation => true;

    public Task<ActivationEpochState> GetReconciliationActivationStateAsync(
        ReconciliationActivationScope scope, CancellationToken cancellationToken = default)
    {
        return GetReconciliationActivationStateForKeyAsync(ActivationKeyFor(scope), cancellationToken);
    }

    public Task<ActivationEpochState> BeginReconciliationActivationEpochAsync(
        ReconciliationActivationScope scope, long targetEpoch, CancellationToken cancellationToken = default)
    {
        var key = ActivationKeyFor(scope);
        return MutateActivationStateAsync(key, targetEpoch, null, "begin", cancellationToken);
    }

    public Task<ActivationEpochState> AdvanceReconciliationCheckpointAsync(
        ReconciliationActivationScope scope, long targetEpoch, string checkpoint,
        CancellationToken cancellationToken = default)
    {
        ValidateCheckpoint(targetEpoch, checkpoint);
        return MutateActivationStateAsync(ActivationKeyFor(scope), targetEpoch, checkpoint, "checkpoint", cancellationToken);
    }

    public Task<ActivationEpochState> CommitReconciliationActivationEpochAsync(
        ReconciliationActivationScope scope, long targetEpoch, CancellationToken cancellationToken = default)
    {
        ValidateTargetEpoch(targetEpoch);
        return MutateActivationStateAsync(ActivationKeyFor(scope), targetEpoch, null, "commit", cancellationToken);
    }

    private string ActivationKeyFor(ReconciliationActivationScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        return Keys.ReconciliationActivationMetadataForScope(scope.ScopeKey);
    }


    public async Task<ActivationEpochState> GetReconciliationActivationStateAsync(
        CancellationToken cancellationToken = default)
        => await GetReconciliationActivationStateForKeyAsync(ActivationMetadataKey, cancellationToken)
            .ConfigureAwait(false);

    private async Task<ActivationEpochState> GetReconciliationActivationStateForKeyAsync(
        string activationMetadataKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureReconciliationActivationTopology();
        HashEntry[] entries;
        try
        {
            entries = await Db.HashGetAllAsync(activationMetadataKey).ConfigureAwait(false);
        }
        catch (RedisServerException ex) when (ex.Message.Contains("WRONGTYPE", StringComparison.OrdinalIgnoreCase))
        {
            throw;
        }
        cancellationToken.ThrowIfCancellationRequested();
        return ParseActivationState(entries);
    }

    public Task<ActivationEpochState> BeginReconciliationActivationEpochAsync(
        long targetEpoch, CancellationToken cancellationToken = default)
    {
        ValidateTargetEpoch(targetEpoch);
        return MutateActivationStateAsync(ActivationMetadataKey, targetEpoch, null, "begin", cancellationToken);
    }

    public Task<ActivationEpochState> AdvanceReconciliationCheckpointAsync(
        long targetEpoch, string checkpoint, CancellationToken cancellationToken = default)
    {
        ValidateCheckpoint(targetEpoch, checkpoint);
        return MutateActivationStateAsync(ActivationMetadataKey, targetEpoch, checkpoint, "checkpoint", cancellationToken);
    }

    public Task<ActivationEpochState> CommitReconciliationActivationEpochAsync(
        long targetEpoch, CancellationToken cancellationToken = default)
    {
        ValidateTargetEpoch(targetEpoch);
        return MutateActivationStateAsync(ActivationMetadataKey, targetEpoch, null, "commit", cancellationToken);
    }

    private static void ValidateTargetEpoch(long targetEpoch)
    {
        if (targetEpoch <= 0) throw new ArgumentOutOfRangeException(nameof(targetEpoch));
    }

    private static void ValidateCheckpoint(long targetEpoch, string checkpoint)
    {
        ValidateTargetEpoch(targetEpoch);
        if (string.IsNullOrWhiteSpace(checkpoint) || checkpoint.Length > 200)
            throw new ArgumentException(
                "Activation checkpoint must be non-empty and at most 200 characters.", nameof(checkpoint));
    }

    private async Task<ActivationEpochState> MutateActivationStateAsync(
        string activationMetadataKey, long targetEpoch, string checkpoint, string operation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureReconciliationActivationTopology();
        RedisResult result;
        try
        {
            result = await Db.ScriptEvaluateAsync(MutateReconciliationActivationScript,
                [(RedisKey)activationMetadataKey],
                [(RedisValue)operation, (RedisValue)targetEpoch,
                 (RedisValue)(checkpoint ?? string.Empty)]).ConfigureAwait(false);
        }
        catch (RedisServerException ex) when (ex.Message.Contains(
                   "activation metadata is corrupt", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Redis reconciliation activation metadata is corrupt; no activation state was mutated.", ex);
        }

        // The Lua command has completed atomically. Never re-check the caller token after this boundary:
        // reporting cancellation now would falsely claim that a durable state transition did not happen.
        if (AfterActivationMutationScriptEvaluatedAsync != null)
            await AfterActivationMutationScriptEvaluatedAsync().ConfigureAwait(false);
        return ParseActivationResult((RedisResult[])result);
    }

    private static ActivationEpochState ParseActivationResult(RedisResult[] values)
    {
        if (values.Length != 3 ||
            !long.TryParse(values[0].ToString(), out var epoch) ||
            !int.TryParse(values[1].ToString(), out var phaseValue) ||
            !Enum.IsDefined(typeof(ActivationEpochPhase), phaseValue))
            throw new InvalidDataException("Redis reconciliation activation script returned an invalid state.");
        var checkpoint = values[2].ToString();
        return new ActivationEpochState
        {
            Epoch = epoch,
            Phase = (ActivationEpochPhase)phaseValue,
            Checkpoint = checkpoint.Length == 0 ? null : checkpoint
        };
    }

    private static ActivationEpochState ParseActivationState(HashEntry[] entries)
    {
        if (entries.Length == 0) return ActivationEpochState.PreEpoch;
        if (entries.Length != 3)
            throw new InvalidDataException(
                "Redis reconciliation activation metadata is corrupt; expected exactly epoch, phase, and checkpoint fields.");
        var fields = entries.ToDictionary(x => x.Name.ToString(), x => x.Value.ToString(), StringComparer.Ordinal);
        if (!fields.TryGetValue("epoch", out var epochText) ||
            !long.TryParse(epochText, out var epoch) || epoch < 0 ||
            !fields.TryGetValue("phase", out var phaseText) ||
            !int.TryParse(phaseText, out var phaseValue) ||
            !Enum.IsDefined(typeof(ActivationEpochPhase), phaseValue) ||
            !fields.TryGetValue("checkpoint", out var checkpoint))
            throw new InvalidDataException("Redis reconciliation activation metadata is corrupt.");
        return new ActivationEpochState
        {
            Epoch = epoch,
            Phase = (ActivationEpochPhase)phaseValue,
            Checkpoint = checkpoint.Length == 0 ? null : checkpoint
        };
    }

    protected async Task<bool> IsActivationPublicationVisibleAsync(CancellationToken cancellationToken)
    {
        if (RuntimeAdmissionMisconfigured) return false;
        if (!HasRuntimeActivationScopeBinding) return true;
        var state = await GetReconciliationActivationStateAsync(cancellationToken).ConfigureAwait(false);
        return state.IsActivatedFor(RuntimeActivationEpoch);
    }

    /// <summary>
    /// Durable finalization is intentionally fail-closed on Redis Cluster. Existing ticker,
    /// result, and outbox keys have no common hash tag, so a multi-key script would CROSSSLOT.
    /// Standalone Redis (including ordinary primary/replica deployments) supports the atomic script.
    /// </summary>
    public bool SupportsDurableNodeFinalizationOutbox => IsStandaloneTopology(Db);
    public bool SupportsLegacyRuntimePartitionAdoption => true;

    public async Task AdoptLegacyRuntimePartitionAsync(
        LegacyRuntimePartitionAdoption adoption, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(adoption);
        if (!StringComparer.Ordinal.Equals(adoption.TargetPartition.StorageKey, Keys.Partition.StorageKey))
            throw new InvalidOperationException("Legacy adoption target does not match this provider's runtime partition.");
        if (!IsStandaloneTopology(Db))
            throw new NotSupportedException(
                "Redis legacy runtime partition adoption requires a positively identified standalone topology. " +
                "Redis Cluster cannot atomically lease and move keys between the legacy and target hash slots.");

        const string leaseKey = "tq:legacy-runtime-adoption:v1";
        var authority = $"{Keys.Partition.StorageKey}|{adoption.Epoch}";
        var adopting = authority + "|adopting";
        var completed = authority + "|completed";
        if (!await Db.StringSetAsync(leaseKey, adopting, when: When.NotExists).ConfigureAwait(false))
        {
            var current = (string)await Db.StringGetAsync(leaseKey).ConfigureAwait(false);
            if (current == completed)
            {
                await SetLegacyAdoptionFenceAsync(
                    new RedisKeyBuilder(TickerQRuntimePartition.LegacyGlobal).ReconciliationActivationMetadata,
                    authority, "completed").ConfigureAwait(false);
                return;
            }
            if (current != adopting)
                throw new InvalidOperationException(
                    "Legacy runtime adoption is already held or completed by a different owner or epoch.");
        }

        var legacy = new RedisKeyBuilder(TickerQRuntimePartition.LegacyGlobal);
        var legacyFenceKey = legacy.ReconciliationActivationMetadata;
        await SetLegacyAdoptionFenceAsync(legacyFenceKey, authority, "adopting").ConfigureAwait(false);
        if (AfterLegacyAdoptionFenceForTestAsync != null)
            await AfterLegacyAdoptionFenceForTestAsync(cancellationToken).ConfigureAwait(false);
        var targetPrefix = Keys.PartitionPrefix;
        var legacyPrefix = legacy.PartitionPrefix;
        var multiplexer = Db.Multiplexer;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var endpoint in multiplexer.GetEndPoints())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var server = multiplexer.GetServer(endpoint);
            if (!server.IsConnected || server.IsReplica) continue;
            await foreach (var key in server.KeysAsync(Db.Database, pattern: "tq:{tq:runtime:v1:*}*")
                               .WithCancellation(cancellationToken))
            {
                var value = key.ToString();
                if (!seen.Add(value) || value.StartsWith(legacyPrefix + ":", StringComparison.Ordinal) ||
                    value.StartsWith(targetPrefix + ":", StringComparison.Ordinal))
                    continue;
                throw new InvalidOperationException(
                    "Legacy runtime adoption is ambiguous because runtime keys exist for another namespace.");
            }
        }

        seen.Clear();
        foreach (var endpoint in multiplexer.GetEndPoints())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var server = multiplexer.GetServer(endpoint);
            if (!server.IsConnected || server.IsReplica) continue;
            await foreach (var key in server.KeysAsync(Db.Database, pattern: legacyPrefix + ":*")
                               .WithCancellation(cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var source = key.ToString();
                if (!seen.Add(source) || source == legacyFenceKey.ToString()) continue;
                var destination = targetPrefix + source[legacyPrefix.Length..];
                if (!await Db.KeyRenameAsync(key, destination, When.NotExists).ConfigureAwait(false))
                {
                    if (await Db.KeyExistsAsync(key).ConfigureAwait(false))
                        throw new InvalidOperationException(
                            $"Legacy Redis key adoption found a conflicting target key '{destination}'.");
                }
            }
        }

        await RebuildAdoptedDerivedIndexesAsync(cancellationToken).ConfigureAwait(false);
        await SetLegacyAdoptionFenceAsync(legacyFenceKey, authority, "completed").ConfigureAwait(false);
        const string completeScript = "local v=redis.call('GET',KEYS[1]); if v==ARGV[1] then redis.call('SET',KEYS[1],ARGV[2]); return 1 end; if v==ARGV[2] then return 1 end; return 0";
        var acknowledged = (long)await Db.ScriptEvaluateAsync(
            completeScript, [leaseKey], [adopting, completed]).ConfigureAwait(false);
        if (acknowledged != 1)
            throw new InvalidOperationException("Legacy Redis adoption lease changed before completion could be recorded.");
    }

    private async Task SetLegacyAdoptionFenceAsync(RedisKey key, string authority, string state)
    {
        const string script = "local t=redis.call('TYPE',KEYS[1])['ok']; if t~='none' and t~='hash' then return redis.error_reply('legacy adoption fence key has an incompatible Redis type') end; local a=redis.call('HGET',KEYS[1],'legacyAdoptionAuthority'); if a and a~=ARGV[1] then return 0 end; redis.call('HSET',KEYS[1],'legacyAdoptionAuthority',ARGV[1],'legacyAdoptionState',ARGV[2]); return 1";
        if ((long)await Db.ScriptEvaluateAsync(script, [key], [authority, state]).ConfigureAwait(false) != 1)
            throw new InvalidOperationException(
                "Legacy Redis adoption fence is already held or completed by a different owner or epoch.");
    }

    private async Task RebuildAdoptedDerivedIndexesAsync(CancellationToken cancellationToken)
    {
        // Adoption runs before the target partition is activated. Rebuilding from primary documents
        // closes the fence/rename race without moving or deleting results, slots, evidence, or outbox data.
        await DeleteAdoptedReverseOccurrenceIndexesAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        await Db.KeyDeleteAsync(
        [
            TimeTickerIdsKey, TimeTickerPendingKey, CronIdsKey, CronOccurrenceIdsKey,
            CronOccurrencePendingKey,
            TimeTickerRetentionSucceededKey, TimeTickerRetentionFailedKey,
            TimeTickerRetentionCancelledKey, TimeTickerRetentionSkippedKey,
            CronOccurrenceRetentionSucceededKey, CronOccurrenceRetentionFailedKey,
            CronOccurrenceRetentionCancelledKey, CronOccurrenceRetentionSkippedKey
        ]).ConfigureAwait(false);

        var timePrefix = Keys.TimeTicker(Guid.Empty)[..^36];
        await foreach (var key in ScanAdoptedPrimaryDocumentKeysAsync(timePrefix, cancellationToken))
        {
            var id = ParseAdoptedDocumentId(key, timePrefix);
            var ticker = await Serializer.GetAsync<TTimeTicker>(key.ToString()).ConfigureAwait(false);
            if (ticker == null || ticker.Id != id)
                throw new InvalidDataException($"Adopted Redis time ticker '{key}' is corrupt or has a mismatched ID.");
            await IndexManager.AddTimeTickerIndexesAsync(ticker).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }

        var cronPrefix = Keys.Cron(Guid.Empty)[..^36];
        await foreach (var key in ScanAdoptedPrimaryDocumentKeysAsync(cronPrefix, cancellationToken))
        {
            var id = ParseAdoptedDocumentId(key, cronPrefix);
            var ticker = await Serializer.GetAsync<TCronTicker>(key.ToString()).ConfigureAwait(false);
            if (ticker == null || ticker.Id != id)
                throw new InvalidDataException($"Adopted Redis Cron definition '{key}' is corrupt or has a mismatched ID.");
            await IndexManager.AddCronIndexesAsync(ticker).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }

        var occurrencePrefix = Keys.CronOccurrence(Guid.Empty)[..^36];
        await foreach (var key in ScanAdoptedPrimaryDocumentKeysAsync(occurrencePrefix, cancellationToken))
        {
            var id = ParseAdoptedDocumentId(key, occurrencePrefix);
            var occurrence = await Serializer.GetAsync<CronTickerOccurrenceEntity<TCronTicker>>(key.ToString())
                .ConfigureAwait(false);
            if (occurrence == null || occurrence.Id != id || occurrence.CronTickerId == Guid.Empty)
                throw new InvalidDataException($"Adopted Redis Cron occurrence '{key}' is corrupt or has a mismatched ID.");
            await IndexManager.AddCronOccurrenceIndexesAsync(occurrence).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    private async Task DeleteAdoptedReverseOccurrenceIndexesAsync(CancellationToken cancellationToken)
    {
        var prefix = Keys.Cron(Guid.Empty)[..^36];
        const string suffix = ":occurrences";
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var endpoint in Db.Multiplexer.GetEndPoints())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var server = Db.Multiplexer.GetServer(endpoint);
            if (!server.IsConnected || server.IsReplica) continue;
            await foreach (var key in server.KeysAsync(
                               Db.Database, pattern: prefix + "????????-????-????-????-????????????" + suffix,
                               pageSize: 256).WithCancellation(cancellationToken))
            {
                var value = key.ToString();
                if (!seen.Add(value)) continue;
                var idPart = value[prefix.Length..^suffix.Length];
                if (Guid.TryParseExact(idPart, "D", out _))
                    await Db.KeyDeleteAsync(key).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
    }

    private async IAsyncEnumerable<RedisKey> ScanAdoptedPrimaryDocumentKeysAsync(
        string prefix, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var endpoint in Db.Multiplexer.GetEndPoints())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var server = Db.Multiplexer.GetServer(endpoint);
            if (!server.IsConnected || server.IsReplica) continue;
            await foreach (var key in server.KeysAsync(
                               Db.Database, pattern: prefix + "????????-????-????-????-????????????",
                               pageSize: 256).WithCancellation(cancellationToken))
            {
                var value = key.ToString();
                if (seen.Add(value) && value.Length == prefix.Length + 36 &&
                    Guid.TryParseExact(value[prefix.Length..], "D", out _))
                    yield return key;
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
    }

    private static Guid ParseAdoptedDocumentId(RedisKey key, string prefix)
        => Guid.ParseExact(key.ToString()[prefix.Length..], "D");

    private static bool IsStandaloneTopology(IDatabase db)
    {
        try
        {
            var multiplexer = db.Multiplexer;
            if (multiplexer == null) return false;
            var endpoints = multiplexer.GetEndPoints();
            if (endpoints.Length == 0) return false;
            var observed = false;
            foreach (var endpoint in endpoints)
            {
                var server = multiplexer.GetServer(endpoint);
                if (!server.IsConnected || server.ServerType != ServerType.Standalone) return false;
                observed = true;
            }
            return observed;
        }
        catch
        {
            return false;
        }
    }

    protected static bool IsClusterTopology(IDatabase db)
    {
        try
        {
            var multiplexer = db.Multiplexer;
            if (multiplexer == null) return false;
            foreach (var endpoint in multiplexer.GetEndPoints())
            {
                var server = multiplexer.GetServer(endpoint);
                if (server.IsConnected && server.ServerType == ServerType.Cluster)
                    return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    protected void EnsureAtomicMigrationTopology()
    {
        if (!IsStandaloneTopology(Db))
            throw new NotSupportedException(
                "TickerQ Redis migration/readiness requires a positively identified connected standalone Redis topology. Redis Cluster, unknown, disconnected, proxy, sentinel-ambiguous, and mixed topologies fail closed because the required multi-key Lua scripts cannot be proven atomic.");
    }

    protected void EnsureReconciliationActivationTopology()
    {
        if (!IsStandaloneTopology(Db))
            throw new NotSupportedException(
                "Durable Redis reconciliation activation requires a positively identified connected standalone Redis topology. Activation was not claimed because activation metadata and ticker keys do not share a Redis Cluster hash slot, so atomic ordering cannot be proven.");
    }

    #region Lua script operations
    protected async Task<T> TryAcquireAsync<T>(string key, string resultKey, TickerStatus targetStatus,
        string expectedUpdatedAt = "", string authoritativeCronKey = null) where T : class
    {
        EnsureReconciliationActivationTopology();
        var now = Clock.UtcNow;
        var acquisitionToken = Guid.NewGuid();
        var keys = authoritativeCronKey == null
            ? [(RedisKey)key, (RedisKey)resultKey, TerminalMutationEvidenceKey,
                (RedisKey)ActivationMetadataKey]
            : new RedisKey[] { key, resultKey, authoritativeCronKey,
                TerminalMutationEvidenceKey, ActivationMetadataKey };
        var result = await Db.ScriptEvaluateAsync(
            AcquireScript,
            keys,
            [(RedisValue)LockHolder, (RedisValue)now.ToString("O"), (RedisValue)(int)targetStatus,
             (RedisValue)(expectedUpdatedAt ?? ""), LuaStatusIdle, LuaStatusQueued,
             (RedisValue)acquisitionToken.ToString(),
             (RedisValue)(targetStatus == TickerStatus.InProgress
                 ? now.Add(_schedulerOptions.LeaseDuration).ToString("O")
                 : ""),
             (RedisValue)(key.StartsWith($"{Prefix}:tt:", StringComparison.Ordinal) ? $"{Prefix}:tt:" : ""),
             (RedisValue)RuntimeActivationEpoch, RuntimeAdmissionMode]
        ).ConfigureAwait(false);

        if (result.IsNull) return null;
        return Serializer.DeserializeOrNull<T>((string)result);
    }

    protected async Task<T> TryTransitionQueuedAsync<T>(string key, Guid acquisitionToken,
        string authoritativeCronKey = null) where T : class
    {
        EnsureReconciliationActivationTopology();
        var now = Clock.UtcNow;
        var keys = authoritativeCronKey == null
            ? [(RedisKey)key, (RedisKey)ActivationMetadataKey]
            : new RedisKey[] { key, authoritativeCronKey, ActivationMetadataKey };
        var result = await Db.ScriptEvaluateAsync(
            TransitionQueuedScript,
            keys,
            [(RedisValue)LockHolder, (RedisValue)acquisitionToken.ToString(),
             (RedisValue)now.ToString("O"), LuaStatusQueued, LuaStatusInProgress,
             (RedisValue)now.Add(_schedulerOptions.LeaseDuration).ToString("O"),
             (RedisValue)RuntimeActivationEpoch, RuntimeAdmissionMode]
        ).ConfigureAwait(false);

        if (result.IsNull) return null;
        return Serializer.DeserializeOrNull<T>((string)result);
    }

    protected async Task<T> TryReleaseAsync<T>(string key, string resultKey) where T : class
    {
        EnsureReconciliationActivationTopology();
        var now = Clock.UtcNow;
        var result = await Db.ScriptEvaluateAsync(
            ReleaseScript,
            [(RedisKey)key, (RedisKey)resultKey, (RedisKey)ActivationMetadataKey],
            [(RedisValue)LockHolder, (RedisValue)now.ToString("O"), LuaStatusIdle, LuaStatusQueued,
             (RedisValue)RuntimeActivationEpoch, RuntimeAdmissionMode]
        ).ConfigureAwait(false);

        if (result.IsNull) return null;
        return Serializer.DeserializeOrNull<T>((string)result);
    }

    protected async Task<T> TryRecoverDeadNodeAsync<T>(string key, string resultKey, string deadNodeId,
        string authoritativeCronKey = null) where T : class
    {
        EnsureReconciliationActivationTopology();
        var now = Clock.UtcNow;
        var keys = authoritativeCronKey == null
            ? [(RedisKey)key, (RedisKey)resultKey, (RedisKey)ActivationMetadataKey]
            : new RedisKey[] { key, resultKey, authoritativeCronKey, ActivationMetadataKey };
        var values = authoritativeCronKey == null
            ? [(RedisValue)deadNodeId, (RedisValue)now.ToString("O"), LuaStatusIdle, LuaStatusQueued,
                LuaStatusInProgress, (RedisValue)RuntimeActivationEpoch, RuntimeAdmissionMode]
            : new RedisValue[] { deadNodeId, now.ToString("O"), LuaStatusIdle, LuaStatusQueued,
                LuaStatusInProgress, LuaStatusSkipped,
                "Quarantined because its Cron definition revision is stale.",
                RuntimeActivationEpoch, RuntimeAdmissionMode };
        var result = await Db.ScriptEvaluateAsync(
            RecoverDeadNodeScript,
            keys, values
        ).ConfigureAwait(false);

        if (result.IsNull) return null;
        return Serializer.DeserializeOrNull<T>((string)result);
    }

    private async Task<T> TryCasReplaceAsync<T>(string key, string resultKey, T replacement, DateTime expectedUpdatedAt,
        string expectedHolder = "", Guid? expectedToken = null, int expectedStatus = -1,
        string resultAction = "none", byte[] resultEnvelope = null, Guid? embeddedTargetId = null,
        Guid? expectedChainGeneration = null, string authoritativeCronKey = null) where T : class
    {
        var result = await EvaluateCasReplaceAsync(key, resultKey, replacement, expectedUpdatedAt,
            expectedHolder, expectedToken, expectedStatus, resultAction, resultEnvelope, embeddedTargetId,
            expectedChainGeneration, terminalMutation: false, authoritativeCronKey: authoritativeCronKey)
            .ConfigureAwait(false);
        return result.IsNull ? null : Serializer.DeserializeOrNull<T>((string)result);
    }

    private async Task<bool> TryAcknowledgeCasReplaceAsync<T>(string key, string resultKey, T replacement,
        DateTime expectedUpdatedAt, string expectedHolder, Guid? expectedToken, int expectedStatus,
        string resultAction, byte[] resultEnvelope, Guid? embeddedTargetId = null,
        Guid? expectedChainGeneration = null, RedisNodeFinalizationRecord outboxRecord = null,
        string authoritativeCronKey = null, TickerType terminalType = TickerType.TimeTicker,
        Guid? terminalId = null, string cronSlotKey = null) where T : class
    {
        var serialized = Serializer.Serialize(replacement);
        var evidenceToken = expectedToken ?? expectedChainGeneration;
        var evidenceField = $"{(int)terminalType}:{(terminalId ?? embeddedTargetId ?? Guid.Empty):D}";
        // One authoritative field per typed entity bounds cardinality across repeated executions.
        // The value carries the fenced generation, so only the latest successful terminal mutation
        // remains replayable; a superseding execution atomically overwrites older evidence.
        var terminalDigest = outboxRecord == null
            ? ComputeTerminalEvidenceDigest(serialized, resultAction, resultEnvelope)
            : ComputeOutboxTerminalEvidenceDigest(outboxRecord);
        var evidenceDigest = $"{evidenceToken:D}:{terminalDigest}";
        RedisResult result;
        try
        {
            result = await EvaluateCasReplaceAsync(key, resultKey, replacement, expectedUpdatedAt,
            expectedHolder, expectedToken, expectedStatus, resultAction, resultEnvelope, embeddedTargetId,
            expectedChainGeneration, terminalMutation: true, outboxRecord, authoritativeCronKey, evidenceField, evidenceDigest,
            cronSlotKey).ConfigureAwait(false);
            if (AfterTerminalMutationScriptEvaluatedAsync != null)
                await AfterTerminalMutationScriptEvaluatedAsync().ConfigureAwait(false);
        }
        catch (Exception firstFailure) when (firstFailure is not RedisServerException)
        {
            // A server-side rejection is deterministic (fence, immutable collision, malformed state) and
            // must never be converted into success merely because older generation evidence exists.
            // Replay through the atomic script so evidence and the current entity generation are checked
            // together; a separate evidence read could race a reacquisition.
            try
            {
                result = await EvaluateCasReplaceAsync(key, resultKey, replacement, expectedUpdatedAt,
                    expectedHolder, expectedToken, expectedStatus, resultAction, resultEnvelope, embeddedTargetId,
                    expectedChainGeneration, terminalMutation: true, outboxRecord, authoritativeCronKey, evidenceField, evidenceDigest,
                    cronSlotKey).ConfigureAwait(false);
            }
            catch
            {
                ExceptionDispatchInfo.Capture(firstFailure).Throw();
                throw;
            }
            if (result.IsNull)
            {
                ExceptionDispatchInfo.Capture(firstFailure).Throw();
                throw new InvalidOperationException("Unreachable terminal retry state.");
            }
        }
        return !result.IsNull;
    }

    private static string ComputeTerminalEvidenceDigest(string replacement, string resultAction, byte[] resultEnvelope)
    {
        var document = JsonNode.Parse(replacement)?.AsObject();
        document?.Remove("UpdatedAt");
        document?.Remove("updatedAt");
        var stableReplacement = document?.ToJsonString() ?? replacement;
        var envelope = resultEnvelope == null ? string.Empty : Convert.ToBase64String(resultEnvelope);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            stableReplacement + "\n" + resultAction + "\n" + envelope)));
    }

    private static string ComputeOutboxTerminalEvidenceDigest(RedisNodeFinalizationRecord record)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            "outbox\n" + record.ImmutableDigest + "\n" + record.TerminalMutationDigest)));

    private Task<RedisResult> EvaluateCasReplaceAsync<T>(string key, string resultKey, T replacement,
        DateTime expectedUpdatedAt, string expectedHolder, Guid? expectedToken, int expectedStatus,
        string resultAction, byte[] resultEnvelope, Guid? embeddedTargetId,
        Guid? expectedChainGeneration, bool terminalMutation,
        RedisNodeFinalizationRecord outboxRecord = null,
        string authoritativeCronKey = null, string terminalEvidenceField = "",
        string terminalEvidenceDigest = "", string cronSlotKey = null) where T : class
        => Db.ScriptEvaluateAsync(CasReplaceScript,
            BuildCasKeys(key, resultKey, outboxRecord, authoritativeCronKey, cronSlotKey),
            [(RedisValue)expectedHolder, (RedisValue)(expectedToken?.ToString() ?? ""),
             (RedisValue)expectedStatus, (RedisValue)expectedUpdatedAt.ToString("O"),
             (RedisValue)Serializer.Serialize(replacement), (RedisValue)resultAction,
             (RedisValue)(resultEnvelope ?? []),
             (RedisValue)(embeddedTargetId?.ToString() ?? ""),
             (RedisValue)(expectedChainGeneration?.ToString() ?? ""),
             (RedisValue)(outboxRecord == null ? "" : Serializer.Serialize(outboxRecord)),
             (RedisValue)(outboxRecord?.OutboxId ?? ""),
             (RedisValue)(outboxRecord?.ImmutableDigest ?? ""),
             (RedisValue)(outboxRecord == null ? "" :
                 ToScore(outboxRecord.AvailableAtUtc).ToString(System.Globalization.CultureInfo.InvariantCulture)),
             (RedisValue)(authoritativeCronKey == null ? "0" : "1"),
             (RedisValue)RuntimeActivationEpoch,
             (RedisValue)terminalEvidenceField, (RedisValue)terminalEvidenceDigest,
             (RedisValue)(cronSlotKey == null ? "0" : "1"),
             terminalMutation ? "terminal" : RuntimeAdmissionMode]);

    private RedisKey[] BuildCasKeys(string key, string resultKey,
        RedisNodeFinalizationRecord outboxRecord, string authoritativeCronKey, string cronSlotKey = null)
    {
        var keys = new List<RedisKey> { key, resultKey };
        if (outboxRecord != null)
        {
            keys.Add(NodeFinalizationRecordsKey);
            keys.Add(NodeFinalizationDueKey);
        }
        if (authoritativeCronKey != null)
            keys.Add(authoritativeCronKey);
        if (cronSlotKey != null)
            keys.Add(cronSlotKey);
        keys.Add(TerminalMutationEvidenceKey);
        keys.Add(ActivationMetadataKey);
        return keys.ToArray();
    }

    private static bool IsFencedTerminalWrite(InternalFunctionContext context)
        => context.GetPropsToUpdate().Contains(nameof(InternalFunctionContext.ReleaseLock)) ||
           (context.GetPropsToUpdate().Contains(nameof(InternalFunctionContext.Status)) &&
            context.Status is TickerStatus.Done or TickerStatus.DueDone or TickerStatus.Failed
                or TickerStatus.Cancelled or TickerStatus.Skipped);

    private static bool IsSuccessfulTerminalWrite(InternalFunctionContext context)
        => context.GetPropsToUpdate().Contains(nameof(InternalFunctionContext.Status)) &&
           context.Status is TickerStatus.Done or TickerStatus.DueDone;

    private static (string Action, byte[] Envelope) GetResultMutation(InternalFunctionContext context)
    {
        if (!IsSuccessfulTerminalWrite(context)) return ("none", null);
        var publishes = context.ResultEnvelope != null &&
            context.GetPropsToUpdate().Contains(nameof(InternalFunctionContext.ResultEnvelope));
        return publishes
            ? ("set", RedisResultEnvelopeCodec.Serialize(context.ResultEnvelope))
            : ("clear", null);
    }

    public async Task<bool> CommitSuccessfulTickerAsync(
        InternalFunctionContext functionContext, CancellationToken cancellationToken = default)
    {
        EnsureExactTerminalPartition(functionContext);
        if (functionContext == null) throw new ArgumentNullException(nameof(functionContext));
        if (!functionContext.GetPropsToUpdate().Contains(nameof(InternalFunctionContext.Status)) ||
            functionContext.Status is not (TickerStatus.Done or TickerStatus.DueDone) ||
            !functionContext.GetPropsToUpdate().Contains(nameof(InternalFunctionContext.ResultEnvelope)))
            throw new InvalidOperationException(
                "Atomic result persistence accepts only a successful terminal mutation with an explicit optional result envelope.");
        return await CommitTerminalTickerCoreAsync(
            functionContext, enforceRemoteChildToken: false, cancellationToken).ConfigureAwait(false);
    }

    public bool SupportsAcknowledgedTerminalUpdates => true;

    public Task<bool> CommitTerminalTickerAsync(
        InternalFunctionContext functionContext, CancellationToken cancellationToken = default)
    {
        EnsureExactTerminalPartition(functionContext);
        return CommitTerminalTickerCoreAsync(
            functionContext, enforceRemoteChildToken: true, cancellationToken);
    }

    public Task<bool> CommitTerminalTickerAndEnqueueNodeFinalizationAsync(
        InternalFunctionContext functionContext, NodeFinalizationIntent intent,
        CancellationToken cancellationToken = default)
    {
        EnsureExactTerminalPartition(functionContext);
        if (!SupportsDurableNodeFinalizationOutbox)
            throw new NotSupportedException(
                "Durable Node finalization requires standalone Redis. Redis Cluster is unsupported because existing terminal/result/outbox keys do not share one hash slot.");
        ArgumentNullException.ThrowIfNull(functionContext);
        ArgumentNullException.ThrowIfNull(intent);
        if (intent.TickerType != functionContext.Type || intent.TickerId != functionContext.TickerId ||
            !functionContext.AcquisitionToken.HasValue || intent.AcquisitionToken != functionContext.AcquisitionToken.Value)
            throw new InvalidOperationException("Node finalization intent identity must match the terminal execution context.");
        if (!StringComparer.Ordinal.Equals(intent.RuntimePartitionKey, Keys.Partition.StorageKey))
            throw new InvalidOperationException("Node finalization intent runtime partition mismatch.");
        return CommitTerminalTickerCoreAsync(functionContext, enforceRemoteChildToken: true,
            cancellationToken, RedisNodeFinalizationRecord.Create(intent));
    }

    private void EnsureExactTerminalPartition(InternalFunctionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var expected = Keys.Partition.StorageKey;
        if (string.IsNullOrWhiteSpace(context.RuntimePartitionKey))
        {
            if (Keys.Partition.IsLegacyGlobal)
            {
                context.RuntimePartitionKey = expected;
                return;
            }
            throw new InvalidOperationException("A terminal mutation requires an exact runtime partition identity.");
        }
        if (!StringComparer.Ordinal.Equals(context.RuntimePartitionKey, expected))
            throw new InvalidOperationException("Terminal mutation runtime partition mismatch.");
    }

    private async Task<bool> CommitTerminalTickerCoreAsync(
        InternalFunctionContext functionContext, bool enforceRemoteChildToken,
        CancellationToken cancellationToken = default, RedisNodeFinalizationRecord outboxRecord = null)
    {
        if (functionContext == null)
            throw new ArgumentNullException(nameof(functionContext));
        var successful = IsSuccessfulTerminalWrite(functionContext);
        if (!functionContext.GetPropsToUpdate().Contains(nameof(InternalFunctionContext.Status)) ||
            functionContext.Status is not (TickerStatus.Done or TickerStatus.DueDone or TickerStatus.Failed
                or TickerStatus.Cancelled or TickerStatus.Skipped) ||
            (successful && !functionContext.GetPropsToUpdate().Contains(nameof(InternalFunctionContext.ResultEnvelope))))
            throw new InvalidOperationException(
                "Acknowledged persistence accepts only a terminal mutation; success requires an explicit optional result envelope.");

        cancellationToken.ThrowIfCancellationRequested();
        // Serialize before reading or invoking Lua so malformed/oversized envelopes cannot mutate status.
        var encodedEnvelope = successful && functionContext.ResultEnvelope != null
            ? RedisResultEnvelopeCodec.Serialize(functionContext.ResultEnvelope)
            : null;
        var resultAction = successful ? (encodedEnvelope == null ? "clear" : "set") : "none";
        if (outboxRecord != null)
            outboxRecord.TerminalMutationDigest = RedisNodeFinalizationRecord.ComputeTerminalMutationDigest(
                functionContext, resultAction, encodedEnvelope);

        return functionContext.Type == TickerType.CronTickerOccurrence
            ? await CommitSuccessfulCronOccurrenceAsync(
                functionContext, resultAction, encodedEnvelope, cancellationToken, outboxRecord).ConfigureAwait(false)
            : await CommitSuccessfulTimeTickerAsync(
                functionContext, resultAction, encodedEnvelope, cancellationToken,
                enforceRemoteChildToken, outboxRecord).ConfigureAwait(false);
    }

    private async Task<bool> CommitSuccessfulTimeTickerAsync(
        InternalFunctionContext functionContext, string resultAction, byte[] encodedEnvelope,
        CancellationToken cancellationToken, bool enforceRemoteChildToken,
        RedisNodeFinalizationRecord outboxRecord = null)
    {
        if (functionContext.ParentId != null)
            return await CommitSuccessfulEmbeddedTimeTickerAsync(
                functionContext, resultAction, encodedEnvelope, cancellationToken,
                enforceRemoteChildToken, outboxRecord).ConfigureAwait(false);

        var ticker = await Serializer.GetAsync<TTimeTicker>(
            TimeTickerKey(functionContext.TickerId)).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (ticker == null || !functionContext.AcquisitionToken.HasValue)
            return false;

        var expectedUpdatedAt = ticker.UpdatedAt;
        var expectedStatus = (int)ticker.Status;
        ApplyFunctionContextToTicker(ticker, functionContext);
        ticker.UpdatedAt = NextAggregateUpdatedAt(expectedUpdatedAt);
        var acknowledged = await TryAcknowledgeCasReplaceAsync(
            TimeTickerKey(ticker.Id), TimeTickerResultKey(ticker.Id), ticker, expectedUpdatedAt,
            LockHolder, functionContext.AcquisitionToken, expectedStatus, resultAction, encodedEnvelope,
            outboxRecord: outboxRecord, terminalType: TickerType.TimeTicker,
            terminalId: functionContext.TickerId)
            .ConfigureAwait(false);
        if (!acknowledged)
            return false;

        await IndexManager.AddTimeTickerIndexesAsync(ticker).ConfigureAwait(false);
        return true;
    }

    private async Task<bool> CommitSuccessfulEmbeddedTimeTickerAsync(
        InternalFunctionContext functionContext, string resultAction, byte[] encodedEnvelope,
        CancellationToken cancellationToken, bool enforceRemoteChildToken = false,
        RedisNodeFinalizationRecord outboxRecord = null)
    {
        var rootId = functionContext.ChainRootId;
        if (!rootId.HasValue || !functionContext.ChainGeneration.HasValue)
            return false;
        if (enforceRemoteChildToken &&
            (!functionContext.AcquisitionToken.HasValue ||
             functionContext.AcquisitionToken != functionContext.ChainGeneration))
            return false;

        var root = await Serializer.GetAsync<TTimeTicker>(TimeTickerKey(rootId.Value)).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (root == null)
            return false;

        var child = FindEmbeddedTimeTicker(root.Children, functionContext.TickerId);
        if (child == null)
            return false;

        var expectedUpdatedAt = root.UpdatedAt;
        var expectedStatus = (int)child.Status;
        ApplyFunctionContextToTicker(child, functionContext);
        root.UpdatedAt = NextAggregateUpdatedAt(expectedUpdatedAt);
        var acknowledged = await TryAcknowledgeCasReplaceAsync(
            TimeTickerKey(root.Id), TimeTickerResultKey(functionContext.TickerId), root, expectedUpdatedAt,
            "", null, expectedStatus, resultAction, encodedEnvelope,
            functionContext.TickerId, functionContext.ChainGeneration, outboxRecord,
            terminalType: TickerType.TimeTicker, terminalId: functionContext.TickerId).ConfigureAwait(false);
        if (!acknowledged)
            return false;

        await IndexManager.AddTimeTickerIndexesAsync(root).ConfigureAwait(false);
        return true;
    }

    private async Task<bool> CommitSuccessfulCronOccurrenceAsync(
        InternalFunctionContext functionContext, string resultAction, byte[] encodedEnvelope,
        CancellationToken cancellationToken, RedisNodeFinalizationRecord outboxRecord = null)
    {
        var occurrence = await Serializer.GetAsync<CronTickerOccurrenceEntity<TCronTicker>>(
            CronOccurrenceKey(functionContext.TickerId)).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (occurrence == null || !functionContext.AcquisitionToken.HasValue)
            return false;

        var expectedUpdatedAt = occurrence.UpdatedAt;
        var expectedStatus = (int)occurrence.Status;
        ApplyFunctionContextToCronOccurrence(occurrence, functionContext);
        var acknowledged = await TryAcknowledgeCasReplaceAsync(
            CronOccurrenceKey(occurrence.Id), CronOccurrenceResultKey(occurrence.Id), occurrence,
            expectedUpdatedAt, LockHolder, functionContext.AcquisitionToken, expectedStatus,
            resultAction, encodedEnvelope, outboxRecord: outboxRecord,
            authoritativeCronKey: CronKey(occurrence.CronTickerId),
            terminalType: TickerType.CronTickerOccurrence, terminalId: occurrence.Id,
            cronSlotKey: CronOccurrenceSlotKey(occurrence.CronTickerId, occurrence.ExecutionTime)).ConfigureAwait(false);
        if (!acknowledged)
            return false;

        try
        {
            if (AfterCronTerminalCleanupAsync != null)
                await AfterCronTerminalCleanupAsync().ConfigureAwait(false);
            await IndexManager.AddCronOccurrenceIndexesAsync(occurrence).ConfigureAwait(false);
        }
        catch
        {
            // The terminal mutation, result/evidence, and slot release committed atomically. Index repair
            // is idempotent and must not turn a truthful acknowledgement into a reported failure.
        }
        return true;
    }

    public async Task<IReadOnlyList<NodeFinalizationClaim>> ClaimDueNodeFinalizationsAsync(
        string workerId, int maxCount, DateTime nowUtc, DateTime leaseUntilUtc,
        CancellationToken cancellationToken = default)
    {
        if (!SupportsDurableNodeFinalizationOutbox || maxCount <= 0)
            return Array.Empty<NodeFinalizationClaim>();
        if (string.IsNullOrWhiteSpace(workerId) || workerId.Length > NodeFinalizationClaim.MaxClaimedByLength)
            throw new ArgumentException("Worker ID is required and must fit the claim owner bound.", nameof(workerId));
        if (nowUtc.Kind != DateTimeKind.Utc || leaseUntilUtc.Kind != DateTimeKind.Utc || leaseUntilUtc <= nowUtc)
            throw new ArgumentException("Claim timestamps must be UTC and lease expiry must follow now.");
        cancellationToken.ThrowIfCancellationRequested();

        var tokens = Enumerable.Range(0, maxCount).Select(_ => Guid.NewGuid()).ToArray();
        var arguments = new RedisValue[6 + tokens.Length];
        arguments[0] = ToScore(nowUtc).ToString(System.Globalization.CultureInfo.InvariantCulture);
        arguments[1] = maxCount;
        arguments[2] = workerId;
        arguments[3] = ToScore(leaseUntilUtc).ToString(System.Globalization.CultureInfo.InvariantCulture);
        arguments[4] = nowUtc.ToString("O");
        arguments[5] = leaseUntilUtc.ToString("O");
        for (var i = 0; i < tokens.Length; i++) arguments[6 + i] = tokens[i].ToString("D");

        var result = await Db.ScriptEvaluateAsync(ClaimNodeFinalizationsScript,
            [(RedisKey)NodeFinalizationRecordsKey, NodeFinalizationDueKey], arguments).ConfigureAwait(false);
        if (AfterNodeFinalizationClaimScriptEvaluatedAsync != null)
            await AfterNodeFinalizationClaimScriptEvaluatedAsync().ConfigureAwait(false);
        var rows = (RedisResult[])result;
        var claims = new List<NodeFinalizationClaim>(Math.Min(rows.Length, maxCount));
        foreach (var row in rows)
        {
            var columns = (RedisResult[])row;
            if (columns.Length != 4) continue;
            var outboxId = columns[0].ToString();
            var issuedToken = columns[1].ToString();
            var issuedOwner = columns[2].ToString();
            var rawRecord = columns[3].ToString();
            var record = Serializer.DeserializeOrNull<RedisNodeFinalizationRecord>(rawRecord);
            if (record == null || !record.HasValidIntegrity() || !Guid.TryParse(record.ClaimToken, out var token) ||
                !string.Equals(record.ClaimedBy, workerId, StringComparison.Ordinal) ||
                record.LeaseUntilUtc != leaseUntilUtc || record.AttemptCount < 1)
            {
                await DiscardClaimedNodeFinalizationAsync(
                    outboxId, issuedToken, issuedOwner, rawRecord).ConfigureAwait(false);
                continue;
            }
            try
            {
                claims.Add(new NodeFinalizationClaim(record.ToIntent(), token, workerId,
                    leaseUntilUtc, record.AttemptCount));
            }
            catch (Exception exception) when (exception is ArgumentException or FormatException)
            {
                await DiscardClaimedNodeFinalizationAsync(
                    outboxId, issuedToken, issuedOwner, rawRecord).ConfigureAwait(false);
            }
        }
        return claims;
    }

    private async Task DiscardClaimedNodeFinalizationAsync(
        string outboxId, string claimToken, string claimedBy, string rawRecord)
    {
        await Db.ScriptEvaluateAsync(DiscardClaimedNodeFinalizationScript,
            [(RedisKey)NodeFinalizationRecordsKey, NodeFinalizationDueKey],
            [(RedisValue)outboxId, claimToken, claimedBy, rawRecord]).ConfigureAwait(false);
    }

    public Task<bool> CompleteNodeFinalizationAsync(
        NodeFinalizationClaim claim, CancellationToken cancellationToken = default)
        => MutateClaimAsync(CompleteNodeFinalizationScript, claim, null, null, cancellationToken);

    public Task<bool> RescheduleNodeFinalizationAsync(
        NodeFinalizationClaim claim, DateTime availableAtUtc, string errorCode,
        CancellationToken cancellationToken = default)
    {
        if (availableAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("AvailableAtUtc must be UTC.", nameof(availableAtUtc));
        if (errorCode is { Length: > NodeFinalizationOperationalState.MaxErrorCodeLength })
            throw new ArgumentException("Error code is too long.", nameof(errorCode));
        return MutateClaimAsync(RescheduleNodeFinalizationScript, claim, availableAtUtc,
            errorCode ?? string.Empty, cancellationToken);
    }

    private async Task<bool> MutateClaimAsync(string script, NodeFinalizationClaim claim,
        DateTime? availableAtUtc, string errorCode, CancellationToken cancellationToken)
    {
        if (!SupportsDurableNodeFinalizationOutbox || claim == null) return false;
        cancellationToken.ThrowIfCancellationRequested();
        var record = RedisNodeFinalizationRecord.Create(claim.Intent);
        var values = new List<RedisValue>
        {
            record.OutboxId, record.ImmutableDigest, record.TickerType, record.TickerId,
            record.AcquisitionToken, record.DispatchId, record.NodeEpoch,
            claim.ClaimToken.ToString("D"), claim.ClaimedBy
        };
        if (availableAtUtc.HasValue)
        {
            values.Add(ToScore(availableAtUtc.Value).ToString(System.Globalization.CultureInfo.InvariantCulture));
            values.Add(availableAtUtc.Value.ToString("O"));
            values.Add(errorCode);
        }
        var result = await Db.ScriptEvaluateAsync(script,
            [(RedisKey)NodeFinalizationRecordsKey, NodeFinalizationDueKey], values.ToArray()).ConfigureAwait(false);
        if (AfterNodeFinalizationMutationScriptEvaluatedAsync != null)
            await AfterNodeFinalizationMutationScriptEvaluatedAsync().ConfigureAwait(false);
        return (long)result == 1;
    }
    #endregion

    #region FunctionContext mapping
    private void ApplyFunctionContext(
        InternalFunctionContext context,
        Action<TickerStatus> setStatus,
        Action<string> setSkippedReason,
        Action<DateTime?> setExecutedAt,
        Action<string> setExceptionMessage,
        Action<long> setElapsedTime,
        Action<int> setRetryCount,
        Action releaseLock,
        Action<DateTime> setUpdatedAt)
    {
        var propsToUpdate = context.GetPropsToUpdate();

        if (propsToUpdate.Contains(nameof(InternalFunctionContext.Status)) &&
            context.Status != TickerStatus.Skipped)
        {
            setStatus(context.Status);
        }
        else if (propsToUpdate.Contains(nameof(InternalFunctionContext.Status)))
        {
            setStatus(context.Status);
            setSkippedReason(context.ExceptionDetails);
        }

        if (propsToUpdate.Contains(nameof(InternalFunctionContext.ExecutedAt)))
            setExecutedAt(context.ExecutedAt);

        if (propsToUpdate.Contains(nameof(InternalFunctionContext.ExceptionDetails)) &&
            context.Status != TickerStatus.Skipped)
            setExceptionMessage(context.ExceptionDetails);

        if (propsToUpdate.Contains(nameof(InternalFunctionContext.ElapsedTime)))
            setElapsedTime(context.ElapsedTime);

        if (propsToUpdate.Contains(nameof(InternalFunctionContext.RetryCount)))
            setRetryCount(context.RetryCount);

        if (propsToUpdate.Contains(nameof(InternalFunctionContext.ReleaseLock)))
            releaseLock();

        setUpdatedAt(Clock.UtcNow);
    }

    protected void ApplyFunctionContextToTicker(TTimeTicker ticker, InternalFunctionContext context)
    {
        ApplyFunctionContext(context,
            status => ticker.Status = status,
            reason => ticker.SkippedReason = reason,
            at => ticker.ExecutedAt = at,
            msg => ticker.ExceptionMessage = msg,
            elapsed => ticker.ElapsedTime = elapsed,
            count => ticker.RetryCount = count,
            () => { ticker.LockHolder = null; ticker.LockedAt = null; ticker.LeaseUntil = null; ticker.AcquisitionToken = null; },
            at => ticker.UpdatedAt = at);
        if (IsFencedTerminalWrite(context))
        {
            ticker.LeaseUntil = null;
            ticker.AcquisitionToken = null;
        }
    }

    protected void ApplyFunctionContextToCronOccurrence(CronTickerOccurrenceEntity<TCronTicker> occurrence, InternalFunctionContext context)
    {
        ApplyFunctionContext(context,
            status => occurrence.Status = status,
            reason => occurrence.SkippedReason = reason,
            at => occurrence.ExecutedAt = at,
            msg => occurrence.ExceptionMessage = msg,
            elapsed => occurrence.ElapsedTime = elapsed,
            count => occurrence.RetryCount = count,
            () => { occurrence.LockHolder = null; occurrence.LockedAt = null; occurrence.LeaseUntil = null; occurrence.AcquisitionToken = null; },
            at => occurrence.UpdatedAt = at);
        if (IsFencedTerminalWrite(context))
        {
            occurrence.LeaseUntil = null;
            occurrence.AcquisitionToken = null;
        }
    }

    protected static TimeTickerEntity MapForQueue(TTimeTicker ticker) => MapQueueNode(ticker);

    private static TimeTickerEntity MapQueueNode(TTimeTicker ticker)
        => new()
        {
            Id = ticker.Id,
            Function = ticker.Function,
            RequestContractVersion = ticker.RequestContractVersion,
            RequestContractFingerprint = ticker.RequestContractFingerprint,
            Request = ticker.Request,
            CreatedAt = ticker.CreatedAt,
            Retries = ticker.Retries,
            RetryCount = ticker.RetryCount,
            RetryIntervals = ticker.RetryIntervals,
            TimeoutSeconds = ticker.TimeoutSeconds,
            UpdatedAt = ticker.UpdatedAt,
            ParentId = ticker.ParentId,
            ExecutionTime = ticker.ExecutionTime,
            Status = ticker.Status,
            LockHolder = ticker.LockHolder,
            LockedAt = ticker.LockedAt,
            LeaseUntil = ticker.LeaseUntil,
            AcquisitionToken = ticker.AcquisitionToken,
            ChainRootId = ticker.ChainRootId,
            ChainGeneration = ticker.ChainGeneration,
            ExecutedAt = ticker.ExecutedAt,
            ExceptionMessage = ticker.ExceptionMessage,
            SkippedReason = ticker.SkippedReason,
            ElapsedTime = ticker.ElapsedTime,
            OnStale = ticker.OnStale,
            StaleRestartCount = ticker.StaleRestartCount,
            RunCondition = ticker.RunCondition,
            Children = ticker.Children.Select(MapQueueNode).ToArray()
        };
    #endregion

    #region Core_Time_Ticker_Methods
    public async IAsyncEnumerable<TimeTickerEntity> QueueTimeTickers(TimeTickerEntity[] timeTickers, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var timeTicker in timeTickers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var acquired = await TryAcquireAsync<TTimeTicker>(
                TimeTickerKey(timeTicker.Id), TimeTickerResultKey(timeTicker.Id),
                TickerStatus.Queued,
                timeTicker.UpdatedAt.ToString("O")).ConfigureAwait(false);

            if (acquired == null) continue;

            await IndexManager.AddTimeTickerIndexesAsync(acquired).ConfigureAwait(false);

            timeTicker.LockHolder = acquired.LockHolder;
            timeTicker.LockedAt = acquired.LockedAt;
            timeTicker.UpdatedAt = acquired.UpdatedAt;
            timeTicker.Status = acquired.Status;
            timeTicker.AcquisitionToken = acquired.AcquisitionToken;
            timeTicker.ChainRootId = acquired.ChainRootId;
            timeTicker.ChainGeneration = acquired.ChainGeneration;

            yield return timeTicker;
        }
    }

    public async IAsyncEnumerable<TimeTickerEntity> QueueTimedOutTimeTickers([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var threshold = Clock.UtcNow.AddMilliseconds(-100);

        var dueIds = await Db.SortedSetRangeByScoreAsync(TimeTickerPendingKey, double.NegativeInfinity, ToScore(threshold)).ConfigureAwait(false);

        foreach (var redisValue in dueIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Guid.TryParse(redisValue.ToString(), out var id)) continue;

            var acquired = await TryAcquireAsync<TTimeTicker>(
                TimeTickerKey(id), TimeTickerResultKey(id),
                TickerStatus.InProgress).ConfigureAwait(false);

            if (acquired == null) continue;

            await IndexManager.AddTimeTickerIndexesAsync(acquired).ConfigureAwait(false);

            yield return MapForQueue(acquired);
        }
    }

    public async Task ReleaseAcquiredTimeTickers(Guid[] timeTickerIds, CancellationToken cancellationToken = default)
    {
        var ids = timeTickerIds.Length == 0
            ? ParseGuidSet(await Db.SetMembersAsync(TimeTickerIdsKey).ConfigureAwait(false))
            : timeTickerIds;

        foreach (var id in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var released = await TryReleaseAsync<TTimeTicker>(
                TimeTickerKey(id), TimeTickerResultKey(id)).ConfigureAwait(false);
            if (released == null) continue;

            await IndexManager.AddTimeTickerIndexesAsync(released).ConfigureAwait(false);
        }
    }

    public async Task<TimeTickerEntity[]> GetEarliestTimeTickers(CancellationToken cancellationToken = default)
    {
        if (!await IsActivationPublicationVisibleAsync(cancellationToken).ConfigureAwait(false)) return [];
        var now = Clock.UtcNow;
        var oneSecondAgoScore = ToScore(now.AddSeconds(-1));

        var first = await Db.SortedSetRangeByScoreWithScoresAsync(TimeTickerPendingKey, oneSecondAgoScore, double.PositiveInfinity, Exclude.None, Order.Ascending, 0, 1).ConfigureAwait(false);
        if (first.Length == 0) return [];

        var earliestScore = first[0].Score;
        var minSecond = new DateTime((long)earliestScore, DateTimeKind.Utc);
        var maxScore = ToScore(minSecond.AddSeconds(1));

        var entries = await Db.SortedSetRangeByScoreAsync(TimeTickerPendingKey, earliestScore, maxScore).ConfigureAwait(false);
        var idsInWindow = ParseGuidSet(entries);

        var tickers = await Serializer.LoadByIdsAsync<TTimeTicker>(idsInWindow, TimeTickerKey, cancellationToken).ConfigureAwait(false);

        return tickers
            .Where(t => CanAcquire(t.Status, t.LockHolder, LockHolder))
            .Select(MapForQueue)
            .ToArray();
    }

    public async Task<int> UpdateTimeTicker(InternalFunctionContext functionContext, CancellationToken cancellationToken = default)
    {
        if (functionContext.ParentId != null)
            return await UpdateEmbeddedTimeTickerAsync(functionContext, cancellationToken).ConfigureAwait(false);

        for (var attempt = 0; attempt < 64; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var ticker = await Serializer.GetAsync<TTimeTicker>(TimeTickerKey(functionContext.TickerId)).ConfigureAwait(false);
            if (ticker == null) return 0;

            var fenced = IsFencedTerminalWrite(functionContext);
            if (fenced && !functionContext.AcquisitionToken.HasValue) return 0;
            var expectedUpdatedAt = ticker.UpdatedAt;
            var expectedStatus = fenced ? (int)ticker.Status : -1;
            ApplyFunctionContextToTicker(ticker, functionContext);
            ticker.UpdatedAt = NextAggregateUpdatedAt(expectedUpdatedAt);
            var resultMutation = GetResultMutation(functionContext);
            var updated = await TryCasReplaceAsync(TimeTickerKey(ticker.Id), TimeTickerResultKey(ticker.Id),
                    ticker, expectedUpdatedAt, fenced ? LockHolder : "",
                    fenced ? functionContext.AcquisitionToken : null, expectedStatus,
                    resultMutation.Action, resultMutation.Envelope)
                .ConfigureAwait(false);
            if (updated == null) continue;
            await IndexManager.AddTimeTickerIndexesAsync(updated).ConfigureAwait(false);
            return 1;
        }

        return 0;
    }

    private async Task<int> UpdateEmbeddedTimeTickerAsync(
        InternalFunctionContext functionContext,
        CancellationToken cancellationToken)
    {
        var rootId = functionContext.ChainRootId;
        if (!rootId.HasValue || !functionContext.ChainGeneration.HasValue) return 0;

        for (var attempt = 0; attempt < 64; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var root = await Serializer.GetAsync<TTimeTicker>(TimeTickerKey(rootId.Value)).ConfigureAwait(false);
            if (root == null) return 0;

            var child = FindEmbeddedTimeTicker(root.Children, functionContext.TickerId);
            if (child == null) return 0;

            var expectedUpdatedAt = root.UpdatedAt;
            var expectedStatus = (int)child.Status;
            ApplyFunctionContextToTicker(child, functionContext);
            root.UpdatedAt = NextAggregateUpdatedAt(expectedUpdatedAt);
            var resultMutation = GetResultMutation(functionContext);
            var updated = await TryCasReplaceAsync(TimeTickerKey(root.Id),
                    TimeTickerResultKey(functionContext.TickerId), root, expectedUpdatedAt,
                    expectedHolder: "",
                    expectedToken: null,
                    expectedStatus: expectedStatus,
                    resultAction: resultMutation.Action, resultEnvelope: resultMutation.Envelope,
                    embeddedTargetId: functionContext.TickerId,
                    expectedChainGeneration: functionContext.ChainGeneration)
                .ConfigureAwait(false);
            if (updated == null) continue;
            await IndexManager.AddTimeTickerIndexesAsync(updated).ConfigureAwait(false);
            return 1;
        }

        return 0;
    }

    private DateTime NextAggregateUpdatedAt(DateTime expectedUpdatedAt)
    {
        var now = Clock.UtcNow;
        return now > expectedUpdatedAt ? now : expectedUpdatedAt.AddTicks(1);
    }

    private static TTimeTicker FindEmbeddedTimeTicker(
        IEnumerable<TTimeTicker> children,
        Guid tickerId)
    {
        foreach (var child in children)
        {
            if (child.Id == tickerId) return child;
            var descendant = FindEmbeddedTimeTicker(child.Children, tickerId);
            if (descendant != null) return descendant;
        }

        return null;
    }

    public async Task<byte[]> GetTimeTickerRequest(Guid id, CancellationToken cancellationToken)
    {
        var ticker = await Serializer.GetAsync<TTimeTicker>(TimeTickerKey(id)).ConfigureAwait(false);
        return ticker?.Request;
    }

    public async Task UpdateTimeTickersWithUnifiedContext(Guid[] timeTickerIds, InternalFunctionContext functionContext, CancellationToken cancellationToken = default)
    {
        var tickers = await Serializer.LoadByIdsAsync<TTimeTicker>(timeTickerIds, TimeTickerKey, cancellationToken).ConfigureAwait(false);
        if (AfterUnifiedContextDocumentsLoadedAsync != null)
            await AfterUnifiedContextDocumentsLoadedAsync().ConfigureAwait(false);

        foreach (var ticker in tickers)
        {
            var current = ticker;
            for (var attempt = 0; attempt < 64; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var expectedUpdatedAt = current.UpdatedAt;
                ApplyFunctionContextToTicker(current, functionContext);
                current.UpdatedAt = NextAggregateUpdatedAt(expectedUpdatedAt);
                var outcome = (long)await Db.ScriptEvaluateAsync(
                    UpdateTimeTickerWithIndexesScript,
                    [(RedisKey)TimeTickerKey(current.Id), (RedisKey)ActivationMetadataKey,
                     (RedisKey)TimeTickerIdsKey, (RedisKey)TimeTickerPendingKey,
                     (RedisKey)TimeTickerRetentionSucceededKey, (RedisKey)TimeTickerRetentionFailedKey,
                     (RedisKey)TimeTickerRetentionCancelledKey, (RedisKey)TimeTickerRetentionSkippedKey],
                    BuildUnifiedTimeTickerMutationArguments(current, expectedUpdatedAt)).ConfigureAwait(false);
                if (outcome != -2) break;
                current = await Serializer.GetAsync<TTimeTicker>(TimeTickerKey(current.Id)).ConfigureAwait(false);
                if (current == null) break;
            }
        }
    }

    private RedisValue[] BuildUnifiedTimeTickerMutationArguments(TTimeTicker ticker, DateTime expectedUpdatedAt)
    {
        var pending = ticker.ExecutionTime.HasValue && CanAcquire(ticker.Status, ticker.LockHolder, LockHolder);
        var retentionIndex = 0;
        var retentionScore = string.Empty;
        var now = Clock.UtcNow;
        if (!ticker.ParentId.HasValue && ticker.Children is not { Count: > 0 } &&
            ticker.ExecutedAt is { } executedAt && !ticker.AcquisitionToken.HasValue &&
            !(ticker.LeaseUntil is { } leaseUntil && leaseUntil > now))
        {
            retentionIndex = ticker.Status switch
            {
                TickerStatus.Done or TickerStatus.DueDone => 1,
                TickerStatus.Failed => 2,
                TickerStatus.Cancelled => 3,
                TickerStatus.Skipped => 4,
                _ => 0
            };
            if (retentionIndex != 0)
                retentionScore = ToScore(executedAt).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return
        [
            expectedUpdatedAt.ToString("O"), Serializer.Serialize(ticker), ticker.Id.ToString(),
            pending ? "1" : "0",
            pending ? ToScore(ticker.ExecutionTime.Value).ToString(System.Globalization.CultureInfo.InvariantCulture) : "",
            retentionIndex, retentionScore
        ];
    }

    public async Task<Guid[]> TransitionQueuedTimeTickersToInProgressAsync(
        IReadOnlyCollection<AcquisitionLease> leases, CancellationToken cancellationToken = default)
    {
        var winners = new List<Guid>();
        foreach (var lease in leases)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (lease.AcquisitionToken is not Guid token) continue;
            var ticker = await TryTransitionQueuedAsync<TTimeTicker>(TimeTickerKey(lease.TickerId), token)
                .ConfigureAwait(false);
            if (ticker == null) continue;
            await IndexManager.AddTimeTickerIndexesAsync(ticker).ConfigureAwait(false);
            winners.Add(lease.TickerId);
        }
        return winners.ToArray();
    }

    public async Task<TimeTickerEntity[]> AcquireImmediateTimeTickersAsync(Guid[] ids, CancellationToken cancellationToken = default)
    {
        if (ids == null || ids.Length == 0) return [];

        var acquired = new List<TimeTickerEntity>();
        foreach (var id in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var ticker = await TryAcquireAsync<TTimeTicker>(
                TimeTickerKey(id), TimeTickerResultKey(id),
                TickerStatus.InProgress).ConfigureAwait(false);

            if (ticker == null) continue;

            await IndexManager.AddTimeTickerIndexesAsync(ticker).ConfigureAwait(false);
            acquired.Add(MapForQueue(ticker));
        }

        return acquired.ToArray();
    }

    public async Task<TimeTickerEntity> AcquireTimeTickerOnDemandAsync(
        Guid id, DateTime executionTime, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureReconciliationActivationTopology();
        var now = Clock.UtcNow;
        var token = Guid.NewGuid();
        var result = await Db.ScriptEvaluateAsync(AcquireOnDemandScript,
            [(RedisKey)TimeTickerKey(id), (RedisKey)TimeTickerResultKey(id),
             TerminalMutationEvidenceKey, (RedisKey)ActivationMetadataKey],
            [(RedisValue)LockHolder, (RedisValue)now.ToString("O"),
             (RedisValue)now.Add(_schedulerOptions.LeaseDuration).ToString("O"),
             (RedisValue)token.ToString(), LuaStatusInProgress, (RedisValue)executionTime.ToUniversalTime().ToString("O"),
             LuaStatusQueued, (RedisValue)RuntimeActivationEpoch, RuntimeAdmissionMode])
            .ConfigureAwait(false);
        if (result.IsNull) return null;
        var ticker = Serializer.DeserializeOrNull<TTimeTicker>((string)result);
        if (ticker == null) return null;
        await IndexManager.AddTimeTickerIndexesAsync(ticker).ConfigureAwait(false);
        return MapForQueue(ticker);
    }

    public async Task ReleaseDeadNodeTimeTickerResources(string instanceIdentifier, CancellationToken cancellationToken = default)
    {
        var members = await Db.SetMembersAsync(TimeTickerIdsKey).ConfigureAwait(false);
        var ids = ParseGuidSet(members);

        foreach (var id in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var recovered = await TryRecoverDeadNodeAsync<TTimeTicker>(
                TimeTickerKey(id), TimeTickerResultKey(id), instanceIdentifier).ConfigureAwait(false);
            if (recovered == null) continue;

            await IndexManager.AddTimeTickerIndexesAsync(recovered).ConfigureAwait(false);
        }
    }
    #endregion

    #region Core_Cron_Ticker_Methods
    public Task MigrateDefinedCronTickers((string Function, string Expression)[] cronTickers, CancellationToken cancellationToken = default)
        => MigrateDefinedCronTickers(
            Array.ConvertAll(cronTickers, static ticker => new DefinedCronTickerSeed(ticker.Function, ticker.Expression)),
            cancellationToken);

    public Task MigrateDefinedCronTickers(DefinedCronTickerSeed[] cronTickers, CancellationToken cancellationToken = default)
        => MigrateDefinedCronTickers(new DefinedCronSeedManifest(cronTickers), cancellationToken);

    public Task MigrateDefinedCronTickers(DefinedCronSeedManifest manifest, CancellationToken cancellationToken = default)
        => MigrateDefinedCronTickers(manifest, cancellationToken, 0);

    private async Task MigrateDefinedCronTickers(
        DefinedCronSeedManifest manifest, CancellationToken cancellationToken, int adoptionRetry)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RuntimeManifestAdmission.Validate(manifest,
            _schedulerOptions.HasRuntimeActivationScopeBinding,
            _schedulerOptions.RuntimeSchedulerEnabled,
            _schedulerOptions.RuntimeActivationScope?.ScopeKey,
            _runtimeActivationEpoch);
        EnsureAtomicMigrationTopology();
        await RepairCronDiscoverabilityAsync(256, cancellationToken).ConfigureAwait(false);
        var now = Clock.UtcNow;
        var grace = _schedulerOptions.DefinedCronRetirementGracePeriod;
        const string seedPrefix = "MemoryTicker_Seeded_";

        // Orphan detection compares persisted *seeded* rows (non-empty InitIdentifier) to the DESIRED
        // SEED MANIFEST — the local code-owned schedules this pass wants — never to the global runtime
        // function registry. Comparing to the registry conflated "function still registered" with "code
        // still wants a seeded schedule", so removing only a cron expression left the stale seeded row
        // firing forever (Slice 1). Dashboard-created rows (empty InitIdentifier) are never candidates.
        // Mirrors the EF/Mongo rationale.
        // Blocked seeds (canSeed == false) are present in the manifest but excluded from the desired set,
        // so they are handled by the retirement path below and retired IMMEDIATELY (no grace) because
        // continuing to schedule an unsatisfiable request is unsafe.
        var blockedFunctions = manifest.Seeds.Where(s => !s.CanSeed)
            .Select(s => s.Function).ToHashSet(StringComparer.Ordinal);
        var blockedSeedKeys = manifest.IsLegacyGlobal
            ? null
            : manifest.Seeds.Where(s => !s.CanSeed)
                .Select(manifest.SeedKeyFor).ToHashSet(StringComparer.Ordinal);

        var existingList = await Serializer.LoadAllFromSetAsync<TCronTicker>(CronIdsKey, CronKey, cancellationToken).ConfigureAwait(false);

        if (!manifest.IsLegacyGlobal)
        {
            foreach (var functionGroup in manifest.Seeds.Where(s => s.CanSeed).GroupBy(s => s.Function))
            {
                var legacy = existingList.Where(x => !string.IsNullOrEmpty(x.InitIdentifier)
                    && CronSeedIdentity.CanonicallyEquals(x.Function, functionGroup.Key) && x.SeedOwnerNamespace == null
                    && (x.SeedKey == null
                        || CronSeedIdentity.CanonicallyEquals(x.SeedKey, x.Function)
                        || CronSeedIdentity.LegacyAdoptionKeys(
                            manifest.ApplicationNamespace, functionGroup.Single().StableDefinitionId)
                            .Contains(x.SeedKey, StringComparer.Ordinal))).ToArray();
                if (legacy.Length == 0) continue;
                if (!manifest.TryGetLegacyOwner(functionGroup.Key, out var explicitOwner))
                    throw new InvalidOperationException(
                        $"Legacy defined-Cron function '{functionGroup.Key}' requires an explicit legacy ownership mapping before any application may mutate it.");
                if (!string.Equals(explicitOwner, manifest.ApplicationNamespace, StringComparison.Ordinal))
                    continue;
                if (legacy.Length != 1 || functionGroup.Count() != 1)
                    throw new InvalidOperationException(
                        $"Ambiguous legacy defined-Cron ownership for function '{functionGroup.Key}' while application namespace " +
                        $"'{manifest.ApplicationNamespace}' attempted adoption. No rows were mutated; resolve ownership explicitly.");
            }
        }

        // Phase A — non-destructive retirement of seeded rows no longer desired. A blocked required-
        // contract seed retires immediately; an absent seed honors the grace window. The row's document is
        // rewritten in place (SetAsync) and its index membership is NEVER removed — the row stays
        // discoverable to dashboards while the IsEnabled filter keeps the scheduler from polling it.
        // Nothing is deleted here (Slice 3): occurrences/results are preserved.
        foreach (var cron in existingList.Where(c =>
                     !string.IsNullOrEmpty(c.InitIdentifier)
                     && (manifest.IsLegacyGlobal
                         ? manifest.IsOrphanedSeedFunction(c.Function)
                         : CronSeedIdentity.CanonicallyEquals(c.SeedOwnerNamespace, manifest.ApplicationNamespace)
                           && manifest.IsOrphanedSeedKey(c.SeedKey))))
        {
            var observedRevision = cron.DefinitionRevision;
            var immediate = manifest.IsLegacyGlobal
                ? blockedFunctions.Any(function =>
                    CronSeedIdentity.CanonicallyEquals(function, cron.Function))
                : blockedFunctions.Any(function =>
                      CronSeedIdentity.CanonicallyEquals(function, cron.Function))
                  || blockedSeedKeys!.Contains(cron.SeedKey);
            var retirementChanged = CronSeedRetirement.ApplyRetirement(cron, now, grace, immediate);
            if (retirementChanged)
            {
                cron.UpdatedAt = now;
            }
            if (cron.RetiredAt.HasValue)
                await WriteCronDefinitionAtomicAsync(cron, cancellationToken, quarantinePending: true, now,
                        allowActivating: true, expectedDefinitionRevision: observedRevision)
                    .ConfigureAwait(false);
            else if (retirementChanged)
                await WriteCronDefinitionAtomicAsync(cron, cancellationToken, allowActivating: true,
                    expectedDefinitionRevision: observedRevision).ConfigureAwait(false);
        }

        // Phase B — reconcile desired seeds into code-owned rows keyed by the stable SeedKey. Matching is
        // restricted to seeded rows (non-empty InitIdentifier) so a user/dashboard row sharing a function
        // name is never matched or mutated. Duplicate legacy seeded rows for one function are duplicate-
        // TOLERANT: the deterministic canonical row (lowest id, via GroupBy/OrderBy — NEVER a keyed
        // ToDictionary, which would throw on the duplicate) adopts the SeedKey and stays enabled, while
        // every redundant duplicate is disabled and marked retired IN PLACE (kept null-keyed) but never
        // deleted. A legacy row adopts its SeedKey IN PLACE (its id, hence its Redis key, never changes). A
        // brand-new row uses the deterministic id derived from the SeedKey so concurrent first-time
        // reconciles converge on one Redis key (SetAsync is an idempotent overwrite). A desired seed that
        // (re)appeared has any framework retirement state cleared, restoring only a framework-disabled row.
        var byFunction = existingList
            .Where(c => !string.IsNullOrEmpty(c.InitIdentifier))
            .GroupBy(c => c.Function, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.OrderBy(c => c.Id).ToList(), StringComparer.Ordinal);

        foreach (var seed in manifest.Seeds)
        {
            if (!seed.CanSeed)
                continue;

            var seedKey = manifest.SeedKeyFor(seed);
            var legacyAdoptionKeys = manifest.IsLegacyGlobal
                ? []
                : CronSeedIdentity.LegacyAdoptionKeys(manifest.ApplicationNamespace, seed.StableDefinitionId);
            var acceptedSeedKeys = manifest.IsLegacyGlobal
                ? []
                : CronSeedIdentity.AcceptedSeedKeys(manifest.ApplicationNamespace, seed.StableDefinitionId);

            var group = manifest.IsLegacyGlobal
                ? (byFunction.TryGetValue(seed.Function, out var legacyGroup) ? legacyGroup : null)
                : existingList.Where(x => !string.IsNullOrEmpty(x.InitIdentifier)
                        && ((acceptedSeedKeys.Contains(x.SeedKey, StringComparer.Ordinal)
                             && CronSeedIdentity.CanonicallyEquals(x.SeedOwnerNamespace, manifest.ApplicationNamespace))
                            || (CronSeedIdentity.CanonicallyEquals(x.Function, seed.Function) && x.SeedOwnerNamespace == null
                                && manifest.MayAdoptLegacy(seed.Function)
                                && (x.SeedKey == null
                                    || CronSeedIdentity.CanonicallyEquals(x.SeedKey, x.Function)
                                    || legacyAdoptionKeys.Contains(x.SeedKey, StringComparer.Ordinal)))))
                    .OrderBy(x => x.Id).ToList();

            if (group is { Count: > 0 })
            {
                // Prefer the row that already owns this SeedKey, then an active row, then lowest id;
                // picking the lowest id blindly would move an already-owned SeedKey onto a legacy null-key
                // duplicate and leave two enabled keyed owners.
                var (cron, duplicates) = CronSeedCanonical.Select(group, seedKey);
                var observedRevision = cron.DefinitionRevision;
                var compareLegacyOwner = !manifest.IsLegacyGlobal && cron.SeedOwnerNamespace == null;
                var expectedLegacySeedKey = compareLegacyOwner ? cron.SeedKey ?? string.Empty : null;
                var changed = false;
                var definitionChanged = false;

                if (cron.SeedKey == null || (!manifest.IsLegacyGlobal && cron.SeedKey != seedKey))
                {
                    cron.SeedKey = seedKey; // adopt in place — id/Redis key unchanged
                    changed = true;
                }

                if (!manifest.IsLegacyGlobal && cron.SeedOwnerNamespace != manifest.ApplicationNamespace)
                {
                    cron.SeedOwnerNamespace = manifest.ApplicationNamespace;
                    changed = true;
                }

                if (!string.Equals(cron.Expression, seed.Expression, StringComparison.Ordinal))
                {
                    cron.Expression = seed.Expression;
                    changed = true;
                    definitionChanged = true;
                }

                // Reconcile authoritative contract identity onto seeded rows only; legacy/dashboard
                // rows (no seed InitIdentifier) keep their own identity.
                if (!string.IsNullOrEmpty(cron.InitIdentifier)
                    && !seed.MatchesIdentity(cron.RequestContractVersion, cron.RequestContractFingerprint))
                {
                    cron.RequestContractVersion = seed.RequestContractVersion;
                    cron.RequestContractFingerprint = seed.RequestContractFingerprint;
                    changed = true;
                    definitionChanged = true;
                }

                if (cron.Retries != seed.Retries)
                {
                    cron.Retries = seed.Retries;
                    changed = true;
                    definitionChanged = true;
                }

                if (!(cron.RetryIntervals ?? []).SequenceEqual(seed.RetryIntervals ?? []))
                {
                    cron.RetryIntervals = seed.RetryIntervals;
                    changed = true;
                    definitionChanged = true;
                }

                if (cron.TimeoutSeconds != seed.TimeoutSeconds)
                {
                    cron.TimeoutSeconds = seed.TimeoutSeconds;
                    changed = true;
                    definitionChanged = true;
                }

                if (definitionChanged)
                {
                    cron.DefinitionRevision = Math.Max(1, cron.DefinitionRevision + 1);
                    changed = true;
                }
                else if (cron.DefinitionRevision <= 0)
                {
                    cron.DefinitionRevision = 1;
                    changed = true;
                }

                // Desired-active seed: clear any framework retirement, restoring only framework-disabled state.
                if (CronSeedRetirement.ClearRetirement(cron))
                    changed = true;

                // Advisory heartbeat for retirement grace accounting; persisted with any material change.
                cron.SeedLastSeenAt = now;

                if (changed)
                    cron.UpdatedAt = now;

                var published = await WriteCronDefinitionAtomicAsync(cron, cancellationToken,
                        definitionChanged, now, allowActivating: true,
                        expectedSeedOwnerNamespace: compareLegacyOwner ? string.Empty : null,
                        expectedSeedKey: expectedLegacySeedKey,
                        expectedDefinitionRevision: observedRevision)
                    .ConfigureAwait(false);
                if (!published)
                {
                    if (adoptionRetry >= 8)
                        throw new InvalidOperationException(
                            "Redis legacy cron ownership adoption did not converge after 8 atomic retries.");
                    await MigrateDefinedCronTickers(manifest, cancellationToken, adoptionRetry + 1)
                        .ConfigureAwait(false);
                    return;
                }

                // Retire redundant duplicates in place (canonical already chosen); never delete.
                foreach (var dup in duplicates)
                {
                    var duplicateObservedRevision = dup.DefinitionRevision;
                    var duplicateChanged = CronSeedRetirement.RetireDuplicate(dup, now);
                    if (duplicateChanged)
                    {
                        dup.UpdatedAt = now;
                    }
                    if (dup.RetiredAt.HasValue)
                        await WriteCronDefinitionAtomicAsync(dup, cancellationToken, quarantinePending: true, now,
                                allowActivating: true, expectedDefinitionRevision: duplicateObservedRevision)
                            .ConfigureAwait(false);
                    else if (duplicateChanged)
                        await WriteCronDefinitionAtomicAsync(dup, cancellationToken, allowActivating: true,
                            expectedDefinitionRevision: duplicateObservedRevision).ConfigureAwait(false);
                }
            }
            else
            {
                var entity = new TCronTicker
                {
                    Id = CronSeedIdentity.DeterministicId(seedKey),
                    Function = seed.Function,
                    Expression = seed.Expression,
                    DefinitionRevision = 1,
                    SeedKey = seedKey,
                    SeedOwnerNamespace = manifest.ApplicationNamespace,
                    SeedLastSeenAt = now,
                    InitIdentifier = $"{seedPrefix}{seed.Function}",
                    CreatedAt = now,
                    UpdatedAt = now,
                    Request = [],
                    RequestContractVersion = seed.RequestContractVersion,
                    RequestContractFingerprint = seed.RequestContractFingerprint,
                    Retries = seed.Retries,
                    RetryIntervals = seed.RetryIntervals,
                    TimeoutSeconds = seed.TimeoutSeconds
                };
                await WriteCronDefinitionAtomicAsync(entity, cancellationToken, allowActivating: true,
                    expectedDefinitionRevision: 0).ConfigureAwait(false);
            }
        }
    }

    private async Task RepairCronDiscoverabilityAsync(int batchSize, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await Db.ScriptEvaluateAsync(RepairCronDiscoverabilityScript,
                [CronIdsKey, CronRepairPhaseKey, CronRepairCursorKey, CronRepairPendingKey,
                 CronRepairQuarantineKey, ActivationMetadataKey],
                [(RedisValue)batchSize, (RedisValue)$"{Prefix}:cron:"]).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var values = (RedisResult[])result;
            if ((string)values[3] == "fenced")
                throw new InvalidOperationException(
                    "Redis Cron discoverability repair was rejected because legacy partition adoption has started.");
            if ((long)values[1] > 0)
                throw new InvalidDataException(
                    $"Redis cron discoverability repair quarantined {(long)values[1]} corrupt document(s); progress={values[3]}.");
            if ((long)values[2] == 0)
                return;
        }
    }

    protected async Task<bool> WriteCronDefinitionAtomicAsync(
        TCronTicker ticker, CancellationToken cancellationToken = default,
        bool quarantinePending = false, DateTime? mutationTime = null, bool allowActivating = false,
        string expectedSeedOwnerNamespace = null, string expectedSeedKey = null,
        long? expectedDefinitionRevision = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureAtomicMigrationTopology();
        var now = mutationTime ?? Clock.UtcNow;
        var result = await Db.ScriptEvaluateAsync(MutateCronDefinitionScript,
            [(RedisKey)CronKey(ticker.Id), CronIdsKey, CronRepairQuarantineKey,
             CronOccurrencesByCronKey(ticker.Id), CronOccurrenceIdsKey, CronOccurrencePendingKey,
             CronOccurrenceRetentionSucceededKey, CronOccurrenceRetentionFailedKey,
             CronOccurrenceRetentionCancelledKey, CronOccurrenceRetentionSkippedKey,
             CronOccurrenceRepairQuarantineKey, ActivationMetadataKey],
            [(RedisValue)ticker.Id.ToString(), (RedisValue)"upsert",
             (RedisValue)Serializer.Serialize(ticker), (RedisValue)(quarantinePending ? "1" : "0"),
             (RedisValue)now.ToString("O"), LuaStatusIdle, LuaStatusQueued, LuaStatusSkipped,
             (RedisValue)"Quarantined because its Cron definition revision is stale.",
             (RedisValue)ToScore(now), (RedisValue)$"{Prefix}:co:",
             (RedisValue)$"{Prefix}:cron:{ticker.Id}:slot:",
             (RedisValue)RuntimeActivationEpoch,
             (RedisValue)(allowActivating || StartupSeederAdmissionActive ? "1" : "0"),
             (RedisValue)(expectedSeedKey == null ? "0" : "1"),
             (RedisValue)(expectedSeedOwnerNamespace ?? string.Empty),
             (RedisValue)(expectedSeedKey ?? string.Empty),
             (RedisValue)(expectedDefinitionRevision ?? -1), RuntimeAdmissionMode]).ConfigureAwait(false);
        if (AfterCronDefinitionMutationScriptEvaluatedAsync != null)
            await AfterCronDefinitionMutationScriptEvaluatedAsync().ConfigureAwait(false);
        if ((long)result == -2)
            throw new InvalidDataException(
                $"A Redis Cron occurrence for definition '{ticker.Id}' is corrupt; the original occurrence was quarantined and the new definition revision was not published.");
        if ((long)result == -1)
            throw new InvalidDataException(
                $"Redis cron document '{CronKey(ticker.Id)}' is corrupt; the original value was quarantined and was not replaced.");
        if ((long)result == -3)
            throw new InvalidOperationException(
                "Redis cron definition mutation was rejected by the exact reconciliation activation epoch fence.");
        if ((long)result == -5)
            return false;
        return (long)result != -4;
    }

    protected async Task<bool> DeleteCronDefinitionAtomicAsync(
        Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureAtomicMigrationTopology();
        var result = await Db.ScriptEvaluateAsync(MutateCronDefinitionScript,
            [(RedisKey)CronKey(id), CronIdsKey, CronRepairQuarantineKey,
             CronOccurrencesByCronKey(id), CronOccurrenceIdsKey, CronOccurrencePendingKey,
             CronOccurrenceRetentionSucceededKey, CronOccurrenceRetentionFailedKey,
             CronOccurrenceRetentionCancelledKey, CronOccurrenceRetentionSkippedKey,
             CronOccurrenceRepairQuarantineKey, ActivationMetadataKey],
            [(RedisValue)id.ToString(), (RedisValue)"delete", RedisValue.EmptyString,
             (RedisValue)"0", (RedisValue)Clock.UtcNow.ToString("O"), LuaStatusIdle,
             LuaStatusQueued, LuaStatusSkipped, (RedisValue)"unused", (RedisValue)0,
             (RedisValue)$"{Prefix}:co:", (RedisValue)$"{Prefix}:cron:{id}:slot:",
             (RedisValue)RuntimeActivationEpoch, (RedisValue)"0",
             (RedisValue)"0", RedisValue.EmptyString, RedisValue.EmptyString, (RedisValue)(-1),
             RuntimeAdmissionMode])
            .ConfigureAwait(false);
        return (long)result == 1;
    }

    protected async Task<bool> MarkCronDefinitionDeletingAtomicAsync(
        Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureAtomicMigrationTopology();
        var result = await Db.ScriptEvaluateAsync(MutateCronDefinitionScript,
            [(RedisKey)CronKey(id), CronIdsKey, CronRepairQuarantineKey,
             CronOccurrencesByCronKey(id), CronOccurrenceIdsKey, CronOccurrencePendingKey,
             CronOccurrenceRetentionSucceededKey, CronOccurrenceRetentionFailedKey,
             CronOccurrenceRetentionCancelledKey, CronOccurrenceRetentionSkippedKey,
             CronOccurrenceRepairQuarantineKey, ActivationMetadataKey],
            [(RedisValue)id.ToString(), (RedisValue)"markDeleting", RedisValue.EmptyString,
             (RedisValue)"0", (RedisValue)Clock.UtcNow.ToString("O"), LuaStatusIdle,
             LuaStatusQueued, LuaStatusSkipped, (RedisValue)"unused", (RedisValue)0,
             (RedisValue)$"{Prefix}:co:", (RedisValue)$"{Prefix}:cron:{id}:slot:",
             (RedisValue)RuntimeActivationEpoch, (RedisValue)"0",
             (RedisValue)"0", RedisValue.EmptyString, RedisValue.EmptyString, (RedisValue)(-1),
             RuntimeAdmissionMode])
            .ConfigureAwait(false);
        return (long)result == 1;
    }

    protected async Task<bool> DeleteCronOccurrenceAtomicAsync(
        CronTickerOccurrenceEntity<TCronTicker> occurrence,
        int? firstRetentionStatus = null, int? secondRetentionStatus = null,
        DateTime? retentionCutoff = null, DateTime? now = null)
    {
        var observedAt = Clock.UtcNow;
        if (!firstRetentionStatus.HasValue &&
            (occurrence.Status == TickerStatus.InProgress || occurrence.AcquisitionToken.HasValue ||
             occurrence.LeaseUntil.HasValue && occurrence.LeaseUntil.Value > observedAt))
            return false;
        RedisValue[] arguments = firstRetentionStatus.HasValue
            ? [(RedisValue)occurrence.Id.ToString(), (RedisValue)occurrence.CronTickerId.ToString(),
               (RedisValue)firstRetentionStatus.Value, (RedisValue)secondRetentionStatus!.Value,
               (RedisValue)retentionCutoff!.Value.ToUniversalTime().ToString("O"),
               (RedisValue)now!.Value.ToUniversalTime().ToString("O")]
            : [(RedisValue)occurrence.Id.ToString(), (RedisValue)occurrence.CronTickerId.ToString(),
               (RedisValue)(int)occurrence.Status,
               (RedisValue)(occurrence.AcquisitionToken?.ToString() ?? string.Empty),
               (RedisValue)(occurrence.LeaseUntil?.ToUniversalTime().ToString("O") ?? string.Empty),
               (RedisValue)occurrence.UpdatedAt.ToUniversalTime().ToString("O"),
               (RedisValue)observedAt.ToUniversalTime().ToString("O")];
        var result = await Db.ScriptEvaluateAsync(DeleteCronOccurrenceScript,
            [(RedisKey)CronOccurrenceKey(occurrence.Id), CronOccurrenceResultKey(occurrence.Id),
             CronOccurrenceIdsKey, CronOccurrencePendingKey, CronOccurrencesByCronKey(occurrence.CronTickerId),
             CronOccurrenceRetentionSucceededKey, CronOccurrenceRetentionFailedKey,
             CronOccurrenceRetentionCancelledKey, CronOccurrenceRetentionSkippedKey,
             (RedisKey)CronOccurrenceSlotKey(occurrence.CronTickerId, occurrence.ExecutionTime),
             TerminalMutationEvidenceKey],
            arguments)
            .ConfigureAwait(false);
        return (long)result == 1;
    }

    protected async Task ReleaseCronOccurrenceSlotAsync(
        Guid occurrenceId, Guid cronTickerId, DateTime executionTime)
    {
        await Db.ScriptEvaluateAsync(ReleaseCronOccurrenceSlotScript,
            [(RedisKey)CronOccurrenceSlotKey(cronTickerId, executionTime)],
            [(RedisValue)occurrenceId.ToString()]).ConfigureAwait(false);
    }

    private async Task RemoveUnleasedPendingCronOccurrencesAsync(
        Guid cronTickerId, DateTime now, CancellationToken cancellationToken)
    {
        var members = await Db.SetMembersAsync(CronOccurrencesByCronKey(cronTickerId)).ConfigureAwait(false);
        foreach (var occurrenceId in ParseGuidSet(members))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var occurrence = await Serializer.GetAsync<CronTickerOccurrenceEntity<TCronTicker>>(
                CronOccurrenceKey(occurrenceId)).ConfigureAwait(false);
            var slotKey = occurrence == null
                ? CronOccurrenceKey(occurrenceId)
                : CronOccurrenceSlotKey(cronTickerId, occurrence.ExecutionTime);
            var result = await Db.ScriptEvaluateAsync(DeletePendingCronOccurrenceScript,
                [(RedisKey)CronOccurrenceKey(occurrenceId), CronOccurrenceResultKey(occurrenceId),
                 CronOccurrenceIdsKey, CronOccurrencePendingKey, CronOccurrencesByCronKey(cronTickerId),
                 CronOccurrenceRetentionSucceededKey, CronOccurrenceRetentionFailedKey,
                 CronOccurrenceRetentionCancelledKey, CronOccurrenceRetentionSkippedKey,
                 CronOccurrenceRepairQuarantineKey, (RedisKey)slotKey, ActivationMetadataKey],
                [(RedisValue)occurrenceId.ToString(), (RedisValue)cronTickerId.ToString(),
                 (RedisValue)now.ToString("O"), LuaStatusIdle, LuaStatusQueued, LuaStatusSkipped,
                 (RedisValue)"Quarantined because its Cron definition revision is stale.",
                 (RedisValue)ToScore(now)]).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if ((long)result == -3)
                throw new InvalidOperationException(
                    "Redis Cron occurrence quarantine was rejected because legacy partition adoption has started.");
            if ((long)result < 0)
                throw new InvalidDataException(
                    $"Redis cron occurrence '{CronOccurrenceKey(occurrenceId)}' is corrupt; the original value was quarantined and preserved.");
        }
    }

    public async Task<CronTickerEntity[]> GetAllCronTickerExpressions(CancellationToken cancellationToken = default)
    {
        if (!await IsActivationPublicationVisibleAsync(cancellationToken).ConfigureAwait(false)) return [];
        var list = await Serializer.LoadAllFromSetAsync<TCronTicker>(CronIdsKey, CronKey, cancellationToken).ConfigureAwait(false);
        return list
            .Where(c => c.IsEnabled)
            .Select(c => new CronTickerEntity
            {
                Id = c.Id,
                Expression = c.Expression,
                Function = c.Function,
                RequestContractVersion = c.RequestContractVersion,
                RequestContractFingerprint = c.RequestContractFingerprint,
                RetryIntervals = c.RetryIntervals,
                Retries = c.Retries
            })
            .ToArray();
    }
    #endregion

    #region Core_Cron_TickerOccurrence_Methods
    public async Task<CronTickerOccurrenceEntity<TCronTicker>> GetEarliestAvailableCronOccurrence(Guid[] ids, CancellationToken cancellationToken = default)
    {
        if (ids == null || ids.Length == 0) return null;
        if (!await IsActivationPublicationVisibleAsync(cancellationToken).ConfigureAwait(false)) return null;

        var idSet = ids.ToHashSet();
        long cursor = 0;

        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            var scanResult = await Db.SortedSetRangeByScoreAsync(
                CronOccurrencePendingKey,
                double.NegativeInfinity, double.PositiveInfinity,
                Exclude.None, Order.Ascending,
                cursor, 100).ConfigureAwait(false);

            if (scanResult.Length == 0) break;

            var batchIds = ParseGuidSet(scanResult);
            var occurrences = await Serializer.LoadByIdsAsync<CronTickerOccurrenceEntity<TCronTicker>>(batchIds, CronOccurrenceKey, cancellationToken).ConfigureAwait(false);

            foreach (var occurrence in occurrences)
            {
                if (!idSet.Contains(occurrence.CronTickerId)) continue;
                if (!CanAcquire(occurrence.Status, occurrence.LockHolder, LockHolder)) continue;
                var authoritative = await Serializer.GetAsync<TCronTicker>(CronKey(occurrence.CronTickerId)).ConfigureAwait(false);
                if (authoritative == null || occurrence.DefinitionRevision != authoritative.DefinitionRevision)
                    continue;
                occurrence.CronTicker = authoritative;
                return occurrence;
            }

            cursor += scanResult.Length;
        } while (true);

        return null;
    }

    public async IAsyncEnumerable<CronTickerOccurrenceEntity<TCronTicker>> QueueCronTickerOccurrences((DateTime Key, InternalManagerContext[] Items) cronTickerOccurrences, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        EnsureAtomicMigrationTopology();
        var now = Clock.UtcNow;
        var executionTime = cronTickerOccurrences.Key;

        foreach (var item in cronTickerOccurrences.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var authoritative = await Serializer.GetAsync<TCronTicker>(CronKey(item.Id)).ConfigureAwait(false);
            if (authoritative == null)
                continue;
            if (item.NextCronOccurrence is null)
            {
                var occurrence = new CronTickerOccurrenceEntity<TCronTicker>
                {
                    Id = Guid.NewGuid(),
                    Status = TickerStatus.Queued,
                    LockHolder = LockHolder,
                    ExecutionTime = executionTime,
                    CronTickerId = item.Id,
                    DefinitionRevision = authoritative.DefinitionRevision,
                    LockedAt = now,
                    AcquisitionToken = Guid.NewGuid(),
                    CreatedAt = now,
                    UpdatedAt = now,
                    CronTicker = authoritative
                };

                cancellationToken.ThrowIfCancellationRequested();
                var publication = await Db.ScriptEvaluateAsync(AddCronOccurrenceOnceScript,
                    [CronKey(item.Id), CronOccurrenceKey(occurrence.Id),
                        CronOccurrenceSlotKey(item.Id, executionTime), CronOccurrenceIdsKey,
                        CronOccurrencesByCronKey(item.Id), CronOccurrencePendingKey,
                        ActivationMetadataKey, CronOccurrencePendingKey],
                    [(RedisValue)occurrence.Id.ToString(), Serializer.Serialize(occurrence),
                        (RedisValue)ToScore(executionTime), (RedisValue)authoritative.DefinitionRevision,
                        (RedisValue)$"{Prefix}:co:", (RedisValue)item.Id.ToString(), LuaStatusIdle,
                        LuaStatusQueued, (RedisValue)(int)TickerStatus.Done,
                        (RedisValue)RuntimeActivationEpoch,
                        (RedisValue)1, (RedisValue)0, (RedisValue)0])
                    .ConfigureAwait(false);
                var publicationResult = (RedisResult[])publication;
                if ((long)publicationResult[0] is -3 or -2)
                    continue;
                var committedIdText = publicationResult[1].ToString();
                if (!Guid.TryParse(committedIdText, out var committedId))
                    throw new InvalidDataException("Redis recurring-slot publication returned an invalid owner id.");
                if ((long)publicationResult[0] == 1)
                {
                    yield return occurrence;
                    continue;
                }
                var committed = await Serializer.GetAsync<CronTickerOccurrenceEntity<TCronTicker>>(
                    CronOccurrenceKey(committedId)).ConfigureAwait(false);
                if (committed == null || committed.CronTickerId != item.Id ||
                    committed.DefinitionRevision != authoritative.DefinitionRevision)
                    continue;
                committed.CronTicker = authoritative;
                yield return committed;
            }
            else
            {
                var acquired = await TryAcquireAsync<CronTickerOccurrenceEntity<TCronTicker>>(
                    CronOccurrenceKey(item.NextCronOccurrence.Id), CronOccurrenceResultKey(item.NextCronOccurrence.Id),
                    TickerStatus.Queued, authoritativeCronKey: CronKey(item.Id)).ConfigureAwait(false);

                if (acquired == null) continue;

                acquired.ExecutionTime = executionTime;
                acquired.CronTicker = authoritative;
                yield return acquired;
            }
        }
    }

    protected async Task<bool> InsertCronOccurrenceAtomicAsync(
        CronTickerOccurrenceEntity<TCronTicker> occurrence, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureAtomicMigrationTopology();
        var authoritative = await Serializer.GetAsync<TCronTicker>(CronKey(occurrence.CronTickerId))
            .ConfigureAwait(false);
        if (authoritative == null) return false;
        if (occurrence.DefinitionRevision <= 0)
            occurrence.DefinitionRevision = authoritative.DefinitionRevision;
        if (occurrence.DefinitionRevision != authoritative.DefinitionRevision)
            return false;

        var now = Clock.UtcNow;
        var pendingEligible = CanAcquire(occurrence.Status, occurrence.LockHolder, LockHolder);
        RedisKey retentionKey = occurrence.Status switch
        {
            TickerStatus.Done or TickerStatus.DueDone => CronOccurrenceRetentionSucceededKey,
            TickerStatus.Failed => CronOccurrenceRetentionFailedKey,
            TickerStatus.Cancelled => CronOccurrenceRetentionCancelledKey,
            TickerStatus.Skipped => CronOccurrenceRetentionSkippedKey,
            _ => CronOccurrencePendingKey
        };
        var retentionEligible = occurrence.ExecutedAt.HasValue &&
            !occurrence.AcquisitionToken.HasValue &&
            !(occurrence.LeaseUntil is { } leaseUntil && leaseUntil > now) &&
            occurrence.Status is TickerStatus.Done or TickerStatus.DueDone or TickerStatus.Failed or
                TickerStatus.Cancelled or TickerStatus.Skipped;
        var retentionScore = occurrence.ExecutedAt.HasValue
            ? ToScore(occurrence.ExecutedAt.Value)
            : 0d;

        var publication = await Db.ScriptEvaluateAsync(AddCronOccurrenceOnceScript,
            [CronKey(occurrence.CronTickerId), CronOccurrenceKey(occurrence.Id),
                CronOccurrenceSlotKey(occurrence.CronTickerId, occurrence.ExecutionTime),
                CronOccurrenceIdsKey, CronOccurrencesByCronKey(occurrence.CronTickerId),
                CronOccurrencePendingKey, ActivationMetadataKey, retentionKey],
            [(RedisValue)occurrence.Id.ToString(), Serializer.Serialize(occurrence),
                (RedisValue)ToScore(occurrence.ExecutionTime),
                (RedisValue)occurrence.DefinitionRevision, (RedisValue)$"{Prefix}:co:",
                (RedisValue)occurrence.CronTickerId.ToString(), LuaStatusIdle, LuaStatusQueued,
                (RedisValue)(int)TickerStatus.Done,
                (RedisValue)RuntimeActivationEpoch,
                (RedisValue)(pendingEligible ? 1 : 0),
                (RedisValue)(retentionEligible ? 1 : 0),
                (RedisValue)retentionScore]).ConfigureAwait(false);
        var result = (RedisResult[])publication;
        return (long)result[0] == 1;
    }

    public async IAsyncEnumerable<CronTickerOccurrenceEntity<TCronTicker>> QueueTimedOutCronTickerOccurrences([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var threshold = Clock.UtcNow.AddMilliseconds(-100);

        var dueIds = await Db.SortedSetRangeByScoreAsync(CronOccurrencePendingKey, double.NegativeInfinity, ToScore(threshold)).ConfigureAwait(false);
        foreach (var redisValue in dueIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Guid.TryParse(redisValue.ToString(), out var id)) continue;

            var candidate = await Serializer.GetAsync<CronTickerOccurrenceEntity<TCronTicker>>(
                CronOccurrenceKey(id)).ConfigureAwait(false);
            if (candidate == null || candidate.CronTickerId == Guid.Empty)
                continue;
            var acquired = await TryAcquireAsync<CronTickerOccurrenceEntity<TCronTicker>>(
                CronOccurrenceKey(id), CronOccurrenceResultKey(id),
                TickerStatus.InProgress, authoritativeCronKey: CronKey(candidate.CronTickerId)).ConfigureAwait(false);

            if (acquired == null) continue;

            var authoritative = await Serializer.GetAsync<TCronTicker>(
                CronKey(acquired.CronTickerId)).ConfigureAwait(false);
            if (authoritative == null || acquired.DefinitionRevision != authoritative.DefinitionRevision)
                continue;
            acquired.CronTicker = authoritative;

            await IndexManager.AddCronOccurrenceIndexesAsync(acquired).ConfigureAwait(false);
            yield return acquired;
        }
    }

    public async Task UpdateCronTickerOccurrence(InternalFunctionContext functionContext, CancellationToken cancellationToken = default)
    {
        var occurrence = await Serializer.GetAsync<CronTickerOccurrenceEntity<TCronTicker>>(CronOccurrenceKey(functionContext.TickerId)).ConfigureAwait(false);
        if (occurrence == null) return;

        var fenced = IsFencedTerminalWrite(functionContext);
        if (fenced && !functionContext.AcquisitionToken.HasValue) return;
        var expectedUpdatedAt = occurrence.UpdatedAt;
        var expectedStatus = fenced ? (int)occurrence.Status : -1;
        ApplyFunctionContextToCronOccurrence(occurrence, functionContext);
        var resultMutation = GetResultMutation(functionContext);
        var updated = await TryCasReplaceAsync(CronOccurrenceKey(occurrence.Id),
            CronOccurrenceResultKey(occurrence.Id), occurrence, expectedUpdatedAt,
            fenced ? LockHolder : "", fenced ? functionContext.AcquisitionToken : null, expectedStatus,
            resultMutation.Action, resultMutation.Envelope,
            authoritativeCronKey: CronKey(occurrence.CronTickerId))
            .ConfigureAwait(false);
        if (updated != null)
        {
            if (updated.Status is TickerStatus.Done or TickerStatus.DueDone or TickerStatus.Failed
                or TickerStatus.Cancelled or TickerStatus.Skipped)
                await ReleaseCronOccurrenceSlotAsync(updated.Id, updated.CronTickerId, updated.ExecutionTime)
                    .ConfigureAwait(false);
            await IndexManager.AddCronOccurrenceIndexesAsync(updated).ConfigureAwait(false);
        }
    }

    public async Task ReleaseAcquiredCronTickerOccurrences(Guid[] occurrenceIds, CancellationToken cancellationToken = default)
    {
        var ids = occurrenceIds.Length == 0
            ? ParseGuidSet(await Db.SetMembersAsync(CronOccurrenceIdsKey).ConfigureAwait(false))
            : occurrenceIds;

        foreach (var id in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var released = await TryReleaseAsync<CronTickerOccurrenceEntity<TCronTicker>>(
                CronOccurrenceKey(id), CronOccurrenceResultKey(id)).ConfigureAwait(false);
            if (released == null) continue;

            await IndexManager.AddCronOccurrenceIndexesAsync(released).ConfigureAwait(false);
        }
    }

    public async Task<byte[]> GetCronTickerOccurrenceRequest(Guid tickerId, CancellationToken cancellationToken = default)
    {
        var occurrence = await Serializer.GetAsync<CronTickerOccurrenceEntity<TCronTicker>>(CronOccurrenceKey(tickerId)).ConfigureAwait(false);
        if (occurrence == null || occurrence.CronTickerId == Guid.Empty)
            return null;
        if (occurrence.Status is TickerStatus.Done or TickerStatus.DueDone or TickerStatus.Failed
            or TickerStatus.Cancelled or TickerStatus.Skipped)
            return occurrence.CronTicker?.Request ??
                   (await Serializer.GetAsync<TCronTicker>(CronKey(occurrence.CronTickerId)).ConfigureAwait(false))?.Request;
        var authoritative = await Serializer.GetAsync<TCronTicker>(CronKey(occurrence.CronTickerId)).ConfigureAwait(false);
        return authoritative != null && occurrence.DefinitionRevision == authoritative.DefinitionRevision
            ? authoritative.Request
            : null;
    }

    public async Task UpdateCronTickerOccurrencesWithUnifiedContext(Guid[] cronOccurrenceIds, InternalFunctionContext functionContext, CancellationToken cancellationToken = default)
    {
        foreach (var id in cronOccurrenceIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var attempt = 0; attempt < 64; attempt++)
            {
                var occurrence = await Serializer.GetAsync<CronTickerOccurrenceEntity<TCronTicker>>(
                    CronOccurrenceKey(id)).ConfigureAwait(false);
                if (occurrence == null || occurrence.CronTickerId == Guid.Empty) break;
                var expectedUpdatedAt = occurrence.UpdatedAt;
                var expectedStatus = (int)occurrence.Status;
                ApplyFunctionContextToCronOccurrence(occurrence, functionContext);
                var updated = await TryCasReplaceAsync(CronOccurrenceKey(occurrence.Id),
                    CronOccurrenceResultKey(occurrence.Id), occurrence, expectedUpdatedAt,
                    expectedStatus: expectedStatus,
                    authoritativeCronKey: CronKey(occurrence.CronTickerId)).ConfigureAwait(false);
                if (updated == null) continue;
                if (updated.Status is TickerStatus.Done or TickerStatus.DueDone or TickerStatus.Failed
                    or TickerStatus.Cancelled or TickerStatus.Skipped)
                    await ReleaseCronOccurrenceSlotAsync(updated.Id, updated.CronTickerId, updated.ExecutionTime)
                        .ConfigureAwait(false);
                await IndexManager.AddCronOccurrenceIndexesAsync(updated).ConfigureAwait(false);
                break;
            }
        }
    }

    public async Task<Guid[]> TransitionQueuedCronOccurrencesToInProgressAsync(
        IReadOnlyCollection<AcquisitionLease> leases, CancellationToken cancellationToken = default)
    {
        var winners = new List<Guid>();
        foreach (var lease in leases)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (lease.AcquisitionToken is not Guid token) continue;
            var candidate = await Serializer.GetAsync<CronTickerOccurrenceEntity<TCronTicker>>(
                CronOccurrenceKey(lease.TickerId)).ConfigureAwait(false);
            if (candidate == null || candidate.CronTickerId == Guid.Empty) continue;
            var occurrence = await TryTransitionQueuedAsync<CronTickerOccurrenceEntity<TCronTicker>>(
                CronOccurrenceKey(lease.TickerId), token, CronKey(candidate.CronTickerId)).ConfigureAwait(false);
            if (occurrence == null) continue;
            await IndexManager.AddCronOccurrenceIndexesAsync(occurrence).ConfigureAwait(false);
            winners.Add(lease.TickerId);
        }
        return winners.ToArray();
    }

    public async Task ReleaseDeadNodeOccurrenceResources(string instanceIdentifier, CancellationToken cancellationToken = default)
    {
        var members = await Db.SetMembersAsync(CronOccurrenceIdsKey).ConfigureAwait(false);
        var ids = ParseGuidSet(members);

        foreach (var id in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = await Serializer.GetAsync<CronTickerOccurrenceEntity<TCronTicker>>(
                CronOccurrenceKey(id)).ConfigureAwait(false);
            if (candidate == null || candidate.CronTickerId == Guid.Empty) continue;
            var recovered = await TryRecoverDeadNodeAsync<CronTickerOccurrenceEntity<TCronTicker>>(
                CronOccurrenceKey(id), CronOccurrenceResultKey(id), instanceIdentifier,
                CronKey(candidate.CronTickerId)).ConfigureAwait(false);

            if (recovered == null) continue;

            if (recovered.Status == TickerStatus.Skipped)
                await ReleaseCronOccurrenceSlotAsync(recovered.Id, recovered.CronTickerId, recovered.ExecutionTime)
                    .ConfigureAwait(false);
            await IndexManager.AddCronOccurrenceIndexesAsync(recovered).ConfigureAwait(false);
        }
    }
    #endregion

    #region Lease_Recovery
    public async Task<int> RenewTimeTickerLeases(Guid[] ids, DateTime leaseUntil, CancellationToken cancellationToken = default)
    {
        var rows = await Serializer.LoadByIdsAsync<TTimeTicker>(ids ?? [], TimeTickerKey, cancellationToken).ConfigureAwait(false);
        return await RenewTimeTickerLeases(rows.Select(x => new AcquisitionLease(x.Id, x.AcquisitionToken)).ToArray(), leaseUntil, cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> RenewCronTickerOccurrenceLeases(Guid[] ids, DateTime leaseUntil, CancellationToken cancellationToken = default)
    {
        var rows = await Serializer.LoadByIdsAsync<CronTickerOccurrenceEntity<TCronTicker>>(ids ?? [], CronOccurrenceKey, cancellationToken).ConfigureAwait(false);
        return await RenewCronTickerOccurrenceLeases(rows.Select(x => new AcquisitionLease(x.Id, x.AcquisitionToken)).ToArray(), leaseUntil, cancellationToken).ConfigureAwait(false);
    }

    public Task<int> RenewTimeTickerLeases(IReadOnlyCollection<AcquisitionLease> leases, DateTime leaseUntil, CancellationToken cancellationToken = default)
        => RenewLeasesAsync(leases, TimeTickerKey, leaseUntil, cancellationToken);

    public Task<int> RenewCronTickerOccurrenceLeases(IReadOnlyCollection<AcquisitionLease> leases, DateTime leaseUntil, CancellationToken cancellationToken = default)
        => RenewLeasesAsync(leases, CronOccurrenceKey, leaseUntil, cancellationToken);

    private async Task<int> RenewLeasesAsync(IReadOnlyCollection<AcquisitionLease> leases, Func<Guid, string> keyBuilder,
        DateTime leaseUntil, CancellationToken cancellationToken)
    {
        var renewed = 0;
        foreach (var lease in leases ?? Array.Empty<AcquisitionLease>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (lease.AcquisitionToken is not Guid token) continue;
            var result = await Db.ScriptEvaluateAsync(RenewLeaseScript, [(RedisKey)keyBuilder(lease.TickerId)],
                [(RedisValue)LockHolder, (RedisValue)token.ToString(), LuaStatusInProgress,
                 (RedisValue)leaseUntil.ToUniversalTime().ToString("O")]).ConfigureAwait(false);
            if ((long)result == 1) renewed++;
        }
        return renewed;
    }

    public async Task<Guid[]> GetStillHeldTickerIds(Guid[] timeTickerIds, Guid[] occurrenceIds, CancellationToken cancellationToken = default)
    {
        var time = await Serializer.LoadByIdsAsync<TTimeTicker>(timeTickerIds ?? [], TimeTickerKey, cancellationToken).ConfigureAwait(false);
        var cron = await Serializer.LoadByIdsAsync<CronTickerOccurrenceEntity<TCronTicker>>(occurrenceIds ?? [], CronOccurrenceKey, cancellationToken).ConfigureAwait(false);
        return await GetStillHeldTickerIds(
            time.Select(x => new AcquisitionLease(x.Id, x.AcquisitionToken)).ToArray(),
            cron.Select(x => new AcquisitionLease(x.Id, x.AcquisitionToken)).ToArray(), cancellationToken).ConfigureAwait(false);
    }

    public async Task<Guid[]> GetStillHeldTickerIds(IReadOnlyCollection<AcquisitionLease> timeTickerLeases,
        IReadOnlyCollection<AcquisitionLease> occurrenceLeases, CancellationToken cancellationToken = default)
    {
        var held = new List<Guid>();
        await AddHeldAsync(timeTickerLeases, TimeTickerKey, held, cancellationToken).ConfigureAwait(false);
        await AddHeldAsync(occurrenceLeases, CronOccurrenceKey, held, cancellationToken).ConfigureAwait(false);
        return held.ToArray();
    }

    private async Task AddHeldAsync(IReadOnlyCollection<AcquisitionLease> leases, Func<Guid, string> keyBuilder,
        List<Guid> held, CancellationToken cancellationToken)
    {
        foreach (var lease in leases ?? Array.Empty<AcquisitionLease>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (lease.AcquisitionToken is not Guid token) continue;
            var result = await Db.ScriptEvaluateAsync(CheckLeaseScript, [(RedisKey)keyBuilder(lease.TickerId)],
                [(RedisValue)LockHolder, (RedisValue)token.ToString(), LuaStatusInProgress]).ConfigureAwait(false);
            if ((long)result == 1) held.Add(lease.TickerId);
        }
    }

    public async Task<StaleTickerRecoveryResult> RecoverStaleTickers(int maxStaleRestarts, CancellationToken cancellationToken = default)
    {
        var result = new StaleTickerRecoveryResult();
        var timeIds = ParseGuidSet(await Db.SetMembersAsync(TimeTickerIdsKey).ConfigureAwait(false));
        foreach (var id in timeIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var ticker = await Serializer.GetAsync<TTimeTicker>(TimeTickerKey(id)).ConfigureAwait(false);
            if (ticker == null) continue;
            var code = await RecoverStaleAsync(TimeTickerKey(id), TimeTickerResultKey(id),
                ticker.OnStale, maxStaleRestarts).ConfigureAwait(false);
            if (code.Entity is TTimeTicker updated) await IndexManager.AddTimeTickerIndexesAsync(updated).ConfigureAwait(false);
            if (code.Code == 'R') result.RestartedTimeTickers++;
            else if (code.Code == 'C') result.CancelledTimeTickers++;
        }

        var occurrenceIds = ParseGuidSet(await Db.SetMembersAsync(CronOccurrenceIdsKey).ConfigureAwait(false));
        foreach (var id in occurrenceIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var occurrence = await Serializer.GetAsync<CronTickerOccurrenceEntity<TCronTicker>>(CronOccurrenceKey(id)).ConfigureAwait(false);
            if (occurrence == null) continue;
            var policy = occurrence.CronTicker?.OnStale;
            if (!policy.HasValue)
                policy = (await Serializer.GetAsync<TCronTicker>(CronKey(occurrence.CronTickerId)).ConfigureAwait(false))?.OnStale;
            var code = await RecoverStaleAsync(CronOccurrenceKey(id), CronOccurrenceResultKey(id),
                policy ?? StaleAction.Restart, maxStaleRestarts,
                json => Serializer.DeserializeOrNull<CronTickerOccurrenceEntity<TCronTicker>>(json),
                CronKey(occurrence.CronTickerId)).ConfigureAwait(false);
            if (code.Entity is CronTickerOccurrenceEntity<TCronTicker> updated)
            {
                if (updated.Status is TickerStatus.Cancelled or TickerStatus.Skipped)
                    await ReleaseCronOccurrenceSlotAsync(updated.Id, updated.CronTickerId, updated.ExecutionTime)
                        .ConfigureAwait(false);
                await IndexManager.AddCronOccurrenceIndexesAsync(updated).ConfigureAwait(false);
            }
            if (code.Code == 'R') result.RestartedCronOccurrences++;
            else if (code.Code == 'C') result.CancelledCronOccurrences++;
        }
        return result;
    }

    private Task<(char Code, object Entity)> RecoverStaleAsync(
        string key, string resultKey, StaleAction policy, int maxStaleRestarts)
        => RecoverStaleAsync(key, resultKey, policy, maxStaleRestarts,
            json => Serializer.DeserializeOrNull<TTimeTicker>(json));

    private async Task<(char Code, object Entity)> RecoverStaleAsync(string key, string resultKey,
        StaleAction policy, int maxStaleRestarts,
        Func<string, object> deserialize, string authoritativeCronKey = null)
    {
        EnsureReconciliationActivationTopology();
        const string staleReason = "Stale: the node executing this ticker stopped renewing its lease (presumed dead).";
        const string staleRevisionReason = "Quarantined because its Cron definition revision is stale.";
        var now = Clock.UtcNow;
        var keys = authoritativeCronKey == null
            ? [(RedisKey)key, (RedisKey)resultKey, (RedisKey)ActivationMetadataKey]
            : new RedisKey[] { key, resultKey, authoritativeCronKey, ActivationMetadataKey };
        var response = await Db.ScriptEvaluateAsync(RecoverStaleScript,
            keys,
            [(RedisValue)now.ToString("O"),
             (RedisValue)now.Subtract(_schedulerOptions.QueuedLockTimeout).ToString("O"),
             (RedisValue)maxStaleRestarts, (RedisValue)(int)policy,
             LuaStatusIdle, LuaStatusQueued, LuaStatusInProgress, LuaStatusCancelled,
             (RedisValue)staleReason, LuaStatusSkipped, (RedisValue)staleRevisionReason,
             (RedisValue)RuntimeActivationEpoch, RuntimeAdmissionMode]).ConfigureAwait(false);
        if (response.IsNull) return ('\0', null);
        var text = (string)response;
        return (text[0], deserialize(text[2..]));
    }
    #endregion
}
