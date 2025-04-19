using System;

namespace TickerQ.Utilities.Interfaces
{
    internal interface ITickerClock
    {
        DateTime UtcNow { get; }
    }
}
