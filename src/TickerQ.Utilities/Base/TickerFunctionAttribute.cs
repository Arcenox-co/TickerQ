using System;
using TickerQ.Utilities.Enums;

namespace TickerQ.Utilities.Base
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class TickerFunctionAttribute : Attribute
    {
        public string Name { get; set; }
        public string CronExpression { get; set; }
        public TickerTaskPriority Priority { get; set; }
        
        public TickerFunctionAttribute(string functionName, string cronExpression = null,
            TickerTaskPriority taskPriority = TickerTaskPriority.Normal)
        {
            Name = functionName;
            CronExpression = cronExpression;
            Priority = taskPriority;
        }

        public TickerFunctionAttribute(string functionName, TickerTaskPriority taskPriority)
        {
            Name = functionName;
            CronExpression = null;
            Priority = taskPriority;
        }
    }
}
