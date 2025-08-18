using Microsoft.Extensions.DependencyInjection;
using TickerQ.Dashboard.Hubs;
using TickerQ.Dashboard.Infrastructure.Dashboard;
using TickerQ.Utilities;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models.Ticker;
using System.Text.Json;
using System.Text.Json.Serialization;
using System;
using System.Linq;
using Microsoft.AspNetCore.Builder;

namespace TickerQ.Dashboard.DependencyInjection
{
    public class DashboardConfiguration
    {
        public string BasePath { get; set; } = "/tickerq-dashboard";
        public string[] CorsOrigins { get; set; } = new[] { "*" };
        
        // Backend API Configuration
        public string BackendDomain { get; set; }
        
        public JsonSerializerOptions JsonSerializerOptions { get; set; } = new JsonSerializerOptions()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() }
        };
        
        // Authentication Integration
        public bool EnableBuiltInAuth { get; set; } = true;
        public bool UseHostAuthentication { get; set; } = false;
        public string[] RequiredRoles { get; set; } = Array.Empty<string>();
        public string[] RequiredPolicies { get; set; } = Array.Empty<string>();
        
        // Basic Auth Configuration
        public bool EnableBasicAuth { get; set; } = false;
        
        // Custom Middleware Integration
        public Action<IApplicationBuilder> CustomMiddleware { get; set; }
        public Action<IApplicationBuilder> PreDashboardMiddleware { get; set; }
        public Action<IApplicationBuilder> PostDashboardMiddleware { get; set; }
        public bool UseHostMiddleware { get; set; } = false;
    }

    public static class ServiceExtensions
    {
        public static TickerOptionsBuilder AddDashboard(this TickerOptionsBuilder tickerConfiguration,
            Action<DashboardConfiguration> configureDashboard = null)
        {
            var dashboardConfig = new DashboardConfiguration
            {
                CorsOrigins = new[] { "*" },
                JsonSerializerOptions = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    Converters = { new JsonStringEnumConverter() }
                },
                EnableBuiltInAuth = true,
                UseHostAuthentication = false,
                EnableBasicAuth = false // Default to false, must be explicitly enabled
            };
            
            configureDashboard?.Invoke(dashboardConfig);
            
            // Set the basic auth flag on the ticker configuration if enabled
            if (dashboardConfig.EnableBasicAuth)
            {
                tickerConfiguration.EnableBasicAuth = true;
            }
            
            tickerConfiguration.DashboardLunchUrl = dashboardConfig.BasePath;
            
            tickerConfiguration.DashboardServiceAction = (services) =>
            {
                services.AddScoped<ITickerDashboardRepository, TickerDashboardRepository<TimeTicker, CronTicker>>();
                services.AddSingleton<ITickerQNotificationHubSender, TickerQNotificationHubSender>();
                
                
                
#if NETCOREAPP3_1_OR_GREATER
                // Add authentication services if using host authentication
                if (dashboardConfig.UseHostAuthentication)
                {
                    // The host application should configure authentication services
                    // We just ensure they're available
                    if (!services.Any(s => s.ServiceType.Name.Contains("Authentication")))
                    {
                        services.AddAuthentication();
                        services.AddAuthorization();
                    }
                }
                NetTargetV3Higher.AddDashboardService(services, dashboardConfig);
#else
// Add authentication services if using host authentication
                if (dashboardConfig.UseHostAuthentication)
                {
                    // The host application should configure authentication services
                    // We just ensure they're available
                    if (!services.Any(s => s.ServiceType.Name.Contains("Authentication")))
                    {
                        services.AddAuthenticationCore();
                        services.AddAuthorization();
                    }
                }
                NetTargetV31Lower.AddDashboardService(services, dashboardConfig);
#endif
            };

            UseDashboardDelegate(tickerConfiguration, dashboardConfig);

            return tickerConfiguration;
        }

        private static void UseDashboardDelegate(this TickerOptionsBuilder tickerConfiguration, DashboardConfiguration dashboardConfig)
        {
            
            tickerConfiguration.DashboardApplicationAction = (app, basePath) =>
            {
                TickerOptionsBuilder.NotifyThreadCountFunc = (int threadCount) =>
                {
                     var notificationSender = app.ApplicationServices.GetService<ITickerQNotificationHubSender>();

                     notificationSender?.UpdateActiveThreads(threadCount);
                };
                
                // Execute pre-dashboard middleware
                dashboardConfig.PreDashboardMiddleware?.Invoke(app);
                
#if NETCOREAPP3_1_OR_GREATER
                NetTargetV3Higher.UseDashboard(app, basePath, dashboardConfig);
#else
                NetTargetV31Lower.UseDashboard(app, basePath, dashboardConfig);
#endif
                
                // Execute post-dashboard middleware
                dashboardConfig.PostDashboardMiddleware?.Invoke(app);
            };
        }
    }
}