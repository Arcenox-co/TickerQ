using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TickerQ.Provider;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Managers;
using TickerQ.Utilities.Temps;

namespace TickerQ.DependencyInjection
{
    public static class TickerQServiceExtensions
    {
        public static IServiceCollection AddTickerQ(this IServiceCollection services, Action<TickerOptionsBuilder<TimeTickerEntity, CronTickerEntity>> optionsBuilder = null)
            => AddTickerQ<TimeTickerEntity, CronTickerEntity>(services, optionsBuilder);
        
        public static IServiceCollection AddTickerQ<TTimeTicker, TCronTicker>(this IServiceCollection services, Action<TickerOptionsBuilder<TTimeTicker, TCronTicker>> optionsBuilder = null)
            where TTimeTicker : TimeTickerEntity, new()
            where TCronTicker : CronTickerEntity, new()
        {
            var tickerExecutionContext = new TickerExecutionContext();
            var optionInstance = new TickerOptionsBuilder<TTimeTicker,TCronTicker>(tickerExecutionContext);
            optionsBuilder?.Invoke(optionInstance);

            services.AddSingleton<ITimeTickerManager<TTimeTicker>, TickerManager<TTimeTicker, TCronTicker>>();
            services.AddSingleton<ICronTickerManager<TCronTicker>, TickerManager<TTimeTicker, TCronTicker>>();
            services.AddSingleton<IInternalTickerManager, TickerManager<TTimeTicker, TCronTicker>>();
            services.AddSingleton<ITickerPersistenceProvider<TTimeTicker, TCronTicker>, TickerInMemoryPersistenceProvider<TTimeTicker, TCronTicker>>();
            services.AddSingleton<ITickerQNotificationHubSender, TempTickerQNotificationHubSender>();
            services.AddSingleton<ITickerClock, TickerSystemClock>();
            services.AddSingleton<ITickerHost, TickerHost>();

            optionInstance.ExternalProviderConfigServiceAction?.Invoke(services);
            optionInstance.DashboardServiceAction?.Invoke(services);

            if (optionInstance.TickerExceptionHandlerType != null)
                services.AddSingleton(typeof(ITickerExceptionHandler), optionInstance.TickerExceptionHandlerType);

            if (tickerExecutionContext.MaxConcurrency <= 0)
                optionInstance.SetMaxConcurrency(0);

            if (string.IsNullOrEmpty(tickerExecutionContext.InstanceIdentifier))
                optionInstance.SetInstanceIdentifier(Environment.MachineName);
            
            services.AddSingleton<TickerOptionsBuilder<TTimeTicker, TCronTicker>>(_ => optionInstance);
            services.AddSingleton<TickerExecutionContext>(_ => tickerExecutionContext);
            return services;
        }
        
        public static async Task UseTickerQ(this IApplicationBuilder app, TickerQStartMode qStartMode = TickerQStartMode.Immediate)
        {
            var tickerExecutionContext = app.ApplicationServices.GetService<TickerExecutionContext>();

            tickerExecutionContext?.DashboardApplicationAction?.Invoke(app);

            await UseTickerQ(app.ApplicationServices, qStartMode);
        }
        
        internal static async Task UseTickerQ(IServiceProvider serviceProvider, TickerQStartMode qStartMode = TickerQStartMode.Immediate)
        {
            var tickerExecutionContext = serviceProvider.GetService<TickerExecutionContext>();
            var configuration = serviceProvider.GetService<IConfiguration>();
            var notificationHubSender = serviceProvider.GetService<ITickerQNotificationHubSender>();

            MapCronFromConfig(configuration);

            tickerExecutionContext.NotifyCoreAction = (value, type) =>
            {
                if (type == CoreNotifyActionType.NotifyHostExceptionMessage)
                {
                    notificationHubSender.UpdateHostException(value);
                    tickerExecutionContext.LastHostExceptionMessage = (string)value;
                }
                else if (type == CoreNotifyActionType.NotifyNextOccurence)
                    notificationHubSender.UpdateNextOccurrence(value);
                else if (type == CoreNotifyActionType.NotifyHostStatus)
                    notificationHubSender.UpdateHostStatus(value);
                else if (type == CoreNotifyActionType.NotifyThreadCount)
                    notificationHubSender.UpdateActiveThreads(value);
            };
            
            if(tickerExecutionContext.ExternalProviderApplicationAction != null)
                await tickerExecutionContext.ExternalProviderApplicationAction(serviceProvider).ConfigureAwait(false);
            else
                await SeedDefinedCronTickers(serviceProvider).ConfigureAwait(false);

            if (qStartMode == TickerQStartMode.Manual) return;

            var tickerHost = serviceProvider.GetRequiredService<ITickerHost>();
            
            tickerHost.Run();
        }
        
        
        private static void MapCronFromConfig(IConfiguration configuration)
        {
            var tickerFunctions = new Dictionary<string, (string cronExpression, TickerTaskPriority Priority, TickerFunctionDelegate Delegate)>(TickerFunctionProvider.TickerFunctions ?? new Dictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate)>());

            foreach (var (key, value) in tickerFunctions)
            {
                if (!value.cronExpression.StartsWith('%')) 
                    continue;
                
                var mappedCronExpression = configuration[value.cronExpression.Trim('%')];
                tickerFunctions[key] = (mappedCronExpression, value.Priority, value.Delegate);
            }
            
            TickerFunctionProvider.MapCronExpressionsFromIConfigurations(tickerFunctions);
        }

        private static async Task SeedDefinedCronTickers(IServiceProvider serviceProvider)
        {
            var internalTickerManager = serviceProvider.GetRequiredService<IInternalTickerManager>();
                
            var functionsToSeed = TickerFunctionProvider.TickerFunctions
                .Where(x => !string.IsNullOrEmpty(x.Value.cronExpression))
                .Select(x => (x.Key, x.Value.cronExpression)).ToArray();
                
            await internalTickerManager.MigrateDefinedCronTickers(functionsToSeed);
        }
    }
}
