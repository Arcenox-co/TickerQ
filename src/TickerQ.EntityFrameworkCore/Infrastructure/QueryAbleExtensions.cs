using System;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace TickerQ.EntityFrameworkCore.Infrastructure
{
    internal static class QueryAbleExtensions
    {
        // Use instead of AnyAsync. Any() returns a SQL boolean (CASE WHEN EXISTS(...) THEN TRUE ELSE FALSE),
        // which the Oracle provider only emits correctly for 23ai unless the application lowers its SQL
        // compatibility; Oracle 19c/21c reject it with ORA-00904. Reading a constant from at most one row
        // costs the same as EXISTS and works on every provider and server version.
        public static async Task<bool> ExistsAsync<T>(this IQueryable<T> source, CancellationToken cancellationToken)
            => await source.Select(_ => 1).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false) == 1;

        public static Task<bool> ExistsAsync<T>(this IQueryable<T> source, Expression<Func<T, bool>> predicate,
            CancellationToken cancellationToken)
            => source.Where(predicate).ExistsAsync(cancellationToken);
    }
}
