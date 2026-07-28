#nullable disable
using System.Runtime.CompilerServices;
using System.Text.Json;
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
    private readonly SchedulerOptionsBuilder _schedulerOptions;

    // Lua status constants matching TickerStatus enum values
    private static readonly RedisValue LuaStatusIdle = (int)TickerStatus.Idle;
    private static readonly RedisValue LuaStatusQueued = (int)TickerStatus.Queued;
    private static readonly RedisValue LuaStatusInProgress = (int)TickerStatus.InProgress;
    private static readonly RedisValue LuaStatusCancelled = (int)TickerStatus.Cancelled;

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
        LockHolder = _schedulerOptions.ExecutionOwnerId;

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
        IndexManager = new RedisIndexManager<TTimeTicker, TCronTicker>(db, LockHolder, Clock);
    }

    public bool SupportsLeaseBasedRecovery => true;

    #region Lua script operations
    protected async Task<T> TryAcquireAsync<T>(string key, string resultKey, TickerStatus targetStatus, string expectedUpdatedAt = "") where T : class
    {
        var now = Clock.UtcNow;
        var acquisitionToken = Guid.NewGuid();
        var result = await Db.ScriptEvaluateAsync(
            AcquireScript,
            [(RedisKey)key, (RedisKey)resultKey],
            [(RedisValue)LockHolder, (RedisValue)now.ToString("O"), (RedisValue)(int)targetStatus,
             (RedisValue)(expectedUpdatedAt ?? ""), LuaStatusIdle, LuaStatusQueued,
             (RedisValue)acquisitionToken.ToString(),
             (RedisValue)(targetStatus == TickerStatus.InProgress
                 ? now.Add(_schedulerOptions.LeaseDuration).ToString("O")
                 : ""),
             (RedisValue)(key.StartsWith($"{Prefix}:tt:", StringComparison.Ordinal) ? $"{Prefix}:tt:" : "")]
        ).ConfigureAwait(false);

        if (result.IsNull) return null;
        return Serializer.DeserializeOrNull<T>((string)result);
    }

    protected async Task<T> TryTransitionQueuedAsync<T>(string key, Guid acquisitionToken) where T : class
    {
        var now = Clock.UtcNow;
        var result = await Db.ScriptEvaluateAsync(
            TransitionQueuedScript,
            [(RedisKey)key],
            [(RedisValue)LockHolder, (RedisValue)acquisitionToken.ToString(),
             (RedisValue)now.ToString("O"), LuaStatusQueued, LuaStatusInProgress,
             (RedisValue)now.Add(_schedulerOptions.LeaseDuration).ToString("O")]
        ).ConfigureAwait(false);

        if (result.IsNull) return null;
        return Serializer.DeserializeOrNull<T>((string)result);
    }

    protected async Task<T> TryReleaseAsync<T>(string key, string resultKey) where T : class
    {
        var now = Clock.UtcNow;
        var result = await Db.ScriptEvaluateAsync(
            ReleaseScript,
            [(RedisKey)key, (RedisKey)resultKey],
            [(RedisValue)LockHolder, (RedisValue)now.ToString("O"), LuaStatusIdle, LuaStatusQueued]
        ).ConfigureAwait(false);

        if (result.IsNull) return null;
        return Serializer.DeserializeOrNull<T>((string)result);
    }

    protected async Task<T> TryRecoverDeadNodeAsync<T>(string key, string resultKey, string deadNodeId) where T : class
    {
        var now = Clock.UtcNow;
        var result = await Db.ScriptEvaluateAsync(
            RecoverDeadNodeScript,
            [(RedisKey)key, (RedisKey)resultKey],
            [(RedisValue)deadNodeId, (RedisValue)now.ToString("O"), LuaStatusIdle, LuaStatusQueued, LuaStatusInProgress]
        ).ConfigureAwait(false);

        if (result.IsNull) return null;
        return Serializer.DeserializeOrNull<T>((string)result);
    }

    private async Task<T> TryCasReplaceAsync<T>(string key, string resultKey, T replacement, DateTime expectedUpdatedAt,
        string expectedHolder = "", Guid? expectedToken = null, int expectedStatus = -1,
        string resultAction = "none", byte[] resultEnvelope = null, Guid? embeddedTargetId = null) where T : class
    {
        var result = await EvaluateCasReplaceAsync(key, resultKey, replacement, expectedUpdatedAt,
            expectedHolder, expectedToken, expectedStatus, resultAction, resultEnvelope, embeddedTargetId)
            .ConfigureAwait(false);
        return result.IsNull ? null : Serializer.DeserializeOrNull<T>((string)result);
    }

    private async Task<bool> TryAcknowledgeCasReplaceAsync<T>(string key, string resultKey, T replacement,
        DateTime expectedUpdatedAt, string expectedHolder, Guid? expectedToken, int expectedStatus,
        string resultAction, byte[] resultEnvelope, Guid? embeddedTargetId = null) where T : class
    {
        var result = await EvaluateCasReplaceAsync(key, resultKey, replacement, expectedUpdatedAt,
            expectedHolder, expectedToken, expectedStatus, resultAction, resultEnvelope, embeddedTargetId)
            .ConfigureAwait(false);
        return !result.IsNull;
    }

    private Task<RedisResult> EvaluateCasReplaceAsync<T>(string key, string resultKey, T replacement,
        DateTime expectedUpdatedAt, string expectedHolder, Guid? expectedToken, int expectedStatus,
        string resultAction, byte[] resultEnvelope, Guid? embeddedTargetId) where T : class
        => Db.ScriptEvaluateAsync(CasReplaceScript, [(RedisKey)key, (RedisKey)resultKey],
            [(RedisValue)expectedHolder, (RedisValue)(expectedToken?.ToString() ?? ""),
             (RedisValue)expectedStatus, (RedisValue)expectedUpdatedAt.ToString("O"),
             (RedisValue)Serializer.Serialize(replacement), (RedisValue)resultAction,
             (RedisValue)(resultEnvelope ?? []),
             (RedisValue)(embeddedTargetId?.ToString() ?? "")]);

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
        if (functionContext == null)
            throw new ArgumentNullException(nameof(functionContext));
        if (!functionContext.GetPropsToUpdate().Contains(nameof(InternalFunctionContext.Status)) ||
            functionContext.Status is not (TickerStatus.Done or TickerStatus.DueDone) ||
            !functionContext.GetPropsToUpdate().Contains(nameof(InternalFunctionContext.ResultEnvelope)))
            throw new InvalidOperationException(
                "Atomic result persistence accepts only a successful terminal mutation with an explicit optional result envelope.");

        cancellationToken.ThrowIfCancellationRequested();
        // Serialize before reading or invoking Lua so malformed/oversized envelopes cannot mutate status.
        var encodedEnvelope = functionContext.ResultEnvelope == null
            ? null
            : RedisResultEnvelopeCodec.Serialize(functionContext.ResultEnvelope);
        var resultAction = encodedEnvelope == null ? "clear" : "set";

        return functionContext.Type == TickerType.CronTickerOccurrence
            ? await CommitSuccessfulCronOccurrenceAsync(
                functionContext, resultAction, encodedEnvelope, cancellationToken).ConfigureAwait(false)
            : await CommitSuccessfulTimeTickerAsync(
                functionContext, resultAction, encodedEnvelope, cancellationToken).ConfigureAwait(false);
    }

    public bool SupportsAcknowledgedTerminalUpdates => true;

    public async Task<bool> CommitTerminalTickerAsync(
        InternalFunctionContext functionContext, CancellationToken cancellationToken = default)
    {
        if (functionContext == null) throw new ArgumentNullException(nameof(functionContext));
        if (!functionContext.GetPropsToUpdate().Contains(nameof(InternalFunctionContext.Status)) ||
            functionContext.Status is not (TickerStatus.Done or TickerStatus.DueDone or
                TickerStatus.Failed or TickerStatus.Cancelled or TickerStatus.Skipped))
            throw new InvalidOperationException("Acknowledged terminal persistence requires a terminal status mutation.");
        if (functionContext.Status is TickerStatus.Done or TickerStatus.DueDone)
            return await CommitSuccessfulTickerAsync(functionContext, cancellationToken).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        return functionContext.Type == TickerType.CronTickerOccurrence
            ? await CommitSuccessfulCronOccurrenceAsync(
                functionContext, "none", null, cancellationToken).ConfigureAwait(false)
            : await CommitSuccessfulTimeTickerAsync(
                functionContext, "none", null, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> CommitSuccessfulTimeTickerAsync(
        InternalFunctionContext functionContext, string resultAction, byte[] encodedEnvelope,
        CancellationToken cancellationToken)
    {
        if (functionContext.ParentId != null)
            return await CommitSuccessfulEmbeddedTimeTickerAsync(
                functionContext, resultAction, encodedEnvelope, cancellationToken).ConfigureAwait(false);

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
            LockHolder, functionContext.AcquisitionToken, expectedStatus, resultAction, encodedEnvelope)
            .ConfigureAwait(false);
        if (!acknowledged)
            return false;

        await IndexManager.AddTimeTickerIndexesAsync(ticker).ConfigureAwait(false);
        return true;
    }

    private async Task<bool> CommitSuccessfulEmbeddedTimeTickerAsync(
        InternalFunctionContext functionContext, string resultAction, byte[] encodedEnvelope,
        CancellationToken cancellationToken)
    {
        var rootId = functionContext.ChainRootId ?? functionContext.ParentId;
        if (!rootId.HasValue || !functionContext.AcquisitionToken.HasValue)
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
            LockHolder, functionContext.AcquisitionToken, expectedStatus, resultAction, encodedEnvelope,
            functionContext.TickerId).ConfigureAwait(false);
        if (!acknowledged)
            return false;

        await IndexManager.AddTimeTickerIndexesAsync(root).ConfigureAwait(false);
        return true;
    }

    private async Task<bool> CommitSuccessfulCronOccurrenceAsync(
        InternalFunctionContext functionContext, string resultAction, byte[] encodedEnvelope,
        CancellationToken cancellationToken)
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
            resultAction, encodedEnvelope).ConfigureAwait(false);
        if (!acknowledged)
            return false;

        await IndexManager.AddCronOccurrenceIndexesAsync(occurrence).ConfigureAwait(false);
        return true;
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
        var rootId = functionContext.ChainRootId ?? functionContext.ParentId;
        if (!rootId.HasValue) return 0;

        for (var attempt = 0; attempt < 64; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var root = await Serializer.GetAsync<TTimeTicker>(TimeTickerKey(rootId.Value)).ConfigureAwait(false);
            if (root == null) return 0;

            var child = FindEmbeddedTimeTicker(root.Children, functionContext.TickerId);
            if (child == null) return 0;

            var expectedUpdatedAt = root.UpdatedAt;
            var fenced = IsFencedTerminalWrite(functionContext);
            if (fenced && !functionContext.AcquisitionToken.HasValue) return 0;
            var expectedStatus = fenced ? (int)child.Status : -1;
            ApplyFunctionContextToTicker(child, functionContext);
            root.UpdatedAt = NextAggregateUpdatedAt(expectedUpdatedAt);
            var resultMutation = GetResultMutation(functionContext);
            var updated = await TryCasReplaceAsync(TimeTickerKey(root.Id),
                    TimeTickerResultKey(functionContext.TickerId), root, expectedUpdatedAt,
                    expectedHolder: fenced ? LockHolder : "",
                    expectedToken: fenced ? functionContext.AcquisitionToken : null,
                    expectedStatus: expectedStatus,
                    resultAction: resultMutation.Action, resultEnvelope: resultMutation.Envelope,
                    embeddedTargetId: functionContext.TickerId)
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

        foreach (var ticker in tickers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ApplyFunctionContextToTicker(ticker, functionContext);
            await Serializer.SetAsync(TimeTickerKey(ticker.Id), ticker).ConfigureAwait(false);
            await IndexManager.AddTimeTickerIndexesAsync(ticker).ConfigureAwait(false);
        }
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
        var now = Clock.UtcNow;
        var token = Guid.NewGuid();
        var result = await Db.ScriptEvaluateAsync(AcquireOnDemandScript,
            [(RedisKey)TimeTickerKey(id), (RedisKey)TimeTickerResultKey(id)],
            [(RedisValue)LockHolder, (RedisValue)now.ToString("O"),
             (RedisValue)now.Add(_schedulerOptions.LeaseDuration).ToString("O"),
             (RedisValue)token.ToString(), LuaStatusInProgress, (RedisValue)executionTime.ToUniversalTime().ToString("O"),
             LuaStatusQueued])
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
    public async Task MigrateDefinedCronTickers(DefinedCronTickerSeed[] cronTickers, CancellationToken cancellationToken = default)
    {
        var now = Clock.UtcNow;
        const string seedPrefix = "MemoryTicker_Seeded_";

        var allRegisteredFunctions = TickerFunctionProvider.TickerFunctions.Keys
            .ToHashSet(StringComparer.Ordinal);
        var blockedFunctions = cronTickers.Where(x => !x.CanSeed)
            .Select(x => x.Function).ToHashSet(StringComparer.Ordinal);

        var existingList = await Serializer.LoadAllFromSetAsync<TCronTicker>(CronIdsKey, CronKey, cancellationToken).ConfigureAwait(false);

        var orphanedToDelete = existingList
            .Where(c => !string.IsNullOrEmpty(c.InitIdentifier)
                && (!allRegisteredFunctions.Contains(c.Function)
                    || blockedFunctions.Contains(c.Function)))
            .Select(c => c.Id).ToArray();

        foreach (var id in orphanedToDelete)
        {
            await IndexManager.RemoveCronIndexesAsync(id).ConfigureAwait(false);
            await IndexManager.RemoveCronOccurrencesByParentAsync(id).ConfigureAwait(false);
            await Db.KeyDeleteAsync(CronKey(id)).ConfigureAwait(false);
        }

        // Match only SEEDED rows (non-empty InitIdentifier). A user/dashboard-created
        // row sharing a function name carries a null/non-seed identity and must never be
        // matched, expression-overwritten, or identity-stamped by seed reconciliation.
        var orphanedSet = orphanedToDelete.ToHashSet();
        var existingByFunction = existingList
            .Where(c => !orphanedSet.Contains(c.Id) && !string.IsNullOrEmpty(c.InitIdentifier))
            .ToDictionary(c => c.Function, c => c, StringComparer.Ordinal);

        foreach (var seed in cronTickers)
        {
            if (!seed.CanSeed)
                continue;

            if (existingByFunction.TryGetValue(seed.Function, out var cron))
            {
                var changed = false;

                if (!string.Equals(cron.Expression, seed.Expression, StringComparison.Ordinal))
                {
                    cron.Expression = seed.Expression;
                    changed = true;
                }

                // Reconcile authoritative contract identity onto seeded rows only; legacy/dashboard
                // rows (no seed InitIdentifier) keep their own identity.
                if (!string.IsNullOrEmpty(cron.InitIdentifier)
                    && !seed.MatchesIdentity(cron.RequestContractVersion, cron.RequestContractFingerprint))
                {
                    cron.RequestContractVersion = seed.RequestContractVersion;
                    cron.RequestContractFingerprint = seed.RequestContractFingerprint;
                    changed = true;
                }

                if (changed)
                {
                    cron.UpdatedAt = now;
                    await Serializer.SetAsync(CronKey(cron.Id), cron).ConfigureAwait(false);
                }
            }
            else
            {
                var entity = new TCronTicker
                {
                    Id = Guid.NewGuid(),
                    Function = seed.Function,
                    Expression = seed.Expression,
                    InitIdentifier = $"{seedPrefix}{seed.Function}",
                    CreatedAt = now,
                    UpdatedAt = now,
                    Request = [],
                    RequestContractVersion = seed.RequestContractVersion,
                    RequestContractFingerprint = seed.RequestContractFingerprint
                };
                await Serializer.SetAsync(CronKey(entity.Id), entity).ConfigureAwait(false);
                await IndexManager.AddCronIndexesAsync(entity).ConfigureAwait(false);
            }
        }
    }

    public async Task<CronTickerEntity[]> GetAllCronTickerExpressions(CancellationToken cancellationToken = default)
    {
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
                return occurrence;
            }

            cursor += scanResult.Length;
        } while (true);

        return null;
    }

    public async IAsyncEnumerable<CronTickerOccurrenceEntity<TCronTicker>> QueueCronTickerOccurrences((DateTime Key, InternalManagerContext[] Items) cronTickerOccurrences, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var now = Clock.UtcNow;
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
                    LockHolder = LockHolder,
                    ExecutionTime = executionTime,
                    CronTickerId = item.Id,
                    LockedAt = now,
                    AcquisitionToken = Guid.NewGuid(),
                    CreatedAt = now,
                    UpdatedAt = now,
                    CronTicker = new TCronTicker
                    {
                        Id = item.Id,
                        Function = item.FunctionName,
                        RequestContractVersion = item.RequestContractVersion,
                        RequestContractFingerprint = item.RequestContractFingerprint,
                        Expression = item.Expression,
                        Retries = item.Retries,
                        RetryIntervals = item.RetryIntervals
                    }
                };

                await Serializer.SetAsync(CronOccurrenceKey(occurrence.Id), occurrence).ConfigureAwait(false);
                await IndexManager.AddCronOccurrenceIndexesAsync(occurrence).ConfigureAwait(false);
                yield return occurrence;
            }
            else
            {
                var acquired = await TryAcquireAsync<CronTickerOccurrenceEntity<TCronTicker>>(
                    CronOccurrenceKey(item.NextCronOccurrence.Id), CronOccurrenceResultKey(item.NextCronOccurrence.Id),
                    TickerStatus.Queued).ConfigureAwait(false);

                if (acquired == null) continue;

                acquired.ExecutionTime = executionTime;

                if (acquired.CronTicker == null)
                {
                    acquired.CronTicker = new TCronTicker
                    {
                        Id = item.Id,
                        Function = item.FunctionName,
                        RequestContractVersion = item.RequestContractVersion,
                        RequestContractFingerprint = item.RequestContractFingerprint,
                        Expression = item.Expression,
                        Retries = item.Retries,
                        RetryIntervals = item.RetryIntervals
                    };
                }

                await Serializer.SetAsync(CronOccurrenceKey(acquired.Id), acquired).ConfigureAwait(false);
                await IndexManager.AddCronOccurrenceIndexesAsync(acquired).ConfigureAwait(false);
                yield return acquired;
            }
        }
    }

    public async IAsyncEnumerable<CronTickerOccurrenceEntity<TCronTicker>> QueueTimedOutCronTickerOccurrences([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var threshold = Clock.UtcNow.AddMilliseconds(-100);

        var dueIds = await Db.SortedSetRangeByScoreAsync(CronOccurrencePendingKey, double.NegativeInfinity, ToScore(threshold)).ConfigureAwait(false);
        foreach (var redisValue in dueIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Guid.TryParse(redisValue.ToString(), out var id)) continue;

            var acquired = await TryAcquireAsync<CronTickerOccurrenceEntity<TCronTicker>>(
                CronOccurrenceKey(id), CronOccurrenceResultKey(id),
                TickerStatus.InProgress).ConfigureAwait(false);

            if (acquired == null) continue;

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
            resultMutation.Action, resultMutation.Envelope)
            .ConfigureAwait(false);
        if (updated != null)
            await IndexManager.AddCronOccurrenceIndexesAsync(updated).ConfigureAwait(false);
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
        return occurrence?.CronTicker?.Request;
    }

    public async Task UpdateCronTickerOccurrencesWithUnifiedContext(Guid[] cronOccurrenceIds, InternalFunctionContext functionContext, CancellationToken cancellationToken = default)
    {
        var occurrences = await Serializer.LoadByIdsAsync<CronTickerOccurrenceEntity<TCronTicker>>(cronOccurrenceIds, CronOccurrenceKey, cancellationToken).ConfigureAwait(false);

        foreach (var occurrence in occurrences)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ApplyFunctionContextToCronOccurrence(occurrence, functionContext);
            await Serializer.SetAsync(CronOccurrenceKey(occurrence.Id), occurrence).ConfigureAwait(false);
            await IndexManager.AddCronOccurrenceIndexesAsync(occurrence).ConfigureAwait(false);
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
            var occurrence = await TryTransitionQueuedAsync<CronTickerOccurrenceEntity<TCronTicker>>(
                CronOccurrenceKey(lease.TickerId), token).ConfigureAwait(false);
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

            var recovered = await TryRecoverDeadNodeAsync<CronTickerOccurrenceEntity<TCronTicker>>(
                CronOccurrenceKey(id), CronOccurrenceResultKey(id), instanceIdentifier).ConfigureAwait(false);

            if (recovered == null) continue;

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
                json => Serializer.DeserializeOrNull<CronTickerOccurrenceEntity<TCronTicker>>(json)).ConfigureAwait(false);
            if (code.Entity is CronTickerOccurrenceEntity<TCronTicker> updated)
                await IndexManager.AddCronOccurrenceIndexesAsync(updated).ConfigureAwait(false);
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
        Func<string, object> deserialize)
    {
        const string staleReason = "Stale: the node executing this ticker stopped renewing its lease (presumed dead).";
        var now = Clock.UtcNow;
        var response = await Db.ScriptEvaluateAsync(RecoverStaleScript,
            [(RedisKey)key, (RedisKey)resultKey],
            [(RedisValue)now.ToString("O"),
             (RedisValue)now.Subtract(_schedulerOptions.QueuedLockTimeout).ToString("O"),
             (RedisValue)maxStaleRestarts, (RedisValue)(int)policy,
             LuaStatusIdle, LuaStatusQueued, LuaStatusInProgress, LuaStatusCancelled,
             (RedisValue)staleReason]).ConfigureAwait(false);
        if (response.IsNull) return ('\0', null);
        var text = (string)response;
        return (text[0], deserialize(text[2..]));
    }
    #endregion
}
