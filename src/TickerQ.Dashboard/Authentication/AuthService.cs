using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace TickerQ.Dashboard.Authentication;

/// <summary>
/// Clean, simple authentication service
/// </summary>
public class AuthService : IAuthService
{
    private readonly AuthConfig _config;
    private readonly ILogger<AuthService> _logger;

    public AuthService(AuthConfig config, ILogger<AuthService> logger)
    {
        _config = config;
        _logger = logger;
        _config.Validate();
    }

    public async Task<AuthResult> AuthenticateAsync(HttpContext context)
    {
        try
        {
            // No authentication required
            if (!_config.IsEnabled)
            {
                return AuthResult.Success("anonymous");
            }

            // Authentication performed by host application
            if (_config.Mode == AuthMode.Host)
            {
                return await AuthenticateHostAsync(context);
            }

            // Get authorization header or query parameter
            var authHeader = GetAuthorizationValue(context);
            if (string.IsNullOrEmpty(authHeader))
            {
                return AuthResult.Failure("No authorization provided");
            }

            // Authenticate based on mode
            return _config.Mode switch
            {
                AuthMode.Basic => await AuthenticateBasicAsync(authHeader),
                AuthMode.ApiKey => await AuthenticateApiKeyAsync(authHeader),
                AuthMode.Custom => await AuthenticateCustomAsync(authHeader),
                _ => AuthResult.Failure("Invalid authentication mode")
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Authentication error");
            return AuthResult.Failure("Authentication error");
        }
    }

    public AuthInfo GetAuthInfo()
    {
        return new AuthInfo
        {
            Mode = _config.Mode,
            IsEnabled = _config.IsEnabled,
            SessionTimeoutMinutes = _config.SessionTimeoutMinutes
        };
    }

    private string? GetAuthorizationValue(HttpContext context)
    {
        // Try Authorization header first
        var authHeader = context.Request.Headers.Authorization.FirstOrDefault();
        if (!string.IsNullOrEmpty(authHeader))
        {
            return authHeader;
        }

        // Try access_token query parameter (for SignalR WebSocket)
        var accessToken = context.Request.Query["access_token"].FirstOrDefault();
        if (!string.IsNullOrEmpty(accessToken))
        {
            return accessToken;
        }

        return null;
    }

    private Task<AuthResult> AuthenticateBasicAsync(string authHeader)
    {
        try
        {
            // Handle both "Basic <credentials>" and raw credentials
            var credentials = authHeader.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase)
                ? authHeader.Substring(6)
                : authHeader;

            if (credentials == _config.BasicCredentials)
            {
                // Decode to get username for display
                var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(credentials));
                var username = decoded.Split(':')[0];
                return Task.FromResult(AuthResult.Success(username));
            }

            return Task.FromResult(AuthResult.Failure("Invalid credentials"));
        }
        catch
        {
            return Task.FromResult(AuthResult.Failure("Invalid basic auth format"));
        }
    }

    private Task<AuthResult> AuthenticateApiKeyAsync(string authHeader)
    {
        try
        {
            // Handle both "Bearer <token>", "Bearer:<token>", and raw token
            var token = authHeader switch
            {
                _ when authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) => authHeader[7..],
                _ when authHeader.StartsWith("Bearer:", StringComparison.OrdinalIgnoreCase) => authHeader[7..],
                _ => authHeader
            };

            if (token == _config.ApiKey)
            {
                return Task.FromResult(AuthResult.Success("api-user"));
            }

            return Task.FromResult(AuthResult.Failure("Invalid token"));
        }
        catch
        {
            return Task.FromResult(AuthResult.Failure("Invalid bearer token format"));
        }
    }

    private Task<AuthResult> AuthenticateCustomAsync(string authHeader)
    {
        try
        {
            if (_config.CustomValidator?.Invoke(authHeader) == true)
            {
                return Task.FromResult(AuthResult.Success("custom-user"));
            }

            return Task.FromResult(AuthResult.Failure("Custom authentication failed"));
        }
        catch
        {
            return Task.FromResult(AuthResult.Failure("Custom authentication error"));
        }
    }

    private async Task<AuthResult> AuthenticateHostAsync(HttpContext context)
    {
        if (!string.IsNullOrEmpty(_config.HostAuthorizationPolicy))
        {
            var authorizationService = context.RequestServices.GetRequiredService<Microsoft.AspNetCore.Authorization.IAuthorizationService>();
            var authResult = await authorizationService.AuthorizeAsync(context.User, null, _config.HostAuthorizationPolicy);
            if (!authResult.Succeeded)
            {
                return AuthResult.Failure("Host authorization policy not satisfied");
            }

            return AuthResult.Success(context.User.Identity?.Name ?? "host-user");
        }

        // Delegate to host application's authentication
        if (context.User?.Identity?.IsAuthenticated == true)
        {
            var username = context.User.Identity.Name ?? "host-user";
            return AuthResult.Success(username);
        }

        return AuthResult.Failure("Host authentication required");
    }
}
