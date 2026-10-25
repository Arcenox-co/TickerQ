using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Exceptions;
using TickerQ.Utilities.Infrastructure;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Models;

namespace TickerQ.Utilities.Managers;

// Reading tickers by id, filter, or function, and deleting them by filter. Reads and deletes go through the persistence
// provider, which scopes them to this application namespace and keeps running work out of deletes.
internal partial class TickerManager<TTimeTicker, TCronTicker>
{
    Task<TTimeTicker> ITimeTickerManager<TTimeTicker>.GetAsync(Guid id, CancellationToken cancellationToken)
        => _persistenceProvider.GetTimeTickerById(id, cancellationToken);

    Task<IReadOnlyList<TTimeTicker>> ITimeTickerManager<TTimeTicker>.ListAsync(CancellationToken cancellationToken)
        => ListTimeTickersAsync(null, cancellationToken);

    Task<IReadOnlyList<TTimeTicker>> ITimeTickerManager<TTimeTicker>.ListAsync(
        Expression<Func<TTimeTicker, bool>> predicate, CancellationToken cancellationToken)
        => ListTimeTickersAsync(predicate ?? throw new ArgumentNullException(nameof(predicate)), cancellationToken);

    Task<IReadOnlyList<TTimeTicker>> ITimeTickerManager<TTimeTicker>.ListAsync<TFunction>(CancellationToken cancellationToken)
    {
        var function = TickerFunctionProvider.GetFunctionName<TFunction>();
        return ListTimeTickersAsync(x => x.Function == function, cancellationToken);
    }

    async Task<TickerResult<TTimeTicker>> ITimeTickerManager<TTimeTicker>.DeleteAsync(
        Expression<Func<TTimeTicker, bool>> predicate, CancellationToken cancellationToken)
    {
        if (predicate is null)
            return new TickerResult<TTimeTicker>(new TickerValidatorException("The filter must not be null."));
        return await DeleteMatchingTimeTickersAsync(predicate, cancellationToken).ConfigureAwait(false);
    }

    Task<TCronTicker> ICronTickerManager<TCronTicker>.GetAsync(Guid id, CancellationToken cancellationToken)
        => _persistenceProvider.GetCronTickerById(id, cancellationToken);

    Task<IReadOnlyList<TCronTicker>> ICronTickerManager<TCronTicker>.ListAsync(CancellationToken cancellationToken)
        => ListCronTickersAsync(null, cancellationToken);

    Task<IReadOnlyList<TCronTicker>> ICronTickerManager<TCronTicker>.ListAsync(
        Expression<Func<TCronTicker, bool>> predicate, CancellationToken cancellationToken)
        => ListCronTickersAsync(predicate ?? throw new ArgumentNullException(nameof(predicate)), cancellationToken);

    Task<IReadOnlyList<TCronTicker>> ICronTickerManager<TCronTicker>.ListAsync<TFunction>(CancellationToken cancellationToken)
    {
        var function = TickerFunctionProvider.GetFunctionName<TFunction>();
        return ListCronTickersAsync(x => x.Function == function, cancellationToken);
    }

    async Task<TickerResult<TCronTicker>> ICronTickerManager<TCronTicker>.DeleteAsync(
        Expression<Func<TCronTicker, bool>> predicate, CancellationToken cancellationToken)
    {
        if (predicate is null)
            return new TickerResult<TCronTicker>(new TickerValidatorException("The filter must not be null."));
        return await DeleteMatchingCronTickersAsync(predicate, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<TTimeTicker>> ListTimeTickersAsync(
        Expression<Func<TTimeTicker, bool>> predicate, CancellationToken cancellationToken)
        => await _persistenceProvider.GetTimeTickers(predicate, cancellationToken).ConfigureAwait(false) ?? [];

    private async Task<IReadOnlyList<TCronTicker>> ListCronTickersAsync(
        Expression<Func<TCronTicker, bool>> predicate, CancellationToken cancellationToken)
        => await _persistenceProvider.GetCronTickers(predicate, cancellationToken).ConfigureAwait(false) ?? [];

    private async Task<TickerResult<TTimeTicker>> DeleteMatchingTimeTickersAsync(
        Expression<Func<TTimeTicker, bool>> predicate, CancellationToken cancellationToken)
    {
        await WaitForMutationAdmissionAsync(cancellationToken).ConfigureAwait(false);
        Guid[] ids;
        int affectedRows;
        try
        {
            // Providers return roots with their steps. A Restrict chain with unfinished steps is kept, as
            // DeleteAsync(id) would refuse it; the provider also skips running and leased trees.
            var roots = await _persistenceProvider.GetTimeTickers(predicate, cancellationToken).ConfigureAwait(false);
            ids = roots.Where(root => !ChainDeletionPolicy.BlocksDeletion(root)).Select(root => root.Id).ToArray();
            if (ids.Length == 0)
                return new TickerResult<TTimeTicker>(0);
            affectedRows = await _persistenceProvider.RemoveTimeTickers(ids, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) { return new TickerResult<TTimeTicker>(exception); }

        return await CompleteMutationAsync(new TickerResult<TTimeTicker>(affectedRows), async () =>
        {
            if (affectedRows <= 0) return;
            foreach (var id in ids)
                await NotifyAsync(() => _notificationHubSender.RemoveTimeTickerNotifyAsync(id)).ConfigureAwait(false);
            if (_executionContext.Functions.Any(x => ids.Contains(x.TickerId))) _tickerQHostScheduler.Restart();
        }).ConfigureAwait(false);
    }

    private async Task<TickerResult<TCronTicker>> DeleteMatchingCronTickersAsync(
        Expression<Func<TCronTicker, bool>> predicate, CancellationToken cancellationToken)
    {
        List<Guid> ids;
        try
        {
            // Seeded crons belong to SeedCronTickers; deleting one here would only last until reconciliation.
            var crons = await _persistenceProvider.GetCronTickers(predicate, cancellationToken).ConfigureAwait(false);
            ids = crons.Where(cron => cron.SeedKey == null).Select(cron => cron.Id).ToList();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) { return new TickerResult<TCronTicker>(exception); }

        return ids.Count == 0
            ? new TickerResult<TCronTicker>(0)
            : await DeleteCronTickersBatchAsync(ids, cancellationToken).ConfigureAwait(false);
    }
}
