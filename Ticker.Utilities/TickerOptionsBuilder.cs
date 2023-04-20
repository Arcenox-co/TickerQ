using System;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using TickerQ.Utilities.Interfaces;

namespace TickerQ.Utilities
{
    public class TickerOptionsBuilder
    {
        internal Action<IServiceCollection> EfCoreConfigAction { get; set; }
        internal bool UseEfCore { get; private set; }
        internal Assembly[] Assemblies { get; set; }
        internal TimeSpan TimeOutChecker { get; set; } = TimeSpan.FromMinutes(1);
        internal Type TickerHandlerService { get; set; }
        internal TimeZoneInfo TimeZoneInfo { get; set; }
        internal int MaxConcurrency { get; set; } = 0;
        internal string InstanceIdentifier { get; set; }

        /// <summary>
        /// Set assemblies to scan for TickerJob
        /// </summary>
        /// <param name="assemblies"></param>
        public void SetAssemblies(params Assembly[] assemblies)
            => Assemblies = assemblies;

        /// <summary>
        /// Default max concurrency is 0, which means no limit
        /// </summary>
        /// <param name="maxConcurrency"></param>
        public void SetMaxConcurrency(int maxConcurrency)
            => MaxConcurrency = maxConcurrency;

        /// <summary>
        /// Default is Local Timezone
        /// </summary>
        /// <param name="timeZoneInfo"></param>
        public void SetClockTimeZone(TimeZoneInfo timeZoneInfo)
            => TimeZoneInfo = timeZoneInfo;

        /// <summary>
        /// Set Ticker Exception Handler
        /// </summary>
        /// <typeparam name="THandler"></typeparam>
        public void SetExceptionHandler<THandler>() where THandler : ITickerExceptionHandler
            => TickerHandlerService = typeof(THandler);

        internal void SetUseEfCore(IServiceCollection services)
        {
            EfCoreConfigAction(services);

            UseEfCore = true;

            EfCoreConfigAction = default;
        }
    }
}
