using System;

namespace TickerQ.Dashboard.Authentication;

/// <summary>
/// Authentication configuration for TickerQ Dashboard
/// </summary>
public class AuthConfig
{
    /// <summary>
    /// Authentication mode
    /// </summary>
    public AuthMode Mode { get; set; } = AuthMode.None;
    
    /// <summary>
    /// Basic authentication credentials (Base64 encoded username:password)
    /// </summary>
    public string? BasicCredentials { get; set; }
    
    /// <summary>
    /// API key for authentication (sent as Bearer token)
    /// </summary>
    public string? ApiKey { get; set; }
    
    /// <summary>
    /// Custom authentication function
    /// </summary>
    public Func<string, bool>? CustomValidator { get; set; }
    
    /// <summary>
    /// Session timeout in minutes (default: 60 minutes)
    /// </summary>
    public int SessionTimeoutMinutes { get; set; } = 60;
    
    /// <summary>
    /// Whether authentication is enabled
    /// </summary>
    public bool IsEnabled => Mode != AuthMode.None;
    
    /// <summary>
    /// Validate the configuration
    /// </summary>
    public void Validate()
    {
        switch (Mode)
        {
            case AuthMode.Basic when string.IsNullOrEmpty(BasicCredentials):
                throw new InvalidOperationException("BasicCredentials is required for Basic authentication mode");
            case AuthMode.ApiKey when string.IsNullOrEmpty(ApiKey):
                throw new InvalidOperationException("ApiKey is required for ApiKey authentication mode");
            case AuthMode.Custom when CustomValidator == null:
                throw new InvalidOperationException("CustomValidator is required for Custom authentication mode");
        }
    }
}

/// <summary>
/// Authentication modes supported by TickerQ Dashboard
/// </summary>
public enum AuthMode
{
    /// <summary>
    /// No authentication - public dashboard
    /// </summary>
    None = 0,
    
    /// <summary>
    /// Basic authentication with username/password
    /// </summary>
    Basic = 1,
    
    /// <summary>
    /// API key authentication (sent as Bearer token)
    /// </summary>
    ApiKey = 2,
    
    /// <summary>
    /// Use host application's authentication
    /// </summary>
    Host = 3,
    
    /// <summary>
    /// Custom authentication function
    /// </summary>
    Custom = 4
}
