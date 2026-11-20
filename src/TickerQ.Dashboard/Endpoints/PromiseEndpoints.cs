using System;
using System.Collections.Generic;
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
using TickerQ.Dashboard.Assistant;
using TickerQ.Utilities.Promises;

namespace TickerQ.Dashboard.Endpoints;

/// <summary>
/// Promises: list them and their function's jobs, check one now, change its settings, and fix or dismiss its findings. The list sits on the
/// read group; everything else on the write group, so read-only and license rules apply. Promises are declared in code.
/// </summary>
internal static class PromiseEndpoints
{
    private const int MaxBodyBytes = 16 * 1024;

    internal static void MapPromises(this RouteGroupBuilder reads, RouteGroupBuilder writes, DashboardOptionsBuilder config)
    {
        reads.MapGetAot("/promises", c => Execute(c, async service =>
        {
            var promises = await service.ListAsync(c.RequestAborted);
            var canManage = !config.IsReadOnlyFor(c);
            foreach (var promise in promises)
                promise.CanManage = canManage;
            await Write(c, promises, PromiseApiJsonContext.Default.IReadOnlyListTickerPromiseSnapshot);
        }));

        reads.MapGetAot("/promises/analytics", c => Execute(c, async service =>
            await Write(c, await service.AnalyticsAsync(null, Days(c), OffsetMinutes(c), c.RequestAborted),
                PromiseApiJsonContext.Default.IReadOnlyListTickerPromiseAnalytics)));

        reads.MapGetAot("/promises/{id}/analytics", c => Execute(c, async service =>
            await Write(c, await service.AnalyticsAsync(Id(c), Days(c), OffsetMinutes(c), c.RequestAborted),
                PromiseApiJsonContext.Default.IReadOnlyListTickerPromiseAnalytics)));

        reads.MapGetAot("/promises/{id}/jobs", c => Execute(c, async service =>
            await Write(c, await service.JobsAsync(Id(c), c.RequestAborted), PromiseApiJsonContext.Default.TickerPromiseJobList)));

        writes.MapPostAot("/promises/{id}/check", c => Execute(c, async service =>
            await Write(c, Managed(await service.CheckAsync(Id(c), c.RequestAborted)), PromiseApiJsonContext.Default.TickerPromiseSnapshot)));

        writes.MapPutAot("/promises/{id}/settings", c => Execute(c, async service =>
        {
            var change = ReadSettings(await ReadBody(c) ?? throw new TickerPromiseException(400, "A JSON request body is required."));
            var snapshot = await service.UpdateSettingsAsync(Id(c), change, User(c), c.RequestAborted);
            await Write(c, Managed(snapshot), PromiseApiJsonContext.Default.TickerPromiseSnapshot);
        }));

        writes.MapPostAot("/promises/{id}/findings/{findingId:guid}/fix", c => Execute(c, async service =>
        {
            var result = await service.FixAsync(Id(c), c.RequiredRouteGuid("findingId"), User(c), c.RequestAborted);
            Managed(result.Promise);
            await Write(c, result, PromiseApiJsonContext.Default.TickerPromiseFixResult);
        }));

        writes.MapPostAot("/promises/{id}/fix-all", c => Execute(c, async service =>
        {
            var result = await service.FixAllAsync(Id(c), User(c), c.RequestAborted);
            Managed(result.Promise);
            await Write(c, result, PromiseApiJsonContext.Default.TickerPromiseFixAllResult);
        }));

        writes.MapPostAot("/promises/{id}/findings/{findingId:guid}/dismiss", c => Execute(c, async service =>
        {
            var body = await ReadBody(c);
            var reason = body is { } bytes
                ? JsonSerializer.Deserialize(bytes, PromiseApiJsonContext.Default.DismissPromiseFindingRequest)?.Reason
                : null;
            var snapshot = await service.DismissAsync(Id(c), c.RequiredRouteGuid("findingId"), User(c), reason, c.RequestAborted);
            await Write(c, Managed(snapshot), PromiseApiJsonContext.Default.TickerPromiseSnapshot);
        }));
    }

    // Only the write group reaches these, so the caller may manage.
    private static TickerPromiseSnapshot Managed(TickerPromiseSnapshot snapshot)
    {
        snapshot.CanManage = true;
        return snapshot;
    }

    private static string Id(HttpContext c) => c.RequiredRouteString("id");

    private static int Days(HttpContext c) => IntQuery(c, "days", 14);

    private static int OffsetMinutes(HttpContext c) => IntQuery(c, "offsetMinutes", 0);

    private static int IntQuery(HttpContext c, string name, int fallback)
    {
        var value = c.Request.Query[name].ToString();
        if (value.Length == 0)
            return fallback;
        return int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : throw new TickerPromiseException(400, $"{name} must be a whole number.");
    }

    // Shown in the promise's activity, so prefer the name the user signed in with over the stable user ID.
    private static string? User(HttpContext c)
        => c.Items["auth.username"] as string is { Length: > 0 } name ? name : AssistantEndpoints.ResolveUserId(c);

    /// <summary>
    /// Reads <c>{ paused?, withinSeconds?, autoFix? }</c>. An absent property is left unchanged; a null
    /// <c>withinSeconds</c> or <c>autoFix</c> goes back to the code's value.
    /// </summary>
    private static TickerPromiseSettingsChange ReadSettings(byte[] body)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new TickerPromiseException(400, "The settings must be a JSON object.");
        var withinSet = Property(root, "withinSeconds", out var within);
        var autoFixSet = Property(root, "autoFix", out var autoFix);
        return new TickerPromiseSettingsChange
        {
            Paused = Property(root, "paused", out var paused) ? Bool(paused, "paused") : null,
            SetWithinSeconds = withinSet,
            WithinSeconds = !withinSet || within.ValueKind == JsonValueKind.Null ? null
                : within.TryGetInt32(out var seconds) ? seconds
                : throw new TickerPromiseException(400, "withinSeconds must be a whole number of seconds or null."),
            SetAutoFix = autoFixSet,
            AutoFix = autoFixSet ? Bool(autoFix, "autoFix") : null,
        };
    }

    private static bool? Bool(JsonElement value, string name) => value.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => throw new TickerPromiseException(400, $"{name} must be true, false or null."),
    };

    private static bool Property(JsonElement root, string name, out JsonElement value)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }
        value = default;
        return false;
    }

    /// <summary>The request body, or null when it is empty. A body must be JSON and at most 16 KB.</summary>
    private static async Task<byte[]?> ReadBody(HttpContext c)
    {
        if (c.Request.ContentLength > MaxBodyBytes)
            throw new TickerPromiseException(StatusCodes.Status413PayloadTooLarge, "The request body can be at most 16 KB.");
        using var body = new MemoryStream();
        var buffer = new byte[4096];
        int read;
        while ((read = await c.Request.Body.ReadAsync(buffer, c.RequestAborted)) > 0)
        {
            if (body.Length + read > MaxBodyBytes)
                throw new TickerPromiseException(StatusCodes.Status413PayloadTooLarge, "The request body can be at most 16 KB.");
            body.Write(buffer, 0, read);
        }
        if (body.Length == 0)
            return null;
        if (!c.Request.HasJsonContentType())
            throw new TickerPromiseException(StatusCodes.Status415UnsupportedMediaType, "Send the request body as application/json.");
        return body.ToArray();
    }

    private static Task Write<T>(HttpContext c, T value, JsonTypeInfo<T> type)
        => c.Response.WriteAsJsonAsync(value, type, cancellationToken: c.RequestAborted);

    private static async Task Execute(HttpContext c, Func<ITickerPromiseService, Task> action)
    {
        try
        {
            await action(c.RequestServices.GetRequiredService<ITickerPromiseService>());
        }
        catch (OperationCanceledException) when (c.RequestAborted.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            var (status, message) = ex switch
            {
                TickerPromiseException promise => (promise.StatusCode, promise.Message),
                JsonException => (400, "Invalid JSON."),
                _ => (500, $"The promise request failed. Reference: {c.TraceIdentifier}"),
            };
            if (status >= 500)
                c.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("TickerQ.Promises")
                    .LogError(ex, "Promise request failed. TraceIdentifier: {TraceIdentifier}", c.TraceIdentifier);
            c.Response.StatusCode = status;
            await Write(c, new PromiseApiError { Error = message }, PromiseApiJsonContext.Default.PromiseApiError);
        }
    }
}

internal sealed class DismissPromiseFindingRequest
{
    public string? Reason { get; set; }
}

internal sealed class PromiseApiError
{
    public string Error { get; set; } = "";
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(IReadOnlyList<TickerPromiseSnapshot>))]
[JsonSerializable(typeof(TickerPromiseSnapshot))]
[JsonSerializable(typeof(TickerPromiseJobList))]
[JsonSerializable(typeof(IReadOnlyList<TickerPromiseAnalytics>))]
[JsonSerializable(typeof(TickerPromiseFixResult))]
[JsonSerializable(typeof(TickerPromiseFixAllResult))]
[JsonSerializable(typeof(DismissPromiseFindingRequest))]
[JsonSerializable(typeof(PromiseApiError))]
internal partial class PromiseApiJsonContext : JsonSerializerContext;
