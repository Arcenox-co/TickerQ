using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
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
    internal sealed class TickerQNotificationHubSender : ITickerQNotificationHubSender, IDisposable
    {
        private readonly IHubContext<TickerQNotificationHub> _hubContext;
        private readonly ILogger<TickerQNotificationHubSender> _logger;
        private readonly Timer _timeTickerUpdateTimer;
        private int _hasPendingTimeTickerUpdate;
        private int _disposed;
        private int _deliveryFailureLogged;
        private static readonly TimeSpan TimeTickerUpdateDebounce = TimeSpan.FromMilliseconds(100);

        public TickerQNotificationHubSender(
            IHubContext<TickerQNotificationHub> hubContext,
            ILogger<TickerQNotificationHubSender> logger)
        {
            _hubContext = hubContext ?? throw new ArgumentNullException(nameof(hubContext));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _timeTickerUpdateTimer = new Timer(TimeTickerUpdateCallback, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }

        public Task AddCronTickerNotifyAsync(Guid id)
            => SendSafelyAsync(() => _hubContext.Clients.All.SendAsync("AddCronTickerNotification", id));

        public Task UpdateCronTickerNotifyAsync(Guid id)
            => SendSafelyAsync(() => _hubContext.Clients.All.SendAsync("UpdateCronTickerNotification", id));

        public Task RemoveCronTickerNotifyAsync(Guid id)
            => SendSafelyAsync(() => _hubContext.Clients.All.SendAsync("RemoveCronTickerNotification", id));

        public Task AddTimeTickerNotifyAsync(Guid id)
            => SendSafelyAsync(() => _hubContext.Clients.All.SendAsync("AddTimeTickerNotification", id));

        public Task AddTimeTickersBatchNotifyAsync()
            => SendSafelyAsync(() => _hubContext.Clients.All.SendAsync("AddTimeTickersBatchNotification"));

        public Task UpdateTimeTickerNotifyAsync(Guid id)
            => SendSafelyAsync(() => _hubContext.Clients.All.SendAsync("UpdateTimeTickerNotification", id));

        public Task RemoveTimeTickerNotifyAsync(Guid id)
            => SendSafelyAsync(() => _hubContext.Clients.All.SendAsync("RemoveTimeTickerNotification", id));

        public Task CanceledTickerNotifyAsync(Guid id)
            => SendSafelyAsync(() => _hubContext.Clients.All.SendAsync("CanceledTickerNotification", id));

        public Task AddCronOccurrenceAsync(Guid groupId, Guid occurrenceId)
            => SendSafelyAsync(() => _hubContext.Clients.Group(groupId.ToString("D"))
                .SendAsync("AddCronOccurrenceNotification", occurrenceId));

        public Task UpdateCronOccurrenceAsync(Guid groupId, Guid occurrenceId)
            => SendSafelyAsync(() => _hubContext.Clients.Group(groupId.ToString("D"))
                .SendAsync("UpdateCronOccurrenceNotification", occurrenceId));

        public void UpdateActiveThreads(string activeThreads)
        {
            var json = JsonSerializer.SerializeToElement(activeThreads, DashboardJsonSerializerContext.Default.String);
            _ = SendSafelyAsync(() => _hubContext.Clients.All.SendAsync("GetActiveThreadsNotification", json));
        }

        public void UpdateNextOccurrence(DateTime? nextOccurrence)
        {
            // Null is a meaningful transition: it clears a previously visible
            // next occurrence after the last pending job is removed.
            var json = JsonSerializer.SerializeToElement(nextOccurrence, DashboardJsonSerializerContext.Default.NullableDateTime);
            _ = SendSafelyAsync(() => _hubContext.Clients.All.SendAsync("GetNextOccurrenceNotification", json));
        }

        public void UpdateHostStatus(bool active)
        {
            var json = JsonSerializer.SerializeToElement(active, DashboardJsonSerializerContext.Default.Boolean);
            _ = SendSafelyAsync(() => _hubContext.Clients.All.SendAsync("GetHostStatusNotification", json));
        }

        public void UpdateHostException(string exceptionMessage)
        {
            var json = JsonSerializer.SerializeToElement(exceptionMessage, DashboardJsonSerializerContext.Default.String);
            _ = SendSafelyAsync(() => _hubContext.Clients.All.SendAsync("UpdateHostExceptionNotification", json));
        }

        public Task UpdateNodeHeartBeatAsync(JsonElement nodeHeartBeat)
            => SendSafelyAsync(() => _hubContext.Clients.All.SendAsync("UpdateNodeHeartBeat", nodeHeartBeat));

        public Task UpdateTimeTickerFromInternalFunctionContext<TTimeTicker>(InternalFunctionContext internalFunctionContext)
            where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        {
            // Debounce high-frequency status transitions (queued → in-progress →
            // done) into one zero-payload broadcast every 100ms. The dashboard
            // refetches the whole list once instead of being hammered.
            if (Volatile.Read(ref _disposed) == 0 &&
                Interlocked.Exchange(ref _hasPendingTimeTickerUpdate, 1) == 0)
            {
                try
                {
                    _timeTickerUpdateTimer.Change(TimeTickerUpdateDebounce, Timeout.InfiniteTimeSpan);
                }
                catch (ObjectDisposedException) when (Volatile.Read(ref _disposed) != 0)
                {
                    // Host shutdown raced with a final execution update.
                }
            }

            return Task.CompletedTask;
        }

        private void TimeTickerUpdateCallback(object? _)
        {
            if (Interlocked.Exchange(ref _hasPendingTimeTickerUpdate, 0) == 0)
                return;

            if (Volatile.Read(ref _disposed) == 0)
                _ = SendSafelyAsync(() => _hubContext.Clients.All.SendAsync("UpdateTimeTickerNotification"));
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

            return SendSafelyAsync(() => _hubContext.Clients
                .Group(parentId.ToString("D"))
                .SendAsync("UpdateCronOccurrenceNotification", internalFunctionContext.TickerId));
        }

        private Task SendSafelyAsync(Func<Task> send)
        {
            if (Volatile.Read(ref _disposed) != 0)
                return Task.CompletedTask;

            return SendSafelyCoreAsync(send);
        }

        private async Task SendSafelyCoreAsync(Func<Task> send)
        {
            try
            {
                await send().ConfigureAwait(false);
                Volatile.Write(ref _deliveryFailureLogged, 0);
            }
            catch (Exception exception)
            {
                // Dashboard notifications are cache-invalidation hints. The
                // REST API remains authoritative and the SPA has a periodic
                // consistency refresh, so a transport outage must not crash a
                // scheduler callback or become an unobserved task exception.
                if (Interlocked.Exchange(ref _deliveryFailureLogged, 1) == 0)
                {
                    _logger.LogWarning(exception,
                        "TickerQ dashboard SignalR notifications are unavailable. " +
                        "Scheduler work continues and dashboard clients use their consistency refresh.");
                }
                else
                {
                    _logger.LogDebug(exception, "TickerQ dashboard SignalR notification could not be delivered.");
                }
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            Interlocked.Exchange(ref _hasPendingTimeTickerUpdate, 0);
            _timeTickerUpdateTimer.Dispose();
        }
    }
}
