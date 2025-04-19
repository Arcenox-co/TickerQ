using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Temps;

namespace TickerQ.DependencyInjection
{
    public static class ServiceExtensions
    {
        /// <summary>
        /// Adds Ticker to the service collection.
        /// </summary>
        /// <param name="services"></param>
        /// <param name="optionsBuilder"></param>
        /// <returns></returns>
        public static IServiceCollection AddTickerQ(this IServiceCollection services, Action<TickerOptionsBuilder> optionsBuilder = null)
        {
            var optionInstance = new TickerOptionsBuilder();

            optionsBuilder?.Invoke(optionInstance);

            if (optionInstance.EfCoreConfigServiceAction != null)
                optionInstance.SetUseEfCore(services);

            if(optionInstance.TickerExceptionHandlerType != null)
                services.AddScoped(typeof(ITickerExceptionHandler), optionInstance.TickerExceptionHandlerType);

            if (optionInstance.MaxConcurrency <= 0)
                optionInstance.SetMaxConcurrency(0);
            
            if(string.IsNullOrEmpty(optionInstance.InstanceIdentifier))
                optionInstance.SetInstanceIdentifier(Environment.MachineName);

            if (optionInstance.DashboardServiceAction != null)
            {
                if(optionInstance.UseEfCore)
                    optionInstance.DashboardServiceAction(services);
                else 
                    throw new Exception("TickerQ Dashboard service can be used only with EF Core");
            }
            else
                services.AddSingleton<ITickerQNotificationHubSender, TempTickerQNotificationHubSender>();
            
            services.AddSingleton<ITickerClock, TickerSystemClock>();

            services.AddSingleton<TickerOptionsBuilder>(_ => optionInstance)
                .AddSingleton<ITickerHost, TickerHost>();

            return services;
        }

        /// <summary>
        /// Use Ticker in the application.
        /// </summary>
        /// <param name="app"></param>
        /// <param name="qStartMode"></param>
        /// <returns></returns>
        public static IApplicationBuilder UseTickerQ(this IApplicationBuilder app, TickerQStartMode qStartMode = TickerQStartMode.Immediate)
        {
            var tickerOptBuilder = app.ApplicationServices.GetService<TickerOptionsBuilder>();
            var configuration = app.ApplicationServices.GetService<IConfiguration>();

            var functionsToSeed  = MapCronFromConfig(configuration).ToList();
            
            if (tickerOptBuilder is { DashboardApplicationAction: { } })
                tickerOptBuilder.DashboardApplicationAction.Invoke(app, tickerOptBuilder.DashboardLunchUrl);

            tickerOptBuilder.NotifyNextOccurenceFunc = nextOccurrence =>
            {
                var notificationHubSender = app.ApplicationServices.GetService<ITickerQNotificationHubSender>();
                
                notificationHubSender.UpdateNextOccurrence(nextOccurrence);
            };
            
            tickerOptBuilder.NotifyHostStatusFunc = active =>
            {
                var notificationHubSender = app.ApplicationServices.GetService<ITickerQNotificationHubSender>();
                
                notificationHubSender.UpdateHostStatus(active);
            };
            
            tickerOptBuilder.HostExceptionMessageFunc = message =>
            {
                tickerOptBuilder.LastHostExceptionMessage = message;
                var notificationHubSender = app.ApplicationServices.GetService<ITickerQNotificationHubSender>();
                
                notificationHubSender.UpdateHostException(message);
            };

            if (tickerOptBuilder.UseEfCore)
            {
                using var scope = app.ApplicationServices.CreateScope();
                
                var internalTickerManager = scope.ServiceProvider.GetRequiredService<IInternalTickerManager>();
                
                internalTickerManager.SyncWithDbMemoryCronTickers(functionsToSeed).GetAwaiter().GetResult();

                internalTickerManager.ReleaseOrCancelAllAcquiredResources(tickerOptBuilder.CancelMissedTickersOnReset).GetAwaiter().GetResult();
            }
            
            if (qStartMode == TickerQStartMode.Manual) return app;

            var tickerHost = app.ApplicationServices.GetRequiredService<ITickerHost>();
            
            tickerHost.Start();

            return app;
        }

        private static IEnumerable<(string, string)> MapCronFromConfig(IConfiguration configuration)
        {
            var tickerFunctions =
                new Dictionary<string, (string cronExpression, TickerTaskPriority Priority, TickerFunctionDelegate
                    Delegate)>(TickerFunctionProvider.TickerFunctions ?? new Dictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate)>());
            
            foreach (var (key, value) in tickerFunctions)
            {
                if (value.cronExpression.StartsWith("%"))
                {
                    var mappedCronExpression = configuration[value.cronExpression.Trim('%')];
                    tickerFunctions[key] = (mappedCronExpression, value.Priority, value.Delegate);
                    
                    if(string.IsNullOrEmpty(mappedCronExpression))
                        continue;
                    
                    yield return (key, mappedCronExpression);
                }
                else if(!string.IsNullOrWhiteSpace(value.cronExpression))
                    yield return (key, value.cronExpression);
            }
            TickerFunctionProvider.MapCronExpressionsFromIConfigurations(tickerFunctions);
        }
    }
}
