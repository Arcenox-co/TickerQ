#if !NETCOREAPP3_1_OR_GREATER

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
    internal static class NetTargetV31Lower
    {
        internal static void AddDashboardService(IServiceCollection services)
        {
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
            services.AddMvc(opt => opt.EnableEndpointRouting = false)
                .AddApplicationPart(typeof(TickerQController).Assembly);
        }
        
        internal static void UseDashboard(IApplicationBuilder app, string basePath)
        {
            var assembly = Assembly.GetExecutingAssembly();
            var embeddedFileProvider = new EmbeddedFileProvider(assembly, "TickerQ.Dashboard.wwwroot.dist");

            // Normalize the base path to ensure it starts with "/"
            if (string.IsNullOrEmpty(basePath))
                basePath = "/";
            if (!string.IsNullOrEmpty(basePath) && !basePath.StartsWith("/"))
                basePath = "/" + basePath;

            basePath = basePath.TrimEnd('/');

            // Map the base path
            app.Map(basePath, dashboardApp =>
            {
                // Serve static files
                dashboardApp.UseStaticFiles(new StaticFileOptions
                {
                    FileProvider = embeddedFileProvider
                });

                // Redirect requests for assets without the basePath
                app.Use(async (context, next) =>
                {
                    if (context.Request.Path.StartsWithSegments("/tickerqassets") &&
                        !context.Request.Path.StartsWithSegments($"{basePath}/tickerqassets"))
                    {
                        // Redirect the request to include the basePath
                        var correctedPath = $"{basePath}{context.Request.Path}";
                        context.Response.Redirect(correctedPath);
                        return;
                    }

                    await next();
                });
                
                dashboardApp.UseCors("Allow_TickerQ_Dashboard");
                
                dashboardApp.UseSignalR(routes =>
                {
                    routes.MapHub<TickerQNotificationHub>($"/ticker-notification-hub");
                });
                
                // SPA fallback
                dashboardApp.Use(async (context, next) =>
                {
                    await next();

                    if (context.Response.StatusCode == 404 &&
                        context.Request.PathBase.Value.StartsWith(basePath))
                    {
                        var file = embeddedFileProvider.GetFileInfo("index.html");
                        using var stream = file.CreateReadStream();
                        using var reader = new StreamReader(stream);
                        var htmlContent = await reader.ReadToEndAsync();

                        // Inject <base> tag into the <head> section
                        htmlContent = ReplaceBasePath(htmlContent, basePath);

                        // Write the modified HTML back to the response
                        context.Response.ContentType = "text/html";
                        await context.Response.WriteAsync(htmlContent);
                    }
                });

                dashboardApp.UseMvc(routes =>
                {
                    routes.MapRoute(
                        name: "default",
                        template: $"{basePath}{{controller=Home}}/{{action=Index}}/{{id?}}");
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