using System.Net.Http;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TickerQ.RemoteExecutor.Tunnel;
using TickerQ.RemoteExecutor;
using TickerQ.RemoteExecutor.WorkerStream;

namespace TickerQ.RemoteExecutor.TunnelClient;

/// <summary>
/// Maintains a persistent reverse-tunnel connection to the Hub. Reads <see cref="HubCommand"/>
/// messages and replays each as a local gRPC call, writing responses back as
/// <see cref="SchedulerEvent"/> messages. Auto-reconnects with exponential backoff.
/// </summary>
public sealed class TunnelClientHostedService : BackgroundService
{
    private readonly TunnelClientOptions _options;
    // Shared scheduler config — single source of truth for the cached webhook signature
    // used by Hub-bound HMAC and the worker-stream Hello check. Hub can rotate it via
    // a HubCommand; we mutate the property in place since downstream readers always
    // read fresh.
    private readonly TickerQRemoteExecutionOptions _executionOptions;
    private readonly ILogger<TunnelClientHostedService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private GrpcChannel? _hubChannel;
    private GrpcChannel? _localChannel;
    // gRPC client stream writers don't allow concurrent writes. Serialize all response
    // writes on the scheduler → hub stream so in-flight requests can't clobber each other.
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    // Active request stream writer when a tunnel session is open. The notification sender
    // pulls this to push NotificationEvent frames; null when disconnected (events drop on the floor —
    // the dashboard re-fetches on resume so eventual consistency is preserved).
    private volatile IClientStreamWriter<SchedulerEvent>? _activeWriter;
    public IClientStreamWriter<SchedulerEvent>? ActiveWriter => _activeWriter;
    public SemaphoreSlim WriteLock => _writeLock;

    public TunnelClientHostedService(
        TunnelClientOptions options,
        TickerQRemoteExecutionOptions executionOptions,
        IServiceProvider serviceProvider,
        ILogger<TunnelClientHostedService> logger)
    {
        _options = options;
        _executionOptions = executionOptions;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(_options.HubUrl) ||
            string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            _logger.LogWarning("Tunnel client: missing HubUrl/ApiKey; tunnel disabled");
            return;
        }

        // Allow HTTP/2 over plaintext (h2c) when Hub is on http://; gRPC requires HTTP/2.
        if (_options.HubUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

        var delay = TimeSpan.FromSeconds(1);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken).ConfigureAwait(false);
                delay = TimeSpan.FromSeconds(1); // reset backoff after clean disconnect
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Tunnel client error; reconnecting in {Delay}", delay);
            }

            try { await Task.Delay(delay, stoppingToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }

            delay = TimeSpan.FromSeconds(Math.Min(30, delay.TotalSeconds * 2));
        }
    }

    private async Task RunOnceAsync(CancellationToken stoppingToken)
    {
        _hubChannel?.Dispose();
        _hubChannel = GrpcChannel.ForAddress(_options.HubUrl, new GrpcChannelOptions
        {
            MaxReceiveMessageSize = 16 * 1024 * 1024,
            HttpHandler = BuildHubHandler()
        });

        var client = new TunnelService.TunnelServiceClient(_hubChannel);
        using var call = client.Connect(cancellationToken: stoppingToken);
        _activeWriter = call.RequestStream;

        await _writeLock.WaitAsync(stoppingToken).ConfigureAwait(false);
        try
        {
            var registerMsg = new RegisterRequest
            {
                ApiKey = _options.ApiKey,
                NodeName = _options.NodeName ?? string.Empty,
                SdkVersion = "1.0.0"
            };
            if (!string.IsNullOrWhiteSpace(_options.ApplicationUrl))
                registerMsg.ApplicationUrl = _options.ApplicationUrl;

            await call.RequestStream.WriteAsync(new SchedulerEvent
            {
                Register = registerMsg
            }, stoppingToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }

        _logger.LogInformation("Tunnel client connected to {HubUrl}", _options.HubUrl);

        // Publish current SDK snapshot now and on every change. The source of truth for
        // "live SDKs" is now WorkerStreamRegistry (one entry per open SDK worker stream).
        var workerRegistry = _serviceProvider.GetRequiredService<WorkerStreamRegistry>();
        Action publishSdks = () => _ = PublishSdkReportAsync(call.RequestStream, workerRegistry, stoppingToken);
        workerRegistry.Changed += publishSdks;
        publishSdks();
        try
        {

        while (await call.ResponseStream.MoveNext(stoppingToken).ConfigureAwait(false))
        {
            var cmd = call.ResponseStream.Current;
            switch (cmd.PayloadCase)
            {
                case HubCommand.PayloadOneofCase.RegisterAck:
                    _logger.LogInformation("Tunnel registered: env={EnvId}", cmd.RegisterAck.EnvironmentId);
                    break;
                case HubCommand.PayloadOneofCase.Request:
                    _ = Task.Run(() => HandleRequestAsync(call.RequestStream, cmd.Request, stoppingToken), stoppingToken);
                    break;
                case HubCommand.PayloadOneofCase.WebhookSignatureUpdated:
                    // Hub rotated the signature on the dashboard. Mutate the options
                    // property — DashboardAuthInterceptor / SchedulerWorkerServiceImpl
                    // both read it on every call, so the new value takes effect on the
                    // next signed request.
                    _executionOptions.WebHookSignature = cmd.WebhookSignatureUpdated.Signature;
                    _logger.LogInformation("Webhook signature rotated by Hub — applied in place");
                    break;
                case HubCommand.PayloadOneofCase.Disconnect:
                    _logger.LogInformation("Hub disconnected tunnel: {Reason}", cmd.Disconnect.Reason);
                    return;
            }
        }
        }
        finally
        {
            workerRegistry.Changed -= publishSdks;
            _activeWriter = null;
        }
    }

    private async Task PublishSdkReportAsync(IClientStreamWriter<SchedulerEvent> writer, WorkerStreamRegistry workerRegistry, CancellationToken ct)
    {
        try
        {
            var live = workerRegistry.All();
            _logger.LogInformation("Publishing SDK report: {Count} live worker streams", live.Count);
            var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var report = new SdkConnectionsReport();
            foreach (var w in live)
            {
                // No callback URL (pure-client SDK), no probe history. Liveness = "stream open".
                // Time-since-connect is reported as both LastSeen and LastSuccess so the Hub
                // dashboard's existing healthy-time logic still works.
                var connectedAtMs = new DateTimeOffset(w.ConnectedAt, TimeSpan.Zero).ToUnixTimeMilliseconds();
                report.Connections.Add(new SdkConnection
                {
                    NodeName = w.NodeName ?? string.Empty,
                    CallbackUrl = string.Empty,
                    FunctionCount = 0,
                    LastSeenUnixMs = nowMs,
                    IsHealthy = true,
                    LastSuccessUnixMs = connectedAtMs,
                    LastFailureUnixMs = 0,
                    LastError = string.Empty
                });
            }

            await _writeLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await writer.WriteAsync(new SchedulerEvent { Sdks = report }, ct).ConfigureAwait(false);
            }
            finally
            {
                _writeLock.Release();
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to publish SDK report through tunnel");
        }
    }

    private async Task HandleRequestAsync(
        IClientStreamWriter<SchedulerEvent> writer, CallRequest request, CancellationToken ct)
    {
        var response = new CallResponse { RequestId = request.RequestId };
        try
        {
            var channel = GetLocalChannel();
            var invoker = channel.CreateCallInvoker();

            var method = new Method<byte[], byte[]>(
                MethodType.Unary,
                ExtractServiceName(request.Method),
                ExtractMethodName(request.Method),
                BytesMarshaller,
                BytesMarshaller);

            var metadata = new Metadata();
            foreach (var m in request.Metadata)
                metadata.Add(m.Key, m.Value);

            using var call = invoker.AsyncUnaryCall(
                method, null, new CallOptions(metadata, deadline: DateTime.UtcNow.AddSeconds(30), cancellationToken: ct),
                request.Body.ToByteArray());

            var body = await call.ResponseAsync.ConfigureAwait(false);
            response.StatusCode = (int)StatusCode.OK;
            response.Body = ByteString.CopyFrom(body);
        }
        catch (RpcException rpcEx)
        {
            response.StatusCode = (int)rpcEx.StatusCode;
            response.StatusDetail = rpcEx.Status.Detail ?? string.Empty;
        }
        catch (Exception ex)
        {
            response.StatusCode = (int)StatusCode.Internal;
            response.StatusDetail = ex.Message;
        }

        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await writer.WriteAsync(new SchedulerEvent { Response = response }, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write tunnel response for {RequestId}", request.RequestId);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private GrpcChannel GetLocalChannel()
    {
        if (_localChannel != null) return _localChannel;
        var handler = new SocketsHttpHandler
        {
            EnableMultipleHttp2Connections = true,
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5)
        };
        if (_options.TrustLocalDevCert)
            handler.SslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;

        var localUrl = ResolveLocalGrpcUrl();
        _localChannel = GrpcChannel.ForAddress(localUrl, new GrpcChannelOptions
        {
            HttpHandler = handler,
            MaxReceiveMessageSize = 16 * 1024 * 1024
        });
        return _localChannel;
    }

    /// <summary>
    /// Resolves the loopback URL the tunnel client uses to replay inbound Hub
    /// gRPC calls against the local Kestrel. Read from <see cref="IServerAddressesFeature"/>
    /// so customers don't have to set it manually — whatever Kestrel actually
    /// bound to is what we dial back. Wildcard hosts (0.0.0.0, [::], +, *) are
    /// rewritten to localhost since we're calling from the same process.
    /// HTTPS preferred so HTTP/2 negotiates via ALPN.
    /// </summary>
    private string ResolveLocalGrpcUrl()
    {
        var server = _serviceProvider.GetService<IServer>();
        var addresses = server?.Features.Get<IServerAddressesFeature>()?.Addresses;
        if (addresses == null || addresses.Count == 0)
        {
            // Server hasn't bound yet, or we're not in a web host. Fall back to
            // the historical default so the rare case still has *some* answer.
            return "https://localhost:5100";
        }

        // Prefer HTTPS for ALPN HTTP/2; fall back to first plaintext URL.
        var pick = addresses.FirstOrDefault(a => a.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                   ?? addresses.First();
        return RewriteWildcardToLocalhost(pick);
    }

    private static string RewriteWildcardToLocalhost(string url)
    {
        // Kestrel surfaces wildcards as "https://+:5100", "http://[::]:5100",
        // or "http://0.0.0.0:5100". None are dialable — translate to localhost.
        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed)) return url;
        var host = parsed.Host;
        var isWildcard = host == "+" || host == "*" || host == "0.0.0.0" || host == "[::]" || host == "::";
        if (!isWildcard) return url;

        var builder = new UriBuilder(parsed) { Host = "localhost" };
        return builder.Uri.ToString();
    }

    private static HttpMessageHandler BuildHubHandler()
    {
        var handler = new SocketsHttpHandler
        {
            EnableMultipleHttp2Connections = true,
            KeepAlivePingDelay = TimeSpan.FromSeconds(30),
            KeepAlivePingTimeout = TimeSpan.FromSeconds(30),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5)
        };
        // Trust local dev certs when the hub is on loopback.
        handler.SslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;
        return handler;
    }

    private static string ExtractServiceName(string fullName)
    {
        // fullName like "/tickerq.dashboard.v1.DashboardService/GetTimeTickers"
        var trimmed = fullName.StartsWith("/") ? fullName.Substring(1) : fullName;
        var slash = trimmed.IndexOf('/');
        return slash > 0 ? trimmed.Substring(0, slash) : trimmed;
    }

    private static string ExtractMethodName(string fullName)
    {
        var slash = fullName.LastIndexOf('/');
        return slash >= 0 && slash < fullName.Length - 1 ? fullName.Substring(slash + 1) : string.Empty;
    }

    private static readonly Marshaller<byte[]> BytesMarshaller =
        Marshallers.Create(b => b, b => b);

    public override void Dispose()
    {
        try { _hubChannel?.Dispose(); } catch { }
        try { _localChannel?.Dispose(); } catch { }
        base.Dispose();
    }
}
