using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using TickerQ.Dashboard.Infrastructure;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ.Dashboard.Hubs
{
    /// <summary>
    /// Pushes "something changed" hints (id-only) over SignalR. Clients use the
    /// hint to invalidate their React Query cache and refetch via REST — the
    /// authoritative shape. We never broadcast full entity bytes here: lists are
    /// server-paginated/sorted, so clients can't apply a pushed row locally
    /// without re-running the same query anyway.
    /// </summary>
    internal class TickerQNotificationHubSender : ITickerQNotificationHubSender
    {
        private readonly IHubContext<TickerQNotificationHub> _hubContext;
        private readonly Timer _timeTickerUpdateTimer;
        private int _hasPendingTimeTickerUpdate;
        private static readonly TimeSpan TimeTickerUpdateDebounce = TimeSpan.FromMilliseconds(100);

        public TickerQNotificationHubSender(IHubContext<TickerQNotificationHub> hubContext)
        {
            _hubContext = hubContext ?? throw new ArgumentNullException(nameof(hubContext));
            _timeTickerUpdateTimer = new Timer(TimeTickerUpdateCallback, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }

        public Task AddCronTickerNotifyAsync(Guid id)
            => _hubContext.Clients.All.SendAsync("AddCronTickerNotification", id);

        public Task UpdateCronTickerNotifyAsync(Guid id)
            => _hubContext.Clients.All.SendAsync("UpdateCronTickerNotification", id);

        public Task RemoveCronTickerNotifyAsync(Guid id)
            => _hubContext.Clients.All.SendAsync("RemoveCronTickerNotification", id);

        public Task AddTimeTickerNotifyAsync(Guid id)
            => _hubContext.Clients.All.SendAsync("AddTimeTickerNotification", id);

        public Task AddTimeTickersBatchNotifyAsync()
            => _hubContext.Clients.All.SendAsync("AddTimeTickersBatchNotification");

        public Task UpdateTimeTickerNotifyAsync(Guid id)
            => _hubContext.Clients.All.SendAsync("UpdateTimeTickerNotification", id);

        public Task RemoveTimeTickerNotifyAsync(Guid id)
            => _hubContext.Clients.All.SendAsync("RemoveTimeTickerNotification", id);

        public Task CanceledTickerNotifyAsync(Guid id)
            => _hubContext.Clients.All.SendAsync("CanceledTickerNotification", id);

        public Task AddCronOccurrenceAsync(Guid groupId, Guid occurrenceId)
            => _hubContext.Clients.Group(groupId.ToString()).SendAsync("AddCronOccurrenceNotification", occurrenceId);

        public Task UpdateCronOccurrenceAsync(Guid groupId, Guid occurrenceId)
            => _hubContext.Clients.Group(groupId.ToString()).SendAsync("UpdateCronOccurrenceNotification", occurrenceId);

        public void UpdateActiveThreads(string activeThreads)
        {
            var json = JsonSerializer.SerializeToElement(activeThreads, DashboardJsonSerializerContext.Default.String);
            _ = _hubContext.Clients.All.SendAsync("GetActiveThreadsNotification", json);
        }

        public void UpdateNextOccurrence(DateTime? nextOccurrence)
        {
            if (nextOccurrence != null)
            {
                var json = JsonSerializer.SerializeToElement(nextOccurrence, DashboardJsonSerializerContext.Default.NullableDateTime);
                _ = _hubContext.Clients.All.SendAsync("GetNextOccurrenceNotification", json);
            }
        }

        public void UpdateHostStatus(bool active)
        {
            var json = JsonSerializer.SerializeToElement(active, DashboardJsonSerializerContext.Default.Boolean);
            _ = _hubContext.Clients.All.SendAsync("GetHostStatusNotification", json);
        }

        public void UpdateHostException(string exceptionMessage)
        {
            var json = JsonSerializer.SerializeToElement(exceptionMessage, DashboardJsonSerializerContext.Default.String);
            _ = _hubContext.Clients.All.SendAsync("UpdateHostExceptionNotification", json);
        }

        public Task UpdateNodeHeartBeatAsync(JsonElement nodeHeartBeat)
            => _hubContext.Clients.All.SendAsync("UpdateNodeHeartBeat", nodeHeartBeat);

        public Task UpdateTimeTickerFromInternalFunctionContext<TTimeTicker>(InternalFunctionContext internalFunctionContext)
            where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        {
            // Debounce high-frequency status transitions (queued → in-progress →
            // done) into one zero-payload broadcast every 100ms. The dashboard
            // refetches the whole list once instead of being hammered.
            if (Interlocked.Exchange(ref _hasPendingTimeTickerUpdate, 1) == 0)
            {
                _timeTickerUpdateTimer.Change(TimeTickerUpdateDebounce, Timeout.InfiniteTimeSpan);
            }

            return Task.CompletedTask;
        }

        private void TimeTickerUpdateCallback(object? _)
        {
            if (Interlocked.Exchange(ref _hasPendingTimeTickerUpdate, 0) == 0)
                return;

            _ = _hubContext.Clients.All.SendAsync("UpdateTimeTickerNotification");
        }

        public Task UpdateCronOccurrenceFromInternalFunctionContext<TCronTicker>(InternalFunctionContext internalFunctionContext)
            where TCronTicker : CronTickerEntity, new()
        {
            // Status-transition hint for a cron occurrence. Group-scoped — only
            // the cron-detail page subscribed via JoinGroup(cronTickerId) sees
            // it. Skip silently if we don't know which cron this belongs to:
            // there are no subscribers to "" and the dashboard polling
            // fallbacks will catch it.
            if (internalFunctionContext.ParentId is not { } parentId)
                return Task.CompletedTask;

            return _hubContext.Clients
                .Group(parentId.ToString())
                .SendAsync("UpdateCronOccurrenceNotification", internalFunctionContext.TickerId);
        }
    }
}
