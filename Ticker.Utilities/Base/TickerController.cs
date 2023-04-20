using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;

namespace TickerQ.Utilities.Base
{
    public abstract class TickerController
    {
        public IServiceProvider ServiceProvider { get; set; }

        public virtual async Task<T> GetRequestValueAsync<T>(Guid tickerId, TickerType tickerType)
        {
            IInternalTickerManager tickerManager = ServiceProvider.GetRequiredService<IInternalTickerManager>();

            return await tickerManager.GetRequest<T>(tickerId, tickerType);
        }
    }
}
