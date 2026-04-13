using System;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Models;

namespace TickerQ.Utilities.Interfaces
{
    /// <summary>
    /// Provider-agnostic queryable wrapper. Each persistence provider implements
    /// this interface to translate fluent calls into its own query mechanism
    /// (EF Core LINQ, Dapper SQL, etc.).
    /// </summary>
    public interface ITickerQueryable<TEntity>
    {
        /// <summary>
        /// Filters entities by predicate.
        /// </summary>
        ITickerQueryable<TEntity> Where(Expression<Func<TEntity, bool>> predicate);

        /// <summary>
        /// Includes related entities. Provider translates to its own loading strategy.
        /// </summary>
        ITickerQueryable<TEntity> WithRelated(params TickerRelation[] relations);

        /// <summary>
        /// Orders results ascending by key.
        /// </summary>
        ITickerQueryable<TEntity> OrderBy<TKey>(Expression<Func<TEntity, TKey>> keySelector);

        /// <summary>
        /// Orders results descending by key.
        /// </summary>
        ITickerQueryable<TEntity> OrderByDescending<TKey>(Expression<Func<TEntity, TKey>> keySelector);

        /// <summary>
        /// Skips N entities.
        /// </summary>
        ITickerQueryable<TEntity> Skip(int count);

        /// <summary>
        /// Takes N entities.
        /// </summary>
        ITickerQueryable<TEntity> Take(int count);

        /// <summary>
        /// Disables change tracking (read-only query).
        /// </summary>
        ITickerQueryable<TEntity> AsNoTracking();

        /// <summary>
        /// Materializes the query as an array.
        /// </summary>
        Task<TEntity[]> ToArrayAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns the first matching entity or default.
        /// </summary>
        Task<TEntity> FirstOrDefaultAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns the count of matching entities.
        /// </summary>
        Task<int> CountAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns a paginated result.
        /// </summary>
        Task<PaginationResult<TEntity>> ToPaginatedAsync(int pageNumber, int pageSize, CancellationToken cancellationToken = default);
    }
}
