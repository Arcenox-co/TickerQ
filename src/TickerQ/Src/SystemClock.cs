using System;
using TickerQ.Utilities.Interfaces;

namespace TickerQ
{
    internal class TickerSystemClock : ITickerClock
    {
        public DateTime UtcNow => DateTime.UtcNow;
    }
}
