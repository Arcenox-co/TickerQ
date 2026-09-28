using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using TickerQ.EntityFrameworkCore.DbContextFactory;
using TickerQ.EntityFrameworkCore.Configurations;
using TickerQ.EntityFrameworkCore.Entities;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Infrastructure;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ.EntityFrameworkCore.Infrastructure;

internal abstract class BasePersistenceProvider<TDbContext, TTimeTicker, TCronTicker>
    where TDbContext : DbContext
    where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
    where TCronTicker : CronTickerEntity, new()
{
    public BasePersistenceProvider(IServiceProvider serviceProvider, ITickerClock clock, SchedulerOptionsBuilder optionsBuilder, ITickerQRedisContext redisContext)
    {
        _clock = clock;
        RedisContext = redisContext;
        _serviceProvider = serviceProvider;
        _nodeFinalizationOutboxReadiness = serviceProvider.GetService<IEfCoreNodeFinalizationOutboxReadiness>();
        _lockHolder = optionsBuilder.ExecutionOwnerId;
        _schedulerOptions = optionsBuilder;
        _runtimeSchedulerEnabled = optionsBuilder.RuntimeSchedulerEnabled;
        _hasRuntimeActivationScopeBinding = optionsBuilder.HasRuntimeActivationScopeBinding;
        _requiresActivatedRuntimeAdmission = _runtimeSchedulerEnabled && _hasRuntimeActivationScopeBinding;
        _runtimeActivationEpoch = optionsBuilder.RuntimeActivationEpoch;
        _runtimePartitionKey = (optionsBuilder.RuntimePartition ?? TickerQRuntimePartition.LegacyGlobal).StorageKey;
        _runtimeActivationScopeKey = _hasRuntimeActivationScopeBinding
            ? _schedulerOptions.RuntimeActivationScope?.ScopeKey ?? TickerQStoreMetadata.SingletonId
            : TickerQStoreMetadata.SingletonId;
    }

    protected readonly SchedulerOptionsBuilder _schedulerOptions;
    public bool SupportsLegacyRuntimePartitionAdoption => true;

    public async Task AdoptLegacyRuntimePartitionAsync(
        LegacyRuntimePartitionAdoption adoption, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(adoption);
        if (!StringComparer.Ordinal.Equals(adoption.TargetPartition.StorageKey, _runtimePartitionKey))
            throw new InvalidOperationException("Legacy adoption target does not match this provider's runtime partition.");
        // Adoption is the one store-global maintenance operation. It must be able to
        // address the legacy owner explicitly instead of applying the provider's
        // normal construction-bound Added-entity stamp.
        using var strategyLease = await DbContextLease<TDbContext>.CreateAsync(
            _serviceProvider, cancellationToken).ConfigureAwait(false);
        var strategy = strategyLease.Context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async ct =>
        {
            using var lease = await DbContextLease<TDbContext>.CreateAsync(
                _serviceProvider, ct).ConfigureAwait(false);
            var db = lease.Context;
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct)
                .ConfigureAwait(false);
            var legacyKey = TickerQRuntimePartition.LegacyGlobal.StorageKey;
            var authority = $"{_runtimePartitionKey}|{adoption.Epoch}";
            var record = await db.Set<TickerQStoreMetadata>().SingleOrDefaultAsync(
                x => x.ApplicationNamespaceKey == legacyKey &&
                     x.Id == TickerQStoreMetadata.LegacyRuntimeAdoptionId, ct).ConfigureAwait(false);
            if (record != null)
            {
                if (!StringComparer.Ordinal.Equals(record.LastMigrationId, authority))
                    throw new InvalidOperationException(
                        "Legacy runtime adoption is already held or completed by a different owner or epoch.");
                if (record.ActivationCheckpoint == "completed")
                {
                    await transaction.CommitAsync(ct).ConfigureAwait(false);
                    return;
                }
            }
            else
            {
                record = new TickerQStoreMetadata
                {
                    ApplicationNamespaceKey = legacyKey, Id = TickerQStoreMetadata.LegacyRuntimeAdoptionId,
                    LastMigrationId = authority, ActivationCheckpoint = "adopting",
                    UpdatedAtUtc = _clock.UtcNow, Version = 1
                };
                db.Set<TickerQStoreMetadata>().Add(record);
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }
            if (AfterLegacyAdoptionLeaseForTestAsync != null)
                await AfterLegacyAdoptionLeaseForTestAsync(ct).ConfigureAwait(false);

            var owners = new HashSet<string>(StringComparer.Ordinal);
            owners.UnionWith(await db.Set<TTimeTicker>().Select(x => x.ApplicationNamespaceKey).Distinct().ToListAsync(ct));
            owners.UnionWith(await db.Set<TCronTicker>().Select(x => x.ApplicationNamespaceKey).Distinct().ToListAsync(ct));
            owners.UnionWith(await db.Set<CronTickerOccurrenceEntity<TCronTicker>>().Select(x => x.ApplicationNamespaceKey).Distinct().ToListAsync(ct));
            owners.UnionWith(await db.Set<TimeTickerResultEntity<TTimeTicker>>().Select(x => x.ApplicationNamespaceKey).Distinct().ToListAsync(ct));
            owners.UnionWith(await db.Set<CronTickerOccurrenceResultEntity<TCronTicker>>().Select(x => x.ApplicationNamespaceKey).Distinct().ToListAsync(ct));
            owners.UnionWith(await db.Set<NodeFinalizationOutboxEntity>().Select(x => x.ApplicationNamespaceKey).Distinct().ToListAsync(ct));
            owners.UnionWith(await db.Set<TickerQStoreMetadata>()
                .Where(x => x.Id != TickerQStoreMetadata.SingletonId &&
                            x.Id != TickerQStoreMetadata.LegacyRuntimeAdoptionId)
                .Select(x => x.ApplicationNamespaceKey).Distinct().ToListAsync(ct));
            owners.Remove(legacyKey); owners.Remove(_runtimePartitionKey);
            if (owners.Count > 0)
                throw new InvalidOperationException(
                    "Legacy runtime adoption is ambiguous because runtime rows exist for another namespace.");

            // Composite foreign keys are immediate on SQLite/PostgreSQL and cannot be updated parent-first
            // or child-first. Copy a complete target graph while the legacy graph still satisfies its FKs,
            // then delete the legacy graph dependents-first in the same serializable transaction.
            var legacyTimeTickers = await db.Set<TTimeTicker>().AsNoTracking()
                .Where(x => x.ApplicationNamespaceKey == legacyKey).ToListAsync(ct).ConfigureAwait(false);
            var legacyCronTickers = await db.Set<TCronTicker>().AsNoTracking()
                .Where(x => x.ApplicationNamespaceKey == legacyKey).ToListAsync(ct).ConfigureAwait(false);
            var legacyOccurrences = await db.Set<CronTickerOccurrenceEntity<TCronTicker>>().AsNoTracking()
                .Where(x => x.ApplicationNamespaceKey == legacyKey).ToListAsync(ct).ConfigureAwait(false);
            var legacyTimeResults = await db.Set<TimeTickerResultEntity<TTimeTicker>>().AsNoTracking()
                .Where(x => x.ApplicationNamespaceKey == legacyKey).ToListAsync(ct).ConfigureAwait(false);
            var legacyOccurrenceResults = await db.Set<CronTickerOccurrenceResultEntity<TCronTicker>>().AsNoTracking()
                .Where(x => x.ApplicationNamespaceKey == legacyKey).ToListAsync(ct).ConfigureAwait(false);
            var legacyOutbox = await db.Set<NodeFinalizationOutboxEntity>().AsNoTracking()
                .Where(x => x.ApplicationNamespaceKey == legacyKey).ToListAsync(ct).ConfigureAwait(false);
            var legacyMetadata = await db.Set<TickerQStoreMetadata>().AsNoTracking()
                .Where(x => x.ApplicationNamespaceKey == legacyKey &&
                            x.Id != TickerQStoreMetadata.LegacyRuntimeAdoptionId &&
                            x.Id != TickerQStoreMetadata.SingletonId)
                .ToListAsync(ct).ConfigureAwait(false);

            var targetTimeTickers = legacyTimeTickers.Select(x => CloneScalar(db, x)).ToArray();
            foreach (var row in targetTimeTickers) row.ApplicationNamespaceKey = _runtimePartitionKey;
            var targetCronTickers = legacyCronTickers.Select(x => CloneScalar(db, x)).ToArray();
            foreach (var row in targetCronTickers) row.ApplicationNamespaceKey = _runtimePartitionKey;
            db.Set<TTimeTicker>().AddRange(targetTimeTickers);
            db.Set<TCronTicker>().AddRange(targetCronTickers);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            var targetOccurrences = legacyOccurrences.Select(x => CloneScalar(db, x)).ToArray();
            foreach (var row in targetOccurrences) row.ApplicationNamespaceKey = _runtimePartitionKey;
            db.Set<CronTickerOccurrenceEntity<TCronTicker>>().AddRange(targetOccurrences);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            var targetTimeResults = legacyTimeResults.Select(x => CloneScalar(db, x)).ToArray();
            foreach (var row in targetTimeResults) row.ApplicationNamespaceKey = _runtimePartitionKey;
            var targetOccurrenceResults = legacyOccurrenceResults.Select(x => CloneScalar(db, x)).ToArray();
            foreach (var row in targetOccurrenceResults) row.ApplicationNamespaceKey = _runtimePartitionKey;
            var targetOutbox = legacyOutbox.Select(x => CloneScalar(db, x)).ToArray();
            foreach (var row in targetOutbox) row.ApplicationNamespaceKey = _runtimePartitionKey;
            var targetMetadata = legacyMetadata.Select(x => CloneScalar(db, x)).ToArray();
            foreach (var row in targetMetadata) row.ApplicationNamespaceKey = _runtimePartitionKey;
            db.Set<TimeTickerResultEntity<TTimeTicker>>().AddRange(targetTimeResults);
            db.Set<CronTickerOccurrenceResultEntity<TCronTicker>>().AddRange(targetOccurrenceResults);
            db.Set<NodeFinalizationOutboxEntity>().AddRange(targetOutbox);
            db.Set<TickerQStoreMetadata>().AddRange(targetMetadata);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            await db.Set<TimeTickerResultEntity<TTimeTicker>>().Where(x => x.ApplicationNamespaceKey == legacyKey)
                .ExecuteDeleteAsync(ct).ConfigureAwait(false);
            await db.Set<CronTickerOccurrenceResultEntity<TCronTicker>>().Where(x => x.ApplicationNamespaceKey == legacyKey)
                .ExecuteDeleteAsync(ct).ConfigureAwait(false);
            await db.Set<NodeFinalizationOutboxEntity>().Where(x => x.ApplicationNamespaceKey == legacyKey)
                .ExecuteDeleteAsync(ct).ConfigureAwait(false);
            await db.Set<CronTickerOccurrenceEntity<TCronTicker>>().Where(x => x.ApplicationNamespaceKey == legacyKey)
                .ExecuteDeleteAsync(ct).ConfigureAwait(false);
            await db.Set<TTimeTicker>().Where(x => x.ApplicationNamespaceKey == legacyKey)
                .ExecuteDeleteAsync(ct).ConfigureAwait(false);
            await db.Set<TCronTicker>().Where(x => x.ApplicationNamespaceKey == legacyKey)
                .ExecuteDeleteAsync(ct).ConfigureAwait(false);
            await db.Set<TickerQStoreMetadata>().Where(x => x.ApplicationNamespaceKey == legacyKey &&
                    x.Id != TickerQStoreMetadata.LegacyRuntimeAdoptionId &&
                    x.Id != TickerQStoreMetadata.SingletonId)
                .ExecuteDeleteAsync(ct).ConfigureAwait(false);
            record.ActivationCheckpoint = "completed";
            record.UpdatedAtUtc = _clock.UtcNow;
            record.Version++;
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    private static TEntity CloneScalar<TEntity>(DbContext dbContext, TEntity source)
        where TEntity : class, new()
    {
        var clone = new TEntity();
        dbContext.Entry(clone).CurrentValues.SetValues(source);
        return clone;
    }

    /// <summary>
    /// Lease expiry to stamp on rows this node marks InProgress. Null when stale-job
    /// recovery is disabled — rows then carry no lease and are never swept.
    /// </summary>
    protected DateTime? NextLeaseUntil(DateTime now)
        => _schedulerOptions.StaleJobRecoveryEnabled ? now.Add(_schedulerOptions.LeaseDuration) : (DateTime?)null;

    protected readonly IServiceProvider _serviceProvider;
    protected readonly string _lockHolder;
    protected readonly ITickerClock _clock;
    protected readonly ITickerQRedisContext RedisContext;
    private readonly IEfCoreNodeFinalizationOutboxReadiness _nodeFinalizationOutboxReadiness;
    private readonly string _runtimeActivationScopeKey;
    private readonly bool _runtimeSchedulerEnabled;
    private readonly bool _hasRuntimeActivationScopeBinding;
    private readonly bool _requiresActivatedRuntimeAdmission;
    private readonly long _runtimeActivationEpoch;
    protected readonly string _runtimePartitionKey;
    protected string CronExpressionsCacheKey => $"cron:expressions:{_runtimePartitionKey}";
    protected internal Func<CancellationToken, Task> AfterRunnableAdmissionFenceForTestAsync { get; set; }
    protected internal Func<CancellationToken, Task> AfterLegacyAdoptionLeaseForTestAsync { get; set; }
    protected internal Func<CancellationToken, Task> AfterTimeTickerGraphMutationFenceForTestAsync { get; set; }

    private async Task<bool> LockLegacyWriteAdmissionAsync(
        TDbContext dbContext, CancellationToken cancellationToken)
    {
        if (_runtimePartitionKey != TickerQRuntimePartition.LegacyGlobal.StorageKey)
            return true;
        // This indexed predicate read and adoption's read/insert run under Serializable transactions.
        // The absent-row range is therefore fenced during first adoption; once inserted, the durable
        // tombstone rejects every later legacy creation/publication attempt.
        return !await dbContext.Set<TickerQStoreMetadata>().AsNoTracking().AnyAsync(
            x => x.ApplicationNamespaceKey == TickerQRuntimePartition.LegacyGlobal.StorageKey &&
                 x.Id == TickerQStoreMetadata.LegacyRuntimeAdoptionId,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Executes a store-global TimeTicker graph mutation through the configured provider execution
    /// strategy. Every retry creates a fresh DbContext and takes the same explicit sentinel-row write
    /// lock before reading or replacing graph structure, independent of application activation scope.
    /// </summary>
    protected async Task<TResult> ExecuteTimeTickerGraphMutationAsync<TResult>(
        bool requireRunnableAdmission, TResult deniedResult,
        Func<TDbContext, CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken)
    {
        using var strategySession = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var strategy = strategySession.Context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var dbContext = session.Context;
            RunnableAdmissionContextCreatedForTest?.Invoke(dbContext.ContextId.InstanceId);
            await using var transaction = await dbContext.Database
                .BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
            if (!await LockLegacyWriteAdmissionAsync(dbContext, cancellationToken).ConfigureAwait(false))
            {
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                return deniedResult;
            }
            await LockTimeTickerGraphMutationAsync(dbContext, cancellationToken).ConfigureAwait(false);

            if (requireRunnableAdmission &&
                !await LockRunnableAdmissionAsync(dbContext, cancellationToken, _runtimeActivationScopeKey)
                    .ConfigureAwait(false))
            {
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                return deniedResult;
            }

            var result = await operation(dbContext, cancellationToken).ConfigureAwait(false);
            if (AfterRunnableAdmissionOperationForTestAsync != null)
                await AfterRunnableAdmissionOperationForTestAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
            return result;
        }).ConfigureAwait(false);
    }

    protected async Task LockTimeTickerGraphMutationAsync(
        TDbContext dbContext, CancellationToken cancellationToken)
    {
        var metadata = dbContext.Set<TickerQStoreMetadata>();
            if (!await metadata.AsNoTracking().AnyAsync(
                    x => x.ApplicationNamespaceKey == _runtimePartitionKey &&
                         x.Id == TickerQStoreMetadata.TimeTickerGraphMutationSentinelId,
                    cancellationToken).ConfigureAwait(false))
            {
                metadata.Add(new TickerQStoreMetadata
                {
                    ApplicationNamespaceKey = _runtimePartitionKey,
                    Id = TickerQStoreMetadata.TimeTickerGraphMutationSentinelId,
                    UpdatedAtUtc = _clock.UtcNow
                });
                await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }

            var locked = await metadata
                .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey &&
                            x.Id == TickerQStoreMetadata.TimeTickerGraphMutationSentinelId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Version, x => x.Version),
                    cancellationToken).ConfigureAwait(false);
            if (locked != 1)
                throw new DbUpdateConcurrencyException("TickerQ could not acquire the TimeTicker graph mutation sentinel.");
            if (AfterTimeTickerGraphMutationFenceForTestAsync != null)
                await AfterTimeTickerGraphMutationFenceForTestAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Locks and validates durable runnable admission inside the caller's transaction.</summary>
    protected async Task<bool> LockRunnableAdmissionAsync(
        TDbContext dbContext, CancellationToken cancellationToken, string scopeKey = null)
    {
        // Queue-only producers never participate in runtime activation. This return deliberately
        // precedes every metadata query/write, including for a namespaced producer that uses the
        // reconciliation protocol for startup migrations.
        if (!_runtimeSchedulerEnabled)
            return true;
        // A scheduler cannot safely infer a mutable/default global scope. Registration must bind the
        // immutable scope+epoch before this provider is constructed.
        if (!_hasRuntimeActivationScopeBinding || !_requiresActivatedRuntimeAdmission)
            return false;

        var metadata = dbContext.Set<TickerQStoreMetadata>();
        scopeKey ??= _runtimeActivationScopeKey;

        // A configured scheduler is fail-closed until its immutable construction-bound epoch has
        // been activated. Unbound providers and queue-only producers retain rolling-upgrade
        // compatibility with absent/Pending metadata.
        if (!await metadata.AsNoTracking().AnyAsync(
                x => x.ApplicationNamespaceKey == _runtimePartitionKey && x.Id == scopeKey, cancellationToken)
                .ConfigureAwait(false))
        {
            return false;
        }

        var startupSeeder = string.Equals(scopeKey, _runtimeActivationScopeKey, StringComparison.Ordinal)
                            && StartupSeederAdmissionContext.Matches(
                                _runtimeActivationScopeKey, _runtimeActivationEpoch);
        var locked = await metadata
            .Where(x => x.Id == scopeKey &&
                        x.ApplicationNamespaceKey == _runtimePartitionKey &&
                        (x.ActivationPhase == ActivationEpochPhase.Activated ||
                         (startupSeeder && x.ActivationPhase == ActivationEpochPhase.Activating)) &&
                        x.ActivationEpoch == _runtimeActivationEpoch)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(x => x.Version, x => x.Version),
                cancellationToken).ConfigureAwait(false);
        if (locked == 0)
            return false;
        if (AfterRunnableAdmissionFenceForTestAsync != null)
            await AfterRunnableAdmissionFenceForTestAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    protected async Task<bool> IsRunnableAdmissionAllowedAsync(
        TDbContext dbContext, CancellationToken cancellationToken)
    {
        if (!_runtimeSchedulerEnabled)
            return true;
        if (!_hasRuntimeActivationScopeBinding || !_requiresActivatedRuntimeAdmission)
            return false;
        var state = await dbContext.Set<TickerQStoreMetadata>().AsNoTracking()
            .Where(x => x.Id == _runtimeActivationScopeKey)
            .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey)
            .Select(x => new { x.ActivationEpoch, x.ActivationPhase })
            .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return state != null && state.ActivationEpoch == _runtimeActivationEpoch &&
               (state.ActivationPhase == ActivationEpochPhase.Activated ||
                (state.ActivationPhase == ActivationEpochPhase.Activating &&
                 StartupSeederAdmissionContext.Matches(
                     _runtimeActivationScopeKey, _runtimeActivationEpoch)));
    }

    protected internal Action<Guid> RunnableAdmissionContextCreatedForTest { get; set; }
    protected internal Func<CancellationToken, Task> AfterRunnableAdmissionOperationForTestAsync { get; set; }

    /// <summary>
    /// Executes a runnable-admission mutation behind the durable activation row lock. The execution
    /// strategy itself is obtained from a short-lived context, while every attempt creates a fresh
    /// context/transaction and passes that context to the operation. Retry-local mutable values must
    /// therefore be created inside the operation delegate.
    /// </summary>
    protected async Task<TResult> ExecuteRunnableAdmissionAsync<TResult>(
        TResult deniedResult,
        Func<TDbContext, CancellationToken, Task<TResult>> operation, CancellationToken cancellationToken)
    {
        using var strategySession = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var strategy = strategySession.Context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async ct =>
        {
            using var operationSession = await CreateDbContextAsync(ct).ConfigureAwait(false);
            var dbContext = operationSession.Context;
            RunnableAdmissionContextCreatedForTest?.Invoke(dbContext.ContextId.InstanceId);
            await using var transaction = await dbContext.Database
                .BeginTransactionAsync(IsolationLevel.Serializable, ct).ConfigureAwait(false);
            if (!await LockLegacyWriteAdmissionAsync(dbContext, ct).ConfigureAwait(false))
            {
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                return deniedResult;
            }
            if (!await LockRunnableAdmissionAsync(dbContext, ct, _runtimeActivationScopeKey).ConfigureAwait(false))
            {
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                return deniedResult;
            }

            var result = await operation(dbContext, ct).ConfigureAwait(false);
            if (AfterRunnableAdmissionOperationForTestAsync != null)
                await AfterRunnableAdmissionOperationForTestAsync(ct).ConfigureAwait(false);
            await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
            return result;
        }, cancellationToken).ConfigureAwait(false);
    }

    public bool SupportsResultPublication => true;
    public bool SupportsAcknowledgedTerminalUpdates => true;
    public bool SupportsReconciliationActivationEpoch => true;
    public bool SupportsAuthoritativeCronReconciliation => true;
    public bool SupportsDurableNodeFinalizationOutbox =>
        _nodeFinalizationOutboxReadiness?.IsReady == true;

    public async Task<ActivationEpochState> GetReconciliationActivationStateAsync(
        CancellationToken cancellationToken = default)
        => await GetReconciliationActivationStateCoreAsync(
            _runtimeActivationScopeKey, cancellationToken).ConfigureAwait(false);

    public Task<ActivationEpochState> GetReconciliationActivationStateAsync(
        ReconciliationActivationScope scope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        return GetReconciliationActivationStateCoreAsync(scope.ScopeKey, cancellationToken);
    }

    private async Task<ActivationEpochState> GetReconciliationActivationStateCoreAsync(
        string scopeKey, CancellationToken cancellationToken)
    {
        using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var metadata = await session.Context.Set<TickerQStoreMetadata>().AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.ApplicationNamespaceKey == _runtimePartitionKey && x.Id == scopeKey, cancellationToken)
            .ConfigureAwait(false);
        return ToActivationState(metadata);
    }

    public Task<ActivationEpochState> BeginReconciliationActivationEpochAsync(
        long targetEpoch, CancellationToken cancellationToken = default)
    {
        if (targetEpoch <= 0) throw new ArgumentOutOfRangeException(nameof(targetEpoch));
        return MutateActivationStateAsync(_runtimeActivationScopeKey, targetEpoch, null, ActivationMutation.Begin, cancellationToken);
    }

    public Task<ActivationEpochState> BeginReconciliationActivationEpochAsync(
        ReconciliationActivationScope scope, long targetEpoch, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (targetEpoch <= 0) throw new ArgumentOutOfRangeException(nameof(targetEpoch));
        return MutateActivationStateAsync(scope.ScopeKey, targetEpoch, null, ActivationMutation.Begin, cancellationToken);
    }

    public Task<ActivationEpochState> AdvanceReconciliationCheckpointAsync(
        long targetEpoch, string checkpoint, CancellationToken cancellationToken = default)
    {
        if (targetEpoch <= 0) throw new ArgumentOutOfRangeException(nameof(targetEpoch));
        if (string.IsNullOrWhiteSpace(checkpoint) || checkpoint.Length > 200)
            throw new ArgumentException("Activation checkpoint must be non-empty and at most 200 characters.", nameof(checkpoint));
        return MutateActivationStateAsync(_runtimeActivationScopeKey, targetEpoch, checkpoint, ActivationMutation.Checkpoint, cancellationToken);
    }

    public Task<ActivationEpochState> AdvanceReconciliationCheckpointAsync(
        ReconciliationActivationScope scope, long targetEpoch, string checkpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (targetEpoch <= 0) throw new ArgumentOutOfRangeException(nameof(targetEpoch));
        if (string.IsNullOrWhiteSpace(checkpoint) || checkpoint.Length > 200)
            throw new ArgumentException("Activation checkpoint must be non-empty and at most 200 characters.", nameof(checkpoint));
        return MutateActivationStateAsync(scope.ScopeKey, targetEpoch, checkpoint, ActivationMutation.Checkpoint, cancellationToken);
    }

    public Task<ActivationEpochState> CommitReconciliationActivationEpochAsync(
        long targetEpoch, CancellationToken cancellationToken = default)
    {
        if (targetEpoch <= 0) throw new ArgumentOutOfRangeException(nameof(targetEpoch));
        return MutateActivationStateAsync(_runtimeActivationScopeKey, targetEpoch, null, ActivationMutation.Commit, cancellationToken);
    }

    public Task<ActivationEpochState> CommitReconciliationActivationEpochAsync(
        ReconciliationActivationScope scope, long targetEpoch, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (targetEpoch <= 0) throw new ArgumentOutOfRangeException(nameof(targetEpoch));
        return MutateActivationStateAsync(scope.ScopeKey, targetEpoch, null, ActivationMutation.Commit, cancellationToken);
    }

    private async Task<ActivationEpochState> MutateActivationStateAsync(
        string scopeKey, long targetEpoch, string checkpoint, ActivationMutation mutation,
        CancellationToken cancellationToken)
    {
        const int maxAttempts = 16;
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
                var context = session.Context;
                await using var transaction = await context.Database
                    .BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
                var metadata = await context.Set<TickerQStoreMetadata>().AsNoTracking()
                    .SingleOrDefaultAsync(
                        x => x.ApplicationNamespaceKey == _runtimePartitionKey && x.Id == scopeKey,
                        cancellationToken)
                    .ConfigureAwait(false);

                if (metadata == null)
                {
                    if (mutation != ActivationMutation.Begin)
                    {
                        await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
                        return ActivationEpochState.PreEpoch;
                    }

                    metadata = new TickerQStoreMetadata
                    {
                        ApplicationNamespaceKey = _runtimePartitionKey,
                        Id = scopeKey,
                        ActivationEpoch = targetEpoch,
                        ActivationPhase = ActivationEpochPhase.Activating,
                        Version = 1,
                        UpdatedAtUtc = _clock.UtcNow
                    };
                    context.Add(metadata);
                    await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
                    return ToActivationState(metadata);
                }

                var shouldUpdate = false;
                long nextEpoch = metadata.ActivationEpoch;
                var nextPhase = metadata.ActivationPhase;
                string nextCheckpoint = metadata.ActivationCheckpoint;
                switch (mutation)
                {
                    case ActivationMutation.Begin when targetEpoch > metadata.ActivationEpoch:
                        shouldUpdate = true;
                        nextEpoch = targetEpoch;
                        nextPhase = ActivationEpochPhase.Activating;
                        nextCheckpoint = null;
                        break;
                    case ActivationMutation.Begin when targetEpoch == metadata.ActivationEpoch &&
                                                       metadata.ActivationPhase == ActivationEpochPhase.Pending:
                        shouldUpdate = true;
                        nextPhase = ActivationEpochPhase.Activating;
                        break;
                    case ActivationMutation.Checkpoint when targetEpoch == metadata.ActivationEpoch &&
                                                            metadata.ActivationPhase == ActivationEpochPhase.Activating &&
                                                            CanAdvanceCheckpoint(metadata.ActivationCheckpoint, checkpoint):
                        shouldUpdate = true;
                        nextCheckpoint = checkpoint;
                        break;
                    case ActivationMutation.Commit when targetEpoch == metadata.ActivationEpoch &&
                                                        metadata.ActivationPhase == ActivationEpochPhase.Activating:
                        shouldUpdate = true;
                        nextPhase = ActivationEpochPhase.Activated;
                        break;
                }

                if (!shouldUpdate)
                {
                    await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
                    return ToActivationState(metadata);
                }

                var affected = await context.Set<TickerQStoreMetadata>()
                    .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey &&
                                x.Id == scopeKey && x.Version == metadata.Version)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(x => x.ActivationEpoch, nextEpoch)
                        .SetProperty(x => x.ActivationPhase, nextPhase)
                        .SetProperty(x => x.ActivationCheckpoint, nextCheckpoint)
                        .SetProperty(x => x.Version, metadata.Version + 1)
                        .SetProperty(x => x.UpdatedAtUtc, _clock.UtcNow), cancellationToken)
                    .ConfigureAwait(false);
                if (affected != 1)
                {
                    await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                    continue;
                }

                metadata.ActivationEpoch = nextEpoch;
                metadata.ActivationPhase = nextPhase;
                metadata.ActivationCheckpoint = nextCheckpoint;
                metadata.Version++;
                await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
                return ToActivationState(metadata);
            }
            catch (DbUpdateConcurrencyException) when (attempt + 1 < maxAttempts) { }
            catch (DbUpdateException) when (attempt + 1 < maxAttempts) { }

            await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(50, attempt + 1)), cancellationToken)
                .ConfigureAwait(false);
        }

        throw new InvalidOperationException("TickerQ activation metadata did not converge after repeated optimistic-concurrency retries.");
    }

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

    private static ActivationEpochState ToActivationState(TickerQStoreMetadata metadata)
        => metadata == null
            ? ActivationEpochState.PreEpoch
            : new ActivationEpochState
            {
                Epoch = metadata.ActivationEpoch,
                Phase = metadata.ActivationPhase,
                Checkpoint = metadata.ActivationCheckpoint
            };

    private enum ActivationMutation { Begin, Checkpoint, Commit }

    public async Task<TickerResultEnvelope> GetTimeTickerResultAsync(
        Guid id, CancellationToken cancellationToken = default)
    {
        using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var row = await session.Context.Set<TimeTickerResultEntity<TTimeTicker>>()
            .AsNoTracking().SingleOrDefaultAsync(
                x => x.ApplicationNamespaceKey == _runtimePartitionKey && x.TickerId == id, cancellationToken)
            .ConfigureAwait(false);
        return row == null ? null : ReadEnvelope(
            row.Payload, row.EnvelopeVersion, row.MediaType, row.ContractId, row.ContractType);
    }

    public async Task<TickerResultEnvelope> GetCronTickerOccurrenceResultAsync(
        Guid id, CancellationToken cancellationToken = default)
    {
        using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var row = await session.Context.Set<CronTickerOccurrenceResultEntity<TCronTicker>>()
            .AsNoTracking().SingleOrDefaultAsync(
                x => x.ApplicationNamespaceKey == _runtimePartitionKey && x.TickerId == id, cancellationToken)
            .ConfigureAwait(false);
        return row == null ? null : ReadEnvelope(
            row.Payload, row.EnvelopeVersion, row.MediaType, row.ContractId, row.ContractType);
    }

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

    public async Task<bool> CommitTerminalTickerAndEnqueueNodeFinalizationAsync(
        InternalFunctionContext functionContext, NodeFinalizationIntent intent,
        CancellationToken cancellationToken = default)
    {
        EnsureExactTerminalPartition(functionContext);
        ValidateTerminalCommit(functionContext);
        ArgumentNullException.ThrowIfNull(intent);
        if (intent.TickerType != functionContext.Type || intent.TickerId != functionContext.TickerId ||
            !functionContext.AcquisitionToken.HasValue ||
            intent.AcquisitionToken != functionContext.AcquisitionToken.Value)
            throw new InvalidOperationException(
                "Node finalization intent identity does not match the terminal ticker mutation.");
        if (!StringComparer.Ordinal.Equals(intent.RuntimePartitionKey, _runtimePartitionKey))
            throw new InvalidOperationException("Node finalization intent runtime partition mismatch.");

        ValidateEnvelope(functionContext.ResultEnvelope);
        var successful = functionContext.Status is TickerStatus.Done or TickerStatus.DueDone;
        var terminalMutationDigest = ComputeTerminalMutationDigest(functionContext);
        using var strategySession = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var strategy = strategySession.Context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async ct =>
        {
            using var operationSession = await CreateDbContextAsync(ct).ConfigureAwait(false);
            var dbContext = operationSession.Context;
            await using var transaction = await dbContext.Database
                .BeginTransactionAsync(IsolationLevel.Serializable, ct).ConfigureAwait(false);

            var existing = await dbContext.Set<NodeFinalizationOutboxEntity>()
                .AsNoTracking().SingleOrDefaultAsync(
                    x => x.ApplicationNamespaceKey == _runtimePartitionKey && x.OutboxId == intent.OutboxId, ct)
                .ConfigureAwait(false);
            if (existing != null)
            {
                if (!ImmutableIntentMatches(existing, intent, terminalMutationDigest))
                    throw new InvalidOperationException(
                        "Durable Node finalization outbox integrity violation: an existing outbox ID has different immutable content.");
                await transaction.CommitAsync(ct).ConfigureAwait(false);
                return true;
            }

            var now = _clock.UtcNow;
            int affected;
            if (functionContext.Type == TickerType.CronTickerOccurrence)
            {
                var query = dbContext.Set<CronTickerOccurrenceEntity<TCronTicker>>()
                    .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey &&
                                x.Id == functionContext.TickerId && x.LockHolder == _lockHolder &&
                                x.AcquisitionToken == functionContext.AcquisitionToken);
                affected = await query.ExecuteUpdateAsync(
                    setter => setter.UpdateCronTickerOccurrence<TCronTicker>(
                        functionContext, NextLeaseUntil(now)), ct).ConfigureAwait(false);
            }
            else
            {
                if (functionContext.ParentId != null &&
                    !await LockCurrentChainGenerationAsync(dbContext, functionContext, ct).ConfigureAwait(false))
                {
                    await transaction.RollbackAsync(ct).ConfigureAwait(false);
                    return false;
                }
                if (functionContext.ParentId != null &&
                    functionContext.AcquisitionToken != functionContext.ChainGeneration)
                {
                    await transaction.RollbackAsync(ct).ConfigureAwait(false);
                    return false;
                }
                var query = ApplyTimeTickerGenerationFence(dbContext,
                    dbContext.Set<TTimeTicker>().Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey &&
                                                           x.Id == functionContext.TickerId), functionContext);
                if (functionContext.ParentId == null)
                    query = query.Where(x => x.LockHolder == _lockHolder &&
                                             x.AcquisitionToken == functionContext.AcquisitionToken);
                affected = await query.ExecuteUpdateAsync(
                    setter => setter.UpdateTimeTicker<TTimeTicker>(
                        functionContext, now, NextLeaseUntil(now)), ct).ConfigureAwait(false);
            }

            if (affected != 1)
            {
                await transaction.RollbackAsync(ct).ConfigureAwait(false);
                return false;
            }

            if (successful)
            {
                await OnSuccessfulStatusWrittenForTestAsync(dbContext, functionContext, ct).ConfigureAwait(false);
                await ReplaceResultAsync(dbContext, functionContext, ct).ConfigureAwait(false);
            }

            dbContext.Set<NodeFinalizationOutboxEntity>().Add(ToEntity(intent, terminalMutationDigest));
            await dbContext.SaveChangesAsync(ct).ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return true;
        }, cancellationToken).ConfigureAwait(false);
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

    public async Task<IReadOnlyList<NodeFinalizationClaim>> ClaimDueNodeFinalizationsAsync(
        string workerId, int maxCount, DateTime nowUtc, DateTime leaseUntilUtc,
        CancellationToken cancellationToken = default)
    {
        ValidateWorkerAndLease(workerId, maxCount, nowUtc, leaseUntilUtc);
        using var strategySession = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var strategy = strategySession.Context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async ct =>
        {
            using var operationSession = await CreateDbContextAsync(ct).ConfigureAwait(false);
            var dbContext = operationSession.Context;
            await using var transaction = await dbContext.Database
                .BeginTransactionAsync(IsolationLevel.Serializable, ct).ConfigureAwait(false);
            var candidates = await dbContext.Set<NodeFinalizationOutboxEntity>()
                .AsNoTracking().Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey &&
                                           x.AvailableAtUtc <= nowUtc)
                .OrderBy(x => x.AvailableAtUtc).ThenBy(x => x.OutboxId)
                .Select(x => x.OutboxId).Take(maxCount).ToArrayAsync(ct).ConfigureAwait(false);
            var claims = new List<NodeFinalizationClaim>(candidates.Length);
            foreach (var outboxId in candidates)
            {
                var claimToken = Guid.NewGuid();
                var affected = await dbContext.Set<NodeFinalizationOutboxEntity>()
                    .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey &&
                                x.OutboxId == outboxId && x.AvailableAtUtc <= nowUtc)
                    .ExecuteUpdateAsync(setter => setter
                        .SetProperty(x => x.ClaimToken, claimToken)
                        .SetProperty(x => x.ClaimedBy, workerId)
                        .SetProperty(x => x.AvailableAtUtc, leaseUntilUtc)
                        .SetProperty(x => x.AttemptCount, x => x.AttemptCount + 1)
                        .SetProperty(x => x.LastAttemptAtUtc, nowUtc), ct).ConfigureAwait(false);
                if (affected != 1) continue;
                var row = await dbContext.Set<NodeFinalizationOutboxEntity>().AsNoTracking()
                    .SingleAsync(x => x.ApplicationNamespaceKey == _runtimePartitionKey &&
                                      x.OutboxId == outboxId && x.ClaimToken == claimToken &&
                                      x.ClaimedBy == workerId, ct).ConfigureAwait(false);
                claims.Add(ToClaim(row));
            }
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return (IReadOnlyList<NodeFinalizationClaim>)claims;
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> CompleteNodeFinalizationAsync(
        NodeFinalizationClaim claim, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        var intent = claim.Intent;
        using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await session.Context.Set<NodeFinalizationOutboxEntity>()
            .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey &&
                        x.OutboxId == intent.OutboxId && x.TickerType == intent.TickerType &&
                        x.TickerId == intent.TickerId && x.AcquisitionToken == intent.AcquisitionToken &&
                        x.DispatchId == intent.DispatchId && x.NodeEpoch == intent.NodeEpoch &&
                        x.ClaimToken == claim.ClaimToken && x.ClaimedBy == claim.ClaimedBy)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false) == 1;
    }

    public async Task<bool> RescheduleNodeFinalizationAsync(
        NodeFinalizationClaim claim, DateTime availableAtUtc, string errorCode,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        if (availableAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Available time must be UTC.", nameof(availableAtUtc));
        if (string.IsNullOrWhiteSpace(errorCode) ||
            errorCode.Length > NodeFinalizationOperationalState.MaxErrorCodeLength)
            throw new ArgumentException("Error code must be non-empty and bounded.", nameof(errorCode));
        var intent = claim.Intent;
        using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await session.Context.Set<NodeFinalizationOutboxEntity>()
            .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey &&
                        x.OutboxId == intent.OutboxId && x.TickerType == intent.TickerType &&
                        x.TickerId == intent.TickerId && x.AcquisitionToken == intent.AcquisitionToken &&
                        x.DispatchId == intent.DispatchId && x.NodeEpoch == intent.NodeEpoch &&
                        x.ClaimToken == claim.ClaimToken && x.ClaimedBy == claim.ClaimedBy)
            .ExecuteUpdateAsync(setter => setter
                .SetProperty(x => x.AvailableAtUtc, availableAtUtc)
                .SetProperty(x => x.ClaimToken, (Guid?)null)
                .SetProperty(x => x.ClaimedBy, (string)null)
                .SetProperty(x => x.LastErrorCode, errorCode), cancellationToken)
            .ConfigureAwait(false) == 1;
    }

    private async Task<bool> CommitTerminalTickerCoreAsync(
        InternalFunctionContext functionContext, bool enforceRemoteChildToken,
        CancellationToken cancellationToken = default)
    {
        ValidateTerminalCommit(functionContext);
        var successful = functionContext.Status is TickerStatus.Done or TickerStatus.DueDone;
        ValidateEnvelope(functionContext.ResultEnvelope);
        using var strategySession = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var strategy = strategySession.Context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(
            async ct =>
            {
                // A retry must start with a fresh context so a rolled-back tracked result entity from the
                // prior attempt cannot leak into or collide with the next serializable transaction.
                using var operationSession = await CreateDbContextAsync(ct).ConfigureAwait(false);
                var dbContext = operationSession.Context;
                await using var transaction = await dbContext.Database
                    .BeginTransactionAsync(IsolationLevel.Serializable, ct).ConfigureAwait(false);
                var now = _clock.UtcNow;
                int affected;
                if (functionContext.Type == TickerType.CronTickerOccurrence)
                {
                    var query = dbContext.Set<CronTickerOccurrenceEntity<TCronTicker>>()
                        .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey &&
                                    x.Id == functionContext.TickerId);
                    query = functionContext.AcquisitionToken.HasValue
                        ? query.Where(x => x.LockHolder == _lockHolder &&
                                           x.AcquisitionToken == functionContext.AcquisitionToken)
                        : query.Where(_ => false);
                    affected = await query.ExecuteUpdateAsync(
                        setter => setter.UpdateCronTickerOccurrence<TCronTicker>(
                            functionContext, NextLeaseUntil(now)), ct).ConfigureAwait(false);
                }
                else
                {
                    if (functionContext.ParentId != null &&
                        !await LockCurrentChainGenerationAsync(
                            dbContext, functionContext, ct).ConfigureAwait(false))
                    {
                        await transaction.RollbackAsync(ct).ConfigureAwait(false);
                        return false;
                    }
                    if (functionContext.ParentId != null && enforceRemoteChildToken &&
                        (!functionContext.AcquisitionToken.HasValue ||
                         functionContext.AcquisitionToken != functionContext.ChainGeneration))
                    {
                        await transaction.RollbackAsync(ct).ConfigureAwait(false);
                        return false;
                    }
                    var query = dbContext.Set<TTimeTicker>().Where(
                        x => x.ApplicationNamespaceKey == _runtimePartitionKey && x.Id == functionContext.TickerId);
                    query = ApplyTimeTickerGenerationFence(dbContext, query, functionContext);
                    if (functionContext.ParentId == null)
                        query = functionContext.AcquisitionToken.HasValue
                            ? query.Where(x => x.LockHolder == _lockHolder &&
                                               x.AcquisitionToken == functionContext.AcquisitionToken)
                            : query.Where(_ => false);
                    affected = await query.ExecuteUpdateAsync(
                        setter => setter.UpdateTimeTicker<TTimeTicker>(
                            functionContext, now, NextLeaseUntil(now)), ct).ConfigureAwait(false);
                }

                if (affected != 1)
                {
                    await transaction.RollbackAsync(ct).ConfigureAwait(false);
                    return false;
                }

                if (successful)
                {
                    await OnSuccessfulStatusWrittenForTestAsync(
                        dbContext, functionContext, ct).ConfigureAwait(false);
                    await ReplaceResultAsync(dbContext, functionContext, ct).ConfigureAwait(false);
                }
                await transaction.CommitAsync(ct).ConfigureAwait(false);
                return true;
            }, cancellationToken).ConfigureAwait(false);
    }

    protected internal virtual Task OnSuccessfulStatusWrittenForTestAsync(
        TDbContext dbContext, InternalFunctionContext functionContext, CancellationToken cancellationToken)
        => Task.CompletedTask;

    private async Task ReplaceResultAsync(
        TDbContext dbContext, InternalFunctionContext functionContext, CancellationToken cancellationToken)
    {
        if (functionContext.Type == TickerType.CronTickerOccurrence)
        {
            await dbContext.Set<CronTickerOccurrenceResultEntity<TCronTicker>>()
                .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey &&
                            x.TickerId == functionContext.TickerId)
                .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            if (functionContext.ResultEnvelope != null)
            {
                dbContext.Set<CronTickerOccurrenceResultEntity<TCronTicker>>().Add(new()
                {
                    ApplicationNamespaceKey = _runtimePartitionKey,
                    TickerId = functionContext.TickerId,
                    Payload = functionContext.ResultEnvelope.ToPayloadArray(),
                    EnvelopeVersion = functionContext.ResultEnvelope.Version,
                    MediaType = functionContext.ResultEnvelope.MediaType,
                    ContractId = functionContext.ResultEnvelope.ContractId,
                    ContractType = functionContext.ResultEnvelope.ContractType
                });
                await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            return;
        }

        await dbContext.Set<TimeTickerResultEntity<TTimeTicker>>()
            .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey &&
                        x.TickerId == functionContext.TickerId)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        if (functionContext.ResultEnvelope != null)
        {
            dbContext.Set<TimeTickerResultEntity<TTimeTicker>>().Add(new()
            {
                ApplicationNamespaceKey = _runtimePartitionKey,
                TickerId = functionContext.TickerId,
                Payload = functionContext.ResultEnvelope.ToPayloadArray(),
                EnvelopeVersion = functionContext.ResultEnvelope.Version,
                MediaType = functionContext.ResultEnvelope.MediaType,
                ContractId = functionContext.ResultEnvelope.ContractId,
                ContractType = functionContext.ResultEnvelope.ContractType
            });
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }


    private static void ValidateTerminalCommit(InternalFunctionContext functionContext)
    {
        ArgumentNullException.ThrowIfNull(functionContext);
        var successful = functionContext.Status is TickerStatus.Done or TickerStatus.DueDone;
        if (!functionContext.GetPropsToUpdate().Contains(nameof(InternalFunctionContext.Status)) ||
            functionContext.Status is not (TickerStatus.Done or TickerStatus.DueDone or TickerStatus.Failed
                or TickerStatus.Cancelled or TickerStatus.Skipped) ||
            (successful && !functionContext.GetPropsToUpdate().Contains(nameof(InternalFunctionContext.ResultEnvelope))))
            throw new InvalidOperationException(
                "Acknowledged persistence accepts only a terminal mutation; success requires an explicit optional result envelope.");
    }

    private static void ValidateWorkerAndLease(
        string workerId, int maxCount, DateTime nowUtc, DateTime leaseUntilUtc)
    {
        if (string.IsNullOrWhiteSpace(workerId) || workerId.Length > NodeFinalizationClaim.MaxClaimedByLength)
            throw new ArgumentException("Worker ID must be non-empty and bounded.", nameof(workerId));
        if (maxCount <= 0) throw new ArgumentOutOfRangeException(nameof(maxCount));
        if (nowUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("Current time must be UTC.", nameof(nowUtc));
        if (leaseUntilUtc.Kind != DateTimeKind.Utc || leaseUntilUtc <= nowUtc)
            throw new ArgumentException("Lease expiry must be UTC and later than the current time.", nameof(leaseUntilUtc));
    }

    private NodeFinalizationOutboxEntity ToEntity(
        NodeFinalizationIntent intent, byte[] terminalMutationDigest) => new()
    {
        ApplicationNamespaceKey = _runtimePartitionKey,
        OutboxId = intent.OutboxId,
        SchemaVersion = intent.SchemaVersion,
        TickerType = intent.TickerType,
        TickerId = intent.TickerId,
        AcquisitionToken = intent.AcquisitionToken,
        DispatchId = intent.DispatchId,
        NodeEpoch = intent.NodeEpoch,
        FinalizeUri = intent.FinalizeUri,
        FinalizePathAndQuery = intent.FinalizePathAndQuery,
        AllowPrivateCallbackAddressesForLocalDevelopment = intent.AllowPrivateCallbackAddressesForLocalDevelopment,
        RequestNonce = intent.RequestNonce,
        ControlNonce = intent.ControlNonce,
        ExactBody = intent.ExactBody,
        CreatedAtUtcTicks = intent.CreatedAtUtc.Ticks,
        TerminalMutationDigest = terminalMutationDigest,
        AvailableAtUtc = intent.CreatedAtUtc,
        AttemptCount = 0
    };

    private static NodeFinalizationClaim ToClaim(NodeFinalizationOutboxEntity row)
    {
        var intent = new NodeFinalizationIntent(
            row.SchemaVersion, row.OutboxId, row.TickerType, row.TickerId, row.AcquisitionToken,
            row.DispatchId, row.NodeEpoch, row.FinalizeUri, row.FinalizePathAndQuery,
            row.AllowPrivateCallbackAddressesForLocalDevelopment, row.RequestNonce, row.ControlNonce,
            row.ExactBody, new DateTime(row.CreatedAtUtcTicks, DateTimeKind.Utc), row.ApplicationNamespaceKey);
        return new NodeFinalizationClaim(intent, row.ClaimToken!.Value, row.ClaimedBy,
            AsUtc(row.AvailableAtUtc), row.AttemptCount);
    }

    private static DateTime AsUtc(DateTime value)
        => value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static bool ImmutableIntentMatches(
        NodeFinalizationOutboxEntity row, NodeFinalizationIntent intent, byte[] terminalMutationDigest)
        => StringComparer.Ordinal.Equals(row.ApplicationNamespaceKey, intent.RuntimePartitionKey) &&
           row.SchemaVersion == intent.SchemaVersion && row.OutboxId == intent.OutboxId &&
           row.TickerType == intent.TickerType && row.TickerId == intent.TickerId &&
           row.AcquisitionToken == intent.AcquisitionToken && row.DispatchId == intent.DispatchId &&
           row.NodeEpoch == intent.NodeEpoch &&
           string.Equals(row.FinalizeUri, intent.FinalizeUri, StringComparison.Ordinal) &&
           string.Equals(row.FinalizePathAndQuery, intent.FinalizePathAndQuery, StringComparison.Ordinal) &&
           row.AllowPrivateCallbackAddressesForLocalDevelopment == intent.AllowPrivateCallbackAddressesForLocalDevelopment &&
           row.RequestNonce == intent.RequestNonce && row.ControlNonce == intent.ControlNonce &&
           row.ExactBody != null && row.ExactBody.SequenceEqual(intent.ExactBody) &&
           row.CreatedAtUtcTicks == intent.CreatedAtUtc.Ticks &&
           row.TerminalMutationDigest is { Length: 32 } &&
           CryptographicOperations.FixedTimeEquals(row.TerminalMutationDigest, terminalMutationDigest);

    private static byte[] ComputeTerminalMutationDigest(InternalFunctionContext context)
    {
        var properties = context.GetPropsToUpdate();
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(1); // canonical digest schema version
            writer.Write((int)context.Type);
            writer.Write((int)context.Status);

            WriteOptional(writer, properties.Contains(nameof(InternalFunctionContext.ExecutedAt)),
                () => writer.Write(context.ExecutedAt.Ticks));

            var writesException = context.Status == TickerStatus.Skipped ||
                                  properties.Contains(nameof(InternalFunctionContext.ExceptionDetails));
            WriteOptional(writer, writesException, () => WriteNullableString(writer, context.ExceptionDetails));

            WriteOptional(writer, properties.Contains(nameof(InternalFunctionContext.ElapsedTime)),
                () => writer.Write(context.ElapsedTime));
            WriteOptional(writer, properties.Contains(nameof(InternalFunctionContext.RetryCount)),
                () => writer.Write(context.RetryCount));
            writer.Write(properties.Contains(nameof(InternalFunctionContext.ReleaseLock)));

            // Only cron occurrences persist ExecutionTime through this terminal setter.
            WriteOptional(writer,
                context.Type == TickerType.CronTickerOccurrence &&
                properties.Contains(nameof(InternalFunctionContext.ExecutionTime)),
                () => writer.Write(context.ExecutionTime.Ticks));

            var successful = context.Status is TickerStatus.Done or TickerStatus.DueDone;
            writer.Write(successful);
            if (successful)
            {
                var envelope = context.ResultEnvelope;
                writer.Write(envelope != null); // explicit delete/no-result versus exact envelope
                if (envelope != null)
                {
                    writer.Write(envelope.Version);
                    writer.Write(envelope.MediaType);
                    WriteNullableString(writer, envelope.ContractId);
                    WriteNullableString(writer, envelope.ContractType);
                    var payload = envelope.ToPayloadArray();
                    writer.Write(payload.Length);
                    writer.Write(payload);
                }
            }
        }

        return SHA256.HashData(stream.GetBuffer().AsSpan(0, checked((int)stream.Length)));
    }

    private static void WriteOptional(BinaryWriter writer, bool present, Action writeValue)
    {
        writer.Write(present);
        if (present) writeValue();
    }

    private static void WriteNullableString(BinaryWriter writer, string value)
    {
        writer.Write(value != null);
        if (value != null) writer.Write(value);
    }

    private static void ValidateEnvelope(TickerResultEnvelope envelope)
    {
        if (envelope == null)
            return;
        envelope.EnsureSupportedVersion();
        if (envelope.PayloadLength > TickerResultStorage.MaxPayloadBytes)
            throw new ArgumentOutOfRangeException(nameof(envelope), envelope.PayloadLength,
                $"Ticker result payload cannot exceed {TickerResultStorage.MaxPayloadBytes} bytes.");
    }

    private static TickerResultEnvelope ReadEnvelope(
        byte[] payload, int version, string mediaType, string contractId, string contractType)
    {
        if (payload == null || payload.Length > TickerResultStorage.MaxPayloadBytes)
            throw new InvalidOperationException("Persisted ticker result payload is corrupt or exceeds the 1 MiB limit.");
        var envelope = new TickerResultEnvelope(payload, version, mediaType, contractId, contractType);
        envelope.EnsureSupportedVersion();
        return envelope;
    }

    protected async Task<DbContextLease<TDbContext>> CreateDbContextAsync(CancellationToken cancellationToken)
    {
        var lease = await DbContextLease<TDbContext>.CreateAsync(_serviceProvider, cancellationToken)
            .ConfigureAwait(false);
        ConfigureRuntimePartition(lease.Context);
        return lease;
    }

    protected DbContextLease<TDbContext> CreateDbContext()
    {
        var lease = DbContextLease<TDbContext>.Create(_serviceProvider);
        ConfigureRuntimePartition(lease.Context);
        return lease;
    }

    private void ConfigureRuntimePartition(TDbContext context)
    {
        context.ChangeTracker.Tracked += (_, args) =>
        {
            if (args.Entry.State != EntityState.Added ||
                args.Entry.Metadata.FindProperty("ApplicationNamespaceKey") == null)
                return;
            var property = args.Entry.Property("ApplicationNamespaceKey");
            var current = property.CurrentValue as string;
            if (!string.IsNullOrEmpty(current) &&
                !StringComparer.Ordinal.Equals(current, TickerQRuntimePartition.LegacyGlobal.StorageKey) &&
                !StringComparer.Ordinal.Equals(current, _runtimePartitionKey))
                throw new InvalidOperationException(
                    "An EF runtime entity cannot be redirected to a different application partition.");
            property.CurrentValue = _runtimePartitionKey;
        };
    }
    
    #region Core_Time_Ticker_Methods
    public async IAsyncEnumerable<TimeTickerEntity> QueueTimeTickers(TimeTickerEntity[] timeTickers, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Capture caller-owned CAS inputs once. A failed attempt mutates the returned DTOs, but a
        // retry must compare against the original durable timestamp rather than that failed state.
        var expectedUpdatedAt = timeTickers.ToDictionary(x => x.Id, x => x.UpdatedAt);
        var queued = await ExecuteTimeTickerGraphMutationAsync(true,
            new List<TimeTickerEntity>(), async (dbContext, ct) =>
        {
            var context = dbContext.Set<TTimeTicker>();
            var now = _clock.UtcNow;
            var result = new List<TimeTickerEntity>();
            foreach (var timeTicker in timeTickers)
            {
                ct.ThrowIfCancellationRequested();
                var acquisitionToken = Guid.NewGuid();
                var expected = expectedUpdatedAt[timeTicker.Id];
                var updatedTicker = await context
                    .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey && x.Id == timeTicker.Id)
                    .Where(x => x.UpdatedAt == expected)
                    .ExecuteUpdateAsync(prop => prop
                        .SetProperty(x => x.LockHolder, _lockHolder)
                        .SetProperty(x => x.LockedAt, now)
                        .SetProperty(x => x.AcquisitionToken, acquisitionToken)
                        .SetProperty(x => x.ChainRootId, timeTicker.Id)
                        .SetProperty(x => x.ChainGeneration, acquisitionToken)
                        .SetProperty(x => x.UpdatedAt, now)
                        .SetProperty(x => x.Status, TickerStatus.Queued), ct).ConfigureAwait(false);
                if (updatedTicker <= 0) continue;
                timeTicker.UpdatedAt = now;
                timeTicker.LockHolder = _lockHolder;
                timeTicker.LockedAt = now;
                timeTicker.AcquisitionToken = acquisitionToken;
                timeTicker.ChainRootId = timeTicker.Id;
                timeTicker.ChainGeneration = acquisitionToken;
                timeTicker.Status = TickerStatus.Queued;
                StampQueueGeneration(timeTicker, timeTicker.Id, acquisitionToken);
                await PersistQueueGenerationAsync(context, timeTicker, timeTicker.Id, acquisitionToken, ct)
                    .ConfigureAwait(false);
                result.Add(timeTicker);
            }
            return result;
        }, cancellationToken).ConfigureAwait(false);
        foreach (var ticker in queued) yield return ticker;
    }

    public async IAsyncEnumerable<TimeTickerEntity> QueueTimedOutTimeTickers([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var acquired = await ExecuteTimeTickerGraphMutationAsync(true,
            new List<TimeTickerEntity>(), async (dbContext, ct) =>
        {
            var context = dbContext.Set<TTimeTicker>();
            var now = _clock.UtcNow;
            var fallbackThreshold = now.AddSeconds(-1);  // Fallback picks up tasks older than main 1-second window
            var result = new List<TimeTickerEntity>();
            var timeTickersToUpdate = await context
                .AsNoTracking()
                .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey && x.ExecutionTime != null)
                .Where(x => x.Status == TickerStatus.Idle || x.Status == TickerStatus.Queued)
                .Where(x => x.ExecutionTime <= fallbackThreshold)  // Only tasks older than 1 second
                .Include(x => x.Children.Where(y => y.ExecutionTime == null))
                .Select(MappingExtensions.ForQueueTimeTickers<TTimeTicker>())
                .ToArrayAsync(ct).ConfigureAwait(false);

            // Probe-and-extend: the fast projection loads root + child + grandchild
            // (depth 3). If any chain actually goes deeper, BFS the rest in batch so
            // we don't silently truncate. Single EXISTS probe when nothing's deeper,
            // proportional cost only when chains exceed depth 3.
            await ExtendQueueChainsBeyondGrandchildrenAsync(context, timeTickersToUpdate, ct).ConfigureAwait(false);

            foreach (var timeTicker in timeTickersToUpdate)
            {
                ct.ThrowIfCancellationRequested();
                var acquisitionToken = Guid.NewGuid();

                var affected = await context
                    .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey &&
                                x.Id == timeTicker.Id && x.UpdatedAt <= timeTicker.UpdatedAt)
                    .ExecuteUpdateAsync(setter => setter
                        .SetProperty(x => x.LockHolder, _lockHolder)
                        .SetProperty(x => x.LockedAt, now)
                        .SetProperty(x => x.LeaseUntil, NextLeaseUntil(now))
                        .SetProperty(x => x.AcquisitionToken, acquisitionToken)
                        .SetProperty(x => x.ChainRootId, timeTicker.Id)
                        .SetProperty(x => x.ChainGeneration, acquisitionToken)
                        .SetProperty(x => x.UpdatedAt, now)
                        .SetProperty(x => x.Status, TickerStatus.InProgress), ct).ConfigureAwait(false);

                if (affected <= 0)
                    continue;

                timeTicker.AcquisitionToken = acquisitionToken;
                timeTicker.ChainRootId = timeTicker.Id;
                timeTicker.ChainGeneration = acquisitionToken;
                StampQueueGeneration(timeTicker, timeTicker.Id, acquisitionToken);
                await PersistQueueGenerationAsync(
                    context, timeTicker, timeTicker.Id, acquisitionToken, ct).ConfigureAwait(false);
                result.Add(timeTicker);
            }

            return result;
        }, cancellationToken).ConfigureAwait(false);

        foreach (var ticker in acquired) yield return ticker;
    }

    public async Task ReleaseAcquiredTimeTickers(Guid[] timeTickerIds, CancellationToken cancellationToken)
    {
        var ids = timeTickerIds?.Distinct().ToArray() ?? [];
        await ExecuteRunnableAdmissionAsync(0, async (dbContext, ct) =>
        {
            var now = _clock.UtcNow;
            var leaseQuery = dbContext.Set<TTimeTicker>().AsNoTracking()
                .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey &&
                            (x.Status == TickerStatus.Idle || x.Status == TickerStatus.Queued) &&
                            x.LockHolder == _lockHolder && x.AcquisitionToken != null);
            if (ids.Length > 0)
                leaseQuery = leaseQuery.Where(x => ids.Contains(x.Id));
            var leases = await leaseQuery
                .Select(x => new { x.Id, Token = x.AcquisitionToken.Value })
                .ToArrayAsync(ct).ConfigureAwait(false);
            var released = 0;
            foreach (var lease in leases)
                released += await dbContext.Set<TTimeTicker>()
                    .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey && x.Id == lease.Id &&
                                (x.Status == TickerStatus.Idle || x.Status == TickerStatus.Queued) &&
                                x.LockHolder == _lockHolder &&
                                x.AcquisitionToken == lease.Token)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(x => x.LockHolder, (string)null)
                        .SetProperty(x => x.LockedAt, (DateTime?)null)
                        .SetProperty(x => x.LeaseUntil, (DateTime?)null)
                        .SetProperty(x => x.AcquisitionToken, (Guid?)null)
                        .SetProperty(x => x.ChainGeneration, (Guid?)null)
                        .SetProperty(x => x.Status, TickerStatus.Idle)
                        .SetProperty(x => x.UpdatedAt, now), ct).ConfigureAwait(false);
            return released;
        }, cancellationToken).ConfigureAwait(false);
    }

    public Task<int> UpdateTimeTicker(InternalFunctionContext functionContexts, CancellationToken cancellationToken)
    {
        var releasesToIdle = functionContexts.GetPropsToUpdate().Contains(nameof(InternalFunctionContext.Status)) &&
                             functionContexts.Status == TickerStatus.Idle;
        if (!releasesToIdle)
            return UpdateTimeTickerInNewSessionAsync(functionContexts, cancellationToken);
        return UpdateTimeTickerWithAdmissionAsync(functionContexts, cancellationToken);
    }

    private async Task<int> UpdateTimeTickerWithAdmissionAsync(
        InternalFunctionContext functionContext, CancellationToken cancellationToken)
    {
        return await ExecuteTimeTickerGraphMutationAsync(true, 0,
            (dbContext, ct) => UpdateTimeTickerCore(functionContext, dbContext, ct), cancellationToken).ConfigureAwait(false);
    }

    private async Task<int> UpdateTimeTickerInNewSessionAsync(
        InternalFunctionContext functionContext, CancellationToken cancellationToken)
    {
        using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await UpdateTimeTickerCore(functionContext, session.Context, cancellationToken).ConfigureAwait(false);
    }

    private async Task<int> UpdateTimeTickerCore(
        InternalFunctionContext functionContexts, TDbContext dbContext, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;

        // Child mutations and root generation changes use the same root-first lock order.
        // Own a transaction only when the caller/context does not already have one.
        // This keeps the root CAS lock alive through the child update without committing
        // or rolling back transaction state owned by an ambient caller.
        var ownsTransaction = functionContexts.ParentId != null &&
                              dbContext.Database.CurrentTransaction == null;
        await using var transaction = ownsTransaction
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false)
            : null;

        try
        {
            if (functionContexts.ParentId != null &&
                !await LockCurrentChainGenerationAsync(
                    dbContext, functionContexts, cancellationToken).ConfigureAwait(false))
            {
                if (transaction != null)
                    await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return 0;
            }

            var query = dbContext.Set<TTimeTicker>()
                .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey &&
                            x.Id == functionContexts.TickerId);

            query = ApplyTimeTickerGenerationFence(dbContext, query, functionContexts);

            // Fencing: a terminal write from this node must not overwrite a row the
            // stale watchdog already recovered (lock cleared / re-acquired elsewhere).
            // Roots use acquisition ownership below; children are serialized above by
            // the authoritative root generation CAS.
            var writesRootIdle = functionContexts.ParentId == null &&
                                 functionContexts.GetPropsToUpdate().Contains(nameof(InternalFunctionContext.Status)) &&
                                 functionContexts.Status == TickerStatus.Idle;
            if ((IsFencedTerminalWrite(functionContexts) && functionContexts.ParentId == null) || writesRootIdle)
                query = functionContexts.AcquisitionToken.HasValue
                    ? query.Where(x => x.ParentId == null && x.LockHolder == _lockHolder &&
                                       x.AcquisitionToken == functionContexts.AcquisitionToken)
                    : query.Where(_ => false);

            var affected = await query.ExecuteUpdateAsync(
                setter => setter.UpdateTimeTicker<TTimeTicker>(
                    functionContexts, now, NextLeaseUntil(now)), cancellationToken).ConfigureAwait(false);
            if (transaction != null)
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return affected;
        }
        catch
        {
            if (transaction != null)
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private async Task<bool> LockCurrentChainGenerationAsync(
        TDbContext dbContext, InternalFunctionContext context, CancellationToken cancellationToken)
    {
        if (!context.ChainRootId.HasValue || !context.ChainGeneration.HasValue)
            return false;

        var rootId = context.ChainRootId.Value;
        var generation = context.ChainGeneration.Value;
        var affected = await dbContext.Set<TTimeTicker>()
            .Where(root => root.ApplicationNamespaceKey == _runtimePartitionKey &&
                           root.Id == rootId && root.ParentId == null &&
                           root.ChainRootId == root.Id && root.ChainGeneration == generation)
            // A conditional no-op write is intentional: MVCC providers take a row write lock,
            // serializing this child mutation with every root reacquisition.
            .ExecuteUpdateAsync(
                setter => setter.SetProperty(root => root.ChainGeneration, generation),
                cancellationToken).ConfigureAwait(false);
        return affected == 1;
    }

    private IQueryable<TTimeTicker> ApplyTimeTickerGenerationFence(
        TDbContext dbContext, IQueryable<TTimeTicker> query, InternalFunctionContext context)
    {
        if (!context.ChainRootId.HasValue || !context.ChainGeneration.HasValue)
            return context.ParentId == null ? query : query.Where(_ => false);

        var rootId = context.ChainRootId.Value;
        var generation = context.ChainGeneration.Value;
        return query.Where(target => target.ChainRootId == rootId &&
            dbContext.Set<TTimeTicker>().Any(root =>
                root.ApplicationNamespaceKey == _runtimePartitionKey &&
                root.Id == rootId && root.ParentId == null &&
                root.ChainRootId == root.Id && root.ChainGeneration == generation));
    }

    /// <summary>
    /// True when the context writes a terminal status — the writes that must be
    /// discarded if this node lost its lease while paused (fenced on LockHolder).
    /// </summary>
    protected static bool IsFencedTerminalWrite(InternalFunctionContext functionContext)
        => functionContext.GetPropsToUpdate().Contains(nameof(InternalFunctionContext.ReleaseLock)) ||
           (functionContext.GetPropsToUpdate().Contains(nameof(InternalFunctionContext.Status)) &&
            functionContext.Status is TickerStatus.Done or TickerStatus.DueDone or TickerStatus.Failed
                or TickerStatus.Cancelled or TickerStatus.Skipped);
        
    public async Task UpdateTimeTickersWithUnifiedContext(Guid[] timeTickerIds, InternalFunctionContext functionContext, CancellationToken cancellationToken = default)
    {
        var idList = timeTickerIds.ToList();
        var writesIdle = functionContext.GetPropsToUpdate().Contains(nameof(InternalFunctionContext.Status)) &&
                         functionContext.Status == TickerStatus.Idle;
        if (writesIdle)
        {
            await ExecuteTimeTickerGraphMutationAsync(true, 0, async (dbContext, ct) =>
            {
                var query = dbContext.Set<TTimeTicker>().Where(x =>
                    x.ApplicationNamespaceKey == _runtimePartitionKey && idList.Contains(x.Id));
                if (functionContext.ParentId == null)
                {
                    query = functionContext.AcquisitionToken.HasValue
                        ? query.Where(x => x.ParentId == null && x.LockHolder == _lockHolder &&
                                           x.AcquisitionToken == functionContext.AcquisitionToken)
                        : query.Where(_ => false);
                }
                else
                {
                    if (!await LockCurrentChainGenerationAsync(dbContext, functionContext, ct).ConfigureAwait(false))
                        return 0;
                    query = ApplyTimeTickerGenerationFence(dbContext,
                        query.Where(x => x.ParentId != null), functionContext);
                }

                var now = _clock.UtcNow;
                return await query.ExecuteUpdateAsync(
                    setter => setter.UpdateTimeTicker<TTimeTicker>(functionContext, now, NextLeaseUntil(now)), ct)
                    .ConfigureAwait(false);
            }, cancellationToken).ConfigureAwait(false);
            return;
        }

        using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var context = session.Context;
        var updateNow = _clock.UtcNow;
        await context.Set<TTimeTicker>().Where(x =>
                x.ApplicationNamespaceKey == _runtimePartitionKey && idList.Contains(x.Id))
            .ExecuteUpdateAsync(setter => setter.UpdateTimeTicker<TTimeTicker>(
                functionContext, updateNow, NextLeaseUntil(updateNow)), cancellationToken).ConfigureAwait(false);
    }

    public async Task<Guid[]> TransitionQueuedTimeTickersToInProgressAsync(
        IReadOnlyCollection<AcquisitionLease> leases, CancellationToken cancellationToken = default)
    {
        return await ExecuteRunnableAdmissionAsync(Array.Empty<Guid>(), async (dbContext, ct) =>
        {
            var context = dbContext.Set<TTimeTicker>();
            var now = _clock.UtcNow;
            var winners = new List<Guid>(leases.Count);
            foreach (var lease in leases.Where(x => x.AcquisitionToken.HasValue).Distinct())
            {
                var affected = await context
                    .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey &&
                                x.Id == lease.TickerId && x.Status == TickerStatus.Queued &&
                                x.LockHolder == _lockHolder && x.AcquisitionToken == lease.AcquisitionToken)
                    .ExecuteUpdateAsync(setter => setter
                        .SetProperty(x => x.Status, TickerStatus.InProgress)
                        .SetProperty(x => x.LeaseUntil, NextLeaseUntil(now))
                        .SetProperty(x => x.UpdatedAt, now), ct).ConfigureAwait(false);
                if (affected == 1) winners.Add(lease.TickerId);
            }
            return winners.ToArray();
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<TimeTickerEntity[]> GetEarliestTimeTickers(CancellationToken cancellationToken)
    {
        using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var dbContext = session.Context;
        if (!await IsRunnableAdmissionAllowedAsync(dbContext, cancellationToken).ConfigureAwait(false)) return [];
        var now = _clock.UtcNow;
    
        // Define the window: ignore anything older than 1 second ago
        var oneSecondAgo = now.AddSeconds(-1);
    
        var baseQuery = dbContext.Set<TTimeTicker>()
            .AsNoTracking()
            .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey && x.ExecutionTime != null)
            .Where(x => x.ExecutionTime >= oneSecondAgo)  // Ignore old tickers (fallback handles them)
            .WhereCanAcquire(_lockHolder);
    
        // Find the earliest ticker within our window
        var minExecutionTime = await baseQuery
            .OrderBy(x => x.ExecutionTime)
            .Select(x => x.ExecutionTime)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        if (minExecutionTime == null)
            return [];
    
        // Round the minimum execution time down to its second
        var minSecond = new DateTime(minExecutionTime.Value.Year, minExecutionTime.Value.Month,
            minExecutionTime.Value.Day, minExecutionTime.Value.Hour,
            minExecutionTime.Value.Minute, minExecutionTime.Value.Second,
            DateTimeKind.Utc);
    
        // Fetch all tickers within that complete second (this ensures we get all tickers in the same second)
        var maxExecutionTime = minSecond.AddSeconds(1);
    
        var earliest = await baseQuery
            .Include(x => x.Children.Where(y => y.ExecutionTime == null))
            .Where(x => x.ExecutionTime >= minSecond && x.ExecutionTime < maxExecutionTime)
            .OrderBy(x => x.ExecutionTime)
            .Select(MappingExtensions.ForQueueTimeTickers<TTimeTicker>())
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);

        await ExtendQueueChainsBeyondGrandchildrenAsync(dbContext.Set<TTimeTicker>(), earliest, cancellationToken).ConfigureAwait(false);
        foreach (var root in earliest)
            StampQueueGeneration(root, root.Id, root.ChainGeneration);
        return earliest;
    }

    /// <summary>
    /// Probe-and-extend for the queue projection. The fast path
    /// (<see cref="MappingExtensions.ForQueueTimeTickers{TTimeTicker}"/>) loads
    /// root + child + grandchild in one query. Anything below grandchild was
    /// historically dropped — this helper detects that case with a single
    /// EXISTS probe, then walks remaining levels with one batched query per
    /// depth tier (Contains(parentId) ⇒ "WHERE ParentId IN (...)") and stitches
    /// the new nodes into the existing tree via ParentId. Pure LINQ; portable
    /// across SQL Server / PostgreSQL / MySQL / SQLite.
    /// </summary>
    private async Task ExtendQueueChainsBeyondGrandchildrenAsync(
        DbSet<TTimeTicker> context,
        TimeTickerEntity[] roots,
        CancellationToken cancellationToken)
    {
        if (roots == null || roots.Length == 0)
            return;

        // Collect IDs of the deepest layer the fast projection already loaded
        // (grandchildren). Also build a parent-id → node lookup so later levels
        // can attach themselves.
        var leafIds = new List<Guid>();
        var nodesById = new Dictionary<Guid, TimeTickerEntity>();
        foreach (var root in roots)
        {
            if (root.Children == null)
                continue;
            foreach (var child in root.Children)
            {
                if (child.Children == null)
                    continue;
                foreach (var grandchild in child.Children)
                {
                    grandchild.Children ??= new List<TimeTickerEntity>();
                    leafIds.Add(grandchild.Id);
                    nodesById[grandchild.Id] = grandchild;
                }
            }
        }

        if (leafIds.Count == 0)
            return;

        // Cheap EXISTS probe: does anything live below the grandchild layer?
        // 99% of chains stop at depth 3; in that case we pay one extra
        // sub-millisecond query and return.
        var hasDeeper = await context.AsNoTracking()
            .AnyAsync(x => x.ApplicationNamespaceKey == _runtimePartitionKey && x.ParentId.HasValue &&
                           leafIds.Contains(x.ParentId.Value), cancellationToken)
            .ConfigureAwait(false);
        if (!hasDeeper)
            return;

        // BFS extension: one query per remaining depth tier, all roots in batch.
        var frontier = leafIds;
        while (frontier.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var levelNodes = await context.AsNoTracking()
                .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey && x.ParentId.HasValue &&
                            frontier.Contains(x.ParentId.Value))
                .Select(e => new TimeTickerEntity
                {
                    Id = e.Id,
                    Function = e.Function,
                    Retries = e.Retries,
                    RetryIntervals = e.RetryIntervals,
                    TimeoutSeconds = e.TimeoutSeconds,
                    RunCondition = e.RunCondition,
                    ParentId = e.ParentId,
                    ChainRootId = e.ChainRootId,
                    ChainGeneration = e.ChainGeneration,
                    RequestContractVersion = e.RequestContractVersion,
                    RequestContractFingerprint = e.RequestContractFingerprint,
                })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            if (levelNodes.Count == 0)
                break;

            var nextFrontier = new List<Guid>(levelNodes.Count);
            foreach (var node in levelNodes)
            {
                node.Children ??= new List<TimeTickerEntity>();
                if (node.ParentId.HasValue && nodesById.TryGetValue(node.ParentId.Value, out var parent))
                    parent.Children.Add(node);
                nodesById[node.Id] = node;
                nextFrontier.Add(node.Id);
            }
            frontier = nextFrontier;
        }
    }

    private static void StampQueueGeneration(TimeTickerEntity node, Guid rootId, Guid? generation)
    {
        node.ChainRootId = rootId;
        node.ChainGeneration = generation;
        foreach (var child in node.Children ?? [])
            StampQueueGeneration(child, rootId, generation);
    }

    private async Task PersistQueueGenerationAsync(
        DbSet<TTimeTicker> set, TimeTickerEntity root, Guid rootId, Guid generation,
        CancellationToken cancellationToken)
    {
        var descendantIds = new List<Guid>();
        static void Gather(TimeTickerEntity node, ICollection<Guid> ids)
        {
            foreach (var child in node.Children ?? [])
            {
                ids.Add(child.Id);
                Gather(child, ids);
            }
        }

        Gather(root, descendantIds);
        const int batchSize = 500;
        for (var offset = 0; offset < descendantIds.Count; offset += batchSize)
        {
            var batch = descendantIds.Skip(offset).Take(batchSize).ToArray();
            await set.Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey && batch.Contains(x.Id) &&
                                 (x.ChainRootId == null || x.ChainRootId != rootId ||
                                  x.ChainGeneration == null || x.ChainGeneration != generation))
                .ExecuteUpdateAsync(setter => setter
                    .SetProperty(x => x.ChainRootId, rootId)
                    .SetProperty(x => x.ChainGeneration, generation), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Probe-and-extend for the read paths (GetTimeTickerById, GetTimeTickers,
    /// GetTimeTickersPaginated, RemoveTimeTickers). Same shape as
    /// <see cref="ExtendQueueChainsBeyondGrandchildrenAsync"/> but works on the
    /// derived <typeparamref name="TTimeTicker"/> tree returned by EF Include
    /// chains. Caller is expected to have already eager-loaded 2 levels
    /// (Children + ThenInclude(Children)); this helper detects anything below
    /// and BFS-attaches it.
    /// </summary>
    protected async Task ExtendReadChainsBeyondGrandchildrenAsync(
        DbSet<TTimeTicker> context,
        IEnumerable<TTimeTicker> roots,
        CancellationToken cancellationToken)
    {
        if (roots == null)
            return;

        var leafIds = new List<Guid>();
        var nodesById = new Dictionary<Guid, TTimeTicker>();
        foreach (var root in roots)
        {
            if (root.Children == null)
                continue;
            foreach (var child in root.Children)
            {
                if (child.Children == null)
                    continue;
                foreach (var grandchild in child.Children)
                {
                    grandchild.Children ??= new List<TTimeTicker>();
                    leafIds.Add(grandchild.Id);
                    nodesById[grandchild.Id] = grandchild;
                }
            }
        }

        if (leafIds.Count == 0)
            return;

        var hasDeeper = await context.AsNoTracking()
            .AnyAsync(x => x.ApplicationNamespaceKey == _runtimePartitionKey && x.ParentId.HasValue &&
                           leafIds.Contains(x.ParentId.Value), cancellationToken)
            .ConfigureAwait(false);
        if (!hasDeeper)
            return;

        var frontier = leafIds;
        while (frontier.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var levelNodes = await context.AsNoTracking()
                .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey && x.ParentId.HasValue &&
                            frontier.Contains(x.ParentId.Value))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            if (levelNodes.Count == 0)
                break;

            var nextFrontier = new List<Guid>(levelNodes.Count);
            foreach (var node in levelNodes)
            {
                node.Children ??= new List<TTimeTicker>();
                if (node.ParentId.HasValue && nodesById.TryGetValue(node.ParentId.Value, out var parent))
                    parent.Children.Add(node);
                nodesById[node.Id] = node;
                nextFrontier.Add(node.Id);
            }
            frontier = nextFrontier;
        }
    }
    
    public async Task<byte[]> GetTimeTickerRequest(Guid tickerId, CancellationToken cancellationToken = default)
    {
        using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var dbContext = session.Context;
        return await dbContext.Set<TTimeTicker>()
            .AsNoTracking()
            .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey && x.Id == tickerId)
            .Select(x => x.Request)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
    }
    
    public async Task ReleaseDeadNodeTimeTickerResources(string instanceIdentifier, CancellationToken cancellationToken = default)
    {
        var acknowledged = await ExecuteTimeTickerGraphMutationAsync(true, -1, async (dbContext, ct) =>
        {
            var now = _clock.UtcNow;
            var leases = await dbContext.Set<TTimeTicker>().AsNoTracking()
                .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey &&
                            (x.Status == TickerStatus.Idle || x.Status == TickerStatus.Queued ||
                             x.Status == TickerStatus.InProgress) &&
                            x.LockHolder == instanceIdentifier && x.AcquisitionToken != null)
                .Select(x => new
                {
                    x.Id, x.ParentId, x.ChainRootId, x.ChainGeneration,
                    Token = x.AcquisitionToken.Value
                })
                .ToArrayAsync(ct).ConfigureAwait(false);
            var released = 0;
            foreach (var lease in leases)
            {
                var query = dbContext.Set<TTimeTicker>()
                    .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey && x.Id == lease.Id &&
                                (x.Status == TickerStatus.Idle || x.Status == TickerStatus.Queued ||
                                 x.Status == TickerStatus.InProgress) &&
                                x.LockHolder == instanceIdentifier &&
                                x.AcquisitionToken == lease.Token);
                if (lease.ParentId.HasValue)
                {
                    if (!lease.ChainRootId.HasValue || !lease.ChainGeneration.HasValue)
                        continue;
                    var chainContext = new InternalFunctionContext
                    {
                        ParentId = lease.ParentId,
                        ChainRootId = lease.ChainRootId,
                        ChainGeneration = lease.ChainGeneration
                    };
                    if (!await LockCurrentChainGenerationAsync(dbContext, chainContext, ct).ConfigureAwait(false))
                        continue;
                    query = query.Where(x => x.ParentId != null &&
                        x.ChainRootId == lease.ChainRootId && x.ChainGeneration == lease.ChainGeneration);
                }
                else
                {
                    query = query.Where(x => x.ParentId == null);
                }

                released += await query.ExecuteUpdateAsync(setter => setter
                        .SetProperty(x => x.LockHolder, (string)null)
                        .SetProperty(x => x.LockedAt, (DateTime?)null)
                        .SetProperty(x => x.LeaseUntil, (DateTime?)null)
                        .SetProperty(x => x.AcquisitionToken, (Guid?)null)
                        .SetProperty(x => x.ChainGeneration, (Guid?)null)
                        .SetProperty(x => x.Status, TickerStatus.Idle)
                        .SetProperty(x => x.UpdatedAt, now), ct).ConfigureAwait(false);
            }
            return released;
        }, cancellationToken).ConfigureAwait(false);
        if (acknowledged < 0)
            throw new InvalidOperationException(
                "Dead-node TimeTicker cleanup was denied because this scheduler epoch is not activated.");
    }
    #endregion

    public async Task<TimeTickerEntity[]> AcquireImmediateTimeTickersAsync(Guid[] ids, CancellationToken cancellationToken = default)
    {
        if (ids == null || ids.Length == 0)
            return [];
        var requestedIds = ids.Distinct().ToArray();
        return await ExecuteTimeTickerGraphMutationAsync(true, Array.Empty<TimeTickerEntity>(),
            async (dbContext, ct) =>
            {
                var now = _clock.UtcNow;
                var acquisitionToken = Guid.NewGuid();
                var acquiredIds = new List<Guid>(requestedIds.Length);
                foreach (var id in requestedIds)
                {
                    var affected = await dbContext.Set<TTimeTicker>()
                        .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey && x.Id == id)
                        .WhereCanAcquire(_lockHolder)
                        .ExecuteUpdateAsync(setter => setter
                            .SetProperty(x => x.LockHolder, _lockHolder)
                            .SetProperty(x => x.LockedAt, now)
                            .SetProperty(x => x.LeaseUntil, NextLeaseUntil(now))
                            .SetProperty(x => x.AcquisitionToken, acquisitionToken)
                            .SetProperty(x => x.ChainRootId, id)
                            .SetProperty(x => x.ChainGeneration, acquisitionToken)
                            .SetProperty(x => x.Status, TickerStatus.InProgress)
                            .SetProperty(x => x.UpdatedAt, now),
                            ct)
                        .ConfigureAwait(false);

                    if (affected == 1)
                        acquiredIds.Add(id);
                }

                var attemptAcquiredIds = acquiredIds.ToArray();
                if (attemptAcquiredIds.Length == 0)
                    return [];

                var acquired = await dbContext.Set<TTimeTicker>()
                    .AsNoTracking()
                    .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey &&
                                attemptAcquiredIds.Contains(x.Id) && x.AcquisitionToken == acquisitionToken)
                    .Include(x => x.Children.Where(y => y.ExecutionTime == null))
                    .Select(MappingExtensions.ForQueueTimeTickers<TTimeTicker>())
                    .ToArrayAsync(ct)
                    .ConfigureAwait(false);

                await ExtendQueueChainsBeyondGrandchildrenAsync(
                        dbContext.Set<TTimeTicker>(), acquired, ct)
                    .ConfigureAwait(false);
                foreach (var root in acquired)
                {
                    StampQueueGeneration(root, root.Id, root.ChainGeneration);
                    await PersistQueueGenerationAsync(dbContext.Set<TTimeTicker>(), root, root.Id,
                        root.ChainGeneration!.Value, ct).ConfigureAwait(false);
                }

                ct.ThrowIfCancellationRequested();
                return acquired;
            }, cancellationToken).ConfigureAwait(false);
    }
    public async Task<TimeTickerEntity> AcquireTimeTickerOnDemandAsync(
        Guid id, DateTime executionTime, CancellationToken cancellationToken = default)
    {
        const int maxSerializationAttempts = 5;

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await ExecuteTimeTickerGraphMutationAsync(true, (TimeTickerEntity)null,
                    AcquireOnceAsync, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (
                attempt < maxSerializationAttempts && IsSerializationFailure(exception))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(TimeSpan.FromMilliseconds(10 * attempt), cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        async Task<TimeTickerEntity> AcquireOnceAsync(TDbContext dbContext, CancellationToken ct)
        {
                    var now = _clock.UtcNow;
                    var acquisitionToken = Guid.NewGuid();
                    var affected = await dbContext.Set<TTimeTicker>()
                        .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey && x.Id == id)
                        .Where(x => x.Status == TickerStatus.Idle ||
                                    (x.Status == TickerStatus.Queued &&
                                     (x.LockHolder == null || x.LockHolder == _lockHolder)) ||
                                    x.Status == TickerStatus.Done ||
                                    x.Status == TickerStatus.DueDone ||
                                    x.Status == TickerStatus.Failed ||
                                    x.Status == TickerStatus.Cancelled ||
                                    x.Status == TickerStatus.Skipped)
                        .ExecuteUpdateAsync(setter => setter
                            .SetProperty(x => x.ExecutionTime, executionTime)
                            .SetProperty(x => x.Status, TickerStatus.InProgress)
                            .SetProperty(x => x.LockHolder, _lockHolder)
                            .SetProperty(x => x.LockedAt, now)
                            .SetProperty(x => x.LeaseUntil, NextLeaseUntil(now))
                            .SetProperty(x => x.AcquisitionToken, acquisitionToken)
                            .SetProperty(x => x.ChainRootId, id)
                            .SetProperty(x => x.ChainGeneration, acquisitionToken)
                            .SetProperty(x => x.RetryCount, 0)
                            .SetProperty(x => x.ExceptionMessage, (string)null)
                            .SetProperty(x => x.SkippedReason, (string)null)
                            .SetProperty(x => x.ExecutedAt, (DateTime?)null)
                            .SetProperty(x => x.ElapsedTime, 0L)
                            .SetProperty(x => x.StaleRestartCount, 0)
                            .SetProperty(x => x.UpdatedAt, now), ct)
                        .ConfigureAwait(false);

                    if (affected != 1)
                        return null;

                    // Revival starts a new generation. Remove the previous successful generation's
                    // output in this same transaction so a crash/retry cannot leak stale parent data.
                    await dbContext.Set<TimeTickerResultEntity<TTimeTicker>>()
                        .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey && x.TickerId == id)
                        .ExecuteDeleteAsync(ct).ConfigureAwait(false);

                    var result = await dbContext.Set<TTimeTicker>()
                        .AsNoTracking()
                        .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey && x.Id == id &&
                                    x.AcquisitionToken == acquisitionToken)
                        .Include(x => x.Children.Where(y => y.ExecutionTime == null))
                        .Select(MappingExtensions.ForQueueTimeTickers<TTimeTicker>())
                        .SingleAsync(ct)
                        .ConfigureAwait(false);
                    await ExtendQueueChainsBeyondGrandchildrenAsync(
                        dbContext.Set<TTimeTicker>(), [result], ct).ConfigureAwait(false);
                    StampQueueGeneration(result, result.Id, result.ChainGeneration);
                    await PersistQueueGenerationAsync(dbContext.Set<TTimeTicker>(), result, result.Id,
                        result.ChainGeneration!.Value, ct).ConfigureAwait(false);
                    return result;
        }
    }

    private static bool IsSerializationFailure(Exception exception)
    {
        for (var current = exception; current != null; current = current.InnerException)
            if (current is DbException { SqlState: "40001" })
                return true;
        return false;
    }
        
    #region Core_Cron_Ticker_Methods
    public Task MigrateDefinedCronTickers((string Function, string Expression)[] cronTickers, CancellationToken cancellationToken = default)
        => MigrateDefinedCronTickers(
            Array.ConvertAll(cronTickers, static ticker => new DefinedCronTickerSeed(ticker.Function, ticker.Expression)),
            cancellationToken);

    public Task MigrateDefinedCronTickers(DefinedCronTickerSeed[] cronTickers, CancellationToken cancellationToken = default)
        => MigrateDefinedCronTickers(new DefinedCronSeedManifest(cronTickers), cancellationToken);

    public async Task MigrateDefinedCronTickers(DefinedCronSeedManifest manifest, CancellationToken cancellationToken = default)
    {
        RuntimeManifestAdmission.Validate(manifest, _hasRuntimeActivationScopeBinding,
            _runtimeSchedulerEnabled, _runtimeActivationScopeKey, _runtimeActivationEpoch);
        using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var dbContext = session.Context;
        var now = _clock.UtcNow;

        var grace = _schedulerOptions.DefinedCronRetirementGracePeriod;
        var functions = manifest.DesiredSeedFunctions.ToList();
        // Blocked seeds (canSeed == false) are present in the manifest but excluded from the desired set,
        // so they are handled by the retirement path below and retired IMMEDIATELY (no grace) because
        // continuing to schedule an unsatisfiable request is unsafe.
        // Use List<string> instead of HashSet<string> for broader EF Core provider compatibility
        // (some providers like Devart MySQL don't assign type mappings to HashSet parameters).
        var blockedFunctions = manifest.Seeds.Where(s => !s.CanSeed).Select(s => s.Function).ToList();
        var cronSet = dbContext.Set<TCronTicker>();

        // Retirement + adoption + upsert commit together in a single SaveChanges (one transaction), so a
        // reconcile pass is atomic. The whole pass is retried on a unique/primary-key race: a concurrent
        // node may have inserted the same deterministic row (or the SeedKey index may reject a duplicate);
        // we re-read and converge. After the final attempt the DbUpdateException is NOT swallowed — it
        // propagates so a genuine schema/constraint failure surfaces rather than being masked.
        const int maxAttempts = 5;
        for (var attempt = 1; ; attempt++)
        {
            dbContext.ChangeTracker.Clear();
            await using var transaction = await dbContext.Database
                .BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
            if (!await LockLegacyWriteAdmissionAsync(dbContext, cancellationToken).ConfigureAwait(false))
                throw new InvalidOperationException(
                    "Legacy runtime writes are fenced after legacy partition adoption begins.");
            var cronIdsRequiringPendingCleanup = new HashSet<Guid>();

            // Phase A — non-destructive retirement of seeded rows no longer desired. Compared to the
            // DESIRED SEED MANIFEST (never the global runtime registry): comparing to the registry
            // conflated "function still registered" with "code still wants a seeded schedule", so removing
            // only a cron expression left the stale seeded row firing forever (Slice 1). A blocked seed
            // retires immediately; an absent seed honors the grace window. Rows and their occurrences are
            // NEVER deleted here — retention owns history. Narrowing to seeded rows (non-empty
            // InitIdentifier) protects dashboard/user rows (including SDK/remote `name@node` functions the
            // initializer never seeds); they carry a null/empty seed identity and are never candidates.
            var allSeeded = await cronSet
                .Where(c => c.ApplicationNamespaceKey == _runtimePartitionKey &&
                            !string.IsNullOrEmpty(c.InitIdentifier))
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
                    var legacy = allSeeded.Where(x => CronSeedIdentity.CanonicallyEquals(x.Function, functionGroup.Key)
                        && x.SeedOwnerNamespace == null
                        && (x.SeedKey == null || CronSeedIdentity.CanonicallyEquals(x.SeedKey, x.Function)
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

            var orphaned = allSeeded.Where(c => manifest.IsLegacyGlobal
                ? !functions.Contains(c.Function)
                : CronSeedIdentity.CanonicallyEquals(c.SeedOwnerNamespace, manifest.ApplicationNamespace)
                  && manifest.IsOrphanedSeedKey(c.SeedKey)).ToList();

            foreach (var row in orphaned)
            {
                var immediate = blockedFunctions.Any(function =>
                    CronSeedIdentity.CanonicallyEquals(function, row.Function));
                if (CronSeedRetirement.ApplyRetirement(row, now, grace, immediate))
                    row.UpdatedAt = now;
                if (row.RetiredAt.HasValue)
                    cronIdsRequiringPendingCleanup.Add(row.Id);
            }

            // Phase B — reconcile desired seeds into code-owned rows keyed by the stable SeedKey. Matching
            // is restricted to seeded rows (non-empty InitIdentifier) so a user/dashboard row sharing a
            // function name — carrying a null/non-seed identity — is never matched, expression-overwritten,
            // or identity-stamped. Ownership rules:
            //   * A legacy seeded row (null SeedKey) adopts its SeedKey IN PLACE — its primary key, which
            //     occurrences reference, never changes.
            //   * A brand-new row uses the deterministic id derived from the SeedKey so concurrent
            //     first-time reconciles converge on one row via a primary-key collision.
            //   * Duplicate legacy rows: a deterministic canonical row (lowest id) adopts the SeedKey and
            //     stays enabled; every redundant duplicate is disabled and marked retired IN PLACE (kept
            //     null-keyed so the unique SeedKey index stays satisfied) but never deleted — exactly one
            //     enabled code-owned row per SeedKey results.
            //   * A desired seed that (re)appeared has any framework retirement state cleared, restoring
            //     only a framework-disabled row (a user-disabled row stays disabled).
            var existing = allSeeded.Where(c => manifest.IsLegacyGlobal
                ? functions.Contains(c.Function)
                : functions.Any(function => CronSeedIdentity.CanonicallyEquals(c.Function, function))).ToList();

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
                var acceptedSeedKeys = manifest.IsLegacyGlobal
                    ? Array.Empty<string>()
                    : CronSeedIdentity.AcceptedSeedKeys(
                        manifest.ApplicationNamespace, seed.StableDefinitionId);

                var group = manifest.IsLegacyGlobal
                    ? (byFunction.TryGetValue(seed.Function, out var legacyGroup) ? legacyGroup : null)
                    : existing.Where(x =>
                            (acceptedSeedKeys.Contains(x.SeedKey, StringComparer.Ordinal)
                             && CronSeedIdentity.CanonicallyEquals(x.SeedOwnerNamespace, manifest.ApplicationNamespace))
                            || (CronSeedIdentity.CanonicallyEquals(x.Function, seed.Function) && x.SeedOwnerNamespace == null
                                && manifest.MayAdoptLegacy(seed.Function)
                                && (x.SeedKey == null || CronSeedIdentity.CanonicallyEquals(x.SeedKey, x.Function)
                                    || documentedLegacyKeys.Contains(x.SeedKey, StringComparer.Ordinal))))
                        .OrderBy(x => x.Id).ToList();
                if (group is { Count: > 0 })
                {
                    // Prefer the row that already owns this SeedKey, then an active row, then lowest id —
                    // picking the lowest id blindly would re-assign an already-owned SeedKey to a legacy
                    // null-key duplicate and violate the unique SeedKey index.
                    var (cron, duplicates) = CronSeedCanonical.Select(group, seedKey);
                    var changed = false;
                    var definitionChanged = false;

                    // Adopt the stable SeedKey onto a legacy row IN PLACE (never re-keys the row).
                    if (cron.SeedKey == null)
                    {
                        cron.SeedKey = seedKey;
                        changed = true;
                    }
                    else if (!manifest.IsLegacyGlobal && cron.SeedKey != seedKey)
                    {
                        cron.SeedKey = seedKey;
                        changed = true;
                    }
                    if (!manifest.IsLegacyGlobal && cron.SeedOwnerNamespace != manifest.ApplicationNamespace)
                    {
                        cron.SeedOwnerNamespace = manifest.ApplicationNamespace;
                        changed = true;
                    }

                    if (!string.Equals(cron.Expression, seed.Expression, StringComparison.Ordinal))
                    {
                        cron.Expression = seed.Expression;
                        changed = true;
                        definitionChanged = true;
                    }

                    // Reconcile the authoritative contract identity onto seeded rows only.
                    if (!string.IsNullOrEmpty(cron.InitIdentifier)
                        && !seed.MatchesIdentity(cron.RequestContractVersion, cron.RequestContractFingerprint))
                    {
                        cron.RequestContractVersion = seed.RequestContractVersion;
                        cron.RequestContractFingerprint = seed.RequestContractFingerprint;
                        changed = true;
                        definitionChanged = true;
                    }

                    if (cron.Retries != seed.Retries)
                    {
                        cron.Retries = seed.Retries;
                        changed = true;
                        definitionChanged = true;
                    }
                    if (!(cron.RetryIntervals ?? Array.Empty<int>()).SequenceEqual(
                            seed.RetryIntervals ?? Array.Empty<int>()))
                    {
                        cron.RetryIntervals = seed.RetryIntervals;
                        changed = true;
                        definitionChanged = true;
                    }
                    if (cron.TimeoutSeconds != seed.TimeoutSeconds)
                    {
                        cron.TimeoutSeconds = seed.TimeoutSeconds;
                        changed = true;
                        definitionChanged = true;
                    }

                    if (definitionChanged)
                    {
                        cron.DefinitionRevision = Math.Max(1, cron.DefinitionRevision + 1);
                        changed = true;
                    }
                    else if (cron.DefinitionRevision <= 0)
                    {
                        cron.DefinitionRevision = 1;
                        changed = true;
                    }

                    // Desired-active seed: clear any framework retirement, restoring only framework-disabled state.
                    if (CronSeedRetirement.ClearRetirement(cron))
                        changed = true;

                    // Advisory heartbeat for retirement grace accounting.
                    cron.SeedLastSeenAt = now;

                    if (changed)
                        cron.UpdatedAt = now;
                    if (definitionChanged)
                        cronIdsRequiringPendingCleanup.Add(cron.Id);

                    // Retire redundant duplicates in place (canonical already chosen); never delete.
                    foreach (var dup in duplicates)
                    {
                        if (CronSeedRetirement.RetireDuplicate(dup, now))
                            dup.UpdatedAt = now;
                        if (dup.RetiredAt.HasValue)
                            cronIdsRequiringPendingCleanup.Add(dup.Id);
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
                    await cronSet.AddAsync(entity, cancellationToken).ConfigureAwait(false);
                }
            }

            try
            {
                await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                if (cronIdsRequiringPendingCleanup.Count > 0)
                {
                    await dbContext.Set<CronTickerOccurrenceEntity<TCronTicker>>()
                        .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey &&
                                    cronIdsRequiringPendingCleanup.Contains(x.CronTickerId))
                        .Where(x => x.Status == TickerStatus.Idle || x.Status == TickerStatus.Queued)
                        .Where(x => x.LockHolder == null || x.LockHolder == string.Empty)
                        .Where(x => x.AcquisitionToken == null)
                        .Where(x => x.LeaseUntil == null || x.LeaseUntil <= now)
                        .ExecuteUpdateAsync(setter => setter
                            .SetProperty(x => x.Status, TickerStatus.Skipped)
                            .SetProperty(x => x.SkippedReason,
                                "Quarantined because its Cron definition revision is stale after reconciliation.")
                            .SetProperty(x => x.ExecutedAt, x => x.ExecutedAt ?? now)
                            .SetProperty(x => x.LockHolder, (string)null)
                            .SetProperty(x => x.LockedAt, (DateTime?)null)
                            .SetProperty(x => x.LeaseUntil, (DateTime?)null)
                            .SetProperty(x => x.AcquisitionToken, (Guid?)null)
                            .SetProperty(x => x.UpdatedAt, now), cancellationToken)
                        .ConfigureAwait(false);
                }
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                break;
            }
            catch (DbUpdateException) when (attempt < maxAttempts)
            {
                // Lost a unique/primary-key race with a concurrent reconcile. Re-read and converge.
            }
        }

        // A converged reconcile can add/update/retire seeded rows and so alter the visible cron
        // definitions. Invalidate this partition's "cron:expressions" cache exactly like Insert/Update/Delete —
        // only after a successful SaveChanges (a failed reconcile threw above and never reaches here, so a
        // stale reconcile never drops the coherent cached view).
        if (RedisContext.HasRedisConnection)
            await RedisContext.DistributedCache.RemoveAsync(CronExpressionsCacheKey, cancellationToken).ConfigureAwait(false);
    }
        
    public async Task<CronTickerEntity[]> GetAllCronTickerExpressions(CancellationToken cancellationToken = default)
    {
        var result = await RedisContext.GetOrSetArrayAsync(
            cacheKey: CronExpressionsCacheKey,
            factory: async (ct) =>
            {
                using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
                var dbContext = session.Context;
                return await dbContext.Set<TCronTicker>()
                    .AsNoTracking()
                    .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey &&
                                x.IsEnabled && !x.IsSystemPaused)
                    .Select(MappingExtensions.ForCronTickerExpressions<CronTickerEntity>())
                    .ToArrayAsync(ct)
                    .ConfigureAwait(false);
            },
            expiration: TimeSpan.FromMinutes(10),
            cancellationToken: cancellationToken);

        if (result != null)
            return result;

        using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var dbContext = session.Context;
        return await dbContext.Set<TCronTicker>()
            .AsNoTracking()
            .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey && x.IsEnabled)
            .Select(MappingExtensions.ForCronTickerExpressions<CronTickerEntity>())
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
    }
    #endregion

    #region Core_Cron_TickerOccurrence_Methods
    public Task UpdateCronTickerOccurrence(InternalFunctionContext functionContext, CancellationToken cancellationToken)
    {
        var releasesToIdle = functionContext.GetPropsToUpdate().Contains(nameof(InternalFunctionContext.Status)) &&
                             functionContext.Status == TickerStatus.Idle;
        return releasesToIdle
            ? UpdateCronTickerOccurrenceWithAdmissionAsync(functionContext, cancellationToken)
            : UpdateCronTickerOccurrenceInNewSessionAsync(functionContext, cancellationToken);
    }

    private async Task UpdateCronTickerOccurrenceWithAdmissionAsync(
        InternalFunctionContext functionContext, CancellationToken cancellationToken)
    {
        await ExecuteRunnableAdmissionAsync(0, async (dbContext, ct) =>
        {
            await UpdateCronTickerOccurrenceCore(functionContext, dbContext, ct).ConfigureAwait(false);
            return 1;
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task UpdateCronTickerOccurrenceInNewSessionAsync(
        InternalFunctionContext functionContext, CancellationToken cancellationToken)
    {
        using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await UpdateCronTickerOccurrenceCore(functionContext, session.Context, cancellationToken).ConfigureAwait(false);
    }

    private async Task UpdateCronTickerOccurrenceCore(
        InternalFunctionContext functionContext, TDbContext dbContext, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;

        var query = dbContext.Set<CronTickerOccurrenceEntity<TCronTicker>>()
            .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey &&
                        x.Id == functionContext.TickerId);

        // Fencing: see UpdateTimeTicker. Occurrences are always lock-held by the
        // executing node, so every terminal write is fenced.
        var writesIdle = functionContext.GetPropsToUpdate().Contains(nameof(InternalFunctionContext.Status)) &&
                         functionContext.Status == TickerStatus.Idle;
        if (IsFencedTerminalWrite(functionContext) || writesIdle)
            query = functionContext.AcquisitionToken.HasValue
                ? query.Where(x => x.LockHolder == _lockHolder &&
                                   x.AcquisitionToken == functionContext.AcquisitionToken)
                : query.Where(_ => false);

        await query
            .ExecuteUpdateAsync(setter => setter.UpdateCronTickerOccurrence<TCronTicker>(functionContext, NextLeaseUntil(now)), cancellationToken)
            .ConfigureAwait(false);
    }
    
    public async IAsyncEnumerable<CronTickerOccurrenceEntity<TCronTicker>> QueueTimedOutCronTickerOccurrences([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var acquired = await ExecuteRunnableAdmissionAsync(
            new List<CronTickerOccurrenceEntity<TCronTicker>>(), async (dbContext, ct) =>
        {
            var context = dbContext.Set<CronTickerOccurrenceEntity<TCronTicker>>();
            var now = _clock.UtcNow;
            var fallbackThreshold = now.AddSeconds(-1);  // Fallback picks up tasks older than main 1-second window
            var result = new List<CronTickerOccurrenceEntity<TCronTicker>>();

            // A stale revision can never be restarted under the current definition. Once it is unleased,
            // quarantine it as durable evidence instead of leaving an Idle row stranded forever.
            await context
                .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey &&
                            x.DefinitionRevision != x.CronTicker.DefinitionRevision)
                .Where(x => x.Status == TickerStatus.Idle || x.Status == TickerStatus.Queued)
                .Where(x => x.LockHolder == null || x.LockHolder == string.Empty)
                .Where(x => x.AcquisitionToken == null)
                .Where(x => x.LeaseUntil == null || x.LeaseUntil <= now)
                .ExecuteUpdateAsync(setter => setter
                    .SetProperty(x => x.Status, TickerStatus.Skipped)
                    .SetProperty(x => x.SkippedReason,
                        "Quarantined because its Cron definition revision is stale during timed-out recovery.")
                    .SetProperty(x => x.ExecutedAt, now)
                    .SetProperty(x => x.UpdatedAt, now), ct)
                .ConfigureAwait(false);

            var cronTickersToUpdate = await context
                .AsNoTracking()
                .Include(x => x.CronTicker)
                .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey &&
                            x.DefinitionRevision == x.CronTicker.DefinitionRevision)
                .Where(x => x.Status == TickerStatus.Idle || x.Status == TickerStatus.Queued)
                .Where(x => x.ExecutionTime <= fallbackThreshold)  // Only tasks older than 1 second
                .Select(MappingExtensions.ForQueueCronTickerOccurrence<CronTickerOccurrenceEntity<TCronTicker>, TCronTicker>())
                .ToArrayAsync(ct).ConfigureAwait(false);

            foreach (var cronTickerOccurrence in cronTickersToUpdate)
            {
                ct.ThrowIfCancellationRequested();
                var acquisitionToken = Guid.NewGuid();

                var affected = await context
                    .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey &&
                                x.Id == cronTickerOccurrence.Id && x.UpdatedAt == cronTickerOccurrence.UpdatedAt)
                    .Where(x => x.DefinitionRevision == x.CronTicker.DefinitionRevision)
                    .ExecuteUpdateAsync(setter => setter
                        .SetProperty(x => x.LockHolder, _lockHolder)
                        .SetProperty(x => x.LockedAt, now)
                        .SetProperty(x => x.LeaseUntil, NextLeaseUntil(now))
                        .SetProperty(x => x.AcquisitionToken, acquisitionToken)
                        .SetProperty(x => x.UpdatedAt, now)
                        .SetProperty(x => x.Status, TickerStatus.InProgress), ct)
                    .ConfigureAwait(false);

                if (affected <= 0)
                    continue;

                cronTickerOccurrence.AcquisitionToken = acquisitionToken;
                result.Add(cronTickerOccurrence);
            }

            return result;
        }, cancellationToken).ConfigureAwait(false);

        foreach (var occurrence in acquired) yield return occurrence;
    }
    
    public async Task ReleaseDeadNodeOccurrenceResources(string instanceIdentifier, CancellationToken cancellationToken = default)
    {
        var acknowledged = await ExecuteRunnableAdmissionAsync(-1, async (dbContext, ct) =>
        {
            var now = _clock.UtcNow;
            var leases = await dbContext.Set<CronTickerOccurrenceEntity<TCronTicker>>().AsNoTracking()
                .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey &&
                            (x.Status == TickerStatus.Idle || x.Status == TickerStatus.Queued ||
                             x.Status == TickerStatus.InProgress) &&
                            x.LockHolder == instanceIdentifier && x.AcquisitionToken != null)
                .Select(x => new { x.Id, Token = x.AcquisitionToken.Value })
                .ToArrayAsync(ct).ConfigureAwait(false);
            var released = 0;
            foreach (var lease in leases)
                released += await dbContext.Set<CronTickerOccurrenceEntity<TCronTicker>>()
                    .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey && x.Id == lease.Id &&
                                (x.Status == TickerStatus.Idle || x.Status == TickerStatus.Queued ||
                                 x.Status == TickerStatus.InProgress) &&
                                x.LockHolder == instanceIdentifier &&
                                x.AcquisitionToken == lease.Token)
                    .ExecuteUpdateAsync(setter => setter
                        .SetProperty(x => x.LockHolder, (string)null)
                        .SetProperty(x => x.LockedAt, (DateTime?)null)
                        .SetProperty(x => x.LeaseUntil, (DateTime?)null)
                        .SetProperty(x => x.AcquisitionToken, (Guid?)null)
                        .SetProperty(x => x.Status, TickerStatus.Idle)
                        .SetProperty(x => x.UpdatedAt, now), ct).ConfigureAwait(false);
            return released;
        }, cancellationToken).ConfigureAwait(false);
        if (acknowledged < 0)
            throw new InvalidOperationException(
                "Dead-node Cron occurrence cleanup was denied because this scheduler epoch is not activated.");
    }
    
    public async Task ReleaseAcquiredCronTickerOccurrences(Guid[] occurrenceIds, CancellationToken cancellationToken = default)
    {
        var ids = occurrenceIds?.Distinct().ToArray() ?? [];
        await ExecuteRunnableAdmissionAsync(0, async (dbContext, ct) =>
        {
            var now = _clock.UtcNow;
            var leaseQuery = dbContext.Set<CronTickerOccurrenceEntity<TCronTicker>>().AsNoTracking()
                .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey &&
                            (x.Status == TickerStatus.Idle || x.Status == TickerStatus.Queued) &&
                            x.LockHolder == _lockHolder && x.AcquisitionToken != null);
            if (ids.Length > 0)
                leaseQuery = leaseQuery.Where(x => ids.Contains(x.Id));
            var leases = await leaseQuery
                .Select(x => new { x.Id, Token = x.AcquisitionToken.Value })
                .ToArrayAsync(ct).ConfigureAwait(false);
            var released = 0;
            foreach (var lease in leases)
                released += await dbContext.Set<CronTickerOccurrenceEntity<TCronTicker>>()
                    .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey && x.Id == lease.Id &&
                                (x.Status == TickerStatus.Idle || x.Status == TickerStatus.Queued) &&
                                x.LockHolder == _lockHolder &&
                                x.AcquisitionToken == lease.Token)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(x => x.LockHolder, (string)null)
                        .SetProperty(x => x.LockedAt, (DateTime?)null)
                        .SetProperty(x => x.LeaseUntil, (DateTime?)null)
                        .SetProperty(x => x.AcquisitionToken, (Guid?)null)
                        .SetProperty(x => x.Status, TickerStatus.Idle)
                        .SetProperty(x => x.UpdatedAt, now), ct).ConfigureAwait(false);
            return released;
        }, cancellationToken).ConfigureAwait(false);
    }
    
    public async IAsyncEnumerable<CronTickerOccurrenceEntity<TCronTicker>> QueueCronTickerOccurrences((DateTime Key, InternalManagerContext[] Items) cronTickerOccurrences, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var item in cronTickerOccurrences.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var published = await ExecuteRunnableAdmissionAsync(
                (CronTickerOccurrenceEntity<TCronTicker>)null,
                (dbContext, ct) => PublishCronOccurrenceAsync(
                    dbContext, cronTickerOccurrences.Key, item, _clock.UtcNow, ct),
                cancellationToken).ConfigureAwait(false);
            if (published != null)
                yield return published;
        }
    }

    private async Task<CronTickerOccurrenceEntity<TCronTicker>> PublishCronOccurrenceAsync(
        TDbContext dbContext, DateTime executionTime, InternalManagerContext item, DateTime now,
        CancellationToken cancellationToken)
    {
        var context = dbContext.Set<CronTickerOccurrenceEntity<TCronTicker>>();
        var acquisitionToken = Guid.NewGuid();
        if (item.NextCronOccurrence is null)
        {
            var isCurrentDefinition = await dbContext.Set<TCronTicker>().AsNoTracking()
                .AnyAsync(x => x.ApplicationNamespaceKey == _runtimePartitionKey &&
                               x.Id == item.Id && x.DefinitionRevision == item.DefinitionRevision,
                    cancellationToken).ConfigureAwait(false);
            if (!isCurrentDefinition) return null;

            var occurrence = new CronTickerOccurrenceEntity<TCronTicker>
            {
                ApplicationNamespaceKey = _runtimePartitionKey,
                Id = Guid.NewGuid(), Status = TickerStatus.Queued, LockHolder = _lockHolder,
                ExecutionTime = executionTime, CronTickerId = item.Id,
                DefinitionRevision = item.DefinitionRevision, LockedAt = now,
                AcquisitionToken = acquisitionToken, CreatedAt = now, UpdatedAt = now
            };
            var added = await context.Upsert(occurrence)
                .On(x => new { x.ApplicationNamespaceKey, x.ExecutionTime, x.CronTickerId }).NoUpdate()
                .RunAsync(cancellationToken).ConfigureAwait(false);
            if (added <= 0) return null;
            occurrence.CronTicker = CronSnapshot(item);
            return occurrence;
        }

        var affected = await context
            .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey &&
                        x.Id == item.NextCronOccurrence.Id && x.ExecutionTime == executionTime)
            .Where(x => x.DefinitionRevision == item.DefinitionRevision)
            .Where(x => x.DefinitionRevision == x.CronTicker.DefinitionRevision)
            .WhereCanAcquire(_lockHolder)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.LockHolder, _lockHolder)
                .SetProperty(x => x.LockedAt, now)
                .SetProperty(x => x.AcquisitionToken, acquisitionToken)
                .SetProperty(x => x.UpdatedAt, now)
                .SetProperty(x => x.Status, TickerStatus.Queued), cancellationToken)
            .ConfigureAwait(false);
        if (affected != 1) return null;

        return new CronTickerOccurrenceEntity<TCronTicker>
        {
            Id = item.NextCronOccurrence.Id, CronTickerId = item.Id,
            DefinitionRevision = item.DefinitionRevision, ExecutionTime = executionTime,
            Status = TickerStatus.Queued, LockHolder = _lockHolder, LockedAt = now,
            AcquisitionToken = acquisitionToken, UpdatedAt = now,
            CreatedAt = item.NextCronOccurrence.CreatedAt, CronTicker = CronSnapshot(item)
        };
    }

    private TCronTicker CronSnapshot(InternalManagerContext item) => new()
    {
        Id = item.Id, Function = item.FunctionName,
        RequestContractVersion = item.RequestContractVersion,
        RequestContractFingerprint = item.RequestContractFingerprint,
        InitIdentifier = _lockHolder, Expression = item.Expression, Retries = item.Retries,
        RetryIntervals = item.RetryIntervals, TimeoutSeconds = item.TimeoutSeconds
    };
    
    public async Task<CronTickerOccurrenceEntity<TCronTicker>> GetEarliestAvailableCronOccurrence(Guid[] ids, CancellationToken cancellationToken = default)
    {
        using (var admissionSession = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            if (!await IsRunnableAdmissionAllowedAsync(admissionSession.Context, cancellationToken).ConfigureAwait(false)) return null;
        var now = _clock.UtcNow;
        var mainSchedulerThreshold = now.AddSeconds(-1);
        var idList = ids.ToList();
        using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var dbContext = session.Context;
        return await dbContext.Set<CronTickerOccurrenceEntity<TCronTicker>>()
            .AsNoTracking()
            .Include(x => x.CronTicker)
            .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey && idList.Contains(x.CronTickerId))
            .Where(x => x.DefinitionRevision == x.CronTicker.DefinitionRevision)
            .Where(x => x.ExecutionTime >= mainSchedulerThreshold)  // Only items within the 1-second main scheduler window
            .WhereCanAcquire(_lockHolder)
            .OrderBy(x => x.ExecutionTime)
            .Select(MappingExtensions.ForLatestQueuedCronTickerOccurrence<CronTickerOccurrenceEntity<TCronTicker>, TCronTicker>())
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }
    
    public async Task<byte[]> GetCronTickerOccurrenceRequest(Guid tickerId, CancellationToken cancellationToken = default)
    {
        using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var dbContext = session.Context;
        return await dbContext.Set<CronTickerOccurrenceEntity<TCronTicker>>()
            .AsNoTracking()
            .Include(x => x.CronTicker)
            .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey && x.Id == tickerId)
            // Evidence reads intentionally do not apply the execution revision fence: a quarantined
            // occurrence must remain inspectable with the exact definition payload that is still retained.
            // Discovery/acquisition/queued-to-running updates carry the authoritative revision predicate.
            .Select(x => x.CronTicker.Request)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }
    
    public async Task UpdateCronTickerOccurrencesWithUnifiedContext(Guid[] cronOccurrenceIds, InternalFunctionContext functionContext,
        CancellationToken cancellationToken = default)
    {
        var idList = cronOccurrenceIds.ToList();
        using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var dbContext = session.Context;
        var now = _clock.UtcNow;
        var query = dbContext.Set<CronTickerOccurrenceEntity<TCronTicker>>()
            .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey && idList.Contains(x.Id));
        if (functionContext.GetPropsToUpdate().Contains(nameof(InternalFunctionContext.Status)) &&
            functionContext.Status == TickerStatus.Idle)
            query = functionContext.AcquisitionToken.HasValue
                ? query.Where(x => x.LockHolder == _lockHolder &&
                                   x.AcquisitionToken == functionContext.AcquisitionToken)
                : query.Where(_ => false);
        await query
            .ExecuteUpdateAsync(setter => setter.UpdateCronTickerOccurrence<TCronTicker>(functionContext, NextLeaseUntil(now)), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<Guid[]> TransitionQueuedCronOccurrencesToInProgressAsync(
        IReadOnlyCollection<AcquisitionLease> leases, CancellationToken cancellationToken = default)
    {
        return await ExecuteRunnableAdmissionAsync(Array.Empty<Guid>(), async (dbContext, ct) =>
        {
            var context = dbContext.Set<CronTickerOccurrenceEntity<TCronTicker>>();
            var now = _clock.UtcNow;
            var winners = new List<Guid>(leases.Count);
            foreach (var lease in leases.Where(x => x.AcquisitionToken.HasValue).Distinct())
            {
                var affected = await context
                    .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey &&
                                x.Id == lease.TickerId && x.Status == TickerStatus.Queued &&
                                x.LockHolder == _lockHolder && x.AcquisitionToken == lease.AcquisitionToken)
                    .Where(x => x.DefinitionRevision == x.CronTicker.DefinitionRevision)
                    .ExecuteUpdateAsync(setter => setter
                        .SetProperty(x => x.Status, TickerStatus.InProgress)
                        .SetProperty(x => x.LeaseUntil, NextLeaseUntil(now))
                        .SetProperty(x => x.UpdatedAt, now), ct).ConfigureAwait(false);
                if (affected == 1) winners.Add(lease.TickerId);
            }
            return winners.ToArray();
        }, cancellationToken).ConfigureAwait(false);
    }
    
    public async Task<int> SkipStaleCronOccurrencesAsync(TimeSpan staleThreshold, CancellationToken cancellationToken = default)
    {
        if (staleThreshold <= TimeSpan.Zero)
            return 0;

        var now = _clock.UtcNow;
        var cutoff = now - staleThreshold;

        using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var dbContext = session.Context;

        return await dbContext.Set<CronTickerOccurrenceEntity<TCronTicker>>()
            .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey &&
                        (x.Status == TickerStatus.Idle || x.Status == TickerStatus.Queued))
            .Where(x => x.ExecutionTime < cutoff)
            .ExecuteUpdateAsync(setter => setter
                .SetProperty(x => x.Status, TickerStatus.Skipped)
                .SetProperty(x => x.SkippedReason, "Missed: occurrence was pending when the application restarted")
                .SetProperty(x => x.UpdatedAt, now), cancellationToken)
            .ConfigureAwait(false);
    }

    #endregion

    #region Stale_Job_Recovery

    // EF Core fully implements lease renewal and the stale-job watchdog below.
    public bool SupportsLeaseBasedRecovery => true;

    public async Task<int> RenewTimeTickerLeases(Guid[] timeTickerIds, DateTime leaseUntil, CancellationToken cancellationToken = default)
    {
        if (timeTickerIds == null || timeTickerIds.Length == 0)
            return 0;

        using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var dbContext = session.Context;
        var idList = timeTickerIds.ToList();

        return await dbContext.Set<TTimeTicker>()
            .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey && idList.Contains(x.Id) &&
                        x.LockHolder == _lockHolder && x.Status == TickerStatus.InProgress)
            .ExecuteUpdateAsync(setter => setter
                .SetProperty(x => x.LeaseUntil, leaseUntil), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<int> RenewCronTickerOccurrenceLeases(Guid[] occurrenceIds, DateTime leaseUntil, CancellationToken cancellationToken = default)
    {
        if (occurrenceIds == null || occurrenceIds.Length == 0)
            return 0;

        using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var dbContext = session.Context;
        var idList = occurrenceIds.ToList();

        return await dbContext.Set<CronTickerOccurrenceEntity<TCronTicker>>()
            .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey && idList.Contains(x.Id) &&
                        x.LockHolder == _lockHolder && x.Status == TickerStatus.InProgress)
            .ExecuteUpdateAsync(setter => setter
                .SetProperty(x => x.LeaseUntil, leaseUntil), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<int> RenewTimeTickerLeases(
        IReadOnlyCollection<AcquisitionLease> leases, DateTime leaseUntil,
        CancellationToken cancellationToken = default)
    {
        if (leases == null || leases.Count == 0)
            return 0;

        using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var dbContext = session.Context;
        var renewed = 0;
        foreach (var lease in leases)
        {
            if (!lease.AcquisitionToken.HasValue)
                continue;

            renewed += await dbContext.Set<TTimeTicker>()
                .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey &&
                            x.Id == lease.TickerId && x.LockHolder == _lockHolder &&
                            x.Status == TickerStatus.InProgress &&
                            x.AcquisitionToken == lease.AcquisitionToken)
                .ExecuteUpdateAsync(setter => setter.SetProperty(x => x.LeaseUntil, leaseUntil), cancellationToken)
                .ConfigureAwait(false);
        }

        return renewed;
    }

    public async Task<int> RenewCronTickerOccurrenceLeases(
        IReadOnlyCollection<AcquisitionLease> leases, DateTime leaseUntil,
        CancellationToken cancellationToken = default)
    {
        if (leases == null || leases.Count == 0)
            return 0;

        using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var dbContext = session.Context;
        var renewed = 0;
        foreach (var lease in leases)
        {
            if (!lease.AcquisitionToken.HasValue)
                continue;

            renewed += await dbContext.Set<CronTickerOccurrenceEntity<TCronTicker>>()
                .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey &&
                            x.Id == lease.TickerId && x.LockHolder == _lockHolder &&
                            x.Status == TickerStatus.InProgress &&
                            x.AcquisitionToken == lease.AcquisitionToken)
                .ExecuteUpdateAsync(setter => setter.SetProperty(x => x.LeaseUntil, leaseUntil), cancellationToken)
                .ConfigureAwait(false);
        }

        return renewed;
    }

    public async Task<Guid[]> GetStillHeldTickerIds(
        IReadOnlyCollection<AcquisitionLease> timeTickerLeases,
        IReadOnlyCollection<AcquisitionLease> occurrenceLeases,
        CancellationToken cancellationToken = default)
    {
        using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var dbContext = session.Context;
        var held = new List<Guid>();

        foreach (var lease in timeTickerLeases ?? Array.Empty<AcquisitionLease>())
        {
            if (!lease.AcquisitionToken.HasValue)
                continue;
            if (await dbContext.Set<TTimeTicker>().AsNoTracking().AnyAsync(
                    x => x.ApplicationNamespaceKey == _runtimePartitionKey &&
                         x.Id == lease.TickerId && x.LockHolder == _lockHolder &&
                         x.Status == TickerStatus.InProgress && x.AcquisitionToken == lease.AcquisitionToken,
                    cancellationToken).ConfigureAwait(false))
                held.Add(lease.TickerId);
        }

        foreach (var lease in occurrenceLeases ?? Array.Empty<AcquisitionLease>())
        {
            if (!lease.AcquisitionToken.HasValue)
                continue;
            if (await dbContext.Set<CronTickerOccurrenceEntity<TCronTicker>>().AsNoTracking().AnyAsync(
                    x => x.ApplicationNamespaceKey == _runtimePartitionKey &&
                         x.Id == lease.TickerId && x.LockHolder == _lockHolder &&
                         x.Status == TickerStatus.InProgress && x.AcquisitionToken == lease.AcquisitionToken,
                    cancellationToken).ConfigureAwait(false))
                held.Add(lease.TickerId);
        }

        return held.ToArray();
    }

    public async Task<Guid[]> GetStillHeldTickerIds(Guid[] timeTickerIds, Guid[] occurrenceIds, CancellationToken cancellationToken = default)
    {
        using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var dbContext = session.Context;
        var held = new List<Guid>();

        if (timeTickerIds is { Length: > 0 })
        {
            var idList = timeTickerIds.ToList();
            held.AddRange(await dbContext.Set<TTimeTicker>()
                .AsNoTracking()
                .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey && idList.Contains(x.Id) &&
                            x.LockHolder == _lockHolder && x.Status == TickerStatus.InProgress)
                .Select(x => x.Id)
                .ToArrayAsync(cancellationToken).ConfigureAwait(false));
        }

        if (occurrenceIds is { Length: > 0 })
        {
            var idList = occurrenceIds.ToList();
            held.AddRange(await dbContext.Set<CronTickerOccurrenceEntity<TCronTicker>>()
                .AsNoTracking()
                .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey && idList.Contains(x.Id) &&
                            x.LockHolder == _lockHolder && x.Status == TickerStatus.InProgress)
                .Select(x => x.Id)
                .ToArrayAsync(cancellationToken).ConfigureAwait(false));
        }

        return held.ToArray();
    }

    public async Task<StaleTickerRecoveryResult> RecoverStaleTickers(int maxStaleRestarts, CancellationToken cancellationToken = default)
    {
        return await ExecuteRunnableAdmissionAsync(
            new StaleTickerRecoveryResult(), async (dbContext, ct) =>
        {
            var now = _clock.UtcNow;
            var result = new StaleTickerRecoveryResult();
            const string staleReason =
                "Stale: the node executing this ticker stopped renewing its lease (presumed dead).";

        var staleLockCutoff = now.Subtract(_schedulerOptions.QueuedLockTimeout);
        await dbContext.Set<TTimeTicker>()
            .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey && x.ParentId == null &&
                        (x.Status == TickerStatus.Idle || x.Status == TickerStatus.Queued) &&
                        x.LockHolder != null && x.LockedAt != null && x.LockedAt < staleLockCutoff)
            .ExecuteUpdateAsync(setter => setter
                .SetProperty(x => x.Status, TickerStatus.Idle)
                .SetProperty(x => x.LockHolder, (string)null)
                .SetProperty(x => x.LockedAt, (DateTime?)null)
                .SetProperty(x => x.LeaseUntil, (DateTime?)null)
                .SetProperty(x => x.AcquisitionToken, (Guid?)null)
                .SetProperty(x => x.UpdatedAt, now), cancellationToken)
            .ConfigureAwait(false);

        await dbContext.Set<CronTickerOccurrenceEntity<TCronTicker>>()
            .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey &&
                        (x.Status == TickerStatus.Idle || x.Status == TickerStatus.Queued) &&
                        x.LockHolder != null && x.LockedAt != null && x.LockedAt < staleLockCutoff)
            .Where(x => x.DefinitionRevision != x.CronTicker.DefinitionRevision)
            .ExecuteUpdateAsync(setter => setter
                .SetProperty(x => x.Status, TickerStatus.Skipped)
                .SetProperty(x => x.SkippedReason,
                    "Quarantined because its Cron definition revision is stale after queued-lock timeout.")
                .SetProperty(x => x.ExecutedAt, now)
                .SetProperty(x => x.LockHolder, (string)null)
                .SetProperty(x => x.LockedAt, (DateTime?)null)
                .SetProperty(x => x.LeaseUntil, (DateTime?)null)
                .SetProperty(x => x.AcquisitionToken, (Guid?)null)
                .SetProperty(x => x.UpdatedAt, now), cancellationToken)
            .ConfigureAwait(false);

        await dbContext.Set<CronTickerOccurrenceEntity<TCronTicker>>()
            .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey &&
                        (x.Status == TickerStatus.Idle || x.Status == TickerStatus.Queued) &&
                        x.LockHolder != null && x.LockedAt != null && x.LockedAt < staleLockCutoff)
            .Where(x => x.DefinitionRevision == x.CronTicker.DefinitionRevision)
            .ExecuteUpdateAsync(setter => setter
                .SetProperty(x => x.Status, TickerStatus.Idle)
                .SetProperty(x => x.LockHolder, (string)null)
                .SetProperty(x => x.LockedAt, (DateTime?)null)
                .SetProperty(x => x.LeaseUntil, (DateTime?)null)
                .SetProperty(x => x.AcquisitionToken, (Guid?)null)
                .SetProperty(x => x.UpdatedAt, now), cancellationToken)
            .ConfigureAwait(false);

        // Expiry is the lease-safe point at which an old in-progress generation can no longer be
        // restarted. Quarantine stale revisions before applying the parent Restart policy so old work
        // never re-enters Idle and becomes eligible under a newer semantic definition.
        await dbContext.Set<CronTickerOccurrenceEntity<TCronTicker>>()
            .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey &&
                        x.Status == TickerStatus.InProgress && x.LeaseUntil != null && x.LeaseUntil < now)
            .Where(x => x.DefinitionRevision != x.CronTicker.DefinitionRevision)
            .ExecuteUpdateAsync(setter => setter
                .SetProperty(x => x.Status, TickerStatus.Skipped)
                .SetProperty(x => x.SkippedReason,
                    "Quarantined because its Cron definition revision is stale after lease expiry.")
                .SetProperty(x => x.ExecutedAt, now)
                .SetProperty(x => x.LockHolder, (string)null)
                .SetProperty(x => x.LockedAt, (DateTime?)null)
                .SetProperty(x => x.LeaseUntil, (DateTime?)null)
                .SetProperty(x => x.AcquisitionToken, (Guid?)null)
                .SetProperty(x => x.UpdatedAt, now), cancellationToken)
            .ConfigureAwait(false);

        // Restart pass first: expired-lease InProgress rows whose policy allows it
        // go back to Idle for any node to re-acquire (fallback picks them up since
        // their ExecutionTime is in the past). The subsequent Cancel pass then only
        // sees leftovers — Cancel policy or exhausted restart budget.
        result.RestartedTimeTickers = await dbContext.Set<TTimeTicker>()
            .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey && x.ParentId == null &&
                        x.Status == TickerStatus.InProgress &&
                        x.LeaseUntil != null && x.LeaseUntil < now)
            .Where(x => x.OnStale == StaleAction.Restart && x.StaleRestartCount < maxStaleRestarts)
            .ExecuteUpdateAsync(setter => setter
                .SetProperty(x => x.Status, TickerStatus.Idle)
                .SetProperty(x => x.LockHolder, (string)null)
                .SetProperty(x => x.LockedAt, (DateTime?)null)
                .SetProperty(x => x.LeaseUntil, (DateTime?)null)
                .SetProperty(x => x.AcquisitionToken, (Guid?)null)
                .SetProperty(x => x.StaleRestartCount, x => x.StaleRestartCount + 1)
                .SetProperty(x => x.UpdatedAt, now), cancellationToken)
            .ConfigureAwait(false);

        result.CancelledTimeTickers = await dbContext.Set<TTimeTicker>()
            .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey && x.ParentId == null &&
                        x.Status == TickerStatus.InProgress &&
                        x.LeaseUntil != null && x.LeaseUntil < now)
            .ExecuteUpdateAsync(setter => setter
                .SetProperty(x => x.Status, TickerStatus.Cancelled)
                .SetProperty(x => x.ExceptionMessage, staleReason)
                .SetProperty(x => x.ExecutedAt, now)
                .SetProperty(x => x.LockHolder, (string)null)
                .SetProperty(x => x.LockedAt, (DateTime?)null)
                .SetProperty(x => x.LeaseUntil, (DateTime?)null)
                .SetProperty(x => x.AcquisitionToken, (Guid?)null)
                .SetProperty(x => x.UpdatedAt, now), cancellationToken)
            .ConfigureAwait(false);

        // Occurrences take their OnStale policy from the parent cron template.
        result.RestartedCronOccurrences = await dbContext.Set<CronTickerOccurrenceEntity<TCronTicker>>()
            .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey &&
                        x.Status == TickerStatus.InProgress && x.LeaseUntil != null && x.LeaseUntil < now)
            .Where(x => x.DefinitionRevision == x.CronTicker.DefinitionRevision)
            .Where(x => x.CronTicker.OnStale == StaleAction.Restart && x.StaleRestartCount < maxStaleRestarts)
            .ExecuteUpdateAsync(setter => setter
                .SetProperty(x => x.Status, TickerStatus.Idle)
                .SetProperty(x => x.LockHolder, (string)null)
                .SetProperty(x => x.LockedAt, (DateTime?)null)
                .SetProperty(x => x.LeaseUntil, (DateTime?)null)
                .SetProperty(x => x.AcquisitionToken, (Guid?)null)
                .SetProperty(x => x.StaleRestartCount, x => x.StaleRestartCount + 1)
                .SetProperty(x => x.UpdatedAt, now), cancellationToken)
            .ConfigureAwait(false);

        result.CancelledCronOccurrences = await dbContext.Set<CronTickerOccurrenceEntity<TCronTicker>>()
            .Where(x => x.ApplicationNamespaceKey == _runtimePartitionKey &&
                        x.Status == TickerStatus.InProgress && x.LeaseUntil != null && x.LeaseUntil < now)
            .ExecuteUpdateAsync(setter => setter
                .SetProperty(x => x.Status, TickerStatus.Cancelled)
                .SetProperty(x => x.ExceptionMessage, staleReason)
                .SetProperty(x => x.ExecutedAt, now)
                .SetProperty(x => x.LockHolder, (string)null)
                .SetProperty(x => x.LockedAt, (DateTime?)null)
                .SetProperty(x => x.LeaseUntil, (DateTime?)null)
                .SetProperty(x => x.AcquisitionToken, (Guid?)null)
                .SetProperty(x => x.UpdatedAt, now), cancellationToken)
            .ConfigureAwait(false);

            return result;
        }, cancellationToken).ConfigureAwait(false);
    }

    #endregion
}
