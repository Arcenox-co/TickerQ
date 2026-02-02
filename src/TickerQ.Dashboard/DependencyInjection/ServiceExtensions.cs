using Microsoft.Extensions.DependencyInjection;
using TickerQ.Dashboard.Endpoints;
using TickerQ.Dashboard.Hubs;
using TickerQ.Dashboard.Infrastructure.Dashboard;
using TickerQ.Dashboard.Authentication;
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
                CorsPolicyBuilder = cors => cors
                    .SetIsOriginAllowed(_ => true)
                    .AllowAnyHeader()
                    .AllowCredentials()
            };
            
            configureDashboard?.Invoke(dashboardConfig);
            
            tickerConfiguration.DashboardServiceAction = (services) =>
            {
                services.AddScoped<ITickerDashboardRepository<TTimeTicker, TCronTicker>, TickerDashboardRepository<TTimeTicker, TCronTicker>>();
                services.Replace(ServiceDescriptor.Singleton(services.AddSingleton<ITickerQNotificationHubSender, TickerQNotificationHubSender>()));
                
                // Validate configuration
                dashboardConfig.Validate();
                
                // Register authentication system
                services.AddSingleton(dashboardConfig.Auth);
                services.AddScoped<IAuthService, AuthService>();
                
                // Add authentication services if using host authentication
                if (dashboardConfig.Auth.Mode == AuthMode.Host)
                {
                    // The host application should configure authentication services
                    // We just ensure they're available
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
                ((IApplicationBuilder)app).UseDashboardWithEndpoints<TTimeTicker, TCronTicker>(dashboardConfig);
            });
        }
    }
}