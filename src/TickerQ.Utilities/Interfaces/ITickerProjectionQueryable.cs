using System;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Models;

namespace TickerQ.Utilities.Interfaces;

/// <summary>
/// Optional native projection capability. Existing custom query providers remain compatible;
/// callers can fall back to entity materialization when this capability is unavailable.
/// </summary>
public interface ITickerProjectionQueryable<TEntity> : ITickerQueryable<TEntity>
{
    /// <summary>Whether this query can project without requiring its legacy relationship graph.</summary>
    bool SupportsProjection => true;

    Task<TResult[]> ToArrayAsync<TResult>(Expression<Func<TEntity, TResult>> selector,
        CancellationToken cancellationToken = default);

    Task<PaginationResult<TResult>> ToPaginatedAsync<TResult>(Expression<Func<TEntity, TResult>> selector,
        int pageNumber, int pageSize, CancellationToken cancellationToken = default);
}
