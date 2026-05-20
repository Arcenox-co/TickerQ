using System;
using TickerQ.Utilities.Enums;

namespace TickerQ.Utilities.Base
{
    [AttributeUsage(AttributeTargets.Method)]
    public class TickerFunctionAttribute : Attribute
    {
        public TickerFunctionAttribute(string functionName, string cronExpression = null,
            TickerTaskPriority taskPriority = TickerTaskPriority.Normal, int maxConcurrency = 0)
        {
            _ = functionName;
            _ = cronExpression;
            _ = taskPriority;
            _ = maxConcurrency;
        }

        public TickerFunctionAttribute(string functionName, TickerTaskPriority taskPriority, int maxConcurrency = 0)
        {
            _ = functionName;
            _ = taskPriority;
            _ = maxConcurrency;
        }

        /// <summary>
        /// Optional periodic interval (parsable as <see cref="TimeSpan"/>, e.g. <c>"00:00:30"</c> or <c>"1.00:00:00"</c>).
        /// When set, the source generator records the interval and TickerQ seeds a <c>PeriodicTickerEntity</c>
        /// at startup (only when <c>EnablePeriodic&lt;T&gt;()</c> is configured on the core options).
        /// Mutually exclusive with <see cref="System.Type"/> cron expressions.
        /// </summary>
        public string PeriodicInterval { get; set; }
    }
}