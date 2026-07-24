using System;
using System.Collections.Generic;
using TickerQ.Dashboard.Authentication.Schemes;

namespace TickerQ.Dashboard.Authentication;

/// <summary>
/// Authentication configuration for TickerQ Dashboard.
/// </summary>
/// <remarks>
/// The runtime uses the <see cref="Schemes"/> list — every request is tried
/// against each scheme in order and the first match wins. The legacy POCO-
/// style properties (<see cref="Mode"/>, <see cref="BasicCredentials"/>,
/// <see cref="ApiKey"/>, etc.) remain for backward compatibility with
/// existing callers; they are materialized into a single equivalent scheme
/// by <see cref="Validate"/> at startup.
/// </remarks>
public class AuthConfig
{
    /// <summary>
    /// Active scheme list. The runtime walks this in order on every request.
    /// Builder methods (<c>WithBasicAuth</c>, <c>WithApiKey</c>, …) append
    /// here directly; the legacy property-bag setup path appends inside
    /// <see cref="Validate"/>.
    /// </summary>
    internal List<IAuthScheme> Schemes { get; } = new();

    private AuthMode _mode = AuthMode.None;

    /// <summary>
    /// Legacy single-mode property. New code should configure schemes via
    /// <c>WithBasicAuth</c> / <c>WithApiKey</c> / etc. on the builder.
    /// Reading after <see cref="Validate"/> prefers the first scheme's mode
    /// so multi-scheme setups report a consistent value.
    /// </summary>
    public AuthMode Mode
    {
        get => Schemes.Count > 0 ? Schemes[0].LegacyMode : _mode;
        set => _mode = value;
    }

    /// <summary>Base64 <c>username:password</c> for legacy Basic auth setup.</summary>
    public string? BasicCredentials { get; set; }

    /// <summary>API key value for legacy ApiKey auth setup.</summary>
    public string? ApiKey { get; set; }

    /// <summary>User validator for legacy Custom auth setup.</summary>
    public Func<string, bool>? CustomValidator { get; set; }

    /// <summary>Authorization policy name for legacy Host auth setup.</summary>
    public string? HostAuthorizationPolicy { get; set; }

    /// <summary>
    /// Host-app login page for browser flows (e.g. <c>/account/login</c>).
    /// When set (Host mode), unauthenticated browser NAVIGATIONS to the
    /// dashboard are redirected there with <c>?returnUrl=&lt;dashboard-url&gt;</c>
    /// instead of loading a dashboard that can only 401. The SPA also uses it
    /// for mid-session expiry. Fetch/API clients keep getting plain 401s.
    /// </summary>
    public string? HostLoginRedirectPath { get; set; }

    /// <summary>Session timeout in minutes (advertised to the SPA via /api/auth/info).</summary>
    public int SessionTimeoutMinutes { get; set; } = 60;

    /// <summary>
    /// JWT options when a <see cref="Schemes.JwtBearerScheme"/> is registered.
    /// Populated by <see cref="AuthSchemeBuilder.AddJwtBearer"/> and consumed
    /// by the service-extension wiring (which materializes the
    /// <see cref="Jwt.JwtTokenIssuer"/> using either the explicit signing key
    /// or a DataProtection-derived one).
    /// </summary>
    internal JwtBearerOptions? JwtBearerOptions { get; set; }

    /// <summary>
    /// Cookie options when a <see cref="Schemes.CookieAuthScheme"/> is
    /// registered. Cookies reuse the same <see cref="Jwt.JwtTokenIssuer"/>
    /// the bearer scheme uses — one signing key, two transports.
    /// </summary>
    internal CookieAuthOptions? CookieAuthOptions { get; set; }

    /// <summary>True when at least one scheme is configured (either via builder or legacy POCO).</summary>
    public bool IsEnabled => Schemes.Count > 0 || _mode != AuthMode.None;

    /// <summary>
    /// Materialize legacy property-bag setup into a scheme (if no builder-
    /// added schemes exist) and validate every scheme. Called once at
    /// service-registration time — see <c>ServiceExtensions.AddDashboard</c>.
    /// </summary>
    public void Validate()
    {
        if (Schemes.Count == 0)
        {
            switch (_mode)
            {
                case AuthMode.None:
                    break;
                case AuthMode.Basic:
                    Schemes.Add(new BasicAuthScheme { Credentials = BasicCredentials });
                    break;
                case AuthMode.ApiKey:
                    Schemes.Add(new ApiKeyAuthScheme { ApiKey = ApiKey });
                    break;
                case AuthMode.Host:
                    Schemes.Add(new HostAuthScheme { AuthorizationPolicy = HostAuthorizationPolicy });
                    break;
                case AuthMode.Custom:
                    Schemes.Add(new CustomAuthScheme { Validator = CustomValidator });
                    break;
            }
        }

        foreach (var scheme in Schemes)
            scheme.Validate();
    }
}

/// <summary>
/// Authentication modes supported by the dashboard.
/// </summary>
/// <remarks>
/// Identifies a single scheme. Configurations that combine multiple schemes
/// (Cookie + JWT) report the first scheme's mode via <see cref="AuthConfig.Mode"/>.
/// </remarks>
public enum AuthMode
{
    /// <summary>No authentication — public dashboard.</summary>
    None = 0,

    /// <summary>HTTP Basic authentication.</summary>
    Basic = 1,

    /// <summary>API key authentication (sent as a Bearer token).</summary>
    ApiKey = 2,

    /// <summary>Delegate to the host application's authentication.</summary>
    Host = 3,

    /// <summary>User-supplied validator.</summary>
    Custom = 4,

    /// <summary>JSON Web Token (Bearer header) issued by the dashboard's own login endpoint.</summary>
    Jwt = 5,

    /// <summary>Stateless JWT carried in an HTTP-only cookie, set by the dashboard's login endpoint.</summary>
    Cookie = 6,
}
