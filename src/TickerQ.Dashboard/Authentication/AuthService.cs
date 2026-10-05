using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace TickerQ.Dashboard.Authentication;

/// <summary>
/// Iterates the configured <see cref="Schemes.IAuthScheme"/> list and returns
/// the first match. Schemes own their own request-parsing rules; this service
/// has no awareness of headers, cookies, or query strings — it just picks the
/// winner.
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
            // No auth configured — every request is "anonymous, allowed".
            if (!_config.IsEnabled) return AuthResult.Success("anonymous");

            AuthResult lastFailure = AuthResult.Failure("No authentication scheme matched");
            foreach (var scheme in _config.Schemes)
            {
                var result = await scheme.TryAuthenticateAsync(context);
                if (result.IsAuthenticated) return result;
                lastFailure = result;
            }

            return lastFailure;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Authentication error");
            return AuthResult.Failure("Authentication error");
        }
    }

    public AuthInfo GetAuthInfo() => new()
    {
        Mode = _config.Mode,
        IsEnabled = _config.IsEnabled,
        SessionTimeoutMinutes = _config.SessionTimeoutMinutes,
    };
}
