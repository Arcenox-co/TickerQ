using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;

namespace TickerQ.Provider;

internal partial class TickerInMemoryPersistenceProvider<TTimeTicker, TCronTicker> : ITickerDashboardStatisticsProvider
{
    public Task<IReadOnlyDictionary<TickerStatus, int>> GetOverallJobStatusCountsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyDictionary<TickerStatus, int>>(ReadGraph(() =>
            TimeTickers.Values.Select(row => row.Status).Concat(CronOccurrences.Values.Select(row => row.Status))
                .GroupBy(status => status).ToDictionary(group => group.Key, group => group.Count())));
    }
}
