using System;
using System.Threading.Tasks;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Models;

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
        void UpdateActiveThreads(object activeThreads);
        void UpdateNextOccurrence(object nextOccurrence);
        void UpdateHostStatus(object active);
        void UpdateHostException(object exceptionMessage);
        Task AddCronOccurrenceAsync(Guid groupId, object occurrence);
        Task UpdateCronOccurrenceAsync(Guid groupId, object occurrence);
        Task UpdateTimeTickerFromInternalFunctionContext<TTimeTickerEntity>(InternalFunctionContext internalFunctionContext) where TTimeTickerEntity : TimeTickerEntity, new();
        Task UpdateCronOccurrenceFromInternalFunctionContext<TCronTickerEntity>(InternalFunctionContext internalFunctionContext) where TCronTickerEntity : CronTickerEntity, new();
        Task CanceledTickerNotifyAsync(Guid id);
    }
}