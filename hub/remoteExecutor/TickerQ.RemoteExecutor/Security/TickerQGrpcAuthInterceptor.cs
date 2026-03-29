using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.Extensions.Logging;

namespace TickerQ.RemoteExecutor.Security;

internal sealed class TickerQGrpcAuthInterceptor : Interceptor
{
    private const string SignatureMetadataKey = "x-tickerq-signature";
    private const string TimestampMetadataKey = "x-tickerq-timestamp";
    private const long MaxSkewSeconds = 300;

    private readonly TickerQRemoteExecutionOptions _options;
    private readonly ILogger<TickerQGrpcAuthInterceptor>? _logger;

    public TickerQGrpcAuthInterceptor(
        TickerQRemoteExecutionOptions options,
        ILogger<TickerQGrpcAuthInterceptor>? logger = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger;
    }

    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        ValidateHmac(context, request);
        return await continuation(request, context);
    }

    public override async Task<TResponse> ClientStreamingServerHandler<TRequest, TResponse>(
        IAsyncStreamReader<TRequest> requestStream,
        ServerCallContext context,
        ClientStreamingServerMethod<TRequest, TResponse> continuation)
    {
        ValidateHmac<TRequest>(context, default);
        return await continuation(requestStream, context);
    }

    public override async Task ServerStreamingServerHandler<TRequest, TResponse>(
        TRequest request,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        ServerStreamingServerMethod<TRequest, TResponse> continuation)
    {
        ValidateHmac(context, request);
        await continuation(request, responseStream, context);
    }

    public override async Task DuplexStreamingServerHandler<TRequest, TResponse>(
        IAsyncStreamReader<TRequest> requestStream,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        DuplexStreamingServerMethod<TRequest, TResponse> continuation)
    {
        ValidateHmac<TRequest>(context, default);
        await continuation(requestStream, responseStream, context);
    }

    private void ValidateHmac<TRequest>(ServerCallContext context, TRequest? request)
        where TRequest : class
    {
        if (string.IsNullOrWhiteSpace(_options.WebHookSignature))
        {
            _logger?.LogWarning(
                "TickerQ gRPC auth skipped: WebHookSignature not configured for {Method}",
                context.Method);
            return;
        }

        var signature = context.RequestHeaders.GetValue(SignatureMetadataKey);
        var timestampStr = context.RequestHeaders.GetValue(TimestampMetadataKey);

        if (string.IsNullOrWhiteSpace(signature) || string.IsNullOrWhiteSpace(timestampStr))
        {
            _logger?.LogWarning(
                "TickerQ gRPC auth failed: missing signature/timestamp metadata for {Method}",
                context.Method);
            throw new RpcException(new Status(StatusCode.Unauthenticated, "Missing authentication metadata."));
        }

        if (!long.TryParse(timestampStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ts))
        {
            throw new RpcException(new Status(StatusCode.Unauthenticated, "Invalid timestamp."));
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (Math.Abs(now - ts) > MaxSkewSeconds)
        {
            _logger?.LogWarning(
                "TickerQ gRPC auth failed: timestamp skew too large for {Method} (delta={Delta}s)",
                context.Method, Math.Abs(now - ts));
            throw new RpcException(new Status(StatusCode.Unauthenticated, "Request timestamp expired."));
        }

        byte[] received;
        try
        {
            received = Convert.FromBase64String(signature);
        }
        catch (FormatException)
        {
            throw new RpcException(new Status(StatusCode.Unauthenticated, "Invalid signature format."));
        }

        // Reconstruct HMAC payload: method_path + timestamp + serialized_request
        var methodPath = context.Method ?? string.Empty;
        var headerPart = $"{methodPath}\n{timestampStr}\n";
        var headerBytes = Encoding.UTF8.GetBytes(headerPart);

        byte[] requestBytes = Array.Empty<byte>();
        if (request is IMessage protoMessage)
            requestBytes = protoMessage.ToByteArray();

        var payload = new byte[headerBytes.Length + requestBytes.Length];
        Buffer.BlockCopy(headerBytes, 0, payload, 0, headerBytes.Length);
        if (requestBytes.Length > 0)
            Buffer.BlockCopy(requestBytes, 0, payload, headerBytes.Length, requestBytes.Length);

        var key = Encoding.UTF8.GetBytes(_options.WebHookSignature);
        var expected = HMACSHA256.HashData(key, payload);

        if (expected.Length != received.Length ||
            !CryptographicOperations.FixedTimeEquals(expected, received))
        {
            _logger?.LogWarning(
                "TickerQ gRPC auth failed: HMAC mismatch for {Method}",
                context.Method);
            throw new RpcException(new Status(StatusCode.Unauthenticated, "Invalid authentication credentials."));
        }
    }
}
