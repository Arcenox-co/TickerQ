using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TickerQ.BackgroundServices;
using TickerQ.Provider;
using TickerQ.TickerQThreadPool;
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
            var schedulerOptionsBuilder = new SchedulerOptionsBuilder();
            var optionInstance = new TickerOptionsBuilder<TTimeTicker,TCronTicker>(tickerExecutionContext, schedulerOptionsBuilder);
            optionsBuilder?.Invoke(optionInstance);
            CronScheduleCache.TimeZoneInfo = schedulerOptionsBuilder.SchedulerTimeZone;
            services.AddSingleton<ITimeTickerManager<TTimeTicker>, TickerManager<TTimeTicker, TCronTicker>>();
            services.AddSingleton<ICronTickerManager<TCronTicker>, TickerManager<TTimeTicker, TCronTicker>>();
            services.AddSingleton<IInternalTickerManager, InternalTickerManager<TTimeTicker, TCronTicker>>();
            services.AddSingleton<ITickerQRedisContext, NoOpTickerQRedisContext>();
            services.AddSingleton<ITickerPersistenceProvider<TTimeTicker, TCronTicker>, TickerInMemoryPersistenceProvider<TTimeTicker, TCronTicker>>();
            services.AddSingleton<ITickerQNotificationHubSender, NoOpTickerQNotificationHubSender>();
            services.AddSingleton<ITickerClock, TickerSystemClock>();
            services.AddSingleton<TickerQSchedulerBackgroundService>();
            services.AddSingleton<ITickerQHostScheduler>(provider => 
                provider.GetRequiredService<TickerQSchedulerBackgroundService>());
            services.AddHostedService(provider => 
                provider.GetRequiredService<TickerQSchedulerBackgroundService>());
            services.AddHostedService(provider => provider.GetRequiredService<TickerQFallbackBackgroundService>());
            services.AddSingleton<ITickerQInstrumentation, LoggerInstrumentation>();
            services.AddSingleton<TickerQFallbackBackgroundService>();
            services.AddSingleton<TickerExecutionTaskHandler>();
            services.AddSingleton(sp =>
            {
                var notification = sp.GetRequiredService<ITickerQNotificationHubSender>();
                var notifyDebounce = new SoftSchedulerNotifyDebounce((value) => notification.UpdateActiveThreads(value));
                return new TickerQTaskScheduler(schedulerOptionsBuilder.MaxConcurrency, schedulerOptionsBuilder.IdleWorkerTimeOut, notifyDebounce);
            });
            
            optionInstance.ExternalProviderConfigServiceAction?.Invoke(services);
            optionInstance.DashboardServiceAction?.Invoke(services);

            if (optionInstance.TickerExceptionHandlerType != null)
                services.AddSingleton(typeof(ITickerExceptionHandler), optionInstance.TickerExceptionHandlerType);
            
            services.AddSingleton(_ => optionInstance);
            services.AddSingleton(_ => tickerExecutionContext);
            services.AddSingleton(_ => schedulerOptionsBuilder);
            return services;
        }
        
        public static IApplicationBuilder UseTickerQ(this IApplicationBuilder app, TickerQStartMode qStartMode = TickerQStartMode.Immediate)
        {
            var serviceProvider = app.ApplicationServices;
            var tickerExecutionContext = serviceProvider.GetService<TickerExecutionContext>();
            var configuration = serviceProvider.GetService<IConfiguration>();
            var notificationHubSender = serviceProvider.GetService<ITickerQNotificationHubSender>();
            var backgroundScheduler = serviceProvider.GetService<TickerQSchedulerBackgroundService>();
            backgroundScheduler.SkipFirstRun = qStartMode == TickerQStartMode.Manual;
            
            TickerFunctionProvider.UpdateCronExpressionsFromIConfiguration(configuration);
            TickerFunctionProvider.Build();

            tickerExecutionContext.NotifyCoreAction += (value, type) =>
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
                tickerExecutionContext.ExternalProviderApplicationAction(serviceProvider);
                tickerExecutionContext.ExternalProviderApplicationAction = null;
            }
            else
                SeedDefinedCronTickers(serviceProvider).GetAwaiter().GetResult();
            
            if (tickerExecutionContext?.DashboardApplicationAction != null)
            {
                tickerExecutionContext.DashboardApplicationAction(app);
                tickerExecutionContext.DashboardApplicationAction = null;
            }

            return app;
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
