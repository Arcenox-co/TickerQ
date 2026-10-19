#nullable disable
using StackExchange.Redis;
using static TickerQ.Caching.StackExchangeRedis.Helpers.RedisKeyBuilder;
using TickerQ.Caching.StackExchangeRedis.Helpers;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Infrastructure;
using TickerQ.Utilities.Models;

namespace TickerQ.Caching.StackExchangeRedis.Infrastructure;

internal sealed partial class TickerRedisPersistenceProvider<TTimeTicker, TCronTicker>
{
    private static readonly string MaterializeCronChainScript = LuaScriptLoader.Load("MaterializeCronChain");
    public bool SupportsCronChains => !IsClusterTopology(Db);
    public async Task<CronChainDispatchResult> MaterializeCronChainAsync(InternalFunctionContext context, bool isDue,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!CronChainMaterializer.MatchesPartition(context, Keys.Partition.StorageKey)) return CronChainDispatchResult.LostOwnership;
        var occurrenceJson = await Db.StringGetAsync(CronOccurrenceKey(context.TickerId));
        var occurrence = Serializer.DeserializeOrNull<CronTickerOccurrenceEntity<TCronTicker>>(occurrenceJson);
        if (occurrence == null) return CronChainDispatchResult.LostOwnership;
        if (occurrence.ChainRootId.HasValue) return CronChainDispatchResult.AlreadyCreated;
        var cronJson = await Db.StringGetAsync(CronKey(occurrence.CronTickerId));
        var cron = Serializer.DeserializeOrNull<TCronTicker>(cronJson);
        if (cron?.Chain == null) return CronChainDispatchResult.NotConfigured;
        if (!SupportsCronChains) throw new NotSupportedException("Recurring chain execution requires standalone Redis.");
        if (RuntimeAdmissionMisconfigured || !context.AcquisitionToken.HasValue ||
            occurrence.AcquisitionToken != context.AcquisitionToken || occurrence.LockHolder != LockHolder ||
            occurrence.Status != TickerStatus.InProgress || occurrence.DefinitionRevision != cron.DefinitionRevision)
            return CronChainDispatchResult.LostOwnership;
        var now = Clock.UtcNow;
        var root = CronChainMaterializer.Build<TTimeTicker>(cron, occurrence.Id, now);
        NormalizeAggregate(root, root.Id, null);
        CronChainMaterializer.Complete(occurrence, false, isDue, now);
        var completeJson = Serializer.Serialize(occurrence);
        CronChainMaterializer.Complete(occurrence, true, isDue, now);
        var skippedJson = Serializer.Serialize(occurrence);
        var result = (int)(long)await Db.ScriptEvaluateAsync(MaterializeCronChainScript,
            new RedisKey[] { CronOccurrenceKey(occurrence.Id), CronKey(cron.Id), TimeTickerKey(root.Id),
                TimeTickerIdsKey, TimeTickerPendingKey, CronOccurrencePendingKey, RuntimeActivationMetadataKey,
                $"{Prefix}:cron:{cron.Id}:active-chains", CronOccurrenceRetentionSucceededKey, CronOccurrenceRetentionSkippedKey },
            new RedisValue[] { occurrenceJson, cronJson, Serializer.Serialize(root), completeJson, skippedJson,
                root.Id.ToString(), ToScore(now), cron.SkipIfAlreadyRunning ? 1 : 0, RuntimeActivationEpoch, RuntimeAdmissionMode });
        if (result == (int)CronChainDispatchResult.Created)
            await IndexManager.AddTimeTickerIndexesAsync(root).ConfigureAwait(false);
        return (CronChainDispatchResult)result;
    }
}
