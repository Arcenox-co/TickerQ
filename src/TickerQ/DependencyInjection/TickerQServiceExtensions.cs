using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using TickerQ.BackgroundServices;
using TickerQ.Dispatcher;
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
            
            // Apply JSON serializer options for ticker requests if configured during service registration
            if (optionInstance.RequestJsonSerializerOptions != null)
            {
                TickerHelper.RequestJsonSerializerOptions = optionInstance.RequestJsonSerializerOptions;
            }
            
            // Configure whether ticker request payloads should use GZip compression
            TickerHelper.UseGZipCompression = optionInstance.RequestGZipCompressionEnabled;
            services.AddSingleton<ITimeTickerManager<TTimeTicker>, TickerManager<TTimeTicker, TCronTicker>>();
            services.AddSingleton<ICronTickerManager<TCronTicker>, TickerManager<TTimeTicker, TCronTicker>>();
            services.AddSingleton<IInternalTickerManager, InternalTickerManager<TTimeTicker, TCronTicker>>();
            services.AddSingleton<ITickerQRedisContext, NoOpTickerQRedisContext>();
            services.AddSingleton<ITickerQNotificationHubSender, NoOpTickerQNotificationHubSender>();
            services.AddSingleton<ITickerClock, TickerSystemClock>();
            
            // Only register background services if enabled (default is true)
            if (optionInstance.RegisterBackgroundServices)
            {
                services.AddSingleton<TickerQSchedulerBackgroundService>();
                services.AddSingleton<ITickerQHostScheduler>(provider => 
                    provider.GetRequiredService<TickerQSchedulerBackgroundService>());
                services.AddHostedService(provider => 
                    provider.GetRequiredService<TickerQSchedulerBackgroundService>());
                services.AddHostedService(provider => provider.GetRequiredService<TickerQFallbackBackgroundService>());
                services.AddSingleton<TickerQFallbackBackgroundService>();
                services.AddSingleton<ITickerQDispatcher, TickerQDispatcher>();
                services.AddSingleton<ITickerQTaskScheduler>(sp =>
                {
                    var notification = sp.GetRequiredService<ITickerQNotificationHubSender>();
                    var notifyDebounce = new SoftSchedulerNotifyDebounce((value) => notification.UpdateActiveThreads(value));
                    return new TickerQTaskScheduler(schedulerOptionsBuilder.MaxConcurrency, schedulerOptionsBuilder.IdleWorkerTimeOut, notifyDebounce);
                });
            }
            else
            {
                services.AddSingleton<ITickerQTaskScheduler>(_ => new TickerQTaskScheduler(schedulerOptionsBuilder.MaxConcurrency, schedulerOptionsBuilder.IdleWorkerTimeOut));
                // Register NoOp implementations when background services are disabled
                services.AddSingleton<ITickerQHostScheduler, NoOpTickerQHostScheduler>();
                services.AddSingleton<ITickerQDispatcher, NoOpTickerQDispatcher>();
            }
            services.AddSingleton<ITickerFunctionConcurrencyGate, TickerFunctionConcurrencyGate>();
            services.AddSingleton<ITickerExecutionTaskHandler, TickerExecutionTaskHandler>();
            services.AddSingleton<ITickerQInstrumentation, LoggerInstrumentation>();
            
            optionInstance.ExternalProviderConfigServiceAction?.Invoke(services);

            // Register in-memory persistence as fallback — only used when no external provider
            // (EF Core, Redis, SDK) has been configured via ExternalProviderConfigServiceAction above.
            services.TryAddSingleton<ITickerPersistenceProvider<TTimeTicker, TCronTicker>, TickerInMemoryPersistenceProvider<TTimeTicker, TCronTicker>>();

            optionInstance.DashboardServiceAction?.Invoke(services);

            if (optionInstance.TickerExceptionHandlerType != null)
                services.AddSingleton(typeof(ITickerExceptionHandler), optionInstance.TickerExceptionHandlerType);
            
            services.AddSingleton(_ => optionInstance);
            services.AddSingleton(_ => tickerExecutionContext);
            services.AddSingleton(_ => schedulerOptionsBuilder);
            return services;
        }
        
        /// <summary>
        /// Initializes TickerQ for generic host applications (Console, MAUI, WPF, Worker Services, etc.)
        /// </summary>
        public static IHost UseTickerQ(this IHost host, TickerQStartMode qStartMode = TickerQStartMode.Immediate)
        {
            InitializeTickerQ(host, qStartMode);
            return host;
        }
        
        private static void InitializeTickerQ(IHost host, TickerQStartMode qStartMode)
        {
            if (IsDesignTimeTool())
                return;

            var serviceProvider = host.Services;
            var tickerExecutionContext = serviceProvider.GetService<TickerExecutionContext>();
            var configuration = serviceProvider.GetService<IConfiguration>();
            var notificationHubSender = serviceProvider.GetService<ITickerQNotificationHubSender>();
            var backgroundScheduler = serviceProvider.GetService<TickerQSchedulerBackgroundService>();

            // If background services are registered, configure them
            if (backgroundScheduler != null)
            {
                backgroundScheduler.SkipFirstRun = qStartMode == TickerQStartMode.Manual;

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
            }
            // If background services are not registered (due to DisableBackgroundServices()),
            // silently skip background service configuration. This is expected behavior.

            if (tickerExecutionContext?.DashboardApplicationAction != null)
            {
                // Cast object back to IApplicationBuilder for Dashboard middleware
                tickerExecutionContext.DashboardApplicationAction(host);
                tickerExecutionContext.DashboardApplicationAction = null;
            }

            TickerFunctionProvider.UpdateCronExpressionsFromIConfiguration(configuration);
            TickerFunctionProvider.Build();

            // Run core seeding pipeline based on main options (works for both in-memory and EF providers).
            var options = tickerExecutionContext.OptionsSeeding;

            if (options == null || options.SeedDefinedCronTickers)
            {
                SeedDefinedCronTickers(serviceProvider).GetAwaiter().GetResult();
            }

            if (options?.TimeSeederAction != null)
            {
                options.TimeSeederAction(serviceProvider).GetAwaiter().GetResult();
            }

            if (options?.CronSeederAction != null)
            {
                options.CronSeederAction(serviceProvider).GetAwaiter().GetResult();
            }

            // Let external providers (e.g., EF Core) perform their own startup logic (dead-node cleanup, etc.).
            if (tickerExecutionContext.ExternalProviderApplicationAction != null)
            {
                tickerExecutionContext.ExternalProviderApplicationAction(serviceProvider);
                tickerExecutionContext.ExternalProviderApplicationAction = null;
            }
        }

        /// <summary>
        /// Detects whether the application is running inside a .NET design-time tool
        /// (e.g., dotnet-getdocument for OpenAPI generation, dotnet-ef for migrations).
        /// These tools invoke Program.Main to build the host but should not trigger
        /// runtime initialization like database seeding or background service startup.
        /// </summary>
        internal static bool IsDesignTimeTool()
        {
            var entryAssembly = Assembly.GetEntryAssembly();
            if (entryAssembly is null)
                return false;

            var entryName = entryAssembly.GetName().Name;
            if (entryName is null)
                return false;

            return entryName.StartsWith("dotnet-", StringComparison.OrdinalIgnoreCase);
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
