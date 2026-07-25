using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Http;
using TickerQ.Dashboard.Assistant;
using TickerQ.Dashboard.Authentication;
using TickerQ.Dashboard.Authentication.Schemes;

namespace TickerQ.Dashboard;

public class DashboardOptionsBuilder
{
    internal string BasePath { get; set; } = "/tickerq/dashboard";
    internal Action<CorsPolicyBuilder> CorsPolicyBuilder { get; set; }
    internal string BackendDomain { get; set; }
    internal string GroupName { get; set; }
    internal string Title { get; set; } = "TickerQ Dashboard";
    internal string LogoUrl { get; set; }
    internal bool ReadOnly { get; set; }
    internal Func<HttpContext, bool>? ReadOnlyPredicate { get; set; }
    internal TimeZoneInfo DashboardTimeZone { get; set; }
    internal bool UseForwardedHeaders { get; set; }
    internal List<IPAddress> TrustedForwardedProxies { get; } = [];
    internal List<System.Net.IPNetwork> TrustedForwardedNetworks { get; } = [];
    internal List<string> AllowedOrigins { get; } = [];
    internal bool CustomCorsPolicyConfigured { get; set; }
    internal bool AnonymousDashboardAcknowledged { get; set; }
    internal AssistantOptionsBuilder? Assistant { get; set; }

    /// <summary>
    /// Effective read-only decision for one request: the global flag wins,
    /// otherwise the per-request predicate (role/claim-based) is consulted.
    /// Used by both the runtime config (drives the SPA's UI) and the write-
    /// endpoint guard (server-side 403) so the two can never disagree.
    /// </summary>
    internal bool IsReadOnlyFor(HttpContext context)
        => ReadOnly || (ReadOnlyPredicate?.Invoke(context) ?? false);

    // Clean authentication system
    internal AuthConfig Auth { get; set; } = new();

    // Custom Middleware Integration
    public Action<IApplicationBuilder> CustomMiddleware { get; set; }
    public Action<IApplicationBuilder> PreDashboardMiddleware { get; set; }
    public Action<IApplicationBuilder> PostDashboardMiddleware { get; set; }

    internal JsonSerializerOptions DashboardJsonOptions { get; set; }

    /// <summary>Tracks whether dashboard middleware has been applied to prevent double registration.</summary>
    internal bool MiddlewareApplied { get; set; }

    public void SetCorsPolicy(Action<CorsPolicyBuilder> corsPolicyBuilder)
    {
        CorsPolicyBuilder = corsPolicyBuilder ?? throw new ArgumentNullException(nameof(corsPolicyBuilder));
        CustomCorsPolicyConfigured = true;
    }

    /// <summary>Allow exact browser origins for credentialed split-origin dashboard access.</summary>
    public DashboardOptionsBuilder AllowOrigins(params string[] origins)
    {
        if (origins == null || origins.Length == 0)
            throw new ArgumentException("At least one origin is required.", nameof(origins));

        foreach (var origin in origins)
        {
            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
                uri.AbsolutePath != "/" || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
                throw new ArgumentException($"'{origin}' is not a valid HTTP(S) origin.", nameof(origins));

            var normalized = uri.GetLeftPart(UriPartial.Authority);
            if (!AllowedOrigins.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                AllowedOrigins.Add(normalized);
        }
        return this;
    }

    public void SetBasePath(string basePath)
        => BasePath = basePath;

    public void SetBackendDomain(string backendDomain)
        => BackendDomain = backendDomain;

    /// <summary>Set OpenAPI group name for dashboard endpoints</summary>
    public void SetGroupName(string groupName)
        => GroupName = groupName;

    /// <summary>Brand the dashboard with a custom title (browser tab + top bar).</summary>
    public DashboardOptionsBuilder SetTitle(string title)
    {
        Title = title;
        return this;
    }

    /// <summary>Show a custom logo in the dashboard shell. Absolute URL or a path the browser can reach.</summary>
    public DashboardOptionsBuilder SetLogoUrl(string logoUrl)
    {
        LogoUrl = logoUrl;
        return this;
    }

    /// <summary>
    /// Observability-only mode: the SPA hides every create/edit/pause/delete
    /// affordance AND the server rejects all mutation endpoints with 403, so
    /// the guarantee holds even for hand-crafted requests.
    /// </summary>
    public DashboardOptionsBuilder SetReadOnly(bool readOnly = true)
    {
        ReadOnly = readOnly;
        return this;
    }

    /// <summary>
    /// Per-user read-only mode: the predicate runs on every request and
    /// decides whether THIS user is a viewer. Return true → read-only
    /// (mutation UI hidden, write endpoints answer 403); false → full access.
    /// </summary>
    /// <remarks>
    /// Designed for Host mode, where <see cref="HttpContext.User"/> carries
    /// the host app's identity — e.g.
    /// <c>SetReadOnly(ctx =&gt; !ctx.User.IsInRole("ops"))</c> gives every
    /// authenticated user a live view while only "ops" members can mutate.
    /// With the dashboard's own login schemes, <c>HttpContext.User</c> is not
    /// populated — key off <c>ctx.Items["auth.username"]</c> instead.
    /// </remarks>
    public DashboardOptionsBuilder SetReadOnly(Func<HttpContext, bool> readOnlyWhen)
    {
        ReadOnlyPredicate = readOnlyWhen;
        return this;
    }

    /// <summary>Default rendering time zone for the SPA. Users can still override it in the UI.</summary>
    public DashboardOptionsBuilder SetTimeZone(TimeZoneInfo timeZone)
    {
        DashboardTimeZone = timeZone;
        return this;
    }

    /// <summary>
    /// Honour <c>X-Forwarded-Proto</c> / <c>X-Forwarded-Host</c> / <c>X-Forwarded-For</c>
    /// inside the dashboard branch for reverse-proxy deployments (nginx, Cloudflare,
    /// k8s ingress). Off by default — hosts that already call
    /// <c>UseForwardedHeaders()</c> upstream shouldn't double-apply it.
    /// </summary>
    public DashboardOptionsBuilder EnableForwardedHeaders(bool enabled = true)
    {
        UseForwardedHeaders = enabled;
        return this;
    }

    /// <summary>Trust forwarded headers only from this explicit reverse-proxy address.</summary>
    public DashboardOptionsBuilder TrustForwardedProxy(IPAddress proxy)
    {
        TrustedForwardedProxies.Add(proxy ?? throw new ArgumentNullException(nameof(proxy)));
        UseForwardedHeaders = true;
        return this;
    }

    /// <summary>Trust forwarded headers only from this explicit reverse-proxy network.</summary>
    public DashboardOptionsBuilder TrustForwardedNetwork(System.Net.IPNetwork network)
    {
        TrustedForwardedNetworks.Add(network);
        UseForwardedHeaders = true;
        return this;
    }

    /// <summary>
    /// Enable the AI assistant. The operator supplies a chat client
    /// (Anthropic, OpenAI, Azure, local Ollama, …) whose key stays on the
    /// server. When this is not called — or no chat client is configured —
    /// the assistant UI is never rendered and the endpoint isn't mapped.
    /// </summary>
    public DashboardOptionsBuilder AddAssistant(Action<AssistantOptionsBuilder> configure)
    {
        var builder = new AssistantOptionsBuilder();
        configure(builder);
        Assistant = builder;
        return this;
    }
    
    /// <summary>Configure no authentication (public dashboard)</summary>
    public DashboardOptionsBuilder WithNoAuth()
    {
        Auth.Mode = AuthMode.None;
        AnonymousDashboardAcknowledged = true;
        return this;
    }

    /// <summary>Explicitly acknowledge that all dashboard data and mutations are public.</summary>
    public DashboardOptionsBuilder AllowAnonymousDashboard() => WithNoAuth();

    /// <summary>Enable Basic Authentication with username/password</summary>
    public DashboardOptionsBuilder WithBasicAuth(string username, string password)
    {
        Auth.Mode = AuthMode.Basic;
        Auth.BasicCredentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
        return this;
    }

    /// <summary>
    /// Enable Basic Authentication with an async credential validator —
    /// check the username/password against a database, cache, or identity
    /// provider instead of a fixed pair.
    /// </summary>
    public DashboardOptionsBuilder WithBasicAuth(Func<string, string, Task<bool>> validator)
    {
        Auth.Schemes.Add(new BasicAuthScheme { AsyncValidator = validator });
        return this;
    }

    /// <summary>Enable API Key authentication (sent as Bearer token)</summary>
    public DashboardOptionsBuilder WithApiKey(string apiKey)
    {
        Auth.Mode = AuthMode.ApiKey;
        Auth.ApiKey = apiKey;
        return this;
    }

    /// <summary>
    /// Enable API Key authentication read from a custom request header
    /// (e.g. <c>X-Api-Key</c>) instead of <c>Authorization: Bearer</c>.
    /// </summary>
    public DashboardOptionsBuilder WithApiKey(string apiKey, string headerName)
    {
        Auth.Schemes.Add(new ApiKeyAuthScheme { ApiKey = apiKey, HeaderName = headerName });
        return this;
    }
    
    /// <summary>Use the host application's existing authentication system</summary>
    /// <param name="policy">Optional authorization policy name to require (e.g., "AdminPolicy"). If null or empty, uses the default policy.</param>
    /// <param name="loginRedirectPath">
    /// Optional host-app login page (e.g. <c>/account/login</c>). When set,
    /// unauthenticated browser navigations to the dashboard are redirected
    /// there with <c>?returnUrl=…</c> so the user can sign in on the host app
    /// and land back on the dashboard. API/fetch clients still get plain 401s.
    /// </param>
    public DashboardOptionsBuilder WithHostAuthentication(string? policy = null, string? loginRedirectPath = null)
    {
        Auth.Mode = AuthMode.Host;
        Auth.HostAuthorizationPolicy = policy;
        Auth.HostLoginRedirectPath = loginRedirectPath;
        return this;
    }
    
    /// <summary>Configure custom authentication with validation function</summary>
    public DashboardOptionsBuilder WithCustomAuth(Func<string, bool> validator)
    {
        Auth.Mode = AuthMode.Custom;
        Auth.CustomValidator = validator;
        return this;
    }

    /// <summary>
    /// Configure custom authentication with an async validator. Receives the
    /// full <see cref="Microsoft.AspNetCore.Http.HttpContext"/> so it can
    /// inspect headers/cookies and await a database or external service.
    /// </summary>
    public DashboardOptionsBuilder WithCustomAuth(Func<Microsoft.AspNetCore.Http.HttpContext, Task<bool>> validator)
    {
        Auth.Schemes.Add(new CustomAuthScheme { AsyncContextValidator = validator });
        return this;
    }

    /// <summary>
    /// Configure one or more authentication schemes. The dashboard tries
    /// them in registration order on every request and stops at the first
    /// match. Use this when you need to mix schemes (e.g. cookies for
    /// browsers + bearer tokens for scripts) — the older
    /// <c>WithBasicAuth</c> / <c>WithApiKey</c> / <c>WithHostAuthentication</c>
    /// / <c>WithCustomAuth</c> methods configure exactly one scheme and stay
    /// the simpler choice for single-mode setups.
    /// </summary>
    public DashboardOptionsBuilder WithAuthentication(Action<AuthSchemeBuilder> configure)
    {
        configure(new AuthSchemeBuilder(Auth));
        return this;
    }

    /// <summary>Set session timeout in minutes</summary>
    public DashboardOptionsBuilder WithSessionTimeout(int minutes)
    {
        Auth.SessionTimeoutMinutes = minutes;
        return this;
    }
    
    /// <summary>Validate the authentication configuration</summary>
    internal void Validate()
    {
        Auth.Validate();
        if (!Auth.IsEnabled && !AnonymousDashboardAcknowledged)
            throw new InvalidOperationException(
                "TickerQ Dashboard authentication is not configured. Call AllowAnonymousDashboard() " +
                "to explicitly acknowledge public exposure, or configure an authentication scheme.");
        if (!string.IsNullOrEmpty(BackendDomain) && AllowedOrigins.Count == 0 && !CustomCorsPolicyConfigured)
            throw new InvalidOperationException(
                "SetBackendDomain does not grant cross-origin trust. Call AllowOrigins() with exact browser origins.");
        if (UseForwardedHeaders && TrustedForwardedProxies.Count == 0 && TrustedForwardedNetworks.Count == 0)
            throw new InvalidOperationException(
                "EnableForwardedHeaders requires at least one TrustForwardedProxy() or TrustForwardedNetwork().");
    }
}
