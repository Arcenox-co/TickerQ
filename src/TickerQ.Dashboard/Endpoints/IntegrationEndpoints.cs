using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TickerQ.Utilities.Integrations;

namespace TickerQ.Dashboard.Endpoints;

internal static class IntegrationEndpoints
{
    internal static void MapIntegrations(this RouteGroupBuilder reads, RouteGroupBuilder writes, DashboardOptionsBuilder config)
    {
        reads.MapGet("/integrations", (HttpContext c) => Execute(c, async () =>
        {
            var service = c.RequestServices.GetService<ITickerIntegrationAdminService>();
            var snapshot = service != null
                ? await service.GetAsync(c.RequestAborted)
                : new IntegrationSnapshot { Available = service != null };
            snapshot.CanManage = service != null && config.CanManageIntegrations(c) && !config.IsReadOnlyFor(c);
            if (!snapshot.CanManage)
            {
                // Viewers can inspect health and delivery outcomes, without receiving
                // editable templates, endpoint metadata or credentials.
                snapshot.Configuration = new IntegrationConfiguration
                {
                    Destinations = snapshot.Configuration.Destinations.Select(d => new IntegrationDestination
                    { Id = d.Id, Name = d.Name, Provider = d.Provider, Enabled = d.Enabled }).ToList(),
                    Rules = snapshot.Configuration.Rules,
                    Monitors = snapshot.Configuration.Monitors,
                };
                snapshot.TemplateTokens = [];
                snapshot.RecentDeliveries = snapshot.RecentDeliveries.Select(d => new IntegrationDeliveryStatus
                { DestinationId = d.DestinationId, AtUtc = d.AtUtc, Outcome = d.Outcome }).ToList();
            }
            await c.Response.WriteAsJsonAsync(snapshot, IntegrationApiJsonContext.Default.IntegrationSnapshot, cancellationToken: c.RequestAborted);
        }));
        writes.MapPut("/integrations", (HttpContext c) => Execute(c, async () =>
        {
            var service = RequireManagement(c, config);
            if (service == null) return;
            var request = await Read(c, IntegrationApiJsonContext.Default.IntegrationConfiguration);
            await service.SaveAsync(request, c.RequestAborted);
            c.Response.StatusCode = StatusCodes.Status204NoContent;
        }));
        writes.MapPost("/integrations/preview", (HttpContext c) => Execute(c, async () =>
        {
            var service = RequireManagement(c, config);
            if (service == null) return;
            var result = service.Preview(await Read(c, IntegrationApiJsonContext.Default.IntegrationPreviewRequest));
            await c.Response.WriteAsJsonAsync(result, IntegrationApiJsonContext.Default.IntegrationPreviewResponse, cancellationToken: c.RequestAborted);
        }));
        writes.MapPost("/integrations/destinations/{id:guid}/test", (HttpContext c) => Execute(c, async () =>
        {
            var service = RequireManagement(c, config);
            if (service == null) return;
            var id = Guid.Parse((string)c.Request.RouteValues["id"]!);
            var result = await service.TestAsync(id, c.RequestAborted);
            await c.Response.WriteAsJsonAsync(result, IntegrationApiJsonContext.Default.IntegrationDeliveryStatus, cancellationToken: c.RequestAborted);
        }));
    }

    private static ITickerIntegrationAdminService? RequireManagement(HttpContext c, DashboardOptionsBuilder config)
    {
        if (!config.CanManageIntegrations(c)) { c.Response.StatusCode = StatusCodes.Status403Forbidden; return null; }
        if (!c.Request.HasJsonContentType()) { c.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType; return null; }
        var service = c.RequestServices.GetService<ITickerIntegrationAdminService>();
        if (service == null) c.Response.StatusCode = StatusCodes.Status404NotFound;
        return service;
    }

    private static async Task<T> Read<T>(HttpContext c, JsonTypeInfo<T> type)
    {
        const int limit = 256 * 1024;
        if (c.Request.ContentLength > limit) throw new BadHttpRequestException("Integration settings exceed 256 KB.", StatusCodes.Status413PayloadTooLarge);
        using var body = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await c.Request.Body.ReadAsync(buffer, c.RequestAborted)) > 0)
        {
            if (body.Length + read > limit) throw new BadHttpRequestException("Integration settings exceed 256 KB.", StatusCodes.Status413PayloadTooLarge);
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
                IntegrationConfigurationConflictException => (409, ex.Message),
                ArgumentException => (400, ex.Message),
                JsonException => (400, "Invalid integration JSON."),
                BadHttpRequestException bad => (bad.StatusCode, "Integration request is too large or invalid."),
                NotSupportedException => (503, "The operational store does not support integration settings."),
                _ => (503, "Integration settings are unavailable. Check the database schema and shared Data Protection keys.")
            };
            if (status >= 500) c.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("TickerQ.Integrations")
                .LogWarning("Integration administration failed ({ErrorType})", ex.GetType().Name);
            c.Response.StatusCode = status;
            await c.Response.WriteAsJsonAsync(new IntegrationApiError { Error = message }, IntegrationApiJsonContext.Default.IntegrationApiError, cancellationToken: c.RequestAborted);
        }
    }
}

internal sealed class IntegrationApiError { public string Error { get; set; } = ""; }
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(IntegrationSnapshot))]
[JsonSerializable(typeof(IntegrationConfiguration))]
[JsonSerializable(typeof(IntegrationPreviewRequest))]
[JsonSerializable(typeof(IntegrationPreviewResponse))]
[JsonSerializable(typeof(IntegrationDeliveryStatus))]
[JsonSerializable(typeof(IntegrationApiError))]
internal partial class IntegrationApiJsonContext : JsonSerializerContext;
