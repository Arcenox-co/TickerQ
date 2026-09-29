using System;

namespace TickerQ.Utilities.Exceptions
{
    public class TickerTerminalUpdateNotAcknowledgedException : InvalidOperationException
    {
        public TickerTerminalUpdateNotAcknowledgedException(string message) : base(message) { }
    }
}