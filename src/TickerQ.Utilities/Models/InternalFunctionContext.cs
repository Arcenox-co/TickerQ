using System;
using TickerQ.Utilities.Enums;

namespace TickerQ.Utilities.Models
{
    internal class InternalFunctionContext
    {
        public string FunctionName { get; set; }
        public Guid TickerId { get; set; }
        public TickerType Type { get; set; }
        public int Retries { get; set; }
        public int RetryCount { get; set; }
        public TickerStatus Status { get; set; }
        public long ElapsedTime { get; set; }
        public string ExceptionDetails { get; set; }
        public int[] RetryIntervals { get; set; }
    }
}
