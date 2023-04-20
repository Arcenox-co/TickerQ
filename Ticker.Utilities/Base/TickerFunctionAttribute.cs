using System;
using TickerQ.Utilities.Enums;

namespace TickerQ.Utilities.Base
{
    public class TickerFunctionAttribute : Attribute
    {
        public TickerFunctionAttribute(string FunctionName, string CronExpression = null, TickerTaskPriority TaskPriority = TickerTaskPriority.Normal)
        {
            this.FunctionName = FunctionName;
            this.CronExpression = CronExpression;
            this.TaskPriority = TaskPriority;
        }

        public TickerFunctionAttribute(string FunctionName, TickerTaskPriority TaskPriority)
        {
            this.FunctionName = FunctionName;
            this.TaskPriority = TaskPriority;
        }

        public readonly string FunctionName;
        public readonly string CronExpression;
        public readonly TickerTaskPriority TaskPriority;
    }
}
