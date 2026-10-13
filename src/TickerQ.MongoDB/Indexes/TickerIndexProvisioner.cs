using System.Threading;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Driver;
using TickerQ.MongoDB.Infrastructure;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces;

namespace TickerQ.MongoDB.Indexes
{
    internal sealed class TickerIndexProvisioner<TTimeTicker, TCronTicker> :
        ITickerQPersistenceBootstrapper,
        ITickerQPersistenceFinalizer,
        ITickerQPersistenceReadinessProbe
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        private readonly ITickerMongoContext<TTimeTicker, TCronTicker> _context;
        private readonly TickerMongoPersistenceProvider<TTimeTicker, TCronTicker> _provider;
        private static readonly TransactionOptions ProbeTransactionOptions = new(
            readConcern: ReadConcern.Snapshot,
            writeConcern: WriteConcern.WMajority);

        public TickerIndexProvisioner(
            ITickerMongoContext<TTimeTicker, TCronTicker> context,
            TickerMongoPersistenceProvider<TTimeTicker, TCronTicker> provider)
        {
            _context = context;
            _provider = provider;
        }

        public async Task BootstrapAsync(CancellationToken cancellationToken = default)
        {
            var fence = _context.Database.GetCollection<BsonDocument>(
                _context.TimeTickers.CollectionNamespace.CollectionName + "_GraphFence");
            await fence.UpdateOneAsync(
                Builders<BsonDocument>.Filter.Eq("_id", "time-ticker-graph"),
                Builders<BsonDocument>.Update.SetOnInsert("Version", 0L),
                new UpdateOptions { IsUpsert = true },
                cancellationToken).ConfigureAwait(false);
            await CreateTimeTickerIndexes(cancellationToken).ConfigureAwait(false);
            await CreateCronTickerBaseIndexes(cancellationToken).ConfigureAwait(false);
            await CreateCronOccurrenceIndexes(cancellationToken).ConfigureAwait(false);
            await CreateResultIndexes(cancellationToken).ConfigureAwait(false);
            await CreateNodeFinalizationIndexes(cancellationToken).ConfigureAwait(false);
            await ProbeTransactionsAndMarkReadyAsync(fence, cancellationToken).ConfigureAwait(false);
        }

        public Task FinalizeAsync(CancellationToken cancellationToken = default)
            => CreateCronTickerSeedKeyIndex(cancellationToken);

        public async Task ProbeAsync(CancellationToken cancellationToken = default)
        {
            var fence = _context.Database.GetCollection<BsonDocument>(
                _context.TimeTickers.CollectionNamespace.CollectionName + "_GraphFence");
            await ProbeTransactionsAndMarkReadyAsync(fence, cancellationToken).ConfigureAwait(false);
        }

        // Retained for the provider's direct integration fixtures. Runtime startup uses the explicit
        // bootstrap/finalizer contracts so scheduler readiness never depends on hosted-service order.
        internal async Task StartAsync(CancellationToken cancellationToken)
        {
            await BootstrapAsync(cancellationToken).ConfigureAwait(false);
            await FinalizeAsync(cancellationToken).ConfigureAwait(false);
        }

        private async Task ProbeTransactionsAndMarkReadyAsync(
            IMongoCollection<BsonDocument> fence, CancellationToken cancellationToken)
        {
            _provider.MarkDurableNodeFinalizationOutboxUnavailable();
            using var probeTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            probeTimeout.CancelAfter(System.TimeSpan.FromSeconds(10));
            var probeToken = probeTimeout.Token;
            using var session = await _context.Database.Client.StartSessionAsync(
                cancellationToken: probeToken).ConfigureAwait(false);
            try
            {
                await session.WithTransactionAsync(async (s, ct) =>
                {
                    await fence.UpdateOneAsync(s,
                        Builders<BsonDocument>.Filter.Eq("_id", "time-ticker-graph"),
                        Builders<BsonDocument>.Update.Inc("Version", 0L),
                        cancellationToken: ct).ConfigureAwait(false);
                    return true;
                }, ProbeTransactionOptions, probeToken).ConfigureAwait(false);
                _provider.MarkDurableNodeFinalizationOutboxReady();
            }
            catch (MongoCommandException ex) when (ex.Code == 20 && ex.Message.Contains(
                       "Transaction numbers are only allowed on a replica set member or mongos",
                       System.StringComparison.OrdinalIgnoreCase))
            {
                // Standalone deployments remain operational for non-transactional features, but
                // durable Node finalization support stays false.
            }
            catch (System.NotSupportedException ex) when (ex.Message.Contains(
                       "Standalone servers do not support transactions",
                       System.StringComparison.OrdinalIgnoreCase))
            {
                // The driver can reject a discovered standalone locally before issuing a command.
            }
        }


        private Task CreateTimeTickerIndexes(CancellationToken ct)
        {
            var keys = Builders<TTimeTicker>.IndexKeys;
            var models = new[]
            {
                new CreateIndexModel<TTimeTicker>(keys.Ascending(x => x.ApplicationNamespaceKey).Ascending(x => x.Id),
                    new CreateIndexOptions { Name = "UX_TimeTicker_Partition_Id", Unique = true }),
                new CreateIndexModel<TTimeTicker>(keys.Ascending(x => x.ApplicationNamespaceKey).Ascending(x => x.ExecutionTime),
                    new CreateIndexOptions { Name = "IX_TimeTicker_ExecutionTime" }),
                new CreateIndexModel<TTimeTicker>(keys.Ascending(x => x.ApplicationNamespaceKey).Ascending(x => x.Status).Ascending(x => x.ExecutionTime),
                    new CreateIndexOptions { Name = "IX_TimeTicker_Status_ExecutionTime" }),
                new CreateIndexModel<TTimeTicker>(keys.Ascending(x => x.ApplicationNamespaceKey).Ascending(x => x.ParentId),
                    new CreateIndexOptions { Name = "IX_TimeTicker_ParentId", Sparse = true }),
                new CreateIndexModel<TTimeTicker>(
                    keys.Ascending(x => x.ApplicationNamespaceKey).Ascending(x => x.ChainRootId).Ascending(x => x.ChainGeneration),
                    new CreateIndexOptions { Name = "IX_TimeTicker_ChainRootId_ChainGeneration", Sparse = true }),
                new CreateIndexModel<TTimeTicker>(
                    keys.Ascending(x => x.ApplicationNamespaceKey).Ascending(x => x.ParentId).Ascending(x => x.Status)
                        .Ascending(x => x.ExecutedAt).Ascending(x => x.Id),
                    new CreateIndexOptions { Name = "IX_TimeTicker_Retention" }),
                new CreateIndexModel<TTimeTicker>(
                    keys.Ascending(x => x.ApplicationNamespaceKey).Ascending(x => x.LockHolder).Ascending(x => x.LeaseUntil),
                    new CreateIndexOptions { Name = "IX_TimeTicker_RetentionClaim" }),
            };
            return _context.TimeTickers.Indexes.CreateManyAsync(models, ct);
        }

        private Task CreateCronTickerBaseIndexes(CancellationToken ct)
        {
            var keys = Builders<TCronTicker>.IndexKeys;
            var models = new[]
            {
                new CreateIndexModel<TCronTicker>(keys.Ascending(x => x.ApplicationNamespaceKey).Ascending(x => x.Id),
                    new CreateIndexOptions { Name = "UX_CronTickers_Partition_Id", Unique = true }),
                new CreateIndexModel<TCronTicker>(keys.Ascending(x => x.ApplicationNamespaceKey).Ascending(x => x.Expression),
                    new CreateIndexOptions { Name = "IX_CronTickers_Expression" }),
                new CreateIndexModel<TCronTicker>(keys.Ascending(x => x.ApplicationNamespaceKey).Ascending(x => x.Function).Ascending(x => x.Expression),
                    new CreateIndexOptions { Name = "IX_Function_Expression" }),
            };
            return _context.CronTickers.Indexes.CreateManyAsync(models, ct);
        }

        private Task CreateCronTickerSeedKeyIndex(CancellationToken ct)
        {
            var keys = Builders<TCronTicker>.IndexKeys;
            return _context.CronTickers.Indexes.CreateOneAsync(
                new CreateIndexModel<TCronTicker>(keys.Ascending(x => x.ApplicationNamespaceKey).Ascending(x => x.SeedKey),
                    new CreateIndexOptions<TCronTicker>
                    {
                        Name = "UX_CronTickers_SeedKey",
                        Unique = true,
                        PartialFilterExpression = Builders<TCronTicker>.Filter.Type(x => x.SeedKey, BsonType.String)
                    }),
                cancellationToken: ct);
        }

        private Task CreateCronOccurrenceIndexes(CancellationToken ct)
        {
            var keys = Builders<CronTickerOccurrenceEntity<TCronTicker>>.IndexKeys;
            var models = new[]
            {
                new CreateIndexModel<CronTickerOccurrenceEntity<TCronTicker>>(
                    keys.Ascending(x => x.ApplicationNamespaceKey).Ascending(x => x.Id),
                    new CreateIndexOptions { Name = "UX_CronTickerOccurrence_Partition_Id", Unique = true }),
                new CreateIndexModel<CronTickerOccurrenceEntity<TCronTicker>>(
                    keys.Ascending(x => x.ApplicationNamespaceKey).Ascending(x => x.CronTickerId),
                    new CreateIndexOptions { Name = "IX_CronTickerOccurrence_CronTickerId" }),
                new CreateIndexModel<CronTickerOccurrenceEntity<TCronTicker>>(
                    keys.Ascending(x => x.ApplicationNamespaceKey).Ascending(x => x.ExecutionTime),
                    new CreateIndexOptions { Name = "IX_CronTickerOccurrence_ExecutionTime" }),
                new CreateIndexModel<CronTickerOccurrenceEntity<TCronTicker>>(
                    keys.Ascending(x => x.ApplicationNamespaceKey).Ascending(x => x.Status).Ascending(x => x.ExecutionTime),
                    new CreateIndexOptions { Name = "IX_CronTickerOccurrence_Status_ExecutionTime" }),
                new CreateIndexModel<CronTickerOccurrenceEntity<TCronTicker>>(
                    keys.Ascending(x => x.ApplicationNamespaceKey).Ascending(x => x.Status).Ascending(x => x.ExecutedAt).Ascending(x => x.Id),
                    new CreateIndexOptions { Name = "IX_CronTickerOccurrence_Retention" }),
                new CreateIndexModel<CronTickerOccurrenceEntity<TCronTicker>>(
                    keys.Ascending(x => x.ApplicationNamespaceKey).Ascending(x => x.CronTickerId).Ascending(x => x.ExecutionTime),
                    new CreateIndexOptions { Name = "UQ_CronTickerId_ExecutionTime", Unique = true }),
            };
            return _context.CronTickerOccurrences.Indexes.CreateManyAsync(models, ct);
        }

        private Task CreateResultIndexes(CancellationToken ct)
            => _context.TickerResults.Indexes.CreateOneAsync(
                new CreateIndexModel<BsonDocument>(
                    Builders<BsonDocument>.IndexKeys.Ascending("ApplicationNamespaceKey").Ascending("Kind").Ascending("TickerId"),
                    new CreateIndexOptions { Name = "IX_TickerResult_Kind" }),
                cancellationToken: ct);

        private Task CreateNodeFinalizationIndexes(CancellationToken ct)
        {
            var keys = Builders<BsonDocument>.IndexKeys;
            return _context.NodeFinalizations.Indexes.CreateManyAsync(new[]
            {
                new CreateIndexModel<BsonDocument>(
                    keys.Ascending("ApplicationNamespaceKey").Ascending("TickerType").Ascending("TickerId").Ascending("AcquisitionToken")
                        .Ascending("DispatchId").Ascending("NodeEpoch"),
                    new CreateIndexOptions { Name = "UQ_NodeFinalization_FullIdentity", Unique = true }),
                new CreateIndexModel<BsonDocument>(
                    keys.Ascending("ApplicationNamespaceKey").Ascending("AvailableAtUtc").Ascending("_id"),
                    new CreateIndexOptions { Name = "IX_NodeFinalization_Due" })
            }, ct);
        }
    }
}
