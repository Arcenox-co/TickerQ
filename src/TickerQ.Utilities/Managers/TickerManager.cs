using NCrontab;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Entities.BaseEntity;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Infrastructure;
using TickerQ.Utilities.Exceptions;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Models;

namespace TickerQ.Utilities.Managers
{
    internal partial class
        TickerManager<TTimeTicker, TCronTicker> :
        ICronTickerManager<TCronTicker>,
        ITimeTickerManager<TTimeTicker>
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        private readonly ITickerPersistenceProvider<TTimeTicker, TCronTicker> _persistenceProvider;
        private readonly ITickerQHostScheduler _tickerQHostScheduler;
        private readonly ITickerClock _clock;
        private readonly ITickerQNotificationHubSender _notificationHubSender;
        private readonly ITickerQDispatcher _dispatcher;
        private readonly TickerExecutionContext _executionContext;
        private readonly ITickerQActivationGate _activationGate;
        private readonly ILogger<TickerManager<TTimeTicker, TCronTicker>> _logger;
        private readonly TickerLifecycleLogger _lifecycle;
        private readonly bool _runtimeSchedulerEnabled;
        private readonly string _runtimeActivationScopeKey;
        private readonly long _runtimeActivationEpoch;
        private static readonly SemaphoreSlim AddOnceGate = new(1, 1);
        public TickerManager(
            ITickerPersistenceProvider<TTimeTicker, TCronTicker> persistenceProvider,
            ITickerQHostScheduler tickerQHostScheduler,
            ITickerClock clock,
            ITickerQNotificationHubSender notificationHubSender,
            TickerExecutionContext executionContext,
            ITickerQDispatcher dispatcher,
            SchedulerOptionsBuilder schedulerOptions,
            ITickerQActivationGate activationGate,
            ILogger<TickerManager<TTimeTicker, TCronTicker>> logger = null,
            TickerLifecycleLogger lifecycle = null
            )
        {
            _persistenceProvider = persistenceProvider;
            _tickerQHostScheduler = tickerQHostScheduler ?? throw new ArgumentNullException(nameof(tickerQHostScheduler));
            _clock = clock;
            _notificationHubSender = notificationHubSender;
            _executionContext = executionContext ?? throw new ArgumentNullException(nameof(executionContext));
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _runtimeSchedulerEnabled = schedulerOptions?.RuntimeSchedulerEnabled ?? true;
            _runtimeActivationScopeKey = schedulerOptions?.RuntimeActivationScope?.ScopeKey;
            _runtimeActivationEpoch = schedulerOptions?.RuntimeActivationEpoch ?? 0;
            _activationGate = activationGate ?? throw new ArgumentNullException(nameof(activationGate));
            _logger = logger ?? NullLogger<TickerManager<TTimeTicker, TCronTicker>>.Instance;
            _lifecycle = lifecycle;
        }

        Task<TickerResult<TCronTicker>> ICronTickerManager<TCronTicker>.AddAsync(TCronTicker entity, CancellationToken cancellationToken)
            => AddCronTickerAsync(entity, cancellationToken);

        Task<TickerResult<TTimeTicker>> ITimeTickerManager<TTimeTicker>.AddAsync(TTimeTicker entity, CancellationToken cancellationToken)
            => AddTimeTickerAsync(entity, cancellationToken);

        async Task<TickerResult<TTimeTicker>> ITimeTickerManager<TTimeTicker>.AddOnceAsync(string initIdentifier, TTimeTicker entity, CancellationToken cancellationToken)
        {
            if (entity is null)
                return new TickerResult<TTimeTicker>(new TickerValidatorException("Ticker must not be null."));
            if (string.IsNullOrWhiteSpace(initIdentifier))
                return new TickerResult<TTimeTicker>(
                    new TickerValidatorException("AddOnceAsync requires a non-empty initIdentifier."));

            // Admission must happen before the process-wide deduplication gate. Otherwise an ordinary
            // pre-start caller can hold AddOnceGate while waiting for activation and deadlock the
            // initializer seeder that is authorized to complete that activation.
            await WaitForMutationAdmissionAsync(cancellationToken).ConfigureAwait(false);
            await AddOnceGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var existing = await _persistenceProvider
                    .GetTimeTickers(x => x.InitIdentifier == initIdentifier, cancellationToken)
                    .ConfigureAwait(false);
                if (existing is { Length: > 0 })
                    return new TickerResult<TTimeTicker>(existing.OrderBy(x => x.Id).First());

                entity.InitIdentifier = initIdentifier;
                entity.Id = TimeTickerInitIdentity.DeterministicId(initIdentifier);
                var inserted = await AddTimeTickerAsync(entity, cancellationToken).ConfigureAwait(false);
                if (inserted.IsSucceeded) return inserted;

                // Another node can win between discovery and insert. Deterministic ids make that
                // conflict convergent; return the committed winner rather than surfacing a false error.
                existing = await _persistenceProvider
                    .GetTimeTickers(x => x.InitIdentifier == initIdentifier, cancellationToken)
                    .ConfigureAwait(false);
                return existing is { Length: > 0 }
                    ? new TickerResult<TTimeTicker>(existing.OrderBy(x => x.Id).First())
                    : inserted;
            }
            finally
            {
                AddOnceGate.Release();
            }
        }

        Task<TickerResult<TCronTicker>> ICronTickerManager<TCronTicker>.UpdateAsync(TCronTicker cronTicker, CancellationToken cancellationToken)
            => UpdateCronTickerAsync(cronTicker, cancellationToken);

        Task<TickerResult<TTimeTicker>> ITimeTickerManager<TTimeTicker>.UpdateAsync(TTimeTicker timeTicker, CancellationToken cancellationToken)
            => UpdateTimeTickerAsync(timeTicker, cancellationToken);

        Task<TickerResult<TCronTicker>> ICronTickerManager<TCronTicker>.DeleteAsync(Guid id, CancellationToken cancellationToken)
            => DeleteCronTickerAsync(id, cancellationToken);

        Task<TickerResult<TTimeTicker>> ITimeTickerManager<TTimeTicker>.DeleteAsync(Guid id, CancellationToken cancellationToken)
            => DeleteTimeTickerAsync(id, cancellationToken);

        async Task<TickerResult<TTimeTicker>> ITimeTickerManager<TTimeTicker>.CancelAsync(Guid id, CancellationToken cancellationToken)
        {
            await WaitForMutationAdmissionAsync(cancellationToken).ConfigureAwait(false);
            int affected;
            try
            {
                affected = await _persistenceProvider.CancelPendingTimeTickerAsync(id, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception) { return new TickerResult<TTimeTicker>(exception); }

            if (affected > 0)
            {
                // Persistence already fenced execution. Refresh hints must not turn a committed
                // cancellation into a reported failure or cancel an unrelated running function.
                try { _tickerQHostScheduler.Restart(); } catch { }
                try { await _notificationHubSender.UpdateTimeTickerNotifyAsync(id).ConfigureAwait(false); } catch { }
            }
            return new TickerResult<TTimeTicker>(affected);
        }

        async Task<TickerResult<TTimeTicker>> ITimeTickerManager<TTimeTicker>.SetDeleteBehaviorAsync(
            Guid rootId, ChainDeleteBehavior behavior, CancellationToken cancellationToken)
        {
            if (!Enum.IsDefined(typeof(ChainDeleteBehavior), behavior))
                return new TickerResult<TTimeTicker>(new TickerValidatorException("Unknown chain deletion behavior."));
            await WaitForMutationAdmissionAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var affected = await _persistenceProvider.SetTimeTickerDeleteBehaviorAsync(rootId, behavior, cancellationToken).ConfigureAwait(false);
                if (affected > 0)
                    await NotifyAsync(() => _notificationHubSender.UpdateTimeTickerNotifyAsync(rootId)).ConfigureAwait(false);
                return affected > 0
                    ? new TickerResult<TTimeTicker>(affected)
                    : new TickerResult<TTimeTicker>(new TickerValidatorException("Chain root was not found or changed. Refresh and try again."));
            }
            catch (Exception e)
            {
                return new TickerResult<TTimeTicker>(e);
            }
        }

        Task<TickerResult<TTimeTicker>> ITimeTickerManager<TTimeTicker>.ReplaceChainAsync(Guid oldRootId, TTimeTicker newRoot, CancellationToken cancellationToken)
            => ReplaceTimeTickerChainAsync(oldRootId, newRoot, cancellationToken);

        Task<TickerResult<List<TTimeTicker>>> ITimeTickerManager<TTimeTicker>.AddBatchAsync(List<TTimeTicker> entities, CancellationToken cancellationToken)
            => AddTimeTickersBatchAsync(entities, cancellationToken);

        Task<TickerResult<List<TTimeTicker>>> ITimeTickerManager<TTimeTicker>.UpdateBatchAsync(List<TTimeTicker> timeTickers, CancellationToken cancellationToken)
            => UpdateTimeTickersBatchAsync(timeTickers, cancellationToken);

        Task<TickerResult<TTimeTicker>> ITimeTickerManager<TTimeTicker>.DeleteBatchAsync(List<Guid> ids, CancellationToken cancellationToken)
            => DeleteTimeTickersBatchAsync(ids, cancellationToken);

        Task<TickerResult<TTimeTicker>> ITimeTickerManager<TTimeTicker>.AddAsync<TFunction>(DateTime? executionTime, CancellationToken cancellationToken)
        {
            var functionName = TickerFunctionProvider.GetFunctionName<TFunction>();
            var entity = new TTimeTicker
            {
                Function = functionName,
                ExecutionTime = executionTime
            };
            return AddTimeTickerAsync(entity, cancellationToken);
        }

        Task<TickerResult<TTimeTicker>> ITimeTickerManager<TTimeTicker>.AddAsync<TFunction, TRequest>(DateTime? executionTime, TRequest request, CancellationToken cancellationToken)
        {
            var functionName = TickerFunctionProvider.GetFunctionName<TFunction>();
            var entity = new TTimeTicker
            {
                Function = functionName,
                ExecutionTime = executionTime,
                Request = TickerHelper.CreateTickerRequest(request, functionName)
            };
            return AddTimeTickerAsync(entity, cancellationToken);
        }

        Task<TickerResult<List<TCronTicker>>> ICronTickerManager<TCronTicker>.AddBatchAsync(List<TCronTicker> entities, CancellationToken cancellationToken)
            => AddCronTickersBatchAsync(entities, cancellationToken);

        Task<TickerResult<List<TCronTicker>>> ICronTickerManager<TCronTicker>.UpdateBatchAsync(List<TCronTicker> cronTickers, CancellationToken cancellationToken)
            => UpdateCronTickersBatchAsync(cronTickers, cancellationToken);

        Task<TickerResult<TCronTicker>> ICronTickerManager<TCronTicker>.DeleteBatchAsync(List<Guid> ids, CancellationToken cancellationToken)
            => DeleteCronTickersBatchAsync(ids, cancellationToken);

        private async Task<TickerResult<TTimeTicker>> AddTimeTickerAsync(TTimeTicker entity, CancellationToken cancellationToken)
        {
            if (entity is null)
                return new TickerResult<TTimeTicker>(new TickerValidatorException("Ticker must not be null."));
            await WaitForMutationAdmissionAsync(cancellationToken).ConfigureAwait(false);

            if (entity.Id == Guid.Empty)
                entity.Id = Guid.NewGuid();

            if (ValidateAndStampTimeTickerGraph([entity]) is { } requestError)
                return new TickerResult<TTimeTicker>(requestError);

            entity.ExecutionTime = entity.ExecutionTime == null
                ? _clock.UtcNow
                : ConvertToUtcIfNeeded(entity.ExecutionTime.Value);

            entity.CreatedAt = _clock.UtcNow;
            entity.UpdatedAt = _clock.UtcNow;

            try
            {
                var now = _clock.UtcNow;
                var executionTime = entity.ExecutionTime!.Value;

                // Persist first
                var affected = await _persistenceProvider.AddTimeTickers([entity], cancellationToken: cancellationToken);
                if (affected <= 0)
                    return new TickerResult<TTimeTicker>(new TickerValidatorException(
                        $"TimeTicker '{entity.Id}' was not inserted because it already exists."));

                return await CompleteMutationAsync(new TickerResult<TTimeTicker>(entity, affected), async () =>
                {
                    _lifecycle?.CreatedTree(entity, _executionContext.RuntimePartition.StorageKey);
                    // Notify the dashboard BEFORE the dispatch path — for tickers
                    // scheduled at "now" the dispatcher acquires + runs immediately
                    // and execution often finishes before this method returns. If
                    // the SignalR push happens after dispatch, the dashboard never
                    // sees the row in its Idle/Queued/InProgress states; it just
                    // appears already Done. Sending the scope.changed event up
                    // front guarantees the list refetches the moment the row is
                    // committed to the DB.
                    await NotifyAsync(() => _notificationHubSender.AddTimeTickerNotifyAsync(entity.Id)).ConfigureAwait(false);

                    // Only try to dispatch immediately if dispatcher is enabled (background services running)
                    if (_dispatcher.IsEnabled && executionTime <= now.AddSeconds(1))
                    {
                        // Acquire and mark InProgress in one provider call
                        var acquired = await _persistenceProvider
                            .AcquireImmediateTimeTickersAsync([entity.Id], CancellationToken.None)
                            .ConfigureAwait(false);

                        if (acquired.Length > 0)
                        {
                            var contexts = BuildImmediateContextsFromNonGeneric(acquired);
                            CacheFunctionReferences(contexts.AsSpan());
                            // CancellationToken.None on purpose: the caller's token is often an HTTP
                            // request's — the dispatched job (and its terminal status write)
                            // must outlive the request that scheduled it.
                            await _dispatcher.DispatchAsync(contexts, CancellationToken.None).ConfigureAwait(false);
                        }
                    }
                    else
                    {
                        _tickerQHostScheduler.RestartIfNeeded(executionTime);
                    }
                }).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                return new TickerResult<TTimeTicker>(e);
            }
        }

        private async Task<TickerResult<TCronTicker>> AddCronTickerAsync(TCronTicker entity, CancellationToken cancellationToken)
        {
            if (entity is null)
                return new TickerResult<TCronTicker>(new TickerValidatorException("Cron ticker must not be null."));
            await WaitForMutationAdmissionAsync(cancellationToken).ConfigureAwait(false);


            if (entity.Id == Guid.Empty)
                entity.Id = Guid.NewGuid();

            if (TickerFunctionProvider.TickerFunctions.All(x => x.Key != entity?.Function))
                return new TickerResult<TCronTicker>(
                    new TickerValidatorException($"Cannot find TickerFunction with name {entity?.Function}"));

            if (ValidateAndStampCron(entity, _persistenceProvider.SupportsCronChains) is { } requestError)
                return new TickerResult<TCronTicker>(requestError);

            if (CronScheduleCache.GetNextOccurrenceOrDefault(entity.Expression, _clock.UtcNow) is not { } nextOccurrence)
                return new TickerResult<TCronTicker>(
                    new TickerValidatorException($"Cannot parse expression {entity.Expression}"));


            entity.CreatedAt = _clock.UtcNow;
            entity.UpdatedAt = _clock.UtcNow;

            try
            {
                var affected = await _persistenceProvider.InsertCronTickers([entity], cancellationToken: cancellationToken);
                if (affected <= 0)
                    return new TickerResult<TCronTicker>(new TickerValidatorException("No cron ticker was inserted. It may already exist or mutation admission may have changed."));

                return await CompleteMutationAsync(new TickerResult<TCronTicker>(entity, affected), async () =>
                {
                    await NotifyAsync(() => _notificationHubSender.AddCronTickerNotifyAsync(entity.Id)).ConfigureAwait(false);
                    _tickerQHostScheduler.RestartIfNeeded(nextOccurrence);
                }).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                return new TickerResult<TCronTicker>(e);
            }
        }

        private async Task<TickerResult<TTimeTicker>> UpdateTimeTickerAsync(TTimeTicker timeTicker, CancellationToken cancellationToken)
        {
            await WaitForMutationAdmissionAsync(cancellationToken).ConfigureAwait(false);

            if (timeTicker is null)
                return new TickerResult<TTimeTicker>(
                    new TickerValidatorException($"Ticker must not be null!"));

            if (timeTicker.ExecutionTime == null)
                return new TickerResult<TTimeTicker>(
                    new TickerValidatorException($"Ticker ExecutionTime must not be null!"));

            if (ValidateAndStampTimeTickerGraph([timeTicker]) is { } requestError)
                return new TickerResult<TTimeTicker>(requestError);

            timeTicker.UpdatedAt = _clock.UtcNow;
            timeTicker.ExecutionTime = ConvertToUtcIfNeeded(timeTicker.ExecutionTime.Value);

            try
            {
                var affectedRows = await _persistenceProvider.UpdateTimeTickers([timeTicker], cancellationToken: cancellationToken).ConfigureAwait(false);

                if (affectedRows <= 0) return new TickerResult<TTimeTicker>(new InvalidOperationException(
                    "The ticker was not updated because it no longer exists or its execution state changed. Reload it before editing."));
                await NotifyAsync(() => _notificationHubSender.UpdateTimeTickerNotifyAsync(timeTicker.Id)).ConfigureAwait(false);
                return await CompleteMutationAsync(new TickerResult<TTimeTicker>(timeTicker, affectedRows), async () =>
                {
                    if (_executionContext.Functions.Any(x => x.TickerId == timeTicker.Id))
                        _tickerQHostScheduler.Restart();
                    else
                        _tickerQHostScheduler.RestartIfNeeded(timeTicker.ExecutionTime);
                }).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                return new TickerResult<TTimeTicker>(e);
            }
        }

        private async Task<TickerResult<TCronTicker>> UpdateCronTickerAsync(TCronTicker cronTicker, CancellationToken cancellationToken = default)
        {
            await WaitForMutationAdmissionAsync(cancellationToken).ConfigureAwait(false);

            if (cronTicker is null)
                return new TickerResult<TCronTicker>(new TickerValidatorException("Cron ticker must not be null."));

            if (TickerFunctionProvider.TickerFunctions.All(x => x.Key != cronTicker?.Function))
                return new TickerResult<TCronTicker>(
                    new TickerValidatorException($"Cannot find TickerFunction with name {cronTicker.Function}"));

            if (ValidateAndStampCron(cronTicker, _persistenceProvider.SupportsCronChains) is { } requestError)
                return new TickerResult<TCronTicker>(requestError);


            if (CronScheduleCache.GetNextOccurrenceOrDefault(cronTicker.Expression, _clock.UtcNow) is not { } nextOccurrence)
                return new TickerResult<TCronTicker>(
                    new TickerValidatorException($"Cannot parse expression {cronTicker.Expression}"));

            try
            {
                cronTicker.UpdatedAt = _clock.UtcNow;

                var affectedRows = await _persistenceProvider.UpdateCronTickers([cronTicker], cancellationToken: cancellationToken);

                if (affectedRows <= 0) return new TickerResult<TCronTicker>(cronTicker, affectedRows);
                await NotifyAsync(() => _notificationHubSender.UpdateCronTickerNotifyAsync(cronTicker.Id)).ConfigureAwait(false);
                return await CompleteMutationAsync(new TickerResult<TCronTicker>(cronTicker, affectedRows), async () =>
                {
                    if (_executionContext.Functions.FirstOrDefault(x => x.ParentId == cronTicker.Id) is { } internalFunction)
                    {
                        internalFunction.ResetUpdateProps()
                            .SetProperty(x => x.ExecutionTime, nextOccurrence);

                        await _persistenceProvider.UpdateCronTickerOccurrence(internalFunction, cancellationToken: CancellationToken.None).ConfigureAwait(false);

                        _tickerQHostScheduler.Restart();
                    }

                    _tickerQHostScheduler.RestartIfNeeded(nextOccurrence);
                }).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                return new TickerResult<TCronTicker>(e);
            }
        }

        private async Task<TickerResult<TCronTicker>> DeleteCronTickerAsync(Guid id, CancellationToken cancellationToken = default)
        {
            await WaitForMutationAdmissionAsync(cancellationToken).ConfigureAwait(false);
            var affectedRows = await _persistenceProvider.RemoveCronTickers([id], cancellationToken: cancellationToken);

            return await CompleteMutationAsync(new TickerResult<TCronTicker>(affectedRows), async () =>
            {
                if (affectedRows <= 0) return;
                await NotifyAsync(() => _notificationHubSender.RemoveCronTickerNotifyAsync(id)).ConfigureAwait(false);
                if (_executionContext.Functions.Any(x => x.ParentId == id)) _tickerQHostScheduler.Restart();
            }).ConfigureAwait(false);
        }


        private async Task<TickerResult<TTimeTicker>> DeleteTimeTickerAsync(Guid id, CancellationToken cancellationToken = default)
        {
            await WaitForMutationAdmissionAsync(cancellationToken).ConfigureAwait(false);
            if (await GetDeletionRestrictionAsync([id], cancellationToken).ConfigureAwait(false) is { } restriction)
                return new TickerResult<TTimeTicker>(restriction);
            var affectedRows = await _persistenceProvider.RemoveTimeTickers([id], cancellationToken: cancellationToken);

            return await CompleteMutationAsync(new TickerResult<TTimeTicker>(affectedRows), async () =>
            {
                if (affectedRows <= 0) return;
                await NotifyAsync(() => _notificationHubSender.RemoveTimeTickerNotifyAsync(id)).ConfigureAwait(false);
                if (_executionContext.Functions.Any(x => x.TickerId == id)) _tickerQHostScheduler.Restart();
            }).ConfigureAwait(false);
        }

        // Friendly preflight; each provider also enforces this under its atomic graph mutation boundary.
        private async Task<TickerValidatorException> GetDeletionRestrictionAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken)
        {
            foreach (var id in ids.Distinct())
            {
                var root = await _persistenceProvider.GetTimeTickerById(id, cancellationToken).ConfigureAwait(false);
                if (root != null && !root.ParentId.HasValue && ChainDeletionPolicy.BlocksDeletion(root))
                    return new TickerValidatorException($"Job '{id}' uses Restrict and has unfinished descendants. Wait until every descendant is Done, DueDone, Failed, Cancelled, or Skipped before deleting or replacing this chain.");
            }
            return null;
        }

        private async Task<TickerResult<TTimeTicker>> ReplaceTimeTickerChainAsync(
            Guid oldRootId, TTimeTicker newRoot, CancellationToken cancellationToken)
        {
            await WaitForMutationAdmissionAsync(cancellationToken).ConfigureAwait(false);

            if (newRoot is null)
                return new TickerResult<TTimeTicker>(
                    new TickerValidatorException("Replacement chain root must not be null!"));

            if (newRoot.Id == Guid.Empty)
                newRoot.Id = Guid.NewGuid();

            // The original chain must exist. Bail out here — before validating or persisting
            // anything — so a bad root id can never mutate the store.
            var existing = await _persistenceProvider
                .GetTimeTickerById(oldRootId, cancellationToken)
                .ConfigureAwait(false);
            if (existing == null || existing.ParentId.HasValue)
                return new TickerResult<TTimeTicker>(
                    new TickerValidatorException($"Chain root '{oldRootId}' was not found or is not a root."));

            if (existing.OriginCronTickerId.HasValue)
                return new TickerResult<TTimeTicker>(new TickerValidatorException(
                    "A recurring chain run cannot be replaced. Edit the cron chain template for future runs."));

            if (await GetDeletionRestrictionAsync([oldRootId], cancellationToken).ConfigureAwait(false) is { } restriction)
                return new TickerResult<TTimeTicker>(restriction);

            // Validate + stamp the COMPLETE replacement graph before touching persistence.
            // A validation failure returns here with the original aggregate untouched.
            if (ValidateAndStampTimeTickerGraph([newRoot]) is { } requestError)
                return new TickerResult<TTimeTicker>(requestError);

            newRoot.ExecutionTime = newRoot.ExecutionTime == null
                ? _clock.UtcNow
                : ConvertToUtcIfNeeded(newRoot.ExecutionTime.Value);
            newRoot.CreatedAt = _clock.UtcNow;
            newRoot.UpdatedAt = _clock.UtcNow;

            try
            {
                // Atomic in the provider: persist the replacement, then remove the original,
                // in one transaction. Any failure rolls back and leaves the original intact.
                var inserted = await _persistenceProvider
                    .ReplaceTimeTickerChainAsync(oldRootId, newRoot, cancellationToken)
                    .ConfigureAwait(false);
                if (inserted == 0)
                    return new TickerResult<TTimeTicker>(
                        new TickerValidatorException(
                            $"Chain root '{oldRootId}' no longer exists, is active, or changed during replacement."));

                return await CompleteMutationAsync(new TickerResult<TTimeTicker>(newRoot, inserted), async () =>
                {
                    _lifecycle?.CreatedTree(newRoot, _executionContext.RuntimePartition.StorageKey);
                    await NotifyAsync(() => _notificationHubSender.RemoveTimeTickerNotifyAsync(oldRootId)).ConfigureAwait(false);
                    await NotifyAsync(() => _notificationHubSender.AddTimeTickerNotifyAsync(newRoot.Id)).ConfigureAwait(false);

                    if (_executionContext.Functions.Any(x => x.TickerId == oldRootId))
                        _tickerQHostScheduler.Restart();
                    else
                        _tickerQHostScheduler.RestartIfNeeded(newRoot.ExecutionTime!.Value);
                }).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                return new TickerResult<TTimeTicker>(e);
            }
        }

        private static DateTime ConvertToUtcIfNeeded(DateTime dateTime)
        {
            // If DateTime.Kind is Unspecified, assume it's in system timezone
            return dateTime.Kind switch
            {
                DateTimeKind.Utc => dateTime,
                DateTimeKind.Local => dateTime.ToUniversalTime(),
                DateTimeKind.Unspecified => TimeZoneInfo.ConvertTimeToUtc(dateTime, CronScheduleCache.TimeZoneInfo),
                _ => dateTime
            };
        }

        private Task WaitForMutationAdmissionAsync(CancellationToken cancellationToken)
            => _runtimeSchedulerEnabled &&
               !StartupSeederAdmissionContext.Matches(
                   _runtimeActivationScopeKey, _runtimeActivationEpoch)
                ? _activationGate.WaitForActivationAsync(cancellationToken)
                : Task.CompletedTask;

        internal static TickerValidatorException ValidateAndStampCron(TCronTicker cron, bool supportsCronChains)
        {
            if (ValidateAndStampRequest(cron, cron.Request) is { } error) return error;
            if (cron.Chain == null) return null;
            if (!supportsCronChains)
                return new TickerValidatorException("Recurring chains require a provider with atomic chain publication support.");
            try
            {
                CronChainMaterializer.ValidateShape(cron.Chain);
                var stack = new Stack<CronChainNode>(cron.Chain.Children);
                while (stack.TryPop(out var node))
                {
                    // Reuse the normal time-chain validator to stamp the exact request contract.
                    var entity = new TTimeTicker { Function = node.Function, Request = node.Request };
                    if (ValidateAndStampRequest(entity, entity.Request) is { } childError) return childError;
                    node.RequestContractVersion = entity.RequestContractVersion;
                    node.RequestContractFingerprint = entity.RequestContractFingerprint;
                    foreach (var child in node.Children ?? new()) stack.Push(child);
                }
                cron.Chain = CronChainMaterializer.Deserialize(CronChainMaterializer.Serialize(cron.Chain));
                return null;
            }
            catch (Exception ex) when (ex is ArgumentException or System.Text.Json.JsonException)
            {
                return new TickerValidatorException(ex.Message);
            }
        }

        internal static TickerValidatorException ValidateAndStampRequest(BaseTickerEntity entity, byte[] request)
        {
            var validation = TickerRequestPayloadValidator.Validate(entity.Function, request);
            return StampAcceptedRequest(entity, validation);
        }

        internal static TickerValidatorException ValidateAndStampTimeTickerGraph(IEnumerable<TTimeTicker> roots)
        {
            var snapshot = TickerFunctionProvider.Snapshot;
            var accepted = new List<(TTimeTicker Entity, TickerRequestValidationResult Validation)>();
            // Per-operation, reference-identity active-path set. A node reappearing while it is
            // still on the current traversal path is a cycle; a node reached again after it has
            // fully validated (shared DAG reference) is popped from the set and validates normally.
            var activePath = new HashSet<TTimeTicker>(ReferenceEqualityComparer.Instance);

            foreach (var root in roots)
            {
                if (ValidateTimeTickerGraph(root, snapshot, accepted, activePath) is { } error)
                    return error;
            }

            foreach (var item in accepted)
            {
                item.Entity.RequestContractVersion = item.Validation.AcceptedContractVersion;
                item.Entity.RequestContractFingerprint = item.Validation.AcceptedContractFingerprint;
            }

            return null;
        }

        private static TickerValidatorException ValidateTimeTickerGraph(
            TTimeTicker entity,
            TickerFunctionRegistrySnapshot snapshot,
            List<(TTimeTicker Entity, TickerRequestValidationResult Validation)> accepted,
            HashSet<TTimeTicker> activePath)
        {
            var pending = new Stack<(TTimeTicker Entity, bool Exit)>();
            pending.Push((entity, false));
            while (pending.TryPop(out var item))
            {
                var current = item.Entity;
                if (item.Exit)
                {
                    activePath.Remove(current);
                    continue;
                }
                if (current is null)
                    return new TickerValidatorException("Ticker and chain children must not be null.");
                if (current.Children is null)
                    return new TickerValidatorException("Ticker Children must not be null. Use an empty collection for standalone jobs.");
                if (!Enum.IsDefined(typeof(ChainDeleteBehavior), current.DeleteBehavior))
                    return new TickerValidatorException("Unknown chain deletion behavior.");
                if ((activePath.Count > 0 || current.ParentId.HasValue) && current.DeleteBehavior != ChainDeleteBehavior.Cascade)
                    return new TickerValidatorException("Set deletion behavior on the chain root only.");
                if (!activePath.Add(current))
                    return new TickerValidatorException(
                        $"Cyclic time ticker graph detected for TickerFunction '{current.Function}': " +
                        "a ticker cannot appear within its own child hierarchy.");

                var validation = TickerRequestPayloadValidator.Validate(snapshot, current.Function, current.Request);
                if (!validation.IsValid)
                    return CreateRequestValidationException(current.Function, validation);
                accepted.Add((current, validation));

                // Exit markers preserve active-path cycle detection and shared DAG references
                // without consuming the CLR stack for valid, deeply nested time chains.
                pending.Push((current, true));
                foreach (var child in current.Children.Reverse()) pending.Push((child, false));
            }
            return null;
        }

        private static TickerValidatorException StampAcceptedRequest(
            BaseTickerEntity entity,
            TickerRequestValidationResult validation)
        {
            if (!validation.IsValid)
                return CreateRequestValidationException(entity.Function, validation);

            entity.RequestContractVersion = validation.AcceptedContractVersion;
            entity.RequestContractFingerprint = validation.AcceptedContractFingerprint;
            return null;
        }

        private static TickerValidatorException CreateRequestValidationException(
            string functionName,
            TickerRequestValidationResult validation)
        {
            var details = string.Join("; ", validation.Errors.Select(
                error => string.IsNullOrEmpty(error.Location)
                    ? $"{error.Code}: {error.Message}"
                    : $"{error.Code} at {error.Location}: {error.Message}"));
            return new TickerValidatorException(
                $"Invalid request payload for TickerFunction '{functionName}': {details}");
        }

        // Batch operations implementation
        private static void CacheFunctionReferences(Span<InternalFunctionContext> functions)
            => TickerExecutionContext.CacheFunctionReferences(functions);

        private InternalFunctionContext[] BuildImmediateContextsFromNonGeneric(IEnumerable<TimeTickerEntity> tickers)
        {
            return tickers.Select(BuildContextFromNonGeneric).ToArray();
        }

        private InternalFunctionContext BuildContextFromNonGeneric(TimeTickerEntity root)
        {
            return TimeTickerContextBuilder.Build(root, ticker => new InternalFunctionContext
            {
                FunctionName = ticker.Function,
                RuntimePartitionKey = _executionContext.RuntimePartition.StorageKey,
                RequestContractVersion = ticker.RequestContractVersion,
                RequestContractFingerprint = ticker.RequestContractFingerprint,
                TickerId = ticker.Id,
                Type = Enums.TickerType.TimeTicker,
                Retries = ticker.Retries,
                RetryIntervals = ticker.RetryIntervals,
                TimeoutSeconds = ticker.TimeoutSeconds,
                ParentId = ticker.ParentId,
                AcquisitionToken = ticker.AcquisitionToken,
                ChainRootId = ticker.ChainRootId,
                ChainGeneration = ticker.ChainGeneration,
                ExecutionTime = ticker.ExecutionTime ?? DateTime.UtcNow,
                RunCondition = ticker.RunCondition ?? Enums.RunCondition.OnAnyCompletedStatus
            });
        }

        private async Task<TickerResult<List<TTimeTicker>>> AddTimeTickersBatchAsync(List<TTimeTicker> entities, CancellationToken cancellationToken = default)
        {
            if (entities is null)
                return new TickerResult<List<TTimeTicker>>(new TickerValidatorException("Tickers must not be null."));
            await WaitForMutationAdmissionAsync(cancellationToken).ConfigureAwait(false);

            if (entities.Count == 0)
                return new TickerResult<List<TTimeTicker>>(entities, 0);

            if (ValidateAndStampTimeTickerGraph(entities) is { } requestError)
                return new TickerResult<List<TTimeTicker>>(requestError);

            var immediateTickers = new List<Guid>();
            var now = _clock.UtcNow;
            DateTime earliestForNonImmediate = default;
            foreach (var entity in entities)
            {
                if (entity.Id == Guid.Empty)
                    entity.Id = Guid.NewGuid();

                entity.ExecutionTime ??= now;
                entity.ExecutionTime = ConvertToUtcIfNeeded(entity.ExecutionTime.Value);

                // Align with single AddTimeTickerAsync: initialize timestamps
                entity.CreatedAt = now;
                entity.UpdatedAt = now;

                if (entity.ExecutionTime.Value <= now.AddSeconds(1))
                    immediateTickers.Add(entity.Id);
                else if (earliestForNonImmediate == default || entity.ExecutionTime <= earliestForNonImmediate)
                    earliestForNonImmediate = entity.ExecutionTime.Value;
            }

            try
            {
                var affected = await _persistenceProvider.AddTimeTickers(entities.ToArray(), cancellationToken: cancellationToken);
                if (affected <= 0)
                    return new TickerResult<List<TTimeTicker>>(new TickerValidatorException("No time tickers were inserted. They may already exist or mutation admission may have changed."));

                return await CompleteMutationAsync(new TickerResult<List<TTimeTicker>>(entities, affected), async () =>
                {
                    await CaptureCreatedBatchAsync(entities).ConfigureAwait(false);
                    await NotifyAsync(() => _notificationHubSender.AddTimeTickersBatchNotifyAsync()).ConfigureAwait(false);

                    // Only try to dispatch immediately if dispatcher is enabled (background services running)
                    if (_dispatcher.IsEnabled && immediateTickers.Count > 0)
                    {
                        var acquired = await _persistenceProvider
                            .AcquireImmediateTimeTickersAsync(immediateTickers.ToArray(), CancellationToken.None)
                            .ConfigureAwait(false);

                        if (acquired.Length > 0)
                        {
                            var contexts = BuildImmediateContextsFromNonGeneric(acquired);
                            CacheFunctionReferences(contexts.AsSpan());
                            // CancellationToken.None on purpose: the caller's token is often an HTTP
                            // request's — the dispatched job (and its terminal status write)
                            // must outlive the request that scheduled it.
                            await _dispatcher.DispatchAsync(contexts, CancellationToken.None).ConfigureAwait(false);
                        }
                    }

                    if (immediateTickers.Count > 0)
                        _tickerQHostScheduler.RestartIfNeeded(_clock.UtcNow);

                    if (earliestForNonImmediate != default)
                    {
                        _tickerQHostScheduler.RestartIfNeeded(earliestForNonImmediate);
                    }
                }).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                return new TickerResult<List<TTimeTicker>>(e);
            }
        }

        private async Task<TickerResult<List<TCronTicker>>> AddCronTickersBatchAsync(List<TCronTicker> entities, CancellationToken cancellationToken = default)
        {
            if (entities is null || entities.Any(entity => entity is null))
                return new TickerResult<List<TCronTicker>>(new TickerValidatorException("Cron tickers must not be null or contain null items."));
            await WaitForMutationAdmissionAsync(cancellationToken).ConfigureAwait(false);
            if (entities.Count == 0)
                return new TickerResult<List<TCronTicker>>(entities, 0);

            var validEntities = new List<TCronTicker>();
            var errors = new List<Exception>();
            var nextOccurrences = new List<DateTime>();

            foreach (var entity in entities)
            {
                if (entity.Id == Guid.Empty)
                    entity.Id = Guid.NewGuid();

                if (TickerFunctionProvider.TickerFunctions.All(x => x.Key != entity?.Function))
                {
                    errors.Add(new TickerValidatorException($"Cannot find TickerFunction with name {entity?.Function}"));
                    continue;
                }

                if (ValidateAndStampCron(entity, _persistenceProvider.SupportsCronChains) is { } requestError)
                {
                    errors.Add(requestError);
                    continue;
                }

                if (CronScheduleCache.GetNextOccurrenceOrDefault(entity.Expression, _clock.UtcNow) is not { } nextOccurrence)
                {
                    errors.Add(new TickerValidatorException($"Cannot parse expression {entity.Expression}"));
                    continue;
                }

                entity.CreatedAt = _clock.UtcNow;
                entity.UpdatedAt = _clock.UtcNow;

                validEntities.Add(entity);
                nextOccurrences.Add(nextOccurrence);
            }

            if (errors.Any())
                return new TickerResult<List<TCronTicker>>(errors.First());

            try
            {
                var affected = await _persistenceProvider.InsertCronTickers(validEntities.ToArray(), cancellationToken: cancellationToken);
                if (affected <= 0)
                    return new TickerResult<List<TCronTicker>>(new TickerValidatorException("No cron tickers were inserted. They may already exist or mutation admission may have changed."));

                return await CompleteMutationAsync(new TickerResult<List<TCronTicker>>(validEntities, affected), async () =>
                {
                    if (validEntities.Count != 0)
                    {
                        // Send notifications for all
                        foreach (var entity in validEntities)
                        {
                            await NotifyAsync(() => _notificationHubSender.AddCronTickerNotifyAsync(entity.Id)).ConfigureAwait(false);
                        }
                        _tickerQHostScheduler.RestartIfNeeded(nextOccurrences.Min());
                    }
                }).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                return new TickerResult<List<TCronTicker>>(e);
            }
        }

        private async Task<TickerResult<List<TTimeTicker>>> UpdateTimeTickersBatchAsync(List<TTimeTicker> timeTickers, CancellationToken cancellationToken = default)
        {
            if (timeTickers is null)
                return new TickerResult<List<TTimeTicker>>(new TickerValidatorException("Tickers must not be null."));
            await WaitForMutationAdmissionAsync(cancellationToken).ConfigureAwait(false);

            if (timeTickers.Any(timeTicker => timeTicker is null))
                return new TickerResult<List<TTimeTicker>>(
                    new TickerValidatorException("Ticker must not be null!"));

            if (ValidateAndStampTimeTickerGraph(timeTickers) is { } requestError)
                return new TickerResult<List<TTimeTicker>>(requestError);

            var validTickers = new List<TTimeTicker>();
            var errors = new List<Exception>();
            var needsRestart = false;

            foreach (var timeTicker in timeTickers)
            {
                if (timeTicker.ExecutionTime == null)
                {
                    errors.Add(new TickerValidatorException("Ticker ExecutionTime must not be null!"));
                    continue;
                }

                timeTicker.UpdatedAt = _clock.UtcNow;
                timeTicker.ExecutionTime = ConvertToUtcIfNeeded(timeTicker.ExecutionTime.Value);

                if (_executionContext.Functions.Any(x => x.TickerId == timeTicker.Id))
                    needsRestart = true;

                validTickers.Add(timeTicker);
            }

            if (errors.Any())
                return new TickerResult<List<TTimeTicker>>(errors.First());

            try
            {
                var affectedRows = await _persistenceProvider.UpdateTimeTickers(validTickers.ToArray(), cancellationToken: cancellationToken).ConfigureAwait(false);

                if (affectedRows <= 0) return new TickerResult<List<TTimeTicker>>(validTickers, affectedRows);
                foreach (var ticker in validTickers)
                    await NotifyAsync(() => _notificationHubSender.UpdateTimeTickerNotifyAsync(ticker.Id)).ConfigureAwait(false);
                return await CompleteMutationAsync(new TickerResult<List<TTimeTicker>>(validTickers, affectedRows), async () =>
                {
                    if (needsRestart)
                        _tickerQHostScheduler.Restart();
                    else if (validTickers.Any())
                    {
                        var earliestExecution = validTickers.Min(t => t.ExecutionTime);
                        _tickerQHostScheduler.RestartIfNeeded(earliestExecution);
                    }
                }).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                return new TickerResult<List<TTimeTicker>>(e);
            }
        }

        private async Task<TickerResult<List<TCronTicker>>> UpdateCronTickersBatchAsync(List<TCronTicker> cronTickers, CancellationToken cancellationToken = default)
        {
            if (cronTickers is null)
                return new TickerResult<List<TCronTicker>>(new TickerValidatorException("Cron tickers must not be null."));
            await WaitForMutationAdmissionAsync(cancellationToken).ConfigureAwait(false);

            var validTickers = new List<TCronTicker>();
            var errors = new List<Exception>();
            var nextOccurrences = new List<DateTime>();
            var needsRestart = false;
            var internalFunctionsToUpdate = new List<(InternalFunctionContext Context, DateTime NextOccurrence)>();

            foreach (var cronTicker in cronTickers)
            {
                if (cronTicker is null)
                {
                    errors.Add(new TickerValidatorException("Cron ticker must not be null."));
                    continue;
                }

                if (TickerFunctionProvider.TickerFunctions.All(x => x.Key != cronTicker?.Function))
                {
                    errors.Add(new TickerValidatorException($"Cannot find TickerFunction with name {cronTicker.Function}"));
                    continue;
                }

                if (ValidateAndStampCron(cronTicker, _persistenceProvider.SupportsCronChains) is { } requestError)
                {
                    errors.Add(requestError);
                    continue;
                }

                if (CronScheduleCache.GetNextOccurrenceOrDefault(cronTicker.Expression, _clock.UtcNow) is not { } nextOccurrence)
                {
                    errors.Add(new TickerValidatorException($"Cannot parse expression {cronTicker.Expression}"));
                    continue;
                }

                cronTicker.UpdatedAt = _clock.UtcNow;

                if (_executionContext.Functions.FirstOrDefault(x => x.ParentId == cronTicker.Id) is { } internalFunction)
                {
                    internalFunctionsToUpdate.Add((internalFunction, nextOccurrence));
                    needsRestart = true;
                }

                validTickers.Add(cronTicker);
                nextOccurrences.Add(nextOccurrence);
            }

            if (errors.Any())
                return new TickerResult<List<TCronTicker>>(errors.First());

            try
            {
                var affectedRows = await _persistenceProvider.UpdateCronTickers(validTickers.ToArray(), cancellationToken: cancellationToken);

                if (affectedRows <= 0) return new TickerResult<List<TCronTicker>>(validTickers, affectedRows);
                foreach (var ticker in validTickers)
                    await NotifyAsync(() => _notificationHubSender.UpdateCronTickerNotifyAsync(ticker.Id)).ConfigureAwait(false);
                return await CompleteMutationAsync(new TickerResult<List<TCronTicker>>(validTickers, affectedRows), async () =>
                {
                    // Update internal functions for those that need it
                    foreach (var update in internalFunctionsToUpdate)
                    {
                        update.Context.ResetUpdateProps().SetProperty(x => x.ExecutionTime, update.NextOccurrence);
                        await _persistenceProvider.UpdateCronTickerOccurrence(update.Context, cancellationToken: CancellationToken.None).ConfigureAwait(false);
                    }

                    if (needsRestart)
                        _tickerQHostScheduler.Restart();

                    else if (nextOccurrences.Any())
                    {
                        var earliestOccurrence = nextOccurrences.Min();
                        _tickerQHostScheduler.RestartIfNeeded(earliestOccurrence);
                    }
                }).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                return new TickerResult<List<TCronTicker>>(e);
            }
        }

        private async Task<TickerResult<TTimeTicker>> DeleteTimeTickersBatchAsync(List<Guid> ids, CancellationToken cancellationToken = default)
        {
            if (ids is null)
                return new TickerResult<TTimeTicker>(new TickerValidatorException("Ticker IDs must not be null."));
            await WaitForMutationAdmissionAsync(cancellationToken).ConfigureAwait(false);
            if (await GetDeletionRestrictionAsync(ids, cancellationToken).ConfigureAwait(false) is { } restriction)
                return new TickerResult<TTimeTicker>(restriction);
            var affectedRows = await _persistenceProvider.RemoveTimeTickers(ids.ToArray(), cancellationToken: cancellationToken);

            return await CompleteMutationAsync(new TickerResult<TTimeTicker>(affectedRows), async () =>
            {
                if (affectedRows <= 0) return;
                foreach (var id in ids.Distinct())
                    await NotifyAsync(() => _notificationHubSender.RemoveTimeTickerNotifyAsync(id)).ConfigureAwait(false);
                if (_executionContext.Functions.Any(x => ids.Contains(x.TickerId))) _tickerQHostScheduler.Restart();
            }).ConfigureAwait(false);
        }

        private async Task<TickerResult<TCronTicker>> DeleteCronTickersBatchAsync(List<Guid> ids, CancellationToken cancellationToken = default)
        {
            if (ids is null)
                return new TickerResult<TCronTicker>(new TickerValidatorException("Cron ticker IDs must not be null."));
            await WaitForMutationAdmissionAsync(cancellationToken).ConfigureAwait(false);
            var affectedRows = await _persistenceProvider.RemoveCronTickers(ids.ToArray(), cancellationToken: cancellationToken);

            return await CompleteMutationAsync(new TickerResult<TCronTicker>(affectedRows), async () =>
            {
                if (affectedRows <= 0) return;
                foreach (var id in ids.Distinct())
                    await NotifyAsync(() => _notificationHubSender.RemoveCronTickerNotifyAsync(id)).ConfigureAwait(false);
                if (_executionContext.Functions.Any(x => ids.Contains(x.ParentId ?? Guid.Empty))) _tickerQHostScheduler.Restart();
            }).ConfigureAwait(false);
        }

        private async Task CaptureCreatedBatchAsync(List<TTimeTicker> entities)
        {
            if (_lifecycle?.Enabled != true) return;
            // Batch providers may accept only some roots. Read committed rows, never report
            // a rejected input as created. Diagnostics must not interrupt post-commit dispatch.
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                var ids = entities.Select(x => x.Id).ToArray();
                var saved = await _persistenceProvider.GetTimeTickers(x => ids.Contains(x.Id), timeout.Token).ConfigureAwait(false);
                foreach (var row in saved) _lifecycle.CreatedTree(row, _executionContext.RuntimePartition.StorageKey);
            }
            catch { }
        }

        private async Task<TickerResult<TEntity>> CompleteMutationAsync<TEntity>(
            TickerResult<TEntity> committed, Func<Task> continuation) where TEntity : class
        {
            try { await continuation().ConfigureAwait(false); }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Ticker mutation committed, but scheduler follow-up failed. The persisted job remains available for recovery.");
                // A saturated in-process admission queue can decline an immediate dispatch.
                // The mutation remains committed; wake normal acquisition to retry the job
                // instead of waiting for an unrelated schedule or the fallback interval.
                try { _tickerQHostScheduler.Restart(); }
                catch (Exception wakeException)
                {
                    _logger.LogWarning(wakeException,
                        "The scheduler could not be woken after a committed mutation; fallback recovery will retry it.");
                }
            }
            return committed;
        }

        private async Task NotifyAsync(Func<Task> notify)
        {
            try { await notify().ConfigureAwait(false); }
            catch (Exception exception)
            {
                // The mutation is already committed. A failed UI hint must never invite
                // the caller to retry that mutation or prevent the scheduler from waking.
                _logger.LogWarning(exception, "Ticker mutation committed, but its dashboard notification failed.");
            }
        }
    }
}
