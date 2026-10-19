using TickerQ.Utilities.Entities;

namespace TickerQ.Utilities.Interfaces;

/// <summary>
/// Optional flat dashboard metadata source for providers whose normal entity query
/// reconstructs navigation graphs. Rows omit request payloads; Children is only an
/// existence marker for filtering. Exact counts/statuses come from flat ParentId rows.
/// </summary>
public interface ITickerDashboardQueryProvider<TTimeTicker> where TTimeTicker : TimeTickerEntity<TTimeTicker>
{
    ITickerQueryable<TTimeTicker> TimeTickersDashboardQuery();
}
