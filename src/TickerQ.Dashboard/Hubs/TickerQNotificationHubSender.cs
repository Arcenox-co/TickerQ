using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
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
            await _hubContext.Clients.All.SendAsync("AddCronTickerNotification", cronTicker);
        }

        public async Task UpdateCronTickerNotifyAsync(object cronTicker)
        {
            await _hubContext.Clients.All.SendAsync("UpdateCronTickerNotification", cronTicker);
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
            await _hubContext.Clients.All.SendAsync("UpdateTimeTickerNotification", timeTicker);
        }

        public async Task RemoveTimeTickerNotifyAsync(Guid id)
        {
            await _hubContext.Clients.All.SendAsync("RemoveTimeTickerNotification", id);
        }

        public void UpdateActiveThreads(object activeThreads)
        {
            _ = _hubContext.Clients.All.SendAsync("GetActiveThreadsNotification", activeThreads);
        }

        public void UpdateNextOccurrence(object nextOccurrence)
        {
            if(nextOccurrence != null)
                _ = _hubContext.Clients.All.SendAsync("GetNextOccurrenceNotification", nextOccurrence);
        }

        public void UpdateHostStatus(object active)
        {
            _ = _hubContext.Clients.All.SendAsync("GetHostStatusNotification", active);
        }

        public void UpdateHostException(object exceptionMessage)
        {
            _ = _hubContext.Clients.All.SendAsync("UpdateHostExceptionNotification", exceptionMessage);
        }

        public async Task UpdateNodeHeartBeatAsync(object nodeHeartBeat)
        {
            await _hubContext.Clients.All.SendAsync("UpdateNodeHeartBeat", nodeHeartBeat);
        }

        public async Task AddCronOccurrenceAsync(Guid groupId, object occurrence)
        {
            await _hubContext.Clients.Group(groupId.ToString()).SendAsync("AddCronOccurrenceNotification", occurrence);
        }

        public async Task UpdateCronOccurrenceAsync(Guid groupId, object occurrence)
        {
            await _hubContext.Clients.Group(groupId.ToString()).SendAsync("UpdateCronOccurrenceNotification", occurrence);
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
            var updatePayload = new
            {
                id = internalFunctionContext.TickerId,
                status = internalFunctionContext.Status,
                cronTickerId = internalFunctionContext.ParentId,
                executedAt = internalFunctionContext.ExecutedAt,
                elapsedTime = internalFunctionContext.ElapsedTime,
                retryCount = internalFunctionContext.RetryCount,
                exceptionMessage = internalFunctionContext.ExceptionDetails
            };

            _ = _hubContext.Clients
                .Group(internalFunctionContext.ParentId?.ToString() ?? string.Empty)
                .SendAsync("UpdateCronOccurrenceNotification", updatePayload);

            return Task.CompletedTask;
        }

        public async Task CanceledTickerNotifyAsync(Guid id)
        {
            await _hubContext.Clients.All.SendAsync("CanceledTickerNotification", id);
        }
    }
}
