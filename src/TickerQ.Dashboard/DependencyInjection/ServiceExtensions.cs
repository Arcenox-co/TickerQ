using Microsoft.Extensions.DependencyInjection;
using TickerQ.Dashboard.Endpoints;
using TickerQ.Dashboard.Hubs;
using TickerQ.Dashboard.Infrastructure;
using TickerQ.Dashboard.Infrastructure.Dashboard;
using TickerQ.Dashboard.Authentication;
using TickerQ.Utilities;
using TickerQ.Utilities.Interfaces;
using System;
using System.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
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

                // Register IStartupFilter for old Startup.cs pattern where IHost != IApplicationBuilder.
                // In the new WebApplication pattern, UseDashboardDelegate handles it directly.
                services.AddSingleton<IStartupFilter>(new DashboardStartupFilter<TTimeTicker, TCronTicker>(dashboardConfig));
            };

            UseDashboardDelegate(tickerConfiguration, dashboardConfig);

            return tickerConfiguration;
        }

        private static void UseDashboardDelegate<TTimeTicker, TCronTicker>(this TickerOptionsBuilder<TTimeTicker, TCronTicker> tickerConfiguration, DashboardOptionsBuilder dashboardConfig)
            where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
            where TCronTicker : CronTickerEntity, new()
        {
            tickerConfiguration.UseDashboardApplication((appObj) =>
            {
                if (appObj is IApplicationBuilder app)
                {
                    // New WebApplication pattern: WebApplication implements both IHost and IApplicationBuilder.
                    // Mark as applied so DashboardStartupFilter skips duplicate registration.
                    dashboardConfig.MiddlewareApplied = true;
                    app.UseDashboardWithEndpoints<TTimeTicker, TCronTicker>(dashboardConfig);
                }
                // Old Startup.cs pattern: IHost is not IApplicationBuilder.
                // Dashboard middleware is injected via IStartupFilter registered in AddDashboard.
            });
        }
    }
}