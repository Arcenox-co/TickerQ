using System;

namespace TickerQ.Utilities.Interfaces
{
    public interface ITickerClock
    {
        DateTime UtcNow { get; }
    }
}
