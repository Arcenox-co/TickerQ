using System.Net.Http;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TickerQ.SDK.Grpc.SdkControl;
using TickerQ.SDK.Infrastructure;
using TickerQ.Utilities;

namespace TickerQ.SDK.HostedServices;

/// <summary>
/// Maintains a persistent outbound gRPC bidi stream to the Hub's
/// <c>SdkControlService</c>. The SDK stays client-only; the Hub pushes
/// resync / remove-function commands down the stream. Auto-reconnects with
/// exponential backoff.
/// </summary>
internal sealed class TickerQSdkControlClient : BackgroundService
{
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(30);

    private readonly TickerSdkOptions _options;
    private readonly TickerQFunctionSyncService _syncService;
    private readonly ILogger<TickerQSdkControlClient> _logger;
    private GrpcChannel? _channel;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public TickerQSdkControlClient(
        TickerSdkOptions options,
        TickerQFunctionSyncService syncService,
        ILogger<TickerQSdkControlClient> logger)
    {
        _options = options;
        _syncService = syncService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            _logger.LogWarning("SDK control client: missing API token; disabled");
            return;
        }

        var hubUri = _options.HubControlUri;

        var delay = TimeSpan.FromSeconds(1);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(hubUri, stoppingToken).ConfigureAwait(false);
                delay = TimeSpan.FromSeconds(1);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "SDK control stream error; reconnecting in {Delay}", delay);
            }

            try { await Task.Delay(delay, stoppingToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
            delay = TimeSpan.FromSeconds(Math.Min(30, delay.TotalSeconds * 2));
        }
    }

    private async Task RunOnceAsync(Uri hubUri, CancellationToken stoppingToken)
    {
        _channel?.Dispose();
        var handler = new SocketsHttpHandler
        {
            EnableMultipleHttp2Connections = true,
            KeepAlivePingDelay = TimeSpan.FromSeconds(30),
            KeepAlivePingTimeout = TimeSpan.FromSeconds(30),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5)
        };
        handler.SslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;

        _channel = GrpcChannel.ForAddress(hubUri, new GrpcChannelOptions
        {
            HttpHandler = handler,
            MaxReceiveMessageSize = 16 * 1024 * 1024
        });

        var client = new SdkControlService.SdkControlServiceClient(_channel);
        using var call = client.Connect(cancellationToken: stoppingToken);

        await SendAsync(call.RequestStream, new SdkEvent
        {
            Register = new RegisterRequest
            {
                ApiKey = _options.ApiKey ?? string.Empty,
                NodeName = _options.NodeName,
                SdkVersion = "1.0.0"
            }
        }, stoppingToken);

        _logger.LogInformation("SDK control stream connected to {Hub}", hubUri);

        // Heartbeat loop (fire-and-forget; observed only for cancellation)
        var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var heartbeatTask = Task.Run(() => HeartbeatLoopAsync(call.RequestStream, heartbeatCts.Token), heartbeatCts.Token);

        try
        {
            while (await call.ResponseStream.MoveNext(stoppingToken).ConfigureAwait(false))
            {
                var cmd = call.ResponseStream.Current;
                switch (cmd.PayloadCase)
                {
                    case SdkCommand.PayloadOneofCase.RegisterAck:
                        _logger.LogInformation("SDK registered: node={NodeId}", cmd.RegisterAck.NodeId);
                        break;
                    case SdkCommand.PayloadOneofCase.TriggerResync:
                        _ = Task.Run(() => HandleTriggerResyncAsync(call.RequestStream, cmd.TriggerResync, stoppingToken));
                        break;
                    case SdkCommand.PayloadOneofCase.RemoveFunction:
                        _ = Task.Run(() => HandleRemoveFunctionAsync(call.RequestStream, cmd.RemoveFunction, stoppingToken));
                        break;
                    case SdkCommand.PayloadOneofCase.WebhookSignatureUpdated:
                        // Hub rotated the signature on the dashboard. TickerQSdkHttpClient
                        // and WorkerStreamHostedService both read _options.WebhookSignature
                        // on each call, so the new value takes effect immediately.
                        _options.WebhookSignature = cmd.WebhookSignatureUpdated.Signature;
                        _logger.LogInformation("Webhook signature rotated by Hub — applied in place");
                        break;
                    case SdkCommand.PayloadOneofCase.Disconnect:
                        _logger.LogInformation("Hub closed SDK control stream: {Reason}", cmd.Disconnect.Reason);
                        return;
                }
            }
        }
        finally
        {
            heartbeatCts.Cancel();
            try { await heartbeatTask.ConfigureAwait(false); } catch { }
            heartbeatCts.Dispose();
        }
    }

    private async Task HandleTriggerResyncAsync(
        IClientStreamWriter<SdkEvent> writer, TriggerResync trigger, CancellationToken ct)
    {
        var ack = new CommandAck { CommandId = trigger.CommandId };
        try
        {
            await _syncService.SyncAsync(ct).ConfigureAwait(false);
            ack.Success = true;
        }
        catch (Exception ex)
        {
            ack.Success = false;
            ack.Error = ex.Message;
            _logger.LogWarning(ex, "Resync failed for command {CommandId}", trigger.CommandId);
        }

        await SendAsync(writer, new SdkEvent { Ack = ack }, ct);
    }

    private async Task HandleRemoveFunctionAsync(
        IClientStreamWriter<SdkEvent> writer, RemoveFunction remove, CancellationToken ct)
    {
        var ack = new CommandAck { CommandId = remove.CommandId };
        try
        {
            if (!string.IsNullOrWhiteSpace(remove.FunctionName))
            {
                var dict = TickerFunctionProvider.TickerFunctions.ToDictionary();
                if (dict.Remove(remove.FunctionName))
                {
                    TickerFunctionProvider.RegisterFunctions(dict);
                    TickerFunctionProvider.Build();
                }
            }
            ack.Success = true;
        }
        catch (Exception ex)
        {
            ack.Success = false;
            ack.Error = ex.Message;
        }

        await SendAsync(writer, new SdkEvent { Ack = ack }, ct);
    }

    private async Task HeartbeatLoopAsync(IClientStreamWriter<SdkEvent> writer, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(HeartbeatInterval, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }

            try
            {
                await SendAsync(writer, new SdkEvent
                {
                    Heartbeat = new Heartbeat { UnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }
                }, ct);
            }
            catch
            {
                return; // stream is broken; outer loop will reconnect
            }
        }
    }

    private async Task SendAsync(IClientStreamWriter<SdkEvent> writer, SdkEvent evt, CancellationToken ct)
    {
        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await writer.WriteAsync(evt, ct).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public override void Dispose()
    {
        try { _channel?.Dispose(); } catch { }
        _writeLock.Dispose();
        base.Dispose();
    }
}
