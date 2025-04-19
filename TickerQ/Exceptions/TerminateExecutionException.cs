using System;

namespace TickerQ.Exceptions
{
    internal class TerminateExecutionException : Exception
    {
        
    }

    internal class ExceptionDetailClassForSerialization
    {
        public string Message { get; set; }
        public string StackTrace { get; set; }
    }
}