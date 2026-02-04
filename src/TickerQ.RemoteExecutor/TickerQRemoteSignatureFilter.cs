using System.Security.Cryptography;
using System.Text;
using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace TickerQ.RemoteExecutor;

public sealed class TickerQRemoteSignatureFilter : IEndpointFilter
{
    private const long MaxTimestampSkewSeconds = 300;

    private readonly TickerQRemoteExecutionOptions _options;
    private readonly ILogger<TickerQRemoteSignatureFilter>? _logger;

    public TickerQRemoteSignatureFilter(TickerQRemoteExecutionOptions options, ILogger<TickerQRemoteSignatureFilter>? logger = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger;
    }

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext ctx,
        EndpointFilterDelegate next)
    {
        var request = ctx.HttpContext.Request;

        // If signature not configured, skip validation (but log warning)
        if (string.IsNullOrWhiteSpace(_options.WebHookSignature))
        {
            _logger?.LogWarning("TickerQ RemoteExecutor signature validation skipped: WebHookSignature not configured for {Method} {Path}",
                request.Method, request.Path);
            return await next(ctx);
        }

        // Validate required headers first (fail fast)
        if (!request.Headers.TryGetValue("X-TickerQ-Signature", out var sig) || string.IsNullOrWhiteSpace(sig))
        {
            _logger?.LogWarning("TickerQ RemoteExecutor signature validation failed: Missing X-TickerQ-Signature header for {Method} {Path}",
                request.Method, request.Path);
            return Results.Unauthorized();
        }

        if (!request.Headers.TryGetValue("X-Timestamp", out var timestampHeader))
        {
            _logger?.LogWarning("TickerQ RemoteExecutor signature validation failed: Missing X-Timestamp header for {Method} {Path}",
                request.Method, request.Path);
            return Results.Unauthorized();
        }

        var timestamp = timestampHeader.Count > 0 ? timestampHeader[0] : string.Empty;
        if (!long.TryParse(timestamp, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ts))
        {
            _logger?.LogWarning("TickerQ RemoteExecutor signature validation failed: Invalid timestamp format for {Method} {Path}",
                request.Method, request.Path);
            return Results.Unauthorized();
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (Math.Abs(now - ts) > MaxTimestampSkewSeconds)
        {
            _logger?.LogWarning("TickerQ RemoteExecutor signature validation failed: Timestamp skew too large ({SkewSeconds}s) for {Method} {Path}",
                Math.Abs(now - ts), request.Method, request.Path);
            return Results.Unauthorized();
        }

        // Parse signature with error handling
        byte[] received;
        try
        {
            received = Convert.FromBase64String(sig.ToString());
        }
        catch (FormatException)
        {
            _logger?.LogWarning("TickerQ RemoteExecutor signature validation failed: Invalid Base64 signature for {Method} {Path}",
                request.Method, request.Path);
            return Results.Unauthorized();
        }

        // Enable buffering and read body
        request.EnableBuffering();

        byte[] bodyBytes;
        await using (var ms = new MemoryStream())
        {
            await request.Body.CopyToAsync(ms, ctx.HttpContext.RequestAborted);
            bodyBytes = ms.ToArray();
            request.Body.Position = 0;
        }

        // Compute expected signature
        var pathAndQuery = $"{request.Path}{request.QueryString}";
        var header = $"{request.Method}\n{pathAndQuery}\n{timestamp}\n";
        var headerBytes = Encoding.UTF8.GetBytes(header);
        var payload = new byte[headerBytes.Length + bodyBytes.Length];
        Buffer.BlockCopy(headerBytes, 0, payload, 0, headerBytes.Length);
        Buffer.BlockCopy(bodyBytes, 0, payload, headerBytes.Length, bodyBytes.Length);

        var key = Encoding.UTF8.GetBytes(_options.WebHookSignature);
        var expected = HMACSHA256.HashData(key, payload);

        if (expected.Length != received.Length ||
            !CryptographicOperations.FixedTimeEquals(expected, received))
        {
            _logger?.LogWarning("TickerQ RemoteExecutor signature validation failed: Signature mismatch for {Method} {Path}",
                request.Method, request.Path);
            return Results.Unauthorized();
        }

        return await next(ctx);
    }
}
