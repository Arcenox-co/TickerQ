using System;
using TickerQ.Utilities.Enums;

namespace TickerQ.Utilities.DashboardDtos
{
    public class CronOccurrenceTickerGraphData
    {
        public DateTime Date { get; set; }
        public Tuple<int, int>[] Results { get; set; }
    }
}