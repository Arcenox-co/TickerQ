using System;
using System.Threading.Tasks;
using TickerQ.Utilities.Interfaces;

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

        public void UpdateActiveThreads(int activeThreads)
        {
        }

        public void UpdateNextOccurrence(DateTime? nextOccurrence)
        {
        }

        public void UpdateHostStatus(bool active)
        {
        }

        public void UpdateHostException(string exceptionMessage)
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

        public Task CanceledTickerNotifyAsync(Guid id)
        {
            return Task.CompletedTask;
        }
    }
}