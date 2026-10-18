using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TickerQ.Utilities.Notifications;

namespace TickerQ.Dashboard.Endpoints;

internal static class NotificationEndpoints
{
    internal static void MapNotifications(this RouteGroupBuilder reads, RouteGroupBuilder writes, DashboardOptionsBuilder config)
    {
        reads.MapGet("/notifications", (HttpContext c) => Execute(c, async () =>
        {
            var service = c.RequestServices.GetService<ITickerNotificationAdminService>();
            var snapshot = service != null && config.CanManageNotifications(c)
                ? await service.GetAsync(c.RequestAborted)
                : new NotificationSnapshot { Available = service != null };
            snapshot.CanManage = service != null && config.CanManageNotifications(c) && !config.IsReadOnlyFor(c);
            await c.Response.WriteAsJsonAsync(snapshot, NotificationApiJsonContext.Default.NotificationSnapshot, cancellationToken: c.RequestAborted);
        }));
        writes.MapPut("/notifications", (HttpContext c) => Execute(c, async () =>
        {
            var service = RequireManagement(c, config);
            if (service == null) return;
            var request = await Read(c, NotificationApiJsonContext.Default.NotificationConfiguration);
            await service.SaveAsync(request, c.RequestAborted);
            c.Response.StatusCode = StatusCodes.Status204NoContent;
        }));
        writes.MapPost("/notifications/preview", (HttpContext c) => Execute(c, async () =>
        {
            var service = RequireManagement(c, config);
            if (service == null) return;
            var result = service.Preview(await Read(c, NotificationApiJsonContext.Default.NotificationPreviewRequest));
            await c.Response.WriteAsJsonAsync(result, NotificationApiJsonContext.Default.NotificationPreviewResponse, cancellationToken: c.RequestAborted);
        }));
        writes.MapPost("/notifications/destinations/{id:guid}/test", (HttpContext c) => Execute(c, async () =>
        {
            var service = RequireManagement(c, config);
            if (service == null) return;
            var id = Guid.Parse((string)c.Request.RouteValues["id"]!);
            var result = await service.TestAsync(id, c.RequestAborted);
            await c.Response.WriteAsJsonAsync(result, NotificationApiJsonContext.Default.NotificationDeliveryStatus, cancellationToken: c.RequestAborted);
        }));
    }

    private static ITickerNotificationAdminService? RequireManagement(HttpContext c, DashboardOptionsBuilder config)
    {
        if (!config.CanManageNotifications(c)) { c.Response.StatusCode = StatusCodes.Status403Forbidden; return null; }
        if (!c.Request.HasJsonContentType()) { c.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType; return null; }
        var service = c.RequestServices.GetService<ITickerNotificationAdminService>();
        if (service == null) c.Response.StatusCode = StatusCodes.Status404NotFound;
        return service;
    }

    private static async Task<T> Read<T>(HttpContext c, JsonTypeInfo<T> type)
    {
        const int limit = 256 * 1024;
        if (c.Request.ContentLength > limit) throw new BadHttpRequestException("Notification settings exceed 256 KB.", StatusCodes.Status413PayloadTooLarge);
        using var body = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await c.Request.Body.ReadAsync(buffer, c.RequestAborted)) > 0)
        {
            if (body.Length + read > limit) throw new BadHttpRequestException("Notification settings exceed 256 KB.", StatusCodes.Status413PayloadTooLarge);
            body.Write(buffer, 0, read);
        }
        return JsonSerializer.Deserialize(body.ToArray(), type) ?? throw new ArgumentException("A JSON request body is required.");
    }

    private static async Task Execute(HttpContext c, Func<Task> action)
    {
        try { await action(); }
        catch (OperationCanceledException) when (c.RequestAborted.IsCancellationRequested) { }
        catch (Exception ex)
        {
            var (status, message) = ex switch
            {
                NotificationConfigurationConflictException => (409, ex.Message),
                ArgumentException => (400, ex.Message),
                JsonException => (400, "Invalid notification JSON."),
                BadHttpRequestException bad => (bad.StatusCode, "Notification request is too large or invalid."),
                NotSupportedException => (503, "The operational store does not support notification settings."),
                _ => (503, "Notification settings are unavailable. Check the database schema and shared Data Protection keys.")
            };
            if (status >= 500) c.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("TickerQ.Notifications")
                .LogWarning("Notification administration failed ({ErrorType})", ex.GetType().Name);
            c.Response.StatusCode = status;
            await c.Response.WriteAsJsonAsync(new NotificationApiError { Error = message }, NotificationApiJsonContext.Default.NotificationApiError, cancellationToken: c.RequestAborted);
        }
    }
}

internal sealed class NotificationApiError { public string Error { get; set; } = ""; }
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(NotificationSnapshot))]
[JsonSerializable(typeof(NotificationConfiguration))]
[JsonSerializable(typeof(NotificationPreviewRequest))]
[JsonSerializable(typeof(NotificationPreviewResponse))]
[JsonSerializable(typeof(NotificationDeliveryStatus))]
[JsonSerializable(typeof(NotificationApiError))]
internal partial class NotificationApiJsonContext : JsonSerializerContext;
