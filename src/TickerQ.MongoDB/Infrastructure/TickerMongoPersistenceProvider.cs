using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Core.Servers;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
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
        private readonly IMongoCollection<BsonDocument> _graphFence;

        private const string GraphFenceId = "time-ticker-graph";
        private static readonly TransactionOptions GraphTransactionOptions = new(
            readConcern: ReadConcern.Snapshot,
            writeConcern: WriteConcern.WMajority);

        // Deterministic race-test seams. They run inside the transaction at the named fence points.
        internal Func<CancellationToken, Task> BeforeGraphMutationFenceForTestAsync { get; set; }
        internal Func<CancellationToken, Task> AfterGraphMutationFenceForTestAsync { get; set; }
        internal Func<CancellationToken, Task> BeforeRetentionFenceForTestAsync { get; set; }
        internal Func<Guid, CancellationToken, Task> AfterRetentionDiscoveryForTestAsync { get; set; }
        internal Func<CancellationToken, Task> AfterResultMutationForTestAsync { get; set; }

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
            _graphFence = context.Database.GetCollection<BsonDocument>(
                context.TimeTickers.CollectionNamespace.CollectionName + "_GraphFence");
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

        private static bool IsCanonicalTransactionsUnsupported(MongoCommandException exception)
            => exception.Code == 20 &&
               exception.Message.Contains(
                   "Transaction numbers are only allowed on a replica set member or mongos",
                   StringComparison.OrdinalIgnoreCase);

        private async Task TouchGraphFenceAsync(IClientSessionHandle session, CancellationToken cancellationToken)
        {
            await _graphFence.UpdateOneAsync(
                session,
                Builders<BsonDocument>.Filter.Eq("_id", GraphFenceId),
                Builders<BsonDocument>.Update.Inc("Version", 1L),
                new UpdateOptions { IsUpsert = true },
                cancellationToken).ConfigureAwait(false);
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
                        session, fb.In(x => x.ParentId, frontier.Keys.Select(x => (Guid?)x)))
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
                        fb.In(x => x.Id, group.Select(x => x.Key)),
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

        public async Task<bool> CommitTerminalTickerAsync(
            InternalFunctionContext functionContext, CancellationToken cancellationToken = default)
        {
            if (functionContext == null) throw new ArgumentNullException(nameof(functionContext));
            if (!functionContext.GetPropsToUpdate().Contains(nameof(InternalFunctionContext.Status)) ||
                functionContext.Status is not (TickerStatus.Done or TickerStatus.DueDone or
                    TickerStatus.Failed or TickerStatus.Cancelled or TickerStatus.Skipped))
                throw new InvalidOperationException("Acknowledged terminal persistence requires a terminal status mutation.");
            if (functionContext.Status is TickerStatus.Done or TickerStatus.DueDone &&
                functionContext.ParentId != null)
                return false;
            if (functionContext.Status is TickerStatus.Done or TickerStatus.DueDone)
                return await CommitSuccessfulTickerAsync(functionContext, cancellationToken).ConfigureAwait(false);

            var now = _clock.UtcNow;
            if (functionContext.Type == TickerType.CronTickerOccurrence)
            {
                var fb = Builders<CronTickerOccurrenceEntity<TCronTicker>>.Filter;
                var filter = functionContext.AcquisitionToken.HasValue
                    ? fb.And(fb.Eq(x => x.Id, functionContext.TickerId),
                        fb.Eq(x => x.LockHolder, _lockHolder),
                        fb.Eq(x => x.AcquisitionToken, functionContext.AcquisitionToken))
                    : fb.Where(_ => false);
                var result = await _context.CronTickerOccurrences.UpdateOneAsync(filter,
                    MongoUpdateBuilders.BuildCronOccurrenceUpdate<TCronTicker>(
                        functionContext, now, NextLeaseUntil(now)), cancellationToken: cancellationToken).ConfigureAwait(false);
                return result.MatchedCount == 1;
            }

            var timeFb = Builders<TTimeTicker>.Filter;
            var timeFilter = functionContext.AcquisitionToken.HasValue
                ? timeFb.And(timeFb.Eq(x => x.Id, functionContext.TickerId),
                    timeFb.Eq(x => x.LockHolder, _lockHolder),
                    timeFb.Eq(x => x.AcquisitionToken, functionContext.AcquisitionToken))
                : timeFb.Where(_ => false);
            var timeResult = await _context.TimeTickers.UpdateOneAsync(timeFilter,
                MongoUpdateBuilders.BuildTimeTickerUpdate<TTimeTicker>(
                    functionContext, now, NextLeaseUntil(now)), cancellationToken: cancellationToken).ConfigureAwait(false);
            return timeResult.MatchedCount == 1;
        }

        private static BsonBinaryData ResultId(Guid id)
            => new(id, GuidRepresentation.Standard);

        private static void ValidateResult(TickerResultEnvelope envelope)
        {
            envelope.EnsureSupportedVersion();
            if (envelope.PayloadLength > MaxResultPayloadBytes)
                throw new ArgumentOutOfRangeException(
                    nameof(envelope), envelope.PayloadLength,
                    $"Ticker result payloads cannot exceed {MaxResultPayloadBytes} bytes.");
        }

        private static BsonDocument ToResultDocument(
            Guid id, string kind, TickerResultEnvelope envelope)
        {
            ValidateResult(envelope);
            var document = new BsonDocument
            {
                ["_id"] = ResultId(id),
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
            var filter = Builders<BsonDocument>.Filter.Eq("_id", ResultId(id));
            if (envelope == null)
            {
                await _context.TickerResults.DeleteOneAsync(
                    session, filter, new DeleteOptions(), cancellationToken).ConfigureAwait(false);
                return;
            }

            await _context.TickerResults.ReplaceOneAsync(
                session, filter, ToResultDocument(id, kind, envelope),
                new ReplaceOptions { IsUpsert = true }, cancellationToken).ConfigureAwait(false);
        }

        private async Task<TickerResultEnvelope> GetResultAsync(
            Guid id, string expectedKind, CancellationToken cancellationToken)
        {
            var document = await _context.TickerResults.Find(
                    Builders<BsonDocument>.Filter.Eq("_id", ResultId(id)))
                .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
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

        public Task<TickerResultEnvelope> GetTimeTickerResultAsync(
            Guid id, CancellationToken cancellationToken = default)
            => GetResultAsync(id, TimeResultKind, cancellationToken);

        public Task<TickerResultEnvelope> GetCronTickerOccurrenceResultAsync(
            Guid id, CancellationToken cancellationToken = default)
            => GetResultAsync(id, CronOccurrenceResultKind, cancellationToken);

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

            if (functionContext.ResultEnvelope != null)
                ValidateResult(functionContext.ResultEnvelope);
            if (TransactionsKnownUnavailable)
                throw new NotSupportedException(
                    "Atomic Mongo result publication requires a replica set transaction.");

            using var session = await _context.Database.Client
                .StartSessionAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            try
            {
                return await session.WithTransactionAsync(
                    async (s, ct) =>
                    {
                        await TouchGraphFenceAsync(s, ct).ConfigureAwait(false);
                        var now = _clock.UtcNow;
                        UpdateResult acknowledged;
                        string resultKind;

                        if (functionContext.Type == TickerType.CronTickerOccurrence)
                        {
                            var fb = Builders<CronTickerOccurrenceEntity<TCronTicker>>.Filter;
                            var filter = functionContext.AcquisitionToken.HasValue
                                ? fb.And(
                                    fb.Eq(x => x.Id, functionContext.TickerId),
                                    fb.Eq(x => x.LockHolder, _lockHolder),
                                    fb.Eq(x => x.AcquisitionToken, functionContext.AcquisitionToken))
                                : fb.Where(_ => false);
                            acknowledged = await _context.CronTickerOccurrences.UpdateOneAsync(
                                s, filter,
                                MongoUpdateBuilders.BuildCronOccurrenceUpdate<TCronTicker>(
                                    functionContext, now, NextLeaseUntil(now)),
                                cancellationToken: ct).ConfigureAwait(false);
                            resultKind = CronOccurrenceResultKind;
                        }
                        else
                        {
                            if (functionContext.ParentId != null &&
                                !await LockCurrentChainGenerationAsync(s, functionContext, ct).ConfigureAwait(false))
                                return false;
                            var fb = Builders<TTimeTicker>.Filter;
                            var filter = fb.And(
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
                            resultKind = TimeResultKind;
                        }

                        if (acknowledged.MatchedCount != 1)
                            return false;

                        await StoreOrClearResultAsync(
                            s, functionContext.TickerId, resultKind,
                            functionContext.ResultEnvelope, ct).ConfigureAwait(false);
                        if (AfterResultMutationForTestAsync != null)
                            await AfterResultMutationForTestAsync(ct).ConfigureAwait(false);
                        return true;
                    }, GraphTransactionOptions, cancellationToken).ConfigureAwait(false);
            }
            catch (MongoCommandException ex) when (IsCanonicalTransactionsUnsupported(ex))
            {
                throw new NotSupportedException(
                    "Atomic Mongo result publication requires a replica set transaction.", ex);
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
                    await TouchGraphFenceAsync(s, ct).ConfigureAwait(false);
                    var winners = new List<(TimeTickerEntity, TTimeTicker, Guid)>();
                    foreach (var ticker in timeTickers)
                    {
                        ct.ThrowIfCancellationRequested();
                        var generation = Guid.NewGuid();
                        var filter = fb.And(
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
                    await TouchGraphFenceAsync(s, ct).ConfigureAwait(false);
                    var winners = new List<TTimeTicker>();
                    foreach (var candidate in candidates)
                    {
                        ct.ThrowIfCancellationRequested();
                        var generation = Guid.NewGuid();
                        var filter = fb.And(
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
                ? canAcquire
                : fb.And(fb.In(x => x.Id, timeTickerIds), canAcquire);

            var update = Builders<TTimeTicker>.Update
                .Set(x => x.LockHolder, (string)null)
                .Set(x => x.LockedAt, (DateTime?)null)
                .Set(x => x.LeaseUntil, (DateTime?)null)
                .Set(x => x.AcquisitionToken, (Guid?)null)
                .Set(x => x.Status, TickerStatus.Idle)
                .Set(x => x.UpdatedAt, now);

            await coll.UpdateManyAsync(filter, update, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        public async Task<TimeTickerEntity[]> GetEarliestTimeTickers(CancellationToken cancellationToken = default)
        {
            var now = _clock.UtcNow;
            var oneSecondAgo = now.AddSeconds(-1);
            var coll = _context.TimeTickers;
            var fb = Builders<TTimeTicker>.Filter;

            var baseFilter = fb.And(
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
                .Find(Builders<TTimeTicker>.Filter.Eq(x => x.Id, id))
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
                    Builders<TTimeTicker>.Filter.And(
                        Builders<TTimeTicker>.Filter.In(x => x.Id, timeTickerIds),
                        Builders<TTimeTicker>.Filter.Ne(x => x.LockHolder, RetentionLockHolder)),
                    update,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task<Guid[]> TransitionQueuedTimeTickersToInProgressAsync(
            IReadOnlyCollection<AcquisitionLease> leases, CancellationToken cancellationToken = default)
        {
            var now = _clock.UtcNow;
            var fb = Builders<TTimeTicker>.Filter;
            var update = Builders<TTimeTicker>.Update
                .Set(x => x.Status, TickerStatus.InProgress)
                .Set(x => x.LeaseUntil, NextLeaseUntil(now))
                .Set(x => x.UpdatedAt, now);
            var winners = new List<Guid>(leases.Count);
            foreach (var lease in leases.Where(x => x.AcquisitionToken.HasValue).Distinct())
            {
                var filter = fb.And(
                    fb.Eq(x => x.Id, lease.TickerId),
                    fb.Eq(x => x.Status, TickerStatus.Queued),
                    fb.Eq(x => x.LockHolder, _lockHolder),
                    fb.Eq(x => x.AcquisitionToken, lease.AcquisitionToken));
                var result = await _context.TimeTickers.UpdateOneAsync(
                    filter, update, cancellationToken: cancellationToken).ConfigureAwait(false);
                if (result.ModifiedCount == 1) winners.Add(lease.TickerId);
            }
            return winners.ToArray();
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
                        await TouchGraphFenceAsync(s, ct).ConfigureAwait(false);
                        var filter = fb.And(
                            fb.Eq(x => x.Id, id),
                            MongoUpdateBuilders.CanAcquireTimeTicker<TTimeTicker>(_lockHolder));
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
            var filter = fb.And(fb.Eq(x => x.Id, id), eligible);
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
                        await TouchGraphFenceAsync(s, ct).ConfigureAwait(false);
                        var acquired = await _context.TimeTickers.FindOneAndUpdateAsync(
                            s, filter, update,
                            new FindOneAndUpdateOptions<TTimeTicker> { ReturnDocument = ReturnDocument.After },
                            ct).ConfigureAwait(false);
                        if (acquired == null) return null;
                        await NormalizeChainDescendantsAsync(s, id, token, ct).ConfigureAwait(false);
                        await _context.TickerResults.DeleteOneAsync(
                            s, Builders<BsonDocument>.Filter.Eq("_id", ResultId(id)),
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

            await coll.UpdateManyAsync(
                MongoUpdateBuilders.CanAcquireTimeTicker<TTimeTicker>(instanceIdentifier),
                Builders<TTimeTicker>.Update
                    .Set(x => x.LockHolder, (string)null)
                    .Set(x => x.LockedAt, (DateTime?)null)
                    .Set(x => x.LeaseUntil, (DateTime?)null)
                .Set(x => x.AcquisitionToken, (Guid?)null)
                    .Set(x => x.Status, TickerStatus.Idle)
                    .Set(x => x.UpdatedAt, now),
                cancellationToken: cancellationToken).ConfigureAwait(false);

            var fb = Builders<TTimeTicker>.Filter;
            await coll.UpdateManyAsync(
                fb.And(fb.Eq(x => x.LockHolder, instanceIdentifier), fb.Eq(x => x.Status, TickerStatus.InProgress)),
                Builders<TTimeTicker>.Update
                    .Set(x => x.LockHolder, (string)null)
                    .Set(x => x.LockedAt, (DateTime?)null)
                    .Set(x => x.LeaseUntil, (DateTime?)null)
                .Set(x => x.AcquisitionToken, (Guid?)null)
                    .Set(x => x.Status, TickerStatus.Idle)
                    .Set(x => x.UpdatedAt, now),
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        // ===================================================================
        // Cron Ticker — core methods
        // ===================================================================

        public async Task MigrateDefinedCronTickers(DefinedCronTickerSeed[] cronTickers, CancellationToken cancellationToken = default)
        {
            var now = _clock.UtcNow;
            var cronSet = _context.CronTickers;
            var occSet = _context.CronTickerOccurrences;

            var registeredFunctions = TickerFunctionProvider.TickerFunctions.Keys.ToHashSet(StringComparer.Ordinal);
            var blockedFunctions = cronTickers.Where(x => !x.CanSeed)
                .Select(x => x.Function).ToHashSet(StringComparer.Ordinal);

            // Orphan cleanup is intentionally narrowed to *seeded* crons (those with a non-empty
            // InitIdentifier set by the code-defined-cron migration). Dashboard-created crons
            // targeting SDK / RemoteExecutor functions have InitIdentifier == empty; the SDK may
            // not have synced its qualified `name@node` keys into TickerFunctionProvider yet at
            // boot, so wiping non-seeded crons would destroy user data. Mirrors the EF rationale.
            var fb = Builders<TCronTicker>.Filter;
            var orphans = await cronSet
                .Find(fb.And(
                    fb.Ne(x => x.InitIdentifier, null),
                    fb.Ne(x => x.InitIdentifier, string.Empty)))
                .Project(x => new { x.Id, x.Function })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var orphanIds = orphans
                .Where(o => !registeredFunctions.Contains(o.Function)
                    || blockedFunctions.Contains(o.Function))
                .Select(o => o.Id)
                .ToArray();

            if (orphanIds.Length > 0)
            {
                var occurrenceFilter = Builders<CronTickerOccurrenceEntity<TCronTicker>>.Filter
                    .In(x => x.CronTickerId, orphanIds);
                var occurrenceIds = await occSet.Find(occurrenceFilter)
                    .Project(x => x.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
                var resultFilter = Builders<BsonDocument>.Filter.In(
                    "_id", occurrenceIds.Select(ResultId));

                if (TransactionsKnownUnavailable)
                {
                    await occSet.DeleteManyAsync(occurrenceFilter, cancellationToken).ConfigureAwait(false);
                    if (occurrenceIds.Count > 0)
                        await _context.TickerResults.DeleteManyAsync(
                            resultFilter, cancellationToken).ConfigureAwait(false);
                    await cronSet.DeleteManyAsync(
                        fb.In(x => x.Id, orphanIds), cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    using var session = await _context.Database.Client
                        .StartSessionAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
                    await session.WithTransactionAsync(
                        async (s, ct) =>
                        {
                            await TouchGraphFenceAsync(s, ct).ConfigureAwait(false);
                            await occSet.DeleteManyAsync(
                                s, occurrenceFilter, cancellationToken: ct).ConfigureAwait(false);
                            if (occurrenceIds.Count > 0)
                                await _context.TickerResults.DeleteManyAsync(
                                    s, resultFilter, cancellationToken: ct).ConfigureAwait(false);
                            await cronSet.DeleteManyAsync(
                                s, fb.In(x => x.Id, orphanIds), cancellationToken: ct).ConfigureAwait(false);
                            return true;
                        }, GraphTransactionOptions, cancellationToken).ConfigureAwait(false);
                }
            }

            // Match only SEEDED rows (non-empty InitIdentifier). A user/dashboard-created
            // row that shares a function name carries a null/non-seed identity and must
            // never be matched, expression-overwritten, or identity-stamped by seed
            // reconciliation. Seeds reconcile only their own rows.
            var functions = cronTickers.Where(x => x.CanSeed).Select(x => x.Function).ToArray();
            var existing = await cronSet
                .Find(fb.And(
                    fb.In(x => x.Function, functions),
                    fb.Ne(x => x.InitIdentifier, null),
                    fb.Ne(x => x.InitIdentifier, string.Empty)))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var existingByFunction = existing
                .GroupBy(c => c.Function)
                .ToDictionary(g => g.Key, g => g.First());

            foreach (var seed in cronTickers)
            {
                if (!seed.CanSeed)
                    continue;

                if (existingByFunction.TryGetValue(seed.Function, out var cron))
                {
                    var expressionChanged = !string.Equals(cron.Expression, seed.Expression, StringComparison.Ordinal);

                    // Reconcile authoritative contract identity onto seeded rows only; legacy/dashboard
                    // rows (no seed InitIdentifier) keep their own identity.
                    var identityChanged = !string.IsNullOrEmpty(cron.InitIdentifier)
                        && !seed.MatchesIdentity(cron.RequestContractVersion, cron.RequestContractFingerprint);

                    if (expressionChanged || identityChanged)
                    {
                        var update = Builders<TCronTicker>.Update
                            .Set(x => x.Expression, seed.Expression)
                            .Set(x => x.UpdatedAt, now);

                        if (identityChanged)
                        {
                            update = update
                                .Set(x => x.RequestContractVersion, seed.RequestContractVersion)
                                .Set(x => x.RequestContractFingerprint, seed.RequestContractFingerprint);
                        }

                        await cronSet.UpdateOneAsync(
                            fb.Eq(x => x.Id, cron.Id),
                            update,
                            cancellationToken: cancellationToken).ConfigureAwait(false);
                    }
                }
                else
                {
                    var entity = new TCronTicker
                    {
                        Id = Guid.NewGuid(),
                        Function = seed.Function,
                        Expression = seed.Expression,
                        InitIdentifier = $"MemoryTicker_Seeded_{seed.Function}",
                        CreatedAt = now,
                        UpdatedAt = now,
                        Request = Array.Empty<byte>(),
                        RequestContractVersion = seed.RequestContractVersion,
                        RequestContractFingerprint = seed.RequestContractFingerprint
                    };
                    await cronSet.InsertOneAsync(entity, cancellationToken: cancellationToken).ConfigureAwait(false);
                }
            }
        }

        public async Task<CronTickerEntity[]> GetAllCronTickerExpressions(CancellationToken cancellationToken)
        {
            var fb = Builders<TCronTicker>.Filter;
            var rows = await _context.CronTickers
                .Find(fb.And(fb.Eq(x => x.IsEnabled, true), fb.Eq(x => x.IsSystemPaused, false)))
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
            var now = _clock.UtcNow;
            var mainSchedulerThreshold = now.AddSeconds(-1);
            var fb = Builders<CronTickerOccurrenceEntity<TCronTicker>>.Filter;

            var filter = fb.And(
                fb.In(x => x.CronTickerId, ids),
                fb.Gte(x => x.ExecutionTime, mainSchedulerThreshold),
                MongoUpdateBuilders.CanAcquireCronOccurrence<TCronTicker>(_lockHolder));

            var occurrence = await _context.CronTickerOccurrences
                .Find(filter)
                .Sort(Builders<CronTickerOccurrenceEntity<TCronTicker>>.Sort.Ascending(x => x.ExecutionTime))
                .Limit(1)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            if (occurrence == null) return null;

            occurrence.CronTicker = await _context.CronTickers
                .Find(Builders<TCronTicker>.Filter.Eq(x => x.Id, occurrence.CronTickerId))
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
            return occurrence;
        }

        public async IAsyncEnumerable<CronTickerOccurrenceEntity<TCronTicker>> QueueCronTickerOccurrences(
            (DateTime Key, InternalManagerContext[] Items) cronTickerOccurrences,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var now = _clock.UtcNow;
            var executionTime = cronTickerOccurrences.Key;
            var coll = _context.CronTickerOccurrences;
            var fb = Builders<CronTickerOccurrenceEntity<TCronTicker>>.Filter;

            foreach (var item in cronTickerOccurrences.Items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var acquisitionToken = Guid.NewGuid();

                if (item.NextCronOccurrence is null)
                {
                    // INSERT path. Unique index on (CronTickerId, ExecutionTime) is our dedup — a
                    // duplicate-key error here means another scheduler already claimed this slot,
                    // so skip silently (mirrors EF's Upsert.NoUpdate() returning 0).
                    var toAdd = new CronTickerOccurrenceEntity<TCronTicker>
                    {
                        Id = Guid.NewGuid(),
                        Status = TickerStatus.Queued,
                        LockHolder = _lockHolder,
                        ExecutionTime = executionTime,
                        CronTickerId = item.Id,
                        LockedAt = now,
                        AcquisitionToken = acquisitionToken,
                        CreatedAt = now,
                        UpdatedAt = now
                    };

                    bool inserted;
                    try
                    {
                        await coll.InsertOneAsync(toAdd, cancellationToken: cancellationToken).ConfigureAwait(false);
                        inserted = true;
                    }
                    catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
                    {
                        inserted = false;
                    }

                    if (!inserted) continue;

                    toAdd.CronTicker = new TCronTicker
                    {
                        Id = item.Id,
                        Function = item.FunctionName,
                        RequestContractVersion = item.RequestContractVersion,
                        RequestContractFingerprint = item.RequestContractFingerprint,
                        InitIdentifier = _lockHolder,
                        Expression = item.Expression,
                        Retries = item.Retries,
                        RetryIntervals = item.RetryIntervals,
                        TimeoutSeconds = item.TimeoutSeconds
                    };
                    yield return toAdd;
                }
                else
                {
                    // UPDATE path — claim an existing occurrence row
                    var filter = fb.And(
                        fb.Eq(x => x.Id, item.NextCronOccurrence.Id),
                        fb.Eq(x => x.ExecutionTime, executionTime),
                        MongoUpdateBuilders.CanAcquireCronOccurrence<TCronTicker>(_lockHolder));

                    var update = Builders<CronTickerOccurrenceEntity<TCronTicker>>.Update
                        .Set(x => x.LockHolder, _lockHolder)
                        .Set(x => x.LockedAt, now)
                        .Set(x => x.AcquisitionToken, acquisitionToken)
                        .Set(x => x.UpdatedAt, now)
                        .Set(x => x.Status, TickerStatus.Queued);

                    var result = await coll.UpdateOneAsync(filter, update, cancellationToken: cancellationToken).ConfigureAwait(false);
                    if (result.ModifiedCount <= 0) continue;

                    yield return new CronTickerOccurrenceEntity<TCronTicker>
                    {
                        Id = item.NextCronOccurrence.Id,
                        CronTickerId = item.Id,
                        ExecutionTime = executionTime,
                        Status = TickerStatus.Queued,
                        LockHolder = _lockHolder,
                        LockedAt = now,
                        AcquisitionToken = acquisitionToken,
                        UpdatedAt = now,
                        CreatedAt = item.NextCronOccurrence.CreatedAt,
                        CronTicker = new TCronTicker
                        {
                            Id = item.Id,
                            Function = item.FunctionName,
                            RequestContractVersion = item.RequestContractVersion,
                            RequestContractFingerprint = item.RequestContractFingerprint,
                            InitIdentifier = _lockHolder,
                            Expression = item.Expression,
                            Retries = item.Retries,
                            RetryIntervals = item.RetryIntervals,
                            TimeoutSeconds = item.TimeoutSeconds
                        }
                    };
                }
            }
        }

        public async IAsyncEnumerable<CronTickerOccurrenceEntity<TCronTicker>> QueueTimedOutCronTickerOccurrences(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var now = _clock.UtcNow;
            var fallbackThreshold = now.AddSeconds(-1);
            var coll = _context.CronTickerOccurrences;
            var fb = Builders<CronTickerOccurrenceEntity<TCronTicker>>.Filter;

            var candidatesFilter = fb.And(
                fb.In(x => x.Status, new[] { TickerStatus.Idle, TickerStatus.Queued }),
                fb.Lte(x => x.ExecutionTime, fallbackThreshold));

            var candidates = await coll.Find(candidatesFilter).ToListAsync(cancellationToken).ConfigureAwait(false);
            if (candidates.Count == 0) yield break;

            var cronById = await LoadCronTickers(candidates.Select(c => c.CronTickerId).ToArray(), cancellationToken).ConfigureAwait(false);

            foreach (var occ in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var acquisitionToken = Guid.NewGuid();

                var filter = fb.And(
                    fb.Eq(x => x.Id, occ.Id),
                    fb.Eq(x => x.UpdatedAt, occ.UpdatedAt));

                var update = Builders<CronTickerOccurrenceEntity<TCronTicker>>.Update
                    .Set(x => x.LockHolder, _lockHolder)
                    .Set(x => x.LockedAt, now)
                    .Set(x => x.LeaseUntil, NextLeaseUntil(now))
                    .Set(x => x.AcquisitionToken, acquisitionToken)
                    .Set(x => x.UpdatedAt, now)
                    .Set(x => x.Status, TickerStatus.InProgress);

                var result = await coll.UpdateOneAsync(filter, update, cancellationToken: cancellationToken).ConfigureAwait(false);
                if (result.ModifiedCount <= 0) continue;

                occ.AcquisitionToken = acquisitionToken;
                if (cronById.TryGetValue(occ.CronTickerId, out var cron))
                {
                    occ.CronTicker = new TCronTicker
                    {
                        Id = cron.Id,
                        Function = cron.Function,
                        RequestContractVersion = cron.RequestContractVersion,
                        RequestContractFingerprint = cron.RequestContractFingerprint,
                        RetryIntervals = cron.RetryIntervals,
                        Retries = cron.Retries,
                        TimeoutSeconds = cron.TimeoutSeconds
                    };
                }
                yield return occ;
            }
        }

        public async Task UpdateCronTickerOccurrence(InternalFunctionContext functionContext, CancellationToken cancellationToken = default)
        {
            var now = _clock.UtcNow;
            var update = MongoUpdateBuilders.BuildCronOccurrenceUpdate<TCronTicker>(functionContext, now, NextLeaseUntil(now));
            var filter = Builders<CronTickerOccurrenceEntity<TCronTicker>>.Filter.Eq(x => x.Id, functionContext.TickerId);
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
                ? canAcquire
                : fb.And(fb.In(x => x.Id, occurrenceIds), canAcquire);

            var update = Builders<CronTickerOccurrenceEntity<TCronTicker>>.Update
                .Set(x => x.LockHolder, (string)null)
                .Set(x => x.LockedAt, (DateTime?)null)
                .Set(x => x.LeaseUntil, (DateTime?)null)
                .Set(x => x.AcquisitionToken, (Guid?)null)
                .Set(x => x.Status, TickerStatus.Idle)
                .Set(x => x.UpdatedAt, now);

            await coll.UpdateManyAsync(filter, update, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        public async Task<byte[]> GetCronTickerOccurrenceRequest(Guid tickerId, CancellationToken cancellationToken = default)
        {
            var occ = await _context.CronTickerOccurrences
                .Find(Builders<CronTickerOccurrenceEntity<TCronTicker>>.Filter.Eq(x => x.Id, tickerId))
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
            if (occ == null) return null;

            var cron = await _context.CronTickers
                .Find(Builders<TCronTicker>.Filter.Eq(x => x.Id, occ.CronTickerId))
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
                    Builders<CronTickerOccurrenceEntity<TCronTicker>>.Filter.In(x => x.Id, cronOccurrenceIds),
                    update,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task<Guid[]> TransitionQueuedCronOccurrencesToInProgressAsync(
            IReadOnlyCollection<AcquisitionLease> leases, CancellationToken cancellationToken = default)
        {
            var now = _clock.UtcNow;
            var fb = Builders<CronTickerOccurrenceEntity<TCronTicker>>.Filter;
            var update = Builders<CronTickerOccurrenceEntity<TCronTicker>>.Update
                .Set(x => x.Status, TickerStatus.InProgress)
                .Set(x => x.LeaseUntil, NextLeaseUntil(now))
                .Set(x => x.UpdatedAt, now);
            var winners = new List<Guid>(leases.Count);
            foreach (var lease in leases.Where(x => x.AcquisitionToken.HasValue).Distinct())
            {
                var filter = fb.And(
                    fb.Eq(x => x.Id, lease.TickerId),
                    fb.Eq(x => x.Status, TickerStatus.Queued),
                    fb.Eq(x => x.LockHolder, _lockHolder),
                    fb.Eq(x => x.AcquisitionToken, lease.AcquisitionToken));
                var result = await _context.CronTickerOccurrences.UpdateOneAsync(
                    filter, update, cancellationToken: cancellationToken).ConfigureAwait(false);
                if (result.ModifiedCount == 1) winners.Add(lease.TickerId);
            }
            return winners.ToArray();
        }

        public async Task ReleaseDeadNodeOccurrenceResources(string instanceIdentifier, CancellationToken cancellationToken = default)
        {
            var now = _clock.UtcNow;
            var coll = _context.CronTickerOccurrences;

            await coll.UpdateManyAsync(
                MongoUpdateBuilders.CanAcquireCronOccurrence<TCronTicker>(instanceIdentifier),
                Builders<CronTickerOccurrenceEntity<TCronTicker>>.Update
                    .Set(x => x.LockHolder, (string)null)
                    .Set(x => x.LockedAt, (DateTime?)null)
                    .Set(x => x.LeaseUntil, (DateTime?)null)
                .Set(x => x.AcquisitionToken, (Guid?)null)
                    .Set(x => x.Status, TickerStatus.Idle)
                    .Set(x => x.UpdatedAt, now),
                cancellationToken: cancellationToken).ConfigureAwait(false);

            var fb = Builders<CronTickerOccurrenceEntity<TCronTicker>>.Filter;
            await coll.UpdateManyAsync(
                fb.And(fb.Eq(x => x.LockHolder, instanceIdentifier), fb.Eq(x => x.Status, TickerStatus.InProgress)),
                Builders<CronTickerOccurrenceEntity<TCronTicker>>.Update
                    .Set(x => x.LockHolder, (string)null)
                    .Set(x => x.LockedAt, (DateTime?)null)
                    .Set(x => x.LeaseUntil, (DateTime?)null)
                .Set(x => x.AcquisitionToken, (Guid?)null)
                    .Set(x => x.Status, TickerStatus.Idle)
                    .Set(x => x.UpdatedAt, now),
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        public async Task<int> SkipStaleCronOccurrencesAsync(TimeSpan staleThreshold, CancellationToken cancellationToken = default)
        {
            if (staleThreshold <= TimeSpan.Zero) return 0;
            var now = _clock.UtcNow;
            var cutoff = now - staleThreshold;
            var fb = Builders<CronTickerOccurrenceEntity<TCronTicker>>.Filter;

            var filter = fb.And(
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
                timeQueuedFilters.Eq(x => x.ParentId, (Guid?)null),
                timeQueuedFilters.In(x => x.Status, new[] { TickerStatus.Idle, TickerStatus.Queued }),
                timeQueuedFilters.Ne(x => x.LockHolder, null),
                timeQueuedFilters.Ne(x => x.LockedAt, null),
                timeQueuedFilters.Lt(x => x.LockedAt, staleLockCutoff));
            await _context.TimeTickers.UpdateManyAsync(
                staleQueuedTime,
                Builders<TTimeTicker>.Update
                    .Set(x => x.Status, TickerStatus.Idle)
                    .Set(x => x.LockHolder, (string)null)
                    .Set(x => x.LockedAt, (DateTime?)null)
                    .Set(x => x.LeaseUntil, (DateTime?)null)
                    .Set(x => x.AcquisitionToken, (Guid?)null)
                    .Set(x => x.UpdatedAt, now),
                cancellationToken: cancellationToken).ConfigureAwait(false);

            var cronQueuedFilters = Builders<CronTickerOccurrenceEntity<TCronTicker>>.Filter;
            var staleQueuedCron = cronQueuedFilters.And(
                cronQueuedFilters.In(x => x.Status, new[] { TickerStatus.Idle, TickerStatus.Queued }),
                cronQueuedFilters.Ne(x => x.LockHolder, null),
                cronQueuedFilters.Ne(x => x.LockedAt, null),
                cronQueuedFilters.Lt(x => x.LockedAt, staleLockCutoff));
            await _context.CronTickerOccurrences.UpdateManyAsync(
                staleQueuedCron,
                Builders<CronTickerOccurrenceEntity<TCronTicker>>.Update
                    .Set(x => x.Status, TickerStatus.Idle)
                    .Set(x => x.LockHolder, (string)null)
                    .Set(x => x.LockedAt, (DateTime?)null)
                    .Set(x => x.LeaseUntil, (DateTime?)null)
                    .Set(x => x.AcquisitionToken, (Guid?)null)
                    .Set(x => x.UpdatedAt, now),
                cancellationToken: cancellationToken).ConfigureAwait(false);

            var timeFilters = Builders<TTimeTicker>.Filter;
            var staleTime = timeFilters.And(
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
                        Builders<BsonDocument>.Filter.In("_id", restartIds.Select(ResultId)),
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
                        await TouchGraphFenceAsync(s, ct).ConfigureAwait(false);
                        var restartIds = await _context.TimeTickers.Find(s, restartTime)
                            .Project(x => x.Id).ToListAsync(ct).ConfigureAwait(false);
                        var restarted = await _context.TimeTickers.UpdateManyAsync(
                            s, restartTime, restartTimeUpdate,
                            cancellationToken: ct).ConfigureAwait(false);
                        if (restartIds.Count > 0)
                            await _context.TickerResults.DeleteManyAsync(
                                s,
                                Builders<BsonDocument>.Filter.In("_id", restartIds.Select(ResultId)),
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
                    .Find(Builders<TCronTicker>.Filter.Eq(x => x.Id, staleRow.CronTickerId))
                    .Project(x => new { x.OnStale })
                    .FirstOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (parent?.OnStale != StaleAction.Restart) continue;

                var restartOccurrenceFilter = occurrenceFilters.And(
                    staleOccurrences,
                    occurrenceFilters.Eq(x => x.Id, staleRow.Id),
                    occurrenceFilters.Eq(x => x.LeaseUntil, staleRow.LeaseUntil),
                    occurrenceFilters.Eq(x => x.LockHolder, staleRow.LockHolder),
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
                            Builders<BsonDocument>.Filter.Eq("_id", ResultId(staleRow.Id)),
                            cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    using var restartSession = await _context.Database.Client
                        .StartSessionAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
                    restartedCount = await restartSession.WithTransactionAsync(
                        async (s, ct) =>
                        {
                            await TouchGraphFenceAsync(s, ct).ConfigureAwait(false);
                            var restarted = await _context.CronTickerOccurrences.UpdateOneAsync(
                                s, restartOccurrenceFilter, restartOccurrenceUpdate,
                                cancellationToken: ct).ConfigureAwait(false);
                            if (restarted.ModifiedCount == 1)
                                await _context.TickerResults.DeleteOneAsync(
                                    s,
                                    Builders<BsonDocument>.Filter.Eq("_id", ResultId(staleRow.Id)),
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
            var expiredClaim = fb.And(
                fb.Eq(x => x.LockHolder, RetentionLockHolder),
                fb.In(x => x.Status, TerminalStatuses),
                fb.Ne(x => x.AcquisitionToken, (Guid?)null),
                fb.Lte(x => x.LeaseUntil, now));
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
            var eligible = TimeRetentionEligible(cutoffs, now);
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
                var subtreeFilter = fb.In(x => x.Id, chainIds);

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
                    fb.And(fb.Eq(x => x.Id, rootId), fb.Eq(x => x.ParentId, (Guid?)null), eligible),
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
                    Builders<BsonDocument>.Filter.In("_id", chainIds.Select(ResultId)),
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
                    .Find(session, fb.In(x => x.ParentId, frontier.Select(p => (Guid?)p)))
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
            var eligible = OccurrenceRetentionEligible(cutoffs, now);
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
            var resultFilter = Builders<BsonDocument>.Filter.In("_id", candidates.Select(ResultId));
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
                .Find(Builders<TTimeTicker>.Filter.Eq(x => x.Id, id))
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
            if (row == null) return null;
            row.Children = await LoadChildrenRecursive<TTimeTicker>(row.Id, cancellationToken).ConfigureAwait(false);
            return row;
        }

        public async Task<TTimeTicker[]> GetTimeTickers(Expression<Func<TTimeTicker, bool>> predicate, CancellationToken cancellationToken = default)
        {
            var fb = Builders<TTimeTicker>.Filter;
            var filter = fb.And(fb.Eq(x => x.ParentId, (Guid?)null), predicate is null ? fb.Empty : fb.Where(predicate));
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
            var filter = fb.And(fb.Eq(x => x.ParentId, (Guid?)null), predicate is null ? fb.Empty : fb.Where(predicate));
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
            return await ExecuteGraphMutationAsync(
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

                var filter = Builders<TTimeTicker>.Filter.Eq(x => x.Id, current.ParentId.Value);
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
                    .Find(Builders<TTimeTicker>.Filter.In(x => x.Id, topLevelIds))
                    .Project(x => x.Id)
                    .ToListAsync(cancellationToken).ConfigureAwait(false))
                .ToHashSet();
            return await ExecuteGraphMutationAsync(
                async (session, ct) =>
                {
                    if (!await ReferencedParentsExistAsync(session, tickers, ct).ConfigureAwait(false))
                        return 0;
                    if (requiredExistingIds.Count > 0)
                    {
                        var stillExisting = await _context.TimeTickers.CountDocumentsAsync(
                            session,
                            Builders<TTimeTicker>.Filter.In(x => x.Id, requiredExistingIds),
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
            if (parentId.HasValue) ticker.ParentId = parentId.Value;
            ticker.ChainRootId = chainRootId;
            ticker.ChainGeneration = chainGeneration;
            var filter = Builders<TTimeTicker>.Filter.Eq(x => x.Id, ticker.Id);
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
                Builders<TTimeTicker>.Filter.In(x => x.Id, referencedParents),
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

            return await ExecuteGraphMutationAsync(
                async (session, ct) =>
                {
                    var oldExists = await _context.TimeTickers.Find(
                            session, Builders<TTimeTicker>.Filter.And(
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
                        Builders<TTimeTicker>.Filter.In(x => x.Id, oldIds),
                        cancellationToken: ct).ConfigureAwait(false);
                    if (deleted.DeletedCount != oldIds.Count)
                        throw new InvalidOperationException("The original chain changed during replacement.");
                    await _context.TickerResults.DeleteManyAsync(
                        session,
                        Builders<BsonDocument>.Filter.In("_id", oldIds.Select(ResultId)),
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
            return await ExecuteGraphMutationAsync(
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
                var filter = Builders<TTimeTicker>.Filter.In(x => x.Id, allIds);
                DeleteResult result;
                if (session == null)
                    result = await _context.TimeTickers.DeleteManyAsync(filter, cancellationToken).ConfigureAwait(false);
                else
                    result = await _context.TimeTickers.DeleteManyAsync(
                        session, filter, cancellationToken: cancellationToken).ConfigureAwait(false);
                var resultFilter = Builders<BsonDocument>.Filter.In("_id", allIds.Select(ResultId));
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
                .Find(Builders<TCronTicker>.Filter.Eq(x => x.Id, id))
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

        public async Task<TCronTicker[]> GetCronTickers(Expression<Func<TCronTicker, bool>> predicate, CancellationToken cancellationToken)
        {
            var filter = predicate is null ? Builders<TCronTicker>.Filter.Empty : Builders<TCronTicker>.Filter.Where(predicate);
            var rows = await _context.CronTickers
                .Find(filter)
                .Sort(Builders<TCronTicker>.Sort.Descending(x => x.CreatedAt))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            return rows.ToArray();
        }

        public async Task<PaginationResult<TCronTicker>> GetCronTickersPaginated(Expression<Func<TCronTicker, bool>> predicate, int pageNumber, int pageSize, CancellationToken cancellationToken = default)
        {
            var filter = predicate is null ? Builders<TCronTicker>.Filter.Empty : Builders<TCronTicker>.Filter.Where(predicate);
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
            await _context.CronTickers.InsertManyAsync(tickers, cancellationToken: cancellationToken).ConfigureAwait(false);
            return tickers.Length;
        }

        public async Task<int> UpdateCronTickers(TCronTicker[] cronTicker, CancellationToken cancellationToken)
        {
            var count = 0;
            foreach (var t in cronTicker)
            {
                var result = await _context.CronTickers.ReplaceOneAsync(
                    Builders<TCronTicker>.Filter.Eq(x => x.Id, t.Id),
                    t,
                    new ReplaceOptions { IsUpsert = false },
                    cancellationToken).ConfigureAwait(false);
                count += (int)result.ModifiedCount;
            }
            return count;
        }

        public async Task<int> RemoveCronTickers(Guid[] cronTickerIds, CancellationToken cancellationToken)
        {
            var result = await _context.CronTickers.DeleteManyAsync(
                Builders<TCronTicker>.Filter.In(x => x.Id, cronTickerIds),
                cancellationToken).ConfigureAwait(false);
            return (int)result.DeletedCount;
        }

        public async Task<CronTickerOccurrenceEntity<TCronTicker>[]> GetAllCronTickerOccurrences(Expression<Func<CronTickerOccurrenceEntity<TCronTicker>, bool>> predicate, CancellationToken cancellationToken = default)
        {
            var filter = predicate is null
                ? Builders<CronTickerOccurrenceEntity<TCronTicker>>.Filter.Empty
                : Builders<CronTickerOccurrenceEntity<TCronTicker>>.Filter.Where(predicate);
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
            var count = 0;
            foreach (var occ in cronTickerOccurrences)
            {
                try
                {
                    await _context.CronTickerOccurrences.InsertOneAsync(occ, cancellationToken: cancellationToken).ConfigureAwait(false);
                    count++;
                }
                catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
                {
                    // unique-index violation: another writer claimed the same (CronTickerId, ExecutionTime) slot
                }
            }
            return count;
        }

        public async Task<int> RemoveCronTickerOccurrences(Guid[] cronTickerOccurrences, CancellationToken cancellationToken)
        {
            if (cronTickerOccurrences == null || cronTickerOccurrences.Length == 0) return 0;
            var ids = cronTickerOccurrences.Distinct().ToArray();
            var rowFilter = Builders<CronTickerOccurrenceEntity<TCronTicker>>.Filter.In(x => x.Id, ids);
            var resultFilter = Builders<BsonDocument>.Filter.In("_id", ids.Select(ResultId));

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
                var filter = fb.And(
                    fb.Eq(x => x.Id, id),
                    MongoUpdateBuilders.CanAcquireCronOccurrence<TCronTicker>(_lockHolder));
                var acquired = await coll.FindOneAndUpdateAsync(filter, update, options, cancellationToken)
                    .ConfigureAwait(false);
                if (acquired != null) rows.Add(acquired);
            }

            if (rows.Count == 0) return Array.Empty<CronTickerOccurrenceEntity<TCronTicker>>();
            var cronById = await LoadCronTickers(rows.Select(x => x.CronTickerId).Distinct().ToArray(), cancellationToken)
                .ConfigureAwait(false);
            foreach (var row in rows)
                if (cronById.TryGetValue(row.CronTickerId, out var cron)) row.CronTicker = cron;
            return rows.ToArray();
        }

        // ===================================================================
        // Queryable hooks — implemented in MongoTickerQueryable (Task 6)
        // ===================================================================

        public ITickerQueryable<TTimeTicker> TimeTickersQuery()
            => new MongoTickerQueryable<TTimeTicker>(
                _context.TimeTickers,
                async (entity, relations, ct) =>
                {
                    if (relations.Any(r => r == TickerRelation.Children || r == TickerRelation.ChildrenDeep))
                        entity.Children = await LoadChildrenRecursive<TTimeTicker>(entity.Id, ct).ConfigureAwait(false);
                });

        public ITickerQueryable<TCronTicker> CronTickersQuery()
            => new MongoTickerQueryable<TCronTicker>(_context.CronTickers, null);

        public ITickerQueryable<CronTickerOccurrenceEntity<TCronTicker>> CronTickerOccurrencesQuery()
            => new MongoTickerQueryable<CronTickerOccurrenceEntity<TCronTicker>>(
                _context.CronTickerOccurrences,
                async (occ, relations, ct) =>
                {
                    if (relations.Any(r => r == TickerRelation.CronTicker))
                    {
                        occ.CronTicker = await _context.CronTickers
                            .Find(Builders<TCronTicker>.Filter.Eq(x => x.Id, occ.CronTickerId))
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
                    .Find(fb.And(
                        fb.In(x => x.ParentId, frontier.Select(p => (Guid?)p)),
                        fb.Eq(x => x.ExecutionTime, null)))
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

        private async Task<Dictionary<Guid, TCronTicker>> LoadCronTickers(Guid[] ids, CancellationToken ct)
        {
            if (ids.Length == 0) return new Dictionary<Guid, TCronTicker>();
            var rows = await _context.CronTickers
                .Find(Builders<TCronTicker>.Filter.In(x => x.Id, ids))
                .ToListAsync(ct)
                .ConfigureAwait(false);
            return rows.ToDictionary(r => r.Id);
        }

        private async Task<List<T>> LoadChildrenRecursive<T>(Guid parentId, CancellationToken ct) where T : TTimeTicker
        {
            var rows = await _context.TimeTickers
                .Find(Builders<TTimeTicker>.Filter.Eq(x => x.ParentId, (Guid?)parentId))
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
                var filter = Builders<TTimeTicker>.Filter.Eq(x => x.ParentId, (Guid?)id);
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
