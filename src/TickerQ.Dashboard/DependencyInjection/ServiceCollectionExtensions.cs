using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using TickerQ.Dashboard.Controllers;
using TickerQ.Dashboard.Hubs;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;

namespace TickerQ.Dashboard.DependencyInjection
{
    internal static class ServiceCollectionExtensions
    {
        internal static void AddDashboardService(this IServiceCollection services, DashboardConfiguration config)
        {
            services.AddRouting();
            services.AddSignalR();

            services.AddCors(options =>
            {
                options.AddPolicy("Allow_TickerQ_Dashboard", policy =>
                {
                    if (config.CorsOrigins.Contains("*"))
                    {
                        policy.SetIsOriginAllowed(x => true);
                    }
                    else
                    {
                        policy.WithOrigins(config.CorsOrigins);
                    }

                    policy.AllowAnyHeader()
                        .AllowAnyMethod()
                        .AllowCredentials();
                });
            });

            services.AddControllers().AddApplicationPart(typeof(TickerQController).Assembly);
        }

        internal static void UseDashboard(this IApplicationBuilder app, string basePath, DashboardConfiguration config)
        {
            // Get the assembly and set up the embedded file provider (adjust the namespace as needed)
            var assembly = Assembly.GetExecutingAssembly();
            var embeddedFileProvider = new EmbeddedFileProvider(assembly, "TickerQ.Dashboard.wwwroot.dist");

            // Validate and normalize base path
            basePath = NormalizeBasePath(basePath);

            // Map a branch for the basePath
            app.Map(basePath, dashboardApp =>
            {
                // Execute custom middleware if provided
                config.CustomMiddleware?.Invoke(dashboardApp);

                // Serve static files from the embedded provider
                dashboardApp.UseStaticFiles(new StaticFileOptions
                {
                    FileProvider = embeddedFileProvider
                });

                // Set up routing and CORS for this branch
                dashboardApp.UseRouting();
                dashboardApp.UseCors("Allow_TickerQ_Dashboard");

                // Add authentication and authorization if using host authentication
                if (config.UseHostAuthentication)
                {
                    dashboardApp.UseAuthentication();
                    dashboardApp.UseAuthorization();
                }

                // Combine all endpoint registrations into one call
                dashboardApp.UseEndpoints(endpoints =>
                {
                    // Map controller routes (e.g., Home/Index)
                    var controllerRoute = endpoints.MapControllerRoute(
                        name: "default",
                        pattern: "{controller=Home}/{action=Index}/{id?}"
                    );

                    // Map the SignalR hub.
                    // Inside the branch, map with a relative path.
                    endpoints.MapHub<TickerQNotificationHub>("/ticker-notification-hub");


                    // Add role-based or policy-based authorization if specified
                    if (config.RequiredRoles.Any())
                    {
                        controllerRoute.RequireAuthorization(new AuthorizeAttribute()
                        {
                            Roles = string.Join(",", config.RequiredRoles)
                        });
                    }
                    else if (config.RequiredPolicies.Any())
                    {
                        controllerRoute.RequireAuthorization(config.RequiredPolicies);
                    }
                });

                // SPA fallback middleware: if no route is matched, serve the modified index.html
                dashboardApp.Use(async (context, next) =>
                {
                    await next();

                    if (context.Response.StatusCode == 404 &&
                        context.Request.PathBase.Value?.StartsWith(basePath) == true)
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

        private static string ReplaceBasePath(string htmlContent, string basePath, DashboardConfiguration config)
        {
            if (string.IsNullOrEmpty(htmlContent))
                return htmlContent ?? string.Empty;

            // Build the config object
            var envConfig = new
            {
                basePath,
                backendDomain = config.BackendDomain,
                useHostAuthentication = config.UseHostAuthentication,
                enableBuiltInAuth = config.EnableBuiltInAuth,
                enableBasicAuth = config.EnableBasicAuth
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
