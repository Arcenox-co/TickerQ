using System.Threading;
using System.Threading.Tasks;

namespace TickerQ.Utilities.Interfaces;

/// <summary>Optional child-existence filtering for stores that do not map Children as a navigation.</summary>
public interface ITickerChildQueryable<TEntity>
{
    Task<ITickerQueryable<TEntity>> WhereHasChildrenAsync(bool hasChildren,
        CancellationToken cancellationToken = default);
}
