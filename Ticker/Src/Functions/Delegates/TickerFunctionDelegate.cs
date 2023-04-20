using System;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Enums;

namespace TickerQ.Functions.Delegates
{
    internal delegate Task TickerFunctionDelegate(IServiceProvider ticker, Guid tickerId, TickerType tickerType, CancellationToken cancellationToken = default, bool isDue = false);
}
