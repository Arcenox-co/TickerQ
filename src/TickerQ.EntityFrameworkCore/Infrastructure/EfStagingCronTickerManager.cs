using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Managers;
using TickerQ.Utilities.Models;

namespace TickerQ.EntityFrameworkCore.Infrastructure;

// Captures only the resolving scope. Existing operations retain the original manager's behavior.
// Every interface method must be forwarded: one left out falls back to the interface's throwing default.
internal sealed class EfStagingCronTickerManager<TCronTicker>(
    ICronTickerManager<TCronTicker> inner,
    Func<TCronTicker, CancellationToken, Task<TCronTicker>> stage)
    : ICronTickerManager<TCronTicker>, ITickerManagerDecorator
    where TCronTicker : CronTickerEntity
{
    object ITickerManagerDecorator.InnerManager => inner;

    public Task<TCronTicker> StageAsync(TCronTicker entity, CancellationToken cancellationToken = default)
        => stage(entity, cancellationToken);
    public Task<TickerResult<TCronTicker>> AddAsync(TCronTicker entity, CancellationToken cancellationToken = default)
        => inner.AddAsync(entity, cancellationToken);
    public Task<TickerResult<TCronTicker>> AddChainAsync(Action<CronTickerChainBuilder<TCronTicker>> configure, CancellationToken cancellationToken = default)
        => inner.AddChainAsync(configure, cancellationToken);
    public Task<TickerResult<TCronTicker>> UpdateAsync(TCronTicker entity, CancellationToken cancellationToken = default)
        => inner.UpdateAsync(entity, cancellationToken);
    public Task<TickerResult<TCronTicker>> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        => inner.DeleteAsync(id, cancellationToken);
    public Task<TickerResult<CronTickerOccurrenceEntity<TCronTicker>>> RunNowAsync(Guid id, CancellationToken cancellationToken = default)
        => inner.RunNowAsync(id, cancellationToken);
    public Task<TickerResult<CronTickerOccurrenceEntity<TCronTicker>>> RerunOccurrenceAsync(Guid occurrenceId, CancellationToken cancellationToken = default)
        => inner.RerunOccurrenceAsync(occurrenceId, cancellationToken);
    public Task<TickerResult<TCronTicker>> RequestCancellationAsync(Guid occurrenceId, CancellationToken cancellationToken = default)
        => inner.RequestCancellationAsync(occurrenceId, cancellationToken);
    public Task<TickerResult<TCronTicker>> SetEnabledAsync(Guid id, bool isEnabled, CancellationToken cancellationToken = default)
        => inner.SetEnabledAsync(id, isEnabled, cancellationToken);
    public Task<TickerResult<TCronTicker>> AddAsync<TFunction>(string expression, CancellationToken cancellationToken = default)
        where TFunction : class, ITickerFunction => inner.AddAsync<TFunction>(expression, cancellationToken);
    public Task<TickerResult<TCronTicker>> AddAsync<TFunction, TRequest>(string expression, TRequest request, CancellationToken cancellationToken = default)
        where TFunction : class, ITickerFunction<TRequest> => inner.AddAsync<TFunction, TRequest>(expression, request, cancellationToken);
    public Task<TickerResult<TCronTicker>> AddAsync<TFunction>(CronSchedule schedule, CancellationToken cancellationToken = default)
        where TFunction : class, ITickerFunction => inner.AddAsync<TFunction>(schedule, cancellationToken);
    public Task<TickerResult<TCronTicker>> AddAsync<TFunction, TRequest>(CronSchedule schedule, TRequest request, CancellationToken cancellationToken = default)
        where TFunction : class, ITickerFunction<TRequest> => inner.AddAsync<TFunction, TRequest>(schedule, request, cancellationToken);
    public Task<TickerResult<IReadOnlyList<DateTime>>> GetNextOccurrencesAsync(Guid cronTickerId, int count, CancellationToken cancellationToken = default)
        => inner.GetNextOccurrencesAsync(cronTickerId, count, cancellationToken);
    public Task<TickerOutcome> GetOutcomeAsync(Guid occurrenceId, CancellationToken cancellationToken = default)
        => inner.GetOutcomeAsync(occurrenceId, cancellationToken);
    public Task<TickerOutcome> WaitForCompletionAsync(Guid occurrenceId, TimeSpan timeout, CancellationToken cancellationToken = default)
        => inner.WaitForCompletionAsync(occurrenceId, timeout, cancellationToken);
    public Task<TickerResult<List<TCronTicker>>> AddBatchAsync(List<TCronTicker> entities, CancellationToken cancellationToken = default)
        => inner.AddBatchAsync(entities, cancellationToken);
    public Task<TickerResult<List<TCronTicker>>> UpdateBatchAsync(List<TCronTicker> entities, CancellationToken cancellationToken = default)
        => inner.UpdateBatchAsync(entities, cancellationToken);
    public Task<TickerResult<TCronTicker>> DeleteBatchAsync(List<Guid> ids, CancellationToken cancellationToken = default)
        => inner.DeleteBatchAsync(ids, cancellationToken);
}

internal sealed record EfOriginalCronTickerManager<TCronTicker>(ICronTickerManager<TCronTicker> Manager)
    where TCronTicker : CronTickerEntity;
