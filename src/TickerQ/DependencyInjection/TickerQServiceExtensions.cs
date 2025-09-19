using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using TickerQ.Provider;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Instrumentation;
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
            where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
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
            services.AddSingleton<ITickerQRedisContext, NoOpTickerQRedisContext>();

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
        
        public static TickerOptionsBuilder<TTimeTicker, TCronTicker> AddInstrumentation<TTimeTicker, TCronTicker>(
            this TickerOptionsBuilder<TTimeTicker, TCronTicker> tickerConfiguration)
            where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
            where TCronTicker : CronTickerEntity, new()
        {
            tickerConfiguration.ExternalProviderConfigServiceAction += services =>
            {
                services.TryAddSingleton<ITickerQInstrumentation, LoggerInstrumentation>();
            };

            return tickerConfiguration;
        }
        
        
        public static async Task UseTickerQAsync(this IApplicationBuilder app, TickerQStartMode qStartMode = TickerQStartMode.Immediate)
        {
            var serviceProvider = app.ApplicationServices;
            
            var lifetime = app.ApplicationServices.GetRequiredService<IHostApplicationLifetime>();
            var tickerExecutionContext = serviceProvider.GetService<TickerExecutionContext>();
            var configuration = serviceProvider.GetService<IConfiguration>();
            var notificationHubSender = serviceProvider.GetService<ITickerQNotificationHubSender>();
            var loggerInstrumentation = serviceProvider.GetService<ITickerQInstrumentation>();
            TickerFunctionProvider.UpdateCronExpressionsFromIConfiguration(configuration);
            TickerFunctionProvider.Build();

            if (tickerExecutionContext?.DashboardApplicationAction != null)
            {
                tickerExecutionContext.DashboardApplicationAction(app);
                tickerExecutionContext.DashboardApplicationAction = null;
            }
            
            tickerExecutionContext!.NotifyCoreAction = (value, type) =>
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

            if (tickerExecutionContext.ExternalProviderApplicationAction != null)
            {
                lifetime.ApplicationStarted.Register(() =>
                {
                    _ = Task.Run(async () =>
                    {
                        await tickerExecutionContext.ExternalProviderApplicationAction(serviceProvider).ConfigureAwait(false);
                        tickerExecutionContext.ExternalProviderApplicationAction =  null;
                    });
                });

                lifetime.ApplicationStopped.Register(() =>
                {
                    _ = Task.Run(async () =>
                    {
                        var internalTickerManager = serviceProvider.GetRequiredService<IInternalTickerManager>();
                        await internalTickerManager.ReleaseAcquiredResources(null, CancellationToken.None).ConfigureAwait(false);
                    });
                });
            }
            else
                await SeedDefinedCronTickers(serviceProvider).ConfigureAwait(false);
            
            lifetime.ApplicationStarted.Register(() =>
            {
                if (qStartMode == TickerQStartMode.Manual) 
                    return;

                var tickerHost = serviceProvider.GetRequiredService<ITickerHost>();
            
                tickerHost.Run();
            });
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
