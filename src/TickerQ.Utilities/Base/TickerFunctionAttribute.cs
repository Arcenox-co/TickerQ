using System;
using TickerQ.Utilities.Enums;

namespace TickerQ.Utilities.Base
{
    public class TickerFunctionAttribute : Attribute
    {
        /// <summary>
        /// Gets the function name for this ticker function.
        /// </summary>
        public string FunctionName { get; }

        /// <summary>
        /// Gets the cron expression for this ticker function.
        /// </summary>
        public string CronExpression { get; }

        /// <summary>
        /// Gets the task priority for this ticker function.
        /// </summary>
        public TickerTaskPriority TaskPriority { get; }

        public TickerFunctionAttribute(string functionName, string cronExpression = null,
            TickerTaskPriority taskPriority = TickerTaskPriority.Normal)
        {
            FunctionName = functionName;
            CronExpression = cronExpression;
            TaskPriority = taskPriority;
        }

        public TickerFunctionAttribute(string functionName, TickerTaskPriority taskPriority)
        {
            FunctionName = functionName;
            CronExpression = null;
            TaskPriority = taskPriority;
        }
    }
}