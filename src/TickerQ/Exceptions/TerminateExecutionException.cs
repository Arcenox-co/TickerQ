using System;
using TickerQ.Utilities.Enums;

namespace TickerQ.Exceptions
{
    internal class TerminateExecutionException : Exception
    {
        internal readonly TickerStatus Status = TickerStatus.Skipped;
        public TerminateExecutionException(string message) : base(message) { }
        public TerminateExecutionException(TickerStatus tickerType, string message) : base(message)
            => Status = tickerType;
        public TerminateExecutionException(string message, Exception innerException) : base(message, innerException) { }
        public TerminateExecutionException(TickerStatus tickerType, string message, Exception innerException) : base(message, innerException)
            => Status = tickerType;
    }

    internal class ExceptionDetailClassForSerialization
    {
        public string Message { get; set; }
        public string StackTrace { get; set; }
    }
}