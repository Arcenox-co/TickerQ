using System;

namespace TickerQ.Utilities.Exceptions
{
    public class TickerValidatorException : Exception
    {
        public TickerValidatorException(string message) : base(message)
        {
        }
    }
}
