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
using System.Text.RegularExpressions;
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
                    Converters = { new StringToByteArrayConverter() }
                };
            }
            else
            {
                // Ensure StringToByteArrayConverter is always present
                if (!config.DashboardJsonOptions.Converters.Any(c => c is StringToByteArrayConverter))
                {
                    config.DashboardJsonOptions.Converters.Add(new StringToByteArrayConverter());
                }
            }
            
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

            // Map a branch for the basePath to properly isolate dashboard
            app.Map(basePath, dashboardApp =>
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

                // Set up routing and CORS
                dashboardApp.UseRouting();
                dashboardApp.UseCors("TickerQ_Dashboard_CORS");

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

                    if (context.Response.StatusCode == 404)
                    {
                        var file = embeddedFileProvider.GetFileInfo("index.html");
                        if (file.Exists)
                        {
                            await using var stream = file.CreateReadStream();
                            using var reader = new StreamReader(stream);
                            var htmlContent = await reader.ReadToEndAsync();

                            // Inject the base tag and other replacements into the HTML
                            htmlContent = ReplaceBasePath(htmlContent, basePath, config);

                            context.Response.ContentType = "text/html";
                            context.Response.StatusCode = 200;
                            await context.Response.WriteAsync(htmlContent);
                        }
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

        private static string ReplaceBasePath(string htmlContent, string basePath, DashboardOptionsBuilder config)
        {
            if (string.IsNullOrEmpty(htmlContent))
                return htmlContent ?? string.Empty;

            // Build the config object
            var authInfo = new
            {
                mode = config.Auth.Mode.ToString().ToLower(),
                enabled = config.Auth.IsEnabled,
                sessionTimeout = config.Auth.SessionTimeoutMinutes
            };
            
            var envConfig = new
            {
                basePath,
                backendDomain = config.BackendDomain,
                auth = authInfo
            };

            // Serialize without over-escaping, but make sure it won't break </script>
            var json = JsonSerializer.Serialize(
                envConfig,
                new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                });

            json = SanitizeForInlineScript(json);

            // Add base tag for proper asset loading
            var baseTag = $@"<base href=""{basePath}/"" />";

            // Inline bootstrap: set TickerQConfig and derive __dynamic_base__ (vite-plugin-dynamic-base)
            var script = $@"<script>
                (function() {{
                try {{
                    // Expose config
                    window.TickerQConfig = {json};

                    // Derive dynamic base for vite-plugin-dynamic-base
                    window.__dynamic_base__ = window.TickerQConfig.basePath;
                }} catch (e) {{ console.error('Runtime config injection failed:', e); }}
                }})();
                </script>";

            var fullInjection = baseTag + script;
            // Prefer inject immediately after opening <head ...>
            var headOpen = Regex.Match(htmlContent, "(?is)<head\\b[^>]*>");
            if (headOpen.Success)
            {
                return htmlContent.Insert(headOpen.Index + headOpen.Length, fullInjection);
            }

            // Fallback: just before </head>
            var closeIdx = htmlContent.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
            if (closeIdx >= 0)
            {
                return htmlContent.Insert(closeIdx, fullInjection);
            }

            // Last resort: prepend (ensures script runs early)
            return fullInjection + htmlContent;
        }

        /// <summary>
        /// Prevents &lt;/script&gt; in JSON strings from prematurely closing the inline script.
        /// </summary>
        private static string SanitizeForInlineScript(string json)
            => json.Replace("</script", "<\\/script", StringComparison.OrdinalIgnoreCase);
    }
}
