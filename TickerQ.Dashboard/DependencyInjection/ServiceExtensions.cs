using Microsoft.Extensions.DependencyInjection;
using TickerQ.Dashboard.Hubs;
using TickerQ.Utilities;
using TickerQ.Utilities.Interfaces;

namespace TickerQ.Dashboard.DependencyInjection
{
    public static class ServiceExtensions
    {
        public static TickerOptionsBuilder AddDashboardBasicAuth(this TickerOptionsBuilder builder)
        {
            builder.EnableBasicAuth = true;
            return builder;
        }
        
        public static TickerOptionsBuilder AddDashboard(this TickerOptionsBuilder tickerConfiguration,
            string basePath = "/tickerq-dashboard")
        {
            tickerConfiguration.DashboardLunchUrl = basePath;
            tickerConfiguration.DashboardServiceAction = (services) =>
            {
                services.AddSingleton<ITickerQNotificationHubSender, TickerQNotificationHubSender>();
                
#if NETCOREAPP3_1_OR_GREATER
                NetTargetV3Higher.AddDashboardService(services);
#else
                NetTargetV31Lower.AddDashboardService(services);
#endif
            };

            UseDashboardDelegate(tickerConfiguration);

            return tickerConfiguration;
        }

        private static void UseDashboardDelegate(this TickerOptionsBuilder tickerConfiguration)
        {
            
            tickerConfiguration.DashboardApplicationAction = (app, basePath) =>
            {
                TickerOptionsBuilder.NotifyThreadCountFunc = (int threadCount) =>
                {
                     var notificationSender = app.ApplicationServices.GetService<ITickerQNotificationHubSender>();

                     notificationSender?.UpdateActiveThreads(threadCount);
                };
#if NETCOREAPP3_1_OR_GREATER
                NetTargetV3Higher.UseDashboard(app, basePath);
#else
                NetTargetV31Lower.UseDashboard(app, basePath);
#endif
            };
        }
    }
}