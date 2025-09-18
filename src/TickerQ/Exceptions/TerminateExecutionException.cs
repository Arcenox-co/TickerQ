using System;

namespace TickerQ.Exceptions
{
    internal class TerminateExecutionException : Exception
    {
        public TerminateExecutionException(string message) : base(message) { }
    }

    internal class ExceptionDetailClassForSerialization
    {
        public string Message { get; set; }
        public string StackTrace { get; set; }
    }
}