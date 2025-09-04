using NCrontab;
using System;
using TickerQ.Utilities.Interfaces;

namespace TickerQ.Src.Provider
{
    public sealed class CrontabCronParserProvider : ICronParserProvider
    {
        public bool TryGetNextOccurrence(string expression, DateTime dateTime, out DateTime nextOccurrence)
        {
            nextOccurrence = DateTime.MinValue;

            if (!(CrontabSchedule.TryParse(expression) is { } crontabSchedule))
            {
                return false;
            }

            nextOccurrence = crontabSchedule.GetNextOccurrence(dateTime);

            return true;
        }
    }
}