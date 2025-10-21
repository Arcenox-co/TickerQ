using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using TickerQ.Utilities.Interfaces;

namespace TickerQ.Utilities
{
    public class TickerOptionsBuilder
    {
        internal Type TimeTickerType { get; set; }
        internal Type CronTickerType { get; set; }
        internal Action<IServiceCollection> ExternalProviderConfigServiceAction { get; set; }
        internal Action<IServiceProvider> ExternalProviderConfigApplicationAction { get; set; }
        internal Action<IServiceCollection> DashboardServiceAction { get; set; }
        internal Action<IApplicationBuilder, string> DashboardApplicationAction { get; set; }
        internal TimeSpan TimeOutChecker { get; private set; } = TimeSpan.FromMinutes(1);
        internal Type TickerExceptionHandlerType { get; private set; }
        internal int MaxConcurrency { get; private set; } = 0;
        internal string InstanceIdentifier { get; private set; }
        internal bool EnableBasicAuth { get; set; }
        internal string DashboardLunchUrl { get; set; }
        internal static int ActiveThreads;
        internal static Action<int> NotifyThreadCountFunc;
        internal Action<DateTime?> NotifyNextOccurenceFunc;
        internal Action<bool> NotifyHostStatusFunc;
        internal Action<string> HostExceptionMessageFunc;
        internal string LastHostExceptionMessage;
        internal bool SeedDefinedCronJobsToPersistence;
        /// <summary>
        /// Default max concurrency is Environment.ProcessorCount
        /// </summary>
        /// <param name="maxConcurrency"></param>
        public void SetMaxConcurrency(int maxConcurrency)
        {
            MaxConcurrency = maxConcurrency <= 0 ? Environment.ProcessorCount : maxConcurrency;
        }

        public void SetInstanceIdentifier(string instanceIdentifier)
        {
            InstanceIdentifier = instanceIdentifier;
        }
        
        /// <summary>
        /// Set Ticker Exception Handler
        /// </summary>
        /// <typeparam name="THandler"></typeparam>
        public void SetExceptionHandler<THandler>() where THandler : ITickerExceptionHandler
            => TickerExceptionHandlerType = typeof(THandler);

        internal void UseExternalProvider(IServiceCollection services)
        {
            ExternalProviderConfigServiceAction(services);
            
            ExternalProviderConfigServiceAction = null;
        }
        
        /// <summary>
        /// Timeout checker default is 1 minute, cannot set less than 30 seconds
        /// </summary>
        /// <param name="timeSpan"></param>
        public void UpdateMissedJobCheckDelay(TimeSpan timeSpan)
        {
            TimeOutChecker = timeSpan < TimeSpan.FromSeconds(30)
                ? TimeSpan.FromSeconds(30)
                : timeSpan;
        }
    }
}
