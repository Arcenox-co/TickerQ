using System;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Enums;

namespace TickerQ.Utilities.Models
{
    public class TickerFunctionContext<TRequest> : TickerFunctionContext
    {
        public Lazy<Task<TRequest>> Request { get; set; }
    }

    public class TickerFunctionContext
    {
        public bool IsDue { get; set; }
        public Guid TickerId { get; set; }
        public TickerType TickerType { get; set; }
        public CancellationTokenSource CancellationTokenSource { get; set; }
    }
}
