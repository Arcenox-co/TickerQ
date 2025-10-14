using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace TickerQ.Dashboard.Authentication;

/// <summary>
/// Simple authentication middleware that only protects API endpoints
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

        // Skip authentication for:
        // 1. Static files (handled by static files middleware)
        // 2. SignalR negotiate endpoint (anonymous by design)
        // 3. Auth validation endpoint (to avoid circular dependency)
        if (IsExcludedPath(path))
        {
            await _next(context);
            return;
        }

        // Only protect API endpoints
        if (!path.StartsWith("/api/"))
        {
            await _next(context);
            return;
        }

        // Resolve auth service from request scope
        var authService = context.RequestServices.GetRequiredService<IAuthService>();
        
        // Authenticate the request
        var authResult = await authService.AuthenticateAsync(context);
        
        if (!authResult.IsAuthenticated)
        {
            _logger.LogWarning("Authentication failed for {Path}: {Error}", path, authResult.ErrorMessage);
            context.Response.StatusCode = 401;
            await context.Response.WriteAsync("Unauthorized");
            return;
        }

        // Set user information for downstream middleware
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
               path == "/api/auth/info";
    }
}
