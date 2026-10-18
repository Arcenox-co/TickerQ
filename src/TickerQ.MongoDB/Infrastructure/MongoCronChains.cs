using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Infrastructure;
using TickerQ.Utilities.Models;
using MongoDB.Driver;

namespace TickerQ.MongoDB.Infrastructure;

internal partial class TickerMongoPersistenceProvider<TTimeTicker, TCronTicker>
{
    public bool SupportsCronChains => !TransactionsKnownUnavailable;
    public async Task<CronChainDispatchResult> MaterializeCronChainAsync(InternalFunctionContext context, bool isDue,
        CancellationToken cancellationToken = default)
    {
        if (!CronChainMaterializer.MatchesPartition(context, _runtimePartitionKey)) return CronChainDispatchResult.LostOwnership;
        var readFilter = Builders<CronTickerOccurrenceEntity<TCronTicker>>.Filter.And(
            Builders<CronTickerOccurrenceEntity<TCronTicker>>.Filter.Eq(x => x.ApplicationNamespaceKey, _runtimePartitionKey),
            Builders<CronTickerOccurrenceEntity<TCronTicker>>.Filter.Eq(x => x.Id, context.TickerId));
        var receipt = await _context.CronTickerOccurrences.Find(readFilter).FirstOrDefaultAsync(cancellationToken);
        if (receipt == null) return CronChainDispatchResult.LostOwnership;
        if (receipt.ChainRootId.HasValue) return CronChainDispatchResult.AlreadyCreated;
        var definition = await _context.CronTickers.Find(InPartition(Builders<TCronTicker>.Filter.Eq(x => x.Id, receipt.CronTickerId)))
            .FirstOrDefaultAsync(cancellationToken);
        if (definition?.Chain == null) return CronChainDispatchResult.NotConfigured;
        var outcome = CronChainDispatchResult.LostOwnership;
        var committed = await ExecuteAdmittedGraphMutationAsync(async (session, ct) =>
        {
            var occurrenceFilter = Builders<CronTickerOccurrenceEntity<TCronTicker>>.Filter.And(
                Builders<CronTickerOccurrenceEntity<TCronTicker>>.Filter.Eq(x => x.ApplicationNamespaceKey, _runtimePartitionKey),
                Builders<CronTickerOccurrenceEntity<TCronTicker>>.Filter.Eq(x => x.Id, context.TickerId));
            var occurrence = await _context.CronTickerOccurrences.Find(session, occurrenceFilter).FirstOrDefaultAsync(ct);
            if (occurrence == null) { outcome = CronChainDispatchResult.LostOwnership; return 0; }
            if (occurrence.ChainRootId.HasValue) { outcome = CronChainDispatchResult.AlreadyCreated; return 0; }
            var cronFilter = InPartition(Builders<TCronTicker>.Filter.Eq(x => x.Id, occurrence.CronTickerId));
            var cron = await _context.CronTickers.Find(session, cronFilter).FirstOrDefaultAsync(ct);
            if (cron?.Chain == null) { outcome = CronChainDispatchResult.NotConfigured; return 0; }
            if (!context.AcquisitionToken.HasValue || occurrence.AcquisitionToken != context.AcquisitionToken ||
                occurrence.LockHolder != _lockHolder || occurrence.Status != TickerStatus.InProgress ||
                occurrence.DefinitionRevision != cron.DefinitionRevision)
            { outcome = CronChainDispatchResult.LostOwnership; return 0; }
            var now = _clock.UtcNow;
            // Write the definition too: concurrent edits/deletion must conflict with this snapshot.
            await _context.CronTickers.UpdateOneAsync(session, cronFilter,
                Builders<TCronTicker>.Update.Set(x => x.UpdatedAt, now), cancellationToken: ct);
            var activeFilter = InPartition(Builders<TTimeTicker>.Filter.And(
                Builders<TTimeTicker>.Filter.Eq(x => x.OriginCronTickerId, cron.Id),
                Builders<TTimeTicker>.Filter.In(x => x.Status, new[] { TickerStatus.Idle, TickerStatus.Queued, TickerStatus.InProgress })));
            var skip = cron.SkipIfAlreadyRunning &&
                await _context.TimeTickers.Find(session, activeFilter).AnyAsync(ct);
            if (!skip)
            {
                var root = CronChainMaterializer.Build<TTimeTicker>(cron, occurrence.Id, now);
                await InsertWithChildren(session, root, null, root.Id, null, ct);
            }
            CronChainMaterializer.Complete(occurrence, skip, isDue, now);
            await _context.CronTickerOccurrences.ReplaceOneAsync(session, occurrenceFilter, occurrence, cancellationToken: ct);
            outcome = skip ? CronChainDispatchResult.Skipped : CronChainDispatchResult.Created;
            return 1;
        }, _ => throw new NotSupportedException("Recurring chains require MongoDB transactions (a replica set or sharded cluster)."), cancellationToken);
        return committed == 0 && outcome is CronChainDispatchResult.Created or CronChainDispatchResult.Skipped
            ? CronChainDispatchResult.LostOwnership : outcome;
    }
}
