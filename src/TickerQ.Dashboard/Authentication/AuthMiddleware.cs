using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace TickerQ.Dashboard.Authentication;

/// <summary>
/// Gates the dashboard's <c>/api/*</c> endpoints. Static assets, the SignalR
/// negotiate, and the public auth endpoints (<c>/api/auth/info</c>,
/// <c>/login</c>, <c>/refresh</c>, <c>/logout</c>, <c>/validate</c>) bypass
/// the check. When no scheme accepts the request, every registered scheme
/// gets to contribute a <c>WWW-Authenticate</c> value — they stack as
/// multiple headers in the same 401, letting browsers pick Basic while
/// curl picks Bearer.
/// </summary>
public class AuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<AuthMiddleware> _logger;

    public AuthMiddleware(RequestDelegate next, ILogger<AuthMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value?.ToLower() ?? "";

        if (IsExcludedPath(path))
        {
            await _next(context);
            return;
        }

        if (!path.StartsWith("/api/"))
        {
            await _next(context);
            return;
        }

        var authService = context.RequestServices.GetRequiredService<IAuthService>();
        var authResult = await authService.AuthenticateAsync(context);

        if (!authResult.IsAuthenticated)
        {
            _logger.LogWarning("Authentication failed for {Path}: {Error}", path, authResult.ErrorMessage);
            context.RequestServices
                .GetService<Infrastructure.Metrics.ITickerQDashboardMetrics>()?
                .AuthenticationFailed(path);

            // Each registered scheme contributes a WWW-Authenticate value (or
            // null). Multiple values in one response is RFC-7235 compliant —
            // browsers honour the Basic prompt, curl picks Bearer, etc.
            var config = context.RequestServices.GetRequiredService<AuthConfig>();
            var challengeValues = config.Schemes
                .Select(s => s.GetChallengeHeader(context))
                .Where(h => !string.IsNullOrEmpty(h))
                .Select(h => h!)
                .ToArray();
            if (challengeValues.Length > 0)
            {
                context.Response.Headers["WWW-Authenticate"] = new StringValues(challengeValues);
            }

            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Unauthorized");
            return;
        }

        context.Items["auth.username"] = authResult.Username;
        context.Items["auth.authenticated"] = true;

        await _next(context);
    }

    private static bool IsExcludedPath(string path)
    {
        return path.Contains("/assets/") ||
               path.EndsWith(".js") ||
               path.EndsWith(".css") ||
               path.EndsWith(".ico") ||
               path.EndsWith(".png") ||
               path.EndsWith(".jpg") ||
               path.EndsWith(".svg") ||
               path.Contains("/negotiate") ||
               path == "/api/auth/validate" ||
               path == "/api/auth/info" ||
               path == "/api/auth/login" ||
               path == "/api/auth/refresh" ||
               path == "/api/auth/logout";
    }
}
