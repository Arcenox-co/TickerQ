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
    }
}