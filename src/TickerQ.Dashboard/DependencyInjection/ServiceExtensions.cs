using Microsoft.Extensions.DependencyInjection;
using TickerQ.Dashboard.Endpoints;
using TickerQ.Dashboard.Hubs;
using TickerQ.Dashboard.Infrastructure;
using TickerQ.Dashboard.Infrastructure.Dashboard;
using TickerQ.Dashboard.Authentication;
using TickerQ.Dashboard.Authentication.Jwt;
using TickerQ.Dashboard.Authentication.Schemes;
using TickerQ.Utilities;
using TickerQ.Utilities.Interfaces;
using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using TickerQ.Utilities.Entities;

namespace TickerQ.Dashboard.DependencyInjection
{
    public static class ServiceExtensions
    {
        public static TickerOptionsBuilder<TTimeTicker, TCronTicker> AddDashboard<TTimeTicker, TCronTicker>(this TickerOptionsBuilder<TTimeTicker, TCronTicker> tickerConfiguration, Action<DashboardOptionsBuilder> configureDashboard = null)
            where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
            where TCronTicker : CronTickerEntity, new()
        {
            var dashboardConfig = new DashboardOptionsBuilder();

            // Default CORS is same-origin: browsers don't apply CORS to
            // same-origin requests, so an empty policy changes nothing for the
            // standard embedded-dashboard setup while refusing credentialed
            // cross-origin calls from arbitrary websites. Split-origin setups
            // (SetBackendDomain) get the permissive legacy policy; customers
            // who need something in between call SetCorsPolicy themselves.
            dashboardConfig.CorsPolicyBuilder = cors =>
            {
                if (!string.IsNullOrEmpty(dashboardConfig.BackendDomain))
                    cors.SetIsOriginAllowed(_ => true)
                        .AllowAnyHeader()
                        .AllowAnyMethod()
                        .AllowCredentials();
                else
                    cors.SetIsOriginAllowed(_ => false);
            };

            configureDashboard?.Invoke(dashboardConfig);
            
            tickerConfiguration.DashboardServiceAction = (services) =>
            {
                services.AddScoped<ITickerDashboardRepository<TTimeTicker, TCronTicker>, TickerDashboardRepository<TTimeTicker, TCronTicker>>();
                services.Replace(ServiceDescriptor.Singleton(services.AddSingleton<ITickerQNotificationHubSender, TickerQNotificationHubSender>()));
                
                // Validate configuration
                dashboardConfig.Validate();
                
                // Register authentication system
                services.AddSingleton(dashboardConfig.Auth);
                services.AddScoped<IAuthService, AuthService>();

                // Dashboard metrics — customers can observe request latency,
                // auth failures and SignalR connections without OTel by
                // replacing this registration with their own implementation.
                services.TryAddSingleton<Infrastructure.Metrics.ITickerQDashboardMetrics, Infrastructure.Metrics.MeterDashboardMetrics>();

                // AI assistant (optional): only registered when the operator
                // configured a chat client. Its presence in DI is the feature
                // gate consulted by the endpoint and the runtime config.
                if (dashboardConfig.Assistant?.IsEnabled == true)
                {
                    var assistantOptions = dashboardConfig.Assistant;
                    services.AddSingleton(sp => new Assistant.TickerAssistantService(assistantOptions, sp));
                }

                // JWT / cookie support: build a single JwtTokenIssuer that both
                // schemes share — same signing key, same iss/aud, one validation
                // path. When both bearer and cookie are configured the bearer's
                // lifetime wins; cookie-only setups use the cookie's lifetime.
                if (dashboardConfig.Auth.JwtBearerOptions != null || dashboardConfig.Auth.CookieAuthOptions != null)
                {
                    services.AddDataProtection();
                    services.AddSingleton(sp => BuildJwtIssuer(sp, dashboardConfig.Auth));

                    // Late-bind the issuer onto every credential-using scheme so
                    // AuthService can validate without going through the SP each request.
                    services.AddSingleton<IJwtBearerSchemeBinder>(sp =>
                        new JwtBearerSchemeBinder(sp, dashboardConfig.Auth));
                }

                // Host mode delegates to the host app's authentication. Fail
                // loudly when the host never registered auth services —
                // silently adding an empty AddAuthentication() here would
                // "authenticate" nothing and mask the misconfiguration until
                // someone notices the dashboard is open.
                if (dashboardConfig.Auth.Schemes.OfType<HostAuthScheme>().Any())
                {
                    var hasAuthenticationService = services.Any(s =>
                        s.ServiceType == typeof(Microsoft.AspNetCore.Authentication.IAuthenticationService) ||
                        s.ServiceType.Name == "IAuthenticationSchemeProvider");

                    if (!hasAuthenticationService)
                        throw new InvalidOperationException(
                            "TickerQ Dashboard: WithHostAuthentication() requires the host application to register " +
                            "authentication first. Call services.AddAuthentication(...).AddCookie()/.AddJwtBearer()/... " +
                            "and services.AddAuthorization() before AddTickerQ(...), or use one of the dashboard's " +
                            "own schemes instead (WithBasicAuth / WithApiKey / WithAuthentication(a => a.AddCookieLogin(...))).");
                }
                
                services.AddDashboardService<TTimeTicker, TCronTicker>(dashboardConfig);
                services.AddSingleton<DashboardOptionsBuilder>(_ => dashboardConfig);

                // Register IStartupFilter for old Startup.cs pattern where IHost != IApplicationBuilder.
                // In the new WebApplication pattern, UseDashboardDelegate handles it directly.
                services.AddSingleton<IStartupFilter>(new DashboardStartupFilter<TTimeTicker, TCronTicker>(dashboardConfig));
            };

            UseDashboardDelegate(tickerConfiguration, dashboardConfig);

            return tickerConfiguration;
        }

        private static void UseDashboardDelegate<TTimeTicker, TCronTicker>(this TickerOptionsBuilder<TTimeTicker, TCronTicker> tickerConfiguration, DashboardOptionsBuilder dashboardConfig)
            where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
            where TCronTicker : CronTickerEntity, new()
        {
            tickerConfiguration.UseDashboardApplication((appObj) =>
            {
                if (appObj is IApplicationBuilder app)
                {
                    // New WebApplication pattern: WebApplication implements both IHost and IApplicationBuilder.
                    // Mark as applied so DashboardStartupFilter skips duplicate registration.
                    dashboardConfig.MiddlewareApplied = true;
                    // Resolve the binder once so the JwtBearerScheme.Issuer is set
                    // before any request reaches AuthMiddleware.
                    app.ApplicationServices.GetService<IJwtBearerSchemeBinder>()?.Bind();
                    app.UseDashboardWithEndpoints<TTimeTicker, TCronTicker>(dashboardConfig);
                }
                // Old Startup.cs pattern: IHost is not IApplicationBuilder.
                // Dashboard middleware is injected via IStartupFilter registered in AddDashboard.
            });
        }

        private static JwtTokenIssuer BuildJwtIssuer(IServiceProvider sp, AuthConfig config)
        {
            // Prefer bearer settings when both are configured — bearer is the
            // canonical channel for headless clients; cookie inherits its
            // lifetime / iss / aud for symmetry.
            var bearer = config.JwtBearerOptions;
            var cookie = config.CookieAuthOptions;

            var allowEphemeral = (bearer?.AllowEphemeralSigningKey ?? false) || (cookie?.AllowEphemeralSigningKey ?? false);
            var key = bearer?.SigningKey ?? cookie?.SigningKey ?? DeriveSigningKey(sp, allowEphemeral);
            var issuer = bearer?.Issuer ?? cookie?.Issuer ?? "tickerq-dashboard";
            var audience = bearer?.Audience ?? cookie?.Audience ?? "tickerq-api";
            var lifetime = bearer?.AccessTokenLifetime ?? cookie?.SessionLifetime ?? TimeSpan.FromHours(1);

            return new JwtTokenIssuer(key, issuer, audience, lifetime);
        }

        /// <summary>
        /// Auto-derive a stable 32-byte HS256 key from <see cref="IDataProtectionProvider"/>.
        /// </summary>
        /// <remarks>
        /// Calls <c>protector.Protect("v1")</c> to obtain a per-machine encrypted
        /// blob (ASP.NET Core's DataProtection keyring persists across restarts
        /// in app-isolated storage by default), then SHA-256s it down to a
        /// 256-bit secret. In distributed setups, either configure the
        /// DataProtection key store to a shared location OR set
        /// <see cref="JwtBearerOptions.SigningKey"/> explicitly so every node
        /// signs with the same key.
        /// </remarks>
        private static byte[] DeriveSigningKey(IServiceProvider sp, bool allowEphemeral)
        {
            var dp = sp.GetService<IDataProtectionProvider>();
            if (dp != null)
            {
                var protector = dp.CreateProtector("TickerQ.Dashboard.JwtSigning.v1");
                var blob = protector.Protect(Encoding.UTF8.GetBytes("tickerq-signing-key"));
                return SHA256.HashData(blob);
            }

            // No DataProtection and no explicit key: an ephemeral key would
            // silently invalidate every issued token on restart and reject
            // tokens issued by other instances. Fail loudly unless the
            // customer explicitly opted in (dev scenarios).
            if (!allowEphemeral)
                throw new InvalidOperationException(
                    "TickerQ Dashboard: cannot derive a stable JWT signing key because DataProtection is unavailable. " +
                    "Set JwtBearerOptions.SigningKey / CookieAuthOptions.SigningKey explicitly (required for " +
                    "multi-instance deployments), configure DataProtection with a persistent key store, or set " +
                    "AllowEphemeralSigningKey = true to accept that all sessions are lost on every restart.");

            sp.GetService<ILoggerFactory>()?
                .CreateLogger("TickerQ.Dashboard.Authentication")
                .LogWarning("Using an ephemeral JWT signing key (AllowEphemeralSigningKey=true). " +
                            "All tokens become invalid on application restart and other instances will reject them.");
            return RandomNumberGenerator.GetBytes(32);
        }
    }

    /// <summary>
    /// Late-binds the singleton <see cref="JwtTokenIssuer"/> onto the
    /// registered <see cref="JwtBearerScheme"/> so request-time
    /// authentication doesn't have to reach back into the service provider.
    /// </summary>
    internal interface IJwtBearerSchemeBinder
    {
        void Bind();
    }

    internal sealed class JwtBearerSchemeBinder : IJwtBearerSchemeBinder
    {
        private readonly IServiceProvider _sp;
        private readonly AuthConfig _config;
        private bool _bound;

        public JwtBearerSchemeBinder(IServiceProvider sp, AuthConfig config)
        {
            _sp = sp;
            _config = config;
        }

        public void Bind()
        {
            if (_bound) return;
            var issuer = _sp.GetRequiredService<JwtTokenIssuer>();
            foreach (var scheme in _config.Schemes.OfType<JwtBearerScheme>())
                scheme.Issuer = issuer;
            foreach (var scheme in _config.Schemes.OfType<CookieAuthScheme>())
                scheme.Issuer = issuer;
            _bound = true;
        }
    }
}