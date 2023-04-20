using System.Collections.Generic;
using TickerQ.Utilities.Enums;

namespace TickerQ.Utilities.Interfaces
{
    public interface ITickerCollection
    {
        IReadOnlyCollection<(string Function, TickerTaskPriority TaskPriority)> GetTickerFunctions();
        IReadOnlyCollection<(string Function, string CronExpression)> GetMemoryTickers();
        bool ExistFunction(string function);
    }
}
