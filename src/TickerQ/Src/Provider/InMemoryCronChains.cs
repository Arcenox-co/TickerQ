using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Infrastructure;
using TickerQ.Utilities.Models;

namespace TickerQ.Provider;

internal partial class TickerInMemoryPersistenceProvider<TTimeTicker, TCronTicker>
{
    public bool SupportsCronChains => true;
    public Task<CronChainDispatchResult> MaterializeCronChainAsync(InternalFunctionContext context, bool isDue,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!CronChainMaterializer.MatchesPartition(context, _runtimePartitionKey))
            return Task.FromResult(CronChainDispatchResult.LostOwnership);
        return Task.FromResult(WriteGraph(() =>
        {
            if (!CronOccurrences.TryGetValue(context.TickerId, out var occurrence))
                return CronChainDispatchResult.LostOwnership;
            if (occurrence.ChainRootId.HasValue) return CronChainDispatchResult.AlreadyCreated;
            if (!CronTickers.TryGetValue(occurrence.CronTickerId, out var cron) || cron.Chain == null)
                return CronChainDispatchResult.NotConfigured;
            if (!IsStructuralMutationActivationState() || !context.AcquisitionToken.HasValue ||
                occurrence.AcquisitionToken != context.AcquisitionToken || occurrence.LockHolder != _lockHolder ||
                occurrence.Status != TickerStatus.InProgress || occurrence.DefinitionRevision != cron.DefinitionRevision)
                return CronChainDispatchResult.LostOwnership;
            var skip = cron.SkipIfAlreadyRunning &&
                TimeTickers.Values.Any(x => x.OriginCronTickerId == cron.Id && CronChainMaterializer.IsUnfinished(x.Status));
            var now = _clock.UtcNow;
            if (!skip)
            {
                if (TimeTickers.ContainsKey(occurrence.Id)) return CronChainDispatchResult.LostOwnership;
                var root = CronChainMaterializer.Build<TTimeTicker>(cron, occurrence.Id, now);
                AddTickerWithChildren(root, chainRootId: root.Id);
            }
            var completed = CloneCronOccurrence(occurrence);
            CronChainMaterializer.Complete(completed, skip, isDue, now);
            CronOccurrences[occurrence.Id] = completed;
            return skip ? CronChainDispatchResult.Skipped : CronChainDispatchResult.Created;
        }));
    }
}
