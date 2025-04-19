using System;
using TickerQ.Utilities.Enums;

namespace TickerQ.Utilities.DashboardDtos
{
    public class TickerGraphData
    {
        public DateTime Date { get; set; }
        public Tuple<TickerStatus, int>[] Results { get; set; }
    }
}