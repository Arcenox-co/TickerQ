using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Exceptions;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Models;

namespace TickerQ.RemoteExecutor;

/// <summary>
/// Opt-in HTTP compatibility surface for the Node SDK's scheduler persistence calls.
/// This deliberately excludes function registration, webhooks, queueing, and every Hub/gRPC route.
/// </summary>
public static class NodeCompatibilityEndpoints
{
    private const long MaxTimestampSkewSeconds = 300;
    private const int MaxRequestBodyBytes = 2 * 1024 * 1024;
    private const int MaxResultPayloadBytes = 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly Dictionary<string, string> ContextPropertyNames =
        typeof(InternalFunctionContext).GetProperties()
            .ToDictionary(property => property.Name, property => property.Name, StringComparer.OrdinalIgnoreCase);

    /// <summary>Maps the Node compatibility routes for TickerQ's default entity types.</summary>
    public static IEndpointRouteBuilder MapTickerQNodeCompatibilityEndpoints(
        this IEndpointRouteBuilder endpoints,
        string prefix = "")
        => endpoints.MapTickerQNodeCompatibilityEndpoints<TimeTickerEntity, CronTickerEntity>(prefix);

    /// <summary>
    /// Maps only the HTTP routes currently used by the Node SDK. Mapping is the explicit opt-in;
    /// all requests are authenticated with the Hub-provided webhook signature.
    /// </summary>
    public static IEndpointRouteBuilder MapTickerQNodeCompatibilityEndpoints<TTimeTicker, TCronTicker>(
        this IEndpointRouteBuilder endpoints,
        string prefix = "")
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var group = endpoints.MapGroup(NormalizePrefix(prefix));

        group.MapPost("/time-tickers", async (
            HttpContext http,
            TickerQRemoteExecutionOptions options,
            ITickerPersistenceProvider<TTimeTicker, TCronTicker> provider,
            CancellationToken cancellationToken) =>
            await HandleJsonAsync<TTimeTicker[]>(http, options, cancellationToken,
                (tickers, ct) => provider.AddTimeTickers(tickers, ct)));

        group.MapPut("/time-tickers", async (
            HttpContext http,
            TickerQRemoteExecutionOptions options,
            ITickerPersistenceProvider<TTimeTicker, TCronTicker> provider,
            CancellationToken cancellationToken) =>
            await HandleJsonAsync<TTimeTicker[]>(http, options, cancellationToken,
                (tickers, ct) => provider.UpdateTimeTickers(tickers, ct)));

        group.MapPost("/time-tickers/delete", async (
            HttpContext http,
            TickerQRemoteExecutionOptions options,
            ITickerPersistenceProvider<TTimeTicker, TCronTicker> provider,
            CancellationToken cancellationToken) =>
            await HandleJsonAsync<Guid[]>(http, options, cancellationToken,
                (ids, ct) => provider.RemoveTimeTickers(ids, ct)));

        group.MapPut("/time-tickers/context", async (
            HttpContext http,
            TickerQRemoteExecutionOptions options,
            IInternalTickerManager manager,
            CancellationToken cancellationToken) =>
            await HandleContextAsync(http, options, manager, TickerType.TimeTicker, cancellationToken));

        group.MapPost("/time-tickers/unified-context", async (
            HttpContext http,
            TickerQRemoteExecutionOptions options,
            ITickerPersistenceProvider<TTimeTicker, TCronTicker> provider,
            CancellationToken cancellationToken) =>
            await HandleJsonAsync<TimeTickerUnifiedContextRequest>(http, options, cancellationToken,
                async (request, ct) =>
                {
                    if (request.Ids is null || request.Context is null)
                        throw new ArgumentException("ids and context are required.");
                    await provider.UpdateTimeTickersWithUnifiedContext(request.Ids, request.Context, ct);
                    return 0;
                }));

        group.MapGet("/time-tickers/request/{id:guid}", async (
            Guid id,
            HttpContext http,
            TickerQRemoteExecutionOptions options,
            ITickerPersistenceProvider<TTimeTicker, TCronTicker> provider,
            CancellationToken cancellationToken) =>
            await HandleBytesAsync(http, options, cancellationToken,
                ct => provider.GetTimeTickerRequest(id, ct)));

        group.MapPost("/cron-tickers", async (
            HttpContext http,
            TickerQRemoteExecutionOptions options,
            ITickerPersistenceProvider<TTimeTicker, TCronTicker> provider,
            CancellationToken cancellationToken) =>
            await HandleJsonAsync<TCronTicker[]>(http, options, cancellationToken,
                (tickers, ct) => provider.InsertCronTickers(tickers, ct)));

        group.MapPut("/cron-tickers", async (
            HttpContext http,
            TickerQRemoteExecutionOptions options,
            ITickerPersistenceProvider<TTimeTicker, TCronTicker> provider,
            CancellationToken cancellationToken) =>
            await HandleJsonAsync<TCronTicker[]>(http, options, cancellationToken,
                (tickers, ct) => provider.UpdateCronTickers(tickers, ct)));

        group.MapPost("/cron-tickers/delete", async (
            HttpContext http,
            TickerQRemoteExecutionOptions options,
            ITickerPersistenceProvider<TTimeTicker, TCronTicker> provider,
            CancellationToken cancellationToken) =>
            await HandleJsonAsync<Guid[]>(http, options, cancellationToken,
                (ids, ct) => provider.RemoveCronTickers(ids, ct)));

        group.MapPut("/cron-ticker-occurrences/context", async (
            HttpContext http,
            TickerQRemoteExecutionOptions options,
            IInternalTickerManager manager,
            CancellationToken cancellationToken) =>
            await HandleContextAsync(http, options, manager, TickerType.CronTickerOccurrence, cancellationToken));

        group.MapGet("/cron-ticker-occurrences/request/{id:guid}", async (
            Guid id,
            HttpContext http,
            TickerQRemoteExecutionOptions options,
            ITickerPersistenceProvider<TTimeTicker, TCronTicker> provider,
            CancellationToken cancellationToken) =>
            await HandleBytesAsync(http, options, cancellationToken,
                ct => provider.GetCronTickerOccurrenceRequest(id, ct)));

        return endpoints;
    }

    private static string NormalizePrefix(string prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix) || prefix == "/")
            return string.Empty;
        return "/" + prefix.Trim('/');
    }

    private static async Task<IResult> HandleJsonAsync<T>(
        HttpContext http,
        TickerQRemoteExecutionOptions options,
        CancellationToken cancellationToken,
        Func<T, CancellationToken, Task<int>> action)
    {
        var authenticated = await AuthenticateAndReadBodyAsync(http, options, cancellationToken);
        if (authenticated.Error is not null)
            return authenticated.Error;

        try
        {
            var value = JsonSerializer.Deserialize<T>(authenticated.Body, JsonOptions);
            if (value is null)
                return Results.BadRequest("Invalid JSON payload.");
            return Results.Ok(await action(value, cancellationToken));
        }
        catch (JsonException)
        {
            return Results.BadRequest("Malformed JSON payload.");
        }
        catch (ArgumentException)
        {
            return Results.BadRequest("Invalid request payload.");
        }
        catch (NotSupportedException)
        {
            return Results.StatusCode(StatusCodes.Status501NotImplemented);
        }
        catch
        {
            return Results.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

    private static async Task<IResult> HandleContextAsync(
        HttpContext http,
        TickerQRemoteExecutionOptions options,
        IInternalTickerManager manager,
        TickerType expectedType,
        CancellationToken cancellationToken)
    {
        var authenticated = await AuthenticateAndReadBodyAsync(http, options, cancellationToken);
        if (authenticated.Error is not null)
            return authenticated.Error;

        try
        {
            var wire = JsonSerializer.Deserialize<NodeFunctionContextWire>(authenticated.Body, JsonOptions)
                ?? throw new ArgumentException("Context is required.");
            var context = MapContext(wire, expectedType);
            await manager.UpdateTickerAsync(context, cancellationToken);
            return Results.Ok(1);
        }
        catch (JsonException)
        {
            return Results.BadRequest("Malformed JSON payload.");
        }
        catch (FormatException)
        {
            return Results.BadRequest("Invalid result payload encoding.");
        }
        catch (ArgumentException)
        {
            return Results.BadRequest("Invalid or unsupported context payload.");
        }
        catch (TickerResultNotAcknowledgedException)
        {
            return Results.Conflict();
        }
        catch (NotSupportedException)
        {
            return Results.StatusCode(StatusCodes.Status501NotImplemented);
        }
        catch
        {
            return Results.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

    private static async Task<IResult> HandleBytesAsync(
        HttpContext http,
        TickerQRemoteExecutionOptions options,
        CancellationToken cancellationToken,
        Func<CancellationToken, Task<byte[]>> action)
    {
        var authenticated = await AuthenticateAndReadBodyAsync(http, options, cancellationToken);
        if (authenticated.Error is not null)
            return authenticated.Error;

        try
        {
            return Results.Bytes(await action(cancellationToken) ?? [], "application/octet-stream");
        }
        catch (NotSupportedException)
        {
            return Results.StatusCode(StatusCodes.Status501NotImplemented);
        }
        catch
        {
            return Results.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

    private static InternalFunctionContext MapContext(NodeFunctionContextWire wire, TickerType expectedType)
    {
        if (wire.TickerId == Guid.Empty)
            throw new ArgumentException("tickerId must be a non-empty UUID.");
        if (wire.AcquisitionToken is null || wire.AcquisitionToken == Guid.Empty)
            throw new ArgumentException("acquisitionToken must be a non-empty UUID.");
        if (wire.Type != expectedType)
            throw new ArgumentException("Context type does not match the route.");
        if (string.IsNullOrWhiteSpace(wire.FunctionName))
            throw new ArgumentException("functionName is required.");
        if (wire.ParametersToUpdate is null)
            throw new ArgumentException("parametersToUpdate is required.");

        var parameters = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in wire.ParametersToUpdate)
        {
            if (string.IsNullOrWhiteSpace(name) || !ContextPropertyNames.TryGetValue(name, out var canonical))
                throw new ArgumentException("parametersToUpdate contains an unknown property.");
            parameters.Add(canonical);
        }

        var isSuccess = wire.Status is TickerStatus.Done or TickerStatus.DueDone;
        var mutatesResult = parameters.Contains(nameof(InternalFunctionContext.ResultEnvelope));
        if (isSuccess && !mutatesResult)
            throw new ArgumentException("Successful terminal contexts must explicitly mutate ResultEnvelope.");
        if (!isSuccess && (mutatesResult || wire.ResultEnvelope.ValueKind != JsonValueKind.Undefined))
            throw new ArgumentException("Only successful terminal contexts may mutate ResultEnvelope.");
        if (isSuccess && wire.ResultEnvelope.ValueKind == JsonValueKind.Undefined)
            throw new ArgumentException("Successful terminal contexts must include ResultEnvelope, including explicit null.");

        var context = new InternalFunctionContext
        {
            ParametersToUpdate = parameters,
            CachedPriority = wire.CachedPriority,
            CachedMaxConcurrency = wire.CachedMaxConcurrency,
            FunctionName = wire.FunctionName,
            TickerId = wire.TickerId,
            ParentId = wire.ParentId,
            Type = wire.Type,
            Retries = wire.Retries,
            RetryCount = wire.RetryCount,
            Status = wire.Status,
            ElapsedTime = wire.ElapsedTime,
            ExceptionDetails = wire.ExceptionDetails,
            ExecutedAt = wire.ExecutedAt,
            RetryIntervals = wire.RetryIntervals ?? [],
            ReleaseLock = wire.ReleaseLock,
            ExecutionTime = wire.ExecutionTime,
            RunCondition = wire.RunCondition,
            AcquisitionToken = wire.AcquisitionToken
        };

        if (isSuccess)
            context.ResultEnvelope = wire.ResultEnvelope.ValueKind == JsonValueKind.Null
                ? null
                : MapEnvelope(wire.ResultEnvelope);

        return context;
    }

    private static TickerResultEnvelope MapEnvelope(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("resultEnvelope must be an object or null.");
        var wire = element.Deserialize<ResultEnvelopeWire>(JsonOptions)
            ?? throw new ArgumentException("resultEnvelope is required.");
        if (wire.EnvelopeVersion != TickerResultEnvelope.CurrentVersion)
            throw new ArgumentException("Unsupported result envelope version.");
        if (string.IsNullOrWhiteSpace(wire.MediaType))
            throw new ArgumentException("Result mediaType must be non-blank.");
        if (wire.ContractId is not null && string.IsNullOrWhiteSpace(wire.ContractId))
            throw new ArgumentException("Result contractId must be non-blank when supplied.");
        if (wire.ContractType is not null && string.IsNullOrWhiteSpace(wire.ContractType))
            throw new ArgumentException("Result contractType must be non-blank when supplied.");
        if (wire.Payload is null)
            throw new ArgumentException("Result payload is required.");

        var payload = Convert.FromBase64String(wire.Payload);
        if (!string.Equals(Convert.ToBase64String(payload), wire.Payload, StringComparison.Ordinal))
            throw new FormatException("Result payload must use canonical Base64.");
        if (payload.Length > MaxResultPayloadBytes)
            throw new ArgumentException("Result payload exceeds 1 MiB.");

        return new TickerResultEnvelope(
            payload,
            wire.EnvelopeVersion,
            wire.MediaType,
            wire.ContractId,
            wire.ContractType);
    }

    private static async Task<AuthenticatedBody> AuthenticateAndReadBodyAsync(
        HttpContext http,
        TickerQRemoteExecutionOptions options,
        CancellationToken cancellationToken)
    {
        var request = http.Request;
        if (string.IsNullOrWhiteSpace(options.WebHookSignature))
            return new([], Results.StatusCode(StatusCodes.Status503ServiceUnavailable));
        if (!request.Headers.TryGetValue("X-TickerQ-Signature", out var signature) ||
            string.IsNullOrWhiteSpace(signature) ||
            !request.Headers.TryGetValue("X-Timestamp", out var timestampHeader))
            return new([], Results.Unauthorized());

        var timestamp = timestampHeader.Count == 1 ? timestampHeader[0] : null;
        if (!long.TryParse(timestamp, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds) ||
            Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeSeconds() - seconds) > MaxTimestampSkewSeconds)
            return new([], Results.Unauthorized());
        if (request.ContentLength > MaxRequestBodyBytes)
            return new([], Results.StatusCode(StatusCodes.Status413PayloadTooLarge));

        byte[] received;
        try
        {
            received = Convert.FromBase64String(signature.ToString());
        }
        catch (FormatException)
        {
            return new([], Results.Unauthorized());
        }

        request.EnableBuffering();
        await using var stream = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await request.Body.ReadAsync(buffer, cancellationToken)) != 0)
        {
            if (stream.Length + read > MaxRequestBodyBytes)
                return new([], Results.StatusCode(StatusCodes.Status413PayloadTooLarge));
            await stream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        request.Body.Position = 0;
        var body = stream.ToArray();

        var pathAndQuery = $"{request.Path}{request.QueryString}";
        var header = Encoding.UTF8.GetBytes($"{request.Method}\n{pathAndQuery}\n{timestamp}\n");
        var signedPayload = new byte[header.Length + body.Length];
        Buffer.BlockCopy(header, 0, signedPayload, 0, header.Length);
        Buffer.BlockCopy(body, 0, signedPayload, header.Length, body.Length);
        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(options.WebHookSignature), signedPayload);
        if (received.Length != expected.Length || !CryptographicOperations.FixedTimeEquals(received, expected))
            return new([], Results.Unauthorized());

        return new(body, null);
    }

    private sealed record AuthenticatedBody(byte[] Body, IResult? Error);

    private sealed class TimeTickerUnifiedContextRequest
    {
        public Guid[]? Ids { get; set; }
        public InternalFunctionContext? Context { get; set; }
    }

    private sealed class NodeFunctionContextWire
    {
        public string[]? ParametersToUpdate { get; set; }
        public TickerTaskPriority CachedPriority { get; set; }
        public int CachedMaxConcurrency { get; set; }
        public string? FunctionName { get; set; }
        public Guid TickerId { get; set; }
        public Guid? ParentId { get; set; }
        public TickerType Type { get; set; }
        public int Retries { get; set; }
        public int RetryCount { get; set; }
        public TickerStatus Status { get; set; }
        public long ElapsedTime { get; set; }
        public string? ExceptionDetails { get; set; }
        public DateTime ExecutedAt { get; set; }
        public int[]? RetryIntervals { get; set; }
        public bool ReleaseLock { get; set; }
        public DateTime ExecutionTime { get; set; }
        public RunCondition RunCondition { get; set; }
        public Guid? AcquisitionToken { get; set; }
        public JsonElement ResultEnvelope { get; set; }
    }

    private sealed class ResultEnvelopeWire
    {
        public int EnvelopeVersion { get; set; }
        public string? MediaType { get; set; }
        public string? ContractId { get; set; }
        public string? ContractType { get; set; }
        public string? Payload { get; set; }
    }
}
