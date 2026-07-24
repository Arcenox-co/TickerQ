using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using TickerQ.Dashboard.Authentication.Jwt;

namespace TickerQ.Dashboard.Authentication.Schemes;

/// <summary>
/// Reads the dashboard's signed-JWT auth cookie (default name
/// <c>tickerq.auth</c>) and validates it via the same
/// <see cref="JwtTokenIssuer"/> used by the bearer scheme — one crypto path,
/// two transports. The cookie is set by <c>POST /api/auth/login</c> and
/// cleared by <c>POST /api/auth/logout</c>.
/// </summary>
internal sealed class CookieAuthScheme : AuthSchemeBase
{
    public override string Name => "Cookie";
    public override AuthMode LegacyMode => AuthMode.Cookie;

    public JwtTokenIssuer? Issuer { get; set; }
    public IUserStore? Users { get; set; }
    public string CookieName { get; set; } = "tickerq.auth";

    public override Task<AuthResult> TryAuthenticateAsync(HttpContext context)
    {
        if (Issuer == null) return Task.FromResult(AuthResult.Failure("Cookie issuer not configured"));

        if (!context.Request.Cookies.TryGetValue(CookieName, out var token) || string.IsNullOrEmpty(token))
            return Task.FromResult(AuthResult.Failure("No auth cookie present"));

        return Issuer.TryValidate(token, out var username)
            ? Task.FromResult(AuthResult.Success(username))
            : Task.FromResult(AuthResult.Failure("Invalid or expired auth cookie"));
    }

    // No WWW-Authenticate header for cookie mode — browsers don't have a
    // native cookie-auth prompt. The SPA detects 401 and redirects to /login.

    public override void Validate()
    {
        // Issuer is bound by JwtBearerSchemeBinder during startup (after
        // AuthConfig.Validate runs). TryAuthenticateAsync guards null.
        if (Users == null) throw new InvalidOperationException("Cookie scheme is missing a configured IUserStore.");
        if (string.IsNullOrEmpty(CookieName)) throw new InvalidOperationException("Cookie scheme requires a CookieName.");
    }
}
