using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using TickerQ.Src.Provider;
using TickerQ.Utilities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Managers;
using TickerQ.Utilities.Models.Ticker;
using TickerQ.Utilities.Temps;

namespace TickerQ.DependencyInjection
{
    public static class TickerQServiceExtensions
    {
        /// <summary>
        /// Adds Ticker to the service collection.
        /// </summary>
        /// <param name="services"></param>
        /// <param name="optionsBuilder"></param>
        /// <returns></returns>
        public static IServiceCollection AddTickerQ(this IServiceCollection services, Action<TickerOptionsBuilder> optionsBuilder = null)
        {
            services.AddScoped<ICronTickerManager<CronTicker>, TickerManager<TimeTicker, CronTicker>>();
            services.AddScoped<ITimeTickerManager<TimeTicker>, TickerManager<TimeTicker, CronTicker>>();
            services.AddScoped<IInternalTickerManager, TickerManager<TimeTicker, CronTicker>>();

            services.AddSingleton<ITickerPersistenceProvider<TimeTicker, CronTicker>, TickerInMemoryPersistenceProvider<TimeTicker, CronTicker>>();

            var optionInstance = new TickerOptionsBuilder();

            optionsBuilder?.Invoke(optionInstance);

            if (optionInstance.ExternalProviderConfigServiceAction != null)
                optionInstance.UseExternalProvider(services);

            if (optionInstance.TickerExceptionHandlerType != null)
                services.AddScoped(typeof(ITickerExceptionHandler), optionInstance.TickerExceptionHandlerType);

            if (optionInstance.MaxConcurrency <= 0)
                optionInstance.SetMaxConcurrency(0);

            if (string.IsNullOrEmpty(optionInstance.InstanceIdentifier))
                optionInstance.SetInstanceIdentifier(Environment.MachineName);

            if (optionInstance.DashboardServiceAction != null)
                optionInstance.DashboardServiceAction(services);
            else
                services.AddSingleton<ITickerQNotificationHubSender, TempTickerQNotificationHubSender>();

            services
                .AddSingleton<ITickerClock, TickerSystemClock>()
                .AddSingleton<ICronParserProvider, CrontabCronParserProvider>()
                ;

            services.AddSingleton<TickerOptionsBuilder>(_ => optionInstance)
                .AddSingleton<ITickerHost, TickerHost>();

            return services;
        }

        public static void UseTickerQ(this IApplicationBuilder app,
            TickerQStartMode qStartMode = TickerQStartMode.Immediate)
        {
            var tickerOptBuilder = app.ApplicationServices.GetService<TickerOptionsBuilder>();

            if (tickerOptBuilder?.DashboardApplicationAction != null)
                tickerOptBuilder.DashboardApplicationAction.Invoke(app, tickerOptBuilder.DashboardLunchUrl);

            UseTickerQ(app.ApplicationServices, qStartMode);
        }

        /// <summary>
        /// Use Ticker in the application.
        /// </summary>
        /// <param name="serviceProvider"></param>
        /// <param name="qStartMode"></param>
        /// <returns></returns>
        internal static void UseTickerQ(IServiceProvider serviceProvider, TickerQStartMode qStartMode = TickerQStartMode.Immediate)
        {
            var tickerOptBuilder = serviceProvider.GetService<TickerOptionsBuilder>();
            var configuration = serviceProvider.GetService<IConfiguration>();

            MapCronFromConfig(configuration);

            tickerOptBuilder.NotifyNextOccurenceFunc = nextOccurrence =>
            {
                var notificationHubSender = serviceProvider.GetService<ITickerQNotificationHubSender>();

                notificationHubSender.UpdateNextOccurrence(nextOccurrence);
            };

            tickerOptBuilder.NotifyHostStatusFunc = active =>
            {
                var notificationHubSender = serviceProvider.GetService<ITickerQNotificationHubSender>();

                notificationHubSender.UpdateHostStatus(active);
            };

            tickerOptBuilder.HostExceptionMessageFunc = message =>
            {
                tickerOptBuilder.LastHostExceptionMessage = message;
                var notificationHubSender = serviceProvider.GetService<ITickerQNotificationHubSender>();

                notificationHubSender.UpdateHostException(message);
            };

            if (tickerOptBuilder.ExternalProviderConfigApplicationAction != null)
                tickerOptBuilder.ExternalProviderConfigApplicationAction(serviceProvider);
            else
                SeedDefinedCronTickers(serviceProvider);

            if (qStartMode == TickerQStartMode.Manual) return;

            var tickerHost = serviceProvider.GetRequiredService<ITickerHost>();

            tickerHost.Start();
        }

        private static void MapCronFromConfig(IConfiguration configuration)
        {
            var tickerFunctions =
                new Dictionary<string, (string cronExpression, TickerTaskPriority Priority, TickerFunctionDelegate
                    Delegate)>(TickerFunctionProvider.TickerFunctions ?? new Dictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate)>());

            foreach (var (key, value) in tickerFunctions)
            {
                if (!value.cronExpression.StartsWith("%")) continue;

                var mappedCronExpression = configuration[value.cronExpression.Trim('%')];
                tickerFunctions[key] = (mappedCronExpression, value.Priority, value.Delegate);
            }
            TickerFunctionProvider.MapCronExpressionsFromIConfigurations(tickerFunctions);
        }

        private static void SeedDefinedCronTickers(IServiceProvider serviceProvider)
        {
            using var scope = serviceProvider.CreateScope();

            var internalTickerManager = scope.ServiceProvider.GetRequiredService<IInternalTickerManager>();

            var functionsToSeed = TickerFunctionProvider.TickerFunctions
                .Where(x => !string.IsNullOrEmpty(x.Value.cronExpression))
                .Select(x => (x.Key, x.Value.cronExpression)).ToArray();

            internalTickerManager.SyncWithDbMemoryCronTickers(functionsToSeed).GetAwaiter().GetResult();
        }
    }
}