using Microsoft.Extensions.DependencyInjection;
using NCrontab;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;

namespace TickerQ
{
    internal static class TickerHelper
    {
        public static (TimeSpan, (string FunctionName, Guid TickerId, TickerType type)[]) GetNearestMemoryCronExpressions(IReadOnlyDictionary<string, string> memoryCronExpressions, DateTime now)
        {
            if (memoryCronExpressions.Count == 0)
                return (Timeout.InfiniteTimeSpan, Array.Empty<(string, Guid, TickerType)>());

            var nearestExpressions = new List<(string, Guid, TickerType)>();

            var nearestTimeRemaining = TimeSpan.MaxValue;

            foreach (var (functionName, cronExpression) in memoryCronExpressions)
            {
                var nextOccurrence = CrontabSchedule.Parse(cronExpression).GetNextOccurrence(now);
                var timeRemaining = nextOccurrence - now;

                if (timeRemaining < nearestTimeRemaining)
                {
                    nearestExpressions.Clear();
                    nearestExpressions.Add((functionName, default, TickerType.CronExpression));
                    nearestTimeRemaining = timeRemaining;
                }
                else if (timeRemaining == nearestTimeRemaining)
                {
                    nearestExpressions.Add((functionName, default, TickerType.CronExpression));
                }
            }
            return (nearestTimeRemaining, nearestExpressions.ToArray());
        }

        public static async Task<(TimeSpan, (string FunctionName, Guid TickerId, TickerType type)[])> GetNearestOccurrenceFromDbAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
        {
            using var scope = serviceProvider.CreateScope();

            IInternalTickerManager internalTickerManager = scope.ServiceProvider.GetRequiredService<IInternalTickerManager>();

            return await internalTickerManager.GetNextTickers().ConfigureAwait(false);
        }

        public static async Task ReleaseAcquiredResourcesAsync(IServiceProvider serviceProvider, (string FunctionName, Guid TickerId, TickerType type)[] resources, CancellationToken cancellationToken = default)
        {
            using var scope = serviceProvider.CreateScope();

            IInternalTickerManager internalTickerManager = scope.ServiceProvider.GetRequiredService<IInternalTickerManager>();

            await internalTickerManager.ReleaseAcquiredResources(resources.Select(x => (x.TickerId, x.type)), cancellationToken).ConfigureAwait(false);
        }

        public static async Task SetTickersInprogress(IServiceProvider serviceProvider, (string FunctionName, Guid TickerId, TickerType type)[] resources, CancellationToken cancellationToken = default)
        {
            using var scope = serviceProvider.CreateScope();

            IInternalTickerManager internalTickerManager = scope.ServiceProvider.GetRequiredService<IInternalTickerManager>();

            await internalTickerManager.SetTickersInprogress(resources.Select(x => (x.TickerId, x.type)), cancellationToken).ConfigureAwait(false);
        }

        public static async Task SetTickerFinalStatus(IServiceScope scope, Guid tickerId, TickerType tickerType, TickerStatus tickerStatus, CancellationToken cancellationToken = default)
        {
            IInternalTickerManager internalTickerManager = scope.ServiceProvider.GetRequiredService<IInternalTickerManager>();

            await internalTickerManager.SetTickerStatus(tickerId, tickerType, tickerStatus, cancellationToken);
        }

        public static async Task<(string FunctionName, Guid TickerId, TickerType type)[]> GetTimeoutedFunctions(IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
        {
            using var scope = serviceProvider.CreateScope();

            IInternalTickerManager internalTickerManager = scope.ServiceProvider.GetRequiredService<IInternalTickerManager>();

            return await internalTickerManager.GetTimeoutedFunctions(cancellationToken).ConfigureAwait(false);
        }
    }
}
