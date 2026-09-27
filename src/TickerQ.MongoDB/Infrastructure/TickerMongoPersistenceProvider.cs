using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Core.Servers;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Entities.BaseEntity;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Infrastructure;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ.MongoDB.Infrastructure
{
    internal sealed class TickerMongoPersistenceProvider<TTimeTicker, TCronTicker> :
        ITickerPersistenceProvider<TTimeTicker, TCronTicker>
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        private readonly ITickerMongoContext<TTimeTicker, TCronTicker> _context;
        private readonly ITickerClock _clock;
        private readonly string _lockHolder;
        private readonly SchedulerOptionsBuilder _schedulerOptions;
        private readonly long _runtimeActivationEpoch;
        private readonly bool _requiresActivatedRuntimeAdmission;
        private readonly bool _runtimeAdmissionMisconfigured;
        private readonly string _runtimeActivationScopeKey;
        private readonly string _runtimePartitionKey;
        private readonly IMongoCollection<BsonDocument> _graphFence;
        private readonly IMongoCollection<BsonDocument> _storeMetadata;
        private int _durableNodeFinalizationSupported;

        private const string GraphFenceId = "time-ticker-graph";
        private const string LegacyActivationMetadataId = "reconciliation-activation";
        private readonly string _runtimeActivationMetadataId;
        private string RuntimeActivationMetadataId => _runtimeActivationMetadataId;
        private static readonly TransactionOptions GraphTransactionOptions = new(
            readConcern: ReadConcern.Snapshot,
            writeConcern: WriteConcern.WMajority);

        // Deterministic race-test seams. They run inside the transaction at the named fence points.
        internal Func<CancellationToken, Task> BeforeGraphMutationFenceForTestAsync { get; set; }
        internal Func<CancellationToken, Task> AfterGraphMutationFenceForTestAsync { get; set; }
        internal Func<CancellationToken, Task> BeforeRetentionFenceForTestAsync { get; set; }
        internal Func<Guid, CancellationToken, Task> AfterRetentionDiscoveryForTestAsync { get; set; }
        internal Func<CancellationToken, Task> AfterResultMutationForTestAsync { get; set; }
        internal Func<CancellationToken, Task> AfterTerminalReplayAuthorityForTestAsync { get; set; }
        internal Func<CancellationToken, Task> AfterLegacyResultAuthorityForTestAsync { get; set; }
        internal Func<CancellationToken, Task> AfterTerminalTransactionForTestAsync { get; set; }
        internal Func<CancellationToken, Task> AfterNodeFinalizationInsertForTestAsync { get; set; }
        internal Func<CancellationToken, Task> AfterNodeFinalizationTransactionForTestAsync { get; set; }
        internal Func<CancellationToken, Task> BeforeActivationTransactionCommitForTestAsync { get; set; }
        internal Func<CancellationToken, Task> AfterActivationTransactionCommitForTestAsync { get; set; }
        internal Func<CancellationToken, Task> AfterRunnableAdmissionFenceForTestAsync { get; set; }
        internal Func<IClientSessionHandle, Task> CommitActivationTransactionForTestAsync { get; set; }

        internal const string RetentionLockHolder = "__tickerq_retention__";
        private static readonly TickerStatus[] TerminalStatuses =
        {
            TickerStatus.Done, TickerStatus.DueDone, TickerStatus.Failed,
            TickerStatus.Cancelled, TickerStatus.Skipped
        };

        private static readonly Func<TTimeTicker, TimeTickerEntity> ProjectTimeTicker
            = MappingExtensions.ForQueueTimeTickers<TTimeTicker>().Compile();

        public TickerMongoPersistenceProvider(
            ITickerMongoContext<TTimeTicker, TCronTicker> context,
            ITickerClock clock,
            SchedulerOptionsBuilder optionsBuilder)
        {
            _context = context;
            _clock = clock;
            _lockHolder = optionsBuilder.ExecutionOwnerId;
            _schedulerOptions = optionsBuilder;
            _runtimePartitionKey = (optionsBuilder.RuntimePartition ?? TickerQRuntimePartition.LegacyGlobal).StorageKey;
            _requiresActivatedRuntimeAdmission = optionsBuilder.RuntimeSchedulerEnabled &&
                                                  optionsBuilder.RuntimeActivationScope != null;
            _runtimeAdmissionMisconfigured = optionsBuilder.RuntimeSchedulerEnabled &&
                                             optionsBuilder.RuntimeActivationScope == null;
            _runtimeActivationEpoch = _requiresActivatedRuntimeAdmission
                ? optionsBuilder.RuntimeActivationEpoch
                : optionsBuilder.ReconciliationEpoch;
            _runtimeActivationMetadataId = _requiresActivatedRuntimeAdmission
                ? ActivationMetadataId(optionsBuilder.RuntimeActivationScope)
                : LegacyActivationMetadataId;
            _runtimeActivationScopeKey = optionsBuilder.RuntimeActivationScope?.ScopeKey;
            _graphFence = context.Database.GetCollection<BsonDocument>(
                context.TimeTickers.CollectionNamespace.CollectionName + "_GraphFence");
            _storeMetadata = context.StoreMetadata;
        }

        // A directConnection=true client deliberately reports ClusterType.Standalone even when the
        // selected server is a replica-set primary. ServerDescription.Type is the capability-bearing
        // signal: only a fully discovered, genuine standalone is known not to support transactions.
        private bool TransactionsKnownUnavailable
        {
            get
            {
                var servers = _context.Database.Client.Cluster.Description.Servers;
                return servers.Count > 0 && servers.All(x => x.Type == ServerType.Standalone);
            }
        }

        public bool SupportsLegacyRuntimePartitionAdoption => true;

        public async Task AdoptLegacyRuntimePartitionAsync(
            LegacyRuntimePartitionAdoption adoption, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(adoption);
            if (!StringComparer.Ordinal.Equals(adoption.TargetPartition.StorageKey, _runtimePartitionKey))
                throw new InvalidOperationException("Legacy adoption target does not match this provider's runtime partition.");
            if (TransactionsKnownUnavailable)
                throw new NotSupportedException(
                    "MongoDB legacy runtime adoption requires replica-set or mongos transaction support; standalone topology is unsafe.");
            var leaseId = new BsonDocument { ["Kind"] = "legacy-runtime-adoption" };
            var authority = $"{_runtimePartitionKey}|{adoption.Epoch}";
            var leaseFilter = Builders<BsonDocument>.Filter.Eq("_id", leaseId);
            try
            {
                await _storeMetadata.UpdateOneAsync(
                    Builders<BsonDocument>.Filter.And(leaseFilter,
                        Builders<BsonDocument>.Filter.Or(
                            Builders<BsonDocument>.Filter.Exists("Authority", false),
                            Builders<BsonDocument>.Filter.Eq("Authority", authority))),
                    Builders<BsonDocument>.Update
                        .SetOnInsert("Authority", authority)
                        .SetOnInsert("State", "adopting")
                        .Set("UpdatedAtUtc", _clock.UtcNow),
                    new UpdateOptions { IsUpsert = true }, cancellationToken).ConfigureAwait(false);
            }
            catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
            {
                throw new InvalidOperationException(
                    "Legacy runtime adoption is already held or completed by a different owner or epoch.", ex);
            }
            var lease = await _storeMetadata.Find(leaseFilter).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (lease == null || lease.GetValue("Authority", "").AsString != authority)
                throw new InvalidOperationException(
                    "Legacy runtime adoption is already held or completed by a different owner or epoch.");
            if (lease.GetValue("State", "").AsString == "completed") return;
            if (AfterLegacyAdoptionLeaseForTestAsync != null)
                await AfterLegacyAdoptionLeaseForTestAsync(cancellationToken).ConfigureAwait(false);

            using var session = await _context.Database.Client.StartSessionAsync(
                cancellationToken: cancellationToken).ConfigureAwait(false);
            await session.WithTransactionAsync(async (s, ct) =>
            {
                var legacyKey = TickerQRuntimePartition.LegacyGlobal.StorageKey;
                var owners = new HashSet<string>(StringComparer.Ordinal);
                owners.UnionWith(await _context.TimeTickers.Distinct<string>(s, "ApplicationNamespaceKey",
                    FilterDefinition<TTimeTicker>.Empty, cancellationToken: ct).ToListAsync(ct));
                owners.UnionWith(await _context.CronTickers.Distinct<string>(s, "ApplicationNamespaceKey",
                    FilterDefinition<TCronTicker>.Empty, cancellationToken: ct).ToListAsync(ct));
                owners.UnionWith(await _context.CronTickerOccurrences.Distinct<string>(s, "ApplicationNamespaceKey",
                    FilterDefinition<CronTickerOccurrenceEntity<TCronTicker>>.Empty, cancellationToken: ct).ToListAsync(ct));
                await AddDocumentOwnersAsync(s, _context.TickerResults, owners, ct);
                await AddDocumentOwnersAsync(s, _context.NodeFinalizations, owners, ct);
                await AddDocumentOwnersAsync(s, _graphFence, owners, ct);
                await AddDocumentOwnersAsync(s, _storeMetadata, owners, ct);
                owners.Remove(legacyKey); owners.Remove(_runtimePartitionKey); owners.Remove(null);
                if (owners.Count > 0)
                    throw new InvalidOperationException(
                        "Legacy runtime adoption is ambiguous because runtime rows exist for another namespace.");

                await _context.TimeTickers.UpdateManyAsync(s,
                    Builders<TTimeTicker>.Filter.Eq(x => x.ApplicationNamespaceKey, legacyKey),
                    Builders<TTimeTicker>.Update.Set(x => x.ApplicationNamespaceKey, _runtimePartitionKey),
                    cancellationToken: ct);
                await _context.CronTickers.UpdateManyAsync(s,
                    Builders<TCronTicker>.Filter.Eq(x => x.ApplicationNamespaceKey, legacyKey),
                    Builders<TCronTicker>.Update.Set(x => x.ApplicationNamespaceKey, _runtimePartitionKey),
                    cancellationToken: ct);
                await _context.CronTickerOccurrences.UpdateManyAsync(s,
                    Builders<CronTickerOccurrenceEntity<TCronTicker>>.Filter.Eq(x => x.ApplicationNamespaceKey, legacyKey),
                    Builders<CronTickerOccurrenceEntity<TCronTicker>>.Update.Set(x => x.ApplicationNamespaceKey, _runtimePartitionKey),
                    cancellationToken: ct);
                await MoveLegacyDocumentsAsync(s, _context.TickerResults, legacyKey, ct,
                    includePrePartitionDocuments: true);
                await MoveLegacyDocumentsAsync(s, _context.NodeFinalizations, legacyKey, ct);
                await MoveLegacyDocumentsAsync(s, _storeMetadata, legacyKey, ct, leaseId);
                await MoveLegacyDocumentsAsync(s, _graphFence, legacyKey, ct);
                await _storeMetadata.UpdateOneAsync(s, leaseFilter,
                    Builders<BsonDocument>.Update.Set("State", "completed").Set("UpdatedAtUtc", _clock.UtcNow),
                    cancellationToken: ct);
                return true;
            }, GraphTransactionOptions, cancellationToken).ConfigureAwait(false);
        }

        private static async Task AddDocumentOwnersAsync(IClientSessionHandle session,
            IMongoCollection<BsonDocument> collection, HashSet<string> owners,
            CancellationToken cancellationToken)
        {
            var values = await collection.Distinct<string>(session, "ApplicationNamespaceKey",
                Builders<BsonDocument>.Filter.Exists("ApplicationNamespaceKey", true),
                cancellationToken: cancellationToken).ToListAsync(cancellationToken).ConfigureAwait(false);
            owners.UnionWith(values);
        }

        private async Task MoveLegacyDocumentsAsync(IClientSessionHandle session,
            IMongoCollection<BsonDocument> collection, string legacyKey, CancellationToken cancellationToken,
            BsonValue excludedId = null, bool includePrePartitionDocuments = false)
        {
            var filter = Builders<BsonDocument>.Filter.Eq("ApplicationNamespaceKey", legacyKey);
            if (includePrePartitionDocuments)
                filter |= Builders<BsonDocument>.Filter.Exists("ApplicationNamespaceKey", false);
            if (excludedId != null)
                filter &= Builders<BsonDocument>.Filter.Ne("_id", excludedId);
            var documents = await collection.Find(session, filter).ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (var source in documents)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var moved = source.DeepClone().AsBsonDocument;
                moved["ApplicationNamespaceKey"] = _runtimePartitionKey;
                if (moved["_id"].IsBsonDocument && moved["_id"].AsBsonDocument.Contains("ApplicationNamespaceKey"))
                    moved["_id"].AsBsonDocument["ApplicationNamespaceKey"] = _runtimePartitionKey;
                else if (includePrePartitionDocuments && moved["_id"].IsGuid &&
                         moved.TryGetValue("Kind", out var kind) && kind.IsString)
                {
                    moved["_id"] = new BsonDocument
                    {
                        ["ApplicationNamespaceKey"] = _runtimePartitionKey,
                        ["Kind"] = kind.AsString,
                        ["TickerId"] = moved["_id"]
                    };
                }
                else if (includePrePartitionDocuments && !source.Contains("ApplicationNamespaceKey"))
                    throw new InvalidOperationException(
                        "A pre-partition MongoDB result has no canonical ticker identity and cannot be safely adopted.");
                await collection.InsertOneAsync(session, moved, cancellationToken: cancellationToken).ConfigureAwait(false);
                await collection.DeleteOneAsync(session,
                    Builders<BsonDocument>.Filter.Eq("_id", source["_id"]),
                    new DeleteOptions(), cancellationToken).ConfigureAwait(false);
            }
        }

        private static bool IsCanonicalTransactionsUnsupported(MongoCommandException exception)
            => exception.Code == 20 &&
               exception.Message.Contains(
                   "Transaction numbers are only allowed on a replica set member or mongos",
                   StringComparison.OrdinalIgnoreCase);

        internal Func<CancellationToken, Task> AfterLegacyAdoptionLeaseForTestAsync { get; set; }

        private async Task TouchGraphFenceAsync(IClientSessionHandle session, CancellationToken cancellationToken)
        {
            await _graphFence.UpdateOneAsync(
                session,
                Builders<BsonDocument>.Filter.Eq("_id", PartitionedDocumentId(GraphFenceId)),
                Builders<BsonDocument>.Update
                    .SetOnInsert("ApplicationNamespaceKey", _runtimePartitionKey)
                    .Inc("Version", 1L),
                new UpdateOptions { IsUpsert = true },
                cancellationToken).ConfigureAwait(false);
        }

        private FilterDefinition<TDocument> InPartition<TDocument>()
            => Builders<TDocument>.Filter.Eq("ApplicationNamespaceKey", _runtimePartitionKey);

        private FilterDefinition<TDocument> InPartition<TDocument>(FilterDefinition<TDocument> filter)
            => Builders<TDocument>.Filter.And(InPartition<TDocument>(), filter);

        private BsonDocument PartitionedDocumentId(string kind, Guid? id = null)
        {
            var value = new BsonDocument
            {
                ["ApplicationNamespaceKey"] = _runtimePartitionKey,
                ["Kind"] = kind
            };
            if (id.HasValue) value["TickerId"] = GuidValue(id.Value);
            return value;
        }

        private BsonDocument PartitionedMetadataId(string metadataId) => new()
        {
            ["ApplicationNamespaceKey"] = _runtimePartitionKey,
            ["Kind"] = "store-metadata",
            ["MetadataId"] = metadataId
        };

        private void Stamp(BaseTickerEntity entity)
        {
            if (!string.IsNullOrEmpty(entity.ApplicationNamespaceKey) &&
                entity.ApplicationNamespaceKey != TickerQRuntimePartition.LegacyGlobal.StorageKey &&
                !StringComparer.Ordinal.Equals(entity.ApplicationNamespaceKey, _runtimePartitionKey))
                throw new InvalidOperationException(
                    "A Mongo runtime document cannot be redirected to another application partition.");
            entity.ApplicationNamespaceKey = _runtimePartitionKey;
        }

        private void Stamp(CronTickerOccurrenceEntity<TCronTicker> entity)
        {
            if (!string.IsNullOrEmpty(entity.ApplicationNamespaceKey) &&
                entity.ApplicationNamespaceKey != TickerQRuntimePartition.LegacyGlobal.StorageKey &&
                !StringComparer.Ordinal.Equals(entity.ApplicationNamespaceKey, _runtimePartitionKey))
                throw new InvalidOperationException(
                    "A Mongo runtime document cannot be redirected to another application partition.");
            entity.ApplicationNamespaceKey = _runtimePartitionKey;
        }

        private async Task<bool> LockRunnableAdmissionAsync(
            IClientSessionHandle session, CancellationToken cancellationToken)
        {
            if (_runtimeAdmissionMisconfigured) return false;
            // Queue-only producers are intentionally outside activation. In particular, administering
            // either the legacy or a scoped protocol record must never redirect or pause enqueue.
            if (!_requiresActivatedRuntimeAdmission) return true;
            var id = Builders<BsonDocument>.Filter.Eq("_id", PartitionedMetadataId(RuntimeActivationMetadataId));
            await _storeMetadata.UpdateOneAsync(session, id,
                Builders<BsonDocument>.Update
                    .SetOnInsert("ApplicationNamespaceKey", _runtimePartitionKey)
                    .SetOnInsert("ActivationEpoch", 0L)
                    .SetOnInsert("ActivationPhase", (int)ActivationEpochPhase.Pending)
                    .SetOnInsert("ActivationCheckpoint", BsonNull.Value)
                    .SetOnInsert("Version", 0L)
                    .SetOnInsert("UpdatedAtUtc", _clock.UtcNow),
                new UpdateOptions { IsUpsert = true }, cancellationToken).ConfigureAwait(false);

            var phase = Builders<BsonDocument>.Filter.Eq(
                "ActivationPhase", (int)ActivationEpochPhase.Activated);
            if (StartupSeederAdmissionContext.Matches(
                    _runtimeActivationScopeKey, _runtimeActivationEpoch))
                phase = Builders<BsonDocument>.Filter.Or(phase,
                    Builders<BsonDocument>.Filter.Eq(
                        "ActivationPhase", (int)ActivationEpochPhase.Activating));
            var allowed = Builders<BsonDocument>.Filter.And(id, phase,
                Builders<BsonDocument>.Filter.Eq("ActivationEpoch", _runtimeActivationEpoch));
            var locked = await _storeMetadata.UpdateOneAsync(
                session, allowed, Builders<BsonDocument>.Update.Inc("AdmissionFence", 1L),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (locked.MatchedCount != 1) return false;
            if (AfterRunnableAdmissionFenceForTestAsync != null)
                await AfterRunnableAdmissionFenceForTestAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }

        private async Task<bool> IsRunnableAdmissionAllowedStandaloneAsync(CancellationToken cancellationToken)
        {
            if (_runtimeAdmissionMisconfigured) return false;
            if (!_requiresActivatedRuntimeAdmission) return true;
            var document = await _storeMetadata.Find(
                    Builders<BsonDocument>.Filter.Eq("_id", PartitionedMetadataId(RuntimeActivationMetadataId)))
                .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            // Once the activation protocol exists, a standalone cannot atomically couple this document
            // to a runnable mutation and therefore fails closed. Configured scheduler hosts also fail
            // closed before their scoped activation document exists; unbound queue-only producers retain
            // the legacy absent-document behavior.
            return false;
        }

        private async Task<bool> IsRunnableAdmissionAllowedAsync(CancellationToken cancellationToken)
        {
            if (_runtimeAdmissionMisconfigured) return false;
            if (!_requiresActivatedRuntimeAdmission) return true;
            var document = await _storeMetadata.Find(
                    Builders<BsonDocument>.Filter.Eq("_id", PartitionedMetadataId(RuntimeActivationMetadataId)))
                .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (document == null) return false;
            var state = ToActivationState(document);
            return state.Epoch == _runtimeActivationEpoch &&
                   (state.Phase == ActivationEpochPhase.Activated ||
                    (state.Phase == ActivationEpochPhase.Activating &&
                     StartupSeederAdmissionContext.Matches(
                         _runtimeActivationScopeKey, _runtimeActivationEpoch)));
        }

        private async Task<bool> LockCurrentChainGenerationAsync(
            IClientSessionHandle session, InternalFunctionContext context, CancellationToken cancellationToken)
        {
            if (!context.ChainRootId.HasValue || !context.ChainGeneration.HasValue)
                return false;
            var rootId = context.ChainRootId.Value;
            var generation = context.ChainGeneration.Value;
            var fb = Builders<TTimeTicker>.Filter;
            var filter = fb.And(
                fb.Eq(x => x.ApplicationNamespaceKey, _runtimePartitionKey),
                fb.Eq(x => x.Id, rootId),
                fb.Eq(x => x.ParentId, (Guid?)null),
                fb.Eq(x => x.ChainRootId, rootId),
                fb.Eq(x => x.ChainGeneration, generation));

            // Take a write lock on the authoritative root inside the child transaction. A plain
            // snapshot read permits write skew: a concurrent reacquisition could mint a new
            // generation while this transaction writes only the child. The no-op conditional
            // update forces either side of that race to conflict and retry against fresh state.
            var result = await _context.TimeTickers.UpdateOneAsync(
                session, filter,
                Builders<TTimeTicker>.Update.Set(x => x.ChainGeneration, generation),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            return result.MatchedCount == 1;
        }

        private async Task NormalizeChainDescendantsAsync(
            IClientSessionHandle session, Guid rootId, Guid generation, CancellationToken cancellationToken)
            => await NormalizeChainDescendantsAsync(
                session, new Dictionary<Guid, Guid> { [rootId] = generation }, cancellationToken)
                .ConfigureAwait(false);

        private sealed class ChainNodeProjection
        {
            public Guid Id { get; set; }
            public Guid? ParentId { get; set; }
        }

        private async Task NormalizeChainDescendantsAsync(
            IClientSessionHandle session, IReadOnlyDictionary<Guid, Guid> generations,
            CancellationToken cancellationToken)
        {
            var frontier = generations.Keys.ToDictionary(id => id, id => id);
            var fb = Builders<TTimeTicker>.Filter;
            while (frontier.Count > 0)
            {
                var children = await _context.TimeTickers.Find(
                        session, InPartition(fb.In(x => x.ParentId, frontier.Keys.Select(x => (Guid?)x))))
                    .Project(x => new ChainNodeProjection { Id = x.Id, ParentId = x.ParentId })
                    .ToListAsync(cancellationToken).ConfigureAwait(false);
                if (children.Count == 0)
                    return;

                var next = children.ToDictionary(
                    child => child.Id,
                    child => frontier[child.ParentId!.Value]);
                var updates = next.GroupBy(x => x.Value).Select(group =>
                {
                    var rootId = group.Key;
                    return new UpdateManyModel<TTimeTicker>(
                        InPartition(fb.In(x => x.Id, group.Select(x => x.Key))),
                        Builders<TTimeTicker>.Update
                            .Set(x => x.ChainRootId, rootId)
                            .Set(x => x.ChainGeneration, generations[rootId]));
                }).Cast<WriteModel<TTimeTicker>>().ToList();
                await _context.TimeTickers.BulkWriteAsync(
                    session, updates, cancellationToken: cancellationToken).ConfigureAwait(false);
                frontier = next;
            }
        }

        // Every supported structural graph mutation writes the same durable epoch document in the
        // transaction that performs the mutation. Retention writes it too. Mongo write conflicts then
        // serialize both operations even though snapshot reads alone do not protect against phantoms.
        // The document contains no expiring owner/claim: a process crash rolls the transaction back, so
        // there is no claim to recover and old databases are initialized lazily/provisioned on startup.
        private async Task<int> ExecuteGraphMutationAsync(
            Func<IClientSessionHandle, CancellationToken, Task<int>> transactionalOperation,
            Func<CancellationToken, Task<int>> standaloneOperation,
            CancellationToken cancellationToken)
        {
            if (TransactionsKnownUnavailable)
                return await standaloneOperation(cancellationToken).ConfigureAwait(false);

            using var session = await _context.Database.Client
                .StartSessionAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            try
            {
                return await session.WithTransactionAsync(
                    async (s, ct) =>
                    {
                        if (BeforeGraphMutationFenceForTestAsync != null)
                            await BeforeGraphMutationFenceForTestAsync(ct).ConfigureAwait(false);
                        await TouchGraphFenceAsync(s, ct).ConfigureAwait(false);
                        if (AfterGraphMutationFenceForTestAsync != null)
                            await AfterGraphMutationFenceForTestAsync(ct).ConfigureAwait(false);
                        return await transactionalOperation(s, ct).ConfigureAwait(false);
                    },
                    GraphTransactionOptions,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (MongoCommandException ex) when (IsCanonicalTransactionsUnsupported(ex))
            {
                // Retention itself fails closed on this topology, so preserving the historical standalone
                // mutation behavior cannot race a chain delete.
                return await standaloneOperation(cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task<int> ExecuteAdmittedGraphMutationAsync(
            Func<IClientSessionHandle, CancellationToken, Task<int>> transactionalOperation,
            Func<CancellationToken, Task<int>> standaloneOperation,
            CancellationToken cancellationToken)
        {
            if (TransactionsKnownUnavailable)
            {
                if (!await IsRunnableAdmissionAllowedStandaloneAsync(cancellationToken).ConfigureAwait(false))
                    return 0;
                return await standaloneOperation(cancellationToken).ConfigureAwait(false);
            }

            using var session = await _context.Database.Client
                .StartSessionAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            try
            {
                return await session.WithTransactionAsync(async (s, ct) =>
                {
                    // Global lock order for runnable publication: exact activation metadata, graph,
                    // then definition/root/entity authority.
                    if (!await LockRunnableAdmissionAsync(s, ct).ConfigureAwait(false)) return 0;
                    if (BeforeGraphMutationFenceForTestAsync != null)
                        await BeforeGraphMutationFenceForTestAsync(ct).ConfigureAwait(false);
                    await TouchGraphFenceAsync(s, ct).ConfigureAwait(false);
                    if (AfterGraphMutationFenceForTestAsync != null)
                        await AfterGraphMutationFenceForTestAsync(ct).ConfigureAwait(false);
                    return await transactionalOperation(s, ct).ConfigureAwait(false);
                }, GraphTransactionOptions, cancellationToken).ConfigureAwait(false);
            }
            catch (MongoCommandException ex) when (IsCanonicalTransactionsUnsupported(ex))
            {
                if (!await IsRunnableAdmissionAllowedStandaloneAsync(cancellationToken).ConfigureAwait(false))
                    return 0;
                return await standaloneOperation(cancellationToken).ConfigureAwait(false);
            }
        }

        private DateTime? NextLeaseUntil(DateTime now)
            => _schedulerOptions.StaleJobRecoveryEnabled
                ? now.Add(_schedulerOptions.LeaseDuration)
                : (DateTime?)null;

        private static bool IsFencedTerminalWrite(InternalFunctionContext functionContext)
            => functionContext.GetPropsToUpdate().Contains(nameof(InternalFunctionContext.ReleaseLock)) ||
               (functionContext.GetPropsToUpdate().Contains(nameof(InternalFunctionContext.Status)) &&
                functionContext.Status is TickerStatus.Done or TickerStatus.DueDone or TickerStatus.Failed
                    or TickerStatus.Cancelled or TickerStatus.Skipped);

        private const int MaxResultPayloadBytes = 1024 * 1024;
        private const string TimeResultKind = "time";
        private const string CronOccurrenceResultKind = "cron-occurrence";

        public bool SupportsResultPublication => true;
        public bool SupportsAcknowledgedTerminalUpdates => true;
        public bool SupportsTimeTickerChainRepair => true;

        #region Reconciliation_Activation_Epoch

        public bool SupportsReconciliationActivationEpoch => true;
        public bool SupportsAuthoritativeCronReconciliation => true;

        private static string ActivationMetadataId(ReconciliationActivationScope scope)
        {
            ArgumentNullException.ThrowIfNull(scope);
            return $"reconciliation-activation:{scope.ScopeKey}";
        }

        public Task<ActivationEpochState> GetReconciliationActivationStateAsync(
            ReconciliationActivationScope scope, CancellationToken cancellationToken = default)
            => GetCurrentActivationStateAsync(ActivationMetadataId(scope), cancellationToken);

        public Task<ActivationEpochState> BeginReconciliationActivationEpochAsync(
            ReconciliationActivationScope scope, long targetEpoch, CancellationToken cancellationToken = default)
        {
            if (targetEpoch <= 0) throw new ArgumentOutOfRangeException(nameof(targetEpoch));
            return MutateActivationStateAsync(
                ActivationMetadataId(scope), targetEpoch, null, ActivationMutation.Begin, cancellationToken);
        }

        public Task<ActivationEpochState> AdvanceReconciliationCheckpointAsync(
            ReconciliationActivationScope scope, long targetEpoch, string checkpoint,
            CancellationToken cancellationToken = default)
        {
            if (targetEpoch <= 0) throw new ArgumentOutOfRangeException(nameof(targetEpoch));
            if (string.IsNullOrWhiteSpace(checkpoint) || checkpoint.Length > 200)
                throw new ArgumentException(
                    "Activation checkpoint must be non-empty and at most 200 characters.", nameof(checkpoint));
            return MutateActivationStateAsync(
                ActivationMetadataId(scope), targetEpoch, checkpoint, ActivationMutation.Checkpoint, cancellationToken);
        }

        public Task<ActivationEpochState> CommitReconciliationActivationEpochAsync(
            ReconciliationActivationScope scope, long targetEpoch, CancellationToken cancellationToken = default)
        {
            if (targetEpoch <= 0) throw new ArgumentOutOfRangeException(nameof(targetEpoch));
            return MutateActivationStateAsync(
                ActivationMetadataId(scope), targetEpoch, null, ActivationMutation.Commit, cancellationToken);
        }

        public Task<ActivationEpochState> GetReconciliationActivationStateAsync(
            CancellationToken cancellationToken = default)
            => GetCurrentActivationStateAsync(RuntimeActivationMetadataId, cancellationToken);

        private async Task<ActivationEpochState> GetCurrentActivationStateAsync(
            string activationMetadataId, CancellationToken cancellationToken)
        {
            var document = await _storeMetadata.Find(
                    Builders<BsonDocument>.Filter.Eq("_id", PartitionedMetadataId(activationMetadataId)))
                .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            return ToActivationState(document);
        }

        public Task<ActivationEpochState> BeginReconciliationActivationEpochAsync(
            long targetEpoch, CancellationToken cancellationToken = default)
        {
            if (targetEpoch <= 0) throw new ArgumentOutOfRangeException(nameof(targetEpoch));
            return MutateActivationStateAsync(
                RuntimeActivationMetadataId, targetEpoch, null, ActivationMutation.Begin, cancellationToken);
        }

        public Task<ActivationEpochState> AdvanceReconciliationCheckpointAsync(
            long targetEpoch, string checkpoint, CancellationToken cancellationToken = default)
        {
            if (targetEpoch <= 0) throw new ArgumentOutOfRangeException(nameof(targetEpoch));
            if (string.IsNullOrWhiteSpace(checkpoint) || checkpoint.Length > 200)
                throw new ArgumentException(
                    "Activation checkpoint must be non-empty and at most 200 characters.", nameof(checkpoint));
            return MutateActivationStateAsync(
                RuntimeActivationMetadataId, targetEpoch, checkpoint, ActivationMutation.Checkpoint, cancellationToken);
        }

        public Task<ActivationEpochState> CommitReconciliationActivationEpochAsync(
            long targetEpoch, CancellationToken cancellationToken = default)
        {
            if (targetEpoch <= 0) throw new ArgumentOutOfRangeException(nameof(targetEpoch));
            return MutateActivationStateAsync(
                RuntimeActivationMetadataId, targetEpoch, null, ActivationMutation.Commit, cancellationToken);
        }

        private async Task<ActivationEpochState> MutateActivationStateAsync(
            string activationMetadataId, long targetEpoch, string checkpoint, ActivationMutation mutation,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (TransactionsKnownUnavailable)
                throw ActivationTransactionsUnavailable();

            using var session = await _context.Database.Client
                .StartSessionAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            try
            {
                // Every protocol-aware state transition writes this singleton in a transaction. It is
                // both the CAS record and the authoritative publication lock: a transaction publishing
                // related store changes must touch the same document before those changes. Mongo then
                // retries write-conflict losers against a fresh snapshot, so starters converge.
                // Commit publication has an explicit final caller-cancellation boundary below. Once that
                // boundary is crossed, all durable transaction/acknowledgement work is non-cancellable so
                // a committed activation can never be surfaced as caller cancellation.
                var transactionCancellation = mutation == ActivationMutation.Commit
                    ? CancellationToken.None
                    : cancellationToken;
                var committedState = await session.WithTransactionAsync(async (s, ct) =>
                {
                    var idFilter = Builders<BsonDocument>.Filter.Eq(
                        "_id", PartitionedMetadataId(activationMetadataId));
                    await _storeMetadata.UpdateOneAsync(s, idFilter,
                        Builders<BsonDocument>.Update
                            .SetOnInsert("ApplicationNamespaceKey", _runtimePartitionKey)
                            .SetOnInsert("ActivationEpoch", 0L)
                            .SetOnInsert("ActivationPhase", (int)ActivationEpochPhase.Pending)
                            .SetOnInsert("ActivationCheckpoint", BsonNull.Value)
                            .SetOnInsert("Version", 0L)
                            .SetOnInsert("UpdatedAtUtc", _clock.UtcNow),
                        new UpdateOptions { IsUpsert = true }, ct).ConfigureAwait(false);

                    var current = await _storeMetadata.Find(s, idFilter)
                        .FirstAsync(ct).ConfigureAwait(false);
                    var epoch = current["ActivationEpoch"].AsInt64;
                    var phase = (ActivationEpochPhase)current["ActivationPhase"].AsInt32;
                    var currentCheckpoint = ReadOptionalString(current, "ActivationCheckpoint");
                    var version = current["Version"].AsInt64;

                    var nextEpoch = epoch;
                    var nextPhase = phase;
                    var nextCheckpoint = currentCheckpoint;
                    var shouldUpdate = false;
                    switch (mutation)
                    {
                        case ActivationMutation.Begin when targetEpoch > epoch:
                            shouldUpdate = true;
                            nextEpoch = targetEpoch;
                            nextPhase = ActivationEpochPhase.Activating;
                            nextCheckpoint = null;
                            break;
                        case ActivationMutation.Begin when targetEpoch == epoch &&
                                                           phase == ActivationEpochPhase.Pending:
                            shouldUpdate = true;
                            nextPhase = ActivationEpochPhase.Activating;
                            break;
                        case ActivationMutation.Checkpoint when targetEpoch == epoch &&
                                                                phase == ActivationEpochPhase.Activating &&
                                                                CanAdvanceCheckpoint(currentCheckpoint, checkpoint):
                            shouldUpdate = true;
                            nextCheckpoint = checkpoint;
                            break;
                        case ActivationMutation.Commit when targetEpoch == epoch &&
                                                            phase == ActivationEpochPhase.Activating:
                            shouldUpdate = true;
                            nextPhase = ActivationEpochPhase.Activated;
                            break;
                    }

                    if (!shouldUpdate) return ToActivationState(current);

                    var cas = Builders<BsonDocument>.Filter.And(idFilter,
                        Builders<BsonDocument>.Filter.Eq("Version", version));
                    var result = await _storeMetadata.UpdateOneAsync(s, cas,
                        Builders<BsonDocument>.Update
                            .Set("ActivationEpoch", nextEpoch)
                            .Set("ActivationPhase", (int)nextPhase)
                            .Set("ActivationCheckpoint", nextCheckpoint == null
                                ? BsonNull.Value : (BsonValue)nextCheckpoint)
                            .Set("UpdatedAtUtc", _clock.UtcNow)
                            .Inc("Version", 1L), cancellationToken: ct).ConfigureAwait(false);
                    if (result.MatchedCount != 1)
                        throw new MongoException("TickerQ activation metadata CAS lost its transaction lock.");

                    var state = new ActivationEpochState
                    {
                        Epoch = nextEpoch, Phase = nextPhase, Checkpoint = nextCheckpoint
                    };
                    if (mutation == ActivationMutation.Commit &&
                        BeforeActivationTransactionCommitForTestAsync != null)
                        await BeforeActivationTransactionCommitForTestAsync(ct).ConfigureAwait(false);
                    if (mutation == ActivationMutation.Commit)
                        cancellationToken.ThrowIfCancellationRequested();
                    return state;
                }, GraphTransactionOptions, transactionCancellation).ConfigureAwait(false);
                // Commit has succeeded. Do not inspect the caller's token again: cancellation arriving
                // after this boundary belongs to shutdown/the next phase, not this successful commit.
                if (mutation == ActivationMutation.Commit &&
                    CommitActivationTransactionForTestAsync != null)
                    await CommitActivationTransactionForTestAsync(session).ConfigureAwait(false);
                if (mutation == ActivationMutation.Commit &&
                    AfterActivationTransactionCommitForTestAsync != null)
                    await AfterActivationTransactionCommitForTestAsync(CancellationToken.None).ConfigureAwait(false);
                return committedState;
            }
            catch (Exception ex) when (mutation == ActivationMutation.Commit &&
                                       (ex is OperationCanceledException || IsAmbiguousCommitFailure(ex)))
            {
                using var readbackTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var durable = await GetCurrentActivationStateAsync(activationMetadataId, readbackTimeout.Token)
                    .ConfigureAwait(false);
                if (durable.Epoch == targetEpoch && durable.Phase == ActivationEpochPhase.Activated)
                    return durable;
                throw;
            }
            catch (MongoCommandException ex) when (IsCanonicalTransactionsUnsupported(ex))
            {
                throw ActivationTransactionsUnavailable(ex);
            }
            catch (NotSupportedException ex) when (ex.Message.Contains(
                       "Standalone servers do not support transactions", StringComparison.OrdinalIgnoreCase))
            {
                throw ActivationTransactionsUnavailable(ex);
            }
        }

        private static NotSupportedException ActivationTransactionsUnavailable(Exception inner = null)
            => new(
                "Durable Mongo reconciliation activation requires replica-set transactions; standalone MongoDB cannot atomically publish cross-collection reconciliation. Activation was not claimed and the scheduler gate must remain closed.",
                inner);

        private static string ReadOptionalString(BsonDocument document, string name)
            => document.TryGetValue(name, out var value) && value.IsString ? value.AsString : null;

        private static ActivationEpochState ToActivationState(BsonDocument document)
            => document == null
                ? ActivationEpochState.PreEpoch
                : new ActivationEpochState
                {
                    Epoch = document.GetValue("ActivationEpoch", 0L).ToInt64(),
                    Phase = (ActivationEpochPhase)document.GetValue(
                        "ActivationPhase", (int)ActivationEpochPhase.Pending).ToInt32(),
                    Checkpoint = ReadOptionalString(document, "ActivationCheckpoint")
                };

        private static bool CanAdvanceCheckpoint(string current, string requested)
        {
            if (string.Equals(current, requested, StringComparison.Ordinal)) return false;
            if (current == null) return true;
            var currentRank = StartupCheckpointRank(current);
            var requestedRank = StartupCheckpointRank(requested);
            return currentRank == 0 || requestedRank > currentRank;
        }

        private static int StartupCheckpointRank(string checkpoint)
            => checkpoint is { Length: >= 3 } && checkpoint[2] == '-' &&
               int.TryParse(checkpoint.AsSpan(0, 2), out var rank)
                ? rank
                : 0;

        private enum ActivationMutation { Begin, Checkpoint, Commit }

        #endregion

        public async Task<TimeTickerChainRepairResult> RepairTimeTickerChainsAsync(
            CancellationToken cancellationToken = default)
        {
            var summary = TimeTickerChainRepairResult.Empty;

            async Task<int> RepairAsync(IClientSessionHandle session, CancellationToken ct)
            {
                var rows = session == null
                    ? await _context.TimeTickers.Find(InPartition<TTimeTicker>())
                        .ToListAsync(ct).ConfigureAwait(false)
                    : await _context.TimeTickers.Find(session, InPartition<TTimeTicker>())
                        .ToListAsync(ct).ConfigureAwait(false);
                var plan = TimeTickerChainRepairPlanner.Create(rows, ct);
                plan.ThrowIfMalformed();
                summary = plan.Result;
                if (plan.Updates.Count == 0) return 0;
                var updatedAt = _clock.UtcNow;
                var writes = plan.Updates.Select(update => (WriteModel<TTimeTicker>)
                    new UpdateOneModel<TTimeTicker>(
                        Builders<TTimeTicker>.Filter.Eq(x => x.Id, update.Row.Id),
                        Builders<TTimeTicker>.Update
                            .Set(x => x.ChainRootId, update.ChainRootId)
                            .Set(x => x.ChainGeneration, update.ChainGeneration)
                            .Set(x => x.UpdatedAt, updatedAt))).ToList();
                if (session == null)
                    await _context.TimeTickers.BulkWriteAsync(writes, cancellationToken: ct).ConfigureAwait(false);
                else
                    await _context.TimeTickers.BulkWriteAsync(session, writes, cancellationToken: ct).ConfigureAwait(false);
                return writes.Count;
            }

            await ExecuteGraphMutationAsync(
                (session, ct) => RepairAsync(session, ct),
                ct => RepairAsync(null, ct),
                cancellationToken).ConfigureAwait(false);
            return summary;
        }

        public bool SupportsDurableNodeFinalizationOutbox
            => Volatile.Read(ref _durableNodeFinalizationSupported) == 1;

        internal void MarkDurableNodeFinalizationOutboxReady()
            => Interlocked.Exchange(ref _durableNodeFinalizationSupported, 1);

        internal void MarkDurableNodeFinalizationOutboxUnavailable()
            => Interlocked.Exchange(ref _durableNodeFinalizationSupported, 0);

        private static BsonBinaryData LegacyResultId(Guid id)
            => new(id, GuidRepresentation.Standard);

        private BsonDocument ResultId(Guid id, string kind)
            => new()
            {
                ["ApplicationNamespaceKey"] = _runtimePartitionKey,
                ["Kind"] = kind,
                ["TickerId"] = LegacyResultId(id)
            };

        private FilterDefinition<BsonDocument> ResultFilter(Guid id, string kind)
        {
            var filters = Builders<BsonDocument>.Filter;
            var legacyOwnership = _runtimePartitionKey == TickerQRuntimePartition.LegacyGlobal.StorageKey
                ? filters.Or(filters.Eq("ApplicationNamespaceKey", _runtimePartitionKey),
                    filters.Exists("ApplicationNamespaceKey", false))
                : filters.Eq("ApplicationNamespaceKey", _runtimePartitionKey);
            return filters.Or(
                filters.Eq("_id", ResultId(id, kind)),
                filters.And(legacyOwnership,
                    filters.Eq("_id", LegacyResultId(id)), filters.Eq("Kind", kind)));
        }

        private FilterDefinition<BsonDocument> ResultFilter(IEnumerable<Guid> ids, string kind)
        {
            var values = ids.Distinct().ToArray();
            if (values.Length == 0) return Builders<BsonDocument>.Filter.Where(_ => false);
            var filters = Builders<BsonDocument>.Filter;
            var legacyOwnership = _runtimePartitionKey == TickerQRuntimePartition.LegacyGlobal.StorageKey
                ? filters.Or(filters.Eq("ApplicationNamespaceKey", _runtimePartitionKey),
                    filters.Exists("ApplicationNamespaceKey", false))
                : filters.Eq("ApplicationNamespaceKey", _runtimePartitionKey);
            return filters.Or(
                filters.In("_id", values.Select(id => ResultId(id, kind))),
                filters.And(legacyOwnership,
                    filters.In("_id", values.Select(LegacyResultId)), filters.Eq("Kind", kind)));
        }

        private FilterDefinition<BsonDocument> LegacyScalarResultFilter(Guid id, string kind)
        {
            var filters = Builders<BsonDocument>.Filter;
            var ownership = _runtimePartitionKey == TickerQRuntimePartition.LegacyGlobal.StorageKey
                ? filters.Or(filters.Eq("ApplicationNamespaceKey", _runtimePartitionKey),
                    filters.Exists("ApplicationNamespaceKey", false))
                : filters.Eq("ApplicationNamespaceKey", _runtimePartitionKey);
            return filters.And(ownership, filters.Eq("_id", LegacyResultId(id)), filters.Eq("Kind", kind));
        }

        private static void ValidateResult(TickerResultEnvelope envelope)
        {
            envelope.EnsureSupportedVersion();
            if (envelope.PayloadLength > MaxResultPayloadBytes)
                throw new ArgumentOutOfRangeException(
                    nameof(envelope), envelope.PayloadLength,
                    $"Ticker result payloads cannot exceed {MaxResultPayloadBytes} bytes.");
        }

        private BsonDocument ToResultDocument(
            Guid id, string kind, TickerResultEnvelope envelope)
        {
            ValidateResult(envelope);
            var document = new BsonDocument
            {
                ["_id"] = ResultId(id, kind),
                ["ApplicationNamespaceKey"] = _runtimePartitionKey,
                ["Kind"] = kind,
                ["Payload"] = new BsonBinaryData(envelope.ToPayloadArray()),
                ["Version"] = envelope.Version,
                ["MediaType"] = envelope.MediaType
            };
            if (envelope.ContractId != null) document["ContractId"] = envelope.ContractId;
            if (envelope.ContractType != null) document["ContractType"] = envelope.ContractType;
            return document;
        }

        private async Task StoreOrClearResultAsync(
            IClientSessionHandle session, Guid id, string kind,
            TickerResultEnvelope envelope, CancellationToken cancellationToken)
        {
            var filter = ResultFilter(id, kind);
            if (envelope == null)
            {
                await _context.TickerResults.DeleteOneAsync(
                    session, filter, new DeleteOptions(), cancellationToken).ConfigureAwait(false);
                return;
            }

            // A legacy scalar _id cannot be changed by ReplaceOne: MongoDB rejects that as an
            // immutable-key mutation. Remove only the matching legacy kind in this same transaction,
            // then upsert the typed composite identity. A rollback restores the scalar row exactly.
            await _context.TickerResults.DeleteOneAsync(session,
                LegacyScalarResultFilter(id, kind),
                new DeleteOptions(), cancellationToken).ConfigureAwait(false);
            await _context.TickerResults.ReplaceOneAsync(
                session, Builders<BsonDocument>.Filter.Eq("_id", ResultId(id, kind)),
                ToResultDocument(id, kind, envelope),
                new ReplaceOptions { IsUpsert = true }, cancellationToken).ConfigureAwait(false);
        }

        private BsonDocument ToTerminalEvidenceDocument(
            Guid id, string kind, InternalFunctionContext context)
        {
            var result = context.Status is TickerStatus.Done or TickerStatus.DueDone
                ? context.ResultEnvelope
                : null;
            var identity = new BsonDocument
            {
                ["TickerType"] = (int)context.Type,
                ["TickerId"] = GuidValue(context.TickerId),
                ["ParentId"] = context.ParentId.HasValue ? GuidValue(context.ParentId.Value) : BsonNull.Value,
                ["ChainRootId"] = context.ChainRootId.HasValue ? GuidValue(context.ChainRootId.Value) : BsonNull.Value,
                ["ChainGeneration"] = context.ChainGeneration.HasValue
                    ? GuidValue(context.ChainGeneration.Value) : BsonNull.Value,
                ["AcquisitionToken"] = context.AcquisitionToken.HasValue
                    ? GuidValue(context.AcquisitionToken.Value) : BsonNull.Value,
                ["Mutation"] = new BsonBinaryData(TerminalMutationDigest(context))
            };
            var document = new BsonDocument
            {
                ["_id"] = ResultId(id, kind),
                ["ApplicationNamespaceKey"] = _runtimePartitionKey,
                ["Kind"] = kind,
                ["TerminalMutationDigest"] = new BsonBinaryData(SHA256.HashData(identity.ToBson()))
            };
            if (result == null) return document;

            ValidateResult(result);
            document["Payload"] = new BsonBinaryData(result.ToPayloadArray());
            document["Version"] = result.Version;
            document["MediaType"] = result.MediaType;
            if (result.ContractId != null) document["ContractId"] = result.ContractId;
            if (result.ContractType != null) document["ContractType"] = result.ContractType;
            return document;
        }

        private async Task StoreTerminalEvidenceAsync(
            IClientSessionHandle session, BsonDocument expected, CancellationToken cancellationToken)
        {
            var kind = expected["Kind"].AsString;
            var tickerId = expected["_id"].AsBsonDocument["TickerId"].AsBsonBinaryData;
            await _context.TickerResults.DeleteOneAsync(session,
                LegacyScalarResultFilter(tickerId.ToGuid(GuidRepresentation.Standard), kind),
                new DeleteOptions(), cancellationToken).ConfigureAwait(false);
            await _context.TickerResults.ReplaceOneAsync(
                session,
                Builders<BsonDocument>.Filter.Eq("_id", expected["_id"]),
                expected,
                new ReplaceOptions { IsUpsert = true }, cancellationToken).ConfigureAwait(false);
        }

        private async Task<bool> HasExactTerminalEvidenceAsync(
            BsonDocument expected, InternalFunctionContext context, CancellationToken cancellationToken = default)
        {
            if (TransactionsKnownUnavailable) return false;
            using var session = await _context.Database.Client
                .StartSessionAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            return await session.WithTransactionAsync(async (s, ct) =>
            {
                await TouchGraphFenceAsync(s, ct).ConfigureAwait(false);
                if (!await LockTerminalReplayAuthorityAsync(s, context, ct).ConfigureAwait(false))
                    return false;
                var stored = await _context.TickerResults.Find(
                        s, Builders<BsonDocument>.Filter.Eq("_id", expected["_id"]))
                    .FirstOrDefaultAsync(ct).ConfigureAwait(false);
                var matches = stored != null &&
                    stored.GetValue("Kind", BsonNull.Value).Equals(expected["Kind"]) &&
                    stored.GetValue("TerminalMutationDigest", BsonNull.Value)
                        .Equals(expected["TerminalMutationDigest"]);
                if (AfterTerminalReplayAuthorityForTestAsync != null)
                    await AfterTerminalReplayAuthorityForTestAsync(ct).ConfigureAwait(false);
                return matches;
            }, GraphTransactionOptions, cancellationToken).ConfigureAwait(false);
        }

        private async Task<bool> LockTerminalReplayAuthorityAsync(
            IClientSessionHandle session, InternalFunctionContext context, CancellationToken cancellationToken)
        {
            if (context.Type == TickerType.CronTickerOccurrence)
            {
                var fb = Builders<CronTickerOccurrenceEntity<TCronTicker>>.Filter;
                var authority = fb.And(fb.Eq(x => x.ApplicationNamespaceKey, _runtimePartitionKey),
                    fb.Eq(x => x.Id, context.TickerId),
                    fb.Or(fb.Eq(x => x.AcquisitionToken, (Guid?)null),
                        fb.Eq(x => x.AcquisitionToken, context.AcquisitionToken)));
                var locked = await _context.CronTickerOccurrences.UpdateOneAsync(
                    session, authority,
                    Builders<CronTickerOccurrenceEntity<TCronTicker>>.Update.Inc(x => x.ElapsedTime, 0L),
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                if (locked.MatchedCount == 1) return true;
                return !await _context.CronTickerOccurrences.Find(
                        session, InPartition(fb.Eq(x => x.Id, context.TickerId)))
                    .AnyAsync(cancellationToken).ConfigureAwait(false);
            }

            var time = Builders<TTimeTicker>.Filter;
            var generationAuthority = context.ParentId.HasValue
                ? time.Eq(x => x.ChainGeneration, context.ChainGeneration)
                : time.Or(time.Eq(x => x.AcquisitionToken, (Guid?)null),
                    time.Eq(x => x.AcquisitionToken, context.AcquisitionToken));
            var result = await _context.TimeTickers.UpdateOneAsync(
                session, time.And(time.Eq(x => x.ApplicationNamespaceKey, _runtimePartitionKey),
                    time.Eq(x => x.Id, context.TickerId), generationAuthority),
                Builders<TTimeTicker>.Update.Inc(x => x.ElapsedTime, 0L),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (result.MatchedCount == 1) return true;
            return !await _context.TimeTickers.Find(session, InPartition(time.Eq(x => x.Id, context.TickerId)))
                .AnyAsync(cancellationToken).ConfigureAwait(false);
        }

        private async Task<TickerResultEnvelope> GetResultAsync(
            Guid id, string expectedKind, CancellationToken cancellationToken)
        {
            var document = await _context.TickerResults.Find(
                    Builders<BsonDocument>.Filter.Eq("_id", ResultId(id, expectedKind)))
                .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (document == null)
                document = await AdoptLegacyResultAsync(id, expectedKind, cancellationToken).ConfigureAwait(false);
            if (document == null
                || !document.TryGetValue("Kind", out var kind) || !kind.IsString || kind.AsString != expectedKind
                || !document.TryGetValue("Payload", out var payload) || !payload.IsBsonBinaryData
                || !document.TryGetValue("Version", out var version) || !version.IsInt32
                || version.AsInt32 != TickerResultEnvelope.CurrentVersion
                || !document.TryGetValue("MediaType", out var mediaType) || !mediaType.IsString
                || string.IsNullOrWhiteSpace(mediaType.AsString))
                return null;

            var bytes = payload.AsBsonBinaryData.Bytes;
            if (bytes.Length > MaxResultPayloadBytes)
                return null;

            static bool OptionalString(BsonDocument source, string name, out string value)
            {
                value = null;
                if (!source.TryGetValue(name, out var field) || field.IsBsonNull) return true;
                if (!field.IsString || string.IsNullOrWhiteSpace(field.AsString)) return false;
                value = field.AsString;
                return true;
            }

            if (!OptionalString(document, "ContractId", out var contractId)
                || !OptionalString(document, "ContractType", out var contractType))
                return null;

            return new TickerResultEnvelope(
                bytes, version.AsInt32, mediaType.AsString, contractId, contractType);
        }

        private async Task<BsonDocument> AdoptLegacyResultAsync(
            Guid id, string expectedKind, CancellationToken cancellationToken)
        {
            var legacyFilter = Builders<BsonDocument>.Filter.And(
                _runtimePartitionKey == TickerQRuntimePartition.LegacyGlobal.StorageKey
                    ? Builders<BsonDocument>.Filter.Or(
                        Builders<BsonDocument>.Filter.Eq("ApplicationNamespaceKey", _runtimePartitionKey),
                        Builders<BsonDocument>.Filter.Exists("ApplicationNamespaceKey", false))
                    : Builders<BsonDocument>.Filter.Eq("ApplicationNamespaceKey", _runtimePartitionKey),
                Builders<BsonDocument>.Filter.Eq("_id", LegacyResultId(id)),
                Builders<BsonDocument>.Filter.Eq("Kind", expectedKind));
            if (TransactionsKnownUnavailable) return null;

            using var session = await _context.Database.Client
                .StartSessionAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            return await session.WithTransactionAsync(async (s, ct) =>
            {
                await TouchGraphFenceAsync(s, ct).ConfigureAwait(false);
                if (!await LockLegacyResultAuthorityAsync(s, id, expectedKind, ct).ConfigureAwait(false))
                    return null;
                if (AfterLegacyResultAuthorityForTestAsync != null)
                    await AfterLegacyResultAuthorityForTestAsync(ct).ConfigureAwait(false);
                var typedFilter = Builders<BsonDocument>.Filter.Eq("_id", ResultId(id, expectedKind));
                var typed = await _context.TickerResults.Find(s, typedFilter)
                    .FirstOrDefaultAsync(ct).ConfigureAwait(false);
                if (typed != null)
                {
                    await _context.TickerResults.DeleteOneAsync(s, legacyFilter,
                        new DeleteOptions(), ct).ConfigureAwait(false);
                    return typed;
                }

                var legacy = await _context.TickerResults.Find(s, legacyFilter)
                    .FirstOrDefaultAsync(ct).ConfigureAwait(false);
                if (legacy == null) return null;
                var adopted = legacy.DeepClone().AsBsonDocument;
                adopted["_id"] = ResultId(id, expectedKind);
                adopted["ApplicationNamespaceKey"] = _runtimePartitionKey;
                await _context.TickerResults.DeleteOneAsync(s, legacyFilter,
                    new DeleteOptions(), ct).ConfigureAwait(false);
                await _context.TickerResults.InsertOneAsync(s, adopted,
                    cancellationToken: ct).ConfigureAwait(false);
                return adopted;
            }, GraphTransactionOptions, cancellationToken).ConfigureAwait(false);
        }

        private async Task<bool> LockLegacyResultAuthorityAsync(
            IClientSessionHandle session, Guid id, string kind, CancellationToken cancellationToken)
        {
            if (kind == CronOccurrenceResultKind)
            {
                var fb = Builders<CronTickerOccurrenceEntity<TCronTicker>>.Filter;
                var result = await _context.CronTickerOccurrences.UpdateOneAsync(
                    session, InPartition(fb.And(fb.Eq(x => x.Id, id), fb.In(x => x.Status, TerminalStatuses),
                        fb.Eq(x => x.AcquisitionToken, (Guid?)null))),
                    Builders<CronTickerOccurrenceEntity<TCronTicker>>.Update.Inc(x => x.ElapsedTime, 0L),
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                if (result.MatchedCount == 1) return true;
                return !await _context.CronTickerOccurrences.Find(session, InPartition(fb.Eq(x => x.Id, id)))
                    .AnyAsync(cancellationToken).ConfigureAwait(false);
            }

            var time = Builders<TTimeTicker>.Filter;
            var locked = await _context.TimeTickers.UpdateOneAsync(
                session, InPartition(time.And(time.Eq(x => x.Id, id), time.In(x => x.Status, TerminalStatuses),
                    time.Eq(x => x.AcquisitionToken, (Guid?)null))),
                Builders<TTimeTicker>.Update.Inc(x => x.ElapsedTime, 0L),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (locked.MatchedCount == 1) return true;
            return !await _context.TimeTickers.Find(session, InPartition(time.Eq(x => x.Id, id)))
                .AnyAsync(cancellationToken).ConfigureAwait(false);
        }

        public Task<TickerResultEnvelope> GetTimeTickerResultAsync(
            Guid id, CancellationToken cancellationToken = default)
            => GetResultAsync(id, TimeResultKind, cancellationToken);

        public Task<TickerResultEnvelope> GetCronTickerOccurrenceResultAsync(
            Guid id, CancellationToken cancellationToken = default)
            => GetResultAsync(id, CronOccurrenceResultKind, cancellationToken);

        public async Task<bool> CommitSuccessfulTickerAsync(
            InternalFunctionContext functionContext, CancellationToken cancellationToken = default)
        {
            EnsureExactTerminalPartition(functionContext);
            ValidateSuccessfulCommit(functionContext);
            return await CommitTerminalTickerCoreAsync(
                functionContext, enforceRemoteChildToken: false, cancellationToken).ConfigureAwait(false);
        }

        private static void ValidateSuccessfulCommit(InternalFunctionContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (!context.GetPropsToUpdate().Contains(nameof(InternalFunctionContext.Status)) ||
                context.Status is not (TickerStatus.Done or TickerStatus.DueDone) ||
                !context.GetPropsToUpdate().Contains(nameof(InternalFunctionContext.ResultEnvelope)))
                throw new InvalidOperationException(
                    "Atomic result persistence accepts only a successful terminal mutation with an explicit optional result envelope.");
        }

        public Task<bool> CommitTerminalTickerAsync(
            InternalFunctionContext functionContext, CancellationToken cancellationToken = default)
        {
            EnsureExactTerminalPartition(functionContext);
            return CommitTerminalTickerCoreAsync(
                functionContext, enforceRemoteChildToken: true, cancellationToken);
        }

        private static BsonBinaryData GuidValue(Guid value)
            => new(value, GuidRepresentation.Standard);

        private static readonly string[] NodeFinalizationImmutableFields =
        {
            "SchemaVersion", "TickerType", "TickerId", "AcquisitionToken", "DispatchId", "NodeEpoch",
            "FinalizeUri", "FinalizePathAndQuery", "AllowPrivateCallbackAddressesForLocalDevelopment",
            "RequestNonce", "ControlNonce", "ExactBody", "CreatedAtUtc", "TerminalMutationDigest"
        };

        private static byte[] TerminalMutationDigest(InternalFunctionContext context)
        {
            var result = context.ResultEnvelope;
            var canonical = new BsonDocument
            {
                ["Properties"] = new BsonArray(context.GetPropsToUpdate().OrderBy(x => x, StringComparer.Ordinal)),
                ["Status"] = (int)context.Status,
                ["ExecutedAt"] = context.ExecutedAt,
                ["ExceptionDetails"] = context.ExceptionDetails == null ? BsonNull.Value : context.ExceptionDetails,
                ["ElapsedTime"] = context.ElapsedTime,
                ["RetryCount"] = context.RetryCount,
                ["ReleaseLock"] = context.ReleaseLock,
                ["ExecutionTime"] = context.ExecutionTime,
                ["Result"] = result == null ? BsonNull.Value : new BsonDocument
                {
                    ["Payload"] = new BsonBinaryData(result.ToPayloadArray()),
                    ["Version"] = result.Version,
                    ["MediaType"] = result.MediaType,
                    ["ContractId"] = result.ContractId == null ? BsonNull.Value : result.ContractId,
                    ["ContractType"] = result.ContractType == null ? BsonNull.Value : result.ContractType
                }
            };
            return SHA256.HashData(canonical.ToBson());
        }

        private BsonDocument ToNodeFinalizationDocument(
            NodeFinalizationIntent intent, InternalFunctionContext context) => new()
        {
            ["_id"] = PartitionedDocumentId("node-finalization", intent.OutboxId),
            ["ApplicationNamespaceKey"] = _runtimePartitionKey,
            ["OutboxId"] = GuidValue(intent.OutboxId),
            ["SchemaVersion"] = intent.SchemaVersion,
            ["TickerType"] = (int)intent.TickerType,
            ["TickerId"] = GuidValue(intent.TickerId),
            ["AcquisitionToken"] = GuidValue(intent.AcquisitionToken),
            ["DispatchId"] = GuidValue(intent.DispatchId),
            ["NodeEpoch"] = GuidValue(intent.NodeEpoch),
            ["FinalizeUri"] = intent.FinalizeUri,
            ["FinalizePathAndQuery"] = intent.FinalizePathAndQuery,
            ["AllowPrivateCallbackAddressesForLocalDevelopment"] = intent.AllowPrivateCallbackAddressesForLocalDevelopment,
            ["RequestNonce"] = GuidValue(intent.RequestNonce),
            ["ControlNonce"] = GuidValue(intent.ControlNonce),
            ["ExactBody"] = new BsonBinaryData(intent.ExactBody),
            ["CreatedAtUtc"] = intent.CreatedAtUtc,
            ["TerminalMutationDigest"] = new BsonBinaryData(TerminalMutationDigest(context)),
            ["AvailableAtUtc"] = intent.CreatedAtUtc,
            ["ClaimToken"] = BsonNull.Value,
            ["ClaimedBy"] = BsonNull.Value,
            ["AttemptCount"] = 0,
            ["LastAttemptAtUtc"] = BsonNull.Value,
            ["LastErrorCode"] = BsonNull.Value
        };

        private static bool HasExactImmutableIntent(BsonDocument stored, BsonDocument expected)
            => stored.GetValue("_id", BsonNull.Value).Equals(expected["_id"]) &&
               NodeFinalizationImmutableFields.All(name =>
                   stored.GetValue(name, BsonNull.Value).Equals(expected[name]));

        private static bool IsAmbiguousCommitFailure(Exception exception)
            => exception is TimeoutException or MongoConnectionException ||
               exception is MongoException mongo && mongo.HasErrorLabel("UnknownTransactionCommitResult");

        private async Task<bool> ResolveAmbiguousNodeFinalizationCommitAsync(
            Guid outboxId, BsonDocument expected)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var stored = await _context.NodeFinalizations.Find(
                    Builders<BsonDocument>.Filter.Eq("_id", PartitionedDocumentId("node-finalization", outboxId)))
                .FirstOrDefaultAsync(timeout.Token).ConfigureAwait(false);
            if (stored == null) return false;
            if (HasExactImmutableIntent(stored, expected)) return true;
            throw new InvalidOperationException(
                "Ambiguous Node finalization commit resolved to an outbox row with different immutable intent or terminal mutation data.");
        }

        private static Guid ReadGuid(BsonDocument document, string name)
            => document[name].AsBsonBinaryData.ToGuid(GuidRepresentation.Standard);

        private static NodeFinalizationIntent ReadNodeFinalizationIntent(BsonDocument document)
            => new(
                document["SchemaVersion"].AsInt32,
                ReadGuid(document, "OutboxId"),
                (TickerType)document["TickerType"].AsInt32,
                ReadGuid(document, "TickerId"),
                ReadGuid(document, "AcquisitionToken"),
                ReadGuid(document, "DispatchId"),
                ReadGuid(document, "NodeEpoch"),
                document["FinalizeUri"].AsString,
                document["FinalizePathAndQuery"].AsString,
                document["AllowPrivateCallbackAddressesForLocalDevelopment"].AsBoolean,
                ReadGuid(document, "RequestNonce"),
                ReadGuid(document, "ControlNonce"),
                document["ExactBody"].AsBsonBinaryData.Bytes,
                document["CreatedAtUtc"].ToUniversalTime(),
                document["ApplicationNamespaceKey"].AsString);

        private static void ValidateNodeFinalizationCommit(
            InternalFunctionContext context, NodeFinalizationIntent intent)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(intent);
            var successful = context.Status is TickerStatus.Done or TickerStatus.DueDone;
            if (!context.GetPropsToUpdate().Contains(nameof(InternalFunctionContext.Status)) ||
                context.Status is not (TickerStatus.Done or TickerStatus.DueDone or TickerStatus.Failed
                    or TickerStatus.Cancelled or TickerStatus.Skipped) ||
                (successful && !context.GetPropsToUpdate().Contains(nameof(InternalFunctionContext.ResultEnvelope))))
                throw new InvalidOperationException(
                    "Durable Node finalization accepts only a terminal mutation; success requires an explicit optional result envelope.");
            if (context.Type != intent.TickerType || context.TickerId != intent.TickerId ||
                context.AcquisitionToken != intent.AcquisitionToken || intent.OutboxId != intent.DispatchId)
                throw new InvalidOperationException("Node finalization intent does not match the exact ticker execution identity.");
            if (context.ResultEnvelope != null) ValidateResult(context.ResultEnvelope);
        }

        public async Task<bool> CommitTerminalTickerAndEnqueueNodeFinalizationAsync(
            InternalFunctionContext functionContext, NodeFinalizationIntent intent,
            CancellationToken cancellationToken = default)
        {
            EnsureExactTerminalPartition(functionContext);
            if (!StringComparer.Ordinal.Equals(intent?.RuntimePartitionKey, _runtimePartitionKey))
                throw new InvalidOperationException("Node finalization intent runtime partition mismatch.");
            ValidateNodeFinalizationCommit(functionContext, intent);
            if (!SupportsDurableNodeFinalizationOutbox)
                throw new NotSupportedException(
                    "Durable Mongo Node finalization requires an explicitly configured replica set transaction.");

            var expected = ToNodeFinalizationDocument(intent, functionContext);
            using var session = await _context.Database.Client
                .StartSessionAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            try
            {
                var committed = await session.WithTransactionAsync(async (s, ct) =>
                {
                    await TouchGraphFenceAsync(s, ct).ConfigureAwait(false);
                    var idFilter = Builders<BsonDocument>.Filter.Eq(
                        "_id", PartitionedDocumentId("node-finalization", intent.OutboxId));
                    var existing = await _context.NodeFinalizations.Find(s, idFilter)
                        .FirstOrDefaultAsync(ct).ConfigureAwait(false);
                    if (existing != null)
                    {
                        if (HasExactImmutableIntent(existing, expected)) return true;
                        throw new InvalidOperationException(
                            "A Node finalization outbox ID already exists with different immutable intent data.");
                    }

                    var now = _clock.UtcNow;
                    UpdateResult acknowledged;
                    string resultKind;
                    if (functionContext.Type == TickerType.CronTickerOccurrence)
                    {
                        var fb = Builders<CronTickerOccurrenceEntity<TCronTicker>>.Filter;
                        var filter = functionContext.AcquisitionToken.HasValue
                            ? InPartition(fb.And(fb.Eq(x => x.Id, functionContext.TickerId),
                                fb.Eq(x => x.LockHolder, _lockHolder),
                                fb.Eq(x => x.AcquisitionToken, functionContext.AcquisitionToken)))
                            : fb.Where(_ => false);
                        acknowledged = await _context.CronTickerOccurrences.UpdateOneAsync(
                            s, filter, MongoUpdateBuilders.BuildCronOccurrenceUpdate<TCronTicker>(
                                functionContext, now, NextLeaseUntil(now)), cancellationToken: ct).ConfigureAwait(false);
                        resultKind = CronOccurrenceResultKind;
                    }
                    else
                    {
                        if (functionContext.ParentId != null &&
                            !await LockCurrentChainGenerationAsync(s, functionContext, ct).ConfigureAwait(false))
                            return false;
                        if (functionContext.ParentId != null &&
                            (!functionContext.AcquisitionToken.HasValue ||
                             functionContext.AcquisitionToken != functionContext.ChainGeneration))
                            return false;
                        var fb = Builders<TTimeTicker>.Filter;
                        var filter = InPartition(fb.And(fb.Eq(x => x.Id, functionContext.TickerId),
                            fb.Ne(x => x.LockHolder, RetentionLockHolder)));
                        if (functionContext.ParentId != null)
                            filter &= fb.And(
                                fb.Eq(x => x.ParentId, functionContext.ParentId),
                                fb.Eq(x => x.ChainRootId, functionContext.ChainRootId),
                                fb.Eq(x => x.ChainGeneration, functionContext.ChainGeneration),
                                fb.Eq(x => x.AcquisitionToken, functionContext.AcquisitionToken));
                        else
                            filter &= fb.And(fb.Eq(x => x.LockHolder, _lockHolder),
                                fb.Eq(x => x.AcquisitionToken, functionContext.AcquisitionToken));
                        acknowledged = await _context.TimeTickers.UpdateOneAsync(
                            s, filter, MongoUpdateBuilders.BuildTimeTickerUpdate<TTimeTicker>(
                                functionContext, now, NextLeaseUntil(now)), cancellationToken: ct).ConfigureAwait(false);
                        resultKind = TimeResultKind;
                    }

                    if (acknowledged.MatchedCount != 1) return false;
                    if (functionContext.Status is TickerStatus.Done or TickerStatus.DueDone)
                    {
                        await StoreOrClearResultAsync(s, functionContext.TickerId, resultKind,
                            functionContext.ResultEnvelope, ct).ConfigureAwait(false);
                        if (AfterResultMutationForTestAsync != null)
                            await AfterResultMutationForTestAsync(ct).ConfigureAwait(false);
                    }
                    await _context.NodeFinalizations.InsertOneAsync(
                        s, expected, cancellationToken: ct).ConfigureAwait(false);
                    if (AfterNodeFinalizationInsertForTestAsync != null)
                        await AfterNodeFinalizationInsertForTestAsync(ct).ConfigureAwait(false);
                    return true;
                }, GraphTransactionOptions, cancellationToken).ConfigureAwait(false);
                if (AfterNodeFinalizationTransactionForTestAsync != null)
                    await AfterNodeFinalizationTransactionForTestAsync(CancellationToken.None).ConfigureAwait(false);
                return committed;
            }
            catch (MongoCommandException ex) when (IsCanonicalTransactionsUnsupported(ex))
            {
                throw new NotSupportedException(
                    "Durable Mongo Node finalization requires a replica set transaction.", ex);
            }
            catch (Exception ex) when (IsAmbiguousCommitFailure(ex))
            {
                if (await ResolveAmbiguousNodeFinalizationCommitAsync(intent.OutboxId, expected)
                        .ConfigureAwait(false))
                    return true;
                throw;
            }
        }

        private void EnsureExactTerminalPartition(InternalFunctionContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            if (string.IsNullOrWhiteSpace(context.RuntimePartitionKey))
            {
                if (_runtimePartitionKey == TickerQRuntimePartition.LegacyGlobal.StorageKey)
                {
                    context.RuntimePartitionKey = _runtimePartitionKey;
                    return;
                }
                throw new InvalidOperationException("A terminal mutation requires an exact runtime partition identity.");
            }
            if (!StringComparer.Ordinal.Equals(context.RuntimePartitionKey, _runtimePartitionKey))
                throw new InvalidOperationException("Terminal mutation runtime partition mismatch.");
        }

        private FilterDefinition<BsonDocument> ExactNodeFinalizationClaimFilter(NodeFinalizationClaim claim)
        {
            var intent = claim.Intent;
            var fb = Builders<BsonDocument>.Filter;
            return fb.And(
                fb.Eq("_id", PartitionedDocumentId("node-finalization", intent.OutboxId)),
                fb.Eq("ApplicationNamespaceKey", _runtimePartitionKey),
                fb.Eq("TickerType", (int)intent.TickerType),
                fb.Eq("TickerId", GuidValue(intent.TickerId)),
                fb.Eq("AcquisitionToken", GuidValue(intent.AcquisitionToken)),
                fb.Eq("DispatchId", GuidValue(intent.DispatchId)),
                fb.Eq("NodeEpoch", GuidValue(intent.NodeEpoch)),
                fb.Eq("ClaimToken", GuidValue(claim.ClaimToken)),
                fb.Eq("ClaimedBy", claim.ClaimedBy));
        }

        public async Task<IReadOnlyList<NodeFinalizationClaim>> ClaimDueNodeFinalizationsAsync(
            string workerId, int maxCount, DateTime nowUtc, DateTime leaseUntilUtc,
            CancellationToken cancellationToken = default)
        {
            if (!SupportsDurableNodeFinalizationOutbox) return Array.Empty<NodeFinalizationClaim>();
            if (string.IsNullOrWhiteSpace(workerId) || workerId.Length > NodeFinalizationClaim.MaxClaimedByLength)
                throw new ArgumentException("Worker ID is required and must be bounded.", nameof(workerId));
            if (maxCount < 0) throw new ArgumentOutOfRangeException(nameof(maxCount));
            if (maxCount == 0) return Array.Empty<NodeFinalizationClaim>();
            if (nowUtc.Kind != DateTimeKind.Utc || leaseUntilUtc.Kind != DateTimeKind.Utc || leaseUntilUtc <= nowUtc)
                throw new ArgumentException("Claim times must be UTC and the lease must end after now.");

            var claims = new List<NodeFinalizationClaim>(maxCount);
            for (var i = 0; i < maxCount; i++)
            {
                var token = Guid.NewGuid();
                var document = await _context.NodeFinalizations.FindOneAndUpdateAsync(
                    Builders<BsonDocument>.Filter.And(
                        Builders<BsonDocument>.Filter.Eq("ApplicationNamespaceKey", _runtimePartitionKey),
                        Builders<BsonDocument>.Filter.Lte("AvailableAtUtc", nowUtc)),
                    Builders<BsonDocument>.Update
                        .Set("ClaimToken", GuidValue(token)).Set("ClaimedBy", workerId)
                        .Set("AvailableAtUtc", leaseUntilUtc).Set("LastAttemptAtUtc", nowUtc)
                        .Set("LastErrorCode", BsonNull.Value).Inc("AttemptCount", 1),
                    new FindOneAndUpdateOptions<BsonDocument>
                    {
                        Sort = Builders<BsonDocument>.Sort.Ascending("AvailableAtUtc").Ascending("_id"),
                        ReturnDocument = ReturnDocument.After
                    }, cancellationToken).ConfigureAwait(false);
                if (document == null) break;
                claims.Add(new NodeFinalizationClaim(ReadNodeFinalizationIntent(document), token, workerId,
                    document["AvailableAtUtc"].ToUniversalTime(), document["AttemptCount"].AsInt32));
            }
            return claims;
        }

        public async Task<bool> CompleteNodeFinalizationAsync(
            NodeFinalizationClaim claim, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(claim);
            if (!SupportsDurableNodeFinalizationOutbox) return false;
            var result = await _context.NodeFinalizations.DeleteOneAsync(
                ExactNodeFinalizationClaimFilter(claim), cancellationToken).ConfigureAwait(false);
            return result.DeletedCount == 1;
        }

        public async Task<bool> RescheduleNodeFinalizationAsync(
            NodeFinalizationClaim claim, DateTime availableAtUtc, string errorCode,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(claim);
            if (availableAtUtc.Kind != DateTimeKind.Utc)
                throw new ArgumentException("AvailableAtUtc must be UTC.", nameof(availableAtUtc));
            if (string.IsNullOrWhiteSpace(errorCode) || errorCode.Length > NodeFinalizationOperationalState.MaxErrorCodeLength)
                throw new ArgumentException("Error code is required and must be bounded.", nameof(errorCode));
            if (!SupportsDurableNodeFinalizationOutbox) return false;
            var result = await _context.NodeFinalizations.UpdateOneAsync(
                ExactNodeFinalizationClaimFilter(claim),
                Builders<BsonDocument>.Update.Set("AvailableAtUtc", availableAtUtc)
                    .Set("ClaimToken", BsonNull.Value).Set("ClaimedBy", BsonNull.Value)
                    .Set("LastErrorCode", errorCode), cancellationToken: cancellationToken).ConfigureAwait(false);
            return result.MatchedCount == 1;
        }

        private async Task<bool> CommitTerminalTickerCoreAsync(
            InternalFunctionContext functionContext, bool enforceRemoteChildToken,
            CancellationToken cancellationToken = default)
        {
            if (functionContext == null)
                throw new ArgumentNullException(nameof(functionContext));
            var successful = functionContext.Status is TickerStatus.Done or TickerStatus.DueDone;
            if (!functionContext.GetPropsToUpdate().Contains(nameof(InternalFunctionContext.Status)) ||
                functionContext.Status is not (TickerStatus.Done or TickerStatus.DueDone or TickerStatus.Failed
                    or TickerStatus.Cancelled or TickerStatus.Skipped) ||
                (successful && !functionContext.GetPropsToUpdate().Contains(nameof(InternalFunctionContext.ResultEnvelope))))
                throw new InvalidOperationException(
                    "Acknowledged persistence accepts only a terminal mutation; success requires an explicit optional result envelope.");

            if (functionContext.ResultEnvelope != null)
                ValidateResult(functionContext.ResultEnvelope);
            if (TransactionsKnownUnavailable)
                throw new NotSupportedException(
                    "Atomic Mongo result publication requires a replica set transaction.");

            var resultKind = functionContext.Type == TickerType.CronTickerOccurrence
                ? CronOccurrenceResultKind
                : TimeResultKind;
            var expectedEvidence = ToTerminalEvidenceDocument(
                functionContext.TickerId, resultKind, functionContext);
            if (await HasExactTerminalEvidenceAsync(expectedEvidence, functionContext).ConfigureAwait(false))
                return true;

            using var session = await _context.Database.Client
                .StartSessionAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            try
            {
                var committed = await session.WithTransactionAsync(
                    async (s, ct) =>
                    {
                        await TouchGraphFenceAsync(s, ct).ConfigureAwait(false);
                        var now = _clock.UtcNow;
                        UpdateResult acknowledged;

                        if (functionContext.Type == TickerType.CronTickerOccurrence)
                        {
                            var fb = Builders<CronTickerOccurrenceEntity<TCronTicker>>.Filter;
                            var filter = functionContext.AcquisitionToken.HasValue
                                ? fb.And(
                                    fb.Eq(x => x.ApplicationNamespaceKey, _runtimePartitionKey),
                                    fb.Eq(x => x.Id, functionContext.TickerId),
                                    fb.Eq(x => x.LockHolder, _lockHolder),
                                    fb.Eq(x => x.AcquisitionToken, functionContext.AcquisitionToken))
                                : fb.Where(_ => false);
                            acknowledged = await _context.CronTickerOccurrences.UpdateOneAsync(
                                s, filter,
                                MongoUpdateBuilders.BuildCronOccurrenceUpdate<TCronTicker>(
                                    functionContext, now, NextLeaseUntil(now)),
                                cancellationToken: ct).ConfigureAwait(false);
                        }
                        else
                        {
                            if (functionContext.ParentId != null &&
                                !await LockCurrentChainGenerationAsync(s, functionContext, ct).ConfigureAwait(false))
                                return false;
                            if (functionContext.ParentId != null && enforceRemoteChildToken &&
                                (!functionContext.AcquisitionToken.HasValue ||
                                 functionContext.AcquisitionToken != functionContext.ChainGeneration))
                                return false;
                            var fb = Builders<TTimeTicker>.Filter;
                            var filter = fb.And(
                                fb.Eq(x => x.ApplicationNamespaceKey, _runtimePartitionKey),
                                fb.Eq(x => x.Id, functionContext.TickerId),
                                fb.Ne(x => x.LockHolder, RetentionLockHolder));
                            if (functionContext.ParentId != null)
                                filter &= fb.Eq(x => x.ChainRootId, functionContext.ChainRootId);
                            if (functionContext.ParentId == null)
                                filter &= functionContext.AcquisitionToken.HasValue
                                    ? fb.And(
                                        fb.Eq(x => x.LockHolder, _lockHolder),
                                        fb.Eq(x => x.AcquisitionToken, functionContext.AcquisitionToken))
                                    : fb.Where(_ => false);
                            acknowledged = await _context.TimeTickers.UpdateOneAsync(
                                s, filter,
                                MongoUpdateBuilders.BuildTimeTickerUpdate<TTimeTicker>(
                                    functionContext, now, NextLeaseUntil(now)),
                                cancellationToken: ct).ConfigureAwait(false);
                        }

                        if (acknowledged.MatchedCount != 1)
                            return false;

                        await StoreTerminalEvidenceAsync(s, expectedEvidence, ct).ConfigureAwait(false);
                        if (successful && AfterResultMutationForTestAsync != null)
                            await AfterResultMutationForTestAsync(ct).ConfigureAwait(false);
                        return true;
                    }, GraphTransactionOptions, cancellationToken).ConfigureAwait(false);
                if (AfterTerminalTransactionForTestAsync != null)
                    await AfterTerminalTransactionForTestAsync(CancellationToken.None).ConfigureAwait(false);
                return committed;
            }
            catch (MongoCommandException ex) when (IsCanonicalTransactionsUnsupported(ex))
            {
                throw new NotSupportedException(
                    "Atomic Mongo result publication requires a replica set transaction.", ex);
            }
            catch (Exception ex) when (ex is OperationCanceledException || IsAmbiguousCommitFailure(ex))
            {
                if (await HasExactTerminalEvidenceAsync(expectedEvidence, functionContext).ConfigureAwait(false))
                    return true;
                throw;
            }
        }

        // ===================================================================
        // Time Ticker — core scheduler methods
        // ===================================================================

        public async IAsyncEnumerable<TimeTickerEntity> QueueTimeTickers(
            TimeTickerEntity[] timeTickers,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (timeTickers == null || timeTickers.Length == 0)
                yield break;
            if (TransactionsKnownUnavailable)
                throw new NotSupportedException(
                    "Atomic Mongo scheduler chain acquisition requires a replica set transaction.");

            var now = _clock.UtcNow;
            var coll = _context.TimeTickers;
            var fb = Builders<TTimeTicker>.Filter;
            var acquired = new List<(TimeTickerEntity Input, TTimeTicker Row, Guid Generation)>();
            using var session = await _context.Database.Client
                .StartSessionAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            try
            {
                acquired = await session.WithTransactionAsync(async (s, ct) =>
                {
                    if (!await LockRunnableAdmissionAsync(s, ct).ConfigureAwait(false))
                        return new List<(TimeTickerEntity, TTimeTicker, Guid)>();
                    await TouchGraphFenceAsync(s, ct).ConfigureAwait(false);
                    var winners = new List<(TimeTickerEntity, TTimeTicker, Guid)>();
                    foreach (var ticker in timeTickers)
                    {
                        ct.ThrowIfCancellationRequested();
                        var generation = Guid.NewGuid();
                        var filter = fb.And(
                            fb.Eq(x => x.ApplicationNamespaceKey, _runtimePartitionKey),
                            fb.Eq(x => x.Id, ticker.Id),
                            fb.Eq(x => x.UpdatedAt, ticker.UpdatedAt),
                            fb.Eq(x => x.AcquisitionToken, ticker.AcquisitionToken));
                        var update = Builders<TTimeTicker>.Update
                            .Set(x => x.LockHolder, _lockHolder)
                            .Set(x => x.LockedAt, now)
                            .Set(x => x.AcquisitionToken, generation)
                            .Set(x => x.ChainRootId, ticker.Id)
                            .Set(x => x.ChainGeneration, generation)
                            .Set(x => x.UpdatedAt, now)
                            .Set(x => x.Status, TickerStatus.Queued);
                        var row = await coll.FindOneAndUpdateAsync(
                            s, filter, update,
                            new FindOneAndUpdateOptions<TTimeTicker> { ReturnDocument = ReturnDocument.After }, ct)
                            .ConfigureAwait(false);
                        if (row != null) winners.Add((ticker, row, generation));
                    }
                    await NormalizeChainDescendantsAsync(
                        s, winners.ToDictionary(x => x.Item2.Id, x => x.Item3), ct).ConfigureAwait(false);
                    return winners;
                }, GraphTransactionOptions, cancellationToken).ConfigureAwait(false);
            }
            catch (MongoCommandException ex) when (IsCanonicalTransactionsUnsupported(ex))
            {
                throw new NotSupportedException(
                    "Atomic Mongo scheduler chain acquisition requires a replica set transaction.", ex);
            }

            foreach (var (ticker, _, generation) in acquired)
            {
                ticker.UpdatedAt = now;
                ticker.LockHolder = _lockHolder;
                ticker.LockedAt = now;
                ticker.AcquisitionToken = generation;
                StampQueueGeneration(ticker, ticker.Id, generation);
                ticker.Status = TickerStatus.Queued;
                yield return ticker;
            }
        }

        public async IAsyncEnumerable<TimeTickerEntity> QueueTimedOutTimeTickers(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (TransactionsKnownUnavailable)
                throw new NotSupportedException(
                    "Atomic Mongo scheduler chain acquisition requires a replica set transaction.");

            var now = _clock.UtcNow;
            var fallbackThreshold = now.AddSeconds(-1);
            var coll = _context.TimeTickers;
            var fb = Builders<TTimeTicker>.Filter;

            var candidatesFilter = fb.And(
                fb.Eq(x => x.ApplicationNamespaceKey, _runtimePartitionKey),
                fb.Ne(x => x.ExecutionTime, null),
                fb.In(x => x.Status, new[] { TickerStatus.Idle, TickerStatus.Queued }),
                fb.Lte(x => x.ExecutionTime, fallbackThreshold));

            var candidates = await coll.Find(candidatesFilter).ToListAsync(cancellationToken).ConfigureAwait(false);
            var acquired = new List<TTimeTicker>();
            using var session = await _context.Database.Client
                .StartSessionAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            try
            {
                acquired = await session.WithTransactionAsync(async (s, ct) =>
                {
                    if (!await LockRunnableAdmissionAsync(s, ct).ConfigureAwait(false))
                        return new List<TTimeTicker>();
                    await TouchGraphFenceAsync(s, ct).ConfigureAwait(false);
                    var winners = new List<TTimeTicker>();
                    foreach (var candidate in candidates)
                    {
                        ct.ThrowIfCancellationRequested();
                        var generation = Guid.NewGuid();
                        var filter = fb.And(
                            fb.Eq(x => x.ApplicationNamespaceKey, _runtimePartitionKey),
                            fb.Eq(x => x.Id, candidate.Id),
                            fb.Lte(x => x.UpdatedAt, candidate.UpdatedAt));
                        var update = Builders<TTimeTicker>.Update
                            .Set(x => x.LockHolder, _lockHolder)
                            .Set(x => x.LockedAt, now)
                            .Set(x => x.LeaseUntil, NextLeaseUntil(now))
                            .Set(x => x.AcquisitionToken, generation)
                            .Set(x => x.ChainRootId, candidate.Id)
                            .Set(x => x.ChainGeneration, generation)
                            .Set(x => x.UpdatedAt, now)
                            .Set(x => x.Status, TickerStatus.InProgress);
                        var row = await coll.FindOneAndUpdateAsync(
                            s, filter, update,
                            new FindOneAndUpdateOptions<TTimeTicker> { ReturnDocument = ReturnDocument.After }, ct)
                            .ConfigureAwait(false);
                        if (row != null) winners.Add(row);
                    }
                    await NormalizeChainDescendantsAsync(
                        s, winners.ToDictionary(x => x.Id, x => x.ChainGeneration!.Value), ct)
                        .ConfigureAwait(false);
                    return winners;
                }, GraphTransactionOptions, cancellationToken).ConfigureAwait(false);
            }
            catch (MongoCommandException ex) when (IsCanonicalTransactionsUnsupported(ex))
            {
                throw new NotSupportedException(
                    "Atomic Mongo scheduler chain acquisition requires a replica set transaction.", ex);
            }

            var byParent = await LoadChildrenLookup(
                acquired.Select(c => c.Id).ToArray(), cancellationToken).ConfigureAwait(false);
            foreach (var candidate in acquired)
                yield return BuildQueuedEntity(candidate, byParent);
        }

        public async Task ReleaseAcquiredTimeTickers(Guid[] timeTickerIds, CancellationToken cancellationToken = default)
        {
            var now = _clock.UtcNow;
            var coll = _context.TimeTickers;
            var fb = Builders<TTimeTicker>.Filter;

            var canAcquire = MongoUpdateBuilders.CanAcquireTimeTicker<TTimeTicker>(_lockHolder);
            var filter = timeTickerIds.Length == 0
                ? InPartition(canAcquire)
                : InPartition(fb.And(fb.In(x => x.Id, timeTickerIds), canAcquire));

            var update = Builders<TTimeTicker>.Update
                .Set(x => x.LockHolder, (string)null)
                .Set(x => x.LockedAt, (DateTime?)null)
                .Set(x => x.LeaseUntil, (DateTime?)null)
                .Set(x => x.AcquisitionToken, (Guid?)null)
                .Set(x => x.Status, TickerStatus.Idle)
                .Set(x => x.UpdatedAt, now);

            await ExecuteAdmittedGraphMutationAsync(
                async (session, ct) => (int)(await coll.UpdateManyAsync(
                    session, filter, update, cancellationToken: ct).ConfigureAwait(false)).ModifiedCount,
                async ct => (int)(await coll.UpdateManyAsync(
                    filter, update, cancellationToken: ct).ConfigureAwait(false)).ModifiedCount,
                cancellationToken).ConfigureAwait(false);
        }

        public async Task<TimeTickerEntity[]> GetEarliestTimeTickers(CancellationToken cancellationToken = default)
        {
            if (!await IsRunnableAdmissionAllowedAsync(cancellationToken).ConfigureAwait(false))
                return Array.Empty<TimeTickerEntity>();
            var now = _clock.UtcNow;
            var oneSecondAgo = now.AddSeconds(-1);
            var coll = _context.TimeTickers;
            var fb = Builders<TTimeTicker>.Filter;

            var baseFilter = fb.And(
                fb.Eq(x => x.ApplicationNamespaceKey, _runtimePartitionKey),
                fb.Ne(x => x.ExecutionTime, null),
                fb.Gte(x => x.ExecutionTime, oneSecondAgo),
                MongoUpdateBuilders.CanAcquireTimeTicker<TTimeTicker>(_lockHolder));

            var earliest = await coll
                .Find(baseFilter)
                .Sort(Builders<TTimeTicker>.Sort.Ascending(x => x.ExecutionTime))
                .Limit(1)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            if (earliest?.ExecutionTime == null)
                return Array.Empty<TimeTickerEntity>();

            var min = earliest.ExecutionTime.Value;
            var minSecond = new DateTime(min.Year, min.Month, min.Day, min.Hour, min.Minute, min.Second, DateTimeKind.Utc);
            var maxExecutionTime = minSecond.AddSeconds(1);

            var windowFilter = fb.And(
                baseFilter,
                fb.Gte(x => x.ExecutionTime, minSecond),
                fb.Lt(x => x.ExecutionTime, maxExecutionTime));

            var rows = await coll
                .Find(windowFilter)
                .Sort(Builders<TTimeTicker>.Sort.Ascending(x => x.ExecutionTime))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var byParent = await LoadChildrenLookup(rows.Select(r => r.Id).ToArray(), cancellationToken).ConfigureAwait(false);
            return rows.Select(r => BuildQueuedEntity(r, byParent)).ToArray();
        }

        public async Task<int> UpdateTimeTicker(InternalFunctionContext functionContext, CancellationToken cancellationToken = default)
        {
            var now = _clock.UtcNow;
            var update = MongoUpdateBuilders.BuildTimeTickerUpdate<TTimeTicker>(functionContext, now, NextLeaseUntil(now));
            var filter = Builders<TTimeTicker>.Filter.And(
                Builders<TTimeTicker>.Filter.Eq(x => x.ApplicationNamespaceKey, _runtimePartitionKey),
                Builders<TTimeTicker>.Filter.Eq(x => x.Id, functionContext.TickerId),
                Builders<TTimeTicker>.Filter.Ne(x => x.LockHolder, RetentionLockHolder));
            if (IsFencedTerminalWrite(functionContext) && functionContext.ParentId == null)
            {
                var fb = Builders<TTimeTicker>.Filter;
                filter &= functionContext.AcquisitionToken.HasValue
                    ? fb.And(fb.Eq(x => x.LockHolder, _lockHolder),
                             fb.Eq(x => x.AcquisitionToken, functionContext.AcquisitionToken))
                    : fb.Where(_ => false);
            }

            if (functionContext.ParentId == null)
            {
                var rootResult = await _context.TimeTickers.UpdateOneAsync(
                    filter, update, cancellationToken: cancellationToken).ConfigureAwait(false);
                return (int)rootResult.ModifiedCount;
            }

            if (TransactionsKnownUnavailable)
                throw new NotSupportedException("Durable Mongo chain generation fencing requires a replica set transaction.");
            using var session = await _context.Database.Client.StartSessionAsync(
                cancellationToken: cancellationToken).ConfigureAwait(false);
            try
            {
                return await session.WithTransactionAsync(async (s, ct) =>
                {
                    await TouchGraphFenceAsync(s, ct).ConfigureAwait(false);
                    if (!await LockCurrentChainGenerationAsync(s, functionContext, ct).ConfigureAwait(false))
                        return 0;
                    filter &= Builders<TTimeTicker>.Filter.Eq(x => x.ChainRootId, functionContext.ChainRootId);
                    var childResult = await _context.TimeTickers.UpdateOneAsync(
                        s, filter, update, cancellationToken: ct).ConfigureAwait(false);
                    return (int)childResult.ModifiedCount;
                }, GraphTransactionOptions, cancellationToken).ConfigureAwait(false);
            }
            catch (MongoCommandException ex) when (IsCanonicalTransactionsUnsupported(ex))
            {
                throw new NotSupportedException(
                    "Durable Mongo chain generation fencing requires a replica set transaction.", ex);
            }
        }

        public async Task<byte[]> GetTimeTickerRequest(Guid id, CancellationToken cancellationToken)
        {
            var ticker = await _context.TimeTickers
                .Find(InPartition(Builders<TTimeTicker>.Filter.Eq(x => x.Id, id)))
                .Project(x => x.Request)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
            return ticker;
        }

        public async Task UpdateTimeTickersWithUnifiedContext(Guid[] timeTickerIds, InternalFunctionContext functionContext, CancellationToken cancellationToken = default)
        {
            if (timeTickerIds.Length == 0) return;
            var now = _clock.UtcNow;
            var update = MongoUpdateBuilders.BuildTimeTickerUpdate<TTimeTicker>(functionContext, now, NextLeaseUntil(now));
            await _context.TimeTickers
                .UpdateManyAsync(
                    InPartition(Builders<TTimeTicker>.Filter.And(
                        Builders<TTimeTicker>.Filter.In(x => x.Id, timeTickerIds),
                        Builders<TTimeTicker>.Filter.Ne(x => x.LockHolder, RetentionLockHolder))),
                    update,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task<Guid[]> TransitionQueuedTimeTickersToInProgressAsync(
            IReadOnlyCollection<AcquisitionLease> leases, CancellationToken cancellationToken = default)
        {
            if (TransactionsKnownUnavailable) return Array.Empty<Guid>();
            var now = _clock.UtcNow;
            var fb = Builders<TTimeTicker>.Filter;
            var update = Builders<TTimeTicker>.Update
                .Set(x => x.Status, TickerStatus.InProgress)
                .Set(x => x.LeaseUntil, NextLeaseUntil(now))
                .Set(x => x.UpdatedAt, now);
            using var session = await _context.Database.Client
                .StartSessionAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            return await session.WithTransactionAsync(async (s, ct) =>
            {
                if (!await LockRunnableAdmissionAsync(s, ct).ConfigureAwait(false))
                    return Array.Empty<Guid>();
                var winners = new List<Guid>(leases.Count);
                foreach (var lease in leases.Where(x => x.AcquisitionToken.HasValue).Distinct())
                {
                    var filter = InPartition(fb.And(
                        fb.Eq(x => x.Id, lease.TickerId),
                        fb.Eq(x => x.Status, TickerStatus.Queued),
                        fb.Eq(x => x.LockHolder, _lockHolder),
                        fb.Eq(x => x.AcquisitionToken, lease.AcquisitionToken)));
                    var result = await _context.TimeTickers.UpdateOneAsync(
                        s, filter, update, cancellationToken: ct).ConfigureAwait(false);
                    if (result.ModifiedCount == 1) winners.Add(lease.TickerId);
                }
                return winners.ToArray();
            }, GraphTransactionOptions, cancellationToken).ConfigureAwait(false);
        }

        public async Task<TimeTickerEntity[]> AcquireImmediateTimeTickersAsync(Guid[] ids, CancellationToken cancellationToken = default)
        {
            if (ids == null || ids.Length == 0) return Array.Empty<TimeTickerEntity>();
            if (TransactionsKnownUnavailable)
                throw new NotSupportedException(
                    "Atomic Mongo chain acquisition requires a replica set transaction.");

            var now = _clock.UtcNow;
            var acquisitionToken = Guid.NewGuid();
            var coll = _context.TimeTickers;
            var fb = Builders<TTimeTicker>.Filter;
            var update = Builders<TTimeTicker>.Update
                .Set(x => x.LockHolder, _lockHolder)
                .Set(x => x.LockedAt, now)
                .Set(x => x.LeaseUntil, NextLeaseUntil(now))
                .Set(x => x.AcquisitionToken, acquisitionToken)
                .Set(x => x.ChainGeneration, acquisitionToken)
                .Set(x => x.Status, TickerStatus.InProgress)
                .Set(x => x.UpdatedAt, now);
            var rows = new List<TTimeTicker>(ids.Length);

            foreach (var id in ids.Distinct())
            {
                using var session = await _context.Database.Client
                    .StartSessionAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
                try
                {
                    var acquired = await session.WithTransactionAsync(async (s, ct) =>
                    {
                        if (!await LockRunnableAdmissionAsync(s, ct).ConfigureAwait(false))
                            return null;
                        await TouchGraphFenceAsync(s, ct).ConfigureAwait(false);
                        var filter = InPartition(fb.And(
                            fb.Eq(x => x.Id, id),
                            MongoUpdateBuilders.CanAcquireTimeTicker<TTimeTicker>(_lockHolder)));
                        var row = await coll.FindOneAndUpdateAsync(
                            s, filter, update.Set(x => x.ChainRootId, id),
                            new FindOneAndUpdateOptions<TTimeTicker> { ReturnDocument = ReturnDocument.After }, ct)
                            .ConfigureAwait(false);
                        if (row != null)
                            await NormalizeChainDescendantsAsync(s, id, acquisitionToken, ct).ConfigureAwait(false);
                        return row;
                    }, GraphTransactionOptions, cancellationToken).ConfigureAwait(false);
                    if (acquired != null) rows.Add(acquired);
                }
                catch (MongoCommandException ex) when (IsCanonicalTransactionsUnsupported(ex))
                {
                    throw new NotSupportedException(
                        "Atomic Mongo chain acquisition requires a replica set transaction.", ex);
                }
            }

            if (rows.Count == 0) return Array.Empty<TimeTickerEntity>();
            var byParent = await LoadChildrenLookup(rows.Select(r => r.Id).ToArray(), cancellationToken).ConfigureAwait(false);
            return rows.Select(r => BuildQueuedEntity(r, byParent)).ToArray();
        }

        public async Task<TimeTickerEntity> AcquireTimeTickerOnDemandAsync(
            Guid id, DateTime executionTime, CancellationToken cancellationToken = default)
        {
            var now = _clock.UtcNow;
            var token = Guid.NewGuid();
            var fb = Builders<TTimeTicker>.Filter;
            var terminal = fb.In(x => x.Status, TerminalStatuses);
            var terminalOwnershipAvailable = fb.Or(
                fb.Eq(x => x.AcquisitionToken, (Guid?)null),
                fb.Ne(x => x.LockHolder, RetentionLockHolder),
                fb.And(
                    fb.Eq(x => x.LockHolder, RetentionLockHolder),
                    fb.Lte(x => x.LeaseUntil, now)));
            var eligible = fb.Or(
                fb.Eq(x => x.Status, TickerStatus.Idle),
                fb.And(fb.Eq(x => x.Status, TickerStatus.Queued),
                    fb.Or(fb.Eq(x => x.LockHolder, null), fb.Eq(x => x.LockHolder, _lockHolder))),
                fb.And(terminal, terminalOwnershipAvailable));
            var filter = InPartition(fb.And(fb.Eq(x => x.Id, id), eligible));
            var update = Builders<TTimeTicker>.Update
                .Set(x => x.ExecutionTime, executionTime)
                .Set(x => x.Status, TickerStatus.InProgress)
                .Set(x => x.LockHolder, _lockHolder)
                .Set(x => x.LockedAt, now)
                .Set(x => x.LeaseUntil, NextLeaseUntil(now))
                .Set(x => x.AcquisitionToken, token)
                .Set(x => x.ChainRootId, id)
                .Set(x => x.ChainGeneration, token)
                .Set(x => x.RetryCount, 0)
                .Set(x => x.ExceptionMessage, (string)null)
                .Set(x => x.SkippedReason, (string)null)
                .Set(x => x.ExecutedAt, (DateTime?)null)
                .Set(x => x.ElapsedTime, 0L)
                .Set(x => x.StaleRestartCount, 0)
                .Set(x => x.UpdatedAt, now);
            if (TransactionsKnownUnavailable)
                throw new NotSupportedException(
                    "Atomic Mongo ticker restart requires a replica set transaction.");

            using var session = await _context.Database.Client
                .StartSessionAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            TTimeTicker row;
            try
            {
                row = await session.WithTransactionAsync(
                    async (s, ct) =>
                    {
                        if (!await LockRunnableAdmissionAsync(s, ct).ConfigureAwait(false))
                            return null;
                        await TouchGraphFenceAsync(s, ct).ConfigureAwait(false);
                        var acquired = await _context.TimeTickers.FindOneAndUpdateAsync(
                            s, filter, update,
                            new FindOneAndUpdateOptions<TTimeTicker> { ReturnDocument = ReturnDocument.After },
                            ct).ConfigureAwait(false);
                        if (acquired == null) return null;
                        await NormalizeChainDescendantsAsync(s, id, token, ct).ConfigureAwait(false);
                        await _context.TickerResults.DeleteOneAsync(
                            s, ResultFilter(id, TimeResultKind),
                            new DeleteOptions(), ct).ConfigureAwait(false);
                        return acquired;
                    }, GraphTransactionOptions, cancellationToken).ConfigureAwait(false);
            }
            catch (MongoCommandException ex) when (IsCanonicalTransactionsUnsupported(ex))
            {
                throw new NotSupportedException(
                    "Atomic Mongo ticker restart requires a replica set transaction.", ex);
            }
            if (row == null) return null;

            var byParent = await LoadChildrenLookup([row.Id], cancellationToken).ConfigureAwait(false);
            return BuildQueuedEntity(row, byParent);
        }

        public async Task ReleaseDeadNodeTimeTickerResources(string instanceIdentifier, CancellationToken cancellationToken = default)
        {
            var now = _clock.UtcNow;
            var coll = _context.TimeTickers;
            var fb = Builders<TTimeTicker>.Filter;
            var firstFilter = MongoUpdateBuilders.CanAcquireTimeTicker<TTimeTicker>(instanceIdentifier);
            var secondFilter = fb.And(fb.Eq(x => x.LockHolder, instanceIdentifier),
                fb.Eq(x => x.Status, TickerStatus.InProgress));
            var update = Builders<TTimeTicker>.Update
                    .Set(x => x.LockHolder, (string)null)
                    .Set(x => x.LockedAt, (DateTime?)null)
                    .Set(x => x.LeaseUntil, (DateTime?)null)
                    .Set(x => x.AcquisitionToken, (Guid?)null)
                    .Set(x => x.Status, TickerStatus.Idle)
                    .Set(x => x.UpdatedAt, now);
            await ExecuteAdmittedGraphMutationAsync(
                async (session, ct) =>
                {
                    var first = await coll.UpdateManyAsync(session, firstFilter, update,
                        cancellationToken: ct).ConfigureAwait(false);
                    var second = await coll.UpdateManyAsync(session, secondFilter, update,
                        cancellationToken: ct).ConfigureAwait(false);
                    return (int)(first.ModifiedCount + second.ModifiedCount);
                },
                async ct =>
                {
                    var first = await coll.UpdateManyAsync(firstFilter, update,
                        cancellationToken: ct).ConfigureAwait(false);
                    var second = await coll.UpdateManyAsync(secondFilter, update,
                        cancellationToken: ct).ConfigureAwait(false);
                    return (int)(first.ModifiedCount + second.ModifiedCount);
                }, cancellationToken).ConfigureAwait(false);
        }

        // ===================================================================
        // Cron Ticker — core methods
        // ===================================================================

        public Task MigrateDefinedCronTickers((string Function, string Expression)[] cronTickers, CancellationToken cancellationToken = default)
            => MigrateDefinedCronTickers(
                Array.ConvertAll(cronTickers, static ticker => new DefinedCronTickerSeed(ticker.Function, ticker.Expression)),
                cancellationToken);

        public Task MigrateDefinedCronTickers(DefinedCronTickerSeed[] cronTickers, CancellationToken cancellationToken = default)
            => MigrateDefinedCronTickers(new DefinedCronSeedManifest(cronTickers), cancellationToken);

        public Task MigrateDefinedCronTickers(DefinedCronSeedManifest manifest, CancellationToken cancellationToken = default)
            => MigrateDefinedCronTickers(manifest, 0, cancellationToken);

        private async Task MigrateDefinedCronTickers(
            DefinedCronSeedManifest manifest, int concurrencyAttempt, CancellationToken cancellationToken)
        {
            RuntimeManifestAdmission.Validate(manifest,
                _schedulerOptions.HasRuntimeActivationScopeBinding,
                _schedulerOptions.RuntimeSchedulerEnabled,
                _runtimeActivationScopeKey, _runtimeActivationEpoch);
            var now = _clock.UtcNow;
            var grace = _schedulerOptions.DefinedCronRetirementGracePeriod;
            var cronSet = _context.CronTickers;

            // Blocked seeds (canSeed == false) are present in the manifest but excluded from the desired
            // set, so they are handled by the retirement path below and retired IMMEDIATELY (no grace)
            // because continuing to schedule an unsatisfiable request is unsafe.
            var blockedFunctions = manifest.Seeds.Where(s => !s.CanSeed)
                .Select(s => s.Function).ToHashSet(StringComparer.Ordinal);

            // Phase A — non-destructive retirement of seeded rows no longer desired. Compared to the
            // DESIRED SEED MANIFEST (never the global runtime registry): comparing to the registry
            // conflated "function still registered" with "code still wants a seeded schedule", so removing
            // only a cron expression left the stale seeded row firing forever (Slice 1). Rows and their
            // occurrences/results are NEVER deleted here — retirement is a set of atomic per-row field
            // updates and does NOT depend on destructive orphan cleanup; retention owns history.
            // Dashboard-created crons (empty InitIdentifier), including those targeting SDK/remote
            // `name@node` functions the initializer never seeds, carry a non-seed identity and are never
            // candidates.
            var fb = Builders<TCronTicker>.Filter;
            var orphans = await cronSet
                .Find(InPartition(fb.And(
                    fb.Ne(x => x.InitIdentifier, null),
                    fb.Ne(x => x.InitIdentifier, string.Empty))))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            if (!manifest.IsLegacyGlobal)
            {
                foreach (var functionGroup in manifest.Seeds.Where(s => s.CanSeed).GroupBy(s => s.Function))
                {
                    var documentedLegacyKeys = functionGroup
                        .SelectMany(seed => CronSeedIdentity.LegacyAdoptionKeys(
                            manifest.ApplicationNamespace, seed.StableDefinitionId))
                        .ToHashSet(StringComparer.Ordinal);
                    var legacy = orphans.Where(x => x.Function == functionGroup.Key
                        && x.SeedOwnerNamespace == null
                        && (x.SeedKey == null || x.SeedKey == x.Function
                            || documentedLegacyKeys.Contains(x.SeedKey))).ToArray();
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

            foreach (var row in orphans.Where(o => manifest.IsLegacyGlobal
                         ? manifest.IsOrphanedSeedFunction(o.Function)
                         : o.SeedOwnerNamespace == manifest.ApplicationNamespace && manifest.IsOrphanedSeedKey(o.SeedKey)))
            {
                var immediate = blockedFunctions.Contains(row.Function);
                if (!TryBuildRetirementUpdate(row, r => CronSeedRetirement.ApplyRetirement(r, now, grace, immediate), now, out var update))
                {
                    if (row.RetiredAt.HasValue)
                        await RemoveUnleasedPendingCronOccurrencesAsync(
                            null, row.Id, now, cancellationToken).ConfigureAwait(false);
                    continue;
                }
                if (row.RetiredAt.HasValue)
                    await UpdateCronAndCleanupPendingAsync(
                        InPartition(fb.And(fb.Eq(x => x.Id, row.Id),
                            fb.Eq(x => x.DefinitionRevision, row.DefinitionRevision),
                            fb.Eq(x => x.SeedOwnerNamespace, row.SeedOwnerNamespace),
                            fb.Eq(x => x.SeedKey, row.SeedKey))),
                        row.Id, update, now, cancellationToken).ConfigureAwait(false);
                else
                    await cronSet.UpdateOneAsync(
                        InPartition(fb.Eq(x => x.Id, row.Id)), update, cancellationToken: cancellationToken).ConfigureAwait(false);
            }

            // Phase B — reconcile desired seeds into code-owned rows keyed by the stable SeedKey. Matching
            // is restricted to seeded rows (non-empty InitIdentifier) so a user/dashboard row sharing a
            // function name — carrying a null/non-seed identity — is never matched or mutated. A legacy
            // seeded row (null SeedKey) adopts its SeedKey IN PLACE (its _id, which occurrences reference,
            // never changes); a brand-new row uses the deterministic _id derived from the SeedKey so
            // concurrent first-time reconciles converge via a duplicate-key collision. Duplicate legacy
            // rows: a deterministic canonical row (lowest _id) adopts the SeedKey and stays enabled while
            // every redundant duplicate is disabled and marked retired IN PLACE (kept null-keyed so the
            // unique SeedKey index stays satisfied) but never deleted. A desired seed that (re)appeared has
            // any framework retirement state cleared, restoring only a framework-disabled row.
            var functions = manifest.DesiredSeedFunctions.ToArray();
            var existing = orphans.Where(x => functions.Contains(x.Function)).ToList();

            var byFunction = existing
                .GroupBy(c => c.Function)
                .ToDictionary(g => g.Key, g => g.OrderBy(c => c.Id).ToList());

            foreach (var seed in manifest.Seeds)
            {
                if (!seed.CanSeed)
                    continue;

                var seedKey = manifest.SeedKeyFor(seed);
                var documentedLegacyKeys = manifest.IsLegacyGlobal
                    ? Array.Empty<string>()
                    : CronSeedIdentity.LegacyAdoptionKeys(
                        manifest.ApplicationNamespace, seed.StableDefinitionId);

                var group = manifest.IsLegacyGlobal
                    ? (byFunction.TryGetValue(seed.Function, out var legacyGroup) ? legacyGroup : null)
                    : existing.Where(x =>
                            (x.SeedKey == seedKey && x.SeedOwnerNamespace == manifest.ApplicationNamespace)
                            || (x.Function == seed.Function && x.SeedOwnerNamespace == null
                                && manifest.MayAdoptLegacy(seed.Function)
                                && (x.SeedKey == null || x.SeedKey == x.Function
                                    || documentedLegacyKeys.Contains(x.SeedKey, StringComparer.Ordinal))))
                        .OrderBy(x => x.Id).ToList();
                if (group is { Count: > 0 })
                {
                    // Prefer the row that already owns this SeedKey, then an active row, then lowest _id —
                    // picking the lowest _id blindly would re-assign an already-owned SeedKey to a legacy
                    // null-key duplicate and violate the unique SeedKey index.
                    var (cron, duplicates) = CronSeedCanonical.Select(group, seedKey);

                    var sets = new List<UpdateDefinition<TCronTicker>>
                    {
                        Builders<TCronTicker>.Update.Set(x => x.SeedLastSeenAt, now)
                    };
                    var materiallyChanged = false;
                    var definitionChanged = false;

                    if (cron.SeedKey == null)
                    {
                        sets.Add(Builders<TCronTicker>.Update.Set(x => x.SeedKey, seedKey)); // adopt in place
                        materiallyChanged = true;
                    }
                    else if (!manifest.IsLegacyGlobal && cron.SeedKey != seedKey)
                    {
                        sets.Add(Builders<TCronTicker>.Update.Set(x => x.SeedKey, seedKey));
                        materiallyChanged = true;
                    }
                    if (!manifest.IsLegacyGlobal && cron.SeedOwnerNamespace != manifest.ApplicationNamespace)
                    {
                        sets.Add(Builders<TCronTicker>.Update.Set(x => x.SeedOwnerNamespace, manifest.ApplicationNamespace));
                        materiallyChanged = true;
                    }

                    if (!string.Equals(cron.Expression, seed.Expression, StringComparison.Ordinal))
                    {
                        sets.Add(Builders<TCronTicker>.Update.Set(x => x.Expression, seed.Expression));
                        materiallyChanged = true;
                        definitionChanged = true;
                    }

                    if (!string.IsNullOrEmpty(cron.InitIdentifier)
                        && !seed.MatchesIdentity(cron.RequestContractVersion, cron.RequestContractFingerprint))
                    {
                        sets.Add(Builders<TCronTicker>.Update.Set(x => x.RequestContractVersion, seed.RequestContractVersion));
                        sets.Add(Builders<TCronTicker>.Update.Set(x => x.RequestContractFingerprint, seed.RequestContractFingerprint));
                        materiallyChanged = true;
                        definitionChanged = true;
                    }

                    if (cron.Retries != seed.Retries)
                    {
                        sets.Add(Builders<TCronTicker>.Update.Set(x => x.Retries, seed.Retries));
                        materiallyChanged = true;
                        definitionChanged = true;
                    }
                    if (!(cron.RetryIntervals ?? Array.Empty<int>()).SequenceEqual(
                            seed.RetryIntervals ?? Array.Empty<int>()))
                    {
                        sets.Add(Builders<TCronTicker>.Update.Set(x => x.RetryIntervals, seed.RetryIntervals));
                        materiallyChanged = true;
                        definitionChanged = true;
                    }
                    if (cron.TimeoutSeconds != seed.TimeoutSeconds)
                    {
                        sets.Add(Builders<TCronTicker>.Update.Set(x => x.TimeoutSeconds, seed.TimeoutSeconds));
                        materiallyChanged = true;
                        definitionChanged = true;
                    }

                    if (definitionChanged)
                    {
                        sets.Add(Builders<TCronTicker>.Update.Set(
                            x => x.DefinitionRevision, Math.Max(1, cron.DefinitionRevision + 1)));
                        materiallyChanged = true;
                    }
                    else if (cron.DefinitionRevision <= 0)
                    {
                        sets.Add(Builders<TCronTicker>.Update.Set(x => x.DefinitionRevision, 1));
                        materiallyChanged = true;
                    }

                    // Desired-active seed: clear any framework retirement, restoring only framework-disabled state.
                    if (AddRetirementSets(cron, CronSeedRetirement.ClearRetirement, sets))
                        materiallyChanged = true;

                    if (materiallyChanged)
                        sets.Add(Builders<TCronTicker>.Update.Set(x => x.UpdatedAt, now));

                    var combinedUpdate = Builders<TCronTicker>.Update.Combine(sets);
                    var expectedFilter = InPartition(fb.And(
                        fb.Eq(x => x.Id, cron.Id),
                        fb.Eq(x => x.DefinitionRevision, cron.DefinitionRevision),
                        fb.Eq(x => x.SeedOwnerNamespace, cron.SeedOwnerNamespace),
                        fb.Eq(x => x.SeedKey, cron.SeedKey)));
                    var published = definitionChanged
                        ? await UpdateCronAndCleanupPendingAsync(
                            expectedFilter, cron.Id, combinedUpdate, now, cancellationToken).ConfigureAwait(false)
                        : (await cronSet.UpdateOneAsync(
                            expectedFilter, combinedUpdate,
                            cancellationToken: cancellationToken).ConfigureAwait(false)).MatchedCount == 1;
                    if (!published)
                    {
                        if (concurrencyAttempt >= 4)
                            throw new InvalidOperationException(
                                $"Cron definition '{cron.Id}' kept changing during reconciliation.");
                        await MigrateDefinedCronTickers(manifest, concurrencyAttempt + 1, cancellationToken)
                            .ConfigureAwait(false);
                        return;
                    }

                    // Retire redundant duplicates in place (canonical already chosen); never delete.
                    foreach (var dup in duplicates)
                    {
                        if (!TryBuildRetirementUpdate(dup, r => CronSeedRetirement.RetireDuplicate(r, now), now, out var dupUpdate))
                        {
                            // A prior interrupted pass may already have persisted retirement while leaving
                            // pending rows behind; reconciliation remains a repair operation on every retry.
                            if (dup.RetiredAt.HasValue)
                                await RemoveUnleasedPendingCronOccurrencesAsync(
                                    null, dup.Id, now, cancellationToken).ConfigureAwait(false);
                            continue;
                        }
                        if (dup.RetiredAt.HasValue)
                            await UpdateCronAndCleanupPendingAsync(
                                InPartition(fb.And(fb.Eq(x => x.Id, dup.Id),
                                    fb.Eq(x => x.DefinitionRevision, dup.DefinitionRevision),
                                    fb.Eq(x => x.SeedOwnerNamespace, dup.SeedOwnerNamespace),
                                    fb.Eq(x => x.SeedKey, dup.SeedKey))),
                                dup.Id, dupUpdate, now, cancellationToken).ConfigureAwait(false);
                        else
                            await cronSet.UpdateOneAsync(
                                InPartition(fb.Eq(x => x.Id, dup.Id)), dupUpdate, cancellationToken: cancellationToken).ConfigureAwait(false);
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
                        InitIdentifier = $"MemoryTicker_Seeded_{seed.Function}",
                        CreatedAt = now,
                        UpdatedAt = now,
                        Request = Array.Empty<byte>(),
                        RequestContractVersion = seed.RequestContractVersion,
                        RequestContractFingerprint = seed.RequestContractFingerprint,
                        Retries = seed.Retries,
                        RetryIntervals = seed.RetryIntervals,
                        TimeoutSeconds = seed.TimeoutSeconds
                    };
                    Stamp(entity);

                    try
                    {
                        await cronSet.InsertOneAsync(entity, cancellationToken: cancellationToken).ConfigureAwait(false);
                    }
                    catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
                    {
                        // A concurrent reconcile inserted the same deterministic row (or the unique partial
                        // SeedKey index already claims this key). Converge: if the winning row exists but
                        // predates seed ownership, adopt the key in place; otherwise it is already owned.
                        var winner = await cronSet.Find(InPartition(fb.Eq(x => x.Id, entity.Id)))
                            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
                        if (winner is { SeedKey: null })
                            await cronSet.UpdateOneAsync(
                                InPartition(fb.Eq(x => x.Id, entity.Id)),
                                Builders<TCronTicker>.Update
                                    .Set(x => x.SeedKey, seedKey)
                                    .Set(x => x.SeedLastSeenAt, now),
                                cancellationToken: cancellationToken).ConfigureAwait(false);
                    }
                }
            }
        }

        private async Task<bool> UpdateCronAndCleanupPendingAsync(
            FilterDefinition<TCronTicker> expectedFilter, Guid cronTickerId,
            UpdateDefinition<TCronTicker> update, DateTime now,
            CancellationToken cancellationToken)
        {
            if (TransactionsKnownUnavailable)
            {
                // Standalone MongoDB cannot make the definition write and cross-collection cleanup atomic.
                // Clean first so an interruption leaves the old definition visible; a retry still detects
                // the change and repeats cleanup before publishing the authoritative definition.
                await RemoveUnleasedPendingCronOccurrencesAsync(
                    null, cronTickerId, now, cancellationToken).ConfigureAwait(false);
                var result = await _context.CronTickers.UpdateOneAsync(
                    expectedFilter, update, cancellationToken: cancellationToken).ConfigureAwait(false);
                return result.MatchedCount == 1;
            }

            using var session = await _context.Database.Client
                .StartSessionAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            try
            {
                return await session.WithTransactionAsync(async (s, ct) =>
                {
                    var result = await _context.CronTickers.UpdateOneAsync(
                        s, expectedFilter, update,
                        cancellationToken: ct).ConfigureAwait(false);
                    if (result.MatchedCount != 1) return false;
                    await RemoveUnleasedPendingCronOccurrencesAsync(s, cronTickerId, now, ct)
                        .ConfigureAwait(false);
                    return true;
                }, GraphTransactionOptions, cancellationToken).ConfigureAwait(false);
            }
            catch (MongoCommandException ex) when (IsCanonicalTransactionsUnsupported(ex))
            {
                // Transaction capability was discovered only after the attempt. Preserve the same
                // cleanup-first retry contract as the known-standalone path.
                await RemoveUnleasedPendingCronOccurrencesAsync(
                    null, cronTickerId, now, cancellationToken).ConfigureAwait(false);
                var result = await _context.CronTickers.UpdateOneAsync(
                    expectedFilter, update, cancellationToken: cancellationToken).ConfigureAwait(false);
                return result.MatchedCount == 1;
            }
        }

        private async Task RemoveUnleasedPendingCronOccurrencesAsync(
            IClientSessionHandle session, Guid cronTickerId, DateTime now,
            CancellationToken cancellationToken)
        {
            var fb = Builders<CronTickerOccurrenceEntity<TCronTicker>>.Filter;
            // Revision publication fences every not-yet-running occurrence. Quarantine in place so
            // payload, timestamps, and result sidecars remain available as durable evidence.
            var removable = InPartition(fb.And(
                fb.Eq(x => x.CronTickerId, cronTickerId),
                fb.In(x => x.Status, new[] { TickerStatus.Idle, TickerStatus.Queued }),
                fb.Or(fb.Eq(x => x.LockHolder, null), fb.Eq(x => x.LockHolder, string.Empty)),
                fb.Eq(x => x.AcquisitionToken, null),
                fb.Or(fb.Eq(x => x.LeaseUntil, null), fb.Lte(x => x.LeaseUntil, now))));
            var quarantine = Builders<CronTickerOccurrenceEntity<TCronTicker>>.Update
                .Set(x => x.Status, TickerStatus.Skipped)
                .Set(x => x.SkippedReason,
                    "Quarantined because its Cron definition revision is stale.")
                .Set(x => x.ExecutedAt, now)
                .Set(x => x.LockHolder, (string)null)
                .Set(x => x.LockedAt, (DateTime?)null)
                .Set(x => x.LeaseUntil, (DateTime?)null)
                .Set(x => x.AcquisitionToken, (Guid?)null)
                .Set(x => x.UpdatedAt, now);

            if (session == null)
                await _context.CronTickerOccurrences.UpdateManyAsync(
                    removable, quarantine, cancellationToken: cancellationToken).ConfigureAwait(false);
            else
                await _context.CronTickerOccurrences.UpdateManyAsync(
                    session, removable, quarantine, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        // Translates a retirement state transition applied to a loaded entity into a minimal, atomic
        // per-row Mongo update: the shared CronSeedRetirement helper mutates the in-memory copy, then only
        // the fields it actually changed are $set (plus UpdatedAt). Returns false — and no update — when
        // the transition is a no-op, so an already-settled row is never rewritten.
        private static bool TryBuildRetirementUpdate(
            TCronTicker row, Func<CronTickerEntity, bool> transition, DateTime now,
            out UpdateDefinition<TCronTicker> update)
        {
            var sets = new List<UpdateDefinition<TCronTicker>>();
            if (!AddRetirementSets(row, transition, sets))
            {
                update = null;
                return false;
            }

            sets.Add(Builders<TCronTicker>.Update.Set(x => x.UpdatedAt, now));
            update = Builders<TCronTicker>.Update.Combine(sets);
            return true;
        }

        // Applies a retirement state transition and appends a $set for each retirement field it changed
        // to the supplied list; returns whether anything changed. Shared by orphan retirement, duplicate
        // retirement, and the desired-seed clear so all three stay consistent with the other providers.
        private static bool AddRetirementSets(
            TCronTicker row, Func<CronTickerEntity, bool> transition, List<UpdateDefinition<TCronTicker>> sets)
        {
            var beforeReq = row.RetirementRequestedAt;
            var beforeRet = row.RetiredAt;
            var beforeEnabled = row.IsEnabled;
            var beforeWas = row.SeedWasEnabledBeforeRetirement;

            if (!transition(row))
                return false;

            var u = Builders<TCronTicker>.Update;
            if (row.RetirementRequestedAt != beforeReq)
                sets.Add(u.Set(x => x.RetirementRequestedAt, row.RetirementRequestedAt));
            if (row.RetiredAt != beforeRet)
                sets.Add(u.Set(x => x.RetiredAt, row.RetiredAt));
            if (row.IsEnabled != beforeEnabled)
                sets.Add(u.Set(x => x.IsEnabled, row.IsEnabled));
            if (row.SeedWasEnabledBeforeRetirement != beforeWas)
                sets.Add(u.Set(x => x.SeedWasEnabledBeforeRetirement, row.SeedWasEnabledBeforeRetirement));
            return true;
        }

        public async Task<CronTickerEntity[]> GetAllCronTickerExpressions(CancellationToken cancellationToken)
        {
            var fb = Builders<TCronTicker>.Filter;
            var rows = await _context.CronTickers
                .Find(InPartition(fb.And(fb.Eq(x => x.IsEnabled, true), fb.Eq(x => x.IsSystemPaused, false))))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var project = MappingExtensions.ForCronTickerExpressions<TCronTicker>().Compile();
            return rows.Select(project).ToArray();
        }

        // ===================================================================
        // Cron Occurrence — core methods
        // ===================================================================

        public async Task<CronTickerOccurrenceEntity<TCronTicker>> GetEarliestAvailableCronOccurrence(Guid[] ids, CancellationToken cancellationToken = default)
        {
            if (TransactionsKnownUnavailable) return null;
            if (!await IsRunnableAdmissionAllowedAsync(cancellationToken).ConfigureAwait(false))
                return null;
            var now = _clock.UtcNow;
            var mainSchedulerThreshold = now.AddSeconds(-1);
            var fb = Builders<CronTickerOccurrenceEntity<TCronTicker>>.Filter;

            var filter = InPartition(fb.And(
                fb.In(x => x.CronTickerId, ids),
                fb.Gte(x => x.ExecutionTime, mainSchedulerThreshold),
                MongoUpdateBuilders.CanAcquireCronOccurrence<TCronTicker>(_lockHolder)));

            var candidates = await _context.CronTickerOccurrences
                .Find(filter)
                .Sort(Builders<CronTickerOccurrenceEntity<TCronTicker>>.Sort.Ascending(x => x.ExecutionTime))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            foreach (var occurrence in candidates)
            {
                occurrence.CronTicker = await _context.CronTickers
                    .Find(InPartition(Builders<TCronTicker>.Filter.Eq(x => x.Id, occurrence.CronTickerId)))
                    .FirstOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (occurrence.CronTicker != null &&
                    occurrence.DefinitionRevision == occurrence.CronTicker.DefinitionRevision)
                    return occurrence;

                await QuarantineStalePendingOccurrenceAsync(occurrence, now, cancellationToken)
                    .ConfigureAwait(false);
            }
            return null;
        }

        public async IAsyncEnumerable<CronTickerOccurrenceEntity<TCronTicker>> QueueCronTickerOccurrences(
            (DateTime Key, InternalManagerContext[] Items) cronTickerOccurrences,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (TransactionsKnownUnavailable) yield break;
            var now = _clock.UtcNow;
            var executionTime = cronTickerOccurrences.Key;
            var coll = _context.CronTickerOccurrences;
            var fb = Builders<CronTickerOccurrenceEntity<TCronTicker>>.Filter;

            foreach (var item in cronTickerOccurrences.Items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var acquisitionToken = Guid.NewGuid();
                using var session = await _context.Database.Client
                    .StartSessionAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
                var queued = await session.WithTransactionAsync(async (s, ct) =>
                {
                    // All runnable publication uses one parent-first lock order: activation metadata,
                    // authoritative Cron revision, then occurrence/slot. This makes a revision publication
                    // or activation transition conflict with the old writer before its child can commit.
                    if (!await LockRunnableAdmissionAsync(s, ct).ConfigureAwait(false) ||
                        item.DefinitionRevision <= 0 ||
                        !await LockCronRevisionAsync(s, item.Id, item.DefinitionRevision, ct).ConfigureAwait(false))
                        return null;

                    if (item.NextCronOccurrence is null)
                    {
                        var slotFilter = InPartition(fb.And(
                            fb.Eq(x => x.CronTickerId, item.Id),
                            fb.Eq(x => x.ExecutionTime, executionTime)));
                        if (await coll.Find(s, slotFilter).AnyAsync(ct).ConfigureAwait(false))
                            return null;

                        var toAdd = new CronTickerOccurrenceEntity<TCronTicker>
                        {
                            Id = Guid.NewGuid(),
                            Status = TickerStatus.Queued,
                            LockHolder = _lockHolder,
                            ExecutionTime = executionTime,
                            CronTickerId = item.Id,
                            DefinitionRevision = item.DefinitionRevision,
                            LockedAt = now,
                            AcquisitionToken = acquisitionToken,
                            CreatedAt = now,
                            UpdatedAt = now
                        };
                        Stamp(toAdd);
                        await coll.InsertOneAsync(s, toAdd, cancellationToken: ct).ConfigureAwait(false);
                        return toAdd;
                    }

                    var filter = InPartition(fb.And(
                        fb.Eq(x => x.Id, item.NextCronOccurrence.Id),
                        fb.Eq(x => x.ExecutionTime, executionTime),
                        fb.Eq(x => x.DefinitionRevision, item.DefinitionRevision),
                        MongoUpdateBuilders.CanAcquireCronOccurrence<TCronTicker>(_lockHolder)));
                    var update = Builders<CronTickerOccurrenceEntity<TCronTicker>>.Update
                        .Set(x => x.LockHolder, _lockHolder)
                        .Set(x => x.LockedAt, now)
                        .Set(x => x.AcquisitionToken, acquisitionToken)
                        .Set(x => x.UpdatedAt, now)
                        .Set(x => x.Status, TickerStatus.Queued);
                    return await coll.FindOneAndUpdateAsync(
                        s, filter, update,
                        new FindOneAndUpdateOptions<CronTickerOccurrenceEntity<TCronTicker>>
                            { ReturnDocument = ReturnDocument.After }, ct).ConfigureAwait(false);
                }, GraphTransactionOptions, cancellationToken).ConfigureAwait(false);

                if (queued == null) continue;
                queued.CronTicker = new TCronTicker
                {
                    Id = item.Id,
                    Function = item.FunctionName,
                    RequestContractVersion = item.RequestContractVersion,
                    RequestContractFingerprint = item.RequestContractFingerprint,
                    InitIdentifier = _lockHolder,
                    Expression = item.Expression,
                    Retries = item.Retries,
                    RetryIntervals = item.RetryIntervals,
                    TimeoutSeconds = item.TimeoutSeconds,
                    DefinitionRevision = item.DefinitionRevision
                };
                yield return queued;
            }
        }

        public async IAsyncEnumerable<CronTickerOccurrenceEntity<TCronTicker>> QueueTimedOutCronTickerOccurrences(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (!await IsRunnableAdmissionAllowedAsync(cancellationToken).ConfigureAwait(false)) yield break;
            var now = _clock.UtcNow;
            var fallbackThreshold = now.AddSeconds(-1);
            var coll = _context.CronTickerOccurrences;
            var fb = Builders<CronTickerOccurrenceEntity<TCronTicker>>.Filter;

            var candidatesFilter = InPartition(fb.And(
                fb.In(x => x.Status, new[] { TickerStatus.Idle, TickerStatus.Queued }),
                fb.Lte(x => x.ExecutionTime, fallbackThreshold)));

            var candidates = await coll.Find(candidatesFilter).ToListAsync(cancellationToken).ConfigureAwait(false);
            if (candidates.Count == 0) yield break;

            var cronById = await LoadCronTickers(candidates.Select(c => c.CronTickerId).ToArray(), cancellationToken).ConfigureAwait(false);

            foreach (var occ in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var acquisitionToken = Guid.NewGuid();

                if (!cronById.TryGetValue(occ.CronTickerId, out var authoritative) ||
                    occ.DefinitionRevision != authoritative.DefinitionRevision)
                {
                    await QuarantineStalePendingOccurrenceAsync(occ, now, cancellationToken)
                        .ConfigureAwait(false);
                    continue;
                }
                // Starting work requires cross-collection activation and authoritative-revision locks.
                // A standalone cannot serialize those checks with this occurrence update, so it fails closed.
                if (TransactionsKnownUnavailable || !CanAcquirePendingOccurrence(occ, now))
                    continue;

                var filter = InPartition(fb.And(
                    fb.Eq(x => x.Id, occ.Id),
                    fb.Eq(x => x.UpdatedAt, occ.UpdatedAt),
                    fb.Eq(x => x.DefinitionRevision, authoritative.DefinitionRevision),
                    MongoUpdateBuilders.CanAcquireCronOccurrence<TCronTicker>(_lockHolder)));

                var update = Builders<CronTickerOccurrenceEntity<TCronTicker>>.Update
                    .Set(x => x.LockHolder, _lockHolder)
                    .Set(x => x.LockedAt, now)
                    .Set(x => x.LeaseUntil, NextLeaseUntil(now))
                    .Set(x => x.AcquisitionToken, acquisitionToken)
                    .Set(x => x.UpdatedAt, now)
                    .Set(x => x.Status, TickerStatus.InProgress);

                using var session = await _context.Database.Client
                    .StartSessionAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
                var acquired = await session.WithTransactionAsync(async (s, ct) =>
                {
                    if (!await LockRunnableAdmissionAsync(s, ct).ConfigureAwait(false) ||
                        !await LockCronRevisionAsync(s, occ.CronTickerId,
                            authoritative.DefinitionRevision, ct).ConfigureAwait(false))
                        return null;
                    return await coll.FindOneAndUpdateAsync(
                        s, filter, update,
                        new FindOneAndUpdateOptions<CronTickerOccurrenceEntity<TCronTicker>>
                            { ReturnDocument = ReturnDocument.After }, ct).ConfigureAwait(false);
                }, GraphTransactionOptions, cancellationToken).ConfigureAwait(false);
                if (acquired == null) continue;

                acquired.CronTicker = new TCronTicker
                {
                    Id = authoritative.Id,
                    Function = authoritative.Function,
                    RequestContractVersion = authoritative.RequestContractVersion,
                    RequestContractFingerprint = authoritative.RequestContractFingerprint,
                    RetryIntervals = authoritative.RetryIntervals,
                    Retries = authoritative.Retries,
                    TimeoutSeconds = authoritative.TimeoutSeconds,
                    DefinitionRevision = authoritative.DefinitionRevision
                };
                yield return acquired;
            }
        }

        public async Task UpdateCronTickerOccurrence(InternalFunctionContext functionContext, CancellationToken cancellationToken = default)
        {
            var now = _clock.UtcNow;
            var update = MongoUpdateBuilders.BuildCronOccurrenceUpdate<TCronTicker>(functionContext, now, NextLeaseUntil(now));
            var filter = InPartition(Builders<CronTickerOccurrenceEntity<TCronTicker>>.Filter.Eq(x => x.Id, functionContext.TickerId));
            if (IsFencedTerminalWrite(functionContext))
            {
                var fb = Builders<CronTickerOccurrenceEntity<TCronTicker>>.Filter;
                filter &= functionContext.AcquisitionToken.HasValue
                    ? fb.And(fb.Eq(x => x.LockHolder, _lockHolder),
                             fb.Eq(x => x.AcquisitionToken, functionContext.AcquisitionToken))
                    : fb.Where(_ => false);
            }

            await _context.CronTickerOccurrences.UpdateOneAsync(
                filter, update, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        public async Task ReleaseAcquiredCronTickerOccurrences(Guid[] occurrenceIds, CancellationToken cancellationToken = default)
        {
            var now = _clock.UtcNow;
            var coll = _context.CronTickerOccurrences;
            var fb = Builders<CronTickerOccurrenceEntity<TCronTicker>>.Filter;

            var canAcquire = MongoUpdateBuilders.CanAcquireCronOccurrence<TCronTicker>(_lockHolder);
            var filter = occurrenceIds.Length == 0
                ? InPartition(canAcquire)
                : InPartition(fb.And(fb.In(x => x.Id, occurrenceIds), canAcquire));

            var update = Builders<CronTickerOccurrenceEntity<TCronTicker>>.Update
                .Set(x => x.LockHolder, (string)null)
                .Set(x => x.LockedAt, (DateTime?)null)
                .Set(x => x.LeaseUntil, (DateTime?)null)
                .Set(x => x.AcquisitionToken, (Guid?)null)
                .Set(x => x.Status, TickerStatus.Idle)
                .Set(x => x.UpdatedAt, now);

            await ExecuteAdmittedGraphMutationAsync(
                async (session, ct) => (int)(await coll.UpdateManyAsync(
                    session, filter, update, cancellationToken: ct).ConfigureAwait(false)).ModifiedCount,
                async ct => (int)(await coll.UpdateManyAsync(
                    filter, update, cancellationToken: ct).ConfigureAwait(false)).ModifiedCount,
                cancellationToken).ConfigureAwait(false);
        }

        public async Task<byte[]> GetCronTickerOccurrenceRequest(Guid tickerId, CancellationToken cancellationToken = default)
        {
            var occ = await _context.CronTickerOccurrences
                .Find(InPartition(Builders<CronTickerOccurrenceEntity<TCronTicker>>.Filter.Eq(x => x.Id, tickerId)))
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
            if (occ == null) return null;

            var cron = await _context.CronTickers
                .Find(InPartition(Builders<TCronTicker>.Filter.Eq(x => x.Id, occ.CronTickerId)))
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
            return cron?.Request;
        }

        public async Task UpdateCronTickerOccurrencesWithUnifiedContext(Guid[] cronOccurrenceIds, InternalFunctionContext functionContext, CancellationToken cancellationToken = default)
        {
            if (cronOccurrenceIds.Length == 0) return;
            var now = _clock.UtcNow;
            var update = MongoUpdateBuilders.BuildCronOccurrenceUpdate<TCronTicker>(functionContext, now, NextLeaseUntil(now));
            await _context.CronTickerOccurrences
                .UpdateManyAsync(
                    InPartition(Builders<CronTickerOccurrenceEntity<TCronTicker>>.Filter.In(x => x.Id, cronOccurrenceIds)),
                    update,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task<Guid[]> TransitionQueuedCronOccurrencesToInProgressAsync(
            IReadOnlyCollection<AcquisitionLease> leases, CancellationToken cancellationToken = default)
        {
            if (TransactionsKnownUnavailable) return Array.Empty<Guid>();
            var now = _clock.UtcNow;
            var fb = Builders<CronTickerOccurrenceEntity<TCronTicker>>.Filter;
            var update = Builders<CronTickerOccurrenceEntity<TCronTicker>>.Update
                .Set(x => x.Status, TickerStatus.InProgress)
                .Set(x => x.LeaseUntil, NextLeaseUntil(now))
                .Set(x => x.UpdatedAt, now);
            var winners = new List<Guid>(leases.Count);
            foreach (var lease in leases.Where(x => x.AcquisitionToken.HasValue).Distinct())
            {
                using var session = await _context.Database.Client
                    .StartSessionAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
                var won = await session.WithTransactionAsync(async (s, ct) =>
                {
                    if (!await LockRunnableAdmissionAsync(s, ct).ConfigureAwait(false))
                        return false;
                    var candidate = await _context.CronTickerOccurrences.Find(s, fb.And(
                            fb.Eq(x => x.ApplicationNamespaceKey, _runtimePartitionKey),
                            fb.Eq(x => x.Id, lease.TickerId),
                            fb.Eq(x => x.AcquisitionToken, lease.AcquisitionToken)))
                        .FirstOrDefaultAsync(ct).ConfigureAwait(false);
                    if (candidate == null ||
                        !await LockCronRevisionAsync(s, candidate.CronTickerId,
                            candidate.DefinitionRevision, ct).ConfigureAwait(false))
                        return false;

                    var filter = fb.And(
                        fb.Eq(x => x.ApplicationNamespaceKey, _runtimePartitionKey),
                        fb.Eq(x => x.Id, lease.TickerId),
                        fb.Eq(x => x.Status, TickerStatus.Queued),
                        fb.Eq(x => x.LockHolder, _lockHolder),
                        fb.Eq(x => x.AcquisitionToken, lease.AcquisitionToken),
                        fb.Eq(x => x.DefinitionRevision, candidate.DefinitionRevision));
                    var result = await _context.CronTickerOccurrences.UpdateOneAsync(
                        s, filter, update, cancellationToken: ct).ConfigureAwait(false);
                    return result.ModifiedCount == 1;
                }, GraphTransactionOptions, cancellationToken).ConfigureAwait(false);
                if (won) winners.Add(lease.TickerId);
            }
            return winners.ToArray();
        }

        public async Task ReleaseDeadNodeOccurrenceResources(string instanceIdentifier, CancellationToken cancellationToken = default)
        {
            var now = _clock.UtcNow;
            var coll = _context.CronTickerOccurrences;
            var fb = Builders<CronTickerOccurrenceEntity<TCronTicker>>.Filter;
            var firstFilter = InPartition(MongoUpdateBuilders.CanAcquireCronOccurrence<TCronTicker>(instanceIdentifier));
            var secondFilter = fb.And(fb.Eq(x => x.ApplicationNamespaceKey, _runtimePartitionKey),
                fb.Eq(x => x.LockHolder, instanceIdentifier),
                fb.Eq(x => x.Status, TickerStatus.InProgress));
            var update = Builders<CronTickerOccurrenceEntity<TCronTicker>>.Update
                    .Set(x => x.LockHolder, (string)null)
                    .Set(x => x.LockedAt, (DateTime?)null)
                    .Set(x => x.LeaseUntil, (DateTime?)null)
                    .Set(x => x.AcquisitionToken, (Guid?)null)
                    .Set(x => x.Status, TickerStatus.Idle)
                    .Set(x => x.UpdatedAt, now);
            await ExecuteAdmittedGraphMutationAsync(
                async (session, ct) =>
                {
                    var first = await coll.UpdateManyAsync(session, firstFilter, update,
                        cancellationToken: ct).ConfigureAwait(false);
                    var second = await coll.UpdateManyAsync(session, secondFilter, update,
                        cancellationToken: ct).ConfigureAwait(false);
                    return (int)(first.ModifiedCount + second.ModifiedCount);
                },
                async ct =>
                {
                    var first = await coll.UpdateManyAsync(firstFilter, update,
                        cancellationToken: ct).ConfigureAwait(false);
                    var second = await coll.UpdateManyAsync(secondFilter, update,
                        cancellationToken: ct).ConfigureAwait(false);
                    return (int)(first.ModifiedCount + second.ModifiedCount);
                }, cancellationToken).ConfigureAwait(false);
        }

        public async Task<int> SkipStaleCronOccurrencesAsync(TimeSpan staleThreshold, CancellationToken cancellationToken = default)
        {
            if (staleThreshold <= TimeSpan.Zero) return 0;
            var now = _clock.UtcNow;
            var cutoff = now - staleThreshold;
            var fb = Builders<CronTickerOccurrenceEntity<TCronTicker>>.Filter;

            var filter = fb.And(
                fb.Eq(x => x.ApplicationNamespaceKey, _runtimePartitionKey),
                fb.In(x => x.Status, new[] { TickerStatus.Idle, TickerStatus.Queued }),
                fb.Lt(x => x.ExecutionTime, cutoff));

            var update = Builders<CronTickerOccurrenceEntity<TCronTicker>>.Update
                .Set(x => x.Status, TickerStatus.Skipped)
                .Set(x => x.SkippedReason, "Missed: occurrence was pending when the application restarted")
                .Set(x => x.UpdatedAt, now);

            var result = await _context.CronTickerOccurrences.UpdateManyAsync(filter, update, cancellationToken: cancellationToken).ConfigureAwait(false);
            return (int)result.ModifiedCount;
        }

        // ===================================================================
        // Stale-job recovery
        // ===================================================================

        // MongoDB fully implements lease renewal and the stale-job watchdog below.
        public bool SupportsLeaseBasedRecovery => true;

        public async Task<int> RenewTimeTickerLeases(Guid[] timeTickerIds, DateTime leaseUntil, CancellationToken cancellationToken = default)
        {
            if (timeTickerIds == null || timeTickerIds.Length == 0) return 0;
            var fb = Builders<TTimeTicker>.Filter;
            var filter = fb.And(
                fb.Eq(x => x.ApplicationNamespaceKey, _runtimePartitionKey),
                fb.In(x => x.Id, timeTickerIds),
                fb.Eq(x => x.LockHolder, _lockHolder),
                fb.Eq(x => x.Status, TickerStatus.InProgress));
            var result = await _context.TimeTickers
                .UpdateManyAsync(filter, Builders<TTimeTicker>.Update.Set(x => x.LeaseUntil, leaseUntil), cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return (int)result.MatchedCount;
        }

        public async Task<int> RenewCronTickerOccurrenceLeases(Guid[] occurrenceIds, DateTime leaseUntil, CancellationToken cancellationToken = default)
        {
            if (occurrenceIds == null || occurrenceIds.Length == 0) return 0;
            var fb = Builders<CronTickerOccurrenceEntity<TCronTicker>>.Filter;
            var filter = fb.And(
                fb.Eq(x => x.ApplicationNamespaceKey, _runtimePartitionKey),
                fb.In(x => x.Id, occurrenceIds),
                fb.Eq(x => x.LockHolder, _lockHolder),
                fb.Eq(x => x.Status, TickerStatus.InProgress));
            var result = await _context.CronTickerOccurrences
                .UpdateManyAsync(filter, Builders<CronTickerOccurrenceEntity<TCronTicker>>.Update.Set(x => x.LeaseUntil, leaseUntil), cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return (int)result.MatchedCount;
        }

        public async Task<int> RenewTimeTickerLeases(
            IReadOnlyCollection<AcquisitionLease> leases, DateTime leaseUntil,
            CancellationToken cancellationToken = default)
        {
            if (leases == null || leases.Count == 0) return 0;
            var renewed = 0;
            var fb = Builders<TTimeTicker>.Filter;
            foreach (var lease in leases)
            {
                if (!lease.AcquisitionToken.HasValue) continue;
                var filter = fb.And(
                    fb.Eq(x => x.ApplicationNamespaceKey, _runtimePartitionKey),
                    fb.Eq(x => x.Id, lease.TickerId),
                    fb.Eq(x => x.LockHolder, _lockHolder),
                    fb.Eq(x => x.Status, TickerStatus.InProgress),
                    fb.Eq(x => x.AcquisitionToken, lease.AcquisitionToken));
                var result = await _context.TimeTickers.UpdateOneAsync(
                    filter, Builders<TTimeTicker>.Update.Set(x => x.LeaseUntil, leaseUntil),
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                renewed += (int)result.MatchedCount;
            }
            return renewed;
        }

        public async Task<int> RenewCronTickerOccurrenceLeases(
            IReadOnlyCollection<AcquisitionLease> leases, DateTime leaseUntil,
            CancellationToken cancellationToken = default)
        {
            if (leases == null || leases.Count == 0) return 0;
            var renewed = 0;
            var fb = Builders<CronTickerOccurrenceEntity<TCronTicker>>.Filter;
            foreach (var lease in leases)
            {
                if (!lease.AcquisitionToken.HasValue) continue;
                var filter = fb.And(
                    fb.Eq(x => x.ApplicationNamespaceKey, _runtimePartitionKey),
                    fb.Eq(x => x.Id, lease.TickerId),
                    fb.Eq(x => x.LockHolder, _lockHolder),
                    fb.Eq(x => x.Status, TickerStatus.InProgress),
                    fb.Eq(x => x.AcquisitionToken, lease.AcquisitionToken));
                var result = await _context.CronTickerOccurrences.UpdateOneAsync(
                    filter, Builders<CronTickerOccurrenceEntity<TCronTicker>>.Update.Set(x => x.LeaseUntil, leaseUntil),
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                renewed += (int)result.MatchedCount;
            }
            return renewed;
        }

        public async Task<Guid[]> GetStillHeldTickerIds(
            IReadOnlyCollection<AcquisitionLease> timeTickerLeases,
            IReadOnlyCollection<AcquisitionLease> occurrenceLeases,
            CancellationToken cancellationToken = default)
        {
            var held = new List<Guid>();
            var timeFb = Builders<TTimeTicker>.Filter;
            foreach (var lease in timeTickerLeases ?? Array.Empty<AcquisitionLease>())
            {
                if (!lease.AcquisitionToken.HasValue) continue;
                var filter = timeFb.And(
                    timeFb.Eq(x => x.ApplicationNamespaceKey, _runtimePartitionKey),
                    timeFb.Eq(x => x.Id, lease.TickerId),
                    timeFb.Eq(x => x.LockHolder, _lockHolder),
                    timeFb.Eq(x => x.Status, TickerStatus.InProgress),
                    timeFb.Eq(x => x.AcquisitionToken, lease.AcquisitionToken));
                if (await _context.TimeTickers.Find(filter).AnyAsync(cancellationToken).ConfigureAwait(false))
                    held.Add(lease.TickerId);
            }

            var cronFb = Builders<CronTickerOccurrenceEntity<TCronTicker>>.Filter;
            foreach (var lease in occurrenceLeases ?? Array.Empty<AcquisitionLease>())
            {
                if (!lease.AcquisitionToken.HasValue) continue;
                var filter = cronFb.And(
                    cronFb.Eq(x => x.ApplicationNamespaceKey, _runtimePartitionKey),
                    cronFb.Eq(x => x.Id, lease.TickerId),
                    cronFb.Eq(x => x.LockHolder, _lockHolder),
                    cronFb.Eq(x => x.Status, TickerStatus.InProgress),
                    cronFb.Eq(x => x.AcquisitionToken, lease.AcquisitionToken));
                if (await _context.CronTickerOccurrences.Find(filter).AnyAsync(cancellationToken).ConfigureAwait(false))
                    held.Add(lease.TickerId);
            }
            return held.ToArray();
        }

        public async Task<Guid[]> GetStillHeldTickerIds(Guid[] timeTickerIds, Guid[] occurrenceIds, CancellationToken cancellationToken = default)
        {
            var held = new List<Guid>();

            if (timeTickerIds is { Length: > 0 })
            {
                var fb = Builders<TTimeTicker>.Filter;
                var filter = fb.And(
                    fb.Eq(x => x.ApplicationNamespaceKey, _runtimePartitionKey),
                    fb.In(x => x.Id, timeTickerIds),
                    fb.Eq(x => x.LockHolder, _lockHolder),
                    fb.Eq(x => x.Status, TickerStatus.InProgress));
                held.AddRange(await _context.TimeTickers.Find(filter).Project(x => x.Id)
                    .ToListAsync(cancellationToken).ConfigureAwait(false));
            }

            if (occurrenceIds is { Length: > 0 })
            {
                var fb = Builders<CronTickerOccurrenceEntity<TCronTicker>>.Filter;
                var filter = fb.And(
                    fb.Eq(x => x.ApplicationNamespaceKey, _runtimePartitionKey),
                    fb.In(x => x.Id, occurrenceIds),
                    fb.Eq(x => x.LockHolder, _lockHolder),
                    fb.Eq(x => x.Status, TickerStatus.InProgress));
                held.AddRange(await _context.CronTickerOccurrences.Find(filter).Project(x => x.Id)
                    .ToListAsync(cancellationToken).ConfigureAwait(false));
            }

            return held.ToArray();
        }

        public async Task<StaleTickerRecoveryResult> RecoverStaleTickers(int maxStaleRestarts, CancellationToken cancellationToken = default)
        {
            var now = _clock.UtcNow;
            const string staleReason =
                "Stale: the node executing this ticker stopped renewing its lease (presumed dead).";
            var result = new StaleTickerRecoveryResult();

            var staleLockCutoff = now.Subtract(_schedulerOptions.QueuedLockTimeout);
            var timeQueuedFilters = Builders<TTimeTicker>.Filter;
            var staleQueuedTime = timeQueuedFilters.And(
                timeQueuedFilters.Eq(x => x.ApplicationNamespaceKey, _runtimePartitionKey),
                timeQueuedFilters.Eq(x => x.ParentId, (Guid?)null),
                timeQueuedFilters.In(x => x.Status, new[] { TickerStatus.Idle, TickerStatus.Queued }),
                timeQueuedFilters.Ne(x => x.LockHolder, null),
                timeQueuedFilters.Ne(x => x.LockedAt, null),
                timeQueuedFilters.Lt(x => x.LockedAt, staleLockCutoff));
            var staleQueuedTimeUpdate = Builders<TTimeTicker>.Update
                    .Set(x => x.Status, TickerStatus.Idle)
                    .Set(x => x.LockHolder, (string)null)
                    .Set(x => x.LockedAt, (DateTime?)null)
                    .Set(x => x.LeaseUntil, (DateTime?)null)
                    .Set(x => x.AcquisitionToken, (Guid?)null)
                    .Set(x => x.UpdatedAt, now);

            var cronQueuedFilters = Builders<CronTickerOccurrenceEntity<TCronTicker>>.Filter;
            var staleQueuedCron = cronQueuedFilters.And(
                cronQueuedFilters.Eq(x => x.ApplicationNamespaceKey, _runtimePartitionKey),
                cronQueuedFilters.In(x => x.Status, new[] { TickerStatus.Idle, TickerStatus.Queued }),
                cronQueuedFilters.Ne(x => x.LockHolder, null),
                cronQueuedFilters.Ne(x => x.LockedAt, null),
                cronQueuedFilters.Lt(x => x.LockedAt, staleLockCutoff));
            var staleQueuedCronUpdate = Builders<CronTickerOccurrenceEntity<TCronTicker>>.Update
                    .Set(x => x.Status, TickerStatus.Idle)
                    .Set(x => x.LockHolder, (string)null)
                    .Set(x => x.LockedAt, (DateTime?)null)
                    .Set(x => x.LeaseUntil, (DateTime?)null)
                    .Set(x => x.AcquisitionToken, (Guid?)null)
                    .Set(x => x.UpdatedAt, now);
            var admittedQueuedRecovery = await ExecuteAdmittedGraphMutationAsync(
                async (session, ct) =>
                {
                    var time = await _context.TimeTickers.UpdateManyAsync(
                        session, staleQueuedTime, staleQueuedTimeUpdate,
                        cancellationToken: ct).ConfigureAwait(false);
                    var cron = await _context.CronTickerOccurrences.UpdateManyAsync(
                        session, staleQueuedCron, staleQueuedCronUpdate,
                        cancellationToken: ct).ConfigureAwait(false);
                    return (int)(time.ModifiedCount + cron.ModifiedCount);
                },
                async ct =>
                {
                    var time = await _context.TimeTickers.UpdateManyAsync(
                        staleQueuedTime, staleQueuedTimeUpdate, cancellationToken: ct).ConfigureAwait(false);
                    var cron = await _context.CronTickerOccurrences.UpdateManyAsync(
                        staleQueuedCron, staleQueuedCronUpdate, cancellationToken: ct).ConfigureAwait(false);
                    return (int)(time.ModifiedCount + cron.ModifiedCount);
                }, cancellationToken).ConfigureAwait(false);
            if (_requiresActivatedRuntimeAdmission && admittedQueuedRecovery == 0 &&
                !await IsRunnableAdmissionAllowedAsync(cancellationToken).ConfigureAwait(false))
                return result;

            var timeFilters = Builders<TTimeTicker>.Filter;
            var staleTime = timeFilters.And(
                timeFilters.Eq(x => x.ApplicationNamespaceKey, _runtimePartitionKey),
                timeFilters.Eq(x => x.ParentId, (Guid?)null),
                timeFilters.Eq(x => x.Status, TickerStatus.InProgress),
                timeFilters.Ne(x => x.LeaseUntil, null),
                timeFilters.Lt(x => x.LeaseUntil, now));
            var restartTime = timeFilters.And(
                staleTime,
                timeFilters.Eq(x => x.OnStale, StaleAction.Restart),
                timeFilters.Lt(x => x.StaleRestartCount, maxStaleRestarts));

            var restartTimeUpdate = Builders<TTimeTicker>.Update
                .Set(x => x.Status, TickerStatus.Idle)
                .Set(x => x.LockHolder, (string)null)
                .Set(x => x.LockedAt, (DateTime?)null)
                .Set(x => x.LeaseUntil, (DateTime?)null)
                .Set(x => x.AcquisitionToken, (Guid?)null)
                .Inc(x => x.StaleRestartCount, 1)
                .Set(x => x.UpdatedAt, now);
            if (TransactionsKnownUnavailable)
            {
                var restartIds = await _context.TimeTickers.Find(restartTime)
                    .Project(x => x.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
                var restartTimeResult = await _context.TimeTickers.UpdateManyAsync(
                    restartTime, restartTimeUpdate,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                if (restartIds.Count > 0)
                    await _context.TickerResults.DeleteManyAsync(
                        ResultFilter(restartIds, TimeResultKind),
                        cancellationToken).ConfigureAwait(false);
                result.RestartedTimeTickers = (int)restartTimeResult.ModifiedCount;
            }
            else
            {
                using var restartSession = await _context.Database.Client
                    .StartSessionAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
                result.RestartedTimeTickers = await restartSession.WithTransactionAsync(
                    async (s, ct) =>
                    {
                        if (!await LockRunnableAdmissionAsync(s, ct).ConfigureAwait(false)) return 0;
                        await TouchGraphFenceAsync(s, ct).ConfigureAwait(false);
                        var restartIds = await _context.TimeTickers.Find(s, restartTime)
                            .Project(x => x.Id).ToListAsync(ct).ConfigureAwait(false);
                        var restarted = await _context.TimeTickers.UpdateManyAsync(
                            s, restartTime, restartTimeUpdate,
                            cancellationToken: ct).ConfigureAwait(false);
                        if (restartIds.Count > 0)
                            await _context.TickerResults.DeleteManyAsync(
                                s,
                                ResultFilter(restartIds, TimeResultKind),
                                cancellationToken: ct).ConfigureAwait(false);
                        return (int)restarted.ModifiedCount;
                    }, GraphTransactionOptions, cancellationToken).ConfigureAwait(false);
            }

            var cancelTimeResult = await _context.TimeTickers.UpdateManyAsync(
                staleTime,
                Builders<TTimeTicker>.Update
                    .Set(x => x.Status, TickerStatus.Cancelled)
                    .Set(x => x.ExceptionMessage, staleReason)
                    .Set(x => x.ExecutedAt, now)
                    .Set(x => x.LockHolder, (string)null)
                    .Set(x => x.LockedAt, (DateTime?)null)
                    .Set(x => x.LeaseUntil, (DateTime?)null)
                .Set(x => x.AcquisitionToken, (Guid?)null)
                    .Set(x => x.UpdatedAt, now),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            result.CancelledTimeTickers = (int)cancelTimeResult.ModifiedCount;

            var occurrenceFilters = Builders<CronTickerOccurrenceEntity<TCronTicker>>.Filter;
            var staleOccurrences = occurrenceFilters.And(
                occurrenceFilters.Eq(x => x.ApplicationNamespaceKey, _runtimePartitionKey),
                occurrenceFilters.Eq(x => x.Status, TickerStatus.InProgress),
                occurrenceFilters.Ne(x => x.LeaseUntil, null),
                occurrenceFilters.Lt(x => x.LeaseUntil, now));
            var staleRows = await _context.CronTickerOccurrences.Find(staleOccurrences)
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            foreach (var staleRow in staleRows)
            {
                if (staleRow.StaleRestartCount >= maxStaleRestarts) continue;

                // Re-read the parent policy immediately before the CAS update. The
                // occurrence predicates below also pin the observed lease, owner,
                // and restart count so a renewed or re-acquired row cannot be reset.
                var parent = await _context.CronTickers
                    .Find(InPartition(Builders<TCronTicker>.Filter.Eq(x => x.Id, staleRow.CronTickerId)))
                    .Project(x => new { x.OnStale, x.DefinitionRevision })
                    .FirstOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (parent == null || staleRow.DefinitionRevision != parent.DefinitionRevision)
                {
                    await _context.CronTickerOccurrences.UpdateOneAsync(
                        occurrenceFilters.And(
                            staleOccurrences,
                            occurrenceFilters.Eq(x => x.Id, staleRow.Id),
                            occurrenceFilters.Eq(x => x.DefinitionRevision, staleRow.DefinitionRevision),
                            occurrenceFilters.Eq(x => x.LeaseUntil, staleRow.LeaseUntil),
                            occurrenceFilters.Eq(x => x.AcquisitionToken, staleRow.AcquisitionToken)),
                        Builders<CronTickerOccurrenceEntity<TCronTicker>>.Update
                            .Set(x => x.Status, TickerStatus.Skipped)
                            .Set(x => x.SkippedReason,
                                "Quarantined because its Cron definition revision is stale during recovery.")
                            .Set(x => x.ExecutedAt, now)
                            .Set(x => x.LockHolder, (string)null)
                            .Set(x => x.LockedAt, (DateTime?)null)
                            .Set(x => x.LeaseUntil, (DateTime?)null)
                            .Set(x => x.AcquisitionToken, (Guid?)null)
                            .Set(x => x.UpdatedAt, now),
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                    continue;
                }
                if (parent.OnStale != StaleAction.Restart) continue;

                var restartOccurrenceFilter = occurrenceFilters.And(
                    staleOccurrences,
                    occurrenceFilters.Eq(x => x.Id, staleRow.Id),
                    occurrenceFilters.Eq(x => x.DefinitionRevision, staleRow.DefinitionRevision),
                    occurrenceFilters.Eq(x => x.LeaseUntil, staleRow.LeaseUntil),
                    occurrenceFilters.Eq(x => x.LockHolder, staleRow.LockHolder),
                    occurrenceFilters.Eq(x => x.AcquisitionToken, staleRow.AcquisitionToken),
                    occurrenceFilters.Eq(x => x.StaleRestartCount, staleRow.StaleRestartCount));
                var restartOccurrenceUpdate = Builders<CronTickerOccurrenceEntity<TCronTicker>>.Update
                    .Set(x => x.Status, TickerStatus.Idle)
                    .Set(x => x.LockHolder, (string)null)
                    .Set(x => x.LockedAt, (DateTime?)null)
                    .Set(x => x.LeaseUntil, (DateTime?)null)
                    .Set(x => x.AcquisitionToken, (Guid?)null)
                    .Inc(x => x.StaleRestartCount, 1)
                    .Set(x => x.UpdatedAt, now);
                int restartedCount;
                if (TransactionsKnownUnavailable)
                {
                    var restarted = await _context.CronTickerOccurrences.UpdateOneAsync(
                        restartOccurrenceFilter, restartOccurrenceUpdate,
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                    restartedCount = (int)restarted.ModifiedCount;
                    if (restartedCount == 1)
                        await _context.TickerResults.DeleteOneAsync(
                            ResultFilter(staleRow.Id, CronOccurrenceResultKind),
                            cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    using var restartSession = await _context.Database.Client
                        .StartSessionAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
                    restartedCount = await restartSession.WithTransactionAsync(
                        async (s, ct) =>
                        {
                            if (!await LockRunnableAdmissionAsync(s, ct).ConfigureAwait(false) ||
                                !await LockCronRevisionAsync(s, staleRow.CronTickerId,
                                    staleRow.DefinitionRevision, ct).ConfigureAwait(false))
                                return 0;
                            var restarted = await _context.CronTickerOccurrences.UpdateOneAsync(
                                s, restartOccurrenceFilter, restartOccurrenceUpdate,
                                cancellationToken: ct).ConfigureAwait(false);
                            if (restarted.ModifiedCount == 1)
                                await _context.TickerResults.DeleteOneAsync(
                                    s,
                                    ResultFilter(staleRow.Id, CronOccurrenceResultKind),
                                    new DeleteOptions(), ct).ConfigureAwait(false);
                            return (int)restarted.ModifiedCount;
                        }, GraphTransactionOptions, cancellationToken).ConfigureAwait(false);
                }
                result.RestartedCronOccurrences += restartedCount;
            }

            var cancelOccurrenceResult = await _context.CronTickerOccurrences.UpdateManyAsync(
                staleOccurrences,
                Builders<CronTickerOccurrenceEntity<TCronTicker>>.Update
                    .Set(x => x.Status, TickerStatus.Cancelled)
                    .Set(x => x.ExceptionMessage, staleReason)
                    .Set(x => x.ExecutedAt, now)
                    .Set(x => x.LockHolder, (string)null)
                    .Set(x => x.LockedAt, (DateTime?)null)
                    .Set(x => x.LeaseUntil, (DateTime?)null)
                .Set(x => x.AcquisitionToken, (Guid?)null)
                    .Set(x => x.UpdatedAt, now),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            result.CancelledCronOccurrences = (int)cancelOccurrenceResult.ModifiedCount;

            return result;
        }

        // ===================================================================
        // Retention
        // ===================================================================

        public bool SupportsRetention => true;

        private static FilterDefinition<TTimeTicker> TimeRetentionEligible(
            RetentionCutoffs cutoffs, DateTime now)
        {
            var fb = Builders<TTimeTicker>.Filter;
            var outcomes = new List<FilterDefinition<TTimeTicker>>(4);
            if (cutoffs.SucceededBefore is { } succeeded)
                outcomes.Add(fb.And(
                    fb.In(x => x.Status, new[] { TickerStatus.Done, TickerStatus.DueDone }),
                    fb.Ne(x => x.ExecutedAt, (DateTime?)null),
                    fb.Lt(x => x.ExecutedAt, succeeded)));
            if (cutoffs.FailedBefore is { } failed)
                outcomes.Add(fb.And(fb.Eq(x => x.Status, TickerStatus.Failed),
                    fb.Ne(x => x.ExecutedAt, (DateTime?)null), fb.Lt(x => x.ExecutedAt, failed)));
            if (cutoffs.CancelledBefore is { } cancelled)
                outcomes.Add(fb.And(fb.Eq(x => x.Status, TickerStatus.Cancelled),
                    fb.Ne(x => x.ExecutedAt, (DateTime?)null), fb.Lt(x => x.ExecutedAt, cancelled)));
            if (cutoffs.SkippedBefore is { } skipped)
                outcomes.Add(fb.And(fb.Eq(x => x.Status, TickerStatus.Skipped),
                    fb.Ne(x => x.ExecutedAt, (DateTime?)null), fb.Lt(x => x.ExecutedAt, skipped)));

            if (outcomes.Count == 0)
                return fb.Where(_ => false);

            return fb.And(
                fb.Or(outcomes),
                fb.Eq(x => x.AcquisitionToken, (Guid?)null),
                fb.Or(fb.Eq(x => x.LeaseUntil, (DateTime?)null), fb.Lte(x => x.LeaseUntil, now)));
        }

        private static FilterDefinition<CronTickerOccurrenceEntity<TCronTicker>> OccurrenceRetentionEligible(
            RetentionCutoffs cutoffs, DateTime now)
        {
            var fb = Builders<CronTickerOccurrenceEntity<TCronTicker>>.Filter;
            var outcomes = new List<FilterDefinition<CronTickerOccurrenceEntity<TCronTicker>>>(4);
            if (cutoffs.SucceededBefore is { } succeeded)
                outcomes.Add(fb.And(
                    fb.In(x => x.Status, new[] { TickerStatus.Done, TickerStatus.DueDone }),
                    fb.Ne(x => x.ExecutedAt, (DateTime?)null),
                    fb.Lt(x => x.ExecutedAt, succeeded)));
            if (cutoffs.FailedBefore is { } failed)
                outcomes.Add(fb.And(fb.Eq(x => x.Status, TickerStatus.Failed),
                    fb.Ne(x => x.ExecutedAt, (DateTime?)null), fb.Lt(x => x.ExecutedAt, failed)));
            if (cutoffs.CancelledBefore is { } cancelled)
                outcomes.Add(fb.And(fb.Eq(x => x.Status, TickerStatus.Cancelled),
                    fb.Ne(x => x.ExecutedAt, (DateTime?)null), fb.Lt(x => x.ExecutedAt, cancelled)));
            if (cutoffs.SkippedBefore is { } skipped)
                outcomes.Add(fb.And(fb.Eq(x => x.Status, TickerStatus.Skipped),
                    fb.Ne(x => x.ExecutedAt, (DateTime?)null), fb.Lt(x => x.ExecutedAt, skipped)));

            if (outcomes.Count == 0)
                return fb.Where(_ => false);

            return fb.And(
                fb.Or(outcomes),
                fb.Eq(x => x.AcquisitionToken, (Guid?)null),
                fb.Or(fb.Eq(x => x.LeaseUntil, (DateTime?)null), fb.Lte(x => x.LeaseUntil, now)));
        }

        private async Task RecoverExpiredRetentionClaimsAsync(DateTime now, CancellationToken cancellationToken)
        {
            var fb = Builders<TTimeTicker>.Filter;
            var expiredClaim = InPartition(fb.And(
                fb.Eq(x => x.LockHolder, RetentionLockHolder),
                fb.In(x => x.Status, TerminalStatuses),
                fb.Ne(x => x.AcquisitionToken, (Guid?)null),
                fb.Lte(x => x.LeaseUntil, now)));
            await _context.TimeTickers.UpdateManyAsync(
                expiredClaim,
                Builders<TTimeTicker>.Update
                    .Set(x => x.LockHolder, (string)null)
                    .Set(x => x.LockedAt, (DateTime?)null)
                    .Set(x => x.LeaseUntil, (DateTime?)null)
                    .Set(x => x.AcquisitionToken, (Guid?)null),
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        public async Task<RetentionChainBatchResult> DeleteEligibleTimeTickerChainsAsync(
            RetentionCutoffs cutoffs, int batchSize, RetentionCursor cursor,
            CancellationToken cancellationToken = default)
        {
            if (cutoffs is null || !cutoffs.HasAny || batchSize <= 0)
                return RetentionChainBatchResult.Empty;

            cancellationToken.ThrowIfCancellationRequested();
            var now = _clock.UtcNow;

            // Defensive migration cleanup: recover any expired retention claim a prior build may have left so
            // those terminal rows are eligible again. The transactional delete below never leaves a claim.
            await RecoverExpiredRetentionClaimsAsync(now, cancellationToken).ConfigureAwait(false);

            var coll = _context.TimeTickers;
            var fb = Builders<TTimeTicker>.Filter;
            var eligible = InPartition(TimeRetentionEligible(cutoffs, now));
            var rootFilter = fb.And(fb.Eq(x => x.ParentId, (Guid?)null), eligible);
            if (cursor.HasValue)
            {
                rootFilter = fb.And(rootFilter, fb.Or(
                    fb.Gt(x => x.ExecutedAt, cursor.ExecutedAt),
                    fb.And(fb.Eq(x => x.ExecutedAt, cursor.ExecutedAt), fb.Gt(x => x.Id, cursor.Id))));
            }

            var roots = await coll.Find(rootFilter)
                .Sort(Builders<TTimeTicker>.Sort.Ascending(x => x.ExecutedAt).Ascending(x => x.Id))
                .Limit(batchSize + 1)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            var hasMore = roots.Count > batchSize;
            if (hasMore)
                roots.RemoveAt(roots.Count - 1);
            if (roots.Count == 0)
                return RetentionChainBatchResult.Empty;

            // Time-chain deletion MUST be atomic across the whole chain. Each chain is re-read, re-checked for
            // full eligibility, and deleted inside ONE Mongo transaction that rolls back on any mismatch — so a
            // chain is removed whole or not at all, never partially. A standalone deployment cannot run
            // transactions; rather than risk a non-atomic (partial) chain delete we FAIL CLOSED and delete no
            // time chains this sweep. We never downgrade to a non-transactional chain delete.
            var client = _context.Database.Client;

            // The root query has selected a server, so a genuine standalone can be refused up front. Do not use
            // ClusterType here: directConnection=true reports Standalone even for a replica-set primary.
            if (TransactionsKnownUnavailable)
                return RetentionChainBatchResult.Empty;

            using var session = await client
                .StartSessionAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

            var deleted = 0;
            foreach (var root in roots)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    deleted += await DeleteChainTransactionallyAsync(
                        session, root.Id, cutoffs, now, eligible, cancellationToken).ConfigureAwait(false);
                }
                catch (MongoException ex) when (ex.HasErrorLabel("TransientTransactionError"))
                {
                    // A supported graph mutation won the epoch write. Retain this chain for the sweep;
                    // the next sweep will discover the mutation's committed topology from scratch.
                }
                catch (MongoCommandException ex) when (IsCanonicalTransactionsUnsupported(ex))
                {
                    // Standalone topology: transactions are unsupported. Fail closed — retain every time
                    // chain this sweep rather than deleting any chain non-atomically.
                    return RetentionChainBatchResult.Empty;
                }
            }

            var last = roots[^1];
            var next = hasMore
                ? RetentionCursor.After(last.ExecutedAt!.Value, last.Id)
                : RetentionCursor.Start;
            return new RetentionChainBatchResult(deleted, hasMore, next);
        }

        // Deletes the whole chain rooted at <paramref name="rootId"/> inside a single transaction iff EVERY
        // node (root + descendants) is eligible and unowned. Traversal is bounded to
        // <see cref="RetentionCutoffs.MaxNodesPerChain"/>: an oversized chain is retained WHOLE and no delete
        // is issued for it, which also keeps every <c>$in</c> id list bounded by the cap. Any eligibility or
        // count mismatch aborts the transaction and leaves the chain fully intact. Propagates
        // <see cref="NotSupportedException"/> when the deployment cannot run transactions (standalone) so the
        // caller fails closed.
        private async Task<int> DeleteChainTransactionallyAsync(
            IClientSessionHandle session, Guid rootId, RetentionCutoffs cutoffs, DateTime now,
            FilterDefinition<TTimeTicker> eligible, CancellationToken cancellationToken)
        {
            var coll = _context.TimeTickers;
            var fb = Builders<TTimeTicker>.Filter;

            // Throws NotSupportedException on standalone deployments (no transaction support).
            session.StartTransaction(new TransactionOptions(
                readConcern: ReadConcern.Snapshot, writeConcern: WriteConcern.WMajority));

            var committed = false;
            try
            {
                if (BeforeRetentionFenceForTestAsync != null)
                    await BeforeRetentionFenceForTestAsync(cancellationToken).ConfigureAwait(false);
                await TouchGraphFenceAsync(session, cancellationToken).ConfigureAwait(false);

                // Re-read the chain under the transaction snapshot, bounded to the cap.
                var chainIds = await CollectBoundedChainIds(
                    session, rootId, cutoffs.MaxNodesPerChain, cancellationToken).ConfigureAwait(false);
                if (chainIds is null)
                {
                    // Oversized chain (or non-positive cap): retain whole, delete nothing.
                    await session.AbortTransactionAsync(cancellationToken).ConfigureAwait(false);
                    return 0;
                }

                cancellationToken.ThrowIfCancellationRequested();
                var subtreeFilter = InPartition(fb.In(x => x.Id, chainIds));

                // Every node must still be eligible & unowned under the snapshot.
                var eligibleCount = await coll.CountDocumentsAsync(
                    session, fb.And(subtreeFilter, eligible),
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                if (eligibleCount != chainIds.Count)
                {
                    await session.AbortTransactionAsync(cancellationToken).ConfigureAwait(false);
                    return 0; // ineligible descendant / concurrent mutation → retain whole
                }

                // The candidate must still be a root. Candidate discovery happened before the transaction;
                // pinning this exact edge prevents a reparented candidate from being deleted as a root.
                var rootStillRoot = await coll.CountDocumentsAsync(
                    session,
                    InPartition(fb.And(fb.Eq(x => x.Id, rootId),
                        fb.Eq(x => x.ParentId, (Guid?)null), eligible)),
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                if (rootStillRoot != 1)
                {
                    await session.AbortTransactionAsync(cancellationToken).ConfigureAwait(false);
                    return 0;
                }

                if (AfterRetentionDiscoveryForTestAsync != null)
                    await AfterRetentionDiscoveryForTestAsync(rootId, cancellationToken).ConfigureAwait(false);

                var result = await coll.DeleteManyAsync(
                    session, fb.And(subtreeFilter, eligible),
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                if (result.DeletedCount != chainIds.Count)
                {
                    await session.AbortTransactionAsync(cancellationToken).ConfigureAwait(false);
                    return 0; // concurrent delete/mutation → retain whole
                }

                await _context.TickerResults.DeleteManyAsync(
                    session,
                    ResultFilter(chainIds, TimeResultKind),
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                await session.CommitTransactionAsync(cancellationToken).ConfigureAwait(false);
                committed = true;
                return (int)result.DeletedCount;
            }
            finally
            {
                if (!committed && session.IsInTransaction)
                    await session.AbortTransactionAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }

        // BFS the chain (root + descendants) under <paramref name="session"/>, bounded to <paramref name="cap"/>
        // nodes. Returns null when the chain exceeds the cap (retain whole) or the cap is non-positive (fail
        // closed). Every <c>$in</c> frontier list and the returned id list are bounded by the cap, so no
        // oversized filter is ever built. Cancellation is honored on every tier.
        private async Task<List<Guid>> CollectBoundedChainIds(
            IClientSessionHandle session, Guid rootId, int cap, CancellationToken cancellationToken)
        {
            if (cap < 1)
                return null; // fail closed: cannot even admit the root

            var fb = Builders<TTimeTicker>.Filter;
            var all = new List<Guid> { rootId };
            var visited = new HashSet<Guid> { rootId };
            var frontier = new List<Guid> { rootId };

            while (frontier.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var remaining = cap - all.Count;
                var childIds = await _context.TimeTickers
                    .Find(session, InPartition(fb.In(x => x.ParentId, frontier.Select(p => (Guid?)p))))
                    .Project(x => x.Id)
                    .Limit(remaining + 1)
                    .ToListAsync(cancellationToken).ConfigureAwait(false);

                var next = new List<Guid>(childIds.Count);
                foreach (var childId in childIds)
                {
                    if (!visited.Add(childId))
                        continue; // cycle guard
                    all.Add(childId);
                    if (all.Count > cap)
                        return null; // oversized → retain whole
                    next.Add(childId);
                }
                frontier = next;
            }

            return all;
        }

        public async Task<RetentionBatchResult> DeleteEligibleCronTickerOccurrencesAsync(
            RetentionCutoffs cutoffs, int batchSize, CancellationToken cancellationToken = default)
        {
            if (cutoffs is null || !cutoffs.HasAny || batchSize <= 0)
                return RetentionBatchResult.Empty;

            var now = _clock.UtcNow;
            var coll = _context.CronTickerOccurrences;
            var fb = Builders<CronTickerOccurrenceEntity<TCronTicker>>.Filter;
            var eligible = InPartition(OccurrenceRetentionEligible(cutoffs, now));
            var candidates = await coll.Find(eligible)
                .Sort(Builders<CronTickerOccurrenceEntity<TCronTicker>>.Sort
                    .Ascending(x => x.ExecutedAt).Ascending(x => x.Id))
                .Project(x => x.Id)
                .Limit(batchSize + 1)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            var hasMore = candidates.Count > batchSize;
            if (hasMore)
                candidates.RemoveAt(candidates.Count - 1);
            if (candidates.Count == 0)
                return RetentionBatchResult.Empty;

            var rowFilter = fb.And(fb.In(x => x.Id, candidates), eligible);
            var resultFilter = ResultFilter(candidates, CronOccurrenceResultKind);
            if (TransactionsKnownUnavailable)
            {
                var standalone = await coll.DeleteManyAsync(
                    rowFilter, cancellationToken).ConfigureAwait(false);
                await _context.TickerResults.DeleteManyAsync(
                    resultFilter, cancellationToken).ConfigureAwait(false);
                return new RetentionBatchResult((int)standalone.DeletedCount, hasMore);
            }

            using var session = await _context.Database.Client
                .StartSessionAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            var deleted = await session.WithTransactionAsync(
                async (s, ct) =>
                {
                    await TouchGraphFenceAsync(s, ct).ConfigureAwait(false);
                    var result = await coll.DeleteManyAsync(
                        s, rowFilter, cancellationToken: ct).ConfigureAwait(false);
                    await _context.TickerResults.DeleteManyAsync(
                        s, resultFilter, cancellationToken: ct).ConfigureAwait(false);
                    return (int)result.DeletedCount;
                }, GraphTransactionOptions, cancellationToken).ConfigureAwait(false);
            return new RetentionBatchResult(deleted, hasMore);
        }

        // ===================================================================
        // Shared / dashboard methods
        // ===================================================================

        public async Task<TTimeTicker> GetTimeTickerById(Guid id, CancellationToken cancellationToken = default)
        {
            var row = await _context.TimeTickers
                .Find(InPartition(Builders<TTimeTicker>.Filter.Eq(x => x.Id, id)))
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
            if (row == null) return null;
            row.Children = await LoadChildrenRecursive<TTimeTicker>(row.Id, cancellationToken).ConfigureAwait(false);
            return row;
        }

        public async Task<TTimeTicker[]> GetTimeTickers(Expression<Func<TTimeTicker, bool>> predicate, CancellationToken cancellationToken = default)
        {
            var fb = Builders<TTimeTicker>.Filter;
            var filter = InPartition(fb.And(fb.Eq(x => x.ParentId, (Guid?)null), predicate is null ? fb.Empty : fb.Where(predicate)));
            var rows = await _context.TimeTickers
                .Find(filter)
                .Sort(Builders<TTimeTicker>.Sort.Descending(x => x.ExecutionTime))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (var row in rows)
                row.Children = await LoadChildrenRecursive<TTimeTicker>(row.Id, cancellationToken).ConfigureAwait(false);

            return rows.ToArray();
        }

        public async Task<PaginationResult<TTimeTicker>> GetTimeTickersPaginated(Expression<Func<TTimeTicker, bool>> predicate, int pageNumber, int pageSize, CancellationToken cancellationToken = default)
        {
            var fb = Builders<TTimeTicker>.Filter;
            var filter = InPartition(fb.And(fb.Eq(x => x.ParentId, (Guid?)null), predicate is null ? fb.Empty : fb.Where(predicate)));
            var total = await _context.TimeTickers.CountDocumentsAsync(filter, cancellationToken: cancellationToken).ConfigureAwait(false);
            var rows = await _context.TimeTickers
                .Find(filter)
                .Sort(Builders<TTimeTicker>.Sort.Descending(x => x.ExecutionTime))
                .Skip((pageNumber - 1) * pageSize)
                .Limit(pageSize)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (var row in rows)
                row.Children = await LoadChildrenRecursive<TTimeTicker>(row.Id, cancellationToken).ConfigureAwait(false);

            return new PaginationResult<TTimeTicker>(rows, (int)total, pageNumber, pageSize);
        }

        public async Task<int> AddTimeTickers(TTimeTicker[] tickers, CancellationToken cancellationToken = default)
        {
            if (tickers == null || tickers.Length == 0) return 0;
            return await ExecuteAdmittedGraphMutationAsync(
                async (session, ct) =>
                {
                    if (!await ReferencedParentsExistAsync(session, tickers, ct).ConfigureAwait(false))
                        return 0;
                    var count = 0;
                    foreach (var ticker in tickers)
                    {
                        var identity = await ResolveInsertedChainIdentityAsync(
                            session, ticker, tickers, ct).ConfigureAwait(false);
                        count += await InsertWithChildren(
                            session, ticker, null, identity.RootId, identity.Generation, ct).ConfigureAwait(false);
                    }
                    return count;
                },
                async ct =>
                {
                    var count = 0;
                    foreach (var ticker in tickers)
                    {
                        var identity = await ResolveInsertedChainIdentityAsync(
                            null, ticker, tickers, ct).ConfigureAwait(false);
                        count += await InsertWithChildren(
                            null, ticker, null, identity.RootId, identity.Generation, ct).ConfigureAwait(false);
                    }
                    return count;
                },
                cancellationToken).ConfigureAwait(false);
        }

        private async Task<(Guid RootId, Guid? Generation)> ResolveInsertedChainIdentityAsync(
            IClientSessionHandle session, TTimeTicker ticker, IReadOnlyCollection<TTimeTicker> supplied,
            CancellationToken cancellationToken)
        {
            var suppliedById = supplied.ToDictionary(x => x.Id);
            var current = ticker;
            var visited = new HashSet<Guid>();
            while (current.ParentId.HasValue && visited.Add(current.Id))
            {
                if (suppliedById.TryGetValue(current.ParentId.Value, out var suppliedParent))
                {
                    current = suppliedParent;
                    continue;
                }

                var filter = InPartition(Builders<TTimeTicker>.Filter.Eq(x => x.Id, current.ParentId.Value));
                var parent = session == null
                    ? await _context.TimeTickers.Find(filter).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false)
                    : await _context.TimeTickers.Find(session, filter).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
                return parent == null
                    ? (ticker.Id, ticker.ChainGeneration)
                    : (parent.ChainRootId ?? parent.Id, parent.ChainGeneration);
            }

            return (current.Id, current.ChainGeneration);
        }

        private async Task<int> InsertWithChildren(
            IClientSessionHandle session, TTimeTicker ticker, Guid? parentId, Guid chainRootId,
            Guid? chainGeneration, CancellationToken ct)
        {
            Stamp(ticker);
            if (parentId.HasValue) ticker.ParentId = parentId.Value;
            ticker.ChainRootId = chainRootId;
            ticker.ChainGeneration = chainGeneration;
            if (session == null)
                await _context.TimeTickers.InsertOneAsync(ticker, cancellationToken: ct).ConfigureAwait(false);
            else
                await _context.TimeTickers.InsertOneAsync(session, ticker, cancellationToken: ct).ConfigureAwait(false);
            var count = 1;
            if (ticker.Children != null)
            {
                foreach (var child in ticker.Children)
                    if (child is TTimeTicker c)
                        count += await InsertWithChildren(
                            session, c, ticker.Id, chainRootId, chainGeneration, ct).ConfigureAwait(false);
            }
            return count;
        }

        public async Task<int> UpdateTimeTickers(TTimeTicker[] tickers, CancellationToken cancellationToken = default)
        {
            if (tickers == null || tickers.Length == 0) return 0;
            // Preserve historical upsert behavior for genuinely-new top-level rows, while remembering
            // which rows existed when this mutation began. If retention wins and deletes one of those
            // rows before this transaction wins the fence, do not resurrect it from a stale replacement.
            var topLevelIds = tickers.Select(x => x.Id).Distinct().ToArray();
            var requiredExistingIds = (await _context.TimeTickers
                    .Find(InPartition(Builders<TTimeTicker>.Filter.In(x => x.Id, topLevelIds)))
                    .Project(x => x.Id)
                    .ToListAsync(cancellationToken).ConfigureAwait(false))
                .ToHashSet();
            return await ExecuteAdmittedGraphMutationAsync(
                async (session, ct) =>
                {
                    if (!await ReferencedParentsExistAsync(session, tickers, ct).ConfigureAwait(false))
                        return 0;
                    if (requiredExistingIds.Count > 0)
                    {
                        var stillExisting = await _context.TimeTickers.CountDocumentsAsync(
                            session,
                            InPartition(Builders<TTimeTicker>.Filter.In(x => x.Id, requiredExistingIds)),
                            cancellationToken: ct).ConfigureAwait(false);
                        if (stillExisting != requiredExistingIds.Count)
                            return 0;
                    }
                    var count = 0;
                    foreach (var ticker in tickers)
                        count += await ReplaceWithChildren(
                            session, ticker, null, ticker.Id, ticker.ChainGeneration, ct).ConfigureAwait(false);
                    return count;
                },
                async ct =>
                {
                    var count = 0;
                    foreach (var ticker in tickers)
                        count += await ReplaceWithChildren(
                            null, ticker, null, ticker.Id, ticker.ChainGeneration, ct).ConfigureAwait(false);
                    return count;
                },
                cancellationToken).ConfigureAwait(false);
        }

        private async Task<int> ReplaceWithChildren(
            IClientSessionHandle session, TTimeTicker ticker, Guid? parentId, Guid chainRootId,
            Guid? chainGeneration, CancellationToken ct)
        {
            Stamp(ticker);
            if (parentId.HasValue) ticker.ParentId = parentId.Value;
            ticker.ChainRootId = chainRootId;
            ticker.ChainGeneration = chainGeneration;
            var filter = InPartition(Builders<TTimeTicker>.Filter.Eq(x => x.Id, ticker.Id));
            var options = new ReplaceOptions { IsUpsert = true };
            ReplaceOneResult result;
            if (session == null)
                result = await _context.TimeTickers.ReplaceOneAsync(filter, ticker, options, ct).ConfigureAwait(false);
            else
                result = await _context.TimeTickers.ReplaceOneAsync(session, filter, ticker, options, ct).ConfigureAwait(false);
            var count = (int)result.ModifiedCount + (result.UpsertedId is null ? 0 : 1);
            if (ticker.Children != null)
            {
                foreach (var child in ticker.Children)
                    if (child is TTimeTicker c)
                        count += await ReplaceWithChildren(
                            session, c, ticker.Id, chainRootId, chainGeneration, ct).ConfigureAwait(false);
            }
            return count;
        }

        // A mutation that attaches to an existing aggregate must prove its external parent still exists
        // after winning the graph fence. If retention committed first, this check fails and the mutation
        // returns zero rather than inserting/upserting an orphan under the deleted aggregate.
        private async Task<bool> ReferencedParentsExistAsync(
            IClientSessionHandle session, IEnumerable<TTimeTicker> roots, CancellationToken cancellationToken)
        {
            var suppliedIds = new HashSet<Guid>();
            var referencedParents = new HashSet<Guid>();
            void Visit(TTimeTicker node, Guid? imposedParent)
            {
                suppliedIds.Add(node.Id);
                var parent = imposedParent ?? node.ParentId;
                if (parent.HasValue) referencedParents.Add(parent.Value);
                if (node.Children == null) return;
                foreach (var child in node.Children)
                    if (child is TTimeTicker typed) Visit(typed, node.Id);
            }
            foreach (var root in roots) Visit(root, null);
            referencedParents.ExceptWith(suppliedIds);
            if (referencedParents.Count == 0) return true;

            var count = await _context.TimeTickers.CountDocumentsAsync(
                session,
                InPartition(Builders<TTimeTicker>.Filter.In(x => x.Id, referencedParents)),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            return count == referencedParents.Count;
        }

        public async Task<int> ReplaceTimeTickerChainAsync(
            Guid oldRootId, TTimeTicker newRoot, CancellationToken cancellationToken = default)
        {
            if (newRoot == null) throw new ArgumentNullException(nameof(newRoot));
            if (TransactionsKnownUnavailable)
                throw new NotSupportedException(
                    "Atomic Mongo time-ticker chain replacement requires a replica set transaction.");

            return await ExecuteAdmittedGraphMutationAsync(
                async (session, ct) =>
                {
                    var oldExists = await _context.TimeTickers.Find(
                            session, Builders<TTimeTicker>.Filter.And(
                                Builders<TTimeTicker>.Filter.Eq(x => x.ApplicationNamespaceKey, _runtimePartitionKey),
                                Builders<TTimeTicker>.Filter.Eq(x => x.Id, oldRootId),
                                Builders<TTimeTicker>.Filter.Eq(x => x.ParentId, (Guid?)null)))
                        .AnyAsync(ct).ConfigureAwait(false);
                    if (!oldExists) return 0;

                    var inserted = await InsertWithChildren(
                        session, newRoot, null, newRoot.Id, null, ct).ConfigureAwait(false);
                    var oldIds = await CollectBoundedChainIds(
                        session, oldRootId, 1_000, ct).ConfigureAwait(false);
                    if (oldIds == null)
                        throw new InvalidOperationException("The original chain exceeds the safe replacement bound.");
                    var deleted = await _context.TimeTickers.DeleteManyAsync(
                        session,
                        InPartition(Builders<TTimeTicker>.Filter.In(x => x.Id, oldIds)),
                        cancellationToken: ct).ConfigureAwait(false);
                    if (deleted.DeletedCount != oldIds.Count)
                        throw new InvalidOperationException("The original chain changed during replacement.");
                    await _context.TickerResults.DeleteManyAsync(
                        session,
                        ResultFilter(oldIds, TimeResultKind),
                        cancellationToken: ct).ConfigureAwait(false);
                    return inserted;
                },
                _ => throw new NotSupportedException(
                    "Atomic Mongo time-ticker chain replacement requires a replica set transaction."),
                cancellationToken).ConfigureAwait(false);
        }

        public async Task<int> RemoveTimeTickers(Guid[] tickerIds, CancellationToken cancellationToken = default)
        {
            if (tickerIds == null || tickerIds.Length == 0) return 0;
            return await ExecuteAdmittedGraphMutationAsync(
                (session, ct) => RemoveTimeTickersCoreAsync(session, tickerIds, ct),
                ct => RemoveTimeTickersCoreAsync(null, tickerIds, ct),
                cancellationToken).ConfigureAwait(false);
        }

        private async Task<int> RemoveTimeTickersCoreAsync(
            IClientSessionHandle session, IEnumerable<Guid> tickerIds, CancellationToken cancellationToken)
        {
            var count = 0;
            foreach (var id in tickerIds.Distinct())
            {
                var allIds = await CollectDescendantIds(session, id, cancellationToken).ConfigureAwait(false);
                allIds.Add(id);
                var filter = InPartition(Builders<TTimeTicker>.Filter.In(x => x.Id, allIds));
                DeleteResult result;
                if (session == null)
                    result = await _context.TimeTickers.DeleteManyAsync(filter, cancellationToken).ConfigureAwait(false);
                else
                    result = await _context.TimeTickers.DeleteManyAsync(
                        session, filter, cancellationToken: cancellationToken).ConfigureAwait(false);
                var resultFilter = ResultFilter(allIds, TimeResultKind);
                if (session == null)
                    await _context.TickerResults.DeleteManyAsync(resultFilter, cancellationToken).ConfigureAwait(false);
                else
                    await _context.TickerResults.DeleteManyAsync(
                        session, resultFilter, cancellationToken: cancellationToken).ConfigureAwait(false);
                count += (int)result.DeletedCount;
            }
            return count;
        }

        public async Task<TCronTicker> GetCronTickerById(Guid id, CancellationToken cancellationToken)
            => await _context.CronTickers
                .Find(InPartition(Builders<TCronTicker>.Filter.Eq(x => x.Id, id)))
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

        public async Task<TCronTicker[]> GetCronTickers(Expression<Func<TCronTicker, bool>> predicate, CancellationToken cancellationToken)
        {
            var filter = InPartition(predicate is null ? Builders<TCronTicker>.Filter.Empty : Builders<TCronTicker>.Filter.Where(predicate));
            var rows = await _context.CronTickers
                .Find(filter)
                .Sort(Builders<TCronTicker>.Sort.Descending(x => x.CreatedAt))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            return rows.ToArray();
        }

        public async Task<PaginationResult<TCronTicker>> GetCronTickersPaginated(Expression<Func<TCronTicker, bool>> predicate, int pageNumber, int pageSize, CancellationToken cancellationToken = default)
        {
            var filter = InPartition(predicate is null ? Builders<TCronTicker>.Filter.Empty : Builders<TCronTicker>.Filter.Where(predicate));
            var total = await _context.CronTickers.CountDocumentsAsync(filter, cancellationToken: cancellationToken).ConfigureAwait(false);
            var rows = await _context.CronTickers
                .Find(filter)
                .Sort(Builders<TCronTicker>.Sort.Descending(x => x.CreatedAt))
                .Skip((pageNumber - 1) * pageSize)
                .Limit(pageSize)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            return new PaginationResult<TCronTicker>(rows, (int)total, pageNumber, pageSize);
        }

        public async Task<int> InsertCronTickers(TCronTicker[] tickers, CancellationToken cancellationToken)
        {
            if (tickers.Length == 0) return 0;
            foreach (var ticker in tickers)
            {
                Stamp(ticker);
                if (ticker.DefinitionRevision <= 0)
                    ticker.DefinitionRevision = 1;
            }
            if (_requiresActivatedRuntimeAdmission)
            {
                if (TransactionsKnownUnavailable) return 0;
                using var session = await _context.Database.Client
                    .StartSessionAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
                return await session.WithTransactionAsync(async (s, ct) =>
                {
                    if (!await LockRunnableAdmissionAsync(s, ct).ConfigureAwait(false)) return 0;
                    await _context.CronTickers.InsertManyAsync(
                        s, tickers, cancellationToken: ct).ConfigureAwait(false);
                    return tickers.Length;
                }, GraphTransactionOptions, cancellationToken).ConfigureAwait(false);
            }
            await _context.CronTickers.InsertManyAsync(tickers, cancellationToken: cancellationToken).ConfigureAwait(false);
            return tickers.Length;
        }

        public async Task<int> UpdateCronTickers(TCronTicker[] cronTicker, CancellationToken cancellationToken)
        {
            if (_requiresActivatedRuntimeAdmission)
            {
                if (TransactionsKnownUnavailable) return 0;
                var updated = 0;
                foreach (var ticker in cronTicker)
                {
                    using var session = await _context.Database.Client
                        .StartSessionAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
                    updated += await session.WithTransactionAsync(async (s, ct) =>
                    {
                        if (!await LockRunnableAdmissionAsync(s, ct).ConfigureAwait(false)) return 0;
                        var current = await _context.CronTickers.Find(s,
                                InPartition(Builders<TCronTicker>.Filter.Eq(x => x.Id, ticker.Id)))
                            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
                        if (current == null) return 0;
                        ticker.DefinitionRevision = Math.Max(1, current.DefinitionRevision + 1);
                        ticker.UpdatedAt = _clock.UtcNow;
                        var occurrenceFilter = Builders<CronTickerOccurrenceEntity<TCronTicker>>.Filter.And(
                            Builders<CronTickerOccurrenceEntity<TCronTicker>>.Filter.Eq(
                                x => x.ApplicationNamespaceKey, _runtimePartitionKey),
                            Builders<CronTickerOccurrenceEntity<TCronTicker>>.Filter.Eq(
                                x => x.CronTickerId, ticker.Id),
                            Builders<CronTickerOccurrenceEntity<TCronTicker>>.Filter.Eq(
                                x => x.DefinitionRevision, current.DefinitionRevision),
                            Builders<CronTickerOccurrenceEntity<TCronTicker>>.Filter.In(
                                x => x.Status, [TickerStatus.Idle, TickerStatus.Queued]),
                            Builders<CronTickerOccurrenceEntity<TCronTicker>>.Filter.Eq(
                                x => x.LockHolder, null));
                        await _context.CronTickerOccurrences.UpdateManyAsync(s, occurrenceFilter,
                            Builders<CronTickerOccurrenceEntity<TCronTicker>>.Update
                                .Set(x => x.Status, TickerStatus.Skipped)
                                .Set(x => x.SkippedReason,
                                    "Quarantined because its Cron definition revision is stale.")
                                .Set(x => x.ExecutedAt, _clock.UtcNow)
                                .Set(x => x.UpdatedAt, _clock.UtcNow),
                            cancellationToken: ct).ConfigureAwait(false);
                        var replace = await _context.CronTickers.ReplaceOneAsync(s,
                            Builders<TCronTicker>.Filter.And(
                                Builders<TCronTicker>.Filter.Eq(x => x.ApplicationNamespaceKey, _runtimePartitionKey),
                                Builders<TCronTicker>.Filter.Eq(x => x.Id, ticker.Id),
                                Builders<TCronTicker>.Filter.Eq(
                                    x => x.DefinitionRevision, current.DefinitionRevision)),
                            ticker, new ReplaceOptions { IsUpsert = false }, ct).ConfigureAwait(false);
                        return (int)replace.ModifiedCount;
                    }, GraphTransactionOptions, cancellationToken).ConfigureAwait(false);
                }
                return updated;
            }
            var count = 0;
            foreach (var t in cronTicker)
            {
                Stamp(t);
                var result = await _context.CronTickers.ReplaceOneAsync(
                    InPartition(Builders<TCronTicker>.Filter.Eq(x => x.Id, t.Id)),
                    t,
                    new ReplaceOptions { IsUpsert = false },
                    cancellationToken).ConfigureAwait(false);
                count += (int)result.ModifiedCount;
            }
            return count;
        }

        public async Task<int> RemoveCronTickers(Guid[] cronTickerIds, CancellationToken cancellationToken)
        {
            if (_requiresActivatedRuntimeAdmission)
            {
                if (TransactionsKnownUnavailable) return 0;
                using var session = await _context.Database.Client
                    .StartSessionAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
                return await session.WithTransactionAsync(async (s, ct) =>
                {
                    if (!await LockRunnableAdmissionAsync(s, ct).ConfigureAwait(false)) return 0;
                    var deleted = await _context.CronTickers.DeleteManyAsync(s,
                        InPartition(Builders<TCronTicker>.Filter.In(x => x.Id, cronTickerIds)),
                        cancellationToken: ct)
                        .ConfigureAwait(false);
                    return (int)deleted.DeletedCount;
                }, GraphTransactionOptions, cancellationToken).ConfigureAwait(false);
            }
            var result = await _context.CronTickers.DeleteManyAsync(
                InPartition(Builders<TCronTicker>.Filter.In(x => x.Id, cronTickerIds)),
                cancellationToken).ConfigureAwait(false);
            return (int)result.DeletedCount;
        }

        public async Task<CronTickerOccurrenceEntity<TCronTicker>[]> GetAllCronTickerOccurrences(Expression<Func<CronTickerOccurrenceEntity<TCronTicker>, bool>> predicate, CancellationToken cancellationToken = default)
        {
            var filter = predicate is null
                ? Builders<CronTickerOccurrenceEntity<TCronTicker>>.Filter.Empty
                : Builders<CronTickerOccurrenceEntity<TCronTicker>>.Filter.Where(predicate);
            filter = InPartition(filter);
            var rows = await _context.CronTickerOccurrences
                .Find(filter)
                .Sort(Builders<CronTickerOccurrenceEntity<TCronTicker>>.Sort.Descending(x => x.CreatedAt))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            return rows.ToArray();
        }

        public async Task<PaginationResult<CronTickerOccurrenceEntity<TCronTicker>>> GetAllCronTickerOccurrencesPaginated(Expression<Func<CronTickerOccurrenceEntity<TCronTicker>, bool>> predicate, int pageNumber, int pageSize, CancellationToken cancellationToken = default)
        {
            var filter = predicate is null
                ? Builders<CronTickerOccurrenceEntity<TCronTicker>>.Filter.Empty
                : Builders<CronTickerOccurrenceEntity<TCronTicker>>.Filter.Where(predicate);
            filter = InPartition(filter);
            var total = await _context.CronTickerOccurrences.CountDocumentsAsync(filter, cancellationToken: cancellationToken).ConfigureAwait(false);
            var rows = await _context.CronTickerOccurrences
                .Find(filter)
                .Sort(Builders<CronTickerOccurrenceEntity<TCronTicker>>.Sort.Descending(x => x.CreatedAt))
                .Skip((pageNumber - 1) * pageSize)
                .Limit(pageSize)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            return new PaginationResult<CronTickerOccurrenceEntity<TCronTicker>>(rows, (int)total, pageNumber, pageSize);
        }

        public async Task<int> InsertCronTickerOccurrences(CronTickerOccurrenceEntity<TCronTicker>[] cronTickerOccurrences, CancellationToken cancellationToken)
        {
            if (cronTickerOccurrences.Length == 0) return 0;
            var now = _clock.UtcNow;
            var count = 0;
            foreach (var occ in cronTickerOccurrences)
            {
                Stamp(occ);
                async Task<int> InsertAgainstRevisionAsync(
                    IClientSessionHandle session, TCronTicker cron, CancellationToken ct)
                {
                    if (cron == null) return 0;
                    if (occ.DefinitionRevision <= 0)
                        occ.DefinitionRevision = cron.DefinitionRevision;
                    if (occ.DefinitionRevision != cron.DefinitionRevision &&
                        occ.Status is TickerStatus.Idle or TickerStatus.Queued)
                        QuarantineOccurrence(occ, now,
                            "Quarantined because its Cron definition revision is stale.");
                    try
                    {
                        if (session == null)
                            await _context.CronTickerOccurrences.InsertOneAsync(
                                occ, cancellationToken: ct).ConfigureAwait(false);
                        else
                            await _context.CronTickerOccurrences.InsertOneAsync(
                                session, occ, cancellationToken: ct).ConfigureAwait(false);
                        return 1;
                    }
                    catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
                    {
                        return 0;
                    }
                }

                if (TransactionsKnownUnavailable)
                {
                    throw new NotSupportedException(
                        "MongoDB Cron occurrence insertion requires transactions so the parent revision fence and child insert commit atomically. Standalone MongoDB is unsupported for this operation.");
                }

                using var session = await _context.Database.Client
                    .StartSessionAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
                count += await session.WithTransactionAsync(async (s, ct) =>
                {
                    if (!await LockRunnableAdmissionAsync(s, ct).ConfigureAwait(false))
                        return 0;
                    var cron = await _context.CronTickers.Find(
                            s, InPartition(Builders<TCronTicker>.Filter.Eq(x => x.Id, occ.CronTickerId)))
                        .FirstOrDefaultAsync(ct).ConfigureAwait(false);
                    if (cron == null || !await LockCronRevisionAsync(
                            s, cron.Id, cron.DefinitionRevision, ct).ConfigureAwait(false))
                        return 0;

                    // Duplicate-key errors abort a Mongo transaction even when caught. Because the
                    // authoritative Cron row is locked above, inspect the unique logical slot inside
                    // that serialized transaction and use the index only as a final integrity guard.
                    var slotFilter = Builders<CronTickerOccurrenceEntity<TCronTicker>>.Filter.And(
                        Builders<CronTickerOccurrenceEntity<TCronTicker>>.Filter.Eq(
                            x => x.ApplicationNamespaceKey, _runtimePartitionKey),
                        Builders<CronTickerOccurrenceEntity<TCronTicker>>.Filter.Eq(x => x.CronTickerId, occ.CronTickerId),
                        Builders<CronTickerOccurrenceEntity<TCronTicker>>.Filter.Eq(x => x.ExecutionTime, occ.ExecutionTime));
                    if (await _context.CronTickerOccurrences.Find(s, slotFilter)
                            .AnyAsync(ct).ConfigureAwait(false))
                        return 0;

                    return await InsertAgainstRevisionAsync(s, cron, ct).ConfigureAwait(false);
                }, GraphTransactionOptions, cancellationToken).ConfigureAwait(false);
            }
            return count;
        }

        public async Task<int> RemoveCronTickerOccurrences(Guid[] cronTickerOccurrences, CancellationToken cancellationToken)
        {
            if (cronTickerOccurrences == null || cronTickerOccurrences.Length == 0) return 0;
            var ids = cronTickerOccurrences.Distinct().ToArray();
            var rowFilter = InPartition(
                Builders<CronTickerOccurrenceEntity<TCronTicker>>.Filter.In(x => x.Id, ids));
            var resultFilter = ResultFilter(ids, CronOccurrenceResultKind);

            if (TransactionsKnownUnavailable)
            {
                var standalone = await _context.CronTickerOccurrences.DeleteManyAsync(
                    rowFilter, cancellationToken).ConfigureAwait(false);
                await _context.TickerResults.DeleteManyAsync(resultFilter, cancellationToken).ConfigureAwait(false);
                return (int)standalone.DeletedCount;
            }

            using var session = await _context.Database.Client
                .StartSessionAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            return await session.WithTransactionAsync(
                async (s, ct) =>
                {
                    await TouchGraphFenceAsync(s, ct).ConfigureAwait(false);
                    var result = await _context.CronTickerOccurrences.DeleteManyAsync(
                        s, rowFilter, cancellationToken: ct).ConfigureAwait(false);
                    await _context.TickerResults.DeleteManyAsync(
                        s, resultFilter, cancellationToken: ct).ConfigureAwait(false);
                    return (int)result.DeletedCount;
                }, GraphTransactionOptions, cancellationToken).ConfigureAwait(false);
        }

        public async Task<CronTickerOccurrenceEntity<TCronTicker>[]> AcquireImmediateCronOccurrencesAsync(Guid[] occurrenceIds, CancellationToken cancellationToken = default)
        {
            if (occurrenceIds == null || occurrenceIds.Length == 0)
                return Array.Empty<CronTickerOccurrenceEntity<TCronTicker>>();
            if (TransactionsKnownUnavailable)
                return Array.Empty<CronTickerOccurrenceEntity<TCronTicker>>();

            var now = _clock.UtcNow;
            var acquisitionToken = Guid.NewGuid();
            var coll = _context.CronTickerOccurrences;
            var fb = Builders<CronTickerOccurrenceEntity<TCronTicker>>.Filter;
            var update = Builders<CronTickerOccurrenceEntity<TCronTicker>>.Update
                .Set(x => x.LockHolder, _lockHolder)
                .Set(x => x.LockedAt, now)
                .Set(x => x.LeaseUntil, NextLeaseUntil(now))
                .Set(x => x.AcquisitionToken, acquisitionToken)
                .Set(x => x.Status, TickerStatus.InProgress)
                .Set(x => x.UpdatedAt, now);
            var options = new FindOneAndUpdateOptions<CronTickerOccurrenceEntity<TCronTicker>>
                { ReturnDocument = ReturnDocument.After };
            var rows = new List<CronTickerOccurrenceEntity<TCronTicker>>(occurrenceIds.Length);

            foreach (var id in occurrenceIds.Distinct())
            {
                using var session = await _context.Database.Client
                    .StartSessionAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
                var acquired = await session.WithTransactionAsync(async (s, ct) =>
                {
                    if (!await LockRunnableAdmissionAsync(s, ct).ConfigureAwait(false))
                        return null;
                    var candidate = await coll.Find(s, InPartition(fb.Eq(x => x.Id, id)))
                        .FirstOrDefaultAsync(ct).ConfigureAwait(false);
                    if (candidate == null ||
                        !await LockCronRevisionAsync(s, candidate.CronTickerId,
                            candidate.DefinitionRevision, ct).ConfigureAwait(false))
                        return null;

                    var filter = fb.And(
                        fb.Eq(x => x.ApplicationNamespaceKey, _runtimePartitionKey),
                        fb.Eq(x => x.Id, id),
                        fb.Eq(x => x.DefinitionRevision, candidate.DefinitionRevision),
                        MongoUpdateBuilders.CanAcquireCronOccurrence<TCronTicker>(_lockHolder));
                    var row = await coll.FindOneAndUpdateAsync(s, filter, update, options, ct)
                        .ConfigureAwait(false);
                    if (row == null) return null;
                    row.CronTicker = await _context.CronTickers.Find(s,
                            InPartition(Builders<TCronTicker>.Filter.Eq(x => x.Id, candidate.CronTickerId)))
                        .FirstOrDefaultAsync(ct).ConfigureAwait(false);
                    return row;
                }, GraphTransactionOptions, cancellationToken).ConfigureAwait(false);
                if (acquired != null) rows.Add(acquired);
            }

            return rows.ToArray();
        }

        // ===================================================================
        // Queryable hooks — implemented in MongoTickerQueryable (Task 6)
        // ===================================================================

        public ITickerQueryable<TTimeTicker> TimeTickersQuery()
            => new MongoTickerQueryable<TTimeTicker>(
                _context.TimeTickers,
                x => x.ApplicationNamespaceKey == _runtimePartitionKey,
                async (entity, relations, ct) =>
                {
                    if (relations.Any(r => r == TickerRelation.Children || r == TickerRelation.ChildrenDeep))
                        entity.Children = await LoadChildrenRecursive<TTimeTicker>(entity.Id, ct).ConfigureAwait(false);
                });

        public ITickerQueryable<TCronTicker> CronTickersQuery()
            => new MongoTickerQueryable<TCronTicker>(
                _context.CronTickers, x => x.ApplicationNamespaceKey == _runtimePartitionKey, null);

        public ITickerQueryable<CronTickerOccurrenceEntity<TCronTicker>> CronTickerOccurrencesQuery()
            => new MongoTickerQueryable<CronTickerOccurrenceEntity<TCronTicker>>(
                _context.CronTickerOccurrences,
                x => x.ApplicationNamespaceKey == _runtimePartitionKey,
                async (occ, relations, ct) =>
                {
                    if (relations.Any(r => r == TickerRelation.CronTicker))
                    {
                        occ.CronTicker = await _context.CronTickers
                            .Find(InPartition(Builders<TCronTicker>.Filter.Eq(x => x.Id, occ.CronTickerId)))
                            .FirstOrDefaultAsync(ct)
                            .ConfigureAwait(false);
                    }
                });

        // ===================================================================
        // Private helpers
        // ===================================================================

        /// <summary>
        /// Breadth-first hydrate the whole child-definition subtree for the given
        /// roots. Child definitions are the <c>ExecutionTime == null</c> rows chained
        /// under a root by <see cref="TimeTickerEntity{T}.ParentId"/>; a chain can go
        /// root → child → grandchild → deeper. We fetch one batch per depth tier
        /// (<c>WHERE ParentId IN frontier</c>) rather than a query per node, and guard
        /// with a visited set so malformed cyclic data cannot loop forever even though
        /// the schema should prevent a cycle.
        /// </summary>
        private async Task<Dictionary<Guid, List<TTimeTicker>>> LoadChildrenLookup(Guid[] rootIds, CancellationToken ct)
        {
            if (rootIds.Length == 0) return new Dictionary<Guid, List<TTimeTicker>>();
            var fb = Builders<TTimeTicker>.Filter;
            var descendants = new List<TTimeTicker>();
            var visited = new HashSet<Guid>(rootIds);
            var frontier = rootIds.ToList();

            while (frontier.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                var rows = await _context.TimeTickers
                    .Find(InPartition(fb.And(
                        fb.In(x => x.ParentId, frontier.Select(p => (Guid?)p)),
                        fb.Eq(x => x.ExecutionTime, null))))
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                var next = new List<Guid>(rows.Count);
                foreach (var row in rows)
                {
                    // Cycle guard: a row already placed cannot re-enqueue its subtree.
                    if (!visited.Add(row.Id)) continue;
                    descendants.Add(row);
                    next.Add(row.Id);
                }
                frontier = next;
            }

            return BuildChildLookup(descendants);
        }

        /// <summary>Groups already-fetched child-definition rows by their parent id.</summary>
        internal static Dictionary<Guid, List<TTimeTicker>> BuildChildLookup(IEnumerable<TTimeTicker> descendants)
            => descendants
                .Where(r => r.ParentId.HasValue)
                .GroupBy(r => r.ParentId.Value)
                .ToDictionary(g => g.Key, g => g.ToList());

        internal static TimeTickerEntity BuildQueuedEntity(TTimeTicker ticker, Dictionary<Guid, List<TTimeTicker>> byParent)
        {
            // Attach the raw child + grandchild layers so the compiled projection
            // (root → child → grandchild) captures them, then stitch any deeper layers
            // onto the projected tree — the same probe-and-extend contract the EF
            // provider uses so nested chains are never silently truncated.
            AttachRawChildren(ticker, byParent, depth: 2, new HashSet<Guid>());
            var projected = ProjectTimeTicker(ticker);
            ExtendProjectedChainsBeyondGrandchildren(projected, byParent);
            return projected;
        }

        private static void StampQueueGeneration(TimeTickerEntity node, Guid rootId, Guid generation)
        {
            node.ChainRootId = rootId;
            node.ChainGeneration = generation;
            foreach (var child in node.Children ?? [])
                StampQueueGeneration(child, rootId, generation);
        }

        /// <summary>Attaches up to <paramref name="depth"/> raw child levels onto <paramref name="node"/>.</summary>
        private static void AttachRawChildren(TTimeTicker node, Dictionary<Guid, List<TTimeTicker>> byParent, int depth, HashSet<Guid> visited)
        {
            if (depth <= 0 || !visited.Add(node.Id) || !byParent.TryGetValue(node.Id, out var children))
            {
                node.Children = new List<TTimeTicker>();
                return;
            }
            node.Children = children;
            foreach (var child in children)
                AttachRawChildren(child, byParent, depth - 1, visited);
        }

        /// <summary>
        /// The compiled projection stops at grandchildren; walk from there and
        /// BFS-attach any deeper child definitions from the same lookup. The visited
        /// set guards against cyclic data re-entering the walk.
        /// </summary>
        private static void ExtendProjectedChainsBeyondGrandchildren(TimeTickerEntity root, Dictionary<Guid, List<TTimeTicker>> byParent)
        {
            var visited = new HashSet<Guid>();
            var frontier = new List<TimeTickerEntity>();
            foreach (var child in root.Children)
                foreach (var grandchild in child.Children)
                    frontier.Add(grandchild);

            while (frontier.Count > 0)
            {
                var next = new List<TimeTickerEntity>();
                foreach (var node in frontier)
                {
                    if (!visited.Add(node.Id)) continue;
                    if (!byParent.TryGetValue(node.Id, out var children) || children.Count == 0)
                        continue;

                    var mapped = children
                        .Select(c => new TimeTickerEntity
                        {
                            Id = c.Id,
                            Function = c.Function,
                            RequestContractVersion = c.RequestContractVersion,
                            RequestContractFingerprint = c.RequestContractFingerprint,
                            Retries = c.Retries,
                            RetryIntervals = c.RetryIntervals,
                            TimeoutSeconds = c.TimeoutSeconds,
                            RunCondition = c.RunCondition,
                            ParentId = c.ParentId,
                            ChainRootId = c.ChainRootId,
                            ChainGeneration = c.ChainGeneration,
                        })
                        .ToList();
                    node.Children = mapped;
                    next.AddRange(mapped);
                }
                frontier = next;
            }
        }

        private async Task<bool> LockCronRevisionAsync(
            IClientSessionHandle session, Guid cronId, long revision, CancellationToken ct)
        {
            var fb = Builders<TCronTicker>.Filter;
            var result = await _context.CronTickers.UpdateOneAsync(
                session,
                InPartition(fb.And(fb.Eq(x => x.Id, cronId), fb.Eq(x => x.DefinitionRevision, revision))),
                Builders<TCronTicker>.Update.Set(x => x.DefinitionRevision, revision),
                cancellationToken: ct).ConfigureAwait(false);
            return result.MatchedCount == 1;
        }

        private static bool CanAcquirePendingOccurrence(
            CronTickerOccurrenceEntity<TCronTicker> occurrence, DateTime now)
            => occurrence.Status is TickerStatus.Idle or TickerStatus.Queued
               && (string.IsNullOrEmpty(occurrence.LockHolder))
               && occurrence.AcquisitionToken == null
               && (occurrence.LeaseUntil == null || occurrence.LeaseUntil <= now);

        private static void QuarantineOccurrence(
            CronTickerOccurrenceEntity<TCronTicker> occurrence, DateTime now, string reason)
        {
            occurrence.Status = TickerStatus.Skipped;
            occurrence.SkippedReason = reason;
            occurrence.ExecutedAt ??= now;
            occurrence.LockHolder = null;
            occurrence.LockedAt = null;
            occurrence.LeaseUntil = null;
            occurrence.AcquisitionToken = null;
            occurrence.UpdatedAt = now;
        }

        private async Task QuarantineStalePendingOccurrenceAsync(
            CronTickerOccurrenceEntity<TCronTicker> occurrence, DateTime now, CancellationToken ct)
        {
            var fb = Builders<CronTickerOccurrenceEntity<TCronTicker>>.Filter;
            var filter = InPartition(fb.And(
                fb.Eq(x => x.Id, occurrence.Id),
                fb.Eq(x => x.DefinitionRevision, occurrence.DefinitionRevision),
                fb.In(x => x.Status, new[] { TickerStatus.Idle, TickerStatus.Queued }),
                fb.Or(fb.Eq(x => x.LockHolder, null), fb.Eq(x => x.LockHolder, string.Empty)),
                fb.Eq(x => x.AcquisitionToken, null),
                fb.Or(fb.Eq(x => x.LeaseUntil, null), fb.Lte(x => x.LeaseUntil, now))));
            await _context.CronTickerOccurrences.UpdateOneAsync(
                filter,
                Builders<CronTickerOccurrenceEntity<TCronTicker>>.Update
                    .Set(x => x.Status, TickerStatus.Skipped)
                    .Set(x => x.SkippedReason,
                        "Quarantined because its Cron definition revision is stale.")
                    .Set(x => x.ExecutedAt, now)
                    .Set(x => x.LockHolder, (string)null)
                    .Set(x => x.LockedAt, (DateTime?)null)
                    .Set(x => x.LeaseUntil, (DateTime?)null)
                    .Set(x => x.AcquisitionToken, (Guid?)null)
                    .Set(x => x.UpdatedAt, now),
                cancellationToken: ct).ConfigureAwait(false);
        }

        private async Task<Dictionary<Guid, TCronTicker>> LoadCronTickers(Guid[] ids, CancellationToken ct)
        {
            if (ids.Length == 0) return new Dictionary<Guid, TCronTicker>();
            var rows = await _context.CronTickers
                .Find(InPartition(Builders<TCronTicker>.Filter.In(x => x.Id, ids)))
                .ToListAsync(ct)
                .ConfigureAwait(false);
            return rows.ToDictionary(r => r.Id);
        }

        private async Task<List<T>> LoadChildrenRecursive<T>(Guid parentId, CancellationToken ct) where T : TTimeTicker
        {
            var rows = await _context.TimeTickers
                .Find(InPartition(Builders<TTimeTicker>.Filter.Eq(x => x.ParentId, (Guid?)parentId)))
                .ToListAsync(ct)
                .ConfigureAwait(false);
            var result = new List<T>(rows.Count);
            foreach (var row in rows)
            {
                row.Children = (await LoadChildrenRecursive<TTimeTicker>(row.Id, ct).ConfigureAwait(false)).Cast<TTimeTicker>().ToList();
                result.Add((T)row);
            }
            return result;
        }

        private async Task<HashSet<Guid>> CollectDescendantIds(
            IClientSessionHandle session, Guid parentId, CancellationToken ct)
        {
            var collected = new HashSet<Guid>();
            var frontier = new Queue<Guid>();
            frontier.Enqueue(parentId);
            while (frontier.Count > 0)
            {
                var id = frontier.Dequeue();
                var filter = InPartition(Builders<TTimeTicker>.Filter.Eq(x => x.ParentId, (Guid?)id));
                var query = session == null
                    ? _context.TimeTickers.Find(filter)
                    : _context.TimeTickers.Find(session, filter);
                var childIds = await query
                    .Project(x => x.Id)
                    .ToListAsync(ct)
                    .ConfigureAwait(false);
                foreach (var c in childIds)
                {
                    if (collected.Add(c)) frontier.Enqueue(c);
                }
            }
            return collected;
        }
    }
}
