using System;

namespace TickerQ.Utilities.Models.Ticker
{
    public class BaseTicker
    {
        public Guid Id { get; set; }
        public string Function { get; set; }
        public string Description { get; set; }
        public string InitIdentifier { get; internal set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
