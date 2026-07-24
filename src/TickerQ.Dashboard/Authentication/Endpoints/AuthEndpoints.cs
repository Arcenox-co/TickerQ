using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using TickerQ.Dashboard.Authentication.Jwt;
using TickerQ.Dashboard.Authentication.Schemes;
using TickerQ.Dashboard.Infrastructure;

namespace TickerQ.Dashboard.Authentication.Endpoints;

/// <summary>
/// Maps the credential-based auth endpoints — <c>POST /api/auth/login</c>,
/// <c>POST /api/auth/refresh</c>, <c>POST /api/auth/logout</c>. Only mounted
/// when a credential-issuing scheme (JWT, Cookie) is configured.
/// </summary>
/// <remarks>
/// JWT and Cookie schemes share one <see cref="JwtTokenIssuer"/> — same
/// signing key, same iss/aud — so a single login can produce both a Bearer
/// token (JSON body) AND a Set-Cookie header. The SPA picks whichever
/// channel its configuration uses; headless clients ignore the cookie.
/// </remarks>
internal static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder endpoints, AuthConfig config)
    {
        var hasCredentialScheme = config.Schemes.Any(s =>
            s is JwtBearerScheme or CookieAuthScheme);
        if (!hasCredentialScheme) return;

        endpoints.MapPost("/api/auth/login", (Delegate)LoginAsync)
            .WithName("Login")
            .WithTags("TickerQ Dashboard")
            .RequireCors("TickerQ_Dashboard_CORS")
            .AllowAnonymous();

        endpoints.MapPost("/api/auth/refresh", (Delegate)RefreshAsync)
            .WithName("RefreshToken")
            .WithTags("TickerQ Dashboard")
            .RequireCors("TickerQ_Dashboard_CORS")
            .AllowAnonymous();

        endpoints.MapPost("/api/auth/logout", (Delegate)LogoutAsync)
            .WithName("Logout")
            .WithTags("TickerQ Dashboard")
            .RequireCors("TickerQ_Dashboard_CORS")
            .AllowAnonymous();
    }

    private static async Task<IResult> LoginAsync(HttpContext ctx)
    {
        var config = ctx.RequestServices.GetRequiredService<AuthConfig>();
        var (issuer, users, lifetimeSeconds) = ResolveIssuer(config);
        if (issuer == null || users == null)
            return Results.Problem("Credential auth is not configured.", statusCode: StatusCodes.Status500InternalServerError);

        // Brute-force brake, keyed by client IP. Applied before touching the
        // user store so blocked clients cost nothing.
        var clientKey = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        if (LoginRateLimiter.IsBlocked(clientKey, out var retryAfter))
        {
            ctx.RequestServices.GetService<Infrastructure.Metrics.ITickerQDashboardMetrics>()?.LoginThrottled();
            ctx.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();
            return Results.StatusCode(StatusCodes.Status429TooManyRequests);
        }

        var body = await ReadJsonAsync<LoginRequest>(ctx);
        if (body == null || string.IsNullOrEmpty(body.Username) || string.IsNullOrEmpty(body.Password))
            return Results.BadRequest(new { error = "Username and password are required." });

        if (!users.Validate(body.Username, body.Password))
        {
            LoginRateLimiter.RecordFailure(clientKey);
            return Results.Unauthorized();
        }

        LoginRateLimiter.RecordSuccess(clientKey);
        var token = issuer.IssueAccessToken(body.Username);
        SetAuthCookieIfConfigured(ctx, config, token);

        var response = new LoginResponse
        {
            // Bearer-scheme clients use this header; cookie-only clients can ignore it.
            AccessToken = HasBearerScheme(config) ? token : string.Empty,
            TokenType = "Bearer",
            ExpiresIn = lifetimeSeconds,
            Username = body.Username,
        };

        return WriteJson(ctx, response, StatusCodes.Status200OK);
    }

    private static async Task<IResult> RefreshAsync(HttpContext ctx)
    {
        var config = ctx.RequestServices.GetRequiredService<AuthConfig>();
        var (issuer, _, lifetimeSeconds) = ResolveIssuer(config);
        if (issuer == null)
            return Results.Problem("Credential auth is not configured.", statusCode: StatusCodes.Status500InternalServerError);

        // Reuse a still-valid token from EITHER the Authorization header (bearer
        // clients) OR the auth cookie (browser sessions). No persistent refresh-
        // token store — expired tokens require a fresh login.
        var current = ReadBearerHeader(ctx) ?? ReadAuthCookie(ctx, config);
        if (string.IsNullOrEmpty(current) || !issuer.TryValidate(current, out var username))
            return Results.Unauthorized();

        var token = issuer.IssueAccessToken(username);
        SetAuthCookieIfConfigured(ctx, config, token);

        var response = new LoginResponse
        {
            AccessToken = HasBearerScheme(config) ? token : string.Empty,
            TokenType = "Bearer",
            ExpiresIn = lifetimeSeconds,
            Username = username,
        };

        return WriteJson(ctx, response, StatusCodes.Status200OK);
    }

    private static Task<IResult> LogoutAsync(HttpContext ctx)
    {
        var config = ctx.RequestServices.GetRequiredService<AuthConfig>();
        ClearAuthCookieIfConfigured(ctx, config);
        // Bearer tokens are stateless — the SPA drops them from memory on
        // logout. Nothing to do server-side.
        return Task.FromResult(Results.NoContent());
    }

    // ───────── helpers ─────────

    private static (JwtTokenIssuer? issuer, IUserStore? users, int lifetimeSeconds) ResolveIssuer(AuthConfig config)
    {
        // Both schemes share the same JwtTokenIssuer singleton (see
        // ServiceExtensions.BuildJwtIssuer). Either scheme's bound Issuer is fine.
        var jwtScheme = config.Schemes.OfType<JwtBearerScheme>().FirstOrDefault();
        var cookieScheme = config.Schemes.OfType<CookieAuthScheme>().FirstOrDefault();

        var issuer = jwtScheme?.Issuer ?? cookieScheme?.Issuer;
        // Bearer's user store takes priority when both schemes share one DSL block;
        // otherwise fall back to the cookie scheme's store.
        var users = jwtScheme?.Users ?? cookieScheme?.Users;
        var lifetime = (int)(config.JwtBearerOptions?.AccessTokenLifetime.TotalSeconds
                              ?? config.CookieAuthOptions?.SessionLifetime.TotalSeconds
                              ?? 3600);
        return (issuer, users, lifetime);
    }

    private static bool HasBearerScheme(AuthConfig config)
        => config.Schemes.Any(s => s is JwtBearerScheme);

    private static string? ReadBearerHeader(HttpContext ctx)
    {
        var header = ctx.Request.Headers.Authorization.FirstOrDefault();
        if (string.IsNullOrEmpty(header)) return null;
        return header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? header[7..] : header;
    }

    private static string? ReadAuthCookie(HttpContext ctx, AuthConfig config)
    {
        var cookie = config.Schemes.OfType<CookieAuthScheme>().FirstOrDefault();
        if (cookie == null) return null;
        return ctx.Request.Cookies.TryGetValue(cookie.CookieName, out var value) ? value : null;
    }

    private static void SetAuthCookieIfConfigured(HttpContext ctx, AuthConfig config, string token)
    {
        var cookieScheme = config.Schemes.OfType<CookieAuthScheme>().FirstOrDefault();
        var options = config.CookieAuthOptions;
        if (cookieScheme == null || options == null) return;

        // PathBase covers the dashboard's mount point (e.g. "/tickerq/dashboard")
        // so the cookie scopes to the dashboard only — host pages above the
        // dashboard never see it.
        var pathBase = ctx.Request.PathBase.HasValue ? ctx.Request.PathBase.Value : "/";

        ctx.Response.Cookies.Append(cookieScheme.CookieName, token, new CookieOptions
        {
            HttpOnly = true,
            Secure = options.SecureCookie,
            SameSite = SameSiteMode.Strict,
            Path = pathBase,
            Expires = DateTimeOffset.UtcNow.Add(options.SessionLifetime),
        });
    }

    private static void ClearAuthCookieIfConfigured(HttpContext ctx, AuthConfig config)
    {
        var cookieScheme = config.Schemes.OfType<CookieAuthScheme>().FirstOrDefault();
        if (cookieScheme == null) return;

        var pathBase = ctx.Request.PathBase.HasValue ? ctx.Request.PathBase.Value : "/";
        ctx.Response.Cookies.Delete(cookieScheme.CookieName, new CookieOptions
        {
            Path = pathBase,
            Secure = config.CookieAuthOptions?.SecureCookie ?? true,
            SameSite = SameSiteMode.Strict,
        });
    }

    private static async Task<T?> ReadJsonAsync<T>(HttpContext ctx) where T : class
    {
        try
        {
            var typeInfo = DashboardJsonSerializerContext.Default.GetTypeInfo(typeof(T))
                ?? throw new InvalidOperationException($"Missing JSON type info for {typeof(T).Name}");
            return await System.Text.Json.JsonSerializer.DeserializeAsync(ctx.Request.Body, (System.Text.Json.Serialization.Metadata.JsonTypeInfo<T>)typeInfo);
        }
        catch
        {
            return null;
        }
    }

    private static IResult WriteJson<T>(HttpContext ctx, T payload, int statusCode) where T : class
    {
        var typeInfo = DashboardJsonSerializerContext.Default.GetTypeInfo(typeof(T))
            ?? throw new InvalidOperationException($"Missing JSON type info for {typeof(T).Name}");
        return Results.Json(payload, (System.Text.Json.Serialization.Metadata.JsonTypeInfo<T>)typeInfo, statusCode: statusCode);
    }
}
