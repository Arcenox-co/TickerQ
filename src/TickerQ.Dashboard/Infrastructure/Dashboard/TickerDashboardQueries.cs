using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.DashboardDtos;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ.Dashboard.Infrastructure.Dashboard
{
    /// <summary>
    /// Dashboard queries using ITickerQueryable — provider-agnostic.
    /// This is a demonstration of the new queryable API alongside the existing repository.
    /// </summary>
    internal class TickerDashboardQueries<TTimeTicker, TCronTicker>
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        private readonly ITickerPersistenceProvider<TTimeTicker, TCronTicker> _provider;

        public TickerDashboardQueries(ITickerPersistenceProvider<TTimeTicker, TCronTicker> provider)
        {
            _provider = provider;
        }

        /// <summary>
        /// Get all time tickers with children included.
        /// </summary>
        public Task<TTimeTicker[]> GetTimeTickersAsync(CancellationToken cancellationToken)
        {
            return _provider.TimeTickersQuery()
                .WithRelated(TickerRelation.ChildrenDeep)
                .AsNoTracking()
                .Where(x => x.ParentId == null)
                .OrderByDescending(x => x.ExecutionTime)
                .ToArrayAsync(cancellationToken);
        }

        /// <summary>
        /// Get time tickers paginated.
        /// </summary>
        public Task<PaginationResult<TTimeTicker>> GetTimeTickersPaginatedAsync(
            int pageNumber, int pageSize, CancellationToken cancellationToken)
        {
            return _provider.TimeTickersQuery()
                .WithRelated(TickerRelation.ChildrenDeep)
                .AsNoTracking()
                .Where(x => x.ParentId == null)
                .OrderByDescending(x => x.ExecutionTime)
                .ToPaginatedAsync(pageNumber, pageSize, cancellationToken);
        }

        /// <summary>
        /// Get time ticker by id.
        /// </summary>
        public Task<TTimeTicker> GetTimeTickerByIdAsync(Guid id, CancellationToken cancellationToken)
        {
            return _provider.TimeTickersQuery()
                .WithRelated(TickerRelation.Children)
                .AsNoTracking()
                .Where(x => x.Id == id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        /// <summary>
        /// Get time ticker status counts.
        /// </summary>
        public async Task<IList<(TickerStatus Status, int Count)>> GetTimeTickerStatusCountsAsync(
            CancellationToken cancellationToken)
        {
            var timeTickers = await _provider.TimeTickersQuery()
                .AsNoTracking()
                .Where(x => x.ParentId == null)
                .ToArrayAsync(cancellationToken);

            var allStatuses = Enum.GetValues<TickerStatus>();
            var statusCounts = timeTickers
                .GroupBy(x => x.Status)
                .ToDictionary(g => g.Key, g => g.Count());

            return allStatuses
                .Select(s => (s, statusCounts.GetValueOrDefault(s, 0)))
                .ToList();
        }

        /// <summary>
        /// Get time tickers graph data for a date range.
        /// </summary>
        public async Task<IList<TickerGraphData>> GetTimeTickersGraphDataAsync(
            int pastDays, int futureDays, CancellationToken cancellationToken)
        {
            var today = DateTime.UtcNow.Date;
            var startDate = today.AddDays(pastDays);
            var endDate = today.AddDays(futureDays);

            var timeTickers = await _provider.TimeTickersQuery()
                .AsNoTracking()
                .Where(x => x.ExecutionTime != null
                             && x.ExecutionTime.Value.Date >= startDate
                             && x.ExecutionTime.Value.Date <= endDate)
                .ToArrayAsync(cancellationToken);

            return BuildGraphData(
                timeTickers,
                t => t.ExecutionTime!.Value.Date,
                t => t.Status,
                startDate, endDate);
        }

        /// <summary>
        /// Get cron ticker occurrences graph data for a date range.
        /// </summary>
        public async Task<IList<TickerGraphData>> GetCronOccurrencesGraphDataAsync(
            int pastDays, int futureDays, CancellationToken cancellationToken)
        {
            var today = DateTime.UtcNow.Date;
            var startDate = today.AddDays(pastDays);
            var endDate = today.AddDays(futureDays);

            var occurrences = await _provider.CronTickerOccurrencesQuery()
                .AsNoTracking()
                .Where(x => x.ExecutionTime.Date >= startDate && x.ExecutionTime.Date <= endDate)
                .ToArrayAsync(cancellationToken);

            return BuildGraphData(
                occurrences,
                o => o.ExecutionTime.Date,
                o => o.Status,
                startDate, endDate);
        }

        /// <summary>
        /// Get cron ticker occurrences graph data filtered by cron ticker id.
        /// </summary>
        public async Task<IList<TickerGraphData>> GetCronOccurrencesGraphDataByIdAsync(
            Guid cronTickerId, int pastDays, int futureDays, CancellationToken cancellationToken)
        {
            var today = DateTime.UtcNow.Date;
            var startDate = today.AddDays(pastDays);
            var endDate = today.AddDays(futureDays);

            var occurrences = await _provider.CronTickerOccurrencesQuery()
                .AsNoTracking()
                .Where(x => x.CronTickerId == cronTickerId
                             && x.ExecutionTime.Date >= startDate
                             && x.ExecutionTime.Date <= endDate)
                .ToArrayAsync(cancellationToken);

            return BuildGraphData(
                occurrences,
                o => o.ExecutionTime.Date,
                o => o.Status,
                startDate, endDate);
        }

        /// <summary>
        /// Get machine job distribution.
        /// </summary>
        public async Task<IList<(string Machine, int Count)>> GetMachineJobsAsync(
            CancellationToken cancellationToken)
        {
            var timeTickers = await _provider.TimeTickersQuery()
                .AsNoTracking()
                .Where(x => x.LockedAt != null)
                .ToArrayAsync(cancellationToken);

            var cronOccurrences = await _provider.CronTickerOccurrencesQuery()
                .AsNoTracking()
                .Where(x => x.LockedAt != null)
                .ToArrayAsync(cancellationToken);

            var combined = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var group in timeTickers.GroupBy(x => x.LockHolder))
                combined[group.Key] = group.Count();

            foreach (var group in cronOccurrences.GroupBy(x => x.LockHolder))
            {
                if (combined.ContainsKey(group.Key))
                    combined[group.Key] += group.Count();
                else
                    combined[group.Key] = group.Count();
            }

            return combined
                .Select(x => (x.Key, x.Value))
                .OrderByDescending(x => x.Value)
                .ToList();
        }

        /// <summary>
        /// Get all cron tickers.
        /// </summary>
        public Task<TCronTicker[]> GetCronTickersAsync(CancellationToken cancellationToken)
        {
            return _provider.CronTickersQuery()
                .AsNoTracking()
                .OrderByDescending(x => x.CreatedAt)
                .ToArrayAsync(cancellationToken);
        }

        /// <summary>
        /// Get cron tickers paginated.
        /// </summary>
        public Task<PaginationResult<TCronTicker>> GetCronTickersPaginatedAsync(
            int pageNumber, int pageSize, CancellationToken cancellationToken)
        {
            return _provider.CronTickersQuery()
                .AsNoTracking()
                .OrderByDescending(x => x.CreatedAt)
                .ToPaginatedAsync(pageNumber, pageSize, cancellationToken);
        }

        /// <summary>
        /// Get cron ticker occurrences by cron ticker id.
        /// </summary>
        public Task<CronTickerOccurrenceEntity<TCronTicker>[]> GetCronOccurrencesAsync(
            Guid cronTickerId, CancellationToken cancellationToken)
        {
            return _provider.CronTickerOccurrencesQuery()
                .WithRelated(TickerRelation.CronTicker)
                .AsNoTracking()
                .Where(x => x.CronTickerId == cronTickerId)
                .OrderByDescending(x => x.ExecutionTime)
                .ToArrayAsync(cancellationToken);
        }

        /// <summary>
        /// Get cron ticker occurrences paginated.
        /// </summary>
        public Task<PaginationResult<CronTickerOccurrenceEntity<TCronTicker>>> GetCronOccurrencesPaginatedAsync(
            Guid cronTickerId, int pageNumber, int pageSize, CancellationToken cancellationToken)
        {
            return _provider.CronTickerOccurrencesQuery()
                .WithRelated(TickerRelation.CronTicker)
                .AsNoTracking()
                .Where(x => x.CronTickerId == cronTickerId)
                .OrderByDescending(x => x.ExecutionTime)
                .ToPaginatedAsync(pageNumber, pageSize, cancellationToken);
        }

        private static IList<TickerGraphData> BuildGraphData<T>(
            T[] items,
            Func<T, DateTime> dateSelector,
            Func<T, TickerStatus> statusSelector,
            DateTime startDate,
            DateTime endDate)
        {
            var allStatuses = Enum.GetValues<TickerStatus>();

            var rawData = items
                .GroupBy(x => new { Date = dateSelector(x), Status = statusSelector(x) })
                .Select(g => new { g.Key.Date, g.Key.Status, Count = g.Count() })
                .ToList();

            var groupedData = rawData
                .GroupBy(x => x.Date)
                .ToDictionary(g => g.Key, g => g.ToDictionary(s => s.Status, s => s.Count));

            var allDates = Enumerable.Range(0, (endDate - startDate).Days + 1)
                .Select(offset => startDate.AddDays(offset))
                .ToList();

            return allDates.Select(date =>
            {
                var statusCounts = groupedData.TryGetValue(date, out var statusData)
                    ? statusData
                    : new Dictionary<TickerStatus, int>();

                return new TickerGraphData
                {
                    Date = date,
                    Results = allStatuses
                        .Select(status => new Tuple<int, int>(
                            (int)status,
                            statusCounts.GetValueOrDefault(status, 0)))
                        .ToArray()
                };
            }).ToList();
        }
    }
}
