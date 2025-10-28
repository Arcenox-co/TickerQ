using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NCrontab;
using TickerQ.Utilities;
using TickerQ.Utilities.DashboardDtos;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Models;

namespace TickerQ.Dashboard.Infrastructure.Dashboard
{
    internal class TickerDashboardRepository<TTimeTicker, TCronTicker> : ITickerDashboardRepository<TTimeTicker, TCronTicker> 
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        private readonly ITickerPersistenceProvider<TTimeTicker, TCronTicker> _persistenceProvider;
        private readonly ITickerQHostScheduler _tickerQHostScheduler;
        private readonly ITickerQNotificationHubSender _notificationHubSender;
        private readonly TickerExecutionContext _executionContext;
        private readonly ITimeTickerManager<TTimeTicker>  _timeTickerManager;
        private readonly ITickerClock _clock;
        public TickerDashboardRepository(
            TickerExecutionContext executionContext,
            ITickerPersistenceProvider<TTimeTicker, TCronTicker> persistenceProvider,
            ITickerQHostScheduler tickerQHostScheduler, 
            ITickerQNotificationHubSender notificationHubSender, ITimeTickerManager<TTimeTicker> timeTickerManager, ITickerClock clock)
        {
            _persistenceProvider = persistenceProvider ?? throw new ArgumentNullException(nameof(persistenceProvider));
            _tickerQHostScheduler = tickerQHostScheduler ?? throw new ArgumentNullException(nameof(tickerQHostScheduler));
            _notificationHubSender = notificationHubSender ?? throw new ArgumentNullException(nameof(notificationHubSender));
            _timeTickerManager = timeTickerManager;
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _executionContext = executionContext ??  throw new ArgumentNullException(nameof(executionContext));
        }

        public async Task<TTimeTicker[]> GetTimeTickersAsync(CancellationToken cancellationToken)
            => await _persistenceProvider.GetTimeTickers(null, cancellationToken);
        
        public async Task<PaginationResult<TTimeTicker>> GetTimeTickersPaginatedAsync(int pageNumber, int pageSize, CancellationToken cancellationToken)
            => await _persistenceProvider.GetTimeTickersPaginated(null, pageNumber, pageSize, cancellationToken);

        public async Task SetTimeTickerBatchParent(Guid targetId, Guid parentId, RunCondition? batchRunCondition = null)
        {
            // var tt = await _persistenceProvider.GetTimeTickerById(targetId);
            //
            // var requestType = GetRequestType(tt.Function);
            //
            //
            // tt.BatchParent = parentId;
            // tt.RunCondition = batchRunCondition;
            //
            // if (tt.Status == TickerStatus.Idle)
            // {
            //     tt.Status = TickerStatus.Batched;
            // }
            // _tickerHost.RestartIfNeeded(tt.ExecutionTime);
            //
            // await _persistenceProvider.UpdateTimeTickers(new[]
            // {
            //     tt
            // });
            //
            // var parentTicker = await _persistenceProvider.GetTimeTickerById(parentId);
            // await _internalTickerManager.CascadeBatchUpdate(parentId, parentTicker.Status);
            //
            // await NotifyOrUpdateUpdate(tt, requestType, false);
        }
        
        public async Task UnbatchTimeTickerAsync(Guid tickerId)
        {
            // var tt = await _persistenceProvider.GetTimeTickerById(tickerId);
            //
            // var requestType = GetRequestType(tt.Function);
            //
            // // Store the original parent ID for cascade updates
            // var originalParentId = tt.BatchParent;
            //
            // // Clear the batch relationship
            // tt.BatchParent = null;
            // tt.RunCondition = null;
            //
            // // If the ticker was batched, change it to idle
            // if (tt.Status == TickerStatus.Batched)
            // {
            //     tt.Status = TickerStatus.Idle;
            // }
            //
            // _tickerHost.RestartIfNeeded(tt.ExecutionTime);
            //
            // await _persistenceProvider.UpdateTimeTickers(new[]
            // {
            //     tt
            // });
            //
            // // If there was a parent, update the parent's batch cascade
            // if (originalParentId.HasValue)
            // {
            //     var parentTicker = await _persistenceProvider.GetTimeTickerById(originalParentId.Value);
            //     await _internalTickerManager.CascadeBatchUpdate(originalParentId.Value, parentTicker.Status);
            // }
            //
            // await NotifyOrUpdateUpdate(tt, requestType, false);
        }

        public async Task<IList<Tuple<TickerStatus, int>>> GetTimeTickerFullDataAsync(CancellationToken cancellationToken)
        {
            var timeTickers = await _persistenceProvider.GetTimeTickers(null, cancellationToken: cancellationToken);

            var allStatuses = Enum.GetValues(typeof(TickerStatus)).Cast<TickerStatus>().ToArray();

            // Group by status and get counts
            var rawData = timeTickers
                .GroupBy(x => x.Status)
                .Select(g => new
                {
                    Status = g.Key,
                    Count = g.Count()
                })
                .ToList();

            // Create a dictionary for quick lookup
            var statusCounts = rawData.ToDictionary(x => x.Status, x => x.Count);

            // Ensure all statuses are included, even those with 0 count
            var result = allStatuses
                .Select(status => new Tuple<TickerStatus, int>(
                    status,
                    statusCounts.GetValueOrDefault(status, 0)))
                .ToList();

            return result;
        }

        public async Task<IList<TickerGraphData>> GetTimeTickersGraphSpecificDataAsync(
            int pastDays,
            int futureDays,
            CancellationToken cancellationToken)
        {
            var today = DateTime.UtcNow.Date;
            var startDate = today.AddDays(pastDays);
            var endDate = today.AddDays(futureDays);

            var timeTickers = await _persistenceProvider.GetTimeTickers(x =>
                    (x.ExecutionTime != null) &&
                    (x.ExecutionTime.Value.Date >= startDate && x.ExecutionTime.Value.Date <= endDate),
                cancellationToken);

            // Get all possible statuses once
            var allStatuses = Enum.GetValues(typeof(TickerStatus)).Cast<TickerStatus>().ToArray();

            var rawData = timeTickers
                .GroupBy(x => new { x.ExecutionTime!.Value.Date, x.Status })
                .Select(g => new
                {
                    g.Key.Date,
                    g.Key.Status,
                    Count = g.Count()
                })
                .ToList();

            // Build the final result: one entry per date, with all statuses filled
            var allDates = Enumerable.Range(0, (endDate - startDate).Days + 1)
                .Select(offset => startDate.AddDays(offset))
                .ToList();

            var groupedData = rawData
                .GroupBy(x => x.Date)
                .ToDictionary(g => g.Key, g => g.ToDictionary(s => s.Status, s => s.Count));

            var finalData = allDates.Select(date =>
            {
                var statusCounts = groupedData.TryGetValue(date, out var statusData)
                    ? statusData
                    : new Dictionary<TickerStatus, int>();

                var results = allStatuses
                    .Select(status => new Tuple<int, int>(
                        (int)status,
                        statusCounts.GetValueOrDefault(status, 0)))
                    .ToArray();

                return new TickerGraphData
                {
                    Date = date,
                    Results = results
                };
            }).ToList();
            return finalData;
        }

        public async Task<IList<TickerGraphData>> GetCronTickersGraphSpecificDataByIdAsync(Guid id, int pastDays,
            int futureDays,
            CancellationToken cancellationToken)
        {
            var today = DateTime.UtcNow.Date;
            var startDate = today.AddDays(pastDays);
            var endDate = today.AddDays(futureDays);

            var cronTickerOccurrences =
                await _persistenceProvider.GetAllCronTickerOccurrences((x => x.CronTickerId == id && x.ExecutionTime.Date >= startDate && x.ExecutionTime.Date <= endDate), cancellationToken);

            var allStatuses = Enum.GetValues(typeof(TickerStatus)).Cast<TickerStatus>().ToArray();

            var rawData = cronTickerOccurrences
                .GroupBy(x => new { x.ExecutionTime.Date, x.Status })
                .Select(g => new
                {
                    g.Key.Date,
                    g.Key.Status,
                    Count = g.Count()
                })
                .ToList();

            var allDates = Enumerable.Range(0, (endDate - startDate).Days + 1)
                .Select(offset => startDate.AddDays(offset))
                .ToList();

            var groupedData = rawData
                .GroupBy(x => x.Date)
                .ToDictionary(g => g.Key, g => g.ToDictionary(s => s.Status, s => s.Count));

            var finalData = allDates.Select(date =>
            {
                var statusCounts = groupedData.TryGetValue(date, out var statusData)
                    ? statusData
                    : new Dictionary<TickerStatus, int>();

                var results = allStatuses
                    .Select(status => new Tuple<int, int>(
                        (int)status,
                        statusCounts.GetValueOrDefault(status, 0)))
                    .ToArray();

                return new TickerGraphData
                {
                    Date = date,
                    Results = results
                };
            }).ToList();

            return finalData;
        }

        public async Task<IList<Tuple<TickerStatus, int>>> GetCronTickerFullDataAsync(CancellationToken cancellationToken)
        {
            var cronTickerOccurrences = await _persistenceProvider.GetAllCronTickerOccurrences(null, cancellationToken: cancellationToken);
            var allStatuses = Enum.GetValues(typeof(TickerStatus)).Cast<TickerStatus>().ToArray();

            var rawData = cronTickerOccurrences
                .GroupBy(x => x.Status)
                .Select(g => new
                {
                    Status = g.Key,
                    Count = g.Count()
                })
                .ToList();

            var statusCounts = rawData.ToDictionary(x => x.Status, x => x.Count);

            var result = allStatuses
                .Select(status => new Tuple<TickerStatus, int>(
                    status,
                    statusCounts.GetValueOrDefault(status, 0)))
                .ToList();

            return result;
        }

        public async Task<IList<TickerGraphData>> GetCronTickersGraphSpecificDataAsync(
            int pastDays,
            int futureDays,
            CancellationToken cancellationToken)
        {
            var today = DateTime.UtcNow.Date;
            var startDate = today.AddDays(pastDays);
            var endDate = today.AddDays(futureDays);

            var cronTickerOccurrences = await _persistenceProvider
                .GetAllCronTickerOccurrences(x => x.ExecutionTime.Date >= startDate && x.ExecutionTime.Date <= endDate, cancellationToken: cancellationToken);
            
            var allStatuses = Enum.GetValues(typeof(TickerStatus)).Cast<TickerStatus>().ToArray();

            var rawData = cronTickerOccurrences
                .GroupBy(x => new { x.ExecutionTime.Date, x.Status })
                .Select(g => new
                {
                    g.Key.Date,
                    g.Key.Status,
                    Count = g.Count()
                })
                .ToList();

            var allDates = Enumerable.Range(0, (endDate - startDate).Days + 1)
                .Select(offset => startDate.AddDays(offset))
                .ToList();

            var groupedData = rawData
                .GroupBy(x => x.Date)
                .ToDictionary(g => g.Key, g => g.ToDictionary(s => s.Status, s => s.Count));

            var finalData = allDates.Select(date =>
            {
                var statusCounts = groupedData.TryGetValue(date, out var statusData)
                    ? statusData
                    : new Dictionary<TickerStatus, int>();

                var results = allStatuses
                    .Select(status => new Tuple<int, int>(
                        (int)status,
                        statusCounts.GetValueOrDefault(status, 0)))
                    .ToArray();

                return new TickerGraphData
                {
                    Date = date,
                    Results = results
                };
            }).ToList();

            return finalData;
        }

        public async Task<IList<(int, int)>> GetLastWeekJobStatusesAsync(CancellationToken cancellationToken)
        {
            var endDate = DateTime.UtcNow.Date;
            var startDate = endDate.AddDays(-7);

            var timeTickers = await _persistenceProvider.GetTimeTickers(x =>
                    (x.ExecutionTime != null) &&
                    (x.ExecutionTime.Value.Date >= startDate && x.ExecutionTime.Value.Date <= endDate)
                , cancellationToken);
            
            var timeTickerStatuses = timeTickers
                .Select(x => x.Status)
                .ToList();

            var cronTickerOccurrences = await _persistenceProvider
                .GetAllCronTickerOccurrences(x => x.ExecutionTime.Date >= startDate && x.ExecutionTime.Date <= endDate, cancellationToken);
            
            var cronTickerStatuses = cronTickerOccurrences
                .Select(x => x.Status)
                .ToList();

            // Merge all statuses into one list
            var allStatuses = timeTickerStatuses.Concat(cronTickerStatuses).ToList();

            // Count per type
            var doneOrDueDoneCount = allStatuses.Count(x => x is TickerStatus.Done or TickerStatus.DueDone);
            var failedCount = allStatuses.Count(x => x == TickerStatus.Failed);
            var totalCount = allStatuses.Count;

            return new List<(int, int)>
            {
                (0, doneOrDueDoneCount),
                (1, failedCount),
                (2, totalCount)
            };
        }

        public async Task<IList<(TickerStatus, int)>> GetOverallJobStatusesAsync(CancellationToken cancellationToken)
        {
            var timeTickers = await _persistenceProvider.GetTimeTickers(null, cancellationToken);
            var timeStatusCounts = timeTickers
                .GroupBy(x => x.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToList();

            var cronTickerOccurrences = await _persistenceProvider.GetAllCronTickerOccurrences(null, cancellationToken);
            var cronStatusCounts = cronTickerOccurrences
                .GroupBy(x => x.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToList();

            // Combine counts into a Dictionary<TickerStatus, int>
            var combined = new Dictionary<TickerStatus, int>();

            foreach (var item in timeStatusCounts)
            {
                if (combined.ContainsKey(item.Status))
                    combined[item.Status] += item.Count;
                else
                    combined[item.Status] = item.Count;
            }

            foreach (var item in cronStatusCounts)
            {
                if (combined.ContainsKey(item.Status))
                    combined[item.Status] += item.Count;
                else
                    combined[item.Status] = item.Count;
            }

            // Return as list of tuples
            return combined
                .Select(kvp => (kvp.Key, kvp.Value))
                .ToList();
        }

        public async Task<IList<(string, int)>> GetMachineJobsAsync(CancellationToken cancellationToken)
        {
            var timeTickers = await _persistenceProvider.GetTimeTickers(x => x.LockedAt != null, cancellationToken);
            var timeTickerCounts = timeTickers
                .GroupBy(x => x.LockHolder)
                .Select(g => new { LockHolder = g.Key, Count = g.Count() })
                .ToList();

            var cronTickerOccurrences = await _persistenceProvider.GetAllCronTickerOccurrences(x => x.LockedAt != null, cancellationToken);
            var cronTickerCounts = cronTickerOccurrences
                .GroupBy(x => x.LockHolder)
                .Select(g => new { LockHolder = g.Key, Count = g.Count() })
                .ToList();

            // Combine results into a single dictionary
            var combined = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in timeTickerCounts)
            {
                if (combined.ContainsKey(item.LockHolder))
                    combined[item.LockHolder] += item.Count;
                else
                    combined[item.LockHolder] = item.Count;
            }

            foreach (var item in cronTickerCounts)
            {
                if (combined.ContainsKey(item.LockHolder))
                    combined[item.LockHolder] += item.Count;
                else
                    combined[item.LockHolder] = item.Count;
            }

            return combined
                .Select(x => (x.Key, x.Value))
                .OrderByDescending(x => x.Item2) // Optional: most active machines first
                .ToList();
        }

        public async Task<CronTickerEntity[]> GetCronTickersAsync(CancellationToken cancellationToken)
            => await _persistenceProvider.GetCronTickers(null, cancellationToken);
        
        public async Task<PaginationResult<CronTickerEntity>> GetCronTickersPaginatedAsync(int pageNumber, int pageSize, CancellationToken cancellationToken)
        {
            // We need to cast TCronTicker[] to CronTickerEntity[] for the pagination result
            var result = await _persistenceProvider.GetCronTickersPaginated(null, pageNumber, pageSize, cancellationToken);
            return new PaginationResult<CronTickerEntity>(
                result.Items.Cast<CronTickerEntity>(),
                result.TotalCount,
                result.PageNumber,
                result.PageSize
            );
        }
        
        public async Task AddOnDemandCronTickerOccurrenceAsync(Guid id, CancellationToken cancellationToken)
        {
            var now = DateTime.UtcNow;
            var onDemandOccurrence = new CronTickerOccurrenceEntity<TCronTicker>
            {
                Id = Guid.NewGuid(),
                Status = TickerStatus.Idle,
                ExecutionTime = now.AddSeconds(1),
                LockedAt = now,
                CronTickerId = id
            };

            await _persistenceProvider.InsertCronTickerOccurrences([onDemandOccurrence], cancellationToken);

            // _tickerQHostScheduler.RestartIfNeeded(onDemandOccurrence.ExecutionTime);

            if (_notificationHubSender != null)
                await _notificationHubSender.AddCronOccurrenceAsync(id, onDemandOccurrence);
        }

        public async Task<CronTickerOccurrenceEntity<TCronTicker>[]> GetCronTickersOccurrencesAsync(Guid cronTickerId, CancellationToken cancellationToken)
        {
            return await _persistenceProvider.GetAllCronTickerOccurrences(x => x.CronTickerId == cronTickerId, cancellationToken);
        }
        
        public async Task<PaginationResult<CronTickerOccurrenceEntity<TCronTicker>>> GetCronTickersOccurrencesPaginatedAsync(Guid cronTickerId, int pageNumber, int pageSize, CancellationToken cancellationToken)
        {
            return await _persistenceProvider.GetAllCronTickerOccurrencesPaginated(x => x.CronTickerId == cronTickerId, pageNumber, pageSize, cancellationToken);
        }

        public async Task<IList<CronOccurrenceTickerGraphData>> GetCronTickersOccurrencesGraphDataAsync(Guid guid, CancellationToken cancellationToken)
        {
            var maxTotalDays = 14;
            var today = DateTime.UtcNow.Date;

            var cronTickerOccurrencesPast =
                await _persistenceProvider.GetAllCronTickerOccurrences(x => x .CronTickerId == guid && x.ExecutionTime.Date < today,cancellationToken);
            var pastData = cronTickerOccurrencesPast
                .GroupBy(x => x.ExecutionTime.Date)
                .Select(group => new CronOccurrenceTickerGraphData
                {
                    Date = group.Key,
                    Results = group
                        .GroupBy(x => x.Status)
                        .Select(statusGroup => new Tuple<int, int>((int)statusGroup.Key, statusGroup.Count()))
                        .ToArray()
                })
                .OrderBy(d => d.Date)
                .ToList();

            var cronTickerOccurrencesToday =
                await _persistenceProvider.GetAllCronTickerOccurrences(x => x .CronTickerId == guid && x.ExecutionTime.Date == today, cancellationToken);
            
            var todayData = cronTickerOccurrencesToday
                .GroupBy(x => x.ExecutionTime.Date)
                .Select(group => new CronOccurrenceTickerGraphData
                {
                    Date = group.Key,
                    Results = group
                        .GroupBy(x => x.Status)
                        .Select(statusGroup => new Tuple<int, int>((int)statusGroup.Key, statusGroup.Count()))
                        .ToArray()
                })
                .FirstOrDefault() ?? new CronOccurrenceTickerGraphData
                {
                    Date = today,
                    Results = []
                };

            var cronTickerOccurrencesFuture =
                await _persistenceProvider.GetAllCronTickerOccurrences(x => x .CronTickerId == guid && x.ExecutionTime.Date > today,cancellationToken);
            var futureData = cronTickerOccurrencesFuture
                .GroupBy(x => x.ExecutionTime.Date)
                .Select(group => new CronOccurrenceTickerGraphData
                {
                    Date = group.Key,
                    Results = group
                        .GroupBy(x => x.Status)
                        .Select(statusGroup => new Tuple<int, int>((int)statusGroup.Key, statusGroup.Count()))
                        .ToArray()
                })
                .OrderBy(d => d.Date)
                .ToList();

            int pastDaysWithData = pastData.Count;
            int futureDaysWithData = futureData.Count;

            int remainingSlots = maxTotalDays - 1; // Exclude today
            int emptyPastSlots = Math.Max(0, (remainingSlots - futureDaysWithData) / 2);
            int emptyFutureSlots = Math.Max(0, remainingSlots - pastDaysWithData - emptyPastSlots);

            List<CronOccurrenceTickerGraphData> emptyPastDays = new List<CronOccurrenceTickerGraphData>();
            if (emptyPastSlots > 0)
            {
                var firstPastDate = pastData.FirstOrDefault()?.Date ?? today.AddDays(-1);
                for (int i = 1; i <= emptyPastSlots; i++)
                {
                    emptyPastDays.Add(new CronOccurrenceTickerGraphData
                    {
                        Date = firstPastDate.AddDays(-i),
                        Results = []
                    });
                }
            }

            List<CronOccurrenceTickerGraphData> emptyFutureDays = new List<CronOccurrenceTickerGraphData>();
            if (emptyFutureSlots > 0)
            {
                var lastFutureDate = futureData.LastOrDefault()?.Date ?? today.AddDays(1);
                for (int i = 1; i <= emptyFutureSlots; i++)
                {
                    emptyFutureDays.Add(new CronOccurrenceTickerGraphData
                    {
                        Date = lastFutureDate.AddDays(i),
                        Results = []
                    });
                }
            }

            var completeData = emptyPastDays
                .Concat(pastData)
                .Append(todayData)
                .Concat(futureData)
                .Concat(emptyFutureDays)
                .OrderBy(d => d.Date)
                .Take(maxTotalDays)
                .ToList();

            var startDate = completeData.First().Date;
            var endDate = completeData.Last().Date;
            var allDates = Enumerable.Range(0, (endDate - startDate).Days + 1)
                .Select(offset => startDate.AddDays(offset))
                .ToList();

            var finalData = allDates
                .Select(date => completeData.FirstOrDefault(d => d.Date == date) ?? new CronOccurrenceTickerGraphData
                {
                    Date = date,
                    Results = []
                })
                .ToList();

            return finalData;
        }

        public bool CancelTickerById(Guid tickerId)
            => TickerCancellationTokenManager.RequestTickerCancellationById(tickerId);

        public async Task DeleteCronTickerByIdAsync(Guid id, CancellationToken cancellationToken)
        {
            await _persistenceProvider.RemoveCronTickers([id], cancellationToken);

            // if (_executionContext.Functions.Any(x => x.TickerId == id))
            //     _tickerQHostScheduler.Restart();

            if (_notificationHubSender != null)
                await _notificationHubSender.RemoveCronTickerNotifyAsync(id);
        }

        public async Task DeleteTimeTickerByIdAsync(Guid id, CancellationToken cancellationToken)
        {
            await _persistenceProvider.RemoveTimeTickers([id], cancellationToken);
            
            // if (_executionContext.Functions.Any(x => x.TickerId == id))
            //     _tickerQHostScheduler.Restart();

            if (_notificationHubSender != null)
                await _notificationHubSender.RemoveTimeTickerNotifyAsync(id);
        }

        public async Task DeleteCronTickerOccurrenceByIdAsync(Guid id, CancellationToken cancellationToken)
        {
            await _persistenceProvider.RemoveCronTickerOccurrences([id], cancellationToken);
            
            // if(_executionContext.Functions.Any(x => x.TickerId == id))
            //     _tickerQHostScheduler.Restart();
        }

        public async Task<(string, int)> GetTickerRequestByIdAsync(Guid tickerId, TickerType tickerType, CancellationToken cancellationToken)
        {
            byte[] jsonRequestBytes;
            string functionName;

            if (tickerType == TickerType.TimeTicker)
            {
                var timeTicker = await _persistenceProvider.GetTimeTickerById(tickerId, cancellationToken);

                if (timeTicker == null)
                    return (string.Empty, 0);

                jsonRequestBytes = timeTicker.Request;
                functionName = timeTicker.Function;
            }
            else
            {
                var cronTicker = await _persistenceProvider.GetCronTickerById(tickerId, cancellationToken);

                if (cronTicker == null)
                    return (string.Empty, 0);

                jsonRequestBytes = cronTicker.Request;
                functionName = cronTicker.Function;
            }

            if (jsonRequestBytes == null)
                return (string.Empty, 0);

            var jsonRequest = TickerHelper.ReadTickerRequestAsString(jsonRequestBytes);

            if (!TickerFunctionProvider.TickerFunctionRequestTypes.TryGetValue(functionName,
                    out var functionTypeContext)) return (jsonRequest, 2);
            try
            {
                JsonSerializer.Deserialize(jsonRequest, functionTypeContext.Item2);
                return (jsonRequest, 1);
            }
            catch
            {
                return (jsonRequest, 2);
            }
        }

        public IEnumerable<(string, (string, string, TickerTaskPriority))> GetTickerFunctions()
        {
            foreach (var tickerFunction in TickerFunctionProvider.TickerFunctions.Select(x => new { x.Key, x.Value.Priority }))
            {
                if (TickerFunctionProvider.TickerFunctionRequestTypes.TryGetValue(tickerFunction.Key,
                        out var functionTypeContext))
                {
                    JsonExampleGenerator.TryGenerateExampleJson(functionTypeContext.Item2, out var exampleJson);
                    yield return (tickerFunction.Key, (functionTypeContext.Item1, exampleJson, tickerFunction.Priority));
                }
                else
                {
                    yield return (tickerFunction.Key, (string.Empty, null, tickerFunction.Priority));
                }
            }
        }

        // New method that accepts request models
        public async Task UpdateTimeTickerAsync(Guid id, TTimeTicker request, CancellationToken cancellationToken)
        {
            request.Id = id;
            
            if(request.ExecutionTime == default)
                request.ExecutionTime = _clock.UtcNow.AddSeconds(1);
            
            await _timeTickerManager.UpdateAsync(request, cancellationToken);
        }

        
        public async Task AddTimeTickerAsync(TTimeTicker request, CancellationToken cancellationToken)
        {
            if(request.ExecutionTime == default)
                request.ExecutionTime = _clock.UtcNow.AddSeconds(1);
            
            await _timeTickerManager.AddAsync(request, cancellationToken);
        }

        // New method that accepts request models
        public async Task AddCronTickerAsync(TTimeTicker request, CancellationToken cancellationToken)
        {
            if(request.ExecutionTime == default)
                request.ExecutionTime = _clock.UtcNow.AddSeconds(1);
            
            await _timeTickerManager.AddAsync(request, cancellationToken);
        }

        // New method that accepts request models
        public async Task UpdateCronTickerAsync(Guid id, UpdateCronTickerRequest request, CancellationToken cancellationToken)
        {
            var cronTicker = await _persistenceProvider.GetCronTickerById(id, cancellationToken);

            if (cronTicker == null)
                throw new KeyNotFoundException($"CronTicker with ID {id} not found.");

            cronTicker.UpdatedAt = DateTime.UtcNow;
            var requestType = GetRequestType(request.Function);

            cronTicker.Function = request.Function;
            cronTicker.Expression = request.Expression;
            cronTicker.Description = request.Description ?? string.Empty;
            cronTicker.Retries = request.Retries ?? 0;
            cronTicker.RetryIntervals = request.Intervals ?? [30];

            // Process the request using the function
            if (!string.IsNullOrWhiteSpace(request.Request))
            {
                var serializedRequest = JsonSerializer.Deserialize<object>(request.Request);
                cronTicker.Request = TickerHelper.CreateTickerRequest(serializedRequest);
            }
            
            await _persistenceProvider.UpdateCronTickers([cronTicker], cancellationToken);

            var nextOccurrence = CrontabSchedule.TryParse(cronTicker.Expression)?.GetNextOccurrence(DateTime.UtcNow);

            // if (nextOccurrence != null)
            //     _tickerQHostScheduler.RestartIfNeeded(nextOccurrence.Value);

            await NotifyOrUpdateUpdate(cronTicker, requestType, false);
        }

        private static string GetRequestType(string function)
        {
            return TickerFunctionProvider.TickerFunctionRequestTypes.TryGetValue(function,
                out var functionTypeContext)
                ? functionTypeContext.Item1
                : string.Empty;
        }

        private async Task NotifyOrUpdateUpdate<T>(T ticker, string requestType, bool isNew) where T : class
        {
            if (_notificationHubSender != null)
            {
                switch (ticker)
                {
                    case TTimeTicker timeTicker when isNew:
                        await _notificationHubSender.AddTimeTickerNotifyAsync(timeTicker);
                        break;
                    case TTimeTicker timeTicker:
                        await _notificationHubSender.UpdateTimeTickerNotifyAsync(timeTicker);
                        break;
                    case TCronTicker cronTicker when isNew:
                        await _notificationHubSender.AddCronTickerNotifyAsync(cronTicker);
                        break;  
                    case TCronTicker cronTicker:
                        await _notificationHubSender.UpdateCronTickerNotifyAsync(cronTicker);
                        break;
                }
            }
        }
    }
}