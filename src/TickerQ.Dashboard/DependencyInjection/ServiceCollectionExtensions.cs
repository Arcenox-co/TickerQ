using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using TickerQ.Dashboard.Authentication;
using TickerQ.Dashboard.Endpoints;
using TickerQ.Dashboard.Infrastructure;
using TickerQ.Dashboard.Infrastructure.Metrics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Routing;
using TickerQ.Utilities.Entities;

namespace TickerQ.Dashboard.DependencyInjection
{
    internal static class ServiceCollectionExtensions
    {
        /// <summary>
        /// SignalR hub route inside the dashboard branch. Advertised to the
        /// SPA via the runtime config (realtime.hubPath) so both sides always
        /// agree — never hardcode this in the frontend.
        /// </summary>
        internal const string NotificationHubPath = "/tickerq-notification-hub";

        private static readonly Lazy<string> PackageVersion = new(() =>
        {
            var informational = typeof(ServiceCollectionExtensions).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
            var metadataIdx = informational.IndexOf('+');
            return metadataIdx > 0 ? informational[..metadataIdx] : informational;
        });

        internal static void AddDashboardService<TTimeTicker, TCronTicker>(this IServiceCollection services, DashboardOptionsBuilder config)
            where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
            where TCronTicker : CronTickerEntity, new()
        {
            config.DashboardJsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                Converters =
                {
                    new StringToByteArrayConverter(),
                    new System.Text.Json.Serialization.JsonStringEnumConverter(),
                },
                TypeInfoResolverChain = { DashboardJsonSerializerContext.Default }
            };
            
            // Register the dashboard configuration for DI
            services.AddSingleton(config);

            services.AddRouting();
            services.AddSignalR();

            // The new authentication system is registered in ServiceExtensions.cs
            // This method is kept for backward compatibility with existing middleware pipeline

            services.AddAuthorization();
            services.AddCors(options =>
            {
                options.AddPolicy("TickerQ_Dashboard_CORS", config.CorsPolicyBuilder);
            });
        }

        internal static void UseDashboardWithEndpoints<TTimeTicker, TCronTicker>(this IApplicationBuilder app, DashboardOptionsBuilder config)
            where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
            where TCronTicker : CronTickerEntity, new()
        {
            // Get the assembly and set up the embedded file provider
            var assembly = Assembly.GetExecutingAssembly();
            var embeddedFileProvider = new EmbeddedFileProvider(assembly, "TickerQ.Dashboard.wwwroot.dist");

            // Validate and normalize base path
            var basePath = NormalizeBasePath(config.BasePath);

            // Load the embedded index.html template. The SPA fallback serves it with a
            // <base href> injected so the bundle's relative "./assets/..." URLs resolve
            // under the configured mount path (see InjectExternalScripts).
            string htmlTemplate = null;
            var indexFile = embeddedFileProvider.GetFileInfo("index.html");
            if (indexFile.Exists)
            {
                using var stream = indexFile.CreateReadStream();
                using var reader = new StreamReader(stream);
                htmlTemplate = reader.ReadToEnd();
            }

            // Map a branch for the basePath with PathBase-aware routing.
            // Standard app.Map() fails when UsePathBase() runs before UseTickerQ() and the user
            // includes the PathBase prefix in SetBasePath() (e.g. SetBasePath("/cool-app/dashboard")
            // with UsePathBase("/cool-app")), because PathBase is already stripped from Request.Path.
            // This also handles the normal case where SetBasePath() contains only the dashboard segment.
            app.MapPathBaseAware(basePath, dashboardApp =>
            {
                // Reverse-proxy support (opt-in via EnableForwardedHeaders):
                // honour X-Forwarded-Proto/Host/For inside this branch so
                // redirects, cookies and absolute URLs use the public scheme
                // and host. Trust-any-proxy is acceptable here precisely
                // because the customer opted in explicitly.
                if (config.UseForwardedHeaders)
                {
                    var forwardedOptions = new ForwardedHeadersOptions
                    {
                        ForwardedHeaders = ForwardedHeaders.XForwardedFor
                                           | ForwardedHeaders.XForwardedProto
                                           | ForwardedHeaders.XForwardedHost,
                    };
                    forwardedOptions.KnownNetworks.Clear();
                    forwardedOptions.KnownProxies.Clear();
                    dashboardApp.UseForwardedHeaders(forwardedOptions);
                }

                // Execute pre-dashboard middleware
                config.PreDashboardMiddleware?.Invoke(dashboardApp);

                // Canonical trailing slash: redirect "{basePath}" → "{basePath}/".
                // MapPathBaseAware moves the matched prefix into PathBase, so a request to
                // the bare base path arrives here with an empty Request.Path. Redirecting
                // guarantees the SPA document has a directory for relative asset URLs to
                // resolve against, matching how Swagger UI behaves under a route prefix.
                dashboardApp.Use(async (context, next) =>
                {
                    if (context.Request.Path == PathString.Empty)
                    {
                        context.Response.Redirect(context.Request.PathBase.Value + "/" + context.Request.QueryString);
                        return;
                    }

                    await next();
                });

                // Host-mode login round-trip: when the customer configured a
                // login page (WithHostAuthentication(policy, "/account/login")),
                // unauthenticated browser NAVIGATIONS bounce there with a
                // returnUrl instead of loading a dashboard that can only 401.
                // Fetch/XHR and non-browser clients still get plain 401s from
                // AuthMiddleware, and authenticated-but-unauthorized users are
                // NOT redirected (that would loop — they're already signed in).
                if (!string.IsNullOrEmpty(config.Auth.HostLoginRedirectPath))
                {
                    var loginPath = config.Auth.HostLoginRedirectPath;
                    dashboardApp.Use(async (context, next) =>
                    {
                        var fetchMode = context.Request.Headers["Sec-Fetch-Mode"].ToString();
                        var isNavigation =
                            string.Equals(fetchMode, "navigate", StringComparison.OrdinalIgnoreCase)
                            || (string.IsNullOrEmpty(fetchMode) &&
                                context.Request.Headers.Accept.ToString().Contains("text/html", StringComparison.OrdinalIgnoreCase));

                        if (isNavigation
                            && HttpMethods.IsGet(context.Request.Method)
                            && context.User.Identity?.IsAuthenticated != true)
                        {
                            var returnUrl = context.Request.PathBase.Add(context.Request.Path) + context.Request.QueryString;
                            var separator = loginPath.Contains('?') ? '&' : '?';
                            context.Response.Redirect($"{loginPath}{separator}returnUrl={Uri.EscapeDataString(returnUrl)}");
                            return;
                        }

                        await next();
                    });
                }

                // CRITICAL: Serve static files FIRST, before any authentication
                // This ensures static assets (JS, CSS, images) are served without auth challenges
                dashboardApp.UseStaticFiles(new StaticFileOptions
                {
                    FileProvider = embeddedFileProvider,
                    OnPrepareResponse = ctx =>
                    {
                        // Cache static assets for 1 hour
                        if (ctx.File.Name.EndsWith(".js") || ctx.File.Name.EndsWith(".css") ||
                            ctx.File.Name.EndsWith(".ico") || ctx.File.Name.EndsWith(".png"))
                        {
                            ctx.Context.Response.Headers.CacheControl = "public,max-age=3600";
                        }
                    }
                });

                // Serve the runtime config as an external script (before auth). Keeping it
                // out-of-line lets the dashboard run under CSP script-src 'self'.
                // Cached for 60s with an ETag derived from the payload, so config
                // changes propagate within a minute without a per-request body.
                dashboardApp.Use(async (context, next) =>
                {
                    if (string.Equals(context.Request.Path.Value, "/__tickerq-config.js", StringComparison.OrdinalIgnoreCase))
                    {
                        var configJs = GenerateConfigJs(context, basePath, config);
                        var etag = "\"" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(configJs)))[..16] + "\"";

                        // "private": the payload can differ per user (per-user
                        // read-only), so shared proxy caches must not serve it
                        // across users.
                        context.Response.Headers.ETag = etag;
                        context.Response.Headers.CacheControl = "private, max-age=60";

                        if (context.Request.Headers.IfNoneMatch.ToString().Contains(etag, StringComparison.Ordinal))
                        {
                            context.Response.StatusCode = StatusCodes.Status304NotModified;
                            return;
                        }

                        context.Response.ContentType = "application/javascript; charset=utf-8";
                        await context.Response.WriteAsync(configJs);
                        return;
                    }

                    await next();
                });

                // Request metrics for the dashboard API. Wraps everything after
                // static files so latency includes auth + endpoint execution.
                dashboardApp.Use(async (context, next) =>
                {
                    if (!context.Request.Path.StartsWithSegments("/api"))
                    {
                        await next();
                        return;
                    }

                    var startTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
                    try
                    {
                        await next();
                    }
                    finally
                    {
                        var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
                        context.RequestServices.GetService<ITickerQDashboardMetrics>()?.RequestCompleted(
                            context.Request.Method,
                            context.Request.Path.Value ?? string.Empty,
                            context.Response.StatusCode,
                            elapsed);
                    }
                });

                // Set up routing and CORS
                dashboardApp.UseRouting();
                dashboardApp.UseCors("TickerQ_Dashboard_CORS");

                // Add ASP.NET Core authorization middleware when auth is enabled.
                // This is required because Host-mode endpoints use RequireAuthorization(),
                // and ASP.NET Core's EndpointMiddleware throws InvalidOperationException
                // if no AuthorizationMiddleware exists between UseRouting() and UseEndpoints().
                // The host app's UseAuthorization() does not propagate into Map() branches.
                if (config.Auth.IsEnabled)
                {
                    dashboardApp.UseAuthorization();
                }

                // Add authentication middleware (only protects API endpoints)
                if (config.Auth.IsEnabled)
                {
                    dashboardApp.UseMiddleware<AuthMiddleware>();
                }

                // Execute custom middleware if provided
                config.CustomMiddleware?.Invoke(dashboardApp);

                // Map Minimal API endpoints and SignalR hub
                dashboardApp.UseEndpoints(endpoints =>
                {
                    endpoints.MapDashboardEndpoints<TTimeTicker, TCronTicker>(config);
                    endpoints.MapHub<Hubs.TickerQNotificationHub>(NotificationHubPath)
                        .RequireCors("TickerQ_Dashboard_CORS");
                });

                // Execute post-dashboard middleware
                config.PostDashboardMiddleware?.Invoke(dashboardApp);

                // SPA fallback middleware: if no route is matched, serve the modified index.html
                dashboardApp.Use(async (context, next) =>
                {
                    await next();

                    if (context.Response.StatusCode == 404 && htmlTemplate != null)
                    {
                        var htmlContent = InjectExternalScripts(htmlTemplate, context, basePath);

                        context.Response.ContentType = "text/html";
                        context.Response.StatusCode = 200;
                        await context.Response.WriteAsync(htmlContent);
                    }
                });
            });
        }

        private static string NormalizeBasePath(string basePath)
        {
            if (string.IsNullOrEmpty(basePath))
                return "/";

            if (!basePath.StartsWith('/'))
                basePath = "/" + basePath;

            return basePath.TrimEnd('/');
        }

        /// <summary>
        /// Generates the runtime config JavaScript served as an external file.
        /// Sets window.TickerQConfig, which the SPA reads at startup (base path, auth mode).
        /// </summary>
        private static string GenerateConfigJs(HttpContext httpContext, string basePath, DashboardOptionsBuilder config)
        {
            var pathBase = httpContext.Request.PathBase.HasValue
                ? httpContext.Request.PathBase.Value
                : string.Empty;

            var frontendBasePath = CombinePathBase(pathBase, basePath);

            var envConfig = new FrontendConfigResponse
            {
                BasePath = frontendBasePath,
                BackendDomain = config.BackendDomain,
                Version = PackageVersion.Value,
                Title = config.Title,
                LogoUrl = config.LogoUrl,
                // Per-request: with the predicate overload this differs per
                // user (viewers get true), driving the SPA's read-only UI.
                ReadOnly = config.IsReadOnlyFor(httpContext),
                Timezone = config.DashboardTimeZone?.Id,
                Realtime = new RealtimeConfigResponse
                {
                    Enabled = true,
                    HubPath = NotificationHubPath.TrimStart('/'),
                },
                Assistant = new AssistantConfigResponse
                {
                    Enabled = config.Assistant?.IsEnabled == true,
                    Model = config.Assistant?.ModelName ?? "",
                    History = config.Assistant?.IsEnabled == true
                              && httpContext.RequestServices.GetService<TickerQ.Utilities.Interfaces.IAssistantHistoryStore>() != null,
                },
                Auth = new AuthInfoResponse
                {
                    Mode = config.Auth.Mode.ToString().ToLower(),
                    Enabled = config.Auth.IsEnabled,
                    SessionTimeout = config.Auth.SessionTimeoutMinutes,
                    LoginRedirect = config.Auth.HostLoginRedirectPath,
                }
            };

            var frontendJsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                TypeInfoResolverChain = { DashboardJsonSerializerContext.Default }
            };
            var json = JsonSerializer.Serialize(envConfig, frontendJsonOptions.GetTypeInfo(typeof(FrontendConfigResponse)));

            return $"(function(){{try{{window.TickerQConfig={json};}}catch(e){{console.error('TickerQ config failed:',e);}}}})();";
        }

        /// <summary>
        /// Injects a &lt;base&gt; tag and the runtime-config script into the HTML template.
        /// The &lt;base href&gt; makes the bundle's relative "./assets/..." URLs resolve under
        /// the configured mount path regardless of the current route depth.
        /// </summary>
        private static string InjectExternalScripts(string htmlTemplate, HttpContext httpContext, string basePath)
        {
            if (string.IsNullOrEmpty(htmlTemplate))
                return htmlTemplate ?? string.Empty;

            var pathBase = httpContext.Request.PathBase.HasValue
                ? httpContext.Request.PathBase.Value
                : string.Empty;

            var frontendBasePath = CombinePathBase(pathBase, basePath);

            var injection = $@"<base href=""{frontendBasePath}/"" />" +
                            @"<script src=""__tickerq-config.js""></script>";

            var headOpen = Regex.Match(htmlTemplate, "(?is)<head\\b[^>]*>");
            if (headOpen.Success)
                return htmlTemplate.Insert(headOpen.Index + headOpen.Length, injection);

            var closeIdx = htmlTemplate.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
            if (closeIdx >= 0)
                return htmlTemplate.Insert(closeIdx, injection);

            return injection + htmlTemplate;
        }

        private static string CombinePathBase(string pathBase, string basePath)
        {
            pathBase ??= string.Empty;
            basePath ??= "/";

            if (string.IsNullOrEmpty(basePath) || basePath == "/")
            {
                return string.IsNullOrEmpty(pathBase) ? "/" : pathBase;
            }

            if (string.IsNullOrEmpty(pathBase))
                return basePath;

            // If basePath already includes the pathBase prefix, treat it as the full frontend path.
            // This prevents /cool-app/cool-app/... and similar double-prefix issues when users
            // configure BasePath with the full URL segment.
            if (basePath.StartsWith(pathBase, StringComparison.OrdinalIgnoreCase))
                return basePath;

            // Inside a Map() branch, ASP.NET adds the matched segment to PathBase automatically.
            // So PathBase already ends with basePath (e.g. PathBase="/cool-app/dashboard",
            // basePath="/dashboard"). In this case, just return PathBase — it already is the
            // full frontend path. Without this check, we'd produce "/cool-app/dashboard/dashboard".
            if (pathBase.EndsWith(basePath, StringComparison.OrdinalIgnoreCase))
                return pathBase;

            // Normalize to avoid double slashes
            if (pathBase.EndsWith("/"))
                pathBase = pathBase.TrimEnd('/');

            // basePath is already normalized to start with '/'
            return pathBase + basePath;
        }

        /// <summary>
        /// Like <see cref="MapExtensions.Map(IApplicationBuilder, PathString, Action{IApplicationBuilder})"/>
        /// but handles the case where <paramref name="basePath"/> includes the application's PathBase prefix.
        /// When <c>UsePathBase("/cool-app")</c> runs before <c>UseTickerQ()</c>, ASP.NET strips the prefix
        /// from <c>Request.Path</c>. If the user configured <c>SetBasePath("/cool-app/dashboard")</c>, the
        /// standard <c>Map()</c> would never match because the request path is already <c>/dashboard</c>.
        /// This method detects and strips the PathBase prefix at request time so routing works regardless
        /// of middleware ordering.
        /// </summary>
        private static void MapPathBaseAware(this IApplicationBuilder app, string basePath, Action<IApplicationBuilder> configuration)
        {
            var branchBuilder = app.New();
            configuration(branchBuilder);
            var branch = branchBuilder.Build();

            app.Use(async (context, next) =>
            {
                var routePath = basePath;

                // If basePath includes the current PathBase prefix, strip it for route matching.
                // Example: basePath="/cool-app/dashboard", PathBase="/cool-app" → routePath="/dashboard"
                if (context.Request.PathBase.HasValue)
                {
                    var pathBaseValue = context.Request.PathBase.Value;
                    if (routePath.StartsWith(pathBaseValue, StringComparison.OrdinalIgnoreCase)
                        && routePath.Length > pathBaseValue.Length)
                    {
                        routePath = routePath.Substring(pathBaseValue.Length);
                    }
                }

                if (context.Request.Path.StartsWithSegments(routePath, out var matchedPath, out var remainingPath))
                {
                    var originalPath = context.Request.Path;
                    var originalPathBase = context.Request.PathBase;

                    // Mirror Map() behavior: move the matched segment from Path to PathBase
                    context.Request.PathBase = originalPathBase.Add(matchedPath);
                    context.Request.Path = remainingPath;

                    // Clear any endpoint matched by host-level routing so the branch's
                    // own UseRouting() re-evaluates against dashboard endpoints.
                    // Without this, host Map*() calls (e.g. MapStaticAssets().ShortCircuit())
                    // can cause the branch's routing middleware to skip evaluation — the
                    // EndpointRoutingMiddleware short-circuits when GetEndpoint() is non-null.
                    // This results in 405 responses for SignalR/WebSocket requests (#456).
                    context.SetEndpoint(null);
                    context.Request.RouteValues?.Clear();

                    try
                    {
                        await branch(context);
                    }
                    finally
                    {
                        context.Request.PathBase = originalPathBase;
                        context.Request.Path = originalPath;
                    }
                }
                else
                {
                    await next();
                }
            });
        }

    }
}
