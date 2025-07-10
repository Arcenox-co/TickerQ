#if NETCOREAPP3_1_OR_GREATER
using System.IO;
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using TickerQ.Dashboard.Controllers;
using TickerQ.Dashboard.Hubs;

namespace TickerQ.Dashboard.DependencyInjection
{
    internal static class NetTargetV3Higher
    {
        internal static void AddDashboardService(IServiceCollection services)
        {
            services.AddRouting();
            services.AddSignalR();
            services.AddCors(options =>
            {
                options.AddPolicy("Allow_TickerQ_Dashboard", policy =>
                {
                    policy.SetIsOriginAllowed(x => true)
                        .AllowAnyHeader()
                        .AllowAnyMethod()
                        .AllowCredentials();
                });
            });
            services.AddControllers().AddApplicationPart(typeof(TickerQController).Assembly);
        }

        internal static void UseDashboard(IApplicationBuilder app, string basePath)
        {
            // Get the assembly and set up the embedded file provider (adjust the namespace as needed)
            var assembly = Assembly.GetExecutingAssembly();
            var embeddedFileProvider = new EmbeddedFileProvider(assembly, "TickerQ.Dashboard.wwwroot.dist");

            // Ensure basePath starts with a "/" and does not end with one.
            if (string.IsNullOrEmpty(basePath))
                basePath = "/";
            if (!string.IsNullOrEmpty(basePath) && !basePath.StartsWith('/'))
                basePath = "/" + basePath;

            basePath = basePath.TrimEnd('/');

            // Map a branch for the basePath
            app.Map(basePath, dashboardApp =>
            {
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

                // Combine all endpoint registrations into one call
                dashboardApp.UseEndpoints(endpoints =>
                {
                    // Map controller routes (e.g., Home/Index)
                    endpoints.MapControllerRoute(
                        name: "default",
                        pattern: $"{basePath}{{controller=Home}}/{{action=Index}}/{{id?}}"
                    );

                    // Map the SignalR hub.
                    // Inside the branch, map with a relative path.
                    endpoints.MapHub<TickerQNotificationHub>("/ticker-notification-hub");
                });

                // SPA fallback middleware: if no route is matched, serve the modified index.html
                dashboardApp.Use(async (context, next) =>
                {
                    await next();

                    if (context.Response.StatusCode == 404 &&
                        context.Request.PathBase.Value.StartsWith(basePath))
                    {
                        var file = embeddedFileProvider.GetFileInfo("index.html");
                        if (file.Exists)
                        {
                            using var stream = file.CreateReadStream();
                            using var reader = new StreamReader(stream);
                            var htmlContent = await reader.ReadToEndAsync();

                            // Inject the base tag and other replacements into the HTML
                            htmlContent = ReplaceBasePath(htmlContent, basePath);

                            context.Response.ContentType = "text/html";
                            await context.Response.WriteAsync(htmlContent);
                        }
                    }
                });
            });
        }

        private static string ReplaceBasePath(string htmlContent, string basePath)
        {
            var regex = new System.Text.RegularExpressions.Regex("(src|href|action)=\"/(?!/)");
            htmlContent = regex.Replace(htmlContent, $"$1=\"{basePath}/");
            return htmlContent.Replace("__base_path__", basePath);
        }
    }
}
#endif