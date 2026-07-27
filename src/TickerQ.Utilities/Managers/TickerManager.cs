using NCrontab;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Entities.BaseEntity;
using TickerQ.Utilities.Exceptions;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Models;

namespace TickerQ.Utilities.Managers
{
    internal class
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
        public TickerManager(
            ITickerPersistenceProvider<TTimeTicker, TCronTicker> persistenceProvider,
            ITickerQHostScheduler tickerQHostScheduler,
            ITickerClock clock,
            ITickerQNotificationHubSender notificationHubSender,
            TickerExecutionContext executionContext,
            ITickerQDispatcher dispatcher
            )
        {
            _persistenceProvider = persistenceProvider;
            _tickerQHostScheduler = tickerQHostScheduler ?? throw new ArgumentNullException(nameof(tickerQHostScheduler));
            _clock = clock;
            _notificationHubSender = notificationHubSender;
            _executionContext = executionContext ?? throw new ArgumentNullException(nameof(executionContext));
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        }

        Task<TickerResult<TCronTicker>> ICronTickerManager<TCronTicker>.AddAsync(TCronTicker entity, CancellationToken cancellationToken)
            => AddCronTickerAsync(entity, cancellationToken);

        Task<TickerResult<TTimeTicker>> ITimeTickerManager<TTimeTicker>.AddAsync(TTimeTicker entity, CancellationToken cancellationToken)
            => AddTimeTickerAsync(entity, cancellationToken);

        async Task<TickerResult<TTimeTicker>> ITimeTickerManager<TTimeTicker>.AddOnceAsync(string initIdentifier, TTimeTicker entity, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(initIdentifier))
                return new TickerResult<TTimeTicker>(
                    new TickerValidatorException("AddOnceAsync requires a non-empty initIdentifier."));

            var existing = await _persistenceProvider
                .GetTimeTickers(x => x.InitIdentifier == initIdentifier, cancellationToken)
                .ConfigureAwait(false);

            if (existing is { Length: > 0 })
                return new TickerResult<TTimeTicker>(existing[0]);

            entity.InitIdentifier = initIdentifier;
            return await AddTimeTickerAsync(entity, cancellationToken).ConfigureAwait(false);
        }

        Task<TickerResult<TCronTicker>> ICronTickerManager<TCronTicker>.UpdateAsync(TCronTicker cronTicker, CancellationToken cancellationToken)
            => UpdateCronTickerAsync(cronTicker, cancellationToken);

        Task<TickerResult<TTimeTicker>> ITimeTickerManager<TTimeTicker>.UpdateAsync(TTimeTicker timeTicker, CancellationToken cancellationToken)
            => UpdateTimeTickerAsync(timeTicker, cancellationToken);

        Task<TickerResult<TCronTicker>> ICronTickerManager<TCronTicker>.DeleteAsync(Guid id, CancellationToken cancellationToken)
            => DeleteCronTickerAsync(id, cancellationToken);

        Task<TickerResult<TTimeTicker>> ITimeTickerManager<TTimeTicker>.DeleteAsync(Guid id, CancellationToken cancellationToken)
            => DeleteTimeTickerAsync(id, cancellationToken);

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
                await _persistenceProvider.AddTimeTickers([entity], cancellationToken: cancellationToken);

                // Notify the dashboard BEFORE the dispatch path — for tickers
                // scheduled at "now" the dispatcher acquires + runs immediately
                // and execution often finishes before this method returns. If
                // the SignalR push happens after dispatch, the dashboard never
                // sees the row in its Idle/Queued/InProgress states; it just
                // appears already Done. Sending the scope.changed event up
                // front guarantees the list refetches the moment the row is
                // committed to the DB.
                await _notificationHubSender.AddTimeTickerNotifyAsync(entity.Id).ConfigureAwait(false);

                // Only try to dispatch immediately if dispatcher is enabled (background services running)
                if (_dispatcher.IsEnabled && executionTime <= now.AddSeconds(1))
                {
                    // Acquire and mark InProgress in one provider call
                    var acquired = await _persistenceProvider
                        .AcquireImmediateTimeTickersAsync([entity.Id], cancellationToken)
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

                return new TickerResult<TTimeTicker>(entity);
            }
            catch (Exception e)
            {
                return new TickerResult<TTimeTicker>(e);
            }
        }

        private async Task<TickerResult<TCronTicker>> AddCronTickerAsync(TCronTicker entity, CancellationToken cancellationToken)
        {
           
            if (entity.Id == Guid.Empty)
                entity.Id = Guid.NewGuid();

            if (TickerFunctionProvider.TickerFunctions.All(x => x.Key != entity?.Function))
                return new TickerResult<TCronTicker>(
                    new TickerValidatorException($"Cannot find TickerFunction with name {entity?.Function}"));

            if (ValidateAndStampRequest(entity, entity.Request) is { } requestError)
                return new TickerResult<TCronTicker>(requestError);

            if (CronScheduleCache.GetNextOccurrenceOrDefault(entity.Expression, _clock.UtcNow) is not { } nextOccurrence)
                return new TickerResult<TCronTicker>(
                    new TickerValidatorException($"Cannot parse expression {entity.Expression}"));

            
            entity.CreatedAt = _clock.UtcNow;
            entity.UpdatedAt = _clock.UtcNow;
            
            try
            {
                await _persistenceProvider.InsertCronTickers([entity], cancellationToken: cancellationToken);

                _tickerQHostScheduler.RestartIfNeeded(nextOccurrence);

                await _notificationHubSender.AddCronTickerNotifyAsync(entity.Id);

                return new TickerResult<TCronTicker>(entity);
            }
            catch (Exception e)
            {
                return new TickerResult<TCronTicker>(e);
            }
        }

        private async Task<TickerResult<TTimeTicker>> UpdateTimeTickerAsync(TTimeTicker timeTicker, CancellationToken cancellationToken)
        {
            if (timeTicker is null)
                return new TickerResult<TTimeTicker>(
                    new TickerValidatorException($"Ticker must not be null!"));
            
            if(timeTicker.ExecutionTime == null)
                return new TickerResult<TTimeTicker>(
                    new TickerValidatorException($"Ticker ExecutionTime must not be null!"));

            if (ValidateAndStampTimeTickerGraph([timeTicker]) is { } requestError)
                return new TickerResult<TTimeTicker>(requestError);
            
            timeTicker.UpdatedAt = _clock.UtcNow;
            timeTicker.ExecutionTime = ConvertToUtcIfNeeded(timeTicker.ExecutionTime.Value);
            
            try {
                var affectedRows = await _persistenceProvider.UpdateTimeTickers([timeTicker], cancellationToken: cancellationToken).ConfigureAwait(false);
                
                if (_executionContext.Functions.Any(x => x.TickerId == timeTicker.Id))
                    _tickerQHostScheduler.Restart();
                else
                    _tickerQHostScheduler.RestartIfNeeded(timeTicker.ExecutionTime);
                
                return new TickerResult<TTimeTicker>(timeTicker, affectedRows);
            }
            catch (Exception e)
            {
                return new TickerResult<TTimeTicker>(e);
            }
        }

        private async Task<TickerResult<TCronTicker>> UpdateCronTickerAsync(TCronTicker cronTicker, CancellationToken cancellationToken = default)
        {
            if (cronTicker is null)
                return new TickerResult<TCronTicker>(new Exception($"Cron ticker must not be null!"));

            if (TickerFunctionProvider.TickerFunctions.All(x => x.Key != cronTicker?.Function))
                return new TickerResult<TCronTicker>(
                    new TickerValidatorException($"Cannot find TickerFunction with name {cronTicker.Function}"));

            if (ValidateAndStampRequest(cronTicker, cronTicker.Request) is { } requestError)
                return new TickerResult<TCronTicker>(requestError);


            if (CronScheduleCache.GetNextOccurrenceOrDefault(cronTicker.Expression, _clock.UtcNow) is not { } nextOccurrence)
                return new TickerResult<TCronTicker>(
                    new TickerValidatorException($"Cannot parse expression {cronTicker.Expression}"));
            
            try
            {
                cronTicker.UpdatedAt = _clock.UtcNow;

                var affectedRows = await _persistenceProvider.UpdateCronTickers([cronTicker], cancellationToken: cancellationToken);

                if (_executionContext.Functions.FirstOrDefault(x => x.ParentId == cronTicker.Id) is { } internalFunction)
                {
                    internalFunction.ResetUpdateProps()
                        .SetProperty(x => x.ExecutionTime, nextOccurrence);
                    
                    await _persistenceProvider.UpdateCronTickerOccurrence(internalFunction, cancellationToken: cancellationToken).ConfigureAwait(false);
                    
                    _tickerQHostScheduler.Restart();
                }
                
                _tickerQHostScheduler.RestartIfNeeded(nextOccurrence);

                return new TickerResult<TCronTicker>(cronTicker, affectedRows);
            }
            catch (Exception e)
            {
                return new TickerResult<TCronTicker>(e);
            }
        }

        private async Task<TickerResult<TCronTicker>> DeleteCronTickerAsync(Guid id, CancellationToken cancellationToken = default)
        {
            var affectedRows = await _persistenceProvider.RemoveCronTickers([id], cancellationToken: cancellationToken);
            
            if(affectedRows > 0 && _executionContext.Functions.Any(x => x.ParentId == id))
                _tickerQHostScheduler.Restart();

            return new TickerResult<TCronTicker>(affectedRows);
        }


        private async Task<TickerResult<TTimeTicker>> DeleteTimeTickerAsync(Guid id, CancellationToken cancellationToken = default)
        {
            var affectedRows = await _persistenceProvider.RemoveTimeTickers([id], cancellationToken: cancellationToken);
            
            if(affectedRows > 0 && _executionContext.Functions.Any(x => x.TickerId == id))
                _tickerQHostScheduler.Restart();

            return new TickerResult<TTimeTicker>(affectedRows);
        }

        private async Task<TickerResult<TTimeTicker>> ReplaceTimeTickerChainAsync(
            Guid oldRootId, TTimeTicker newRoot, CancellationToken cancellationToken)
        {
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
            if (existing == null)
                return new TickerResult<TTimeTicker>(
                    new TickerValidatorException($"Chain root '{oldRootId}' was not found."));

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

                await _notificationHubSender.AddTimeTickerNotifyAsync(newRoot.Id).ConfigureAwait(false);

                if (_executionContext.Functions.Any(x => x.TickerId == oldRootId))
                    _tickerQHostScheduler.Restart();
                else
                    _tickerQHostScheduler.RestartIfNeeded(newRoot.ExecutionTime!.Value);

                return new TickerResult<TTimeTicker>(newRoot, inserted);
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

        private static TickerValidatorException ValidateAndStampRequest(BaseTickerEntity entity, byte[] request)
        {
            var validation = TickerRequestPayloadValidator.Validate(entity.Function, request);
            return StampAcceptedRequest(entity, validation);
        }

        private static TickerValidatorException ValidateAndStampTimeTickerGraph(IEnumerable<TTimeTicker> roots)
        {
            var snapshot = TickerFunctionProvider.Snapshot;
            var accepted = new List<(TTimeTicker Entity, TickerRequestValidationResult Validation)>();
            // Per-operation, reference-identity active-path set. A node reappearing while it is
            // still on the current recursion stack is a cycle; a node reached again after it has
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
            // Reject cycles before recursing: an entity already on the active path would otherwise
            // recurse forever and terminate the process with an uncatchable StackOverflowException.
            if (!activePath.Add(entity))
                return new TickerValidatorException(
                    $"Cyclic time ticker graph detected for TickerFunction '{entity.Function}': " +
                    "a ticker cannot appear within its own child hierarchy.");

            var validation = TickerRequestPayloadValidator.Validate(snapshot, entity.Function, entity.Request);
            if (!validation.IsValid)
                return CreateRequestValidationException(entity.Function, validation);

            accepted.Add((entity, validation));
            foreach (var child in entity.Children)
            {
                if (ValidateTimeTickerGraph(child, snapshot, accepted, activePath) is { } error)
                    return error;
            }

            // Pop on the way out so a node shared across sibling branches (a DAG, not a cycle)
            // is not mistaken for an active-path cycle when reached again.
            activePath.Remove(entity);
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
        {
            for (var i = 0; i < functions.Length; i++)
            {
                ref var context = ref functions[i];
                if (TickerFunctionProvider.TickerFunctions.TryGetValue(context.FunctionName, out var tickerItem))
                {
                    context.CachedDelegate = tickerItem.Delegate;
                    context.CachedPriority = tickerItem.Priority;
                    context.CachedMaxConcurrency = tickerItem.MaxConcurrency;
                }

                if (context.TimeTickerChildren is { Count: > 0 })
                {
                    var childrenSpan = CollectionsMarshal.AsSpan(context.TimeTickerChildren);
                    CacheFunctionReferences(childrenSpan);
                }
            }
        }

        private static InternalFunctionContext[] BuildImmediateContextsFromNonGeneric(IEnumerable<TimeTickerEntity> tickers)
        {
            return tickers.Select(BuildContextFromNonGeneric).ToArray();
        }

        private static InternalFunctionContext BuildContextFromNonGeneric(TimeTickerEntity ticker)
        {
            return new InternalFunctionContext
            {
                FunctionName = ticker.Function,
                RequestContractVersion = ticker.RequestContractVersion,
                RequestContractFingerprint = ticker.RequestContractFingerprint,
                TickerId = ticker.Id,
                Type = Enums.TickerType.TimeTicker,
                Retries = ticker.Retries,
                RetryIntervals = ticker.RetryIntervals,
                TimeoutSeconds = ticker.TimeoutSeconds,
                ParentId = ticker.ParentId,
                AcquisitionToken = ticker.AcquisitionToken,
                ExecutionTime = ticker.ExecutionTime ?? DateTime.UtcNow,
                RunCondition = ticker.RunCondition ?? Enums.RunCondition.OnAnyCompletedStatus,
                TimeTickerChildren = ticker.Children.Select(BuildContextFromNonGeneric).ToList()
            };
        }

        private async Task<TickerResult<List<TTimeTicker>>> AddTimeTickersBatchAsync(List<TTimeTicker> entities, CancellationToken cancellationToken = default)
        {
            if (entities == null || entities.Count == 0)
                return new TickerResult<List<TTimeTicker>>(entities ?? new List<TTimeTicker>());

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
                await _persistenceProvider.AddTimeTickers(entities.ToArray(), cancellationToken: cancellationToken);

                await _notificationHubSender.AddTimeTickersBatchNotifyAsync().ConfigureAwait(false);

                // Only try to dispatch immediately if dispatcher is enabled (background services running)
                if (_dispatcher.IsEnabled && immediateTickers.Count > 0)
                {
                    var acquired = await _persistenceProvider
                        .AcquireImmediateTimeTickersAsync(immediateTickers.ToArray(), cancellationToken)
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

                if (earliestForNonImmediate != default)
                {
                    _tickerQHostScheduler.RestartIfNeeded(earliestForNonImmediate);
                }

                return new TickerResult<List<TTimeTicker>>(entities);
            }
            catch (Exception e)
            {
                return new TickerResult<List<TTimeTicker>>(e);
            }
        }

        private async Task<TickerResult<List<TCronTicker>>> AddCronTickersBatchAsync(List<TCronTicker> entities, CancellationToken cancellationToken = default)
        {
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

                if (ValidateAndStampRequest(entity, entity.Request) is { } requestError)
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
                await _persistenceProvider.InsertCronTickers(validEntities.ToArray(), cancellationToken: cancellationToken);

                if (validEntities.Count != 0)
                {
                    // Restart scheduler for earliest occurrence
                    var earliestOccurrence = nextOccurrences.Min();
                    _tickerQHostScheduler.RestartIfNeeded(earliestOccurrence);

                    // Send notifications for all
                    foreach (var entity in validEntities)
                    {
                        await _notificationHubSender.AddCronTickerNotifyAsync(entity.Id);
                    }
                }

                return new TickerResult<List<TCronTicker>>(validEntities);
            }
            catch (Exception e)
            {
                return new TickerResult<List<TCronTicker>>(e);
            }
        }

        private async Task<TickerResult<List<TTimeTicker>>> UpdateTimeTickersBatchAsync(List<TTimeTicker> timeTickers, CancellationToken cancellationToken = default)
        {
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
                
                if (needsRestart)
                    _tickerQHostScheduler.Restart();
                else if (validTickers.Any())
                {
                    var earliestExecution = validTickers.Min(t => t.ExecutionTime);
                    _tickerQHostScheduler.RestartIfNeeded(earliestExecution);
                }
                
                return new TickerResult<List<TTimeTicker>>(validTickers, affectedRows);
            }
            catch (Exception e)
            {
                return new TickerResult<List<TTimeTicker>>(e);
            }
        }

        private async Task<TickerResult<List<TCronTicker>>> UpdateCronTickersBatchAsync(List<TCronTicker> cronTickers, CancellationToken cancellationToken = default)
        {
            var validTickers = new List<TCronTicker>();
            var errors = new List<Exception>();
            var nextOccurrences = new List<DateTime>();
            var needsRestart = false;
            var internalFunctionsToUpdate = new List<InternalFunctionContext>();
            
            foreach (var cronTicker in cronTickers)
            {
                if (cronTicker is null)
                {
                    errors.Add(new Exception("Cron ticker must not be null!"));
                    continue;
                }

                if (TickerFunctionProvider.TickerFunctions.All(x => x.Key != cronTicker?.Function))
                {
                    errors.Add(new TickerValidatorException($"Cannot find TickerFunction with name {cronTicker.Function}"));
                    continue;
                }

                if (ValidateAndStampRequest(cronTicker, cronTicker.Request) is { } requestError)
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
                    internalFunction.ResetUpdateProps()
                        .SetProperty(x => x.ExecutionTime, nextOccurrence);
                    internalFunctionsToUpdate.Add(internalFunction);
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

                // Update internal functions for those that need it
                foreach (var internalFunction in internalFunctionsToUpdate)
                {
                    await _persistenceProvider.UpdateCronTickerOccurrence(internalFunction, cancellationToken: cancellationToken).ConfigureAwait(false);
                }
                
                if (needsRestart)
                    _tickerQHostScheduler.Restart();
                
                else if (nextOccurrences.Any())
                {
                    var earliestOccurrence = nextOccurrences.Min();
                    _tickerQHostScheduler.RestartIfNeeded(earliestOccurrence);
                }

                return new TickerResult<List<TCronTicker>>(validTickers, affectedRows);
            }
            catch (Exception e)
            {
                return new TickerResult<List<TCronTicker>>(e);
            }
        }

        private async Task<TickerResult<TTimeTicker>> DeleteTimeTickersBatchAsync(List<Guid> ids, CancellationToken cancellationToken = default)
        {
            var affectedRows = await _persistenceProvider.RemoveTimeTickers(ids.ToArray(), cancellationToken: cancellationToken);
            
            if (affectedRows > 0 && _executionContext.Functions.Any(x => ids.Contains(x.TickerId)))
                _tickerQHostScheduler.Restart();

            return new TickerResult<TTimeTicker>(affectedRows);
        }

        private async Task<TickerResult<TCronTicker>> DeleteCronTickersBatchAsync(List<Guid> ids, CancellationToken cancellationToken = default)
        {
            var affectedRows = await _persistenceProvider.RemoveCronTickers(ids.ToArray(), cancellationToken: cancellationToken);
            
            if (affectedRows > 0 && _executionContext.Functions.Any(x => ids.Contains(x.ParentId ?? Guid.Empty)))
                _tickerQHostScheduler.Restart();

            return new TickerResult<TCronTicker>(affectedRows);
        }
    }
}
