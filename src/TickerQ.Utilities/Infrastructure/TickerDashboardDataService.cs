using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TickerQ.Utilities.DashboardDtos;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ.Utilities.Infrastructure
{
    /// <summary>
    /// Default implementation of <see cref="ITickerDashboardDataService{TTimeTicker,TCronTicker}"/>
    /// that projects entities to flat DTOs using <see cref="ITickerQueryable{T}"/>.
    /// </summary>
    public sealed partial class TickerDashboardDataService<TTimeTicker, TCronTicker>
        : ITickerDashboardDataService<TTimeTicker, TCronTicker>
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        private readonly ITickerPersistenceProvider<TTimeTicker, TCronTicker> _persistence;
        private readonly ITickerQHostScheduler _hostScheduler;
        private readonly ITickerQTaskScheduler _taskScheduler;
        private readonly TickerQ.Utilities.SchedulerOptionsBuilder _schedulerOptions;
        private readonly ITickerQDispatcher _dispatcher;
        private readonly TickerLifecycleLogger _lifecycle;
        private readonly ITickerQNotificationHubSender _notificationHubSender;
        private readonly ILogger<TickerDashboardDataService<TTimeTicker, TCronTicker>> _logger;

        public TickerDashboardDataService(
            ITickerPersistenceProvider<TTimeTicker, TCronTicker> persistence,
            ITickerQHostScheduler hostScheduler = null,
            ITickerQTaskScheduler taskScheduler = null,
            TickerQ.Utilities.SchedulerOptionsBuilder schedulerOptions = null)
            : this(persistence, hostScheduler, taskScheduler, schedulerOptions, null, null)
        {
        }

        internal TickerDashboardDataService(
            ITickerPersistenceProvider<TTimeTicker, TCronTicker> persistence,
            ITickerQHostScheduler hostScheduler,
            ITickerQTaskScheduler taskScheduler,
            TickerQ.Utilities.SchedulerOptionsBuilder schedulerOptions,
            ITickerQDispatcher dispatcher,
            ITickerQNotificationHubSender notificationHubSender,
            ILogger<TickerDashboardDataService<TTimeTicker, TCronTicker>> logger = null,
            TickerLifecycleLogger lifecycle = null)
        {
            _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
            _hostScheduler = hostScheduler;
            _taskScheduler = taskScheduler;
            _schedulerOptions = schedulerOptions;
            _dispatcher = dispatcher;
            _notificationHubSender = notificationHubSender;
            _logger = logger;
            _lifecycle = lifecycle;
        }

        // ============ Time Tickers ============

        public async Task<PaginationResult<TimeTickerFlatDto>> GetTimeTickersFlatAsync(
            TimeTickerQueryFilter filter, CancellationToken cancellationToken = default)
        {
            filter ??= new TimeTickerQueryFilter();
            // Native providers project scalars without loading navigation graphs.
            // Legacy query providers retain their relationship-loading fallback.
            var query = TimeMetadataQuery().WithRelated(TickerRelation.Children);

            if (!string.IsNullOrWhiteSpace(filter.FunctionName))
                query = query.Where(x => x.Function.Contains(filter.FunctionName));

            if (filter.Statuses is { Length: > 0 })
                query = query.Where(x => filter.Statuses.Contains(x.Status));

            if (filter.ScheduledFrom.HasValue)
                query = query.Where(x => x.ExecutionTime >= filter.ScheduledFrom);

            if (filter.ScheduledTo.HasValue)
                query = query.Where(x => x.ExecutionTime <= filter.ScheduledTo);

            if (filter.ExecutedFrom.HasValue)
                query = query.Where(x => x.ExecutedAt >= filter.ExecutedFrom);

            if (filter.ExecutedTo.HasValue)
                query = query.Where(x => x.ExecutedAt <= filter.ExecutedTo);

            // Always exclude chain children from the table — they're only viewable
            // through the flowchart of their parent. A chain child has ExecutionTime
            // null and ParentId set; without this filter, the View=All / Standalone
            // tabs would surface them as if they were independently scheduled rows
            // (and clicking them would go nowhere because they're framework-managed).
            // Cron-generated roots belong to their cron occurrence view. Filter
            // them here, before counting and paging, rather than hiding client rows.
            query = query.Where(x => x.ParentId == null && x.OriginCronTickerId == null);

            // HasChildren: true → only chain parents (Children.Any()); false → roots
            // with no children (truly standalone). Combined with the ParentId==null
            // filter above this gives the three modes:
            //   All        → all roots (chains + standalone)
            //   Chains     → roots that have children
            //   Standalone → roots that have no children
            if (filter.HasChildren.HasValue)
            {
                query = await DashboardMetadataQueryable<TTimeTicker>.WhereHasChildrenAsync(query,
                    filter.HasChildren.Value,
                    filter.HasChildren.Value ? x => x.Children.Any() : x => !x.Children.Any(),
                    cancellationToken).ConfigureAwait(false);
            }

            if (!string.IsNullOrWhiteSpace(filter.Search))
            {
                var search = filter.Search;
                query = query.Where(x =>
                    (x.Function != null && x.Function.Contains(search)) ||
                    (x.Description != null && x.Description.Contains(search)));
            }

            query = ApplyTimeTickerSort(query, filter.SortBy, filter.SortDescending);

            var result = await query.ToPaginatedAsync(filter.PageNumber, filter.PageSize, cancellationToken)
                .ConfigureAwait(false);

            var items = result.Items.Select(MapTimeTicker).ToArray();
            await PopulateTimeChainSummariesAsync(items, cancellationToken).ConfigureAwait(false);
            return new PaginationResult<TimeTickerFlatDto>(items, result.TotalCount, result.PageNumber, result.PageSize);
        }

        public async Task<TimeTickerFlatDto> GetTimeTickerByIdAsync(
            Guid id, CancellationToken cancellationToken = default)
        {
            var entity = DashboardTimeSource() is ITickerProjectionQueryable<TTimeTicker> { SupportsProjection: true }
                ? await ChainMetadataQuery().Where(x => x.Id == id).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false)
                : await _persistence.GetTimeTickerById(id, cancellationToken).ConfigureAwait(false);
            if (entity == null) return null;
            var item = MapTimeTicker(entity);
            await PopulateTimeChainSummariesAsync(new[] { item }, cancellationToken).ConfigureAwait(false);
            return item;
        }

        public async Task<IList<TimeTickerFlatDto>> GetTimeTickerChildrenAsync(
            Guid parentId, CancellationToken cancellationToken = default)
        {
            var children = await ChainMetadataQuery()
                .Where(x => x.ParentId == parentId)
                .OrderBy(x => x.ExecutionTime)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);

            var items = children.Select(MapTimeTicker).ToList();
            await PopulateDirectChildCountsAsync(items, cancellationToken).ConfigureAwait(false);
            return items;
        }

        public async Task<IList<TimeTickerFlatDto>> GetTimeTickerChainAsync(Guid rootId, CancellationToken cancellationToken = default)
        {
            var root = await ChainMetadataQuery().AsNoTracking()
                .Where(x => x.Id == rootId).FirstOrDefaultAsync(cancellationToken);
            if (root == null) return new List<TimeTickerFlatDto>();
            var all = new List<TimeTickerFlatDto> { MapTimeTicker(root) };
            var seen = new HashSet<Guid> { rootId };
            var frontier = new[] { rootId };
            for (var depth = 0; frontier.Length > 0; depth++)
            {
                // One bounded query per level, including one extra row to detect overflow.
                var children = await ChainMetadataQuery().AsNoTracking()
                    .Where(x => x.ParentId.HasValue && frontier.Contains(x.ParentId.Value))
                    .OrderBy(x => x.Id).Take(501 - all.Count).ToArrayAsync(cancellationToken);
                if (children.Length == 0) break;
                if (depth >= 20 || all.Count + children.Length > 500 || children.Any(x => !seen.Add(x.Id)))
                    throw new ArgumentOutOfRangeException(nameof(rootId), "Chain exceeds 500 nodes or 20 child levels, or contains a cycle.");
                all.AddRange(children.Select(MapTimeTicker));
                frontier = children.Select(x => x.Id).ToArray();
            }
            var counts = all.Where(x => x.ParentId.HasValue).GroupBy(x => x.ParentId.Value).ToDictionary(x => x.Key, x => x.Count());
            foreach (var item in all) item.ChildCount = counts.GetValueOrDefault(item.Id);
            if (all.Count > 1) all[0].ChainSummary = ChainExecutionSummaryDto.FromStatuses(all.Select(x => x.Status));
            return all;
        }

        // ============ Cron Tickers ============

        public async Task<PaginationResult<CronTickerFlatDto>> GetCronTickersFlatAsync(
            CronTickerQueryFilter filter, CancellationToken cancellationToken = default)
        {
            filter ??= new CronTickerQueryFilter();
            var query = CronMetadataQuery();

            if (!string.IsNullOrWhiteSpace(filter.FunctionName))
                query = query.Where(x => x.Function.Contains(filter.FunctionName));

            if (!string.IsNullOrWhiteSpace(filter.Expression))
                query = query.Where(x => x.Expression.Contains(filter.Expression));

            if (!string.IsNullOrWhiteSpace(filter.Search))
            {
                var search = filter.Search;
                query = query.Where(x =>
                    (x.Function != null && x.Function.Contains(search)) ||
                    (x.Expression != null && x.Expression.Contains(search)) ||
                    (x.Description != null && x.Description.Contains(search)));
            }

            query = ApplyCronTickerSort(query, filter.SortBy, filter.SortDescending);

            var result = await query.ToPaginatedAsync(filter.PageNumber, filter.PageSize, cancellationToken)
                .ConfigureAwait(false);

            var items = new List<CronTickerFlatDto>(result.Items.Count());
            foreach (var cron in result.Items)
            {
                items.Add(await MapCronTickerWithLastRunAsync(cron, cancellationToken).ConfigureAwait(false));
            }

            if (filter.LastRunStatuses is { Length: > 0 })
                items = items.Where(x => x.LastRunStatus.HasValue && filter.LastRunStatuses.Contains(x.LastRunStatus.Value)).ToList();

            await PopulateCronChainSummariesAsync(items, cancellationToken).ConfigureAwait(false);
            return new PaginationResult<CronTickerFlatDto>(items.ToArray(), result.TotalCount, result.PageNumber, result.PageSize);
        }

        public async Task<CronTickerFlatDto> GetCronTickerByIdAsync(
            Guid id, CancellationToken cancellationToken = default)
        {
            var entity = await ReadCronMetadataAsync(id, cancellationToken).ConfigureAwait(false);
            if (entity == null) return null;
            var item = await MapCronTickerWithLastRunAsync(entity, cancellationToken).ConfigureAwait(false);
            await PopulateCronChainSummariesAsync(new[] { item }, cancellationToken).ConfigureAwait(false);
            return item;
        }

        // ============ Cron Occurrences ============

        public async Task<PaginationResult<CronOccurrenceFlatDto>> GetCronOccurrencesFlatAsync(
            Guid cronTickerId, CronOccurrenceQueryFilter filter, CancellationToken cancellationToken = default)
        {
            filter ??= new CronOccurrenceQueryFilter();
            var query = OccurrenceMetadataQuery()
                .Where(x => x.CronTickerId == cronTickerId);

            if (filter.Statuses is { Length: > 0 })
                query = query.Where(x => filter.Statuses.Contains(x.Status));

            if (filter.ScheduledFrom.HasValue)
                query = query.Where(x => x.ExecutionTime >= filter.ScheduledFrom);

            if (filter.ScheduledTo.HasValue)
                query = query.Where(x => x.ExecutionTime <= filter.ScheduledTo);

            query = ApplyOccurrenceSort(query, filter.SortBy, filter.SortDescending);

            var result = await query.ToPaginatedAsync(filter.PageNumber, filter.PageSize, cancellationToken)
                .ConfigureAwait(false);

            var parentFunction = (await ReadCronMetadataAsync(cronTickerId, cancellationToken).ConfigureAwait(false))?.Function;

            var items = result.Items.Select(x => MapOccurrence(x, parentFunction)).ToArray();
            await PopulateOccurrenceChainsAsync(items, cancellationToken).ConfigureAwait(false);
            return new PaginationResult<CronOccurrenceFlatDto>(items, result.TotalCount, result.PageNumber, result.PageSize);
        }

        public async Task<CronOccurrenceFlatDto> GetCronOccurrenceByIdAsync(
            Guid id, CancellationToken cancellationToken = default)
        {
            var entity = await OccurrenceMetadataQuery()
                .Where(x => x.Id == id)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            if (entity == null) return null;

            var parent = await ReadCronMetadataAsync(entity.CronTickerId, cancellationToken).ConfigureAwait(false);
            var item = MapOccurrence(entity, parent?.Function);
            await PopulateOccurrenceChainsAsync(new[] { item }, cancellationToken).ConfigureAwait(false);
            return item;
        }

        private async Task PopulateOccurrenceChainsAsync(CronOccurrenceFlatDto[] items, CancellationToken cancellationToken)
        {
            var snapshots = await ReadChainSnapshotsAsync(items.Where(x => x.ChainRootId.HasValue)
                .Select(x => x.ChainRootId.Value), cancellationToken).ConfigureAwait(false);
            foreach (var item in items)
            {
                if (item.ChainRootId.HasValue && snapshots.TryGetValue(item.ChainRootId.Value, out var chain) &&
                    chain.Root.OriginCronTickerId == item.CronTickerId)
                {
                    item.ChainSummary = chain.Summary;
                    if (item.LockHolder == null) item.ChainLockHolder = chain.Root.LockHolder;
                }
            }
        }

        // ============ Executions (unified) ============

        public async Task<PaginationResult<ExecutionFlatDto>> GetExecutionsFlatAsync(
            ExecutionQueryFilter filter, CancellationToken cancellationToken = default)
        {
            filter ??= new ExecutionQueryFilter();
            var includeTime = filter.Types == null || filter.Types.Length == 0 || filter.Types.Contains(ExecutionType.TimeTicker);
            var includeCron = filter.Types == null || filter.Types.Length == 0 || filter.Types.Contains(ExecutionType.CronOccurrence);

            var all = new List<ExecutionFlatDto>();

            if (includeTime)
            {
                // Exclude chain children — they're owned by their root row and
                // surface in the chain flowchart, not as standalone executions.
                // Showing every child would 5x-pad a fan-out chain in the
                // executions list and bury the rows the user actually scans.
                //
                // Legacy providers load relationships here. Native projections
                // calculate child counts using the bounded chain-summary read below.
                var timeQuery = TimeMetadataQuery()
                    .WithRelated(TickerRelation.Children)
                    .Where(x => x.ExecutedAt != null && x.ParentId == null);
                timeQuery = ApplyExecutionTimeFilters(timeQuery, filter);
                var timeItems = await timeQuery.ToArrayAsync(cancellationToken).ConfigureAwait(false);
                all.AddRange(timeItems.Select(MapTimeTickerToExecution));
            }

            if (includeCron)
            {
                var cronQuery = OccurrenceMetadataQuery()
                    .Where(x => x.ExecutedAt != null);
                cronQuery = ApplyExecutionOccurrenceFilters(cronQuery, filter);
                var cronItems = await cronQuery.ToArrayAsync(cancellationToken).ConfigureAwait(false);
                foreach (var item in cronItems)
                {
                    var parent = await ReadCronMetadataAsync(item.CronTickerId, cancellationToken).ConfigureAwait(false);
                    all.Add(MapOccurrenceToExecution(item, parent?.Function, parent?.Retries ?? 0));
                }
            }

            if (filter.Statuses is { Length: > 0 })
                all = all.Where(x => filter.Statuses.Contains(x.Status)).ToList();

            if (!string.IsNullOrWhiteSpace(filter.FunctionName))
                all = all.Where(x => x.FunctionName != null && x.FunctionName.Contains(filter.FunctionName)).ToList();

            if (!string.IsNullOrWhiteSpace(filter.Search))
            {
                var search = filter.Search;
                all = all.Where(x =>
                    (x.FunctionName != null && x.FunctionName.Contains(search)) ||
                    (x.ExceptionMessage != null && x.ExceptionMessage.Contains(search))).ToList();
            }

            var sorted = ApplyExecutionSort(all, filter.SortBy, filter.SortDescending);
            var total = sorted.Count;
            var paged = sorted
                .Skip((Math.Max(1, filter.PageNumber) - 1) * filter.PageSize)
                .Take(filter.PageSize)
                .ToArray();

            await PopulateExecutionChainSummariesAsync(paged, cancellationToken).ConfigureAwait(false);
            return new PaginationResult<ExecutionFlatDto>(paged, total, filter.PageNumber, filter.PageSize);
        }

        // ============ Request payloads ============

        public Task<byte[]> GetTimeTickerRequestAsync(Guid id, CancellationToken cancellationToken = default)
            => _persistence.GetTimeTickerRequest(id, cancellationToken);

        public Task<byte[]> GetCronOccurrenceRequestAsync(Guid id, CancellationToken cancellationToken = default)
            => _persistence.GetCronTickerOccurrenceRequest(id, cancellationToken);

        // ============ Stats ============

        public async Task<IList<(TickerStatus Status, int Count)>> GetOverallStatusesAsync(
            CancellationToken cancellationToken = default)
        {
            IReadOnlyDictionary<TickerStatus, int> counts;
            if (_persistence is ITickerDashboardStatisticsProvider statistics)
                counts = await statistics.GetOverallJobStatusCountsAsync(cancellationToken).ConfigureAwait(false);
            else
            {
                // Third-party providers retain their existing metadata/projection fallback.
                var timeTickers = await TimeMetadataQuery().ToArrayAsync(cancellationToken).ConfigureAwait(false);
                var occurrences = await OccurrenceMetadataQuery().ToArrayAsync(cancellationToken).ConfigureAwait(false);
                counts = timeTickers.Select(x => x.Status).Concat(occurrences.Select(x => x.Status))
                    .GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());
            }
            return Enum.GetValues<TickerStatus>().Select(status =>
                (status, counts.TryGetValue(status, out var count) ? count : 0)).ToList();
        }

        public Task<IList<(string NodeName, int JobCount)>> GetNodeJobsAsync(
            CancellationToken cancellationToken = default)
        {
            // Per-node execution tracking was removed from the scheduler; node stats are
            // derived from the Hub's NodeApplication registry instead.
            return Task.FromResult<IList<(string NodeName, int JobCount)>>(Array.Empty<(string, int)>());
        }

        // ============ Overview ============

        public async Task<IList<TimeTickerFlatDto>> GetUpcomingTickersAsync(
            int count, CancellationToken cancellationToken = default)
        {
            if (count <= 0) count = 10;
            var now = DateTime.UtcNow;

            // Pull both feeds — time tickers and cron-ticker occurrences — and
            // merge into a unified "next runs" list. Without the cron half the
            // overview's Next Scheduled card hid every recurring job that the
            // polling loop had already materialized as an Idle/Queued occurrence.
            var upcomingTime = await TimeMetadataQuery()
                .Where(x => x.ExecutionTime > now &&
                           (x.Status == TickerStatus.Idle || x.Status == TickerStatus.Queued))
                .OrderBy(x => x.ExecutionTime)
                .Take(count)
                .ToArrayAsync(cancellationToken).ConfigureAwait(false);

            var upcomingCron = await OccurrenceMetadataQuery()
                .Where(x => x.ExecutionTime > now &&
                           (x.Status == TickerStatus.Idle || x.Status == TickerStatus.Queued))
                .OrderBy(x => x.ExecutionTime)
                .Take(count)
                .ToArrayAsync(cancellationToken).ConfigureAwait(false);

            var combined = new List<TimeTickerFlatDto>(upcomingTime.Length + upcomingCron.Length);
            combined.AddRange(upcomingTime.Select(MapTimeTicker));

            foreach (var occ in upcomingCron)
            {
                // Resolve the parent cron's Function — occurrences carry the
                // cron id but the function name lives on the parent row. Cache
                // would be nice but `count` keeps this small and bounded.
                var parent = await ReadCronMetadataAsync(occ.CronTickerId, cancellationToken).ConfigureAwait(false);
                combined.Add(MapCronOccurrenceToTimeTickerDto(occ, parent));
            }

            var upcoming = combined
                .OrderBy(x => x.ScheduledFor)
                .Take(count)
                .ToList();
            await PopulateTimeChainSummariesAsync(upcoming, cancellationToken).ConfigureAwait(false);
            return upcoming;
        }

        /// <summary>
        /// Adapter so cron occurrences can sit alongside time tickers in the
        /// overview's Next Scheduled card. Function/Priority/Retries come from
        /// the parent cron entity; Description borrows the cron expression so
        /// the dashboard can show users where the row originated.
        /// </summary>
        private static TimeTickerFlatDto MapCronOccurrenceToTimeTickerDto(
            CronTickerOccurrenceEntity<TCronTicker> occ, TCronTicker parent)
        {
            var function = parent?.Function ?? string.Empty;
            var priority = TickerTaskPriority.Normal;
            if (TickerFunctionProvider.TickerFunctions.TryGetValue(function, out var info))
                priority = info.Priority;

            return new TimeTickerFlatDto
            {
                Id = occ.Id,
                Type = ExecutionType.CronOccurrence,
                CronTickerId = occ.CronTickerId,
                FunctionName = function,
                Status = occ.Status,
                ScheduledFor = occ.ExecutionTime,
                ElapsedTime = occ.ElapsedTime,
                Retries = parent?.Retries ?? 0,
                RetryCount = occ.RetryCount,
                Priority = priority,
                CreatedAt = occ.CreatedAt,
                ChildCount = 0,
                ExceptionMessage = occ.ExceptionMessage,
                SkippedReason = null,
                ParentId = null,
                RunCondition = null,
                ExecutedAt = occ.ExecutedAt,
                Description = parent != null ? $"Cron: {parent.Expression}" : null,
                LockHolder = occ.LockHolder,
                LockedAt = occ.LockedAt,
                RetryIntervalsSeconds = parent?.RetryIntervals,
                OnStale = parent?.OnStale ?? StaleAction.Restart,
            };
        }

        public async Task<IList<ExecutionFlatDto>> GetRecentActivityAsync(
            int count, CancellationToken cancellationToken = default)
        {
            if (count <= 0) count = 10;

            var recentTime = await TimeMetadataQuery()
                .Where(x => x.ExecutedAt != null && x.ParentId == null)
                .OrderByDescending(x => x.ExecutedAt)
                .Take(count)
                .ToArrayAsync(cancellationToken).ConfigureAwait(false);

            var recentCron = await OccurrenceMetadataQuery()
                .Where(x => x.ExecutedAt != null)
                .OrderByDescending(x => x.ExecutedAt)
                .Take(count)
                .ToArrayAsync(cancellationToken).ConfigureAwait(false);

            var combined = new List<ExecutionFlatDto>(recentTime.Length + recentCron.Length);
            combined.AddRange(recentTime.Select(MapTimeTickerToExecution));

            foreach (var occ in recentCron)
            {
                var parent = await ReadCronMetadataAsync(occ.CronTickerId, cancellationToken).ConfigureAwait(false);
                combined.Add(MapOccurrenceToExecution(occ, parent?.Function, parent?.Retries ?? 0));
            }

            var recent = combined
                .OrderByDescending(x => x.ExecutedAt)
                .Take(count)
                .ToList();
            await PopulateExecutionChainSummariesAsync(recent, cancellationToken).ConfigureAwait(false);
            return recent;
        }

        // ============ Mappers ============

        private static TimeTickerFlatDto MapTimeTicker(TTimeTicker e)
        {
            var priority = TickerTaskPriority.Normal;
            var childCount = e.Children?.Count ?? 0;

            if (TickerFunctionProvider.TickerFunctions.TryGetValue(e.Function, out var info))
                priority = info.Priority;

            return new TimeTickerFlatDto
            {
                Id = e.Id,
                FunctionName = e.Function,
                Status = e.Status,
                ScheduledFor = e.ExecutionTime,
                ElapsedTime = e.ElapsedTime,
                Retries = e.Retries,
                RetryCount = e.RetryCount,
                Priority = priority,
                CreatedAt = e.CreatedAt,
                ChildCount = childCount,
                ExceptionMessage = e.ExceptionMessage,
                SkippedReason = e.SkippedReason,
                ParentId = e.ParentId,
                DeleteBehavior = e.DeleteBehavior,
                OriginCronTickerId = e.OriginCronTickerId,
                RunCondition = e.RunCondition,
                ExecutedAt = e.ExecutedAt,
                Description = e.Description,
                LockHolder = e.LockHolder,
                LockedAt = e.LockedAt,
                RetryIntervalsSeconds = e.RetryIntervals,
                OnStale = e.OnStale,
                TimeoutSeconds = e.TimeoutSeconds,
            };
        }

        private async Task<CronTickerFlatDto> MapCronTickerWithLastRunAsync(
            TCronTicker c, CancellationToken cancellationToken)
        {
            var priority = TickerTaskPriority.Normal;
            if (TickerFunctionProvider.TickerFunctions.TryGetValue(c.Function, out var info))
                priority = info.Priority;

            var occurrences = OccurrenceMetadataQuery().Where(x => x.CronTickerId == c.Id);
            var occurrenceCount = await occurrences.CountAsync(cancellationToken).ConfigureAwait(false);
            var lastRun = await occurrences.Where(x => x.ExecutedAt != null)
                .OrderByDescending(x => x.ExecutedAt).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

            return new CronTickerFlatDto
            {
                Id = c.Id,
                Chain = c.Chain,
                FunctionName = c.Function,
                Expression = c.Expression,
                Description = c.Description,
                Retries = c.Retries,
                Priority = priority,
                CreatedAt = c.CreatedAt,
                OccurrenceCount = occurrenceCount,
                LastRunStatus = lastRun?.Status,
                LastRunChainRootId = lastRun?.ChainRootId,
                LastRunAt = lastRun?.ExecutedAt,
                SkipIfAlreadyRunning = c.SkipIfAlreadyRunning,
                IsEnabled = c.IsEnabled,
                IsSystemPaused = c.IsSystemPaused,
                RetryIntervalsSeconds = c.RetryIntervals,
                OnStale = c.OnStale,
                TimeoutSeconds = c.TimeoutSeconds,
            };
        }

        private static CronOccurrenceFlatDto MapOccurrence(
            CronTickerOccurrenceEntity<TCronTicker> e, string parentFunction)
        {
            return new CronOccurrenceFlatDto
            {
                Id = e.Id,
                CronTickerId = e.CronTickerId,
                ChainRootId = e.ChainRootId,
                FunctionName = parentFunction,
                Status = e.Status,
                ScheduledFor = e.ExecutionTime,
                ElapsedTime = e.ElapsedTime,
                RetryCount = e.RetryCount,
                ExceptionMessage = e.ExceptionMessage,
                SkippedReason = e.SkippedReason,
                ExecutedAt = e.ExecutedAt,
                CreatedAt = e.CreatedAt,
                LockHolder = e.LockHolder,
                LockedAt = e.LockedAt,
            };
        }

        private static ExecutionFlatDto MapTimeTickerToExecution(TTimeTicker e)
            => new()
            {
                Id = e.Id,
                Type = ExecutionType.TimeTicker,
                FunctionName = e.Function,
                Status = e.Status,
                ScheduledFor = e.ExecutionTime ?? default,
                ElapsedTime = e.ElapsedTime,
                RetryCount = e.RetryCount,
                Retries = e.Retries,
                ExceptionMessage = e.ExceptionMessage,
                SkippedReason = e.SkippedReason,
                ExecutedAt = e.ExecutedAt,
                ChildCount = e.Children?.Count ?? 0,
                LockHolder = e.LockHolder,
                LockedAt = e.LockedAt,
            };

        // Cron occurrences inherit Retries from the parent cron ticker — the
        // count lives on the run row, the max lives on the schedule. Caller
        // must pass `parent` (already loaded for FunctionName) so we don't
        // re-query per row.
        private static ExecutionFlatDto MapOccurrenceToExecution(
            CronTickerOccurrenceEntity<TCronTicker> e, string functionName, int parentRetries)
            => new()
            {
                Id = e.Id,
                Type = ExecutionType.CronOccurrence,
                CronTickerId = e.CronTickerId,
                ChainRootId = e.ChainRootId,
                FunctionName = functionName,
                Status = e.Status,
                ScheduledFor = e.ExecutionTime,
                ElapsedTime = e.ElapsedTime,
                RetryCount = e.RetryCount,
                Retries = parentRetries,
                ExceptionMessage = e.ExceptionMessage,
                SkippedReason = e.SkippedReason,
                ExecutedAt = e.ExecutedAt,
                LockHolder = e.LockHolder,
                LockedAt = e.LockedAt,
            };

        // ============ Sort helpers ============

        private static ITickerQueryable<TTimeTicker> ApplyTimeTickerSort(
            ITickerQueryable<TTimeTicker> query, string sortBy, bool descending)
        {
            sortBy = (sortBy ?? "createdat").ToLowerInvariant();
            return sortBy switch
            {
                "status" => descending ? query.OrderByDescending(x => x.Status) : query.OrderBy(x => x.Status),
                "functionname" => descending ? query.OrderByDescending(x => x.Function) : query.OrderBy(x => x.Function),
                "executedat" => descending ? query.OrderByDescending(x => x.ExecutedAt) : query.OrderBy(x => x.ExecutedAt),
                "executiontime" => descending ? query.OrderByDescending(x => x.ElapsedTime) : query.OrderBy(x => x.ElapsedTime),
                "scheduledfor" => descending ? query.OrderByDescending(x => x.ExecutionTime) : query.OrderBy(x => x.ExecutionTime),
                _ => descending ? query.OrderByDescending(x => x.CreatedAt) : query.OrderBy(x => x.CreatedAt)
            };
        }

        private static ITickerQueryable<TCronTicker> ApplyCronTickerSort(
            ITickerQueryable<TCronTicker> query, string sortBy, bool descending)
        {
            sortBy = (sortBy ?? "createdat").ToLowerInvariant();
            return sortBy switch
            {
                "functionname" => descending ? query.OrderByDescending(x => x.Function) : query.OrderBy(x => x.Function),
                "expression" => descending ? query.OrderByDescending(x => x.Expression) : query.OrderBy(x => x.Expression),
                _ => descending ? query.OrderByDescending(x => x.CreatedAt) : query.OrderBy(x => x.CreatedAt)
            };
        }

        private static ITickerQueryable<CronTickerOccurrenceEntity<TCronTicker>> ApplyOccurrenceSort(
            ITickerQueryable<CronTickerOccurrenceEntity<TCronTicker>> query, string sortBy, bool descending)
        {
            sortBy = (sortBy ?? "scheduledfor").ToLowerInvariant();
            return sortBy switch
            {
                "status" => descending ? query.OrderByDescending(x => x.Status) : query.OrderBy(x => x.Status),
                "executedat" => descending ? query.OrderByDescending(x => x.ExecutedAt) : query.OrderBy(x => x.ExecutedAt),
                "executiontime" => descending ? query.OrderByDescending(x => x.ElapsedTime) : query.OrderBy(x => x.ElapsedTime),
                // Newest-first ordering for the dashboard list — matches the
                // user's mental model after Run Now / cron tick (newest row
                // appears at the top, not buried behind future ScheduledFor).
                "createdat" => descending ? query.OrderByDescending(x => x.CreatedAt) : query.OrderBy(x => x.CreatedAt),
                _ => descending ? query.OrderByDescending(x => x.ExecutionTime) : query.OrderBy(x => x.ExecutionTime)
            };
        }

        private static List<ExecutionFlatDto> ApplyExecutionSort(
            List<ExecutionFlatDto> items, string sortBy, bool descending)
        {
            sortBy = (sortBy ?? "executedat").ToLowerInvariant();
            IOrderedEnumerable<ExecutionFlatDto> ordered = sortBy switch
            {
                "status" => descending ? items.OrderByDescending(x => x.Status) : items.OrderBy(x => x.Status),
                "functionname" => descending ? items.OrderByDescending(x => x.FunctionName) : items.OrderBy(x => x.FunctionName),
                "duration" => descending ? items.OrderByDescending(x => x.ElapsedTime) : items.OrderBy(x => x.ElapsedTime),
                "scheduledfor" => descending ? items.OrderByDescending(x => x.ScheduledFor) : items.OrderBy(x => x.ScheduledFor),
                _ => descending ? items.OrderByDescending(x => x.ExecutedAt) : items.OrderBy(x => x.ExecutedAt)
            };
            return ordered.ToList();
        }

        private static ITickerQueryable<TTimeTicker> ApplyExecutionTimeFilters(
            ITickerQueryable<TTimeTicker> query, ExecutionQueryFilter filter)
        {
            if (filter.ExecutedFrom.HasValue)
                query = query.Where(x => x.ExecutedAt >= filter.ExecutedFrom);
            if (filter.ExecutedTo.HasValue)
                query = query.Where(x => x.ExecutedAt <= filter.ExecutedTo);
            return query;
        }

        private static ITickerQueryable<CronTickerOccurrenceEntity<TCronTicker>> ApplyExecutionOccurrenceFilters(
            ITickerQueryable<CronTickerOccurrenceEntity<TCronTicker>> query, ExecutionQueryFilter filter)
        {
            if (filter.ExecutedFrom.HasValue)
                query = query.Where(x => x.ExecutedAt >= filter.ExecutedFrom);
            if (filter.ExecutedTo.HasValue)
                query = query.Where(x => x.ExecutedAt <= filter.ExecutedTo);
            return query;
        }

        // ============ Nodes ============

        public Task<IList<NodeDto>> GetNodesAsync(CancellationToken cancellationToken = default)
        {
            // Per-node execution tracking was removed from the scheduler; the Hub derives
            // node presence from its NodeApplication registry.
            return Task.FromResult<IList<NodeDto>>(Array.Empty<NodeDto>());
        }

        private static NodeHealthStatus ComputeHealth(DateTime? lastHeartbeat, DateTime now)
        {
            if (!lastHeartbeat.HasValue) return NodeHealthStatus.Down;
            var age = now - lastHeartbeat.Value;
            if (age <= TimeSpan.FromMinutes(5)) return NodeHealthStatus.Healthy;
            if (age <= TimeSpan.FromMinutes(30)) return NodeHealthStatus.Degraded;
            return NodeHealthStatus.Down;
        }

        public Task<IList<FunctionInfoDto>> GetNodeFunctionsAsync(string nodeName, CancellationToken cancellationToken = default)
        {
            // No per-node function mapping is persisted on the scheduler side — return all functions;
            // caller can filter by what executed on this node via the executions endpoint.
            return GetAllFunctionsAsync(cancellationToken);
        }

        public Task<IList<FunctionInfoDto>> GetAllFunctionsAsync(CancellationToken cancellationToken = default)
        {
            var registry = TickerFunctionProvider.Snapshot;
            var descriptors = registry.Descriptors;
            var result = descriptors.Values.Select(descriptor =>
            {
                var request = descriptor.Request;
                FunctionRequestContractDto requestDto = null;
                if (request != null)
                {
                    requestDto = new FunctionRequestContractDto
                    {
                        TypeName = request.TypeName,
                        MediaType = request.MediaType,
                        Required = request.Required,
                        SchemaDialect = request.SchemaDialect,
                        SchemaJson = request.Schema?.GetRawText(),
                        Fingerprint = request.Fingerprint,
                        Examples = request.Examples.Select(example => new FunctionRequestExampleDto
                        {
                            Key = example.Key,
                            Summary = example.Summary,
                            ValueJson = example.Value.GetRawText()
                        }).ToList()
                    };
                }

                return new FunctionInfoDto
                {
                    FunctionName = descriptor.FunctionName,
                    ContractVersion = descriptor.ContractVersion,
                    RequestContract = requestDto,
                    RequestType = request?.TypeName,
                    RequestExample = request?.Examples.FirstOrDefault()?.Value.GetRawText(),
                    Priority = descriptor.Priority,
                    CronExpression = descriptor.CronExpression,
                    MaxConcurrency = registry.Functions.TryGetValue(descriptor.FunctionName, out var function)
                        ? function.MaxConcurrency
                        : null
                };
            }).ToList();

            return Task.FromResult<IList<FunctionInfoDto>>(result);
        }

        // ============ Host ============

        public Task<HostStatusDto> GetHostStatusAsync(CancellationToken cancellationToken = default)
        {
            var dto = new HostStatusDto
            {
                IsRunning = _hostScheduler?.IsRunning ?? false,
                ActiveThreads = _taskScheduler?.ActiveWorkers ?? 0,
                MaxConcurrency = _schedulerOptions?.MaxConcurrency ?? 0,
                ActiveExecutions = _taskScheduler?.ActiveExecutionCount ?? 0,
                QueuedExecutions = _taskScheduler?.TotalQueuedTasks ?? 0,
                ActiveWorkflows = _taskScheduler?.ActiveOrchestrationCount ?? 0,
                WaitingForFunction = _taskScheduler?.WaitingForFunctionCount ?? 0,
                RetryingExecutions = _taskScheduler?.RetryingCount ?? 0
            };
            return Task.FromResult(dto);
        }

        public async Task<NextTickerDto> GetNextTickerAsync(CancellationToken cancellationToken = default)
        {
            var now = DateTime.UtcNow;

            var nextTime = await TimeMetadataQuery()
                .Where(x => x.ExecutionTime > now &&
                           (x.Status == TickerStatus.Idle || x.Status == TickerStatus.Queued))
                .OrderBy(x => x.ExecutionTime)
                .Take(1)
                .ToArrayAsync(cancellationToken).ConfigureAwait(false);

            var nextOcc = await OccurrenceMetadataQuery()
                .Where(x => x.ExecutionTime > now &&
                           (x.Status == TickerStatus.Idle || x.Status == TickerStatus.Queued))
                .OrderBy(x => x.ExecutionTime)
                .Take(1)
                .ToArrayAsync(cancellationToken).ConfigureAwait(false);

            var tt = nextTime.FirstOrDefault();
            var co = nextOcc.FirstOrDefault();

            if (tt == null && co == null) return new NextTickerDto();

            if (tt != null && (co == null || tt.ExecutionTime <= co.ExecutionTime))
            {
                return new NextTickerDto
                {
                    Id = tt.Id,
                    FunctionName = tt.Function,
                    ScheduledFor = tt.ExecutionTime,
                    Type = ExecutionType.TimeTicker
                };
            }

            var parent = co == null ? null : await ReadCronMetadataAsync(co.CronTickerId, cancellationToken).ConfigureAwait(false);
            return new NextTickerDto
            {
                Id = co.Id,
                FunctionName = parent?.Function,
                ScheduledFor = co.ExecutionTime,
                Type = ExecutionType.CronOccurrence
            };
        }

        // ============ Graphs ============

        public async Task<IList<GraphBucketDto>> GetTimeTickersGraphAsync(int pastDays, int futureDays, CancellationToken cancellationToken = default)
        {
            var today = DateTime.UtcNow.Date;
            var from = today.AddDays(-Math.Abs(pastDays));
            var to = today.AddDays(Math.Abs(futureDays)).AddDays(1);

            // Pull tickers that EITHER executed in the window, OR are scheduled
            // to fire in the window (the latter for the future-days lookahead).
            // Bucket by ExecutedAt when set so a ticker that was scheduled
            // weeks ago but ran today shows up on today's bar — matching the
            // chart's "Executions" label. Falls back to ExecutionTime for
            // future/Idle/Queued rows that haven't run yet.
            var tickers = await TimeMetadataQuery()
                .Where(x => (x.ExecutedAt != null && x.ExecutedAt >= from && x.ExecutedAt < to)
                            || (x.ExecutionTime != null && x.ExecutionTime >= from && x.ExecutionTime < to))
                .ToArrayAsync(cancellationToken).ConfigureAwait(false);

            return BuildBuckets(from, to, tickers
                .Select(x =>
                {
                    var bucketDate = (x.ExecutedAt ?? x.ExecutionTime ?? default(DateTime)).Date;
                    return (bucketDate, x.Status);
                })
                .Where(t => t.bucketDate >= from && t.bucketDate < to));
        }

        public async Task<IList<GraphBucketDto>> GetCronTickersGraphAsync(int pastDays, int futureDays, CancellationToken cancellationToken = default)
        {
            var today = DateTime.UtcNow.Date;
            var from = today.AddDays(-Math.Abs(pastDays));
            var to = today.AddDays(Math.Abs(futureDays)).AddDays(1);

            var occs = await OccurrenceMetadataQuery()
                .Where(x => x.ExecutionTime >= from && x.ExecutionTime < to)
                .ToArrayAsync(cancellationToken).ConfigureAwait(false);

            return BuildBuckets(from, to, occs.Select(x => (x.ExecutionTime.Date, x.Status)));
        }

        public async Task<IList<GraphBucketDto>> GetCronTickerGraphByIdAsync(Guid cronTickerId, int pastDays, int futureDays, CancellationToken cancellationToken = default)
        {
            var today = DateTime.UtcNow.Date;
            var from = today.AddDays(-Math.Abs(pastDays));
            var to = today.AddDays(Math.Abs(futureDays)).AddDays(1);

            var occs = await OccurrenceMetadataQuery()
                .Where(x => x.CronTickerId == cronTickerId && x.ExecutionTime >= from && x.ExecutionTime < to)
                .ToArrayAsync(cancellationToken).ConfigureAwait(false);

            return BuildBuckets(from, to, occs.Select(x => (x.ExecutionTime.Date, x.Status)));
        }

        public async Task<IList<GraphBucketDto>> GetCronOccurrencesGraphAsync(Guid cronTickerId, CancellationToken cancellationToken = default)
        {
            var occs = await OccurrenceMetadataQuery()
                .Where(x => x.CronTickerId == cronTickerId)
                .ToArrayAsync(cancellationToken).ConfigureAwait(false);

            if (occs.Length == 0) return new List<GraphBucketDto>();

            var from = occs.Min(x => x.ExecutionTime).Date;
            var to = occs.Max(x => x.ExecutionTime).Date.AddDays(1);
            return BuildBuckets(from, to, occs.Select(x => (x.ExecutionTime.Date, x.Status)));
        }

        // ============ Operations ============

        public async Task<bool> ToggleCronTickerAsync(Guid cronTickerId, bool isEnabled, CancellationToken cancellationToken = default)
        {
            var cron = await _persistence.GetCronTickerById(cronTickerId, cancellationToken).ConfigureAwait(false);
            if (cron == null) return false;

            // CronTickerEntity carries IsEnabled (default true). Flip it directly —
            // the polling loop reads this to decide whether to schedule the next
            // occurrence, so flipping is the actual enable/disable signal. The
            // earlier no-op stub only bumped UpdatedAt and the UI saw no change.
            cron.IsEnabled = isEnabled;
            cron.UpdatedAt = DateTime.UtcNow;
            await _persistence.UpdateCronTickers(new[] { cron }, cancellationToken).ConfigureAwait(false);

            if (_hostScheduler != null) _hostScheduler.Restart();
            return true;
        }

        public Task RunCronTickerOnDemandAsync(Guid cronTickerId, CancellationToken cancellationToken = default)
            => OnDemandCronExecution.RunAsync(_persistence, _dispatcher, _notificationHubSender,
                (_schedulerOptions?.RuntimePartition ?? TickerQRuntimePartition.LegacyGlobal).StorageKey,
                cronTickerId, cancellationToken, _logger, lifecycle: _lifecycle);

        public bool CancelTicker(Guid tickerId)
            => TickerCancellationTokenManager.RequestTickerCancellationById(RuntimePartitionKey, tickerId);

        private string RuntimePartitionKey =>
            (_schedulerOptions?.RuntimePartition ?? TickerQRuntimePartition.LegacyGlobal).StorageKey;

        public async Task<bool> RequestRunningTickerCancellationAsync(
            Guid tickerId, TickerType type, CancellationToken cancellationToken = default)
        {
            Guid? token;
            if (type == TickerType.TimeTicker)
            {
                var ticker = await _persistence.GetTimeTickerById(tickerId, cancellationToken).ConfigureAwait(false);
                if (ticker?.Status != TickerStatus.InProgress) return false;
                token = ticker.ParentId.HasValue ? ticker.ChainGeneration : ticker.AcquisitionToken;
            }
            else if (type == TickerType.CronTickerOccurrence)
            {
                var occurrence = await OccurrenceMetadataQuery()
                    .Where(x => x.Id == tickerId).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
                if (occurrence?.Status != TickerStatus.InProgress) return false;
                token = occurrence.AcquisitionToken;
            }
            else return false;

            return token.HasValue && TickerCancellationTokenManager.RequestTickerCancellation(
                RuntimePartitionKey, new TickerExecutionLease(type, tickerId, token));
        }

        public async Task StartHostAsync(CancellationToken cancellationToken = default)
        {
            if (_hostScheduler != null)
                await _hostScheduler.StartAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task StopHostAsync(CancellationToken cancellationToken = default)
        {
            if (_hostScheduler != null)
                await _hostScheduler.StopAsync(cancellationToken).ConfigureAwait(false);
        }

        public void RestartHost()
        {
            _hostScheduler?.Restart();
        }

        private static IList<GraphBucketDto> BuildBuckets(
            DateTime from, DateTime to, IEnumerable<(DateTime date, TickerStatus status)> points)
        {
            var allStatuses = Enum.GetValues<TickerStatus>();
            var grouped = points.GroupBy(p => p.date)
                .ToDictionary(g => g.Key, g => g.GroupBy(x => x.status).ToDictionary(sg => sg.Key, sg => sg.Count()));

            var buckets = new List<GraphBucketDto>();
            for (var date = from; date < to; date = date.AddDays(1))
            {
                grouped.TryGetValue(date, out var statusCounts);
                buckets.Add(new GraphBucketDto
                {
                    Date = date,
                    Counts = allStatuses
                        .Select(s => new GraphBucketCountDto
                        {
                            Status = s,
                            Count = statusCounts != null && statusCounts.TryGetValue(s, out var c) ? c : 0,
                        })
                        .ToArray()
                });
            }
            return buckets;
        }
    }
}
