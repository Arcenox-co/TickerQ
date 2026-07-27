using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using MongoDB.Driver;
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

        // ===================================================================
        // Time Ticker — core scheduler methods
        // ===================================================================

        public async IAsyncEnumerable<TimeTickerEntity> QueueTimeTickers(
            TimeTickerEntity[] timeTickers,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var now = _clock.UtcNow;
            var coll = _context.TimeTickers;
            var fb = Builders<TTimeTicker>.Filter;

            foreach (var ticker in timeTickers)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var acquisitionToken = Guid.NewGuid();

                // Fence the queue CAS on both the observed timestamp and generation. A clock can
                // legitimately return the same value for two writes (or Mongo can truncate it),
                // so UpdatedAt alone permits the same stale snapshot to acquire twice.
                var filter = fb.And(
                    fb.Eq(x => x.Id, ticker.Id),
                    fb.Eq(x => x.UpdatedAt, ticker.UpdatedAt),
                    fb.Eq(x => x.AcquisitionToken, ticker.AcquisitionToken));
                var update = Builders<TTimeTicker>.Update
                    .Set(x => x.LockHolder, _lockHolder)
                    .Set(x => x.LockedAt, now)
                    .Set(x => x.AcquisitionToken, acquisitionToken)
                    .Set(x => x.UpdatedAt, now)
                    .Set(x => x.Status, TickerStatus.Queued);

                var result = await coll.UpdateOneAsync(filter, update, cancellationToken: cancellationToken).ConfigureAwait(false);
                if (result.ModifiedCount <= 0)
                    continue;

                ticker.UpdatedAt = now;
                ticker.LockHolder = _lockHolder;
                ticker.LockedAt = now;
                ticker.AcquisitionToken = acquisitionToken;
                ticker.Status = TickerStatus.Queued;
                yield return ticker;
            }
        }

        public async IAsyncEnumerable<TimeTickerEntity> QueueTimedOutTimeTickers(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var now = _clock.UtcNow;
            var fallbackThreshold = now.AddSeconds(-1);
            var coll = _context.TimeTickers;
            var fb = Builders<TTimeTicker>.Filter;

            var candidatesFilter = fb.And(
                fb.Ne(x => x.ExecutionTime, null),
                fb.In(x => x.Status, new[] { TickerStatus.Idle, TickerStatus.Queued }),
                fb.Lte(x => x.ExecutionTime, fallbackThreshold));

            var candidates = await coll.Find(candidatesFilter).ToListAsync(cancellationToken).ConfigureAwait(false);
            var byParent = await LoadChildrenLookup(candidates.Select(c => c.Id).ToArray(), cancellationToken).ConfigureAwait(false);

            foreach (var candidate in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var acquisitionToken = Guid.NewGuid();

                var filter = fb.And(
                    fb.Eq(x => x.Id, candidate.Id),
                    fb.Lte(x => x.UpdatedAt, candidate.UpdatedAt));

                var update = Builders<TTimeTicker>.Update
                    .Set(x => x.LockHolder, _lockHolder)
                    .Set(x => x.LockedAt, now)
                    .Set(x => x.LeaseUntil, NextLeaseUntil(now))
                    .Set(x => x.AcquisitionToken, acquisitionToken)
                    .Set(x => x.UpdatedAt, now)
                    .Set(x => x.Status, TickerStatus.InProgress);

                var result = await coll.UpdateOneAsync(filter, update, cancellationToken: cancellationToken).ConfigureAwait(false);
                if (result.ModifiedCount <= 0)
                    continue;

                candidate.AcquisitionToken = acquisitionToken;
                yield return BuildQueuedEntity(candidate, byParent);
            }
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
            var filter = Builders<TTimeTicker>.Filter.Eq(x => x.Id, functionContext.TickerId);
            if (IsFencedTerminalWrite(functionContext) && functionContext.ParentId == null)
            {
                var fb = Builders<TTimeTicker>.Filter;
                filter &= functionContext.AcquisitionToken.HasValue
                    ? fb.And(fb.Eq(x => x.LockHolder, _lockHolder),
                             fb.Eq(x => x.AcquisitionToken, functionContext.AcquisitionToken))
                    : fb.Where(_ => false);
            }

            var result = await _context.TimeTickers
                .UpdateOneAsync(
                    filter,
                    update,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return (int)result.ModifiedCount;
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
                    Builders<TTimeTicker>.Filter.In(x => x.Id, timeTickerIds),
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

            var now = _clock.UtcNow;
            var acquisitionToken = Guid.NewGuid();
            var coll = _context.TimeTickers;
            var fb = Builders<TTimeTicker>.Filter;
            var update = Builders<TTimeTicker>.Update
                .Set(x => x.LockHolder, _lockHolder)
                .Set(x => x.LockedAt, now)
                .Set(x => x.LeaseUntil, NextLeaseUntil(now))
                .Set(x => x.AcquisitionToken, acquisitionToken)
                .Set(x => x.Status, TickerStatus.InProgress)
                .Set(x => x.UpdatedAt, now);
            var options = new FindOneAndUpdateOptions<TTimeTicker> { ReturnDocument = ReturnDocument.After };
            var rows = new List<TTimeTicker>(ids.Length);

            foreach (var id in ids.Distinct())
            {
                var filter = fb.And(
                    fb.Eq(x => x.Id, id),
                    MongoUpdateBuilders.CanAcquireTimeTicker<TTimeTicker>(_lockHolder));
                var acquired = await coll.FindOneAndUpdateAsync(filter, update, options, cancellationToken)
                    .ConfigureAwait(false);
                if (acquired != null) rows.Add(acquired);
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
            var eligible = fb.Or(
                fb.Eq(x => x.Status, TickerStatus.Idle),
                fb.And(fb.Eq(x => x.Status, TickerStatus.Queued),
                    fb.Or(fb.Eq(x => x.LockHolder, null), fb.Eq(x => x.LockHolder, _lockHolder))),
                fb.In(x => x.Status, new[]
                {
                    TickerStatus.Done, TickerStatus.DueDone, TickerStatus.Failed,
                    TickerStatus.Cancelled, TickerStatus.Skipped
                }));
            var filter = fb.And(fb.Eq(x => x.Id, id), eligible);
            var update = Builders<TTimeTicker>.Update
                .Set(x => x.ExecutionTime, executionTime)
                .Set(x => x.Status, TickerStatus.InProgress)
                .Set(x => x.LockHolder, _lockHolder)
                .Set(x => x.LockedAt, now)
                .Set(x => x.LeaseUntil, NextLeaseUntil(now))
                .Set(x => x.AcquisitionToken, token)
                .Set(x => x.RetryCount, 0)
                .Set(x => x.ExceptionMessage, (string)null)
                .Set(x => x.SkippedReason, (string)null)
                .Set(x => x.ExecutedAt, (DateTime?)null)
                .Set(x => x.ElapsedTime, 0L)
                .Set(x => x.StaleRestartCount, 0)
                .Set(x => x.UpdatedAt, now);
            var row = await _context.TimeTickers.FindOneAndUpdateAsync(
                filter, update,
                new FindOneAndUpdateOptions<TTimeTicker> { ReturnDocument = ReturnDocument.After },
                cancellationToken).ConfigureAwait(false);
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
                await occSet.DeleteManyAsync(
                    Builders<CronTickerOccurrenceEntity<TCronTicker>>.Filter.In(x => x.CronTickerId, orphanIds),
                    cancellationToken).ConfigureAwait(false);
                await cronSet.DeleteManyAsync(fb.In(x => x.Id, orphanIds), cancellationToken).ConfigureAwait(false);
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

            await _context.CronTickerOccurrences
                .UpdateOneAsync(
                    filter,
                    update,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
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
                timeFilters.Eq(x => x.Status, TickerStatus.InProgress),
                timeFilters.Ne(x => x.LeaseUntil, null),
                timeFilters.Lt(x => x.LeaseUntil, now));
            var restartTime = timeFilters.And(
                staleTime,
                timeFilters.Eq(x => x.OnStale, StaleAction.Restart),
                timeFilters.Lt(x => x.StaleRestartCount, maxStaleRestarts));

            var restartTimeResult = await _context.TimeTickers.UpdateManyAsync(
                restartTime,
                Builders<TTimeTicker>.Update
                    .Set(x => x.Status, TickerStatus.Idle)
                    .Set(x => x.LockHolder, (string)null)
                    .Set(x => x.LockedAt, (DateTime?)null)
                    .Set(x => x.LeaseUntil, (DateTime?)null)
                .Set(x => x.AcquisitionToken, (Guid?)null)
                    .Inc(x => x.StaleRestartCount, 1)
                    .Set(x => x.UpdatedAt, now),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            result.RestartedTimeTickers = (int)restartTimeResult.ModifiedCount;

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
                var restartOccurrenceResult = await _context.CronTickerOccurrences.UpdateOneAsync(
                    restartOccurrenceFilter,
                    Builders<CronTickerOccurrenceEntity<TCronTicker>>.Update
                        .Set(x => x.Status, TickerStatus.Idle)
                        .Set(x => x.LockHolder, (string)null)
                        .Set(x => x.LockedAt, (DateTime?)null)
                        .Set(x => x.LeaseUntil, (DateTime?)null)
                .Set(x => x.AcquisitionToken, (Guid?)null)
                        .Inc(x => x.StaleRestartCount, 1)
                        .Set(x => x.UpdatedAt, now),
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                result.RestartedCronOccurrences += (int)restartOccurrenceResult.ModifiedCount;
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
            var count = 0;
            foreach (var t in tickers) count += await InsertWithChildren(t, null, cancellationToken).ConfigureAwait(false);
            return count;
        }

        private async Task<int> InsertWithChildren(TTimeTicker ticker, Guid? parentId, CancellationToken ct)
        {
            if (parentId.HasValue) ticker.ParentId = parentId.Value;
            await _context.TimeTickers.InsertOneAsync(ticker, cancellationToken: ct).ConfigureAwait(false);
            var count = 1;
            if (ticker.Children != null)
            {
                foreach (var child in ticker.Children)
                    if (child is TTimeTicker c) count += await InsertWithChildren(c, ticker.Id, ct).ConfigureAwait(false);
            }
            return count;
        }

        public async Task<int> UpdateTimeTickers(TTimeTicker[] tickers, CancellationToken cancellationToken = default)
        {
            var count = 0;
            foreach (var t in tickers) count += await ReplaceWithChildren(t, null, cancellationToken).ConfigureAwait(false);
            return count;
        }

        private async Task<int> ReplaceWithChildren(TTimeTicker ticker, Guid? parentId, CancellationToken ct)
        {
            if (parentId.HasValue) ticker.ParentId = parentId.Value;
            var result = await _context.TimeTickers.ReplaceOneAsync(
                Builders<TTimeTicker>.Filter.Eq(x => x.Id, ticker.Id),
                ticker,
                new ReplaceOptions { IsUpsert = true },
                ct).ConfigureAwait(false);
            var count = (int)result.ModifiedCount + (result.UpsertedId is null ? 0 : 1);
            if (ticker.Children != null)
            {
                foreach (var child in ticker.Children)
                    if (child is TTimeTicker c) count += await ReplaceWithChildren(c, ticker.Id, ct).ConfigureAwait(false);
            }
            return count;
        }

        public async Task<int> RemoveTimeTickers(Guid[] tickerIds, CancellationToken cancellationToken = default)
        {
            var count = 0;
            foreach (var id in tickerIds)
            {
                var allIds = await CollectDescendantIds(id, cancellationToken).ConfigureAwait(false);
                allIds.Add(id);
                var result = await _context.TimeTickers.DeleteManyAsync(
                    Builders<TTimeTicker>.Filter.In(x => x.Id, allIds),
                    cancellationToken).ConfigureAwait(false);
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
            var result = await _context.CronTickerOccurrences.DeleteManyAsync(
                Builders<CronTickerOccurrenceEntity<TCronTicker>>.Filter.In(x => x.Id, cronTickerOccurrences),
                cancellationToken).ConfigureAwait(false);
            return (int)result.DeletedCount;
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

        private async Task<HashSet<Guid>> CollectDescendantIds(Guid parentId, CancellationToken ct)
        {
            var collected = new HashSet<Guid>();
            var frontier = new Queue<Guid>();
            frontier.Enqueue(parentId);
            while (frontier.Count > 0)
            {
                var id = frontier.Dequeue();
                var childIds = await _context.TimeTickers
                    .Find(Builders<TTimeTicker>.Filter.Eq(x => x.ParentId, (Guid?)id))
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
