using System;
using TickerQ.Utilities.Enums;

namespace TickerQ.Dashboard.Hubs
{
    internal sealed class PeriodicOccurrenceUpdateNotification
    {
        public Guid Id { get; set; }
        public TickerStatus Status { get; set; }
        public Guid? PeriodicTickerId { get; set; }
        public DateTime ExecutedAt { get; set; }
        public long ElapsedTime { get; set; }
        public int RetryCount { get; set; }
        public string ExceptionMessage { get; set; }
    }
}

