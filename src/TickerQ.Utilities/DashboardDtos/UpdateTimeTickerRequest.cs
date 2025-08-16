using System;

namespace TickerQ.Utilities.DashboardDtos
{
    public class UpdateTimeTickerRequest
    {
        public string Function { get; set; } = string.Empty;
        public string Request { get; set; } = string.Empty;
        public int Retries { get; set; }
        public string Description { get; set; } = string.Empty;
        public string? ExecutionTime { get; set; }
        public int[]? Intervals { get; set; }
    }
} 