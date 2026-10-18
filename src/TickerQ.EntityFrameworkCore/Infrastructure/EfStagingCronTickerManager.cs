using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Models;

namespace TickerQ.EntityFrameworkCore.Infrastructure;

// Captures only the resolving scope. Existing operations retain the original manager's behavior.
internal sealed class EfStagingCronTickerManager<TCronTicker>(
    ICronTickerManager<TCronTicker> inner,
    Func<TCronTicker, CancellationToken, Task<TCronTicker>> stage)
    : ICronTickerManager<TCronTicker>
    where TCronTicker : CronTickerEntity
{
    public Task<TCronTicker> StageAsync(TCronTicker entity, CancellationToken cancellationToken = default)
        => stage(entity, cancellationToken);
    public Task<TickerResult<TCronTicker>> AddAsync(TCronTicker entity, CancellationToken cancellationToken = default)
        => inner.AddAsync(entity, cancellationToken);
    public Task<TickerResult<TCronTicker>> UpdateAsync(TCronTicker entity, CancellationToken cancellationToken = default)
        => inner.UpdateAsync(entity, cancellationToken);
    public Task<TickerResult<TCronTicker>> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        => inner.DeleteAsync(id, cancellationToken);
    public Task<TickerResult<CronTickerOccurrenceEntity<TCronTicker>>> RunNowAsync(Guid id, CancellationToken cancellationToken = default)
        => inner.RunNowAsync(id, cancellationToken);
    public Task<TickerResult<TCronTicker>> RequestCancellationAsync(Guid occurrenceId, CancellationToken cancellationToken = default)
        => inner.RequestCancellationAsync(occurrenceId, cancellationToken);
    public Task<TickerResult<TCronTicker>> SetEnabledAsync(Guid id, bool isEnabled, CancellationToken cancellationToken = default)
        => inner.SetEnabledAsync(id, isEnabled, cancellationToken);
    public Task<TickerResult<TCronTicker>> AddAsync<TFunction>(string expression, CancellationToken cancellationToken = default)
        where TFunction : class, ITickerFunction => inner.AddAsync<TFunction>(expression, cancellationToken);
    public Task<TickerResult<TCronTicker>> AddAsync<TFunction, TRequest>(string expression, TRequest request, CancellationToken cancellationToken = default)
        where TFunction : class, ITickerFunction<TRequest> => inner.AddAsync<TFunction, TRequest>(expression, request, cancellationToken);
    public Task<TickerResult<List<TCronTicker>>> AddBatchAsync(List<TCronTicker> entities, CancellationToken cancellationToken = default)
        => inner.AddBatchAsync(entities, cancellationToken);
    public Task<TickerResult<List<TCronTicker>>> UpdateBatchAsync(List<TCronTicker> entities, CancellationToken cancellationToken = default)
        => inner.UpdateBatchAsync(entities, cancellationToken);
    public Task<TickerResult<TCronTicker>> DeleteBatchAsync(List<Guid> ids, CancellationToken cancellationToken = default)
        => inner.DeleteBatchAsync(ids, cancellationToken);
}

internal sealed record EfOriginalCronTickerManager<TCronTicker>(ICronTickerManager<TCronTicker> Manager)
    where TCronTicker : CronTickerEntity;
