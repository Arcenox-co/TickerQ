using System;
using System.Text.Json;
using System.Threading.Tasks;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Models;

namespace TickerQ.Utilities.Interfaces
{
    /// <summary>
    /// Broadcasts lightweight "something changed" hints to dashboard clients
    /// over SignalR. Hints carry only ids — clients refetch the authoritative
    /// shape over REST. This keeps a single source of truth (the REST
    /// projection) and avoids broadcasting entity bytes that paginated /
    /// sorted list views would discard anyway.
    /// </summary>
    internal interface ITickerQNotificationHubSender
    {
        Task AddCronTickerNotifyAsync(Guid id);
        Task UpdateCronTickerNotifyAsync(Guid id);
        Task RemoveCronTickerNotifyAsync(Guid id);
        Task AddTimeTickerNotifyAsync(Guid id);
        Task AddTimeTickersBatchNotifyAsync();
        Task UpdateTimeTickerNotifyAsync(Guid id);
        Task RemoveTimeTickerNotifyAsync(Guid id);
        Task CanceledTickerNotifyAsync(Guid id);
        Task AddCronOccurrenceAsync(Guid groupId, Guid occurrenceId);
        Task UpdateCronOccurrenceAsync(Guid groupId, Guid occurrenceId);
        Task UpdateTimeTickerFromInternalFunctionContext<TTimeTickerEntity>(InternalFunctionContext internalFunctionContext) where TTimeTickerEntity : TimeTickerEntity<TTimeTickerEntity>, new();
        Task UpdateCronOccurrenceFromInternalFunctionContext<TCronTickerEntity>(InternalFunctionContext internalFunctionContext) where TCronTickerEntity : CronTickerEntity, new();
        void UpdateActiveThreads(string activeThreads);
        void UpdateNextOccurrence(DateTime? nextOccurrence);
        void UpdateHostStatus(bool active);
        void UpdateHostException(string exceptionMessage);
        Task UpdateNodeHeartBeatAsync(JsonElement nodeHeartBeat);
    }
}
