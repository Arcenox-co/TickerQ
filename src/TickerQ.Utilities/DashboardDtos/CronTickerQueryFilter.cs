using TickerQ.Utilities.Enums;

namespace TickerQ.Utilities.DashboardDtos
{
    public class CronTickerQueryFilter
    {
        public string FunctionName { get; set; }
        public TickerStatus[] LastRunStatuses { get; set; }
        public string Expression { get; set; }
        public string Search { get; set; }
        public string SortBy { get; set; }
        public bool SortDescending { get; set; }
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 20;
    }
}
