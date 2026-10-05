using System;
using System.Text.Json;
using System.Threading.Tasks;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ.Utilities.Temps
{
    internal class NoOpTickerQNotificationHubSender : ITickerQNotificationHubSender
    {
        public Task AddCronTickerNotifyAsync(Guid id) => Task.CompletedTask;
        public Task UpdateCronTickerNotifyAsync(Guid id) => Task.CompletedTask;
        public Task RemoveCronTickerNotifyAsync(Guid id) => Task.CompletedTask;
        public Task AddTimeTickerNotifyAsync(Guid id) => Task.CompletedTask;
        public Task AddTimeTickersBatchNotifyAsync() => Task.CompletedTask;
        public Task UpdateTimeTickerNotifyAsync(Guid id) => Task.CompletedTask;
        public Task RemoveTimeTickerNotifyAsync(Guid id) => Task.CompletedTask;
        public Task CanceledTickerNotifyAsync(Guid id) => Task.CompletedTask;
        public Task AddCronOccurrenceAsync(Guid groupId, Guid occurrenceId) => Task.CompletedTask;
        public Task UpdateCronOccurrenceAsync(Guid groupId, Guid occurrenceId) => Task.CompletedTask;

        public Task UpdateTimeTickerFromInternalFunctionContext<TTimeTickerEntity>(InternalFunctionContext internalFunctionContext)
            where TTimeTickerEntity : TimeTickerEntity<TTimeTickerEntity>, new() => Task.CompletedTask;

        public Task UpdateCronOccurrenceFromInternalFunctionContext<TCronTickerEntity>(InternalFunctionContext internalFunctionContext)
            where TCronTickerEntity : CronTickerEntity, new() => Task.CompletedTask;

        public void UpdateActiveThreads(string activeThreads) { }
        public void UpdateNextOccurrence(DateTime? nextOccurrence) { }
        public void UpdateHostStatus(bool active) { }
        public void UpdateHostException(string exceptionMessage) { }
        public Task UpdateNodeHeartBeatAsync(JsonElement nodeHeartBeat) => Task.CompletedTask;
    }
}
