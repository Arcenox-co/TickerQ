using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces;

namespace TickerQ.Utilities
{
    public class TickerOptionsBuilder<TTimeTicker, TCronTicker>
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        private readonly TickerExecutionContext _tickerExecutionContext;
        internal TickerOptionsBuilder(TickerExecutionContext tickerExecutionContext)
            => _tickerExecutionContext = tickerExecutionContext;
        internal Action<IServiceCollection> ExternalProviderConfigServiceAction { get; set; }
        internal Action<IServiceCollection> DashboardServiceAction { get; set; }
        internal Type TickerExceptionHandlerType { get; private set; }
        
        /// <summary>
        /// Default max concurrency is Environment.ProcessorCount
        /// </summary>
        /// <param name="maxConcurrency"></param>
        public TickerOptionsBuilder<TTimeTicker, TCronTicker> SetMaxConcurrency(int maxConcurrency)
        {
             _tickerExecutionContext.MaxConcurrency = maxConcurrency <= 0 
                 ? Environment.ProcessorCount 
                 : maxConcurrency;
             
             return this;
        }

        public TickerOptionsBuilder<TTimeTicker, TCronTicker> SetInstanceIdentifier(string instanceIdentifier)
        {
            _tickerExecutionContext.InstanceIdentifier = instanceIdentifier;
            return this;
        }

        /// <summary>
        /// Set Ticker Exception Handler
        /// </summary>
        /// <typeparam name="THandler"></typeparam>
        public TickerOptionsBuilder<TTimeTicker, TCronTicker> SetExceptionHandler<THandler>() where THandler : ITickerExceptionHandler
        {
            TickerExceptionHandlerType = typeof(THandler);
            return this;
        }

        /// <summary>
        /// Timeout checker default is 1 minute, cannot set less than 30 seconds
        /// </summary>
        /// <param name="timeSpan"></param>
        public TickerOptionsBuilder<TTimeTicker, TCronTicker> FallbackIntervalChecker(TimeSpan timeSpan)
        {
            _tickerExecutionContext.TimeOutChecker = timeSpan < TimeSpan.FromSeconds(30)
                ? TimeSpan.FromSeconds(30)
                : timeSpan;
            
            return this;
        }

        internal void UseExternalProviderApplication(Func<IServiceProvider, Task> func)
            => _tickerExecutionContext.ExternalProviderApplicationAction = func;
        
        internal void UseDashboardApplication(Action<IApplicationBuilder> action)
            => _tickerExecutionContext.DashboardApplicationAction = action;
    }
}
