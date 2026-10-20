using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Exceptions;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Managers;
using TickerQ.Utilities.Models;

namespace TickerQ.EntityFrameworkCore.Infrastructure;

// Singleton interceptor with weak, context-lease-specific state. No application context
// is retained by the scheduler or shared between scopes (including pooled contexts).
internal sealed class EfTransactionalScheduling<TContext, TTimeTicker, TCronTicker>(
    IServiceProvider services, SchedulerOptionsBuilder options, ITickerClock clock,
    ILogger<EfTransactionalScheduling<TContext, TTimeTicker, TCronTicker>> logger)
    : DbCommandInterceptor, ISaveChangesInterceptor, IDbTransactionInterceptor
    where TContext : DbContext
    where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
    where TCronTicker : CronTickerEntity, new()
{
    private readonly ConditionalWeakTable<DbContext, State> _contexts = new();
    private readonly ConditionalWeakTable<DbTransaction, PendingNotifications> _notifications = new();

    private sealed class PendingNotifications
    {
        internal readonly HashSet<Guid> TimeTickers = new();
        internal readonly HashSet<Guid> CronTickers = new();
    }

    private sealed class State(DbContextId lease)
    {
        internal readonly DbContextId Lease = lease;
        internal readonly List<TTimeTicker> Roots = new();
        internal readonly Dictionary<TTimeTicker, NodeStamp[]> Graphs = new(ReferenceEqualityComparer.Instance);
        internal readonly Dictionary<TCronTicker, Guid> Crons = new(ReferenceEqualityComparer.Instance);
        internal TTimeTicker[] SavingRoots = [];
        internal TCronTicker[] SavingCrons = [];
        internal bool Saving;
        internal bool Preparing;
        internal AutoTransactionBehavior PreviousBehavior;
        internal DbTransaction PreparedTransaction;
    }

    private sealed record NodeStamp(TTimeTicker Entity, Guid Id, Guid? ParentId);

    private State GetState(DbContext context)
    {
        var state = _contexts.GetValue(context, db => new State(db.ContextId));
        if (state.Lease != context.ContextId)
        {
            _contexts.Remove(context);
            state = _contexts.GetValue(context, db => new State(db.ContextId));
        }
        return state;
    }

    internal async Task<TTimeTicker> StageAsync(TContext context, TTimeTicker entity, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entity);
        var state = await PrepareStagingAsync<TTimeTicker>(context, ct).ConfigureAwait(false);
        var nodes = ValidateGraph(entity);
        if (nodes.Any(node => context.Entry(node).State != EntityState.Detached))
            throw new InvalidOperationException("StageAsync accepts new, untracked ticker graphs only.");
        if (TickerManager<TTimeTicker, TCronTicker>.ValidateAndStampTimeTickerGraph([entity]) is { } error)
            throw error;
        NormalizeGraph(entity, null, entity.Id == Guid.Empty ? Guid.NewGuid() : entity.Id, clock.UtcNow);
        try
        {
            await context.Set<TTimeTicker>().AddAsync(entity, ct).ConfigureAwait(false);
            state.Roots.Add(entity);
            state.Graphs.Add(entity, nodes.Select(node => new NodeStamp(node, node.Id, node.ParentId)).ToArray());
            return entity;
        }
        catch
        {
            foreach (var node in nodes) context.Entry(node).State = EntityState.Detached;
            throw;
        }
    }

    internal async Task<TCronTicker> StageCronAsync(TContext context, TCronTicker entity, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entity);
        var state = await PrepareStagingAsync<TCronTicker>(context, ct).ConfigureAwait(false);
        if (context.Entry(entity).State != EntityState.Detached)
            throw new InvalidOperationException("StageAsync accepts new, untracked cron tickers only.");
        if (!string.IsNullOrEmpty(entity.ApplicationNamespaceKey) &&
            entity.ApplicationNamespaceKey != TickerQRuntimePartition.LegacyGlobal.StorageKey &&
            entity.ApplicationNamespaceKey != options.RuntimePartition.StorageKey)
            throw new InvalidOperationException("A staged cron cannot be redirected from another application partition.");
        ValidateCron(entity);
        if (entity.Id == Guid.Empty) entity.Id = Guid.NewGuid();
        entity.ApplicationNamespaceKey = options.RuntimePartition.StorageKey;
        entity.CreatedAt = entity.UpdatedAt = clock.UtcNow;
        entity.DefinitionRevision = 1;
        try
        {
            await context.Set<TCronTicker>().AddAsync(entity, ct).ConfigureAwait(false);
            state.Crons.Add(entity, entity.Id);
            return entity;
        }
        catch
        {
            context.Entry(entity).State = EntityState.Detached;
            throw;
        }
    }

    private void ValidateCron(TCronTicker entity)
    {
        // Stage creates a user-owned schedule, not a definition owned by startup reconciliation.
        if (!string.IsNullOrEmpty(entity.InitIdentifier) || entity.SeedKey != null ||
            entity.SeedOwnerNamespace != null || entity.SeedManifestEpoch != null ||
            entity.SeedLastSeenAt != null || entity.RetirementRequestedAt != null ||
            entity.RetiredAt != null || entity.SeedWasEnabledBeforeRetirement != null)
            throw new InvalidOperationException("StageAsync accepts new user-owned cron schedules, not code-owned or retired seeds.");
        if (TickerManager<TTimeTicker, TCronTicker>.ValidateAndStampCron(entity, supportsCronChains: true) is { } error)
            throw error;
        if (string.IsNullOrWhiteSpace(entity.Expression) ||
            CronScheduleCache.GetNextOccurrenceOrDefault(entity.Expression, clock.UtcNow) == null)
            throw new TickerValidatorException($"Cannot parse expression {entity.Expression}");
    }

    private async Task<State> PrepareStagingAsync<TEntity>(TContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!context.Database.IsRelational())
            throw new InvalidOperationException("StageAsync requires a relational EF Core application DbContext.");
        var interceptors = context.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()?.Interceptors;
        if (interceptors == null || !interceptors.Contains(this))
            throw new InvalidOperationException(
                "The application DbContext is missing TickerQ's transactional scheduling interceptors. " +
                "Register it through DI with UseApplicationDbContext<TContext>.");
        if (context.Model.FindEntityType(typeof(TEntity)) == null)
            throw new InvalidOperationException("The application DbContext must include the TickerQ entity mappings.");
        if (options.RuntimeSchedulerEnabled && !StartupSeederAdmissionContext.Matches(
                options.RuntimeActivationScope?.ScopeKey, options.RuntimeActivationEpoch))
            await services.GetRequiredService<ITickerQActivationGate>().WaitForActivationAsync(ct).ConfigureAwait(false);

        var state = GetState(context);
        if (state.Saving)
            throw new InvalidOperationException("Cannot stage a ticker while this DbContext is saving.");
        return state;
    }

    private static List<TTimeTicker> ValidateGraph(TTimeTicker root)
    {
        if (root.ParentId != null || root.Parent != null)
            throw new InvalidOperationException("StageAsync accepts new root tickers and their complete child graphs.");
        var nodes = new List<TTimeTicker>();
        var seen = new HashSet<TTimeTicker>(ReferenceEqualityComparer.Instance);
        var ids = new HashSet<Guid>();
        var pending = new Stack<TTimeTicker>();
        pending.Push(root);
        while (pending.TryPop(out var node))
        {
            if (node == null || !seen.Add(node) || (node.Id != Guid.Empty && !ids.Add(node.Id)))
                throw new InvalidOperationException("A staged ticker graph contains a null, duplicate, or cyclic node.");
            if (node.Status != TickerStatus.Idle || node.AcquisitionToken != null || node.ChainGeneration != null ||
                node.LockHolder != null || node.LockedAt != null || node.LeaseUntil != null || node.ExecutedAt != null)
                throw new InvalidOperationException("StageAsync accepts new, unexecuted ticker graphs only.");
            node.Children ??= new List<TTimeTicker>();
            nodes.Add(node);
            foreach (var child in node.Children) pending.Push(child);
        }
        return nodes;
    }

    private void NormalizeGraph(TTimeTicker node, TTimeTicker parent, Guid rootId, DateTime now)
    {
        var pending = new Stack<(TTimeTicker Row, TTimeTicker Parent)>();
        pending.Push((node, parent));
        while (pending.Count > 0)
        {
            var (current, currentParent) = pending.Pop();
            if (currentParent == null) current.Id = rootId;
            else if (current.Id == Guid.Empty) current.Id = Guid.NewGuid();
            current.Parent = currentParent;
            current.ParentId = currentParent?.Id;
            current.ChainRootId = rootId;
            current.ApplicationNamespaceKey = options.RuntimePartition.StorageKey;
            current.CreatedAt = now;
            current.UpdatedAt = now;
            if (currentParent == null && current.ExecutionTime == null) current.ExecutionTime = now;
            if (current.ExecutionTime is { } time)
                current.ExecutionTime = time.Kind == DateTimeKind.Local ? time.ToUniversalTime() : DateTime.SpecifyKind(time, DateTimeKind.Utc);
            foreach (var child in current.Children.Reverse()) pending.Push((child, current));
        }
    }

    private void BeginSave(DbContext context)
    {
        if (context is not TContext || !_contexts.TryGetValue(context, out _)) return;
        var state = GetState(context);
        // Clear()/detaching the complete graph discards staged work. A partial detach must
        // not leave an orphaned child eligible for an unvalidated, unfenced save.
        foreach (var discarded in state.Roots.Where(root => state.Graphs[root]
                     .All(stamp => context.Entry(stamp.Entity).State == EntityState.Detached)).ToArray())
        {
            state.Roots.Remove(discarded);
            state.Graphs.Remove(discarded);
        }
        var roots = state.Roots.ToArray();
        foreach (var discarded in state.Crons.Keys.Where(cron => context.Entry(cron).State == EntityState.Detached).ToArray())
            state.Crons.Remove(discarded);
        var crons = state.Crons.Keys.ToArray();
        if (roots.Length == 0 && crons.Length == 0) return;
        if (System.Transactions.Transaction.Current != null ||
            System.Transactions.TransactionsDatabaseFacadeExtensions.GetEnlistedTransaction(context.Database) != null)
            throw new InvalidOperationException("StageAsync supports EF database transactions, not ambient TransactionScope transactions.");
        var nodes = new HashSet<TTimeTicker>(ReferenceEqualityComparer.Instance);
        foreach (var root in roots)
        {
            var graph = ValidateGraph(root);
            var graphSet = new HashSet<TTimeTicker>(graph, ReferenceEqualityComparer.Instance);
            var stamps = state.Graphs[root];
            if (graph.Count != stamps.Length || stamps.Any(stamp =>
                    !graphSet.Contains(stamp.Entity) ||
                    stamp.Entity.Id != stamp.Id || stamp.Entity.ParentId != stamp.ParentId ||
                    stamp.Entity.Parent?.Id != stamp.ParentId))
                throw new InvalidOperationException("Construct the complete ticker graph before StageAsync; its identities and parent links cannot change afterward.");
            foreach (var node in graph)
            {
                if (!nodes.Add(node) || context.Entry(node).State != EntityState.Added ||
                    node.ApplicationNamespaceKey != options.RuntimePartition.StorageKey || node.ChainRootId != root.Id)
                    throw new InvalidOperationException("The staged ticker graph changed ownership or tracking state before saving.");
                if (node.ExecutionTime is { } time)
                    node.ExecutionTime = time.Kind == DateTimeKind.Local ? time.ToUniversalTime() : DateTime.SpecifyKind(time, DateTimeKind.Utc);
            }
            root.ExecutionTime ??= clock.UtcNow;
        }
        if (TickerManager<TTimeTicker, TCronTicker>.ValidateAndStampTimeTickerGraph(roots) is { } error) throw error;
        foreach (var cron in crons)
        {
            if (context.Entry(cron).State != EntityState.Added || cron.Id != state.Crons[cron] ||
                cron.ApplicationNamespaceKey != options.RuntimePartition.StorageKey || cron.DefinitionRevision != 1)
                throw new InvalidOperationException("The staged cron changed identity, ownership, revision, or tracking state before saving.");
            ValidateCron(cron);
        }
        state.SavingRoots = roots;
        state.SavingCrons = crons;
        state.Saving = true;
        state.PreparedTransaction = null;
        state.PreviousBehavior = context.Database.AutoTransactionBehavior;
        // Even a single insert needs a transaction containing the admission/graph locks.
        context.Database.AutoTransactionBehavior = AutoTransactionBehavior.Always;
    }

    private void EndSave(DbContext context, bool success)
    {
        if (context == null || !_contexts.TryGetValue(context, out var state) || !state.Saving) return;
        context.Database.AutoTransactionBehavior = state.PreviousBehavior;
        if (success)
        {
            foreach (var root in state.SavingRoots)
            {
                state.Roots.Remove(root);
                state.Graphs.Remove(root);
            }
            foreach (var cron in state.SavingCrons) state.Crons.Remove(cron);
        }
        else if (state.PreparedTransaction != null && _notifications.TryGetValue(state.PreparedTransaction, out var ids))
        {
            foreach (var root in state.SavingRoots) ids.TimeTickers.Remove(root.Id);
            foreach (var cron in state.SavingCrons) ids.CronTickers.Remove(cron.Id);
        }
        state.Saving = false;
        state.SavingRoots = [];
        state.SavingCrons = [];
        state.PreparedTransaction = null;
    }

    public InterceptionResult<int> SavingChanges(DbContextEventData data, InterceptionResult<int> result)
    {
        if (!result.HasResult) BeginSave(data.Context);
        return result;
    }
    public ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken ct = default)
        => new(SavingChanges(data, result));
    public int SavedChanges(SaveChangesCompletedEventData data, int result) { EndSave(data.Context, true); return result; }
    public ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data, int result, CancellationToken ct = default)
        => new(SavedChanges(data, result));
    public void SaveChangesFailed(DbContextErrorEventData data) => EndSave(data.Context, false);
    public Task SaveChangesFailedAsync(DbContextErrorEventData data, CancellationToken ct = default) { EndSave(data.Context, false); return Task.CompletedTask; }
    public void SaveChangesCanceled(DbContextEventData data) => EndSave(data.Context, false);
    public Task SaveChangesCanceledAsync(DbContextEventData data, CancellationToken ct = default) { EndSave(data.Context, false); return Task.CompletedTask; }

    private async Task PrepareCommandAsync(DbContext context, CancellationToken ct)
    {
        if (context is not TContext db || !_contexts.TryGetValue(db, out var state) || !state.Saving || state.Preparing) return;
        var transaction = db.Database.CurrentTransaction?.GetDbTransaction()
            ?? throw new InvalidOperationException("EF must open a transaction before saving staged tickers.");
        if (ReferenceEquals(state.PreparedTransaction, transaction)) return;
        state.Preparing = true;
        try
        {
            var provider = services.GetRequiredService<ITickerPersistenceProvider<TTimeTicker, TCronTicker>>()
                as BasePersistenceProvider<TContext, TTimeTicker, TCronTicker>
                ?? throw new InvalidOperationException("StageAsync requires the configured EF Core persistence provider.");
            await provider.LockStagedTickerSaveAsync(db, state.SavingRoots.Length > 0, ct).ConfigureAwait(false);
            state.PreparedTransaction = transaction;
            var ids = _notifications.GetOrCreateValue(transaction);
            foreach (var root in state.SavingRoots) ids.TimeTickers.Add(root.Id);
            foreach (var cron in state.SavingCrons) ids.CronTickers.Add(cron.Id);
        }
        finally { state.Preparing = false; }
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData data, InterceptionResult<DbDataReader> result)
    { PrepareCommandAsync(data.Context, CancellationToken.None).GetAwaiter().GetResult(); return result; }
    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<DbDataReader> result, CancellationToken ct = default)
    { await PrepareCommandAsync(data.Context, ct).ConfigureAwait(false); return result; }
    public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData data, InterceptionResult<int> result)
    { PrepareCommandAsync(data.Context, CancellationToken.None).GetAwaiter().GetResult(); return result; }
    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<int> result, CancellationToken ct = default)
    { await PrepareCommandAsync(data.Context, ct).ConfigureAwait(false); return result; }
    public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData data, InterceptionResult<object> result)
    { PrepareCommandAsync(data.Context, CancellationToken.None).GetAwaiter().GetResult(); return result; }
    public override async ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<object> result, CancellationToken ct = default)
    { await PrepareCommandAsync(data.Context, ct).ConfigureAwait(false); return result; }

    private bool NeedsTransaction(DbContext context)
        => context != null && _contexts.TryGetValue(context, out var state) && state.Saving;
    public InterceptionResult<DbTransaction> TransactionStarting(DbConnection connection, TransactionStartingEventData data, InterceptionResult<DbTransaction> result)
        => !result.HasResult && NeedsTransaction(data.Context)
            ? InterceptionResult<DbTransaction>.SuppressWithResult(connection.BeginTransaction(IsolationLevel.Serializable)) : result;
    public async ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(DbConnection connection, TransactionStartingEventData data, InterceptionResult<DbTransaction> result, CancellationToken ct = default)
        => !result.HasResult && NeedsTransaction(data.Context)
            ? InterceptionResult<DbTransaction>.SuppressWithResult(await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct).ConfigureAwait(false)) : result;

    public void TransactionCommitted(DbTransaction transaction, TransactionEndEventData data)
        => NotifyCommittedAsync(transaction).GetAwaiter().GetResult();
    public Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData data, CancellationToken ct = default)
        => NotifyCommittedAsync(transaction);
    public void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData data) => _notifications.Remove(transaction);
    public Task TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData data, CancellationToken ct = default)
    { _notifications.Remove(transaction); return Task.CompletedTask; }

    private async Task NotifyCommittedAsync(DbTransaction transaction)
    {
        if (!_notifications.TryGetValue(transaction, out var ids)) return;
        _notifications.Remove(transaction);
        // The commit is already durable. Notification failures cannot turn it into a failed
        // SaveChanges/Commit (and therefore an unsafe business-operation retry).
        foreach (var id in ids.TimeTickers)
        {
            try
            {
                var row = await services.GetRequiredService<ITickerPersistenceProvider<TTimeTicker, TCronTicker>>()
                    .GetTimeTickerById(id, CancellationToken.None).ConfigureAwait(false);
                if (row == null) continue; // A caller may have rolled back a savepoint or deleted it.
                if (row.ExecutionTime is { } time)
                    services.GetRequiredService<ITickerQHostScheduler>().RestartIfNeeded(time);
                await services.GetRequiredService<ITickerQNotificationHubSender>().AddTimeTickerNotifyAsync(id).ConfigureAwait(false);
            }
            catch (Exception error)
            {
                logger.LogError(error, "Ticker {TickerId} was committed, but its scheduler/dashboard notification failed. Database recovery will discover it.", id);
            }
        }
        var cronCacheInvalidationAttempted = false;
        foreach (var id in ids.CronTickers)
        {
            try
            {
                var provider = services.GetRequiredService<ITickerPersistenceProvider<TTimeTicker, TCronTicker>>();
                var row = await provider.GetCronTickerById(id, CancellationToken.None).ConfigureAwait(false);
                if (row == null) continue; // Also handles rollback to a caller-created savepoint.
                if (!cronCacheInvalidationAttempted)
                {
                    cronCacheInvalidationAttempted = true;
                    try
                    {
                        await ((BasePersistenceProvider<TContext, TTimeTicker, TCronTicker>)provider)
                            .InvalidateStagedCronCacheAsync().ConfigureAwait(false);
                    }
                    catch (Exception error)
                    {
                        // Do not fail a durable business commit, or skip the other notifications.
                        logger.LogError(error, "Staged cron schedules were committed, but their cache could not be invalidated. Schedule discovery may be delayed until the cache is refreshed.");
                    }
                }
                if (row.IsEnabled && !row.IsSystemPaused &&
                    CronScheduleCache.GetNextOccurrenceOrDefault(row.Expression, clock.UtcNow) is { } next)
                    services.GetRequiredService<ITickerQHostScheduler>().RestartIfNeeded(next);
                await services.GetRequiredService<ITickerQNotificationHubSender>().AddCronTickerNotifyAsync(id).ConfigureAwait(false);
            }
            catch (Exception error)
            {
                logger.LogError(error, "Cron {TickerId} was committed, but its scheduler/dashboard notification failed. The schedule remains persisted.", id);
            }
        }
    }
}
