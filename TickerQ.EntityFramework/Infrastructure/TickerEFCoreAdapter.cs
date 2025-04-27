using System;
using System.Collections.Generic;
using System.Threading;
using NCrontab;
using TickerQ.Utilities.Enums;

namespace TickerQ.EntityFrameworkCore.Infrastructure
{
    internal static class TickerEFCoreAdapter
    {
        public static (TimeSpan, (string FunctionName, Guid TickerId, TickerType type)[])
            GetNearestMemoryCronExpressions(IReadOnlyDictionary<string, string> memoryCronExpressions, DateTime now)
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
                    nearestExpressions.Add((functionName, Guid.Empty, TickerType.CronExpression));
                    nearestTimeRemaining = timeRemaining;
                }
                else if (timeRemaining == nearestTimeRemaining)
                {
                    nearestExpressions.Add((functionName, Guid.Empty, TickerType.CronExpression));
                }
            }

            return (nearestTimeRemaining, nearestExpressions.ToArray());
        }
    }
}