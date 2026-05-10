using TickerQ.Utilities.Enums;

namespace TickerQ.Utilities.DashboardDtos
{
    public class FunctionInfoDto
    {
        public string FunctionName { get; set; }
        public string RequestType { get; set; }
        public string RequestExample { get; set; }
        public TickerTaskPriority Priority { get; set; }
        public string CronExpression { get; set; }
    }
}
