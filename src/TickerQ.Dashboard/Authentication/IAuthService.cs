using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace TickerQ.Dashboard.Authentication;

/// <summary>
/// Authentication service interface
/// </summary>
public interface IAuthService
{
    /// <summary>
    /// Check if the request is authenticated
    /// </summary>
    Task<AuthResult> AuthenticateAsync(HttpContext context);
    
    /// <summary>
    /// Get authentication configuration for frontend
    /// </summary>
    AuthInfo GetAuthInfo();
}

/// <summary>
/// Authentication result
/// </summary>
public class AuthResult
{
    public bool IsAuthenticated { get; set; }
    public string? Username { get; set; }
    public string? ErrorMessage { get; set; }
    
    public static AuthResult Success(string? username = null) => new()
    {
        IsAuthenticated = true,
        Username = username ?? "user"
    };
    
    public static AuthResult Failure(string? errorMessage = null) => new()
    {
        IsAuthenticated = false,
        ErrorMessage = errorMessage ?? "Authentication failed"
    };
}

/// <summary>
/// Authentication information for frontend
/// </summary>
public class AuthInfo
{
    public AuthMode Mode { get; set; }
    public bool IsEnabled { get; set; }
    public int SessionTimeoutMinutes { get; set; }
}
