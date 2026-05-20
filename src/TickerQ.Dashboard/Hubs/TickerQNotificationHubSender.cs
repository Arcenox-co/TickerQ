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

        public async Task AddCronTickerNotifyAsync(object cronTicker)
        {
            var json = JsonSerializer.SerializeToElement((CronTickerEntity)cronTicker, DashboardJsonSerializerContext.Default.CronTickerEntity);
            await _hubContext.Clients.All.SendAsync("AddCronTickerNotification", json);
        }

        public async Task UpdateCronTickerNotifyAsync(object cronTicker)
        {
            var json = JsonSerializer.SerializeToElement((CronTickerEntity)cronTicker, DashboardJsonSerializerContext.Default.CronTickerEntity);
            await _hubContext.Clients.All.SendAsync("UpdateCronTickerNotification", json);
        }

        public async Task RemoveCronTickerNotifyAsync(Guid id)
        {
            await _hubContext.Clients.All.SendAsync("RemoveCronTickerNotification", id);
        }

        public async Task AddTimeTickerNotifyAsync(Guid id)
        {
            await _hubContext.Clients.All.SendAsync("AddTimeTickerNotification", id);
        }
        
        public async Task AddTimeTickersBatchNotifyAsync()
        {
            await _hubContext.Clients.All.SendAsync("AddTimeTickersBatchNotification");
        }

        public async Task UpdateTimeTickerNotifyAsync(object timeTicker)
        {
            var json = JsonSerializer.SerializeToElement((TimeTickerEntity)timeTicker, DashboardJsonSerializerContext.Default.TimeTickerEntity);
            await _hubContext.Clients.All.SendAsync("UpdateTimeTickerNotification", json);
        }

        public async Task RemoveTimeTickerNotifyAsync(Guid id)
        {
            await _hubContext.Clients.All.SendAsync("RemoveTimeTickerNotification", id);
        }

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

        public async Task UpdateNodeHeartBeatAsync(JsonElement nodeHeartBeat)
        {
            await _hubContext.Clients.All.SendAsync("UpdateNodeHeartBeat", nodeHeartBeat);
        }

        public async Task AddCronOccurrenceAsync(Guid groupId, object occurrence)
        {
            var json = JsonSerializer.SerializeToElement((CronTickerOccurrenceEntity<CronTickerEntity>)occurrence, DashboardJsonSerializerContext.Default.CronTickerOccurrenceEntityCronTickerEntity);
            await _hubContext.Clients.Group(groupId.ToString()).SendAsync("AddCronOccurrenceNotification", json);
        }

        public async Task UpdateCronOccurrenceAsync(Guid groupId, object occurrence)
        {
            var json = JsonSerializer.SerializeToElement((CronTickerOccurrenceEntity<CronTickerEntity>)occurrence, DashboardJsonSerializerContext.Default.CronTickerOccurrenceEntityCronTickerEntity);
            await _hubContext.Clients.Group(groupId.ToString()).SendAsync("UpdateCronOccurrenceNotification", json);
        }

        public Task UpdateTimeTickerFromInternalFunctionContext<TTimeTicker>(InternalFunctionContext internalFunctionContext)
            where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        {
            // Debounce high-frequency updates into a single notification
            if (Interlocked.Exchange(ref _hasPendingTimeTickerUpdate, 1) == 0)
            {
                _timeTickerUpdateTimer.Change(TimeTickerUpdateDebounce, Timeout.InfiniteTimeSpan);
            }

            return Task.CompletedTask;
        }

        private void TimeTickerUpdateCallback(object _)
        {
            if (Interlocked.Exchange(ref _hasPendingTimeTickerUpdate, 0) == 0)
                return;

            _ = _hubContext.Clients.All.SendAsync("UpdateTimeTickerNotification");
        }

        public Task UpdateCronOccurrenceFromInternalFunctionContext<TCronTicker>(InternalFunctionContext internalFunctionContext)
            where TCronTicker : CronTickerEntity, new()
        {
            var updatePayload = new CronOccurrenceUpdateNotification
            {
                Id = internalFunctionContext.TickerId,
                Status = internalFunctionContext.Status,
                CronTickerId = internalFunctionContext.ParentId,
                ExecutedAt = internalFunctionContext.ExecutedAt,
                ElapsedTime = internalFunctionContext.ElapsedTime,
                RetryCount = internalFunctionContext.RetryCount,
                ExceptionMessage = internalFunctionContext.ExceptionDetails
            };

            var json = JsonSerializer.SerializeToElement(updatePayload, DashboardJsonSerializerContext.Default.CronOccurrenceUpdateNotification);
            _ = _hubContext.Clients
                .Group(internalFunctionContext.ParentId?.ToString() ?? string.Empty)
                .SendAsync("UpdateCronOccurrenceNotification", json);

            return Task.CompletedTask;
        }

        public async Task CanceledTickerNotifyAsync(Guid id)
        {
            await _hubContext.Clients.All.SendAsync("CanceledTickerNotification", id);
        }

        // -------------------------------------------------------------------
        // Periodic ticker notifications. Mirror the cron flow so that the
        // dashboard UI consumes a single, consistent stream of ticker events.
        // -------------------------------------------------------------------

        public async Task AddPeriodicTickerNotifyAsync(object periodicTicker)
        {
            // Use object-typed JsonSerializer overload because PeriodicTickerEntity is the
            // base type and consumers may pass subclasses; the AOT context covers the base.
            var json = JsonSerializer.SerializeToElement((PeriodicTickerEntity)periodicTicker, DashboardJsonSerializerContext.Default.PeriodicTickerEntity);
            await _hubContext.Clients.All.SendAsync("AddPeriodicTickerNotification", json);
        }

        public async Task UpdatePeriodicTickerNotifyAsync(object periodicTicker)
        {
            var json = JsonSerializer.SerializeToElement((PeriodicTickerEntity)periodicTicker, DashboardJsonSerializerContext.Default.PeriodicTickerEntity);
            await _hubContext.Clients.All.SendAsync("UpdatePeriodicTickerNotification", json);
        }

        public async Task RemovePeriodicTickerNotifyAsync(Guid id)
        {
            await _hubContext.Clients.All.SendAsync("RemovePeriodicTickerNotification", id);
        }

        public async Task AddPeriodicOccurrenceAsync(Guid groupId, object occurrence)
        {
            var json = JsonSerializer.SerializeToElement(
                (PeriodicTickerOccurrenceEntity<PeriodicTickerEntity>)occurrence,
                DashboardJsonSerializerContext.Default.PeriodicTickerOccurrenceEntityPeriodicTickerEntity);
            await _hubContext.Clients.Group(groupId.ToString()).SendAsync("AddPeriodicOccurrenceNotification", json);
        }

        public async Task UpdatePeriodicOccurrenceAsync(Guid groupId, object occurrence)
        {
            var json = JsonSerializer.SerializeToElement(
                (PeriodicTickerOccurrenceEntity<PeriodicTickerEntity>)occurrence,
                DashboardJsonSerializerContext.Default.PeriodicTickerOccurrenceEntityPeriodicTickerEntity);
            await _hubContext.Clients.Group(groupId.ToString()).SendAsync("UpdatePeriodicOccurrenceNotification", json);
        }

        public Task UpdatePeriodicOccurrenceFromInternalFunctionContext<TPeriodicTicker>(InternalFunctionContext internalFunctionContext)
            where TPeriodicTicker : PeriodicTickerEntity, new()
        {
            var payload = new PeriodicOccurrenceUpdateNotification
            {
                Id = internalFunctionContext.TickerId,
                Status = internalFunctionContext.Status,
                PeriodicTickerId = internalFunctionContext.ParentId,
                ExecutedAt = internalFunctionContext.ExecutedAt,
                ElapsedTime = internalFunctionContext.ElapsedTime,
                RetryCount = internalFunctionContext.RetryCount,
                ExceptionMessage = internalFunctionContext.ExceptionDetails
            };

            var json = JsonSerializer.SerializeToElement(payload, DashboardJsonSerializerContext.Default.PeriodicOccurrenceUpdateNotification);
            _ = _hubContext.Clients
                .Group(internalFunctionContext.ParentId?.ToString() ?? string.Empty)
                .SendAsync("UpdatePeriodicOccurrenceNotification", json);

            return Task.CompletedTask;
        }
    }
}
