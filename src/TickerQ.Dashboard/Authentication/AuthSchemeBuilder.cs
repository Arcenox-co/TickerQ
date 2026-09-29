using System;
using System.Text;
using TickerQ.Dashboard.Authentication.Jwt;
using TickerQ.Dashboard.Authentication.Schemes;

namespace TickerQ.Dashboard.Authentication;

/// <summary>
/// Fluent surface for registering one or more <see cref="IAuthScheme"/>s on a
/// dashboard's <see cref="AuthConfig"/>. Each <c>Add…</c> method appends a
/// scheme; at request time the dashboard tries them in registration order.
/// </summary>
public sealed class AuthSchemeBuilder
{
    private readonly AuthConfig _config;

    internal AuthSchemeBuilder(AuthConfig config) => _config = config;

    /// <summary>Add HTTP Basic authentication.</summary>
    public AuthSchemeBuilder AddBasic(string username, string password, Action<BasicAuthOptions>? configure = null)
    {
        var options = new BasicAuthOptions();
        configure?.Invoke(options);

        _config.Schemes.Add(new BasicAuthScheme
        {
            Credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}")),
            ChallengeBrowserPrompt = options.ChallengeBrowserPrompt,
            Realm = options.Realm,
        });

        return this;
    }

    /// <summary>
    /// Add HTTP Basic authentication with an async credential validator —
    /// receives the decoded username and password and can await a database,
    /// cache, or identity provider.
    /// </summary>
    public AuthSchemeBuilder AddBasic(Func<string, string, System.Threading.Tasks.Task<bool>> validator, Action<BasicAuthOptions>? configure = null)
    {
        var options = new BasicAuthOptions();
        configure?.Invoke(options);

        _config.Schemes.Add(new BasicAuthScheme
        {
            AsyncValidator = validator,
            ChallengeBrowserPrompt = options.ChallengeBrowserPrompt,
            Realm = options.Realm,
        });

        return this;
    }

    /// <summary>Add API-key authentication. The key is sent by clients as a Bearer token.</summary>
    public AuthSchemeBuilder AddApiKey(string apiKey)
    {
        _config.Schemes.Add(new ApiKeyAuthScheme { ApiKey = apiKey });
        return this;
    }

    /// <summary>
    /// Add API-key authentication read from a custom request header
    /// (e.g. <c>X-Api-Key</c>) instead of <c>Authorization: Bearer</c>.
    /// </summary>
    public AuthSchemeBuilder AddApiKey(string apiKey, string headerName)
    {
        _config.Schemes.Add(new ApiKeyAuthScheme { ApiKey = apiKey, HeaderName = headerName });
        return this;
    }

    /// <summary>Delegate to the host application's authentication, optionally enforcing a named policy.</summary>
    /// <param name="authorizationPolicy">Optional authorization policy name to require.</param>
    /// <param name="loginRedirectPath">
    /// Optional host-app login page (e.g. <c>/account/login</c>) — see
    /// <see cref="DashboardOptionsBuilder.WithHostAuthentication(string?, string?)"/>.
    /// </param>
    public AuthSchemeBuilder AddHost(string? authorizationPolicy = null, string? loginRedirectPath = null)
    {
        _config.Schemes.Add(new HostAuthScheme { AuthorizationPolicy = authorizationPolicy });
        if (loginRedirectPath != null) _config.HostLoginRedirectPath = loginRedirectPath;
        return this;
    }

    /// <summary>Add a custom validator. Receives the raw Authorization header value.</summary>
    public AuthSchemeBuilder AddCustom(Func<string, bool> validator)
    {
        _config.Schemes.Add(new CustomAuthScheme { Validator = validator });
        return this;
    }

    /// <summary>
    /// Add a custom async validator. Receives the full
    /// <see cref="Microsoft.AspNetCore.Http.HttpContext"/> so it can inspect
    /// headers/cookies and await a database or external service.
    /// </summary>
    public AuthSchemeBuilder AddCustom(Func<Microsoft.AspNetCore.Http.HttpContext, System.Threading.Tasks.Task<bool>> validator)
    {
        _config.Schemes.Add(new CustomAuthScheme { AsyncContextValidator = validator });
        return this;
    }

    /// <summary>
    /// Add JWT Bearer authentication. The dashboard's <c>/api/auth/login</c>
    /// endpoint issues tokens signed with the configured key; tokens are sent
    /// by clients as <c>Authorization: Bearer …</c>.
    /// </summary>
    /// <param name="configure">
    /// Required. At minimum, register at least one user via
    /// <see cref="JwtBearerOptions.AddUser"/> — otherwise nobody can log in.
    /// Configure <see cref="JwtBearerOptions.SigningKey"/> explicitly for stable
    /// restart and multi-instance validation. Ephemeral signing is development-only opt-in.
    /// </param>
    public AuthSchemeBuilder AddJwtBearer(Action<JwtBearerOptions> configure)
    {
        var options = new JwtBearerOptions();
        configure(options);
        _config.JwtBearerOptions = options;
        _config.Schemes.Add(new JwtBearerScheme { Users = options.Users });
        return this;
    }

    /// <summary>
    /// Add cookie-based authentication. Login issues an HTTP-only signed JWT
    /// cookie; the browser sends it automatically on every request. Sharing
    /// the JWT crypto path with <see cref="AddJwtBearer"/> means the same
    /// signing key validates both transports — a dashboard can accept the
    /// cookie from browser users AND the bearer header from API clients.
    /// </summary>
    public AuthSchemeBuilder AddCookieLogin(Action<CookieAuthOptions> configure)
    {
        var options = new CookieAuthOptions();
        configure(options);
        _config.CookieAuthOptions = options;
        _config.Schemes.Add(new CookieAuthScheme
        {
            Users = options.Users,
            CookieName = options.CookieName,
        });
        return this;
    }
}

/// <summary>Options for <see cref="AuthSchemeBuilder.AddJwtBearer"/>.</summary>
public sealed class JwtBearerOptions
{
    /// <summary>
    /// HS256 signing secret as raw bytes (at least 32 bytes). Required unless
    /// ephemeral development signing is explicitly enabled.
    /// </summary>
    public byte[]? SigningKey { get; set; }

    /// <summary>Convenience: set <see cref="SigningKey"/> from a UTF-8 string.</summary>
    public void SetSigningKey(string secret) => SigningKey = Encoding.UTF8.GetBytes(secret);

    /// <summary>
    /// Allow the dashboard to fall back to a random in-memory signing key when
    /// no <see cref="SigningKey"/> is set AND DataProtection is unavailable.
    /// Off by default because an ephemeral key silently invalidates every
    /// issued token on restart and breaks multi-instance deployments — with
    /// this off, startup throws with instructions instead.
    /// </summary>
    public bool AllowEphemeralSigningKey { get; set; }

    /// <summary>Issuer claim. Defaults to <c>tickerq-dashboard</c>.</summary>
    public string Issuer { get; set; } = "tickerq-dashboard";

    /// <summary>Audience claim. Defaults to <c>tickerq-api</c>.</summary>
    public string Audience { get; set; } = "tickerq-api";

    /// <summary>How long an access token is valid. Default 60 minutes.</summary>
    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(60);

    /// <summary>The user store used by the login endpoint. Defaults to an empty in-memory store.</summary>
    public InMemoryUserStore Users { get; set; } = new();

    /// <summary>Register a user. Shorthand for <c>Users.AddUser(username, password)</c>.</summary>
    public JwtBearerOptions AddUser(string username, string password)
    {
        Users.AddUser(username, password);
        return this;
    }
}

/// <summary>Options for <see cref="AuthSchemeBuilder.AddCookieLogin"/>.</summary>
public sealed class CookieAuthOptions
{
    /// <summary>
    /// HS256 signing secret as raw bytes (at least 32 bytes). Required unless
    /// ephemeral development signing is explicitly enabled. Set the same key
    /// on every instance in distributed deployments.
    /// </summary>
    public byte[]? SigningKey { get; set; }

    /// <summary>Convenience: set <see cref="SigningKey"/> from a UTF-8 string.</summary>
    public void SetSigningKey(string secret) => SigningKey = Encoding.UTF8.GetBytes(secret);

    /// <summary>
    /// Allow the dashboard to fall back to a random in-memory signing key when
    /// no <see cref="SigningKey"/> is set AND DataProtection is unavailable.
    /// Off by default — see <see cref="JwtBearerOptions.AllowEphemeralSigningKey"/>.
    /// </summary>
    public bool AllowEphemeralSigningKey { get; set; }

    /// <summary>Issuer claim. Defaults to <c>tickerq-dashboard</c>.</summary>
    public string Issuer { get; set; } = "tickerq-dashboard";

    /// <summary>Audience claim. Defaults to <c>tickerq-api</c>.</summary>
    public string Audience { get; set; } = "tickerq-api";

    /// <summary>How long the cookie / underlying JWT is valid. Default 8 hours.</summary>
    public TimeSpan SessionLifetime { get; set; } = TimeSpan.FromHours(8);

    /// <summary>Cookie name. Default <c>tickerq.auth</c>.</summary>
    public string CookieName { get; set; } = "tickerq.auth";

    /// <summary>
    /// <c>Secure</c> cookie attribute. Default true. Set to false only when
    /// running the dashboard over plain HTTP for local development.
    /// </summary>
    public bool SecureCookie { get; set; } = true;

    /// <summary>The user store used by the login endpoint. Defaults to an empty in-memory store.</summary>
    public InMemoryUserStore Users { get; set; } = new();

    /// <summary>Register a user. Shorthand for <c>Users.AddUser(username, password)</c>.</summary>
    public CookieAuthOptions AddUser(string username, string password)
    {
        Users.AddUser(username, password);
        return this;
    }
}

/// <summary>Optional behavior tweaks for <see cref="AuthSchemeBuilder.AddBasic"/>.</summary>
public sealed class BasicAuthOptions
{
    /// <summary>
    /// When true, a failed authentication ends with
    /// <c>WWW-Authenticate: Basic realm="…"</c> so browsers show the native
    /// login prompt. Off by default — SPA login flows prefer a plain 401.
    /// </summary>
    public bool ChallengeBrowserPrompt { get; set; }

    /// <summary>Realm name advertised on the browser prompt.</summary>
    public string Realm { get; set; } = "TickerQ Dashboard";
}
