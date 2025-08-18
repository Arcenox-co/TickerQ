#if NETCOREAPP3_1_OR_GREATER
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using TickerQ.Dashboard.Controllers;
using TickerQ.Dashboard.Hubs;
using System.Text.Json;

namespace TickerQ.Dashboard.DependencyInjection
{
    internal static class NetTargetV3Higher
    {
        internal static void AddDashboardService(IServiceCollection services, DashboardConfiguration config)
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

        internal static void UseDashboard(IApplicationBuilder app, string basePath, DashboardConfiguration config)
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

                // Redirect ticker asset requests that lack the basePath segment
                app.Use(async (context, next) =>
                {
                    if (context.Request.Path.StartsWithSegments("/tickerqassets") &&
                        !context.Request.Path.StartsWithSegments($"{basePath}/tickerqassets"))
                    {
                        var correctedPath = $"{basePath}{context.Request.Path}";
                        context.Response.Redirect(correctedPath);
                        return;
                    }

                    await next();
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
                        controllerRoute.RequireAuthorization();
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
                            using var stream = file.CreateReadStream();
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
            // Inject environment configuration (excluding sensitive credentials)
            var envConfig = new
            {
                basePath = basePath,
                backendDomain = config.BackendDomain,
                useHostAuthentication = config.UseHostAuthentication,
                enableBuiltInAuth = config.EnableBuiltInAuth,
                enableBasicAuth = config.EnableBasicAuth
            };
            
            var configScript = $"<script>window.TickerQConfig = {JsonSerializer.Serialize(envConfig)};</script>";
            htmlContent = htmlContent.Replace("</head>", $"{configScript}</head>");
            
            return htmlContent;
        }
    }
}
#endif