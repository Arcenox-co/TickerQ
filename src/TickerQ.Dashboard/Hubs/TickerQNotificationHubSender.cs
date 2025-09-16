using System;
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
        
        public TickerQNotificationHubSender(IHubContext<TickerQNotificationHub> hubContext)
        {
            _hubContext = hubContext ?? throw new ArgumentNullException(nameof(hubContext));
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

        public async Task AddTimeTickerNotifyAsync(object timeTicker)
        {
            await _hubContext.Clients.All.SendAsync("AddTimeTickerNotification", timeTicker);
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

        public async Task AddCronOccurrenceAsync(Guid groupId, object occurrence)
        {
            await _hubContext.Clients.Group(groupId.ToString()).SendAsync("AddCronOccurrenceNotification", occurrence);
        }

        public async Task UpdateCronOccurrenceAsync(Guid groupId, object occurrence)
        {
            await _hubContext.Clients.Group(groupId.ToString()).SendAsync("UpdateCronOccurrenceNotification", occurrence);
        }

        public async Task UpdateTimeTickerFromInternalFunctionContext<TTimeTicker>(InternalFunctionContext internalFunctionContext) where TTimeTicker : TimeTickerEntity, new()
        {
            var timeTicker = new TTimeTicker
            {
                Id = internalFunctionContext.TickerId,
                Status = internalFunctionContext.Status,
                ExecutedAt = internalFunctionContext.ExecutedAt,
                Exception = internalFunctionContext.ExceptionDetails,
                ElapsedTime = internalFunctionContext.ElapsedTime,
                RetryCount = internalFunctionContext.RetryCount,
                UpdatedAt = internalFunctionContext.ExecutedAt
            };
            
            await _hubContext.Clients.All.SendAsync("UpdateTimeTickerNotification", timeTicker);
        }

        public async Task UpdateCronOccurrenceFromInternalFunctionContext<TCronTicker>(InternalFunctionContext internalFunctionContext) where TCronTicker : CronTickerEntity, new()
        {
            var cronOccurrence = new CronTickerOccurrenceEntity<TCronTicker>
            {
                Id = internalFunctionContext.TickerId,
                Status = internalFunctionContext.Status,
                CronTickerId = internalFunctionContext.CronTickerId,
                ExecutedAt = internalFunctionContext.ExecutedAt,
                Exception = internalFunctionContext.ExceptionDetails,
                ElapsedTime = internalFunctionContext.ElapsedTime,
                RetryCount = internalFunctionContext.RetryCount,
                UpdatedAt = internalFunctionContext.ExecutedAt
            };
            
            await _hubContext.Clients.Group(internalFunctionContext.CronTickerId.ToString()).SendAsync("UpdateCronOccurrenceNotification", cronOccurrence);
        }

        public async Task CanceledTickerNotifyAsync(Guid id)
        {
            await _hubContext.Clients.All.SendAsync("CanceledTickerNotification", id);
        }
    }
}