using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities;
using TickerQ.Utilities.DashboardDtos;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ.Dashboard.Infrastructure.Dashboard
{
    internal class PeriodicDashboardRepository<TPeriodicTicker> : IPeriodicDashboardRepository<TPeriodicTicker>
        where TPeriodicTicker : PeriodicTickerEntity, new()
    {
        private readonly IPeriodicTickerPersistenceProvider<TPeriodicTicker> _provider;
        private readonly ITickerQHostScheduler _scheduler;
        private readonly ITickerQNotificationHubSender _notificationHubSender;
        private readonly TickerExecutionContext _executionContext;
        private readonly DashboardOptionsBuilder _dashboardOptions;

        public PeriodicDashboardRepository(
            IPeriodicTickerPersistenceProvider<TPeriodicTicker> provider,
            ITickerQHostScheduler scheduler,
            ITickerQNotificationHubSender notificationHubSender,
            TickerExecutionContext executionContext,
            DashboardOptionsBuilder dashboardOptions)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
            _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
            _notificationHubSender = notificationHubSender ?? throw new ArgumentNullException(nameof(notificationHubSender));
            _executionContext = executionContext ?? throw new ArgumentNullException(nameof(executionContext));
            _dashboardOptions = dashboardOptions ?? throw new ArgumentNullException(nameof(dashboardOptions));
        }

        public async Task<TPeriodicTicker[]> GetPeriodicTickersAsync(CancellationToken cancellationToken = default)
            => await _provider.GetPeriodicTickers(null, cancellationToken);

        public async Task<PaginationResult<TPeriodicTicker>> GetPeriodicTickersPaginatedAsync(
            int pageNumber, int pageSize, bool? isActive = null, string search = null,
            CancellationToken cancellationToken = default)
        {
            Expression<Func<TPeriodicTicker, bool>> predicate = BuildFilter(isActive, search);
            return await _provider.GetPeriodicTickersPaginated(predicate, pageNumber, pageSize, cancellationToken);
        }

        private static Expression<Func<TPeriodicTicker, bool>> BuildFilter(bool? isActive, string search)
        {
            var term = string.IsNullOrWhiteSpace(search) ? null : search;
            if (isActive is { } a && term is not null)
                return x => x.IsActive == a && x.Function != null && x.Function.Contains(term);
            if (isActive is { } activeOnly)
                return x => x.IsActive == activeOnly;
            if (term is not null)
                return x => x.Function != null && x.Function.Contains(term);
            return null;
        }

        public async Task<IList<Tuple<TickerStatus, int>>> GetPeriodicTickerFullDataAsync(CancellationToken cancellationToken = default)
        {
            var occurrences = await _provider.GetAllPeriodicTickerOccurrences(null, cancellationToken);
            var allStatuses = Enum.GetValues<TickerStatus>();
            var counts = occurrences.GroupBy(x => x.Status).ToDictionary(g => g.Key, g => g.Count());
            return allStatuses
                .Select(s => new Tuple<TickerStatus, int>(s, counts.GetValueOrDefault(s, 0)))
                .ToList();
        }

        public async Task<IList<TickerGraphData>> GetPeriodicTickersGraphSpecificDataAsync(
            int pastDays, int futureDays, CancellationToken cancellationToken = default)
        {
            var today = DateTime.UtcNow.Date;
            var startDate = today.AddDays(pastDays);
            var endDate = today.AddDays(futureDays);

            var occurrences = await _provider.GetAllPeriodicTickerOccurrences(
                x => x.ExecutionTime.Date >= startDate && x.ExecutionTime.Date <= endDate,
                cancellationToken);

            return BuildDailyGraph(occurrences, startDate, endDate);
        }

        public async Task<IList<TickerGraphData>> GetPeriodicTickersGraphSpecificDataByIdAsync(
            Guid id, int pastDays, int futureDays, CancellationToken cancellationToken = default)
        {
            var today = DateTime.UtcNow.Date;
            var startDate = today.AddDays(pastDays);
            var endDate = today.AddDays(futureDays);

            var occurrences = await _provider.GetAllPeriodicTickerOccurrences(
                x => x.PeriodicTickerId == id && x.ExecutionTime.Date >= startDate && x.ExecutionTime.Date <= endDate,
                cancellationToken);

            return BuildDailyGraph(occurrences, startDate, endDate);
        }

        private static IList<TickerGraphData> BuildDailyGraph(
            PeriodicTickerOccurrenceEntity<TPeriodicTicker>[] occurrences,
            DateTime startDate, DateTime endDate)
        {
            var allStatuses = Enum.GetValues<TickerStatus>();
            var grouped = occurrences
                .GroupBy(x => x.ExecutionTime.Date)
                .ToDictionary(g => g.Key, g => g.GroupBy(o => o.Status).ToDictionary(sg => sg.Key, sg => sg.Count()));

            var allDates = Enumerable.Range(0, (endDate - startDate).Days + 1)
                .Select(offset => startDate.AddDays(offset))
                .ToList();

            return allDates.Select(date =>
            {
                var counts = grouped.TryGetValue(date, out var c) ? c : new Dictionary<TickerStatus, int>();
                return new TickerGraphData
                {
                    Date = date,
                    Results = allStatuses
                        .Select(s => new Tuple<int, int>((int)s, counts.GetValueOrDefault(s, 0)))
                        .ToArray()
                };
            }).ToList();
        }

        public async Task<PeriodicTickerOccurrenceEntity<TPeriodicTicker>[]> GetPeriodicTickerOccurrencesAsync(
            Guid periodicTickerId, CancellationToken cancellationToken = default)
        {
            return await _provider.GetAllPeriodicTickerOccurrences(x => x.PeriodicTickerId == periodicTickerId, cancellationToken);
        }

        public async Task<PaginationResult<PeriodicTickerOccurrenceEntity<TPeriodicTicker>>> GetPeriodicTickerOccurrencesPaginatedAsync(
            Guid periodicTickerId, int pageNumber, int pageSize, CancellationToken cancellationToken = default)
        {
            return await _provider.GetAllPeriodicTickerOccurrencesPaginated(
                x => x.PeriodicTickerId == periodicTickerId, pageNumber, pageSize, cancellationToken);
        }

        public async Task<IList<CronOccurrenceTickerGraphData>> GetPeriodicTickerOccurrencesGraphDataAsync(
            Guid periodicTickerId, CancellationToken cancellationToken = default)
        {
            const int maxTotalDays = 14;
            var today = DateTime.UtcNow.Date;

            var allOccurrences = await _provider.GetAllPeriodicTickerOccurrences(
                x => x.PeriodicTickerId == periodicTickerId, cancellationToken);

            CronOccurrenceTickerGraphData ToGraph(IGrouping<DateTime, PeriodicTickerOccurrenceEntity<TPeriodicTicker>> g)
                => new()
                {
                    Date = g.Key,
                    Results = g.GroupBy(x => x.Status)
                        .Select(sg => new Tuple<int, int>((int)sg.Key, sg.Count()))
                        .ToArray()
                };

            var pastData = allOccurrences.Where(x => x.ExecutionTime.Date < today)
                .GroupBy(x => x.ExecutionTime.Date).Select(ToGraph).OrderBy(d => d.Date).ToList();

            var todayData = allOccurrences.Where(x => x.ExecutionTime.Date == today)
                .GroupBy(x => x.ExecutionTime.Date).Select(ToGraph).FirstOrDefault()
                ?? new CronOccurrenceTickerGraphData { Date = today, Results = [] };

            var futureData = allOccurrences.Where(x => x.ExecutionTime.Date > today)
                .GroupBy(x => x.ExecutionTime.Date).Select(ToGraph).OrderBy(d => d.Date).ToList();

            int remainingSlots = maxTotalDays - 1;
            int emptyPastSlots = Math.Max(0, (remainingSlots - futureData.Count) / 2);
            int emptyFutureSlots = Math.Max(0, remainingSlots - pastData.Count - emptyPastSlots);

            var emptyPast = new List<CronOccurrenceTickerGraphData>();
            if (emptyPastSlots > 0)
            {
                var firstPastDate = pastData.FirstOrDefault()?.Date ?? today.AddDays(-1);
                for (int i = 1; i <= emptyPastSlots; i++)
                    emptyPast.Add(new CronOccurrenceTickerGraphData { Date = firstPastDate.AddDays(-i), Results = [] });
            }

            var emptyFuture = new List<CronOccurrenceTickerGraphData>();
            if (emptyFutureSlots > 0)
            {
                var lastFutureDate = futureData.LastOrDefault()?.Date ?? today.AddDays(1);
                for (int i = 1; i <= emptyFutureSlots; i++)
                    emptyFuture.Add(new CronOccurrenceTickerGraphData { Date = lastFutureDate.AddDays(i), Results = [] });
            }

            var complete = emptyPast
                .Concat(pastData)
                .Append(todayData)
                .Concat(futureData)
                .Concat(emptyFuture)
                .OrderBy(d => d.Date)
                .Take(maxTotalDays)
                .ToList();

            if (complete.Count == 0)
                return complete;

            var startDate = complete.First().Date;
            var endDate = complete.Last().Date;
            var allDates = Enumerable.Range(0, (endDate - startDate).Days + 1)
                .Select(offset => startDate.AddDays(offset)).ToList();

            return allDates
                .Select(date => complete.FirstOrDefault(d => d.Date == date)
                                ?? new CronOccurrenceTickerGraphData { Date = date, Results = [] })
                .ToList();
        }

        public async Task<bool> TogglePeriodicTickerAsync(Guid id, bool isActive, CancellationToken cancellationToken = default)
        {
            var ticker = await _provider.GetPeriodicTickerById(id, cancellationToken);
            if (ticker == null)
                return false;

            ticker.IsActive = isActive;
            ticker.UpdatedAt = DateTime.UtcNow;

            var affected = await _provider.UpdatePeriodicTickers(new[] { ticker }, cancellationToken);
            if (affected > 0)
            {
                _scheduler.Restart();
                await _notificationHubSender.UpdatePeriodicTickerNotifyAsync(ticker);
            }
            return affected > 0;
        }

        public async Task DeletePeriodicTickerOccurrenceByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            await _provider.RemovePeriodicTickerOccurrences(new[] { id }, cancellationToken);
            if (_executionContext.Functions.Any(x => x.TickerId == id))
                _scheduler.Restart();
        }

        public async Task<(string Json, int MatchType)> GetPeriodicTickerRequestByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            var ticker = await _provider.GetPeriodicTickerById(id, cancellationToken);
            if (ticker == null || ticker.Request == null)
                return (string.Empty, 0);

            var jsonRequest = TickerHelper.ReadTickerRequestAsString(ticker.Request);

            if (!TickerFunctionProvider.TickerFunctionRequestTypes.TryGetValue(ticker.Function, out var functionTypeContext))
                return (jsonRequest, 2);

            try
            {
                var typeInfo = _dashboardOptions.DashboardJsonOptions.GetTypeInfo(functionTypeContext.Item2);
                JsonSerializer.Deserialize(jsonRequest, typeInfo);
                return (jsonRequest, 1);
            }
            catch
            {
                return (jsonRequest, 2);
            }
        }
    }
}

