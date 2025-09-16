using System;
using Microsoft.AspNetCore.Builder;
using TickerQ.Utilities.Entities;

namespace TickerQ.Dashboard;

public class DashboardOptionsBuilder
{
    public string BasePath { get; set; } = "/tickerq-dashboard";
    public string[] CorsOrigins { get; set; } = ["*"];
        
    // Backend API Configuration
    public string BackendDomain { get; set; }
        
    // Authentication Integration
    public bool EnableBuiltInAuth { get; set; } = true;
    public bool UseHostAuthentication { get; set; }
    public string[] RequiredRoles { get; set; } = [];
    public string[] RequiredPolicies { get; set; } = [];
        
    // Basic Auth Configuration
    public bool EnableBasicAuth { get; set; } = false;
        
    // Custom Middleware Integration
    public Action<IApplicationBuilder> CustomMiddleware { get; set; }
    public Action<IApplicationBuilder> PreDashboardMiddleware { get; set; }
    public Action<IApplicationBuilder> PostDashboardMiddleware { get; set; }
    public bool UseHostMiddleware { get; set; } = false;
}