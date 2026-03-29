using Grpc.Core;
using Grpc.Core.Interceptors;
using Grpc.Net.Client;
using TickerQ.RemoteExecutor.Security;

namespace TickerQ.RemoteExecutor.Client;

/// <summary>
/// Provides a gRPC channel to hub.tickerq.net for the RemoteExecutor.
/// </summary>
internal sealed class TickerQHubGrpcChannelProvider : IDisposable
{
    private readonly GrpcChannel _channel;
    private readonly CallInvoker _invoker;

    public TickerQHubGrpcChannelProvider(
        TickerQRemoteExecutionOptions options,
        TickerQHubGrpcClientInterceptor interceptor)
    {
        if (options == null) throw new ArgumentNullException(nameof(options));
        if (interceptor == null) throw new ArgumentNullException(nameof(interceptor));

        var hubUri = new Uri(TickerQRemoteExecutorConstants.HubBaseUrl);
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
