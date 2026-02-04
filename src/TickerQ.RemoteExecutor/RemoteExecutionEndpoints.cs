using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using TickerQ.RemoteExecutor.Models;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Models;

namespace TickerQ.RemoteExecutor;

/// <summary>
/// Extension methods for mapping HTTP endpoints that the TickerQ SDK uses.
/// These endpoints translate incoming HTTP calls into calls on TickerQ's
/// persistence layer and internal managers.
/// </summary>
public static class RemoteExecutionEndpoints
{
    /// <summary>
    /// Maps all TickerQ remote execution endpoints using the default entity types.
    /// </summary>
    public static IEndpointRouteBuilder MapTickerQRemoteExecutionEndpoints(
        this IEndpointRouteBuilder endpoints, string prefix = "")
    {
        return endpoints.MapTickerQRemoteExecutionEndpoints<TimeTickerEntity, CronTickerEntity>(prefix);
    }

    /// <summary>
    /// Maps all TickerQ remote execution endpoints for the specified ticker types.
    /// </summary>
    public static IEndpointRouteBuilder MapTickerQRemoteExecutionEndpoints<TTimeTicker, TCronTicker>(
        this IEndpointRouteBuilder endpoints, string prefix = "")
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        if (endpoints == null) throw new ArgumentNullException(nameof(endpoints));

        // Base group; the host can apply any path prefix by mapping this group under a route.
        var group = endpoints.MapGroup(prefix);

        group.MapPost("webhooks/hub",
            async (HttpRequest request,
                TickerQRemoteExecutionOptions options,
                RemoteFunctionsSyncService syncService,
                CancellationToken cancellationToken) =>
            {
                var authResult = await ValidateSignatureAsync(request, options, cancellationToken).ConfigureAwait(false);
                if (authResult != null)
                    return authResult;

                _ = syncService.SyncOnceAsync(CancellationToken.None);
                return Results.Ok();
            });

        group.MapPost("webhooks/hub/remove-function",
            async (HttpRequest request,
                TickerQRemoteExecutionOptions options,
                CancellationToken cancellationToken) =>
            {
                var authResult = await ValidateSignatureAsync(request, options, cancellationToken).ConfigureAwait(false);
                if (authResult != null)
                    return authResult;

                var payload = await request.ReadFromJsonAsync<RemoveFunctionPayload>(cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                if (payload == null || string.IsNullOrWhiteSpace(payload.FunctionName))
                    return Results.BadRequest("FunctionName is required.");

                var removed = RemoveFunctionByName(payload.FunctionName);
                return removed ? Results.Ok() : Results.NotFound();
            });

        MapFunctionRegistration(group);
        MapTimeTickerEndpoints<TTimeTicker, TCronTicker>(group);
        MapCronTickerEndpoints<TTimeTicker, TCronTicker>(group);
        MapCronOccurrenceEndpoints<TTimeTicker, TCronTicker>(group);

        return endpoints;
    }

    private static void MapFunctionRegistration(IEndpointRouteBuilder group)
    {
        group.MapPost("functions/register",
            async (HttpRequest request,
                TickerQRemoteExecutionOptions options,
                IInternalTickerManager internalTickerManager,
                CancellationToken cancellationToken) =>
            {
                var authResult = await ValidateSignatureAsync(request, options, cancellationToken).ConfigureAwait(false);
                if (authResult != null)
                    return authResult;

                var newFunctions = await request.ReadFromJsonAsync<RemoteTickerFunctionDescriptor[]>(cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (newFunctions == null || newFunctions.Length == 0)
                    return Results.Ok();

                var cronPairs = newFunctions
                    .Where(f => f.IsActive && !string.IsNullOrWhiteSpace(f.CronExpression))
                    .Select(f => (f.Name, f.CronExpression))
                    .ToArray();

                var functionDict = TickerFunctionProvider.TickerFunctions.ToDictionary();

                foreach (var newFunction in newFunctions)
                {
                    // Handle inactive functions by removing them
                    if (!newFunction.IsActive)
                    {
                        functionDict.Remove(newFunction.Name);
                        continue;
                    }

                    // Capture callback URL to avoid closure issues
                    var callbackUrl = newFunction.Callback.TrimEnd('/');

                    var newFunctionDelegate = new TickerFunctionDelegate(async (ct, serviceProvider, context) =>
                    {
                        var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
                        var remoteOptions = serviceProvider.GetRequiredService<TickerQRemoteExecutionOptions>();
                        var httpClient = httpClientFactory.CreateClient("tickerq-callback");

                        // Build a minimal payload describing the execution
                        var payload = new
                        {
                            context.Id,
                            context.FunctionName,
                            context.Type,
                            context.RetryCount,
                            context.ScheduledFor
                        };

                        // Serialize payload for signature computation
                        var json = JsonSerializer.Serialize(payload);
                        var bodyBytes = Encoding.UTF8.GetBytes(json);

                        // Build request with HMAC signature
                        var uri = new Uri($"{callbackUrl}/execute");
                        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
                        var signature = ComputeCallbackSignature(
                            remoteOptions.WebHookSignature,
                            HttpMethod.Post.Method,
                            uri.PathAndQuery,
                            timestamp,
                            bodyBytes);

                        using var request = new HttpRequestMessage(HttpMethod.Post, uri);
                        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                        request.Headers.Add("X-TickerQ-Signature", signature);
                        request.Headers.Add("X-Timestamp", timestamp);

                        using var response = await httpClient.SendAsync(request, ct);
                        response.EnsureSuccessStatusCode();
                    });

                    functionDict.TryAdd(newFunction.Name, (newFunction.CronExpression, newFunction.Priority, newFunctionDelegate));
                }
                
                TickerFunctionProvider.RegisterFunctions(functionDict);
                TickerFunctionProvider.Build();
                
                if (cronPairs.Length > 0)
                    await internalTickerManager.MigrateDefinedCronTickers(cronPairs, cancellationToken)
                        .ConfigureAwait(false);

                return Results.Ok();
            });
    }

    private static void MapTimeTickerEndpoints<TTimeTicker, TCronTicker>(IEndpointRouteBuilder group)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        group.MapPost("time-tickers",
            async (HttpRequest request,
                TickerQRemoteExecutionOptions options,
                ITickerPersistenceProvider<TTimeTicker, TCronTicker> provider,
                CancellationToken cancellationToken) =>
            {
                var authResult = await ValidateSignatureAsync(request, options, cancellationToken).ConfigureAwait(false);
                if (authResult != null)
                    return authResult;

                var tickers = await request.ReadFromJsonAsync<TTimeTicker[]>(cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                if (tickers == null)
                    return Results.BadRequest("Invalid payload");

                var affected = await provider.AddTimeTickers(tickers, cancellationToken).ConfigureAwait(false);
                return Results.Ok(affected);
            });

        group.MapGet("time-tickers/request/{id:guid}",
            async (Guid id,
                HttpRequest request,
                TickerQRemoteExecutionOptions options,
                ITickerPersistenceProvider<TTimeTicker, TCronTicker> provider,
                CancellationToken cancellationToken) =>
            {
                var authResult = await ValidateSignatureAsync(request, options, cancellationToken).ConfigureAwait(false);
                if (authResult != null)
                    return authResult;

                var requestBytes = await provider.GetTimeTickerRequest(id, cancellationToken).ConfigureAwait(false);
                if (requestBytes == null || requestBytes.Length == 0)
                    return Results.Bytes(Array.Empty<byte>(), "application/octet-stream");

                return Results.Bytes(requestBytes, "application/octet-stream");
            });

        group.MapPut("time-tickers",
            async (HttpRequest request,
                TickerQRemoteExecutionOptions options,
                ITickerPersistenceProvider<TTimeTicker, TCronTicker> provider,
                CancellationToken cancellationToken) =>
            {
                var authResult = await ValidateSignatureAsync(request, options, cancellationToken).ConfigureAwait(false);
                if (authResult != null)
                    return authResult;

                var tickers = await request.ReadFromJsonAsync<TTimeTicker[]>(cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                if (tickers == null)
                    return Results.BadRequest("Invalid payload");

                var affected = await provider.UpdateTimeTickers(tickers, cancellationToken).ConfigureAwait(false);
                return Results.Ok(affected);
            });

        group.MapPost("time-tickers/delete",
            async (HttpRequest request,
                TickerQRemoteExecutionOptions options,
                ITickerPersistenceProvider<TTimeTicker, TCronTicker> provider,
                CancellationToken cancellationToken) =>
            {
                var authResult = await ValidateSignatureAsync(request, options, cancellationToken).ConfigureAwait(false);
                if (authResult != null)
                    return authResult;

                var ids = await request.ReadFromJsonAsync<Guid[]>(cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                if (ids == null)
                    return Results.BadRequest("Invalid payload");

                var affected = await provider.RemoveTimeTickers(ids, cancellationToken).ConfigureAwait(false);
                return Results.Ok(affected);
            });

        group.MapPut("time-tickers/context",
            async (HttpRequest request,
                TickerQRemoteExecutionOptions options,
                IInternalTickerManager internalTickerManager,
                CancellationToken cancellationToken) =>
            {
                var authResult = await ValidateSignatureAsync(request, options, cancellationToken).ConfigureAwait(false);
                if (authResult != null)
                    return authResult;

                var context = await request.ReadFromJsonAsync<InternalFunctionContext>(cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                if (context == null)
                    return Results.BadRequest("Invalid payload");

                // Let InternalTickerManager route to the correct persistence methods and handle notifications.
                await internalTickerManager.UpdateTickerAsync(context, cancellationToken).ConfigureAwait(false);
                return Results.Ok(1);
            });

        group.MapPost("time-tickers/unified-context",
            async (HttpRequest request,
                TickerQRemoteExecutionOptions options,
                ITickerPersistenceProvider<TTimeTicker, TCronTicker> provider,
                CancellationToken cancellationToken) =>
            {
                var authResult = await ValidateSignatureAsync(request, options, cancellationToken).ConfigureAwait(false);
                if (authResult != null)
                    return authResult;

                var payload = await request.ReadFromJsonAsync<TimeTickerUnifiedContextRequest>(cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                if (payload?.Ids == null || payload.Context == null)
                    return Results.BadRequest("Ids and context are required.");

                await provider.UpdateTimeTickersWithUnifiedContext(payload.Ids, payload.Context, cancellationToken)
                    .ConfigureAwait(false);
                return Results.Ok();
            });
    }

    private static void MapCronTickerEndpoints<TTimeTicker, TCronTicker>(IEndpointRouteBuilder group)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        group.MapPost("cron-tickers",
            async (HttpRequest request,
                TickerQRemoteExecutionOptions options,
                ITickerPersistenceProvider<TTimeTicker, TCronTicker> provider,
                CancellationToken cancellationToken) =>
            {
                var authResult = await ValidateSignatureAsync(request, options, cancellationToken).ConfigureAwait(false);
                if (authResult != null)
                    return authResult;

                var tickers = await request.ReadFromJsonAsync<TCronTicker[]>(cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                if (tickers == null)
                    return Results.BadRequest("Invalid payload");

                var affected = await provider.InsertCronTickers(tickers, cancellationToken).ConfigureAwait(false);
                return Results.Ok(affected);
            });

        group.MapPut("cron-tickers",
            async (HttpRequest request,
                TickerQRemoteExecutionOptions options,
                ITickerPersistenceProvider<TTimeTicker, TCronTicker> provider,
                CancellationToken cancellationToken) =>
            {
                var authResult = await ValidateSignatureAsync(request, options, cancellationToken).ConfigureAwait(false);
                if (authResult != null)
                    return authResult;

                var tickers = await request.ReadFromJsonAsync<TCronTicker[]>(cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                if (tickers == null)
                    return Results.BadRequest("Invalid payload");

                var affected = await provider.UpdateCronTickers(tickers, cancellationToken).ConfigureAwait(false);
                return Results.Ok(affected);
            });

        group.MapPost("cron-tickers/delete",
            async (HttpRequest request,
                TickerQRemoteExecutionOptions options,
                ITickerPersistenceProvider<TTimeTicker, TCronTicker> provider,
                CancellationToken cancellationToken) =>
            {
                var authResult = await ValidateSignatureAsync(request, options, cancellationToken).ConfigureAwait(false);
                if (authResult != null)
                    return authResult;

                var ids = await request.ReadFromJsonAsync<Guid[]>(cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                if (ids == null)
                    return Results.BadRequest("Invalid payload");

                var affected = await provider.RemoveCronTickers(ids, cancellationToken).ConfigureAwait(false);
                return Results.Ok(affected);
            });
    }

    private static void MapCronOccurrenceEndpoints<TTimeTicker, TCronTicker>(IEndpointRouteBuilder group)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        group.MapPut("cron-ticker-occurrences/context",
            async (HttpRequest request,
                TickerQRemoteExecutionOptions options,
                IInternalTickerManager internalTickerManager,
                CancellationToken cancellationToken) =>
            {
                var authResult = await ValidateSignatureAsync(request, options, cancellationToken).ConfigureAwait(false);
                if (authResult != null)
                    return authResult;

                var context = await request.ReadFromJsonAsync<InternalFunctionContext>(cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                if (context == null)
                    return Results.BadRequest("Invalid payload");

                // Same as time tickers: delegate to InternalTickerManager.
                await internalTickerManager.UpdateTickerAsync(context, cancellationToken).ConfigureAwait(false);
                return Results.Ok();
            });

        group.MapGet("cron-ticker-occurrences/request/{id:guid}",
            async (Guid id,
                HttpRequest request,
                TickerQRemoteExecutionOptions options,
                ITickerPersistenceProvider<TTimeTicker, TCronTicker> provider,
                CancellationToken cancellationToken) =>
            {
                var authResult = await ValidateSignatureAsync(request, options, cancellationToken).ConfigureAwait(false);
                if (authResult != null)
                    return authResult;

                var requestBytes = await provider.GetCronTickerOccurrenceRequest(id, cancellationToken).ConfigureAwait(false);
                if (requestBytes == null || requestBytes.Length == 0)
                    return Results.Bytes(Array.Empty<byte>(), "application/octet-stream");

                return Results.Bytes(requestBytes, "application/octet-stream");
            });
    }

    private static async Task<IResult?> ValidateSignatureAsync(
        HttpRequest request,
        TickerQRemoteExecutionOptions options,
        CancellationToken cancellationToken)
    {
        const long maxSkewSeconds = 300;

        if (string.IsNullOrWhiteSpace(options.WebHookSignature))
            return null;

        var bodyBytes = await ReadBodyBytesAsync(request, cancellationToken).ConfigureAwait(false);

        if (!request.Headers.TryGetValue("X-TickerQ-Signature", out var sig))
            return Results.Unauthorized();

        if (!request.Headers.TryGetValue("X-Timestamp", out var timestampHeader))
            return Results.Unauthorized();

        var timestamp = timestampHeader.Count > 0 ? timestampHeader[0] : string.Empty;
        if (!long.TryParse(timestamp, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ts))
            return Results.Unauthorized();

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (Math.Abs(now - ts) > maxSkewSeconds)
            return Results.Unauthorized();

        byte[] received;
        try
        {
            received = Convert.FromBase64String(sig.ToString());
        }
        catch (FormatException)
        {
            return Results.Unauthorized();
        }

        var pathAndQuery = $"{request.Path}{request.QueryString}";
        var header = $"{request.Method}\n{pathAndQuery}\n{timestamp}\n";
        var headerBytes = Encoding.UTF8.GetBytes(header);
        var payload = new byte[headerBytes.Length + bodyBytes.Length];
        Buffer.BlockCopy(headerBytes, 0, payload, 0, headerBytes.Length);
        Buffer.BlockCopy(bodyBytes, 0, payload, headerBytes.Length, bodyBytes.Length);

        var key = Encoding.UTF8.GetBytes(options.WebHookSignature);
        var expected = HMACSHA256.HashData(key, payload);

        if (expected.Length != received.Length ||
            !CryptographicOperations.FixedTimeEquals(expected, received))
        {
            return Results.Unauthorized();
        }

        return null;
    }

    private static async Task<byte[]> ReadBodyBytesAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        request.EnableBuffering();
        if (request.Body.CanSeek)
            request.Body.Position = 0;

        await using var ms = new MemoryStream();
        await request.Body.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);

        if (request.Body.CanSeek)
            request.Body.Position = 0;

        return ms.ToArray();
    }

    private static bool RemoveFunctionByName(string functionName)
    {
        var functionDict = TickerFunctionProvider.TickerFunctions.ToDictionary();
        if (!functionDict.Remove(functionName))
            return false;

        TickerFunctionProvider.RegisterFunctions(functionDict);
        TickerFunctionProvider.Build();
        return true;
    }

    private sealed class TimeTickerUnifiedContextRequest
    {
        public Guid[] Ids { get; set; } = [];
        public InternalFunctionContext Context { get; set; }
    }

    private sealed class RemoveFunctionPayload
    {
        public string FunctionName { get; set; } = string.Empty;
    }

    private static string ComputeCallbackSignature(
        string secret,
        string method,
        string pathAndQuery,
        string timestamp,
        byte[] bodyBytes)
    {
        if (string.IsNullOrWhiteSpace(secret))
            return string.Empty;

        var header = $"{method}\n{pathAndQuery}\n{timestamp}\n";
        var headerBytes = Encoding.UTF8.GetBytes(header);
        var payload = new byte[headerBytes.Length + bodyBytes.Length];
        Buffer.BlockCopy(headerBytes, 0, payload, 0, headerBytes.Length);
        Buffer.BlockCopy(bodyBytes, 0, payload, headerBytes.Length, bodyBytes.Length);

        var secretKey = Encoding.UTF8.GetBytes(secret);
        var signatureBytes = HMACSHA256.HashData(secretKey, payload);
        return Convert.ToBase64String(signatureBytes);
    }
}
