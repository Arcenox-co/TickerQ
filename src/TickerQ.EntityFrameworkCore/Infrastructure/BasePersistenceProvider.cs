using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Runtime.CompilerServices;
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
        _lockHolder = optionsBuilder.ExecutionOwnerId;
        _schedulerOptions = optionsBuilder;
    }

    protected readonly SchedulerOptionsBuilder _schedulerOptions;

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

    public bool SupportsResultPublication => true;
    public bool SupportsAcknowledgedTerminalUpdates => true;
    public bool SupportsDurableNodeFinalizationOutbox => true;

    public async Task<TickerResultEnvelope> GetTimeTickerResultAsync(
        Guid id, CancellationToken cancellationToken = default)
    {
        using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var row = await session.Context.Set<TimeTickerResultEntity<TTimeTicker>>()
            .AsNoTracking().SingleOrDefaultAsync(x => x.TickerId == id, cancellationToken)
            .ConfigureAwait(false);
        return row == null ? null : ReadEnvelope(
            row.Payload, row.EnvelopeVersion, row.MediaType, row.ContractId, row.ContractType);
    }

    public async Task<TickerResultEnvelope> GetCronTickerOccurrenceResultAsync(
        Guid id, CancellationToken cancellationToken = default)
    {
        using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var row = await session.Context.Set<CronTickerOccurrenceResultEntity<TCronTicker>>()
            .AsNoTracking().SingleOrDefaultAsync(x => x.TickerId == id, cancellationToken)
            .ConfigureAwait(false);
        return row == null ? null : ReadEnvelope(
            row.Payload, row.EnvelopeVersion, row.MediaType, row.ContractId, row.ContractType);
    }

    public async Task<bool> CommitSuccessfulTickerAsync(
        InternalFunctionContext functionContext, CancellationToken cancellationToken = default)
    {
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
        => CommitTerminalTickerCoreAsync(
            functionContext, enforceRemoteChildToken: true, cancellationToken);

    public async Task<bool> CommitTerminalTickerAndEnqueueNodeFinalizationAsync(
        InternalFunctionContext functionContext, NodeFinalizationIntent intent,
        CancellationToken cancellationToken = default)
    {
        ValidateTerminalCommit(functionContext);
        ArgumentNullException.ThrowIfNull(intent);
        if (intent.TickerType != functionContext.Type || intent.TickerId != functionContext.TickerId ||
            !functionContext.AcquisitionToken.HasValue ||
            intent.AcquisitionToken != functionContext.AcquisitionToken.Value)
            throw new InvalidOperationException(
                "Node finalization intent identity does not match the terminal ticker mutation.");

        ValidateEnvelope(functionContext.ResultEnvelope);
        var successful = functionContext.Status is TickerStatus.Done or TickerStatus.DueDone;
        using var strategySession = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var strategy = strategySession.Context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async ct =>
        {
            using var operationSession = await CreateDbContextAsync(ct).ConfigureAwait(false);
            var dbContext = operationSession.Context;
            await using var transaction = await dbContext.Database
                .BeginTransactionAsync(IsolationLevel.Serializable, ct).ConfigureAwait(false);

            var existing = await dbContext.Set<NodeFinalizationOutboxEntity>()
                .AsNoTracking().SingleOrDefaultAsync(x => x.OutboxId == intent.OutboxId, ct)
                .ConfigureAwait(false);
            if (existing != null)
            {
                if (!ImmutableIntentMatches(existing, intent))
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
                    .Where(x => x.Id == functionContext.TickerId && x.LockHolder == _lockHolder &&
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
                    dbContext.Set<TTimeTicker>().Where(x => x.Id == functionContext.TickerId), functionContext);
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

            dbContext.Set<NodeFinalizationOutboxEntity>().Add(ToEntity(intent));
            await dbContext.SaveChangesAsync(ct).ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return true;
        }, cancellationToken).ConfigureAwait(false);
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
                .AsNoTracking().Where(x => x.AvailableAtUtc <= nowUtc)
                .OrderBy(x => x.AvailableAtUtc).ThenBy(x => x.OutboxId)
                .Select(x => x.OutboxId).Take(maxCount).ToArrayAsync(ct).ConfigureAwait(false);
            var claims = new List<NodeFinalizationClaim>(candidates.Length);
            foreach (var outboxId in candidates)
            {
                var claimToken = Guid.NewGuid();
                var affected = await dbContext.Set<NodeFinalizationOutboxEntity>()
                    .Where(x => x.OutboxId == outboxId && x.AvailableAtUtc <= nowUtc)
                    .ExecuteUpdateAsync(setter => setter
                        .SetProperty(x => x.ClaimToken, claimToken)
                        .SetProperty(x => x.ClaimedBy, workerId)
                        .SetProperty(x => x.AvailableAtUtc, leaseUntilUtc)
                        .SetProperty(x => x.AttemptCount, x => x.AttemptCount + 1)
                        .SetProperty(x => x.LastAttemptAtUtc, nowUtc), ct).ConfigureAwait(false);
                if (affected != 1) continue;
                var row = await dbContext.Set<NodeFinalizationOutboxEntity>().AsNoTracking()
                    .SingleAsync(x => x.OutboxId == outboxId && x.ClaimToken == claimToken &&
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
            .Where(x => x.OutboxId == intent.OutboxId && x.TickerType == intent.TickerType &&
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
            .Where(x => x.OutboxId == intent.OutboxId && x.TickerType == intent.TickerType &&
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
                        .Where(x => x.Id == functionContext.TickerId);
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
                    var query = dbContext.Set<TTimeTicker>().Where(x => x.Id == functionContext.TickerId);
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

    private static async Task ReplaceResultAsync(
        TDbContext dbContext, InternalFunctionContext functionContext, CancellationToken cancellationToken)
    {
        if (functionContext.Type == TickerType.CronTickerOccurrence)
        {
            await dbContext.Set<CronTickerOccurrenceResultEntity<TCronTicker>>()
                .Where(x => x.TickerId == functionContext.TickerId)
                .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            if (functionContext.ResultEnvelope != null)
            {
                dbContext.Set<CronTickerOccurrenceResultEntity<TCronTicker>>().Add(new()
                {
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
            .Where(x => x.TickerId == functionContext.TickerId)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        if (functionContext.ResultEnvelope != null)
        {
            dbContext.Set<TimeTickerResultEntity<TTimeTicker>>().Add(new()
            {
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

    private static NodeFinalizationOutboxEntity ToEntity(NodeFinalizationIntent intent) => new()
    {
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
        CreatedAtUtc = intent.CreatedAtUtc,
        AvailableAtUtc = intent.CreatedAtUtc,
        AttemptCount = 0
    };

    private static NodeFinalizationClaim ToClaim(NodeFinalizationOutboxEntity row)
    {
        var intent = new NodeFinalizationIntent(
            row.SchemaVersion, row.OutboxId, row.TickerType, row.TickerId, row.AcquisitionToken,
            row.DispatchId, row.NodeEpoch, row.FinalizeUri, row.FinalizePathAndQuery,
            row.AllowPrivateCallbackAddressesForLocalDevelopment, row.RequestNonce, row.ControlNonce,
            row.ExactBody, AsUtc(row.CreatedAtUtc));
        return new NodeFinalizationClaim(intent, row.ClaimToken!.Value, row.ClaimedBy,
            AsUtc(row.AvailableAtUtc), row.AttemptCount);
    }

    private static DateTime AsUtc(DateTime value)
        => value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static bool ImmutableIntentMatches(
        NodeFinalizationOutboxEntity row, NodeFinalizationIntent intent)
        => row.SchemaVersion == intent.SchemaVersion && row.OutboxId == intent.OutboxId &&
           row.TickerType == intent.TickerType && row.TickerId == intent.TickerId &&
           row.AcquisitionToken == intent.AcquisitionToken && row.DispatchId == intent.DispatchId &&
           row.NodeEpoch == intent.NodeEpoch &&
           string.Equals(row.FinalizeUri, intent.FinalizeUri, StringComparison.Ordinal) &&
           string.Equals(row.FinalizePathAndQuery, intent.FinalizePathAndQuery, StringComparison.Ordinal) &&
           row.AllowPrivateCallbackAddressesForLocalDevelopment == intent.AllowPrivateCallbackAddressesForLocalDevelopment &&
           row.RequestNonce == intent.RequestNonce && row.ControlNonce == intent.ControlNonce &&
           row.ExactBody != null && row.ExactBody.SequenceEqual(intent.ExactBody) &&
           row.CreatedAtUtc.Ticks == intent.CreatedAtUtc.Ticks;

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

    protected Task<DbContextLease<TDbContext>> CreateDbContextAsync(CancellationToken cancellationToken)
        => DbContextLease<TDbContext>.CreateAsync(_serviceProvider, cancellationToken);

    protected DbContextLease<TDbContext> CreateDbContext()
        => DbContextLease<TDbContext>.Create(_serviceProvider);
    
    #region Core_Time_Ticker_Methods
    public async IAsyncEnumerable<TimeTickerEntity> QueueTimeTickers(TimeTickerEntity[] timeTickers, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var dbContext = session.Context;
        var context = dbContext.Set<TTimeTicker>();
        var now = _clock.UtcNow;
        
        foreach (var timeTicker in timeTickers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var acquisitionToken = Guid.NewGuid();
                
            var updatedTicker = await context
                .Where(x => x.Id == timeTicker.Id)
                .Where(x => x.UpdatedAt == timeTicker.UpdatedAt)
                .ExecuteUpdateAsync(prop => prop
                    .SetProperty(x => x.LockHolder, _lockHolder)
                    .SetProperty(x => x.LockedAt, now)
                    .SetProperty(x => x.AcquisitionToken, acquisitionToken)
                    .SetProperty(x => x.ChainRootId, timeTicker.Id)
                    .SetProperty(x => x.ChainGeneration, acquisitionToken)
                    .SetProperty(x => x.UpdatedAt, now)
                    .SetProperty(x => x.Status, TickerStatus.Queued), cancellationToken);

            if (updatedTicker <= 0) 
                continue;
                
            timeTicker.UpdatedAt = now;
            timeTicker.LockHolder = _lockHolder;
            timeTicker.LockedAt = now;
            timeTicker.AcquisitionToken = acquisitionToken;
            timeTicker.ChainRootId = timeTicker.Id;
            timeTicker.ChainGeneration = acquisitionToken;
            timeTicker.Status = TickerStatus.Queued;
            StampQueueGeneration(timeTicker, timeTicker.Id, acquisitionToken);
                
            yield return timeTicker;
        }
    }

    public async IAsyncEnumerable<TimeTickerEntity> QueueTimedOutTimeTickers([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var dbContext = session.Context;
        var context = dbContext.Set<TTimeTicker>();
        var now = _clock.UtcNow;
        var fallbackThreshold = now.AddSeconds(-1);  // Fallback picks up tasks older than main 1-second window

        var timeTickersToUpdate =  await context
            .AsNoTracking()
            .Where(x => x.ExecutionTime != null)
            .Where(x => x.Status == TickerStatus.Idle || x.Status == TickerStatus.Queued)
            .Where(x => x.ExecutionTime <= fallbackThreshold)  // Only tasks older than 1 second
            .Include(x => x.Children.Where(y => y.ExecutionTime == null))
            .Select(MappingExtensions.ForQueueTimeTickers<TTimeTicker>())
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);

        // Probe-and-extend: the fast projection loads root + child + grandchild
        // (depth 3). If any chain actually goes deeper, BFS the rest in batch so
        // we don't silently truncate. Single EXISTS probe when nothing's deeper,
        // proportional cost only when chains exceed depth 3.
        await ExtendQueueChainsBeyondGrandchildrenAsync(context, timeTickersToUpdate, cancellationToken).ConfigureAwait(false);

        foreach (var timeTicker in timeTickersToUpdate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var acquisitionToken = Guid.NewGuid();

            var affected = await context
                .Where(x => x.Id == timeTicker.Id && x.UpdatedAt <= timeTicker.UpdatedAt)
                .ExecuteUpdateAsync(setter => setter
                    .SetProperty(x => x.LockHolder, _lockHolder)
                    .SetProperty(x => x.LockedAt, now)
                    .SetProperty(x => x.LeaseUntil, NextLeaseUntil(now))
                    .SetProperty(x => x.AcquisitionToken, acquisitionToken)
                    .SetProperty(x => x.ChainRootId, timeTicker.Id)
                    .SetProperty(x => x.ChainGeneration, acquisitionToken)
                    .SetProperty(x => x.UpdatedAt, now)
                    .SetProperty(x => x.Status, TickerStatus.InProgress), cancellationToken).ConfigureAwait(false);
                
            if(affected <= 0)
                continue;

            timeTicker.AcquisitionToken = acquisitionToken;
            timeTicker.ChainRootId = timeTicker.Id;
            timeTicker.ChainGeneration = acquisitionToken;
            StampQueueGeneration(timeTicker, timeTicker.Id, acquisitionToken);
            yield return timeTicker;
        }
    }

    public async Task ReleaseAcquiredTimeTickers(Guid[] timeTickerIds, CancellationToken cancellationToken)
    {
        using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var dbContext = session.Context;
        var now = _clock.UtcNow;
            
        var idList = timeTickerIds.ToList();
        var baseQuery = idList.Count == 0
            ? dbContext.Set<TTimeTicker>()
            : dbContext.Set<TTimeTicker>().Where(x => idList.Contains(x.Id));
            
        await baseQuery
            .WhereCanAcquire(_lockHolder)
            .ExecuteUpdateAsync(setter => setter
                .SetProperty(x => x.LockHolder, _ => null)
                .SetProperty(x => x.LockedAt, _ => null)
                .SetProperty(x => x.LeaseUntil, _ => null)
                .SetProperty(x => x.AcquisitionToken, _ => null)
                .SetProperty(x => x.Status, _ => TickerStatus.Idle)
                .SetProperty(x => x.UpdatedAt, _ => now), cancellationToken).ConfigureAwait(false);;
    }
        
    public async Task<int> UpdateTimeTicker(InternalFunctionContext functionContexts, CancellationToken cancellationToken)
    {
        using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var dbContext = session.Context;
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
                .Where(x => x.Id == functionContexts.TickerId);

            query = ApplyTimeTickerGenerationFence(dbContext, query, functionContexts);

            // Fencing: a terminal write from this node must not overwrite a row the
            // stale watchdog already recovered (lock cleared / re-acquired elsewhere).
            // Roots use acquisition ownership below; children are serialized above by
            // the authoritative root generation CAS.
            if (IsFencedTerminalWrite(functionContexts) && functionContexts.ParentId == null)
                query = functionContexts.AcquisitionToken.HasValue
                    ? query.Where(x => x.LockHolder == _lockHolder &&
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

    private static async Task<bool> LockCurrentChainGenerationAsync(
        TDbContext dbContext, InternalFunctionContext context, CancellationToken cancellationToken)
    {
        if (!context.ChainRootId.HasValue || !context.ChainGeneration.HasValue)
            return false;

        var rootId = context.ChainRootId.Value;
        var generation = context.ChainGeneration.Value;
        var affected = await dbContext.Set<TTimeTicker>()
            .Where(root => root.Id == rootId && root.ParentId == null &&
                           root.ChainRootId == root.Id && root.ChainGeneration == generation)
            // A conditional no-op write is intentional: MVCC providers take a row write lock,
            // serializing this child mutation with every root reacquisition.
            .ExecuteUpdateAsync(
                setter => setter.SetProperty(root => root.ChainGeneration, generation),
                cancellationToken).ConfigureAwait(false);
        return affected == 1;
    }

    private static IQueryable<TTimeTicker> ApplyTimeTickerGenerationFence(
        TDbContext dbContext, IQueryable<TTimeTicker> query, InternalFunctionContext context)
    {
        if (!context.ChainRootId.HasValue || !context.ChainGeneration.HasValue)
            return context.ParentId == null ? query : query.Where(_ => false);

        var rootId = context.ChainRootId.Value;
        var generation = context.ChainGeneration.Value;
        return query.Where(target => target.ChainRootId == rootId &&
            dbContext.Set<TTimeTicker>().Any(root => root.Id == rootId && root.ParentId == null &&
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
        using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var dbContext = session.Context;
        var idList = timeTickerIds.ToList();
        var now = _clock.UtcNow;
        await dbContext.Set<TTimeTicker>()
            .Where(x => idList.Contains(x.Id))
            .ExecuteUpdateAsync(setter => setter.UpdateTimeTicker<TTimeTicker>(functionContext, now, NextLeaseUntil(now)), cancellationToken).ConfigureAwait(false);
    }

    public async Task<Guid[]> TransitionQueuedTimeTickersToInProgressAsync(
        IReadOnlyCollection<AcquisitionLease> leases, CancellationToken cancellationToken = default)
    {
        using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var context = session.Context.Set<TTimeTicker>();
        var now = _clock.UtcNow;
        var winners = new List<Guid>(leases.Count);
        foreach (var lease in leases.Where(x => x.AcquisitionToken.HasValue).Distinct())
        {
            var affected = await context
                .Where(x => x.Id == lease.TickerId && x.Status == TickerStatus.Queued &&
                            x.LockHolder == _lockHolder && x.AcquisitionToken == lease.AcquisitionToken)
                .ExecuteUpdateAsync(setter => setter
                    .SetProperty(x => x.Status, TickerStatus.InProgress)
                    .SetProperty(x => x.LeaseUntil, NextLeaseUntil(now))
                    .SetProperty(x => x.UpdatedAt, now), cancellationToken)
                .ConfigureAwait(false);
            if (affected == 1) winners.Add(lease.TickerId);
        }
        return winners.ToArray();
    }
        
    public async Task<TimeTickerEntity[]> GetEarliestTimeTickers(CancellationToken cancellationToken)
    {
        using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var dbContext = session.Context;
        var now = _clock.UtcNow;
    
        // Define the window: ignore anything older than 1 second ago
        var oneSecondAgo = now.AddSeconds(-1);
    
        var baseQuery = dbContext.Set<TTimeTicker>()
            .AsNoTracking()
            .Where(x => x.ExecutionTime != null)
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
    private static async Task ExtendQueueChainsBeyondGrandchildrenAsync(
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
            .AnyAsync(x => x.ParentId.HasValue && leafIds.Contains(x.ParentId.Value), cancellationToken)
            .ConfigureAwait(false);
        if (!hasDeeper)
            return;

        // BFS extension: one query per remaining depth tier, all roots in batch.
        var frontier = leafIds;
        while (frontier.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var levelNodes = await context.AsNoTracking()
                .Where(x => x.ParentId.HasValue && frontier.Contains(x.ParentId.Value))
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

    /// <summary>
    /// Probe-and-extend for the read paths (GetTimeTickerById, GetTimeTickers,
    /// GetTimeTickersPaginated, RemoveTimeTickers). Same shape as
    /// <see cref="ExtendQueueChainsBeyondGrandchildrenAsync"/> but works on the
    /// derived <typeparamref name="TTimeTicker"/> tree returned by EF Include
    /// chains. Caller is expected to have already eager-loaded 2 levels
    /// (Children + ThenInclude(Children)); this helper detects anything below
    /// and BFS-attaches it.
    /// </summary>
    protected static async Task ExtendReadChainsBeyondGrandchildrenAsync(
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
            .AnyAsync(x => x.ParentId.HasValue && leafIds.Contains(x.ParentId.Value), cancellationToken)
            .ConfigureAwait(false);
        if (!hasDeeper)
            return;

        var frontier = leafIds;
        while (frontier.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var levelNodes = await context.AsNoTracking()
                .Where(x => x.ParentId.HasValue && frontier.Contains(x.ParentId.Value))
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
            .Where(x => x.Id == tickerId)
            .Select(x => x.Request)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
    }
    
    public async Task ReleaseDeadNodeTimeTickerResources(string instanceIdentifier, CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var dbContext = session.Context;

        await dbContext.Set<TTimeTicker>()
            .WhereCanAcquire(instanceIdentifier)
            .ExecuteUpdateAsync(setter => setter
                .SetProperty(x => x.LockHolder, _ => null)
                .SetProperty(x => x.LockedAt, _ => null)
                .SetProperty(x => x.LeaseUntil, _ => null)
                .SetProperty(x => x.AcquisitionToken, _ => null)
                .SetProperty(x => x.Status, TickerStatus.Idle)
                .SetProperty(x => x.UpdatedAt, now), cancellationToken)
            .ConfigureAwait(false);
        
        await dbContext.Set<TTimeTicker>()
            .Where(x => x.LockHolder == instanceIdentifier && x.Status == TickerStatus.InProgress)
            .ExecuteUpdateAsync(setter => setter
                .SetProperty(x => x.LockHolder, _ => null)
                .SetProperty(x => x.LockedAt, _ => null)
                .SetProperty(x => x.LeaseUntil, _ => null)
                .SetProperty(x => x.AcquisitionToken, _ => null)
                .SetProperty(x => x.Status, TickerStatus.Idle)
                .SetProperty(x => x.UpdatedAt, now), cancellationToken)
            .ConfigureAwait(false);
    }
    #endregion

    public async Task<TimeTickerEntity[]> AcquireImmediateTimeTickersAsync(Guid[] ids, CancellationToken cancellationToken = default)
    {
        if (ids == null || ids.Length == 0)
            return [];

        using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var dbContext = session.Context;
        var requestedIds = ids.Distinct().ToArray();
        var now = _clock.UtcNow;
        var acquisitionToken = Guid.NewGuid();
        var strategy = dbContext.Database.CreateExecutionStrategy();
        Guid[] lastAcquiredIds = [];

        return await strategy.ExecuteInTransactionAsync(
            operation: async _ =>
            {
                var acquiredIds = new List<Guid>(requestedIds.Length);

                foreach (var id in requestedIds)
                {
                    var affected = await dbContext.Set<TTimeTicker>()
                        .Where(x => x.Id == id)
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
                            cancellationToken)
                        .ConfigureAwait(false);

                    if (affected == 1)
                        acquiredIds.Add(id);
                }

                lastAcquiredIds = acquiredIds.ToArray();
                if (lastAcquiredIds.Length == 0)
                    return [];

                var acquired = await dbContext.Set<TTimeTicker>()
                    .AsNoTracking()
                    .Where(x => lastAcquiredIds.Contains(x.Id))
                    .Include(x => x.Children.Where(y => y.ExecutionTime == null))
                    .Select(MappingExtensions.ForQueueTimeTickers<TTimeTicker>())
                    .ToArrayAsync(cancellationToken)
                    .ConfigureAwait(false);

                await ExtendQueueChainsBeyondGrandchildrenAsync(
                        dbContext.Set<TTimeTicker>(), acquired, cancellationToken)
                    .ConfigureAwait(false);
                foreach (var root in acquired)
                    StampQueueGeneration(root, root.Id, root.ChainGeneration);

                cancellationToken.ThrowIfCancellationRequested();
                return acquired;
            },
            verifySucceeded: async _ =>
            {
                if (lastAcquiredIds.Length == 0)
                    return true;

                var committedCount = await dbContext.Set<TTimeTicker>()
                    .AsNoTracking()
                    .CountAsync(x => lastAcquiredIds.Contains(x.Id)
                                     && x.Status == TickerStatus.InProgress
                                     && x.LockHolder == _lockHolder
                                     && x.AcquisitionToken == acquisitionToken,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                return committedCount == lastAcquiredIds.Length;
            },
            isolationLevel: IsolationLevel.Unspecified,
            cancellationToken: CancellationToken.None).ConfigureAwait(false);
    }
    public async Task<TimeTickerEntity> AcquireTimeTickerOnDemandAsync(
        Guid id, DateTime executionTime, CancellationToken cancellationToken = default)
    {
        using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var dbContext = session.Context;
        var now = _clock.UtcNow;
        var acquisitionToken = Guid.NewGuid();
        var strategy = dbContext.Database.CreateExecutionStrategy();
        var acquired = false;

        return await strategy.ExecuteInTransactionAsync(
            operation: async _ =>
            {
                var affected = await dbContext.Set<TTimeTicker>()
                    .Where(x => x.Id == id)
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
                        .SetProperty(x => x.UpdatedAt, now), cancellationToken)
                    .ConfigureAwait(false);

                acquired = affected == 1;
                if (!acquired)
                    return null;

                // Revival starts a new generation. Remove the previous successful generation's
                // output in this same transaction so a crash/retry cannot leak stale parent data.
                await dbContext.Set<TimeTickerResultEntity<TTimeTicker>>()
                    .Where(x => x.TickerId == id)
                    .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);

                var result = await dbContext.Set<TTimeTicker>()
                    .AsNoTracking()
                    .Where(x => x.Id == id && x.AcquisitionToken == acquisitionToken)
                    .Include(x => x.Children.Where(y => y.ExecutionTime == null))
                    .Select(MappingExtensions.ForQueueTimeTickers<TTimeTicker>())
                    .SingleAsync(cancellationToken)
                    .ConfigureAwait(false);
                await ExtendQueueChainsBeyondGrandchildrenAsync(
                    dbContext.Set<TTimeTicker>(), [result], cancellationToken).ConfigureAwait(false);
                StampQueueGeneration(result, result.Id, result.ChainGeneration);
                return result;
            },
            verifySucceeded: async _ => !acquired || await dbContext.Set<TTimeTicker>()
                .AsNoTracking()
                .AnyAsync(x => x.Id == id && x.Status == TickerStatus.InProgress &&
                               x.LockHolder == _lockHolder && x.AcquisitionToken == acquisitionToken,
                    CancellationToken.None).ConfigureAwait(false),
            isolationLevel: IsolationLevel.Unspecified,
            cancellationToken: CancellationToken.None).ConfigureAwait(false);
    }
        
    #region Core_Cron_Ticker_Methods
    public async Task MigrateDefinedCronTickers(DefinedCronTickerSeed[] cronTickers, CancellationToken cancellationToken = default)
    {
        using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var dbContext = session.Context;
        var now = _clock.UtcNow;

        var functions = cronTickers.Where(x => x.CanSeed).Select(x => x.Function).ToList();
        var blockedFunctions = cronTickers.Where(x => !x.CanSeed).Select(x => x.Function).ToList();
        var cronSet = dbContext.Set<TCronTicker>();

        // Build the complete list of registered function names to detect orphaned tickers.
        // This covers functions whose InitIdentifier was cleared by a dashboard edit (#517).
        // Use List<string> instead of HashSet<string> for broader EF Core provider compatibility
        // (some providers like Devart MySQL don't assign type mappings to HashSet parameters).
        var allRegisteredFunctions = TickerFunctionProvider.TickerFunctions.Keys.ToList();

        // Orphan cleanup is intentionally narrowed to *seeded* crons (those that
        // carry an InitIdentifier from the code-defined-cron migration). Without
        // this filter we'd delete every dashboard-created cron whose function
        // is registered by an SDK / RemoteExecutor — at scheduler boot the SDK
        // hasn't synced yet, so those qualified function names (`name@node`)
        // wouldn't be in TickerFunctionProvider.TickerFunctions yet, and the
        // user's cron would be wiped out on every restart.
        //
        // Skipping non-seeded crons here is safe: they were never tied to a
        // code definition in the first place, so "the code definition went
        // away" doesn't apply to them.
        var orphanedCron = await cronSet
            .Where(c => !string.IsNullOrEmpty(c.InitIdentifier)
                        && (!allRegisteredFunctions.Contains(c.Function)
                            || blockedFunctions.Contains(c.Function)))
            .Select(c => c.Id)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        var orphanedCronList = orphanedCron.ToList();
        if (orphanedCronList.Count > 0)
        {
            // Delete related occurrences first (if any), then the cron tickers
            await dbContext.Set<CronTickerOccurrenceEntity<TCronTicker>>()
                .Where(o => orphanedCronList.Contains(o.CronTickerId))
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            await cronSet
                .Where(c => orphanedCronList.Contains(c.Id))
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        var newFunctionSet = functions.ToHashSet(StringComparer.Ordinal);

        // Load existing SEEDED rows for the current function set. Matching is restricted
        // to rows the code-defined-cron migration owns (non-empty InitIdentifier) so that
        // a user/dashboard-created row that happens to share a function name — and which
        // carries a null/non-seed identity — is never matched, never has its expression
        // overwritten, and never has its contract identity stamped. Seeds reconcile only
        // their own rows; a function with no seeded row falls through to the insert path.
        var existing = await cronSet
            .Where(c => functions.Contains(c.Function) && !string.IsNullOrEmpty(c.InitIdentifier))
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
                var changed = false;

                // Update expression if it changed
                if (!string.Equals(cron.Expression, seed.Expression, StringComparison.Ordinal))
                {
                    cron.Expression = seed.Expression;
                    changed = true;
                }

                // Reconcile the authoritative contract identity onto seeded rows only. Genuinely legacy
                // rows (no seed InitIdentifier — e.g. dashboard-created) keep their own identity so they
                // follow the legacy drift policy instead of code-defined reconciliation.
                if (!string.IsNullOrEmpty(cron.InitIdentifier)
                    && !seed.MatchesIdentity(cron.RequestContractVersion, cron.RequestContractFingerprint))
                {
                    cron.RequestContractVersion = seed.RequestContractVersion;
                    cron.RequestContractFingerprint = seed.RequestContractFingerprint;
                    changed = true;
                }

                if (changed)
                    cron.UpdatedAt = now;
            }
            else
            {
                // Insert new seeded cron ticker
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
                await cronSet.AddAsync(entity, cancellationToken).ConfigureAwait(false);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
        
    public async Task<CronTickerEntity[]> GetAllCronTickerExpressions(CancellationToken cancellationToken = default)
    {
        var result = await RedisContext.GetOrSetArrayAsync(
            cacheKey: "cron:expressions",
            factory: async (ct) =>
            {
                using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
                var dbContext = session.Context;
                return await dbContext.Set<TCronTicker>()
                    .AsNoTracking()
                    .Where(x => x.IsEnabled && !x.IsSystemPaused)
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
            .Where(x => x.IsEnabled)
            .Select(MappingExtensions.ForCronTickerExpressions<CronTickerEntity>())
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
    }
    #endregion

    #region Core_Cron_TickerOccurrence_Methods
    public async Task UpdateCronTickerOccurrence(InternalFunctionContext functionContext, CancellationToken cancellationToken)
    {
        using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var dbContext = session.Context;
        var now = _clock.UtcNow;

        var query = dbContext.Set<CronTickerOccurrenceEntity<TCronTicker>>()
            .Where(x => x.Id == functionContext.TickerId);

        // Fencing: see UpdateTimeTicker. Occurrences are always lock-held by the
        // executing node, so every terminal write is fenced.
        if (IsFencedTerminalWrite(functionContext))
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
        var now = _clock.UtcNow;
        var fallbackThreshold = now.AddSeconds(-1);  // Fallback picks up tasks older than main 1-second window

        using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var dbContext = session.Context;
        var context = dbContext.Set<CronTickerOccurrenceEntity<TCronTicker>>();
            
        var cronTickersToUpdate = await context
            .AsNoTracking()
            .Include(x => x.CronTicker)
            .Where(x => x.Status == TickerStatus.Idle || x.Status == TickerStatus.Queued)
            .Where(x => x.ExecutionTime <= fallbackThreshold)  // Only tasks older than 1 second
            .Select(MappingExtensions.ForQueueCronTickerOccurrence<CronTickerOccurrenceEntity<TCronTicker>, TCronTicker>())
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);

        foreach (var cronTickerOccurrence in cronTickersToUpdate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var acquisitionToken = Guid.NewGuid();

            var affected = await context
                .Where(x => x.Id == cronTickerOccurrence.Id && x.UpdatedAt == cronTickerOccurrence.UpdatedAt)
                .ExecuteUpdateAsync(setter => setter
                    .SetProperty(x => x.LockHolder, _lockHolder)
                    .SetProperty(x => x.LockedAt, now)
                    .SetProperty(x => x.LeaseUntil, NextLeaseUntil(now))
                    .SetProperty(x => x.AcquisitionToken, acquisitionToken)
                    .SetProperty(x => x.UpdatedAt,  now)
                    .SetProperty(x => x.Status, TickerStatus.InProgress), cancellationToken)
                .ConfigureAwait(false);
                
            if(affected <= 0)
                continue;

            cronTickerOccurrence.AcquisitionToken = acquisitionToken;
            yield return cronTickerOccurrence;
        }
    }
    
    public async Task ReleaseDeadNodeOccurrenceResources(string instanceIdentifier, CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var dbContext = session.Context;

        await dbContext.Set<CronTickerOccurrenceEntity<TCronTicker>>()
            .WhereCanAcquire(instanceIdentifier)
            .ExecuteUpdateAsync(setter => setter
                .SetProperty(x => x.LockHolder, _ => null)
                .SetProperty(x => x.LockedAt, _ => null)
                .SetProperty(x => x.LeaseUntil, _ => null)
                .SetProperty(x => x.AcquisitionToken, _ => null)
                .SetProperty(x => x.Status, TickerStatus.Idle)
                .SetProperty(x => x.UpdatedAt, now), cancellationToken)
            .ConfigureAwait(false);
        
        await dbContext.Set<CronTickerOccurrenceEntity<TCronTicker>>()
            .Where(x => x.LockHolder == instanceIdentifier && x.Status == TickerStatus.InProgress)
            .ExecuteUpdateAsync(setter => setter
                .SetProperty(x => x.LockHolder, _ => null)
                .SetProperty(x => x.LockedAt, _ => null)
                .SetProperty(x => x.LeaseUntil, _ => null)
                .SetProperty(x => x.AcquisitionToken, _ => null)
                .SetProperty(x => x.Status, TickerStatus.Idle)
                .SetProperty(x => x.UpdatedAt, now), cancellationToken)
            .ConfigureAwait(false);
    }
    
    public async Task ReleaseAcquiredCronTickerOccurrences(Guid[] occurrenceIds, CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var dbContext = session.Context;

        var idList = occurrenceIds.ToList();
        var baseQuery = idList.Count == 0
            ? dbContext.Set<CronTickerOccurrenceEntity<TCronTicker>>()
            : dbContext.Set<CronTickerOccurrenceEntity<TCronTicker>>().Where(x => idList.Contains(x.Id));
           
        await baseQuery
            .WhereCanAcquire(_lockHolder)
            .ExecuteUpdateAsync(setter => setter
                .SetProperty(x => x.LockHolder, _ => null)
                .SetProperty(x => x.LockedAt, _ => null)
                .SetProperty(x => x.LeaseUntil, _ => null)
                .SetProperty(x => x.AcquisitionToken, _ => null)
                .SetProperty(x => x.Status, TickerStatus.Idle)
                .SetProperty(x => x.UpdatedAt, now), cancellationToken)
            .ConfigureAwait(false);
    }
    
    public async IAsyncEnumerable<CronTickerOccurrenceEntity<TCronTicker>> QueueCronTickerOccurrences((DateTime Key, InternalManagerContext[] Items) cronTickerOccurrences, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        var executionTime = cronTickerOccurrences.Key;

        using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var dbContext = session.Context;
        var context = dbContext.Set<CronTickerOccurrenceEntity<TCronTicker>>();
        
        foreach (var item in cronTickerOccurrences.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var acquisitionToken = Guid.NewGuid();

            if (item.NextCronOccurrence is null)
            {
                var itemToAdd = new CronTickerOccurrenceEntity<TCronTicker>
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
                
                var affectAdded = await context.Upsert(itemToAdd)
                    .On(x => new { x.ExecutionTime, x.CronTickerId })
                    .NoUpdate()
                    .RunAsync(cancellationToken).ConfigureAwait(false);;

                if (affectAdded <= 0)
                    continue;
                
                itemToAdd.CronTicker = new TCronTicker
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
                yield return itemToAdd;
            }
            else
            {
                var affectedUpdate = await context
                    .Where(x => x.Id == item.NextCronOccurrence.Id)
                    .Where(x => x.ExecutionTime == executionTime)
                    .WhereCanAcquire(_lockHolder)
                    .ExecuteUpdateAsync(prop => prop
                            .SetProperty(y => y.LockHolder, _lockHolder)
                            .SetProperty(y => y.LockedAt, now)
                            .SetProperty(y => y.AcquisitionToken, acquisitionToken)
                            .SetProperty(y => y.UpdatedAt, now)
                            .SetProperty(y => y.Status, TickerStatus.Queued),
                        cancellationToken)
                    .ConfigureAwait(false);

                if (affectedUpdate <= 0)
                    continue;
                
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
                        RetryIntervals = item.RetryIntervals
                    }
                };
            }
        }
    }
    
    public async Task<CronTickerOccurrenceEntity<TCronTicker>> GetEarliestAvailableCronOccurrence(Guid[] ids, CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        var mainSchedulerThreshold = now.AddSeconds(-1);
        var idList = ids.ToList();
        using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var dbContext = session.Context;
        return await dbContext.Set<CronTickerOccurrenceEntity<TCronTicker>>()
            .AsNoTracking()
            .Include(x => x.CronTicker)
            .Where(x => idList.Contains(x.CronTickerId))
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
            .Where(x => x.Id == tickerId)
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
        await dbContext.Set<CronTickerOccurrenceEntity<TCronTicker>>()
            .Where(x => idList.Contains(x.Id))
            .ExecuteUpdateAsync(setter => setter.UpdateCronTickerOccurrence<TCronTicker>(functionContext, NextLeaseUntil(now)), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<Guid[]> TransitionQueuedCronOccurrencesToInProgressAsync(
        IReadOnlyCollection<AcquisitionLease> leases, CancellationToken cancellationToken = default)
    {
        using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var context = session.Context.Set<CronTickerOccurrenceEntity<TCronTicker>>();
        var now = _clock.UtcNow;
        var winners = new List<Guid>(leases.Count);
        foreach (var lease in leases.Where(x => x.AcquisitionToken.HasValue).Distinct())
        {
            var affected = await context
                .Where(x => x.Id == lease.TickerId && x.Status == TickerStatus.Queued &&
                            x.LockHolder == _lockHolder && x.AcquisitionToken == lease.AcquisitionToken)
                .ExecuteUpdateAsync(setter => setter
                    .SetProperty(x => x.Status, TickerStatus.InProgress)
                    .SetProperty(x => x.LeaseUntil, NextLeaseUntil(now))
                    .SetProperty(x => x.UpdatedAt, now), cancellationToken)
                .ConfigureAwait(false);
            if (affected == 1) winners.Add(lease.TickerId);
        }
        return winners.ToArray();
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
            .Where(x => x.Status == TickerStatus.Idle || x.Status == TickerStatus.Queued)
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
            .Where(x => idList.Contains(x.Id) && x.LockHolder == _lockHolder && x.Status == TickerStatus.InProgress)
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
            .Where(x => idList.Contains(x.Id) && x.LockHolder == _lockHolder && x.Status == TickerStatus.InProgress)
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
                .Where(x => x.Id == lease.TickerId && x.LockHolder == _lockHolder &&
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
                .Where(x => x.Id == lease.TickerId && x.LockHolder == _lockHolder &&
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
                    x => x.Id == lease.TickerId && x.LockHolder == _lockHolder &&
                         x.Status == TickerStatus.InProgress && x.AcquisitionToken == lease.AcquisitionToken,
                    cancellationToken).ConfigureAwait(false))
                held.Add(lease.TickerId);
        }

        foreach (var lease in occurrenceLeases ?? Array.Empty<AcquisitionLease>())
        {
            if (!lease.AcquisitionToken.HasValue)
                continue;
            if (await dbContext.Set<CronTickerOccurrenceEntity<TCronTicker>>().AsNoTracking().AnyAsync(
                    x => x.Id == lease.TickerId && x.LockHolder == _lockHolder &&
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
                .Where(x => idList.Contains(x.Id) && x.LockHolder == _lockHolder && x.Status == TickerStatus.InProgress)
                .Select(x => x.Id)
                .ToArrayAsync(cancellationToken).ConfigureAwait(false));
        }

        if (occurrenceIds is { Length: > 0 })
        {
            var idList = occurrenceIds.ToList();
            held.AddRange(await dbContext.Set<CronTickerOccurrenceEntity<TCronTicker>>()
                .AsNoTracking()
                .Where(x => idList.Contains(x.Id) && x.LockHolder == _lockHolder && x.Status == TickerStatus.InProgress)
                .Select(x => x.Id)
                .ToArrayAsync(cancellationToken).ConfigureAwait(false));
        }

        return held.ToArray();
    }

    public async Task<StaleTickerRecoveryResult> RecoverStaleTickers(int maxStaleRestarts, CancellationToken cancellationToken = default)
    {
        using var session = await CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var dbContext = session.Context;
        var now = _clock.UtcNow;
        var result = new StaleTickerRecoveryResult();
        const string staleReason =
            "Stale: the node executing this ticker stopped renewing its lease (presumed dead).";

        var staleLockCutoff = now.Subtract(_schedulerOptions.QueuedLockTimeout);
        await dbContext.Set<TTimeTicker>()
            .Where(x => x.ParentId == null &&
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
            .Where(x => (x.Status == TickerStatus.Idle || x.Status == TickerStatus.Queued) &&
                        x.LockHolder != null && x.LockedAt != null && x.LockedAt < staleLockCutoff)
            .ExecuteUpdateAsync(setter => setter
                .SetProperty(x => x.Status, TickerStatus.Idle)
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
            .Where(x => x.ParentId == null && x.Status == TickerStatus.InProgress &&
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
            .Where(x => x.ParentId == null && x.Status == TickerStatus.InProgress &&
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
            .Where(x => x.Status == TickerStatus.InProgress && x.LeaseUntil != null && x.LeaseUntil < now)
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
            .Where(x => x.Status == TickerStatus.InProgress && x.LeaseUntil != null && x.LeaseUntil < now)
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
    }

    #endregion
}
