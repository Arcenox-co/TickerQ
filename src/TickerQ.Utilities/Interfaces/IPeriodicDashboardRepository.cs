using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.DashboardDtos;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Models;

namespace TickerQ.Utilities.Interfaces
{
    /// <summary>
    /// Dashboard-facing repository for periodic tickers.
    /// Separated from <see cref="ITickerDashboardRepository{TTimeTicker, TCronTicker}"/> so the dashboard
    /// can opt-in to periodic support without breaking existing two-source consumers.
    /// </summary>
    internal interface IPeriodicDashboardRepository<TPeriodicTicker>
        where TPeriodicTicker : PeriodicTickerEntity, new()
    {
        Task<TPeriodicTicker[]> GetPeriodicTickersAsync(CancellationToken cancellationToken = default);

        Task<PaginationResult<TPeriodicTicker>> GetPeriodicTickersPaginatedAsync(
            int pageNumber, int pageSize, bool? isActive = null, string search = null,
            CancellationToken cancellationToken = default);

        Task<IList<Tuple<TickerStatus, int>>> GetPeriodicTickerFullDataAsync(CancellationToken cancellationToken = default);

        Task<IList<TickerGraphData>> GetPeriodicTickersGraphSpecificDataAsync(
            int pastDays, int futureDays, CancellationToken cancellationToken = default);

        Task<IList<TickerGraphData>> GetPeriodicTickersGraphSpecificDataByIdAsync(
            Guid id, int pastDays, int futureDays, CancellationToken cancellationToken = default);

        Task<PeriodicTickerOccurrenceEntity<TPeriodicTicker>[]> GetPeriodicTickerOccurrencesAsync(
            Guid periodicTickerId, CancellationToken cancellationToken = default);

        Task<PaginationResult<PeriodicTickerOccurrenceEntity<TPeriodicTicker>>> GetPeriodicTickerOccurrencesPaginatedAsync(
            Guid periodicTickerId, int pageNumber, int pageSize,
            CancellationToken cancellationToken = default);

        Task<IList<CronOccurrenceTickerGraphData>> GetPeriodicTickerOccurrencesGraphDataAsync(
            Guid periodicTickerId, CancellationToken cancellationToken = default);

        Task<bool> TogglePeriodicTickerAsync(Guid id, bool isActive, CancellationToken cancellationToken = default);

        Task DeletePeriodicTickerOccurrenceByIdAsync(Guid id, CancellationToken cancellationToken = default);

        Task<(string Json, int MatchType)> GetPeriodicTickerRequestByIdAsync(Guid id, CancellationToken cancellationToken = default);
    }
}

