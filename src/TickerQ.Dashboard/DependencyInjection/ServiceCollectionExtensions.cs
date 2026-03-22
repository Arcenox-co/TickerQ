using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using TickerQ.Dashboard.Authentication;
using TickerQ.Dashboard.Endpoints;
using TickerQ.Dashboard.Infrastructure;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Routing;
using TickerQ.Utilities.Entities;

namespace TickerQ.Dashboard.DependencyInjection
{
    internal static class ServiceCollectionExtensions
    {
        internal static void AddDashboardService<TTimeTicker, TCronTicker>(this IServiceCollection services, DashboardOptionsBuilder config)
            where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
            where TCronTicker : CronTickerEntity, new()
        {
            // Configure default Dashboard JSON options if not already configured
            if (config.DashboardJsonOptions == null)
            {
                config.DashboardJsonOptions = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    Converters = { new StringToByteArrayConverter() },
                    TypeInfoResolverChain = { DashboardJsonSerializerContext.Default, new DefaultJsonTypeInfoResolver() }
                };
            }
            else
            {
                // Ensure StringToByteArrayConverter is always present
                if (!config.DashboardJsonOptions.Converters.Any(c => c is StringToByteArrayConverter))
                {
                    config.DashboardJsonOptions.Converters.Add(new StringToByteArrayConverter());
                }

                // Ensure the source-generated context is in the resolver chain
                config.DashboardJsonOptions.TypeInfoResolverChain.Insert(0, DashboardJsonSerializerContext.Default);
            }
            
            // Register the dashboard configuration for DI
            services.AddSingleton(config);

            // Register source-generated context into ASP.NET's HTTP JSON options
            // so minimal API endpoint parameter binding works with reflection disabled
            services.ConfigureHttpJsonOptions(options =>
            {
                options.SerializerOptions.TypeInfoResolverChain.Insert(0, DashboardJsonSerializerContext.Default);
            });

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

            // Extract inline preload script from embedded index.html at startup.
            // Serving it as an external file allows CSP script-src 'self' without 'unsafe-inline'.
            string preloadScript = null;
            string htmlTemplate = null;
            var indexFile = embeddedFileProvider.GetFileInfo("index.html");
            if (indexFile.Exists)
            {
                using var stream = indexFile.CreateReadStream();
                using var reader = new StreamReader(stream);
                var rawHtml = reader.ReadToEnd();

                var scriptMatch = Regex.Match(rawHtml, @"<script>\s*([\s\S]*?)\s*</script>");
                if (scriptMatch.Success)
                {
                    preloadScript = scriptMatch.Groups[1].Value;
                    htmlTemplate = rawHtml.Remove(scriptMatch.Index, scriptMatch.Length);
                }
                else
                {
                    htmlTemplate = rawHtml;
                }
            }

            // Map a branch for the basePath with PathBase-aware routing.
            // Standard app.Map() fails when UsePathBase() runs before UseTickerQ() and the user
            // includes the PathBase prefix in SetBasePath() (e.g. SetBasePath("/cool-app/dashboard")
            // with UsePathBase("/cool-app")), because PathBase is already stripped from Request.Path.
            // This also handles the normal case where SetBasePath() contains only the dashboard segment.
            app.MapPathBaseAware(basePath, dashboardApp =>
            {
                // Execute pre-dashboard middleware
                config.PreDashboardMiddleware?.Invoke(dashboardApp);

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

                // Serve dashboard config and preload scripts as external files (before auth).
                // This eliminates inline scripts so the dashboard works with CSP script-src 'self'.
                dashboardApp.Use(async (context, next) =>
                {
                    var path = context.Request.Path.Value;

                    if (string.Equals(path, "/__tickerq-config.js", StringComparison.OrdinalIgnoreCase))
                    {
                        var configJs = GenerateConfigJs(context, basePath, config);
                        context.Response.ContentType = "application/javascript; charset=utf-8";
                        context.Response.Headers.CacheControl = "no-cache";
                        await context.Response.WriteAsync(configJs);
                        return;
                    }

                    if (string.Equals(path, "/__tickerq-preload.js", StringComparison.OrdinalIgnoreCase) && preloadScript != null)
                    {
                        context.Response.ContentType = "application/javascript; charset=utf-8";
                        context.Response.Headers.CacheControl = "public,max-age=3600";
                        await context.Response.WriteAsync(preloadScript);
                        return;
                    }

                    await next();
                });

                // Set up routing and CORS
                dashboardApp.UseRouting();
                dashboardApp.UseCors("TickerQ_Dashboard_CORS");
                
                // Set up authorization
                dashboardApp.UseAuthorization();

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
        /// Sets window.TickerQConfig and window.__dynamic_base__ for Vite dynamic base.
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
                Auth = new AuthInfoResponse
                {
                    Mode = config.Auth.Mode.ToString().ToLower(),
                    Enabled = config.Auth.IsEnabled,
                    SessionTimeout = config.Auth.SessionTimeoutMinutes
                }
            };

            var frontendJsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                TypeInfoResolverChain = { DashboardJsonSerializerContext.Default }
            };
            var json = JsonSerializer.Serialize(envConfig, frontendJsonOptions);

            return $"(function(){{try{{window.TickerQConfig={json};window.__dynamic_base__=window.TickerQConfig.basePath;}}catch(e){{console.error('TickerQ config failed:',e);}}}})();";
        }

        /// <summary>
        /// Injects base tag and external script references into the HTML template.
        /// Config must load before preload since the preload script uses window.__dynamic_base__.
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
                            @"<script src=""__tickerq-config.js""></script>" +
                            @"<script src=""__tickerq-preload.js""></script>";

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
