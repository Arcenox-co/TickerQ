using System;
using System.Threading.Tasks;
using TickerQ.Utilities.Enums;

namespace TickerQ.Utilities.Interfaces
{
    public interface ITickerExceptionHandler
    {
        public Task HandleExceptionAsync(Exception exception, Guid tickerId, TickerType tickerType);
        public Task HandleCanceledExceptionAsync(Exception exception, Guid tickerId, TickerType tickerType);
    }
}
