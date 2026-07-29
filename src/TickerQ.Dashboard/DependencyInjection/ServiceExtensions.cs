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

using Microsoft.AspNetCore.Builder;

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using TickerQ.Utilities.Entities;

namespace TickerQ.Dashboard.DependencyInjection
{
    public static class ServiceExtensions
    {
        internal static void ConfigureDefaultCorsPolicy(DashboardOptionsBuilder config)
        {
            // Same-origin needs no CORS response. Credentialed split-origin access is exact-match only.
            config.CorsPolicyBuilder = cors =>
            {
                if (config.AllowedOrigins.Count > 0)
                    cors.WithOrigins(config.AllowedOrigins.ToArray())
                        .AllowAnyHeader()
                        .AllowAnyMethod()
                        .AllowCredentials();
                else
                    cors.SetIsOriginAllowed(_ => false);
            };
        }

        public static TickerOptionsBuilder<TTimeTicker, TCronTicker> AddDashboard<TTimeTicker, TCronTicker>(this TickerOptionsBuilder<TTimeTicker, TCronTicker> tickerConfiguration, Action<DashboardOptionsBuilder> configureDashboard = null)
            where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
            where TCronTicker : CronTickerEntity, new()
        {
            var dashboardConfig = new DashboardOptionsBuilder();

            ConfigureDefaultCorsPolicy(dashboardConfig);

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
            var key = bearer?.SigningKey ?? cookie?.SigningKey ?? CreateEphemeralSigningKey(sp, allowEphemeral);
            var issuer = bearer?.Issuer ?? cookie?.Issuer ?? "tickerq-dashboard";
            var audience = bearer?.Audience ?? cookie?.Audience ?? "tickerq-api";
            var lifetime = bearer?.AccessTokenLifetime ?? cookie?.SessionLifetime ?? TimeSpan.FromHours(1);

            return new JwtTokenIssuer(key, issuer, audience, lifetime);
        }

        /// <summary>Create a random process-local key only behind explicit development opt-in.</summary>
        private static byte[] CreateEphemeralSigningKey(IServiceProvider sp, bool allowEphemeral)
        {
            if (!allowEphemeral)
                throw new InvalidOperationException(
                    "TickerQ Dashboard credential authentication requires an explicit shared JWT signing key. " +
                    "Set JwtBearerOptions.SigningKey / CookieAuthOptions.SigningKey (at least 32 bytes), or set " +
                    "AllowEphemeralSigningKey = true only for development where restart logout and " +
                    "cross-instance token rejection are acceptable.");

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