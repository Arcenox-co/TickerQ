using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NCrontab;
using TickerQ.Utilities;
using TickerQ.Utilities.DashboardDtos;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Models.Ticker;

namespace TickerQ.Dashboard.Infrastructure.Dashboard
{
    internal class TickerDashboardRepository<TTimeTicker, TCronTicker> : ITickerDashboardRepository
        where TTimeTicker : TimeTicker, new()
        where TCronTicker : CronTicker, new()
    {
        private readonly ITickerPersistenceProvider<TTimeTicker, TCronTicker> _persistenceProvider;
        private readonly ITickerHost _tickerHost;
        private readonly ITickerQNotificationHubSender _notificationHubSender;
        private readonly IInternalTickerManager _internalTickerManager;

        public TickerDashboardRepository(ITickerPersistenceProvider<TTimeTicker, TCronTicker> persistenceProvider,
            ITickerHost tickerHost, ITickerQNotificationHubSender notificationHubSender,
            IInternalTickerManager internalTickerManager)
        {
            _persistenceProvider = persistenceProvider;
            _tickerHost = tickerHost ?? throw new ArgumentNullException(nameof(tickerHost));
            _notificationHubSender = notificationHubSender;
            _internalTickerManager = internalTickerManager;
        }

        public async Task<IList<TimeTickerDto>> GetTimeTickersAsync()
        {
            var timeTickers = await _persistenceProvider.GetAllTimeTickers();

            return timeTickers
                .OrderByDescending(x => x.ExecutionTime)
                .Select(x => new TimeTickerDto
                {
                    Id = x.Id,
                    Function = x.Function,
                    Status = x.Status,
                    LockHolder = x.LockHolder,
                    ExecutionTime = x.ExecutionTime,
                    LockedAt = x.LockedAt,
                    ExecutedAt = x.ExecutedAt,
                    CreatedAt = x.CreatedAt,
                    UpdatedAt = x.UpdatedAt,
                    Retries = x.Retries,
                    Exception = x.Exception,
                    ElapsedTime = x.ElapsedTime,
                    RetryCount = x.RetryCount,
                    RetryIntervals = x.RetryIntervals,
                    Description = x.Description,
                    InitIdentifier = x.InitIdentifier,
                    BatchRunCondition = x.BatchRunCondition,
                    BatchParent = x.BatchParent
                }).ToList();
        }

        public async Task SetTimeTickerBatchParent(Guid targetId, Guid parentId,
            BatchRunCondition? batchRunCondition = null)
        {
            var tt = await _persistenceProvider.GetTimeTickerById(targetId, options => options.SetAsTracking());

            var requestType = GetRequestType(tt.Function);


            tt.BatchParent = parentId;
            tt.BatchRunCondition = batchRunCondition;

            if (tt.Status == TickerStatus.Idle)
            {
                tt.Status = TickerStatus.Batched;
            }
            _tickerHost.RestartIfNeeded(tt.ExecutionTime);

            await _persistenceProvider.UpdateTimeTickers(new[]
            {
                tt
            });

            var parentTicker = await _persistenceProvider.GetTimeTickerById(parentId);
            await _internalTickerManager.CascadeBatchUpdate(parentId, parentTicker.Status);

            await NotifyOrUpdateUpdate(tt, requestType, false);
        }

        public async Task<IList<Tuple<TickerStatus, int>>> GetTimeTickerFullDataAsync(
            CancellationToken cancellationToken)
        {
            var timeTickers = await _persistenceProvider.GetAllTimeTickers(cancellationToken: cancellationToken);

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

            var timeTickers =
                await _persistenceProvider.GetTimeTickersWithin(startDate, endDate,
                    cancellationToken: cancellationToken);

            // Get all possible statuses once
            var allStatuses = Enum.GetValues(typeof(TickerStatus)).Cast<TickerStatus>().ToArray();

            var rawData = timeTickers
                .GroupBy(x => new { x.ExecutionTime.Date, x.Status })
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
                    .Select(status => new Tuple<TickerStatus, int>(
                        status,
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
                await _persistenceProvider.GetCronTickerOccurrencesByCronTickerIdWithin(id, startDate, endDate,
                    cancellationToken: cancellationToken);

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
                    .Select(status => new Tuple<TickerStatus, int>(
                        status,
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

        public async Task<IList<Tuple<TickerStatus, int>>> GetCronTickerFullDataAsync(
            CancellationToken cancellationToken)
        {
            var cronTickerOccurrences =
                await _persistenceProvider.GetAllCronTickerOccurrences(cancellationToken: cancellationToken);
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

            var cronTickerOccurrences =
                await _persistenceProvider.GetCronTickerOccurrencesWithin(startDate, endDate,
                    cancellationToken: cancellationToken);
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
                    .Select(status => new Tuple<TickerStatus, int>(
                        status,
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

        public async Task<IList<(int, int)>> GetLastWeekJobStatusesAsync()
        {
            var endDate = DateTime.UtcNow.Date;
            var startDate = endDate.AddDays(-7);

            var timeTickers = await _persistenceProvider.GetTimeTickersWithin(startDate, endDate);
            var timeTickerStatuses = timeTickers
                .Select(x => x.Status)
                .ToList();

            var cronTickerOccurrences = await _persistenceProvider.GetCronTickerOccurrencesWithin(startDate, endDate);
            var cronTickerStatuses = cronTickerOccurrences
                .Select(x => x.Status)
                .ToList();

            // Merge all statuses into one list
            var allStatuses = timeTickerStatuses.Concat(cronTickerStatuses).ToList();

            // Count per type
            var doneOrDueDoneCount = allStatuses.Count(x => x == TickerStatus.Done || x == TickerStatus.DueDone);
            var failedCount = allStatuses.Count(x => x == TickerStatus.Failed);
            var totalCount = allStatuses.Count;

            return new List<(int, int)>
            {
                (0, doneOrDueDoneCount),
                (1, failedCount),
                (2, totalCount)
            };
        }

        public async Task<IList<(TickerStatus, int)>> GetOverallJobStatusesAsync()
        {
            var timeTickers = await _persistenceProvider.GetAllTimeTickers();
            var timeStatusCounts = timeTickers
                .GroupBy(x => x.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToList();

            var cronTickerOccurrences = await _persistenceProvider.GetAllCronTickerOccurrences();
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

        public async Task<IList<(string, int)>> GetMachineJobsAsync()
        {
            var timeTickers = await _persistenceProvider.GetAllLockedTimeTickers();
            var timeTickerCounts = timeTickers
                .GroupBy(x => x.LockHolder)
                .Select(g => new { LockHolder = g.Key, Count = g.Count() })
                .ToList();

            var cronTickerOccurrences = await _persistenceProvider.GetAllLockedCronTickerOccurrences();
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

        public async Task<IList<CronTickerDto>> GetCronTickersAsync()
        {
            var cronTickers = await _persistenceProvider.GetAllCronTickers();
            return cronTickers
                .OrderByDescending(x => x.CreatedAt)
                .Select(x => new CronTickerDto
                {
                    Id = x.Id,
                    Function = x.Function,
                    Expression = x.Expression,
                    ExpressionReadable = TickerCronExpressionHelper.ToHumanReadable(x.Expression),
                    CreatedAt = x.CreatedAt,
                    UpdatedAt = x.UpdatedAt,
                    Description = x.Description,
                    Retries = x.Retries,
                    RetryIntervals = x.RetryIntervals,
                    InitIdentifier = x.InitIdentifier
                }).ToList();
        }

        public async Task AddOnDemandCronTickerOccurrenceAsync(Guid id)
        {
            var now = DateTime.UtcNow;

            var onDemandOccurrence = new CronTickerOccurrence<TCronTicker>
            {
                Id = Guid.NewGuid(),
                Status = TickerStatus.Idle,
                ExecutionTime = now.AddSeconds(1),
                LockedAt = now,
                CronTickerId = id
            };

            await _persistenceProvider.InsertCronTickerOccurrences(new[] { onDemandOccurrence });

            _tickerHost.RestartIfNeeded(onDemandOccurrence.ExecutionTime);

            if (_notificationHubSender != null)
                await _notificationHubSender.AddCronOccurrenceAsync(id, onDemandOccurrence);
        }

        public async Task<IList<CronTickerOccurrenceDto>> GetCronTickersOccurrencesAsync(Guid cronTickerId)
        {
            var cronTickerOccurrences = await _persistenceProvider.GetCronTickerOccurrencesByCronTickerId(cronTickerId);
            return cronTickerOccurrences
                .OrderByDescending(x => x.ExecutionTime)
                .Select(x => new CronTickerOccurrenceDto
                {
                    Id = x.Id,
                    ExecutedAt = x.ExecutedAt,
                    ExecutionTime = x.ExecutionTime,
                    LockedAt = x.LockedAt,
                    LockHolder = x.LockHolder,
                    Status = x.Status,
                    CronTickerId = x.CronTickerId,
                    RetryCount = x.RetryCount,
                    Exception = x.Exception,
                    ElapsedTime = x.ElapsedTime,
                })
                .ToList();
        }

        public async Task<IList<CronOccurrenceTickerGraphData>> GetCronTickersOccurrencesGraphDataAsync(Guid guid)
        {
            var maxTotalDays = 14;
            var today = DateTime.UtcNow.Date;

            var cronTickerOccurrencesPast =
                await _persistenceProvider.GetPastCronTickerOccurrencesByCronTickerId(guid, today);
            var pastData = cronTickerOccurrencesPast
                .GroupBy(x => x.ExecutionTime.Date)
                .Select(group => new CronOccurrenceTickerGraphData
                {
                    Date = group.Key,
                    Results = group
                        .GroupBy(x => x.Status)
                        .Select(statusGroup => new Tuple<TickerStatus, int>(statusGroup.Key, statusGroup.Count()))
                        .ToArray()
                })
                .OrderBy(d => d.Date)
                .ToList();

            var cronTickerOccurrencesToday =
                await _persistenceProvider.GetTodayCronTickerOccurrencesByCronTickerId(guid, today);
            var todayData = cronTickerOccurrencesToday
                .GroupBy(x => x.ExecutionTime.Date)
                .Select(group => new CronOccurrenceTickerGraphData
                {
                    Date = group.Key,
                    Results = group
                        .GroupBy(x => x.Status)
                        .Select(statusGroup => new Tuple<TickerStatus, int>(statusGroup.Key, statusGroup.Count()))
                        .ToArray()
                })
                .FirstOrDefault() ?? new CronOccurrenceTickerGraphData
                {
                    Date = today,
                    Results = Array.Empty<Tuple<TickerStatus, int>>()
                };

            var cronTickerOccurrencesFuture =
                await _persistenceProvider.GetFutureCronTickerOccurrencesByCronTickerId(guid, today);
            var futureData = cronTickerOccurrencesFuture
                .GroupBy(x => x.ExecutionTime.Date)
                .Select(group => new CronOccurrenceTickerGraphData
                {
                    Date = group.Key,
                    Results = group
                        .GroupBy(x => x.Status)
                        .Select(statusGroup => new Tuple<TickerStatus, int>(statusGroup.Key, statusGroup.Count()))
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
                        Results = Array.Empty<Tuple<TickerStatus, int>>()
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
                        Results = Array.Empty<Tuple<TickerStatus, int>>()
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
                    Results = Array.Empty<Tuple<TickerStatus, int>>()
                })
                .ToList();

            return finalData;
        }

        public bool CancelTickerById(Guid tickerId)
        {
            return TickerCancellationTokenManager.RequestTickerCancellationById(tickerId);
        }

        public async Task DeleteCronTickerByIdAsync(Guid id)
        {
            var cronTicker = await _persistenceProvider.GetCronTickerById(id);

            if (cronTicker != null)
            {
                var earliestCronTickerOccurrence =
                    await _persistenceProvider.GetCronOccurrencesByCronTickerIdAndStatusFlag(cronTicker.Id,
                        new[] { TickerStatus.Queued });

                await _persistenceProvider.RemoveCronTickers(new[] { cronTicker });

                if (earliestCronTickerOccurrence.Length != 0)
                    _tickerHost.Restart();

                if (_notificationHubSender != null)
                    await _notificationHubSender.RemoveCronTickerNotifyAsync(id);
            }
        }

        public async Task DeleteTimeTickerByIdAsync(Guid id)
        {
            var timeTicker = await _persistenceProvider.GetTimeTickerById(id);

            await _persistenceProvider.RemoveTimeTickers(new[] { timeTicker });

            _tickerHost.RestartIfNeeded(timeTicker.ExecutionTime);

            if (_notificationHubSender != null)
                await _notificationHubSender.RemoveTimeTickerNotifyAsync(id);
        }

        public async Task DeleteCronTickerOccurrenceByIdAsync(Guid id)
        {
            var cronTickerOccurrence = await _persistenceProvider.GetCronTickerOccurrenceById(id);

            await _persistenceProvider.RemoveCronTickerOccurrences(new[] { cronTickerOccurrence });

            _tickerHost.RestartIfNeeded(cronTickerOccurrence.ExecutionTime);
        }

        public async Task<(string, int)> GetTickerRequestByIdAsync(Guid tickerId, TickerType tickerType)
        {
            byte[] jsonRequestBytes;
            string functionName;

            if (tickerType == TickerType.Timer)
            {
                var timeTicker = await _persistenceProvider.GetTimeTickerById(tickerId);

                if (timeTicker == null)
                    return (string.Empty, 0);

                jsonRequestBytes = timeTicker.Request;
                functionName = timeTicker.Function;
            }
            else
            {
                var cronTicker = await _persistenceProvider.GetCronTickerById(tickerId);

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
            foreach (var tickerFunction in TickerFunctionProvider.TickerFunctions.Select(x =>
                         new { x.Key, x.Value.Priority }))
            {
                if (TickerFunctionProvider.TickerFunctionRequestTypes.TryGetValue(tickerFunction.Key,
                        out var functionTypeContext))
                {
                    JsonExampleGenerator.TryGenerateExampleJson(functionTypeContext.Item2, out var exampleJson);
                    yield return (tickerFunction.Key,
                        (functionTypeContext.Item1, exampleJson, tickerFunction.Priority));
                }
                else
                {
                    yield return (tickerFunction.Key, (string.Empty, null, tickerFunction.Priority));
                }
            }
        }

        public async Task UpdateTimeTickerAsync(Guid id, string jsonBody)
        {
            var timeTicker = await _persistenceProvider.GetTimeTickerById(id);
            var requestType = string.Empty;

            timeTicker.UpdatedAt = DateTime.UtcNow;
            timeTicker.LockedAt = null;
            timeTicker.LockHolder = null;
            timeTicker.Status = TickerStatus.Idle;

            var newTimeTicker = JsonSerializer.Deserialize<JsonElement>(jsonBody);

            if (newTimeTicker.TryGetProperty("function", out var functionProperty))
            {
                timeTicker.Function = functionProperty.GetString();
                requestType = GetRequestType(functionProperty.GetString());
                timeTicker.Request = ProcessRequest(newTimeTicker, functionProperty.GetString());
            }

            if (newTimeTicker.TryGetProperty("description", out var descriptionProperty))
                timeTicker.Description = descriptionProperty.GetString();

            if (newTimeTicker.TryGetProperty("intervals", out var intervalsProperty))
                timeTicker.RetryIntervals = intervalsProperty.EnumerateArray().Select(x => x.GetInt32()).ToArray();

            timeTicker.ExecutionTime = newTimeTicker.TryGetProperty("executionTime", out var executionTimeProperty)
                ? executionTimeProperty.GetDateTime().ToUniversalTime()
                : DateTime.UtcNow.AddSeconds(1);


            _tickerHost.RestartIfNeeded(timeTicker.ExecutionTime);

            await _persistenceProvider.UpdateTimeTickers(new[] { timeTicker });
            await NotifyOrUpdateUpdate(timeTicker, requestType, false);
        }

        public async Task AddTimeTickerAsync(string jsonBody)
        {
            var newTimeTicker = JsonSerializer.Deserialize<JsonElement>(jsonBody);
            var requestType = string.Empty;

            var timeTicker = new TTimeTicker
            {
                Status = TickerStatus.Idle,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                RetryIntervals = newTimeTicker.TryGetProperty("intervals", out var intervalsProperty)
                    ? intervalsProperty.EnumerateArray().Select(x => x.GetInt32()).ToArray()
                    : new[] { 30 }
            };

            if (newTimeTicker.TryGetProperty("retries", out var retryProperty))
                timeTicker.Retries = retryProperty.GetInt32();

            if (newTimeTicker.TryGetProperty("description", out var descriptionProperty))
                timeTicker.Description = descriptionProperty.GetString();

            if (newTimeTicker.TryGetProperty("function", out var functionProperty))
            {
                timeTicker.Function = functionProperty.GetString();
                requestType = GetRequestType(functionProperty.GetString());
                timeTicker.Request = ProcessRequest(newTimeTicker, functionProperty.GetString());
            }

            timeTicker.ExecutionTime = newTimeTicker.TryGetProperty("executionTime", out var executionTimeProperty)
                ? executionTimeProperty.GetDateTime().ToUniversalTime()
                : DateTime.UtcNow.AddSeconds(1);

            await _persistenceProvider.InsertTimeTickers(new[] { timeTicker });

            _tickerHost.RestartIfNeeded(timeTicker.ExecutionTime);

            await NotifyOrUpdateUpdate(timeTicker, requestType, true);
        }

        public async Task AddCronTickerAsync(string jsonBody)
        {
            var requestType = string.Empty;
            var newCronTicker = JsonSerializer.Deserialize<JsonElement>(jsonBody);

            var cronTicker = new TCronTicker
            {
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            cronTicker.RetryIntervals = newCronTicker.TryGetProperty("intervals", out var intervalsProperty)
                ? intervalsProperty.EnumerateArray().Select(x => x.GetInt32()).ToArray()
                : new[] { 30 };

            if (newCronTicker.TryGetProperty("retries", out var retryProperty))
                cronTicker.Retries = retryProperty.GetInt32();

            if (newCronTicker.TryGetProperty("description", out var descriptionProperty))
                cronTicker.Description = descriptionProperty.GetString();

            if (newCronTicker.TryGetProperty("function", out var functionProperty))
            {
                cronTicker.Function = functionProperty.GetString();
                requestType = GetRequestType(functionProperty.GetString());
                cronTicker.Request = ProcessRequest(newCronTicker, functionProperty.GetString());
            }

            if (newCronTicker.TryGetProperty("expression", out var expressionProperty))
                cronTicker.Expression = expressionProperty.GetString();

            await _persistenceProvider.InsertCronTickers(new[] { cronTicker });

            var nextOccurrence = CrontabSchedule.TryParse(cronTicker.Expression)?.GetNextOccurrence(DateTime.UtcNow);

            if (nextOccurrence != null)
                _tickerHost.RestartIfNeeded(nextOccurrence.Value);

            await NotifyOrUpdateUpdate(cronTicker, requestType, true);
        }

        public async Task UpdateCronTickerAsync(Guid id, string jsonBody)
        {
            var newCronTicker = JsonSerializer.Deserialize<JsonElement>(jsonBody);
            var cronTicker = await _persistenceProvider.GetCronTickerById(id);

            if (cronTicker == null)
                throw new KeyNotFoundException($"CronTicker with ID {id} not found.");

            cronTicker.UpdatedAt = DateTime.UtcNow;
            string requestType = string.Empty;

            cronTicker.RetryIntervals = newCronTicker.TryGetProperty("intervals", out var intervalsProperty)
                ? intervalsProperty.EnumerateArray().Select(x => x.GetInt32()).ToArray()
                : new[] { 30 };

            if (newCronTicker.TryGetProperty("retries", out var retryProperty))
                cronTicker.Retries = retryProperty.GetInt32();

            if (newCronTicker.TryGetProperty("description", out var descriptionProperty))
                cronTicker.Description = descriptionProperty.GetString();

            if (newCronTicker.TryGetProperty("function", out var functionProperty))
            {
                cronTicker.Function = functionProperty.GetString();
                requestType = GetRequestType(functionProperty.GetString());
                cronTicker.Request = ProcessRequest(newCronTicker, functionProperty.GetString());
            }

            if (newCronTicker.TryGetProperty("expression", out var expressionProperty))
                cronTicker.Expression = expressionProperty.GetString();

            await _persistenceProvider.UpdateCronTickers(new[] { cronTicker });

            var nextOccurrence = CrontabSchedule.TryParse(cronTicker.Expression)?.GetNextOccurrence(DateTime.UtcNow);

            if (nextOccurrence != null)
                _tickerHost.RestartIfNeeded(nextOccurrence.Value);

            await NotifyOrUpdateUpdate(cronTicker, requestType, false);
        }

        private static string GetRequestType(string function)
        {
            return TickerFunctionProvider.TickerFunctionRequestTypes.TryGetValue(function,
                out var functionTypeContext)
                ? functionTypeContext.Item1
                : string.Empty;
        }

        private static byte[] ProcessRequest(JsonElement newTickerRequest, string function)
        {
            if (!newTickerRequest.TryGetProperty("request", out var requestProperty) ||
                string.IsNullOrEmpty(requestProperty.GetString()))
                return null;

            if (!TickerFunctionProvider.TickerFunctionRequestTypes.TryGetValue(function,
                    out var functionTypeContext) ||
                functionTypeContext == default) return TickerHelper.CreateTickerRequest(requestProperty.GetString());

            var instance = JsonSerializer.Deserialize(requestProperty.GetString(), functionTypeContext.Item2);
            return TickerHelper.CreateTickerRequest(instance);
        }

        private async Task NotifyOrUpdateUpdate<T>(T ticker, string requestType, bool isNew) where T : class
        {
            if (_notificationHubSender != null)
            {
                switch (ticker)
                {
                    case TTimeTicker timeTicker:
                        {
                            var timeTickerDto = new TimeTickerDto
                            {
                                Id = timeTicker.Id,
                                Function = timeTicker.Function,
                                CreatedAt = timeTicker.CreatedAt,
                                UpdatedAt = timeTicker.UpdatedAt,
                                RequestType = requestType,
                                Status = timeTicker.Status,
                                ExecutionTime = timeTicker.ExecutionTime,
                                RetryIntervals = timeTicker.RetryIntervals,
                                Retries = timeTicker.Retries,
                                Description = timeTicker.Description,
                                BatchRunCondition = timeTicker.BatchRunCondition,
                                BatchParent = timeTicker.BatchParent
                            };
                            if (isNew)
                                await _notificationHubSender.AddTimeTickerNotifyAsync(timeTickerDto);
                            else
                                await _notificationHubSender.UpdateTimeTickerNotifyAsync(timeTickerDto);
                            break;
                        }
                    case TCronTicker cronTicker:
                        {
                            var cronTickerDto = new CronTickerDto
                            {
                                Id = cronTicker.Id,
                                Function = cronTicker.Function,
                                Expression = cronTicker.Expression,
                                ExpressionReadable = TickerCronExpressionHelper.ToHumanReadable(cronTicker.Expression),
                                CreatedAt = cronTicker.CreatedAt,
                                UpdatedAt = cronTicker.UpdatedAt,
                                RequestType = requestType,
                                RetryIntervals = cronTicker.RetryIntervals,
                                Description = cronTicker.Description,
                                Retries = cronTicker.Retries,
                            };
                            if (isNew)
                                await _notificationHubSender.AddCronTickerNotifyAsync(cronTickerDto);
                            else
                                await _notificationHubSender.UpdateCronTickerNotifyAsync(cronTickerDto);
                            break;
                        }
                }
            }
        }
    }
}