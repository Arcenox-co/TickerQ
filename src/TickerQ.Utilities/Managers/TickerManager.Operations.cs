using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Exceptions;
using TickerQ.Utilities.Infrastructure;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Models;

namespace TickerQ.Utilities.Managers;

internal partial class TickerManager<TTimeTicker, TCronTicker>
{
    async Task<TickerResult<TTimeTicker>> ITimeTickerManager<TTimeTicker>.RunNowAsync(Guid id, CancellationToken cancellationToken)
    {
        await WaitForMutationAdmissionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var accepted = await OnDemandTimeExecution.RunAsync(_persistenceProvider, _dispatcher,
                _notificationHubSender, _executionContext.RuntimePartition.StorageKey, id, _clock.UtcNow,
                cancellationToken, _logger).ConfigureAwait(false);
            return accepted ? new TickerResult<TTimeTicker>(1)
                : new TickerResult<TTimeTicker>(new TickerValidatorException("The time ticker was not found, is a chain child, is already active, or could not be acquired."));
        }
        catch (Exception exception) { return new TickerResult<TTimeTicker>(exception); }
    }

    async Task<TickerResult<CronTickerOccurrenceEntity<TCronTicker>>> ICronTickerManager<TCronTicker>.RunNowAsync(Guid cronTickerId, CancellationToken cancellationToken)
    {
        await WaitForMutationAdmissionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var occurrence = await OnDemandCronExecution.RunAsync(_persistenceProvider, _dispatcher,
                _notificationHubSender, _executionContext.RuntimePartition.StorageKey, cronTickerId,
                cancellationToken, _logger, _clock.UtcNow, _lifecycle).ConfigureAwait(false);
            return new TickerResult<CronTickerOccurrenceEntity<TCronTicker>>(occurrence, 1);
        }
        catch (Exception exception) { return new TickerResult<CronTickerOccurrenceEntity<TCronTicker>>(exception); }
    }

    Task<TickerResult<TTimeTicker>> ITimeTickerManager<TTimeTicker>.RequestCancellationAsync(Guid id, CancellationToken cancellationToken)
        => CancelExecutionAsync<TTimeTicker>(id, TickerType.TimeTicker, cancellationToken);

    Task<TickerResult<TCronTicker>> ICronTickerManager<TCronTicker>.RequestCancellationAsync(Guid occurrenceId, CancellationToken cancellationToken)
        => CancelExecutionAsync<TCronTicker>(occurrenceId, TickerType.CronTickerOccurrence, cancellationToken);

    private async Task<TickerResult<TEntity>> CancelExecutionAsync<TEntity>(Guid id, TickerType type, CancellationToken cancellationToken) where TEntity : class
    {
        cancellationToken.ThrowIfCancellationRequested();
        bool signalled;
        try
        {
            signalled = TickerCancellationTokenManager.RequestTickerCancellation(
                new TickerExecutionKey(_executionContext.RuntimePartition.StorageKey, type, id));
        }
        catch (AggregateException exception)
        {
            // CancellationTokenSource sets cancellation before invoking callbacks. A
            // failing user callback must not report that the request was never sent.
            signalled = true;
            _logger.LogWarning(exception, "Execution {TickerId} was signalled for cancellation, but a cancellation callback failed.", id);
        }
        if (signalled) await NotifyAsync(() => _notificationHubSender.CanceledTickerNotifyAsync(id)).ConfigureAwait(false);
        return new TickerResult<TEntity>(signalled ? 1 : 0);
    }

    async Task<TickerResult<TCronTicker>> ICronTickerManager<TCronTicker>.SetEnabledAsync(Guid id, bool isEnabled, CancellationToken cancellationToken)
    {
        await WaitForMutationAdmissionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var existing = await _persistenceProvider.GetCronTickerById(id, cancellationToken).ConfigureAwait(false);
            if (existing == null)
                return new TickerResult<TCronTicker>(new TickerValidatorException("Cron ticker was not found."));
            // Some providers return live objects. Never edit the source before persistence accepts it.
            var cron = (TCronTicker)existing.CreateShallowSnapshot();
            cron.IsEnabled = isEnabled;
            cron.UpdatedAt = _clock.UtcNow;
            var affected = await _persistenceProvider.UpdateCronTickers([cron], cancellationToken).ConfigureAwait(false);
            if (affected <= 0)
                return new TickerResult<TCronTicker>(new TickerValidatorException("Cron ticker was not changed. Refresh and try again."));
            return await CompleteMutationAsync(new TickerResult<TCronTicker>(cron, affected), async () =>
            {
                await NotifyAsync(() => _notificationHubSender.UpdateCronTickerNotifyAsync(id)).ConfigureAwait(false);
                _tickerQHostScheduler.Restart();
            }).ConfigureAwait(false);
        }
        catch (Exception exception) { return new TickerResult<TCronTicker>(exception); }
    }

    Task<TickerResult<TCronTicker>> ICronTickerManager<TCronTicker>.AddAsync<TFunction>(string expression, CancellationToken cancellationToken)
    {
        try
        {
            return AddCronTickerAsync(new TCronTicker { Function = TickerFunctionProvider.GetFunctionName<TFunction>(), Expression = expression }, cancellationToken);
        }
        catch (Exception exception) { return Task.FromResult(new TickerResult<TCronTicker>(exception)); }
    }

    Task<TickerResult<TCronTicker>> ICronTickerManager<TCronTicker>.AddAsync<TFunction, TRequest>(string expression, TRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var function = TickerFunctionProvider.GetFunctionName<TFunction>();
            return AddCronTickerAsync(new TCronTicker
            {
                Function = function,
                Expression = expression,
                Request = TickerHelper.CreateTickerRequest(request, function)
            }, cancellationToken);
        }
        catch (Exception exception) { return Task.FromResult(new TickerResult<TCronTicker>(exception)); }
    }
}
