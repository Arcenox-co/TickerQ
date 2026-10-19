using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Infrastructure;
using TickerQ.Utilities.Models;
using Microsoft.EntityFrameworkCore;

namespace TickerQ.EntityFrameworkCore.Infrastructure;

internal partial class TickerEfCorePersistenceProvider<TDbContext, TTimeTicker, TCronTicker>
{
    public bool SupportsCronChains => true;
    public async Task<CronChainDispatchResult> MaterializeCronChainAsync(InternalFunctionContext context, bool isDue,
        CancellationToken cancellationToken = default)
    {
        if (!CronChainMaterializer.MatchesPartition(context, _runtimePartitionKey)) return CronChainDispatchResult.LostOwnership;
        // Keep ordinary cron execution off the graph's write fence. A saved launch receipt
        // wins even if an administrator has subsequently removed the template.
        using (var readSession = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            var read = readSession.Context;
            var receipt = await read.Set<CronTickerOccurrenceEntity<TCronTicker>>().AsNoTracking()
                .SingleOrDefaultAsync(x => x.ApplicationNamespaceKey == _runtimePartitionKey && x.Id == context.TickerId, cancellationToken);
            if (receipt == null) return CronChainDispatchResult.LostOwnership;
            if (receipt.ChainRootId.HasValue) return CronChainDispatchResult.AlreadyCreated;
            if (!await read.Set<TCronTicker>().AnyAsync(x => x.ApplicationNamespaceKey == _runtimePartitionKey &&
                x.Id == receipt.CronTickerId && x.Chain != null, cancellationToken)) return CronChainDispatchResult.NotConfigured;
        }
        return await ExecuteTimeTickerGraphMutationAsync(true, CronChainDispatchResult.LostOwnership, async (db, ct) =>
        {
            var occurrence = await db.Set<CronTickerOccurrenceEntity<TCronTicker>>()
                .SingleOrDefaultAsync(x => x.ApplicationNamespaceKey == _runtimePartitionKey && x.Id == context.TickerId, ct);
            if (occurrence == null) return CronChainDispatchResult.LostOwnership;
            if (occurrence.ChainRootId.HasValue) return CronChainDispatchResult.AlreadyCreated;
            var cron = await db.Set<TCronTicker>()
                .SingleOrDefaultAsync(x => x.ApplicationNamespaceKey == _runtimePartitionKey && x.Id == occurrence.CronTickerId, ct);
            if (cron?.Chain == null) return CronChainDispatchResult.NotConfigured;
            if (!context.AcquisitionToken.HasValue || occurrence.AcquisitionToken != context.AcquisitionToken ||
                occurrence.LockHolder != _lockHolder || occurrence.Status != TickerStatus.InProgress ||
                occurrence.DefinitionRevision != cron.DefinitionRevision)
                return CronChainDispatchResult.LostOwnership;
            var skip = cron.SkipIfAlreadyRunning &&
                await db.Set<TTimeTicker>().AnyAsync(x => x.ApplicationNamespaceKey == _runtimePartitionKey &&
                    x.OriginCronTickerId == cron.Id &&
                    (x.Status == TickerStatus.Idle || x.Status == TickerStatus.Queued || x.Status == TickerStatus.InProgress), ct);
            var now = _clock.UtcNow;
            if (!skip)
            {
                var root = CronChainMaterializer.Build<TTimeTicker>(cron, occurrence.Id, now);
                NormalizeChainRoot(root, root.Id);
                db.Set<TTimeTicker>().Add(root);
            }
            CronChainMaterializer.Complete(occurrence, skip, isDue, now);
            await db.SaveChangesAsync(ct);
            return skip ? CronChainDispatchResult.Skipped : CronChainDispatchResult.Created;
        }, cancellationToken).ConfigureAwait(false);
    }
}
