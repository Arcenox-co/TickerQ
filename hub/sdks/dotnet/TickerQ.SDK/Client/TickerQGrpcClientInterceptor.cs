using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Core.Interceptors;

namespace TickerQ.SDK.Client;

internal sealed class TickerQGrpcClientInterceptor : Interceptor
{
    private const string ApiKeyMetadataKey = "x-api-key";
    private const string ApiSecretMetadataKey = "x-api-secret";
    private const string SignatureMetadataKey = "x-tickerq-signature";
    private const string TimestampMetadataKey = "x-tickerq-timestamp";

    private readonly TickerSdkOptions _options;

    public TickerQGrpcClientInterceptor(TickerSdkOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        return continuation(request, AddAuthMetadata(context, request));
    }

    public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncClientStreamingCallContinuation<TRequest, TResponse> continuation)
    {
        return continuation(AddAuthMetadata<TRequest, TResponse>(context, default));
    }

    public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncServerStreamingCallContinuation<TRequest, TResponse> continuation)
    {
        return continuation(request, AddAuthMetadata(context, request));
    }

    public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncDuplexStreamingCallContinuation<TRequest, TResponse> continuation)
    {
        return continuation(AddAuthMetadata<TRequest, TResponse>(context, default));
    }

    private ClientInterceptorContext<TRequest, TResponse> AddAuthMetadata<TRequest, TResponse>(
        ClientInterceptorContext<TRequest, TResponse> context,
        TRequest? request)
        where TRequest : class
        where TResponse : class
    {
        var metadata = context.Options.Headers ?? new Metadata();

        // API key/secret for hub authentication
        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
            metadata.Add(ApiKeyMetadataKey, _options.ApiKey);

        if (!string.IsNullOrWhiteSpace(_options.ApiSecret))
            metadata.Add(ApiSecretMetadataKey, _options.ApiSecret);

        // HMAC signature if webhook signature is available
        if (!string.IsNullOrWhiteSpace(_options.WebhookSignature))
        {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                .ToString(CultureInfo.InvariantCulture);

            var methodPath = context.Method?.FullName ?? string.Empty;
            var headerPart = $"{methodPath}\n{timestamp}\n";
            var headerBytes = Encoding.UTF8.GetBytes(headerPart);

            byte[] requestBytes = Array.Empty<byte>();
            if (request is IMessage protoMessage)
                requestBytes = protoMessage.ToByteArray();

            var payload = new byte[headerBytes.Length + requestBytes.Length];
            Buffer.BlockCopy(headerBytes, 0, payload, 0, headerBytes.Length);
            if (requestBytes.Length > 0)
                Buffer.BlockCopy(requestBytes, 0, payload, headerBytes.Length, requestBytes.Length);

            var key = Encoding.UTF8.GetBytes(_options.WebhookSignature);
            var signatureBytes = HMACSHA256.HashData(key, payload);

            metadata.Add(SignatureMetadataKey, Convert.ToBase64String(signatureBytes));
            metadata.Add(TimestampMetadataKey, timestamp);
        }

        var newOptions = context.Options.WithHeaders(metadata);
        return new ClientInterceptorContext<TRequest, TResponse>(context.Method, context.Host, newOptions);
    }
}
