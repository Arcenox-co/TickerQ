using System;
using TickerQ.Utilities.Enums;

namespace TickerQ.Utilities.DashboardDtos
{
    public class CronOccurrenceTickerGraphData
    {
        public DateTime Date { get; set; }
        public Tuple<TickerStatus, int>[] Results { get; set; }
    }
}