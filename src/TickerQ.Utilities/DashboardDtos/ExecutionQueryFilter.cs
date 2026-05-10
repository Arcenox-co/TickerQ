using System;
using TickerQ.Utilities.Enums;

namespace TickerQ.Utilities.DashboardDtos
{
    public class ExecutionQueryFilter
    {
        public ExecutionType[] Types { get; set; }
        public TickerStatus[] Statuses { get; set; }
        public string FunctionName { get; set; }
        public DateTime? ExecutedFrom { get; set; }
        public DateTime? ExecutedTo { get; set; }
        public string Search { get; set; }
        public string SortBy { get; set; }
        public bool SortDescending { get; set; } = true;
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 20;
    }
}
