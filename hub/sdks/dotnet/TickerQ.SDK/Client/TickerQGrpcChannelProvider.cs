using Grpc.Core;
using Grpc.Core.Interceptors;
using Grpc.Net.Client;

namespace TickerQ.SDK.Client;

/// <summary>
/// Provides a lazily-initialized gRPC channel to the RemoteExecutor.
/// The channel is created after hub sync returns the scheduler URL.
/// </summary>
internal sealed class TickerQGrpcChannelProvider : IDisposable
{
    private readonly TickerSdkOptions _options;
    private readonly TickerQGrpcClientInterceptor _interceptor;
    private readonly TaskCompletionSource<CallInvoker> _channelReady = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private GrpcChannel? _channel;

    public TickerQGrpcChannelProvider(TickerSdkOptions options, TickerQGrpcClientInterceptor interceptor)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _interceptor = interceptor ?? throw new ArgumentNullException(nameof(interceptor));
    }

    /// <summary>
    /// Called after hub sync succeeds and the scheduler URL is available.
    /// </summary>
    public void Initialize()
    {
        if (_options.ApiUri == null)
            throw new InvalidOperationException("ApiUri must be set before initializing gRPC channel.");

        _channel = GrpcChannel.ForAddress(_options.ApiUri, new GrpcChannelOptions
        {
            MaxReceiveMessageSize = 16 * 1024 * 1024,
            MaxSendMessageSize = 16 * 1024 * 1024
        });

        var invoker = _channel.Intercept(_interceptor);
        _channelReady.TrySetResult(invoker);
    }

    public async Task<CallInvoker> GetChannelAsync()
    {
        return await _channelReady.Task.ConfigureAwait(false);
    }

    public CallInvoker? GetChannelIfReady()
    {
        return _channelReady.Task.IsCompletedSuccessfully ? _channelReady.Task.Result : null;
    }

    public void Dispose()
    {
        _channel?.Dispose();
    }
}
