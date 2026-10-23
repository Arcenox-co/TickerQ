using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Infrastructure;

namespace TickerQ.Provider;

internal partial class TickerInMemoryPersistenceProvider<TTimeTicker, TCronTicker> : ITickerDashboardQueryProvider<TTimeTicker>
{
    public ITickerQueryable<TTimeTicker> TimeTickersDashboardQuery()
        // These rows are already detached, payload-free dashboard snapshots. Advertising
        // projection would execute the generic member-init expression through the runtime
        // expression interpreter under Native AOT. That interpreter cannot invoke every
        // virtual generic entity setter (notably when a value flows through a by-ref call
        // site), so keep the already-materialized snapshot as the query result.
        => new InMemoryTickerQueryable<TTimeTicker>(LoadDashboardTimeRows);

    private Task<List<TTimeTicker>> LoadDashboardTimeRows(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ReadGraph(() =>
        {
            var parents = TimeTickers.Values.Where(x => x.ParentId.HasValue)
                .Select(x => x.ParentId.Value).ToHashSet();
            // Dashboard predicates only inspect Children.Any(). Use one shared marker
            // instead of reconstructing every descendant navigation graph. Actual counts
            // and chain statuses are computed from bounded flat ParentId rows.
            var hasChildren = new[] { new TTimeTicker() };
            var empty = Array.Empty<TTimeTicker>();
            return TimeTickers.Values.Select(ticker =>
            {
                var row = ticker.CreateQuerySnapshot();
                row.Request = null;
                row.Children = parents.Contains(ticker.Id) ? hasChildren : empty;
                return row;
            }).ToList();
        }));
    }
}
