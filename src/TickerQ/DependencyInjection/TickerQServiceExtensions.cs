using System;
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

            // Register the initializer hosted service BEFORE scheduler services
            // to guarantee seeding completes before the scheduler starts polling.
            // Registered as a singleton so UseTickerQ can resolve it to set the initialization flag.
            services.AddSingleton<TickerQInitializerHostedService>();
            services.AddHostedService(sp => sp.GetRequiredService<TickerQInitializerHostedService>());

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

            // Register AFTER initializer and scheduler to ensure it runs last
            services.AddHostedService<TickerQStartupValidator>();

            return services;
        }

        /// <summary>
        /// Initializes TickerQ for generic host applications (Console, MAUI, WPF, Worker Services, etc.).
        ///
        /// This method configures middleware and scheduler settings. All I/O-bound
        /// initialization (function discovery, database seeding, external provider startup)
        /// is deferred to <see cref="TickerQInitializerHostedService"/> which runs when
        /// the host starts. This means design-time tools that build the host without
        /// starting it (OpenAPI generators, EF migration tools, etc.) will not trigger
        /// database operations.
        /// </summary>
        public static IHost UseTickerQ(this IHost host, TickerQStartMode qStartMode = TickerQStartMode.Immediate)
        {
            var serviceProvider = host.Services;
            var tickerExecutionContext = serviceProvider.GetService<TickerExecutionContext>();
            var notificationHubSender = serviceProvider.GetService<ITickerQNotificationHubSender>();
            var backgroundScheduler = serviceProvider.GetService<TickerQSchedulerBackgroundService>();

            // Signal the initializer hosted service that UseTickerQ was called,
            // so it should perform startup I/O when the host starts.
            var initializer = serviceProvider.GetService<TickerQInitializerHostedService>();
            if (initializer != null)
                initializer.InitializationRequested = true;

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

            return host;
        }
    }
}
