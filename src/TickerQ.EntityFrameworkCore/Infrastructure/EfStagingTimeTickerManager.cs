using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Managers;
using TickerQ.Utilities.Models;

namespace TickerQ.EntityFrameworkCore.Infrastructure;

// A lightweight facade captures the resolving scope, never a DbContext in a singleton.
// Ordinary operations still delegate to the original singleton manager, including when
// resolved from the root provider. Only StageAsync requires a real application scope.
// Every interface method must be forwarded: one left out falls back to the interface's throwing default.
internal sealed class EfStagingTimeTickerManager<TTimeTicker>(
    ITimeTickerManager<TTimeTicker> inner,
    Func<TTimeTicker, CancellationToken, Task<TTimeTicker>> stage)
    : ITimeTickerManager<TTimeTicker>, ITickerManagerDecorator
    where TTimeTicker : TimeTickerEntity<TTimeTicker>
{
    object ITickerManagerDecorator.InnerManager => inner;

    public Task<TTimeTicker> StageAsync(TTimeTicker entity, CancellationToken cancellationToken = default)
        => stage(entity, cancellationToken);
    public Task<TickerResult<TTimeTicker>> AddAsync(TTimeTicker entity, CancellationToken cancellationToken = default)
        => inner.AddAsync(entity, cancellationToken);
    public Task<TickerResult<TTimeTicker>> AddChainAsync(Action<TimeTickerChainBuilder<TTimeTicker>> configure, CancellationToken cancellationToken = default)
        => inner.AddChainAsync(configure, cancellationToken);
    public Task<TickerResult<TTimeTicker>> UpdateAsync(TTimeTicker entity, CancellationToken cancellationToken = default)
        => inner.UpdateAsync(entity, cancellationToken);
    public Task<TickerResult<TTimeTicker>> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        => inner.DeleteAsync(id, cancellationToken);
    public Task<TickerResult<TTimeTicker>> CancelAsync(Guid id, CancellationToken cancellationToken = default)
        => inner.CancelAsync(id, cancellationToken);
    public Task<TickerResult<TTimeTicker>> RunNowAsync(Guid id, CancellationToken cancellationToken = default)
        => inner.RunNowAsync(id, cancellationToken);
    public Task<TickerResult<TTimeTicker>> RetryFromAsync(Guid id, CancellationToken cancellationToken = default)
        => inner.RetryFromAsync(id, cancellationToken);
    public Task<TickerResult<TTimeTicker>> RescheduleAsync(Guid id, DateTime executionTime, CancellationToken cancellationToken = default)
        => inner.RescheduleAsync(id, executionTime, cancellationToken);
    public Task<TickerResult<TTimeTicker>> CancelPendingAsync<TFunction>(CancellationToken cancellationToken = default)
        where TFunction : class, ITickerFunctionBase => inner.CancelPendingAsync<TFunction>(cancellationToken);
    public Task<TickerOutcome> GetOutcomeAsync(Guid id, CancellationToken cancellationToken = default)
        => inner.GetOutcomeAsync(id, cancellationToken);
    public Task<TickerOutcome> WaitForCompletionAsync(Guid id, TimeSpan timeout, CancellationToken cancellationToken = default)
        => inner.WaitForCompletionAsync(id, timeout, cancellationToken);
    public Task<TTimeTicker> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => inner.GetAsync(id, cancellationToken);
    public Task<IReadOnlyList<TTimeTicker>> ListAsync(CancellationToken cancellationToken = default)
        => inner.ListAsync(cancellationToken);
    public Task<IReadOnlyList<TTimeTicker>> ListAsync(Expression<Func<TTimeTicker, bool>> predicate, CancellationToken cancellationToken = default)
        => inner.ListAsync(predicate, cancellationToken);
    public Task<IReadOnlyList<TTimeTicker>> ListAsync<TFunction>(CancellationToken cancellationToken = default)
        where TFunction : class, ITickerFunctionBase => inner.ListAsync<TFunction>(cancellationToken);
    public Task<TickerResult<TTimeTicker>> DeleteAsync(Expression<Func<TTimeTicker, bool>> predicate, CancellationToken cancellationToken = default)
        => inner.DeleteAsync(predicate, cancellationToken);
    public Task<TickerResult<TTimeTicker>> RequestCancellationAsync(Guid id, CancellationToken cancellationToken = default)
        => inner.RequestCancellationAsync(id, cancellationToken);
    public Task<TickerResult<TTimeTicker>> SetDeleteBehaviorAsync(Guid id, ChainDeleteBehavior behavior, CancellationToken cancellationToken = default)
        => inner.SetDeleteBehaviorAsync(id, behavior, cancellationToken);
    public Task<TickerResult<TTimeTicker>> ReplaceChainAsync(Guid oldRootId, TTimeTicker newRoot, CancellationToken cancellationToken = default)
        => inner.ReplaceChainAsync(oldRootId, newRoot, cancellationToken);
    public Task<TickerResult<List<TTimeTicker>>> AddBatchAsync(List<TTimeTicker> entities, CancellationToken cancellationToken = default)
        => inner.AddBatchAsync(entities, cancellationToken);
    public Task<TickerResult<List<TTimeTicker>>> UpdateBatchAsync(List<TTimeTicker> entities, CancellationToken cancellationToken = default)
        => inner.UpdateBatchAsync(entities, cancellationToken);
    public Task<TickerResult<TTimeTicker>> DeleteBatchAsync(List<Guid> ids, CancellationToken cancellationToken = default)
        => inner.DeleteBatchAsync(ids, cancellationToken);
    public Task<TickerResult<TTimeTicker>> AddAsync<TFunction>(DateTime? executionTime = null, CancellationToken cancellationToken = default)
        where TFunction : class, ITickerFunction => inner.AddAsync<TFunction>(executionTime, cancellationToken);
    public Task<TickerResult<TTimeTicker>> AddAsync<TFunction, TRequest>(DateTime? executionTime, TRequest request, CancellationToken cancellationToken = default)
        where TFunction : class, ITickerFunction<TRequest> => inner.AddAsync<TFunction, TRequest>(executionTime, request, cancellationToken);
    public Task<TickerResult<TTimeTicker>> AddAsync<TFunction>(TimeSchedule schedule, CancellationToken cancellationToken = default)
        where TFunction : class, ITickerFunction => inner.AddAsync<TFunction>(schedule, cancellationToken);
    public Task<TickerResult<TTimeTicker>> AddAsync<TFunction, TRequest>(TimeSchedule schedule, TRequest request, CancellationToken cancellationToken = default)
        where TFunction : class, ITickerFunction<TRequest> => inner.AddAsync<TFunction, TRequest>(schedule, request, cancellationToken);
}

internal sealed record EfOriginalTimeTickerManager<TTimeTicker>(ITimeTickerManager<TTimeTicker> Manager)
    where TTimeTicker : TimeTickerEntity<TTimeTicker>;

internal sealed record EfStagingRootProvider(IServiceProvider Provider);
