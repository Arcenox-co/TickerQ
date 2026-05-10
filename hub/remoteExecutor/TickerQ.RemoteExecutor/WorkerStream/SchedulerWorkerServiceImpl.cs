using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Google.Protobuf;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using TickerQ.RemoteExecutor.Logging;
using TickerQ.RemoteExecutor.TunnelClient;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Models;
using TickerQ.Worker.V1;

namespace TickerQ.RemoteExecutor.WorkerStream;

/// <summary>
/// Server-side implementation of <c>SchedulerWorkerService.OpenWorkerStream</c>.
///
/// One bidi stream per SDK process. Authenticates the SDK at handshake using the
/// per-env <see cref="TickerQRemoteExecutionOptions.WebHookSignature"/> shared secret
/// (both Scheduler and SDK got the same value from Hub at their respective boots),
/// registers the connection in <see cref="WorkerStreamRegistry"/>, then runs a
/// read loop that demuxes inbound <see cref="WorkerEvent"/> frames and routes each
/// to the same <see cref="IInternalTickerManager"/> / <see cref="ITickerPersistenceProvider{TTimeTicker, TCronTicker}"/>
/// methods the legacy SchedulerSdkGrpcService used to call.
///
/// Outbound function dispatch is initiated externally (by RemoteExecutionDelegateFactory)
/// via <see cref="SchedulerWorkerConnection.ExecuteFunctionAsync"/> on the connection
/// returned by <see cref="WorkerStreamRegistry.PickForNode"/>.
/// </summary>
internal sealed class SchedulerWorkerServiceImpl<TTimeTicker, TCronTicker>
    : SchedulerWorkerService.SchedulerWorkerServiceBase
    where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
    where TCronTicker : CronTickerEntity, new()
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    // Replay window: ±300s skew. Same as today's per-call HMAC, just done once per stream.
    private static readonly TimeSpan ReplaySkew = TimeSpan.FromSeconds(300);

    private readonly TickerQRemoteExecutionOptions _options;
    private readonly WorkerStreamRegistry _registry;
    private readonly IInternalTickerManager _internalTickerManager;
    private readonly ITickerPersistenceProvider<TTimeTicker, TCronTicker> _provider;
    private readonly TickerLogRingBuffer _logBuffer;
    // Optional: only set when tunnel is enabled (NoOp sender otherwise).
    private readonly TunnelTickerQNotificationHubSender? _tunnelSender;
    private readonly ILogger<SchedulerWorkerServiceImpl<TTimeTicker, TCronTicker>> _logger;

    public SchedulerWorkerServiceImpl(
        TickerQRemoteExecutionOptions options,
        WorkerStreamRegistry registry,
        IInternalTickerManager internalTickerManager,
        ITickerPersistenceProvider<TTimeTicker, TCronTicker> provider,
        TickerLogRingBuffer logBuffer,
        ITickerQNotificationHubSender notificationSender,
        ILogger<SchedulerWorkerServiceImpl<TTimeTicker, TCronTicker>> logger)
    {
        _options = options;
        _registry = registry;
        _internalTickerManager = internalTickerManager;
        _provider = provider;
        _logBuffer = logBuffer;
        _tunnelSender = notificationSender as TunnelTickerQNotificationHubSender;
        _logger = logger;
    }

    public override async Task OpenWorkerStream(
        IAsyncStreamReader<WorkerEvent> requestStream,
        IServerStreamWriter<SchedulerCommand> responseStream,
        ServerCallContext context)
    {
        // ── 1. Read first frame, expect Hello ──
        if (!await requestStream.MoveNext(context.CancellationToken).ConfigureAwait(false))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Missing Hello frame"));

        var first = requestStream.Current;
        if (first.PayloadCase != WorkerEvent.PayloadOneofCase.Hello)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "First frame must be Hello"));

        var hello = first.Hello;
        if (string.IsNullOrWhiteSpace(hello.NodeName))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Hello.node_name is required"));

        // ── 2. Validate HMAC handshake ──
        ValidateHmacOrThrow(hello);

        // ── 3. Register the stream ──
        var workerId = Guid.NewGuid().ToString("N");
        // env/app/node ids are best-effort — Scheduler is per-env in this model but doesn't
        // hold the canonical Guids; passing what we know (empty if unknown) lets the SDK
        // continue without coupling to Hub at runtime.
        var conn = new SchedulerWorkerConnection(
            environmentId: Guid.Empty,
            applicationId: Guid.Empty,
            nodeApplicationId: Guid.Empty,
            nodeName: hello.NodeName,
            workerId: workerId,
            maxConcurrency: hello.MaxConcurrency > 0 ? hello.MaxConcurrency : 1,
            writer: responseStream);
        _registry.Register(conn);

        await responseStream.WriteAsync(new SchedulerCommand
        {
            HelloAck = new HelloAck { WorkerId = workerId }
        }).ConfigureAwait(false);

        _logger.LogInformation(
            "Worker stream registered: node={NodeName} worker={WorkerId} sdk_version={SdkVersion}",
            hello.NodeName, workerId, hello.SdkVersion);

        // ── 4. Read loop: demux inbound frames ──
        try
        {
            while (await requestStream.MoveNext(context.CancellationToken).ConfigureAwait(false))
            {
                var msg = requestStream.Current;
                // Process each inbound frame in a fire-and-forget task so a slow handler
                // doesn't block the read loop and starve the stream.
                _ = HandleEventAsync(conn, msg, context.CancellationToken);
            }
        }
        catch (OperationCanceledException) { /* client disconnect — expected */ }
        finally
        {
            _registry.Unregister(conn);
            conn.SignalClosed();
            _logger.LogInformation("Worker stream closed: node={NodeName} worker={WorkerId}",
                hello.NodeName, workerId);
        }
    }

    private async Task HandleEventAsync(SchedulerWorkerConnection conn, WorkerEvent msg, System.Threading.CancellationToken ct)
    {
        try
        {
            switch (msg.PayloadCase)
            {
                case WorkerEvent.PayloadOneofCase.Heartbeat:
                    // No-op for now — presence of any frame keeps the stream alive.
                    return;

                case WorkerEvent.PayloadOneofCase.ExecutionResult:
                    conn.CompleteExecution(msg.ExecutionResult);
                    return;

                case WorkerEvent.PayloadOneofCase.Ack:
                    conn.CompleteAck(msg.Ack);
                    return;

                case WorkerEvent.PayloadOneofCase.AddTimeTickers:
                    await HandleAddTimeTickers(conn, msg.AddTimeTickers, ct).ConfigureAwait(false);
                    return;

                case WorkerEvent.PayloadOneofCase.UpdateTimeTickers:
                    await HandleUpdateTimeTickers(conn, msg.UpdateTimeTickers, ct).ConfigureAwait(false);
                    return;

                case WorkerEvent.PayloadOneofCase.RemoveTimeTickers:
                    await HandleRemoveTimeTickers(conn, msg.RemoveTimeTickers, ct).ConfigureAwait(false);
                    return;

                case WorkerEvent.PayloadOneofCase.InsertCronTickers:
                    await HandleInsertCronTickers(conn, msg.InsertCronTickers, ct).ConfigureAwait(false);
                    return;

                case WorkerEvent.PayloadOneofCase.UpdateCronTickers:
                    await HandleUpdateCronTickers(conn, msg.UpdateCronTickers, ct).ConfigureAwait(false);
                    return;

                case WorkerEvent.PayloadOneofCase.RemoveCronTickers:
                    await HandleRemoveCronTickers(conn, msg.RemoveCronTickers, ct).ConfigureAwait(false);
                    return;

                case WorkerEvent.PayloadOneofCase.UpdateTimeTicker:
                    await HandleUpdateTimeTicker(conn, msg.UpdateTimeTicker, ct).ConfigureAwait(false);
                    return;

                case WorkerEvent.PayloadOneofCase.UpdateCronOccurrence:
                    await HandleUpdateCronOccurrence(conn, msg.UpdateCronOccurrence, ct).ConfigureAwait(false);
                    return;

                case WorkerEvent.PayloadOneofCase.UpdateTimeTickersUnified:
                    await HandleUpdateTimeTickersUnified(conn, msg.UpdateTimeTickersUnified, ct).ConfigureAwait(false);
                    return;

                case WorkerEvent.PayloadOneofCase.GetTimeTickerRequest:
                    await HandleGetTimeTickerRequest(conn, msg.GetTimeTickerRequest, ct).ConfigureAwait(false);
                    return;

                case WorkerEvent.PayloadOneofCase.GetCronOccurrenceRequest:
                    await HandleGetCronOccurrenceRequest(conn, msg.GetCronOccurrenceRequest, ct).ConfigureAwait(false);
                    return;

                case WorkerEvent.PayloadOneofCase.LogLine:
                    HandleLogLine(msg.LogLine);
                    return;

                case WorkerEvent.PayloadOneofCase.Hello:
                    // Hello on an established stream is a client bug; ignore.
                    return;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Worker event handler failed: {Case}", msg.PayloadCase);
            // Best-effort: send back an error result if the request had a request_id.
            var requestId = TryExtractRequestId(msg);
            if (!string.IsNullOrEmpty(requestId))
            {
                try
                {
                    await conn.WriteAsync(new SchedulerCommand
                    {
                        OperationResult = new OperationResult
                        {
                            RequestId = requestId,
                            Success = false,
                            Error = ex.Message
                        }
                    }, ct).ConfigureAwait(false);
                }
                catch { /* best-effort */ }
            }
        }
    }

    // ── CRUD handlers ──

    private async Task HandleAddTimeTickers(SchedulerWorkerConnection conn, AddTimeTickersRequest req, System.Threading.CancellationToken ct)
    {
        var entities = JsonSerializer.Deserialize<TTimeTicker[]>(req.EntitiesJson.ToByteArray(), Json) ?? Array.Empty<TTimeTicker>();
        var affected = await _provider.AddTimeTickers(entities, ct).ConfigureAwait(false);
        await SendOperationResult(conn, req.RequestId, affected, ct).ConfigureAwait(false);
    }

    private async Task HandleUpdateTimeTickers(SchedulerWorkerConnection conn, UpdateTimeTickersRequest req, System.Threading.CancellationToken ct)
    {
        var entities = JsonSerializer.Deserialize<TTimeTicker[]>(req.EntitiesJson.ToByteArray(), Json) ?? Array.Empty<TTimeTicker>();
        var affected = await _provider.UpdateTimeTickers(entities, ct).ConfigureAwait(false);
        await SendOperationResult(conn, req.RequestId, affected, ct).ConfigureAwait(false);
    }

    private async Task HandleRemoveTimeTickers(SchedulerWorkerConnection conn, RemoveTimeTickersRequest req, System.Threading.CancellationToken ct)
    {
        var ids = req.Ids.Select(Guid.Parse).ToArray();
        var affected = await _provider.RemoveTimeTickers(ids, ct).ConfigureAwait(false);
        await SendOperationResult(conn, req.RequestId, affected, ct).ConfigureAwait(false);
    }

    private async Task HandleInsertCronTickers(SchedulerWorkerConnection conn, InsertCronTickersRequest req, System.Threading.CancellationToken ct)
    {
        var entities = JsonSerializer.Deserialize<TCronTicker[]>(req.EntitiesJson.ToByteArray(), Json) ?? Array.Empty<TCronTicker>();
        var affected = await _provider.InsertCronTickers(entities, ct).ConfigureAwait(false);
        await SendOperationResult(conn, req.RequestId, affected, ct).ConfigureAwait(false);
    }

    private async Task HandleUpdateCronTickers(SchedulerWorkerConnection conn, UpdateCronTickersRequest req, System.Threading.CancellationToken ct)
    {
        var entities = JsonSerializer.Deserialize<TCronTicker[]>(req.EntitiesJson.ToByteArray(), Json) ?? Array.Empty<TCronTicker>();
        var affected = await _provider.UpdateCronTickers(entities, ct).ConfigureAwait(false);
        await SendOperationResult(conn, req.RequestId, affected, ct).ConfigureAwait(false);
    }

    private async Task HandleRemoveCronTickers(SchedulerWorkerConnection conn, RemoveCronTickersRequest req, System.Threading.CancellationToken ct)
    {
        var ids = req.Ids.Select(Guid.Parse).ToArray();
        var affected = await _provider.RemoveCronTickers(ids, ct).ConfigureAwait(false);
        await SendOperationResult(conn, req.RequestId, affected, ct).ConfigureAwait(false);
    }

    private async Task HandleUpdateTimeTicker(SchedulerWorkerConnection conn, UpdateTimeTickerRequest req, System.Threading.CancellationToken ct)
    {
        var ctx = MapToContext(req.Context);
        await _internalTickerManager.UpdateTickerAsync(ctx, ct).ConfigureAwait(false);
        // SDK-reported status update — emit lifecycle log line. (The local task
        // handler's update path bypasses this method, so its bogus dispatch-ack
        // doesn't trigger the synth.)
        _tunnelSender?.EmitLifecycleLine(ctx);
        await SendOperationResult(conn, req.RequestId, 1, ct).ConfigureAwait(false);
    }

    private async Task HandleUpdateCronOccurrence(SchedulerWorkerConnection conn, UpdateCronTickerOccurrenceRequest req, System.Threading.CancellationToken ct)
    {
        var ctx = MapToContext(req.Context);
        await _internalTickerManager.UpdateTickerAsync(ctx, ct).ConfigureAwait(false);
        _tunnelSender?.EmitLifecycleLine(ctx);
        await SendOperationResult(conn, req.RequestId, 1, ct).ConfigureAwait(false);
    }

    private async Task HandleUpdateTimeTickersUnified(SchedulerWorkerConnection conn, UpdateTimeTickersUnifiedRequest req, System.Threading.CancellationToken ct)
    {
        if (req.Ids.Count == 0 || req.Context == null)
        {
            await SendErrorResult(conn, req.RequestId, "ids and context are required", ct).ConfigureAwait(false);
            return;
        }
        var ctx = MapToContext(req.Context);
        var ids = req.Ids.Select(Guid.Parse).ToArray();
        await _provider.UpdateTimeTickersWithUnifiedContext(ids, ctx, ct).ConfigureAwait(false);
        if (_tunnelSender != null)
        {
            foreach (var id in ids)
            {
                // Re-target the context to each id so dedup state keys correctly.
                ctx.TickerId = id;
                _tunnelSender.EmitLifecycleLine(ctx);
            }
        }
        await SendOperationResult(conn, req.RequestId, ids.Length, ct).ConfigureAwait(false);
    }

    private async Task HandleGetTimeTickerRequest(SchedulerWorkerConnection conn, GetTimeTickerRequestRequest req, System.Threading.CancellationToken ct)
    {
        if (!Guid.TryParse(req.TickerId, out var tickerId))
        {
            await SendBytesError(conn, req.RequestId, "TickerId must be a GUID", ct).ConfigureAwait(false);
            return;
        }
        var bytes = await _provider.GetTimeTickerRequest(tickerId, ct).ConfigureAwait(false);
        await conn.WriteAsync(new SchedulerCommand
        {
            BytesResult = new BytesResult
            {
                RequestId = req.RequestId,
                Success = true,
                Found = bytes != null,
                Payload = bytes != null ? ByteString.CopyFrom(bytes) : ByteString.Empty
            }
        }, ct).ConfigureAwait(false);
    }

    private async Task HandleGetCronOccurrenceRequest(SchedulerWorkerConnection conn, GetCronTickerOccurrenceRequestRequest req, System.Threading.CancellationToken ct)
    {
        if (!Guid.TryParse(req.TickerId, out var tickerId))
        {
            await SendBytesError(conn, req.RequestId, "TickerId must be a GUID", ct).ConfigureAwait(false);
            return;
        }
        var bytes = await _provider.GetCronTickerOccurrenceRequest(tickerId, ct).ConfigureAwait(false);
        await conn.WriteAsync(new SchedulerCommand
        {
            BytesResult = new BytesResult
            {
                RequestId = req.RequestId,
                Success = true,
                Found = bytes != null,
                Payload = bytes != null ? ByteString.CopyFrom(bytes) : ByteString.Empty
            }
        }, ct).ConfigureAwait(false);
    }

    private void HandleLogLine(LogLine proto)
    {
        if (!Guid.TryParse(proto.TickerId, out var tickerId) || tickerId == Guid.Empty) return;

        var line = new TickerLogLine
        {
            TickerId = tickerId,
            TickerType = proto.TickerType,
            UnixMs = proto.UnixMs,
            Level = proto.Level,
            Source = string.IsNullOrEmpty(proto.Source) ? "sdk" : proto.Source,
            Message = proto.Message ?? string.Empty,
            Category = proto.Category ?? string.Empty,
            FunctionName = proto.FunctionName ?? string.Empty
        };

        // Always buffer (so dashboard tail-on-open works even when the tunnel is down).
        // Push through tunnel best-effort — sender is null when running without Hub
        // tunneling (in-process dashboard), in which case the buffer alone is enough.
        if (_tunnelSender != null)
            _ = _tunnelSender.PushTickerLogLineAsync(line);
        else
            _logBuffer.Append(line);
    }

    // ── Helpers ──

    private static Task SendOperationResult(SchedulerWorkerConnection conn, string requestId, int affected, System.Threading.CancellationToken ct)
        => conn.WriteAsync(new SchedulerCommand
        {
            OperationResult = new OperationResult
            {
                RequestId = requestId,
                Success = true,
                Affected = affected
            }
        }, ct);

    private static Task SendErrorResult(SchedulerWorkerConnection conn, string requestId, string error, System.Threading.CancellationToken ct)
        => conn.WriteAsync(new SchedulerCommand
        {
            OperationResult = new OperationResult
            {
                RequestId = requestId,
                Success = false,
                Error = error
            }
        }, ct);

    private static Task SendBytesError(SchedulerWorkerConnection conn, string requestId, string error, System.Threading.CancellationToken ct)
        => conn.WriteAsync(new SchedulerCommand
        {
            BytesResult = new BytesResult
            {
                RequestId = requestId,
                Success = false,
                Error = error
            }
        }, ct);

    private static InternalFunctionContext MapToContext(FunctionContext c)
        => new()
        {
            FunctionName = c.FunctionName,
            TickerId = Guid.TryParse(c.TickerId, out var tid) ? tid : Guid.Empty,
            ParentId = !string.IsNullOrEmpty(c.ParentId) && Guid.TryParse(c.ParentId, out var pid) ? pid : (Guid?)null,
            Type = (TickerType)c.Type,
            Retries = c.Retries,
            RetryCount = c.RetryCount,
            Status = (TickerStatus)c.Status,
            ElapsedTime = c.ElapsedTimeMs,
            ExceptionDetails = c.ExceptionDetails ?? string.Empty,
            ExecutedAt = c.ExecutedAt?.ToDateTime() ?? default,
            RetryIntervals = c.RetryIntervals.ToArray(),
            ReleaseLock = c.ReleaseLock,
            ExecutionTime = c.ExecutionTime?.ToDateTime() ?? default,
            RunCondition = (RunCondition)c.RunCondition,
            ParametersToUpdate = c.ParametersToUpdate.ToHashSet()
        };

    private static string? TryExtractRequestId(WorkerEvent msg) => msg.PayloadCase switch
    {
        WorkerEvent.PayloadOneofCase.AddTimeTickers => msg.AddTimeTickers.RequestId,
        WorkerEvent.PayloadOneofCase.UpdateTimeTickers => msg.UpdateTimeTickers.RequestId,
        WorkerEvent.PayloadOneofCase.RemoveTimeTickers => msg.RemoveTimeTickers.RequestId,
        WorkerEvent.PayloadOneofCase.InsertCronTickers => msg.InsertCronTickers.RequestId,
        WorkerEvent.PayloadOneofCase.UpdateCronTickers => msg.UpdateCronTickers.RequestId,
        WorkerEvent.PayloadOneofCase.RemoveCronTickers => msg.RemoveCronTickers.RequestId,
        WorkerEvent.PayloadOneofCase.UpdateTimeTicker => msg.UpdateTimeTicker.RequestId,
        WorkerEvent.PayloadOneofCase.UpdateCronOccurrence => msg.UpdateCronOccurrence.RequestId,
        WorkerEvent.PayloadOneofCase.UpdateTimeTickersUnified => msg.UpdateTimeTickersUnified.RequestId,
        WorkerEvent.PayloadOneofCase.GetTimeTickerRequest => msg.GetTimeTickerRequest.RequestId,
        WorkerEvent.PayloadOneofCase.GetCronOccurrenceRequest => msg.GetCronOccurrenceRequest.RequestId,
        _ => null
    };

    private void ValidateHmacOrThrow(Hello hello)
    {
        var secret = _options.WebHookSignature;
        if (string.IsNullOrEmpty(secret))
            throw new RpcException(new Status(StatusCode.FailedPrecondition,
                "Scheduler is not yet bootstrapped (WebhookSignature not loaded from Hub)"));

        // Replay window
        var nowSec = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (Math.Abs(nowSec - hello.UnixSeconds) > ReplaySkew.TotalSeconds)
            throw new RpcException(new Status(StatusCode.Unauthenticated, "Hello timestamp outside replay window"));

        if (string.IsNullOrEmpty(hello.HmacSignature))
            throw new RpcException(new Status(StatusCode.Unauthenticated, "Hello.hmac_signature is required"));

        var canonical = $"{hello.NodeName}\n{hello.SdkVersion}\n{hello.Nonce}\n{hello.UnixSeconds}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var computed = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(canonical)));

        // Fixed-time comparison
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(computed),
                Encoding.UTF8.GetBytes(hello.HmacSignature)))
        {
            throw new RpcException(new Status(StatusCode.Unauthenticated, "Hello HMAC signature invalid"));
        }
    }
}
