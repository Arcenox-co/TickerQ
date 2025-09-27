using Microsoft.Extensions.DependencyInjection;
using TickerQ.Dashboard.Endpoints;
using TickerQ.Dashboard.Hubs;
using TickerQ.Dashboard.Infrastructure.Dashboard;
using TickerQ.Utilities;
using TickerQ.Utilities.Interfaces;
using System;
using System.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TickerQ.Utilities.Entities;

namespace TickerQ.Dashboard.DependencyInjection
{
    public static class ServiceExtensions
    {
        public static TickerOptionsBuilder<TTimeTicker, TCronTicker> AddDashboard<TTimeTicker, TCronTicker>(this TickerOptionsBuilder<TTimeTicker, TCronTicker> tickerConfiguration, Action<DashboardOptionsBuilder> configureDashboard = null)
            where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
            where TCronTicker : CronTickerEntity, new()
        {
            var dashboardConfig = new DashboardOptionsBuilder
            {
                CorsOrigins = ["*"],
                EnableBuiltInAuth = true,
                UseHostAuthentication = false,
                EnableBasicAuth = false
            };
            
            configureDashboard?.Invoke(dashboardConfig);
            
            tickerConfiguration.DashboardServiceAction = (services) =>
            {
                services.AddScoped<ITickerDashboardRepository<TTimeTicker, TCronTicker>, TickerDashboardRepository<TTimeTicker, TCronTicker>>();
                services.Replace(ServiceDescriptor.Singleton(services.AddSingleton<ITickerQNotificationHubSender, TickerQNotificationHubSender>()));
                
                // Add authentication services if using host authentication
                if (dashboardConfig.UseHostAuthentication)
                {
                    // The host application should configure authentication services
                    // We just ensure they're available
                    // Check for specific authentication services instead of string matching
                    var hasAuthenticationService = services.Any(s => 
                        s.ServiceType == typeof(Microsoft.AspNetCore.Authentication.IAuthenticationService) ||
                        s.ServiceType.Name == "IAuthenticationSchemeProvider");
                        
                    if (!hasAuthenticationService)
                    {
                        services.AddAuthentication();
                        services.AddAuthorization();
                    }
                }
                services.AddDashboardService<TTimeTicker, TCronTicker>(dashboardConfig);
                services.AddSingleton<DashboardOptionsBuilder>(_ => dashboardConfig);
            };

            UseDashboardDelegate(tickerConfiguration, dashboardConfig);

            return tickerConfiguration;
        }

        private static void UseDashboardDelegate<TTimeTicker, TCronTicker>(this TickerOptionsBuilder<TTimeTicker, TCronTicker> tickerConfiguration, DashboardOptionsBuilder dashboardConfig)
            where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
            where TCronTicker : CronTickerEntity, new()
        {
            tickerConfiguration.UseDashboardApplication((app) =>
            {
                // Configure static files and middleware with endpoints
                app.UseDashboardWithEndpoints<TTimeTicker, TCronTicker>(dashboardConfig);
            });
        }
    }
}