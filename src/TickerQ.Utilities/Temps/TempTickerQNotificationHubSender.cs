using System;
using System.Threading.Tasks;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ.Utilities.Temps
{
    internal class TempTickerQNotificationHubSender : ITickerQNotificationHubSender
    {
        public Task AddCronTickerNotifyAsync(object cronTicker)
        {
            return Task.CompletedTask;
        }

        public Task UpdateCronTickerNotifyAsync(object cronTicker)
        {
            return Task.CompletedTask;
        }

        public Task RemoveCronTickerNotifyAsync(Guid id)
        {
            return Task.CompletedTask;
        }

        public Task AddTimeTickerNotifyAsync(object timeTicker)
        {
            return Task.CompletedTask;
        }

        public Task UpdateTimeTickerNotifyAsync(object timeTicker)
        {
            return Task.CompletedTask;
        }

        public Task RemoveTimeTickerNotifyAsync(Guid id)
        {
            return Task.CompletedTask;
        }

        public void UpdateActiveThreads(object activeThreads)
        {
        }

        public void UpdateNextOccurrence(object nextOccurrence)
        {
        }

        public void UpdateHostStatus(object active)
        {
        }

        public void UpdateHostException(object exceptionMessage)
        {
        }

        public Task AddCronOccurrenceAsync(Guid groupId, object occurrence)
        { 
            return Task.CompletedTask;
        }

        public Task UpdateCronOccurrenceAsync(Guid groupId, object occurrence)
        {
            return Task.CompletedTask;
        }

        public Task UpdateTimeTickerFromInternalFunctionContext<TTimeTickerEntity>(InternalFunctionContext internalFunctionContext) where TTimeTickerEntity : TimeTickerEntity<TTimeTickerEntity>, new()
        {
            return Task.CompletedTask;
        }

        public Task UpdateCronOccurrenceFromInternalFunctionContext<TCronTickerEntity>(
            InternalFunctionContext internalFunctionContext) where TCronTickerEntity : CronTickerEntity, new()
        {
            return Task.CompletedTask;
        }

        public Task CanceledTickerNotifyAsync(Guid id)
        {
            return Task.CompletedTask;
        }
    }
}