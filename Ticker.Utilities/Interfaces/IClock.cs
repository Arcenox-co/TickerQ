using System;

namespace TickerQ.Utilities.Interfaces
{
    internal interface IClock
    {
        TimeZoneInfo TimeZone { get; }
        DateTimeOffset OffsetNow { get; }
        DateTime Now { get; }
    }
}
