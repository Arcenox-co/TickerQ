using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Grpc.Core;
using Grpc.Core.Interceptors;

namespace TickerQ.RemoteExecutor.GrpcServices;

/// <summary>
/// Server-side gRPC interceptor that validates the webhook signature on incoming
/// dashboard requests. Clients must include:
///   - x-tickerq-signature : base64(HMAC-SHA256(secret, method + "\n" + timestamp))
///   - x-tickerq-timestamp : unix seconds
/// No-op if <see cref="TickerQRemoteExecutionOptions.WebHookSignature"/> is empty.
/// </summary>
public sealed class DashboardAuthInterceptor : Interceptor
{
    private const long MaxSkewSeconds = 300;
    private const string SignatureHeader = "x-tickerq-signature";
    private const string TimestampHeader = "x-tickerq-timestamp";

    private readonly TickerQRemoteExecutionOptions _options;

    public DashboardAuthInterceptor(TickerQRemoteExecutionOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public override Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        EnsureAuthorized(context);
        return continuation(request, context);
    }

    public override Task<TResponse> ClientStreamingServerHandler<TRequest, TResponse>(
        IAsyncStreamReader<TRequest> requestStream,
        ServerCallContext context,
        ClientStreamingServerMethod<TRequest, TResponse> continuation)
    {
        EnsureAuthorized(context);
        return continuation(requestStream, context);
    }

    public override Task ServerStreamingServerHandler<TRequest, TResponse>(
        TRequest request,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        ServerStreamingServerMethod<TRequest, TResponse> continuation)
    {
        EnsureAuthorized(context);
        return continuation(request, responseStream, context);
    }

    public override Task DuplexStreamingServerHandler<TRequest, TResponse>(
        IAsyncStreamReader<TRequest> requestStream,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        DuplexStreamingServerMethod<TRequest, TResponse> continuation)
    {
        EnsureAuthorized(context);
        return continuation(requestStream, responseStream, context);
    }

    private void EnsureAuthorized(ServerCallContext context)
    {
        // The worker stream authenticates inside the Hello handshake (HMAC over
        // node_name+sdk_version+nonce+unix_seconds), not via per-call headers. Skip the
        // header check for it — SchedulerWorkerServiceImpl validates Hello.HmacSignature
        // before registering the stream. Method full name is
        // "/tickerq.worker.v1.SchedulerWorkerService/OpenWorkerStream", so match the
        // service name with its preceding '.' from the package qualifier.
        if (context.Method != null && context.Method.Contains(".SchedulerWorkerService/", StringComparison.Ordinal))
            return;

        // Signature validation is opt-in: skip if no secret configured.
        if (string.IsNullOrWhiteSpace(_options.WebHookSignature))
            return;

        var signature = GetHeader(context, SignatureHeader);
        var timestamp = GetHeader(context, TimestampHeader);

        if (string.IsNullOrEmpty(signature) || string.IsNullOrEmpty(timestamp))
            throw new RpcException(new Status(StatusCode.Unauthenticated, "Missing signature headers"));

        if (!long.TryParse(timestamp, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ts))
            throw new RpcException(new Status(StatusCode.Unauthenticated, "Invalid timestamp"));

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (Math.Abs(now - ts) > MaxSkewSeconds)
            throw new RpcException(new Status(StatusCode.Unauthenticated, "Timestamp outside allowed skew"));

        byte[] received;
        try
        {
            received = Convert.FromBase64String(signature);
        }
        catch (FormatException)
        {
            throw new RpcException(new Status(StatusCode.Unauthenticated, "Malformed signature"));
        }

        // Payload = grpc method full name + "\n" + timestamp
        var payload = Encoding.UTF8.GetBytes($"{context.Method}\n{timestamp}");
        var key = Encoding.UTF8.GetBytes(_options.WebHookSignature);
        var expected = HMACSHA256.HashData(key, payload);

        if (expected.Length != received.Length ||
            !CryptographicOperations.FixedTimeEquals(expected, received))
        {
            throw new RpcException(new Status(StatusCode.Unauthenticated, "Invalid signature"));
        }
    }

    private static string GetHeader(ServerCallContext context, string key)
    {
        foreach (var entry in context.RequestHeaders)
        {
            if (string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase))
                return entry.Value;
        }
        return null;
    }
}
