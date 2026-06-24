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

        private static readonly Func<TTimeTicker, TimeTickerEntity> ProjectTimeTicker
            = MappingExtensions.ForQueueTimeTickers<TTimeTicker>().Compile();

        public TickerMongoPersistenceProvider(
            ITickerMongoContext<TTimeTicker, TCronTicker> context,
            ITickerClock clock,
            SchedulerOptionsBuilder optionsBuilder)
        {
            _context = context;
            _clock = clock;
            _lockHolder = optionsBuilder.NodeIdentifier;
        }

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

                var filter = fb.And(fb.Eq(x => x.Id, ticker.Id), fb.Eq(x => x.UpdatedAt, ticker.UpdatedAt));
                var update = Builders<TTimeTicker>.Update
                    .Set(x => x.LockHolder, _lockHolder)
                    .Set(x => x.LockedAt, now)
                    .Set(x => x.UpdatedAt, now)
                    .Set(x => x.Status, TickerStatus.Queued);

                var result = await coll.UpdateOneAsync(filter, update, cancellationToken: cancellationToken).ConfigureAwait(false);
                if (result.ModifiedCount <= 0)
                    continue;

                ticker.UpdatedAt = now;
                ticker.LockHolder = _lockHolder;
                ticker.LockedAt = now;
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

                var filter = fb.And(
                    fb.Eq(x => x.Id, candidate.Id),
                    fb.Lte(x => x.UpdatedAt, candidate.UpdatedAt));

                var update = Builders<TTimeTicker>.Update
                    .Set(x => x.LockHolder, _lockHolder)
                    .Set(x => x.LockedAt, now)
                    .Set(x => x.UpdatedAt, now)
                    .Set(x => x.Status, TickerStatus.InProgress);

                var result = await coll.UpdateOneAsync(filter, update, cancellationToken: cancellationToken).ConfigureAwait(false);
                if (result.ModifiedCount <= 0)
                    continue;

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
            var update = MongoUpdateBuilders.BuildTimeTickerUpdate<TTimeTicker>(functionContext, _clock.UtcNow);
            var result = await _context.TimeTickers
                .UpdateOneAsync(
                    Builders<TTimeTicker>.Filter.Eq(x => x.Id, functionContext.TickerId),
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
            var update = MongoUpdateBuilders.BuildTimeTickerUpdate<TTimeTicker>(functionContext, _clock.UtcNow);
            await _context.TimeTickers
                .UpdateManyAsync(
                    Builders<TTimeTicker>.Filter.In(x => x.Id, timeTickerIds),
                    update,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task<TimeTickerEntity[]> AcquireImmediateTimeTickersAsync(Guid[] ids, CancellationToken cancellationToken = default)
        {
            if (ids == null || ids.Length == 0) return Array.Empty<TimeTickerEntity>();

            var now = _clock.UtcNow;
            var coll = _context.TimeTickers;
            var fb = Builders<TTimeTicker>.Filter;

            var filter = fb.And(
                fb.In(x => x.Id, ids),
                MongoUpdateBuilders.CanAcquireTimeTicker<TTimeTicker>(_lockHolder));

            var update = Builders<TTimeTicker>.Update
                .Set(x => x.LockHolder, _lockHolder)
                .Set(x => x.LockedAt, now)
                .Set(x => x.Status, TickerStatus.InProgress)
                .Set(x => x.UpdatedAt, now);

            var result = await coll.UpdateManyAsync(filter, update, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (result.ModifiedCount == 0) return Array.Empty<TimeTickerEntity>();

            var acquiredFilter = fb.And(
                fb.In(x => x.Id, ids),
                fb.Eq(x => x.LockHolder, _lockHolder),
                fb.Eq(x => x.Status, TickerStatus.InProgress));

            var rows = await coll.Find(acquiredFilter).ToListAsync(cancellationToken).ConfigureAwait(false);
            var byParent = await LoadChildrenLookup(rows.Select(r => r.Id).ToArray(), cancellationToken).ConfigureAwait(false);
            return rows.Select(r => BuildQueuedEntity(r, byParent)).ToArray();
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
                    .Set(x => x.Status, TickerStatus.Idle)
                    .Set(x => x.UpdatedAt, now),
                cancellationToken: cancellationToken).ConfigureAwait(false);

            var fb = Builders<TTimeTicker>.Filter;
            await coll.UpdateManyAsync(
                fb.And(fb.Eq(x => x.LockHolder, instanceIdentifier), fb.Eq(x => x.Status, TickerStatus.InProgress)),
                Builders<TTimeTicker>.Update
                    .Set(x => x.LockHolder, (string)null)
                    .Set(x => x.LockedAt, (DateTime?)null)
                    .Set(x => x.Status, TickerStatus.Idle)
                    .Set(x => x.UpdatedAt, now),
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        // ===================================================================
        // Cron Ticker — core methods
        // ===================================================================

        public async Task MigrateDefinedCronTickers((string Function, string Expression)[] cronTickers, CancellationToken cancellationToken = default)
        {
            var now = _clock.UtcNow;
            var cronSet = _context.CronTickers;
            var occSet = _context.CronTickerOccurrences;

            var registeredFunctions = TickerFunctionProvider.TickerFunctions.Keys.ToHashSet(StringComparer.Ordinal);

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
                .Where(o => !registeredFunctions.Contains(o.Function))
                .Select(o => o.Id)
                .ToArray();

            if (orphanIds.Length > 0)
            {
                await occSet.DeleteManyAsync(
                    Builders<CronTickerOccurrenceEntity<TCronTicker>>.Filter.In(x => x.CronTickerId, orphanIds),
                    cancellationToken).ConfigureAwait(false);
                await cronSet.DeleteManyAsync(fb.In(x => x.Id, orphanIds), cancellationToken).ConfigureAwait(false);
            }

            var functions = cronTickers.Select(x => x.Function).ToArray();
            var existing = await cronSet
                .Find(fb.In(x => x.Function, functions))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var existingByFunction = existing
                .GroupBy(c => c.Function)
                .ToDictionary(g => g.Key, g => g.First());

            foreach (var (function, expression) in cronTickers)
            {
                if (existingByFunction.TryGetValue(function, out var cron))
                {
                    if (!string.Equals(cron.Expression, expression, StringComparison.Ordinal))
                    {
                        await cronSet.UpdateOneAsync(
                            fb.Eq(x => x.Id, cron.Id),
                            Builders<TCronTicker>.Update
                                .Set(x => x.Expression, expression)
                                .Set(x => x.UpdatedAt, now),
                            cancellationToken: cancellationToken).ConfigureAwait(false);
                    }
                }
                else
                {
                    var entity = new TCronTicker
                    {
                        Id = Guid.NewGuid(),
                        Function = function,
                        Expression = expression,
                        InitIdentifier = $"MemoryTicker_Seeded_{function}",
                        CreatedAt = now,
                        UpdatedAt = now,
                        Request = Array.Empty<byte>()
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
                        InitIdentifier = _lockHolder,
                        Expression = item.Expression,
                        Retries = item.Retries,
                        RetryIntervals = item.RetryIntervals
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
                        UpdatedAt = now,
                        CreatedAt = item.NextCronOccurrence.CreatedAt,
                        CronTicker = new TCronTicker
                        {
                            Id = item.Id,
                            Function = item.FunctionName,
                            InitIdentifier = _lockHolder,
                            Expression = item.Expression,
                            Retries = item.Retries,
                            RetryIntervals = item.RetryIntervals
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

                var filter = fb.And(
                    fb.Eq(x => x.Id, occ.Id),
                    fb.Eq(x => x.UpdatedAt, occ.UpdatedAt));

                var update = Builders<CronTickerOccurrenceEntity<TCronTicker>>.Update
                    .Set(x => x.LockHolder, _lockHolder)
                    .Set(x => x.LockedAt, now)
                    .Set(x => x.UpdatedAt, now)
                    .Set(x => x.Status, TickerStatus.InProgress);

                var result = await coll.UpdateOneAsync(filter, update, cancellationToken: cancellationToken).ConfigureAwait(false);
                if (result.ModifiedCount <= 0) continue;

                if (cronById.TryGetValue(occ.CronTickerId, out var cron))
                {
                    occ.CronTicker = new TCronTicker
                    {
                        Id = cron.Id,
                        Function = cron.Function,
                        RetryIntervals = cron.RetryIntervals,
                        Retries = cron.Retries
                    };
                }
                yield return occ;
            }
        }

        public async Task UpdateCronTickerOccurrence(InternalFunctionContext functionContext, CancellationToken cancellationToken = default)
        {
            var update = MongoUpdateBuilders.BuildCronOccurrenceUpdate<TCronTicker>(functionContext, _clock.UtcNow);
            await _context.CronTickerOccurrences
                .UpdateOneAsync(
                    Builders<CronTickerOccurrenceEntity<TCronTicker>>.Filter.Eq(x => x.Id, functionContext.TickerId),
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
            var update = MongoUpdateBuilders.BuildCronOccurrenceUpdate<TCronTicker>(functionContext, _clock.UtcNow);
            await _context.CronTickerOccurrences
                .UpdateManyAsync(
                    Builders<CronTickerOccurrenceEntity<TCronTicker>>.Filter.In(x => x.Id, cronOccurrenceIds),
                    update,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
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
                    .Set(x => x.Status, TickerStatus.Idle)
                    .Set(x => x.UpdatedAt, now),
                cancellationToken: cancellationToken).ConfigureAwait(false);

            var fb = Builders<CronTickerOccurrenceEntity<TCronTicker>>.Filter;
            await coll.UpdateManyAsync(
                fb.And(fb.Eq(x => x.LockHolder, instanceIdentifier), fb.Eq(x => x.Status, TickerStatus.InProgress)),
                Builders<CronTickerOccurrenceEntity<TCronTicker>>.Update
                    .Set(x => x.LockHolder, (string)null)
                    .Set(x => x.LockedAt, (DateTime?)null)
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
            var coll = _context.CronTickerOccurrences;
            var fb = Builders<CronTickerOccurrenceEntity<TCronTicker>>.Filter;

            var filter = fb.And(
                fb.In(x => x.Id, occurrenceIds),
                MongoUpdateBuilders.CanAcquireCronOccurrence<TCronTicker>(_lockHolder));

            var update = Builders<CronTickerOccurrenceEntity<TCronTicker>>.Update
                .Set(x => x.LockHolder, _lockHolder)
                .Set(x => x.LockedAt, now)
                .Set(x => x.Status, TickerStatus.InProgress)
                .Set(x => x.UpdatedAt, now);

            var result = await coll.UpdateManyAsync(filter, update, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (result.ModifiedCount == 0) return Array.Empty<CronTickerOccurrenceEntity<TCronTicker>>();

            var acquiredFilter = fb.And(
                fb.In(x => x.Id, occurrenceIds),
                fb.Eq(x => x.LockHolder, _lockHolder),
                fb.Eq(x => x.Status, TickerStatus.InProgress));

            var rows = await coll.Find(acquiredFilter).ToListAsync(cancellationToken).ConfigureAwait(false);
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

        private async Task<Dictionary<Guid, List<TTimeTicker>>> LoadChildrenLookup(Guid[] parentIds, CancellationToken ct)
        {
            if (parentIds.Length == 0) return new Dictionary<Guid, List<TTimeTicker>>();
            var fb = Builders<TTimeTicker>.Filter;
            var rows = await _context.TimeTickers
                .Find(fb.And(
                    fb.In(x => x.ParentId, parentIds.Select(p => (Guid?)p)),
                    fb.Eq(x => x.ExecutionTime, null)))
                .ToListAsync(ct)
                .ConfigureAwait(false);
            return rows
                .Where(r => r.ParentId.HasValue)
                .GroupBy(r => r.ParentId.Value)
                .ToDictionary(g => g.Key, g => g.ToList());
        }

        private TimeTickerEntity BuildQueuedEntity(TTimeTicker ticker, Dictionary<Guid, List<TTimeTicker>> byParent)
        {
            // The ForQueueTimeTickers projection expects nested .Children with grandchildren.
            // Since we already filter children to ExecutionTime == null, attach them in-memory
            // then invoke the compiled projection to produce the shape the scheduler expects.
            if (byParent.TryGetValue(ticker.Id, out var directChildren))
            {
                ticker.Children = directChildren;
                // Mongo-side we don't fetch grandchildren in LoadChildrenLookup (they'd cascade widely).
                // Grandchildren are rare in practice and only needed for deeply-nested time tickers;
                // when needed the dashboard goes through ITickerQueryable.WithRelated(ChildrenDeep).
                foreach (var ch in directChildren)
                    ch.Children = new List<TTimeTicker>();
            }
            else
            {
                ticker.Children = new List<TTimeTicker>();
            }
            return ProjectTimeTicker(ticker);
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
