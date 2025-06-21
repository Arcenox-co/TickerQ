using System;
using System.Threading.Tasks;

namespace TickerQ.Utilities.Interfaces
{
    internal interface ITickerQNotificationHubSender
    {
        Task AddCronTickerNotifyAsync(object cronTicker);
        Task UpdateCronTickerNotifyAsync(object cronTicker);
        Task RemoveCronTickerNotifyAsync(Guid id);
        Task AddTimeTickerNotifyAsync(object timeTicker);
        Task UpdateTimeTickerNotifyAsync(object timeTicker);
        Task RemoveTimeTickerNotifyAsync(Guid id);
        void UpdateActiveThreads(int activeThreads);
        void UpdateNextOccurrence(DateTime? nextOccurrence);
        void UpdateHostStatus(bool active);
        void UpdateHostException(string exceptionMessage);
        Task AddCronOccurrenceAsync(Guid groupId, object occurrence);
        Task UpdateCronOccurrenceAsync(Guid groupId, object occurrence);
        Task CanceledTickerNotifyAsync(Guid id);
    }
}