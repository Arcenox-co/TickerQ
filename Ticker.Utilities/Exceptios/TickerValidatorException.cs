using System;

namespace TickerQ.Utilities.Exceptios
{
    public class TickerValidatorException : Exception
    {
        public TickerValidatorException(string message) : base(message)
        {
        }
    }
}
