using System;
using TickerQ.Utilities.Enums;

namespace TickerQ.Utilities.Base
{
    [AttributeUsage(AttributeTargets.Method)]
    public class TickerFunctionAttribute : Attribute
    {
        /// <summary>
        /// Optional declared result contract. The annotated method must return this type directly
        /// or through Task/ValueTask; generated code publishes it through the AOT-safe result path.
        /// </summary>
        public Type ResultType { get; set; }

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
    }
}