using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Enums;

namespace TickerQ.Utilities.Interfaces;

/// <summary>Optional provider-native aggregation for dashboard status totals.</summary>
/// <remarks>Counts the same time rows as the provider's dashboard query and all occurrences,
/// within its application partition. Implementations return counts only, without job payloads.</remarks>
public interface ITickerDashboardStatisticsProvider
{
    Task<IReadOnlyDictionary<TickerStatus, int>> GetOverallJobStatusCountsAsync(
        CancellationToken cancellationToken = default);
}
