using System;
using System.Threading.Tasks;
using TickerQ.Utilities.Enums;

namespace TickerQ.Utilities.Models
{
    public class TickerFunctionContext<TRequest> : TickerFunctionContext
    {
        public TickerFunctionContext(TickerFunctionContext tickerFunctionContext, TRequest request) : 
            base(tickerFunctionContext.Id, tickerFunctionContext.Type, tickerFunctionContext.RetryCount, tickerFunctionContext.IsDue, tickerFunctionContext.DeleteAsync, tickerFunctionContext.CancelTicker)
        {
            Request = request;
        }
        
        public TRequest Request { get; }
    }

    public class TickerFunctionContext
    {
        internal TickerFunctionContext(Guid id, TickerType type, int retryCount, bool isDue, Func<Task> deleteAsync, Action cancelTicker)
        {
            Id = id;
            Type = type;
            RetryCount = retryCount;
            IsDue = isDue;
            DeleteAsync = deleteAsync;
            CancelTicker = cancelTicker;
        }
        public Guid Id { get; }
        public TickerType Type { get; }
        public int RetryCount { get; }
        public bool IsDue { get; }
        /// <summary>
        /// Deletes current Ticker and terminates it
        /// </summary>
        public Func<Task> DeleteAsync { get; }
        public Action CancelTicker { get; }
    }
}
