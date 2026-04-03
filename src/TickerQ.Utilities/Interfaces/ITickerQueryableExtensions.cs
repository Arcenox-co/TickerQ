using System;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Enums;

namespace TickerQ.Utilities.Interfaces
{
    /// <summary>
    /// Convenience extensions for ITickerQueryable.
    /// </summary>
    public static class TickerQueryableExtensions
    {
        /// <summary>
        /// Combines Where + FirstOrDefault for a single entity lookup by predicate.
        /// </summary>
        public static Task<TEntity> FindAsync<TEntity>(
            this ITickerQueryable<TEntity> queryable,
            Expression<Func<TEntity, bool>> predicate,
            CancellationToken cancellationToken = default)
        {
            return queryable.Where(predicate).FirstOrDefaultAsync(cancellationToken);
        }

        /// <summary>
        /// Combines Where + Count for a filtered count.
        /// </summary>
        public static Task<int> CountAsync<TEntity>(
            this ITickerQueryable<TEntity> queryable,
            Expression<Func<TEntity, bool>> predicate,
            CancellationToken cancellationToken = default)
        {
            return queryable.Where(predicate).CountAsync(cancellationToken);
        }

        /// <summary>
        /// Checks if any entity matches the predicate.
        /// </summary>
        public static async Task<bool> AnyAsync<TEntity>(
            this ITickerQueryable<TEntity> queryable,
            Expression<Func<TEntity, bool>> predicate,
            CancellationToken cancellationToken = default)
        {
            return await queryable.Where(predicate).CountAsync(cancellationToken) > 0;
        }
    }
}
