using System;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ.Utilities.Infrastructure;

/// <summary>
/// Keeps predicates, ordering, counts and paging on the provider's original query,
/// then projects only dashboard metadata at materialization. Includes are unnecessary:
/// chain summaries read bounded flat rows separately.
/// </summary>
internal sealed class DashboardMetadataQueryable<TEntity> : ITickerQueryable<TEntity>
{
    private readonly ITickerProjectionQueryable<TEntity> _source;
    private readonly Expression<Func<TEntity, TEntity>> _selector;

    private DashboardMetadataQueryable(ITickerProjectionQueryable<TEntity> source, Expression<Func<TEntity, TEntity>> selector)
    {
        _source = source;
        _selector = selector;
    }

    internal static ITickerQueryable<TEntity> Create(ITickerQueryable<TEntity> source, Expression<Func<TEntity, TEntity>> selector)
        => source is ITickerProjectionQueryable<TEntity> { SupportsProjection: true } projected
            ? new DashboardMetadataQueryable<TEntity>(projected, selector)
            : source; // Third-party providers keep their existing relationship-loading behavior.

    internal static async Task<ITickerQueryable<TEntity>> WhereHasChildrenAsync(
        ITickerQueryable<TEntity> source, bool hasChildren,
        Expression<Func<TEntity, bool>> navigationPredicate, CancellationToken cancellationToken)
    {
        if (source is DashboardMetadataQueryable<TEntity> wrapper && wrapper._source is ITickerChildQueryable<TEntity> children)
            return wrapper.Wrap(await children.WhereHasChildrenAsync(hasChildren, cancellationToken).ConfigureAwait(false));
        return source.Where(navigationPredicate);
    }

    private ITickerQueryable<TEntity> Wrap(ITickerQueryable<TEntity> source) => Create(source, _selector);
    public ITickerQueryable<TEntity> Where(Expression<Func<TEntity, bool>> predicate) => Wrap(_source.Where(predicate));
    public ITickerQueryable<TEntity> OrderBy<TKey>(Expression<Func<TEntity, TKey>> keySelector) => Wrap(_source.OrderBy(keySelector));
    public ITickerQueryable<TEntity> OrderByDescending<TKey>(Expression<Func<TEntity, TKey>> keySelector) => Wrap(_source.OrderByDescending(keySelector));
    public ITickerQueryable<TEntity> Skip(int count) => Wrap(_source.Skip(count));
    public ITickerQueryable<TEntity> Take(int count) => Wrap(_source.Take(count));
    public ITickerQueryable<TEntity> AsNoTracking() => Wrap(_source.AsNoTracking());
    public ITickerQueryable<TEntity> WithRelated(params TickerRelation[] relations) => this;
    public Task<TEntity[]> ToArrayAsync(CancellationToken cancellationToken = default) => _source.ToArrayAsync(_selector, cancellationToken);
    public async Task<TEntity> FirstOrDefaultAsync(CancellationToken cancellationToken = default)
        => (await Wrap(_source.Take(1)).ToArrayAsync(cancellationToken).ConfigureAwait(false)).FirstOrDefault();
    public Task<int> CountAsync(CancellationToken cancellationToken = default) => _source.CountAsync(cancellationToken);
    public Task<PaginationResult<TEntity>> ToPaginatedAsync(int pageNumber, int pageSize, CancellationToken cancellationToken = default)
        => _source.ToPaginatedAsync(_selector, pageNumber, pageSize, cancellationToken);
}
