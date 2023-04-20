using System;
using TickerQ.Utilities.Interfaces;

namespace TickerQ
{
    public class SystemClock : IClock
    {
        public TimeZoneInfo TimeZone { get; private set; }

        public SystemClock() : this(TimeZoneInfo.Local) { }

        public SystemClock(TimeZoneInfo timeZone)
            => TimeZone = timeZone;

        public DateTimeOffset OffsetNow => TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZone);
        public DateTime Now => OffsetNow.DateTime;
    }
}
