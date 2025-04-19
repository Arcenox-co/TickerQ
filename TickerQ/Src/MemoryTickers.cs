using System;
using System.Linq;
using System.Threading;
using NCrontab;
using TickerQ.Utilities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Models;

namespace TickerQ
{
    internal static class MemoryTickers
    {
        public static (TimeSpan TimeRemaining, InternalFunctionContext[] Functions) GetNextMemoryTicker()
        {
            var now = DateTime.UtcNow;

            var validFunctions = TickerFunctionProvider.TickerFunctions
                .Select(kvp =>
                {
                    try
                    {
                        var schedule = CrontabSchedule.Parse(kvp.Value.cronExpression);
                        var nextOccurrence = schedule.GetNextOccurrence(now);
                        var timeRemaining = nextOccurrence - now;

                        return new
                        {
                            Key = kvp.Key,
                            NextOccurrence = nextOccurrence,
                            TimeRemaining = timeRemaining
                        };
                    }
                    catch
                    {
                        return null;
                    }
                })
                .Where(x => x != null)
                .ToList();

            if (!validFunctions.Any())
            {
                return (Timeout.InfiniteTimeSpan, Array.Empty<InternalFunctionContext>());
            }

            var closestTime = validFunctions.Min(x => x.NextOccurrence);

            var matchingFunctions = validFunctions
                .Where(x => x.NextOccurrence == closestTime)
                .ToArray();

            return (
                TimeRemaining: matchingFunctions[0].TimeRemaining,
                Functions: matchingFunctions
                    .Select(f => new InternalFunctionContext { FunctionName = f.Key, Type = TickerType.CronExpression })
                    .ToArray()
            );
        }
    }
}