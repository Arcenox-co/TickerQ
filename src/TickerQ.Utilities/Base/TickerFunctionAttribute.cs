using System;
using TickerQ.Utilities.Enums;

namespace TickerQ.Utilities.Base
{
    [AttributeUsage(AttributeTargets.Method)]
    public class TickerFunctionAttribute : Attribute
    {
        internal const string CronMovedMessage =
            "Cron schedules are no longer declared on [TickerFunction]. Remove the cron argument and declare the " +
            "schedule with options.SeedCronTickers(crons => crons.Add(\"FunctionName\", \"<cron>\")). " +
            "See https://tickerq.net/docs/5.x/guides/scheduling/cron-ticker#seed-cron-tickers.";

        /// <summary>
        /// Optional declared result contract. The annotated method must return this type directly
        /// or through Task/ValueTask; generated code publishes it through the AOT-safe result path.
        /// </summary>
        public Type ResultType { get; set; }

        /// <summary>
        /// Registers the method as a TickerQ function. Schedule it with the cron or time ticker managers,
        /// or declare an owned cron with <c>options.SeedCronTickers(...)</c>.
        /// </summary>
        public TickerFunctionAttribute(string functionName,
            TickerTaskPriority taskPriority = TickerTaskPriority.Normal, int maxConcurrency = 0)
        {
            _ = functionName;
            _ = taskPriority;
            _ = maxConcurrency;
        }

        /// <summary>No longer supported: declare cron schedules with <c>options.SeedCronTickers(...)</c>.</summary>
        [Obsolete(CronMovedMessage, error: true)]
        public TickerFunctionAttribute(string functionName, string cronExpression,
            TickerTaskPriority taskPriority = TickerTaskPriority.Normal, int maxConcurrency = 0)
        {
            _ = functionName;
            _ = cronExpression;
            _ = taskPriority;
            _ = maxConcurrency;
        }
    }
}
