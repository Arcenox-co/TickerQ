using System;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;
using TickerQ.Dashboard.Authentication;

namespace TickerQ.Dashboard;

public class DashboardOptionsBuilder
{
    internal string BasePath { get; set; } = "/";
    internal Action<CorsPolicyBuilder> CorsPolicyBuilder { get; set; }
    internal string BackendDomain { get; set; }
    
    // Clean authentication system
    internal AuthConfig Auth { get; set; } = new();
    
    // Custom Middleware Integration
    public Action<IApplicationBuilder> CustomMiddleware { get; set; }
    public Action<IApplicationBuilder> PreDashboardMiddleware { get; set; }
    public Action<IApplicationBuilder> PostDashboardMiddleware { get; set; }
    
    public void SetCorsPolicy(Action<CorsPolicyBuilder> corsPolicyBuilder)
        => CorsPolicyBuilder = corsPolicyBuilder;
    
    public void SetBasePath(string basePath)
        => BasePath = basePath;
    
    public void SetBackendDomain(string backendDomain)
        => BackendDomain = backendDomain;
    
    /// <summary>Configure no authentication (public dashboard)</summary>
    public DashboardOptionsBuilder WithNoAuth()
    {
        Auth.Mode = AuthMode.None;
        return this;
    }

    /// <summary>Enable Basic Authentication with username/password</summary>
    public DashboardOptionsBuilder WithBasicAuth(string username, string password)
    {
        Auth.Mode = AuthMode.Basic;
        Auth.BasicCredentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
        return this;
    }
    
    /// <summary>Enable API Key authentication (sent as Bearer token)</summary>
    public DashboardOptionsBuilder WithApiKey(string apiKey)
    {
        Auth.Mode = AuthMode.ApiKey;
        Auth.ApiKey = apiKey;
        return this;
    }
    
    /// <summary>Use the host application's existing authentication system</summary>
    public DashboardOptionsBuilder WithHostAuthentication()
    {
        Auth.Mode = AuthMode.Host;
        return this;
    }
    
    /// <summary>Configure custom authentication with validation function</summary>
    public DashboardOptionsBuilder WithCustomAuth(Func<string, bool> validator)
    {
        Auth.Mode = AuthMode.Custom;
        Auth.CustomValidator = validator;
        return this;
    }
    
    /// <summary>Set session timeout in minutes</summary>
    public DashboardOptionsBuilder WithSessionTimeout(int minutes)
    {
        Auth.SessionTimeoutMinutes = minutes;
        return this;
    }
    
    /// <summary>Validate the authentication configuration</summary>
    internal void Validate()
    {
        Auth.Validate();
    }
}