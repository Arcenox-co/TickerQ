using Grpc.Core;
using Grpc.Core.Interceptors;
using Grpc.Net.Client;

namespace TickerQ.SDK.Client;

/// <summary>
/// Provides a gRPC channel to hub.tickerq.net.
/// Initialized immediately (hub URL is known at startup).
/// </summary>
internal sealed class TickerQHubGrpcChannelProvider : IDisposable
{
    private readonly GrpcChannel _channel;
    private readonly CallInvoker _invoker;

    public TickerQHubGrpcChannelProvider(TickerSdkOptions options, TickerQGrpcClientInterceptor interceptor)
    {
        if (options == null) throw new ArgumentNullException(nameof(options));
        if (interceptor == null) throw new ArgumentNullException(nameof(interceptor));

        var hubUri = options.HubUri;
        _channel = GrpcChannel.ForAddress(hubUri, new GrpcChannelOptions
        {
            MaxReceiveMessageSize = 16 * 1024 * 1024,
            MaxSendMessageSize = 16 * 1024 * 1024
        });

        _invoker = _channel.Intercept(interceptor);
    }

    public CallInvoker GetChannel() => _invoker;

    public void Dispose()
    {
        _channel.Dispose();
    }
}
