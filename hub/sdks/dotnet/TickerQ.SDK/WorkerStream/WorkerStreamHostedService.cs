using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TickerQ.SDK.Infrastructure;
using TickerQ.SDK.Logging;
using TickerQ.Utilities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;
using TickerQ.Worker.V1;

namespace TickerQ.SDK.WorkerStream;

/// <summary>
/// SDK-side worker stream client. Opens one persistent bidi gRPC stream to the
/// Scheduler at <c>_options.ApiUri</c> (set by the Hub bootstrap function-sync) and
/// keeps it alive forever with auto-reconnect. Replaces both the SDK's gRPC server
/// (function dispatch was inbound to it) and the SDK's transient gRPC client to the
/// Scheduler (CRUD / status updates were unary outbound).
///
/// Once the stream is up, both directions multiplex:
///  - Scheduler pushes <see cref="ExecuteFunction"/> / <see cref="TriggerResync"/> /
///    <see cref="RemoveFunction"/> down the stream
///  - SDK pushes ticker CRUD / status updates / payload fetches up the stream;
///    callers (TickerQRemotePersistenceProvider) await replies correlated by request_id
///    via <see cref="SendAndAwaitOperationAsync"/> / <see cref="SendAndAwaitBytesAsync"/>
/// </summary>
internal sealed class WorkerStreamHostedService : BackgroundService
{
    private static readonly TimeSpan ReconnectMin = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ReconnectMax = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(15);

    private readonly TickerSdkOptions _options;
    private readonly TickerQFunctionSyncService _syncService;
    private readonly IServiceProvider _serviceProvider;
    private readonly TickerExecutionLogQueue _logQueue;
    private readonly ILogger<WorkerStreamHostedService> _logger;

    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<OperationResult>> _pendingOps = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<BytesResult>> _pendingBytes = new();
    // Currently-executing tickers — mapped to the per-execution CancellationTokenSource
    // we hand to [TickerFunction] methods. The scheduler can dispatch a CancelExecution
    // command (from a dashboard Cancel click) to signal one of these CTSes; the user's
    // method awaiting on the token then throws OperationCanceledException.
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _runningCts = new();

    // Flips to a fresh instance on every reconnect; .Task is awaited by the persistence
    // provider's SendAsync to avoid writing to a half-torn-down stream.
    private TaskCompletionSource<bool> _readyGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private volatile IClientStreamWriter<WorkerEvent>? _writer;
    private GrpcChannel? _channel;

    public WorkerStreamHostedService(
        TickerSdkOptions options,
        TickerQFunctionSyncService syncService,
        IServiceProvider serviceProvider,
        TickerExecutionLogQueue logQueue,
        ILogger<WorkerStreamHostedService> logger)
    {
        _options = options;
        _syncService = syncService;
        _serviceProvider = serviceProvider;
        _logQueue = logQueue;
        _logger = logger;
    }

    /// <summary>
    /// Sends an outbound WorkerEvent (typically an SDK-initiated CRUD/status request)
    /// and awaits the matching OperationResult by request_id.
    /// </summary>
    public async Task<OperationResult> SendAndAwaitOperationAsync(string requestId, WorkerEvent evt, TimeSpan timeout, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<OperationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingOps[requestId] = tcs;
        try
        {
            await SendAsync(evt, ct).ConfigureAwait(false);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linked.CancelAfter(timeout);
            using var _ = linked.Token.Register(() =>
            {
                if (_pendingOps.TryRemove(requestId, out var s))
                    s.TrySetException(new TimeoutException($"Scheduler did not return OperationResult for {requestId} within {timeout}"));
            });
            return await tcs.Task.ConfigureAwait(false);
        }
        finally
        {
            _pendingOps.TryRemove(requestId, out _);
        }
    }

    /// <summary>Same as <see cref="SendAndAwaitOperationAsync"/> but for BytesResult replies (payload fetches).</summary>
    public async Task<BytesResult> SendAndAwaitBytesAsync(string requestId, WorkerEvent evt, TimeSpan timeout, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<BytesResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingBytes[requestId] = tcs;
        try
        {
            await SendAsync(evt, ct).ConfigureAwait(false);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linked.CancelAfter(timeout);
            using var _ = linked.Token.Register(() =>
            {
                if (_pendingBytes.TryRemove(requestId, out var s))
                    s.TrySetException(new TimeoutException($"Scheduler did not return BytesResult for {requestId} within {timeout}"));
            });
            return await tcs.Task.ConfigureAwait(false);
        }
        finally
        {
            _pendingBytes.TryRemove(requestId, out _);
        }
    }

    private async Task SendAsync(WorkerEvent evt, CancellationToken ct)
    {
        // Block until a stream is up.
        await _readyGate.Task.WaitAsync(ct).ConfigureAwait(false);
        var writer = _writer
            ?? throw new InvalidOperationException("Worker stream is not connected");

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

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var backoff = ReconnectMin;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Ensure bootstrap (Hub function-sync) has completed and ApiUri /
                // WebhookSignature are populated. ApiUri is null until SyncAsync
                // returns and overwrites it with the env's ApplicationUrl.
                if (_options.ApiUri == null || string.IsNullOrEmpty(_options.WebhookSignature))
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(500), stoppingToken).ConfigureAwait(false);
                    continue;
                }

                await RunOnceAsync(stoppingToken).ConfigureAwait(false);
                backoff = ReconnectMin; // clean disconnect — reset backoff
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Worker stream error; reconnecting in {Backoff}", backoff);
            }

            try { await Task.Delay(backoff, stoppingToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }

            backoff = TimeSpan.FromSeconds(Math.Min(ReconnectMax.TotalSeconds, backoff.TotalSeconds * 2));
        }
    }

    private async Task RunOnceAsync(CancellationToken stoppingToken)
    {
        // Build (or rebuild) the channel.
        _channel?.Dispose();
        _channel = BuildChannel(_options.ApiUri!.ToString());

        var client = new SchedulerWorkerService.SchedulerWorkerServiceClient(_channel);
        using var call = client.OpenWorkerStream(cancellationToken: stoppingToken);
        _writer = call.RequestStream;

        try
        {
            // ── Send Hello ──
            await SendHelloAsync(stoppingToken).ConfigureAwait(false);

            // ── Await HelloAck before flipping ready ──
            if (!await call.ResponseStream.MoveNext(stoppingToken).ConfigureAwait(false))
                throw new InvalidOperationException("Worker stream closed before HelloAck");
            var first = call.ResponseStream.Current;
            if (first.PayloadCase != SchedulerCommand.PayloadOneofCase.HelloAck)
                throw new InvalidOperationException($"Expected HelloAck, got {first.PayloadCase}");

            _logger.LogInformation("Worker stream registered: worker={WorkerId}", first.HelloAck.WorkerId);

            // Flip the gate AFTER ack so persistence provider doesn't write before scheduler is ready.
            _readyGate.TrySetResult(true);

            // ── Heartbeat task (fire-and-forget for the duration of this stream) ──
            using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            var heartbeatTask = Task.Run(async () =>
            {
                try
                {
                    while (!heartbeatCts.IsCancellationRequested)
                    {
                        await Task.Delay(HeartbeatInterval, heartbeatCts.Token).ConfigureAwait(false);
                        await SendAsync(new WorkerEvent
                        {
                            Heartbeat = new Heartbeat { UnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }
                        }, heartbeatCts.Token).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) { _logger.LogDebug(ex, "Heartbeat loop ended"); }
            });

            // ── Log drain task — ships LogLines produced by ILogger calls inside
            // [TickerFunction] bodies up the same stream. Fire-and-forget for the
            // lifetime of this stream; cancelled in finally below. ──
            var logDrainTask = Task.Run(async () =>
            {
                try
                {
                    await foreach (var line in _logQueue.Reader.ReadAllAsync(heartbeatCts.Token).ConfigureAwait(false))
                    {
                        try
                        {
                            await SendAsync(new WorkerEvent { LogLine = line }, heartbeatCts.Token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) { return; }
                        catch (Exception ex) { _logger.LogDebug(ex, "Failed to forward LogLine for ticker {TickerId}", line.TickerId); }
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) { _logger.LogDebug(ex, "Log drain loop ended"); }
            });

            // ── Read loop ──
            try
            {
                while (await call.ResponseStream.MoveNext(stoppingToken).ConfigureAwait(false))
                {
                    var msg = call.ResponseStream.Current;
                    // Fire-and-forget per command so a slow handler (e.g. long function exec)
                    // doesn't block the read loop.
                    _ = HandleCommandAsync(msg, stoppingToken);
                }
            }
            finally
            {
                heartbeatCts.Cancel();
                try { await heartbeatTask.ConfigureAwait(false); } catch { }
                try { await logDrainTask.ConfigureAwait(false); } catch { }
            }
        }
        finally
        {
            // Mark stream torn down so future SendAsync calls block on the next gate.
            _writer = null;
            _readyGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            // Fail any pending operations so callers don't hang.
            FailAllPending(new InvalidOperationException("Worker stream closed before reply arrived"));
        }
    }

    private async Task SendHelloAsync(CancellationToken ct)
    {
        var nodeName = _options.NodeName;
        var sdkVersion = TickerQSdkConstants.SdkVersion;
        var nonce = Guid.NewGuid().ToString("N");
        var unixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var canonical = $"{nodeName}\n{sdkVersion}\n{nonce}\n{unixSeconds}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_options.WebhookSignature ?? string.Empty));
        var signature = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(canonical)));

        await SendUnguardedAsync(new WorkerEvent
        {
            Hello = new Hello
            {
                ApiKey = _options.ApiKey ?? string.Empty,
                NodeName = nodeName,
                SdkVersion = sdkVersion,
                MaxConcurrency = 0,
                Nonce = nonce,
                UnixSeconds = unixSeconds,
                HmacSignature = signature
            }
        }, ct).ConfigureAwait(false);
    }

    /// <summary>Send without waiting for the ready gate (used for the Hello handshake itself).</summary>
    private async Task SendUnguardedAsync(WorkerEvent evt, CancellationToken ct)
    {
        var writer = _writer
            ?? throw new InvalidOperationException("Worker stream not opened");
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

    private async Task HandleCommandAsync(SchedulerCommand cmd, CancellationToken ct)
    {
        try
        {
            switch (cmd.PayloadCase)
            {
                case SchedulerCommand.PayloadOneofCase.HelloAck:
                    return; // already consumed during handshake

                case SchedulerCommand.PayloadOneofCase.Heartbeat:
                    return; // no-op

                case SchedulerCommand.PayloadOneofCase.Disconnect:
                    _logger.LogInformation("Scheduler closed worker stream: {Reason}", cmd.Disconnect.Reason);
                    return;

                case SchedulerCommand.PayloadOneofCase.OperationResult:
                    if (_pendingOps.TryRemove(cmd.OperationResult.RequestId, out var opTcs))
                        opTcs.TrySetResult(cmd.OperationResult);
                    return;

                case SchedulerCommand.PayloadOneofCase.BytesResult:
                    if (_pendingBytes.TryRemove(cmd.BytesResult.RequestId, out var byTcs))
                        byTcs.TrySetResult(cmd.BytesResult);
                    return;

                case SchedulerCommand.PayloadOneofCase.ExecuteFunction:
                    await HandleExecuteFunctionAsync(cmd.ExecuteFunction, ct).ConfigureAwait(false);
                    return;

                case SchedulerCommand.PayloadOneofCase.TriggerResync:
                    await HandleTriggerResyncAsync(cmd.TriggerResync, ct).ConfigureAwait(false);
                    return;

                case SchedulerCommand.PayloadOneofCase.RemoveFunction:
                    HandleRemoveFunction(cmd.RemoveFunction);
                    await SendAsync(new WorkerEvent
                    {
                        Ack = new CommandAck { RequestId = cmd.RemoveFunction.RequestId, Success = true }
                    }, ct).ConfigureAwait(false);
                    return;

                case SchedulerCommand.PayloadOneofCase.CancelExecution:
                    HandleCancelExecution(cmd.CancelExecution);
                    await SendAsync(new WorkerEvent
                    {
                        Ack = new CommandAck { RequestId = cmd.CancelExecution.RequestId, Success = true }
                    }, ct).ConfigureAwait(false);
                    return;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Worker command handler failed: {Case}", cmd.PayloadCase);
        }
    }

    private Task HandleExecuteFunctionAsync(ExecuteFunction req, CancellationToken ct)
    {
        // Validate + parse synchronously so a malformed request can fail fast and
        // doesn't escape to a background task where the error would be silently
        // swallowed. Heavy work (the actual user function) runs on a Task.Run so
        // the worker stream's read loop unblocks immediately — otherwise long
        // functions (e.g. our SampleSlowJob with Task.Delay(120s)) would hold the
        // read loop and we couldn't receive a CancelExecution to interrupt them.
        if (string.IsNullOrWhiteSpace(req.FunctionName))
            return SendImmediateFailureAsync(req.RequestId, "FunctionName is required", ct);
        if (!Guid.TryParse(req.TickerId, out var tickerId))
            return SendImmediateFailureAsync(req.RequestId, "TickerId must be a GUID", ct);

        // Background-execute. We deliberately do NOT await this — the read loop
        // returns to handle further commands (CancelExecution, heartbeat, etc.)
        // while the function body runs. The ExecutionResult is sent from inside
        // the background task once the work actually completes.
        _ = Task.Run(() => RunExecuteFunctionInBackgroundAsync(req, tickerId, ct));
        return Task.CompletedTask;
    }

    private async Task SendImmediateFailureAsync(string requestId, string error, CancellationToken ct)
    {
        try
        {
            await SendAsync(new WorkerEvent
            {
                ExecutionResult = new ExecutionResult { RequestId = requestId, Success = false, Error = error }
            }, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send ExecutionResult for {RequestId}", requestId);
        }
    }

    private async Task RunExecuteFunctionInBackgroundAsync(ExecuteFunction req, Guid tickerId, CancellationToken ct)
    {
        ExecutionResult result;
        // Per-execution CTS — looked up by tickerId in _runningCts so a
        // CancelExecution arriving on the worker stream can signal this CTS,
        // which in turn cancels the user's Task.Delay(_, ct) and exits the body.
        using var executionCts = new CancellationTokenSource();
        _runningCts[tickerId] = executionCts;
        try
        {
            // The scheduler stores function names qualified ("SampleTimeJob@test-sdk-node")
            // so multiple SDKs can host the same bare name without collision. The SDK only
            // ever runs functions registered from its own source-gen ([TickerFunction("X")]),
            // which uses the BARE name. Strip the @node suffix before lookup.
            var bareFunctionName = req.FunctionName;
            var atIdx = bareFunctionName.IndexOf('@');
            if (atIdx > 0) bareFunctionName = bareFunctionName.Substring(0, atIdx);

            await using var scope = _serviceProvider.CreateAsyncScope();
            var taskHandler = scope.ServiceProvider.GetRequiredService<ITickerExecutionTaskHandler>();

            _logger.LogInformation(
                "[DBG] ExecuteFunction running: ticker={TickerId} retries={Retries} intervals=[{Intervals}]",
                tickerId, req.Retries, string.Join(",", req.RetryIntervalsSeconds));

            var function = new InternalFunctionContext
            {
                FunctionName = bareFunctionName,
                TickerId = tickerId,
                ParentId = null,
                Type = (TickerType)req.Type,
                Retries = req.Retries,
                RetryCount = req.RetryCount,
                RetryIntervals = req.RetryIntervalsSeconds.Count > 0
                    ? req.RetryIntervalsSeconds.ToArray()
                    : null,
                Status = TickerStatus.Idle,
                ExecutionTime = req.ScheduledFor?.ToDateTime() ?? DateTime.UtcNow,
                RunCondition = RunCondition.OnSuccess
            };

            if (TickerFunctionProvider.TickerFunctions.TryGetValue(function.FunctionName, out var item))
            {
                function.CachedDelegate = item.Delegate;
                function.CachedPriority = item.Priority;
                function.CachedMaxConcurrency = item.MaxConcurrency;
            }

            // Concurrency gate is honored manually here since we no longer pass
            // through the local TickerQTaskScheduler (its LongRunning path is
            // fire-and-forget which would mask the real completion time, the
            // exact bug we hit when the dashboard saw Done immediately).
            var concurrencyGate = scope.ServiceProvider.GetService<ITickerFunctionConcurrencyGate>();
            var semaphore = concurrencyGate?.GetSemaphoreOrNull(function.FunctionName, function.CachedMaxConcurrency);
            if (semaphore != null) await semaphore.WaitAsync(executionCts.Token).ConfigureAwait(false);
            try
            {
                using var _ = TickerExecutionScope.Push(tickerId, (TickerType)req.Type, bareFunctionName);
                await taskHandler.ExecuteTaskAsync(function, req.IsDue, executionCts.Token).ConfigureAwait(false);
            }
            finally { semaphore?.Release(); }

            // cancelled=true tells the scheduler to land the row as Cancelled
            // instead of Failed (Success=false alone maps to Failed).
            // ExecuteTaskAsync swallows user exceptions internally and writes the
            // outcome onto function.Status — inspect it instead of relying on a
            // thrown exception.
            if (executionCts.IsCancellationRequested)
            {
                result = new ExecutionResult { RequestId = req.RequestId, Success = false, Cancelled = true, Error = "Cancelled by dashboard" };
            }
            else if (function.Status == TickerStatus.Failed)
            {
                result = new ExecutionResult { RequestId = req.RequestId, Success = false, Error = function.ExceptionDetails ?? "Function execution failed" };
            }
            else
            {
                result = new ExecutionResult { RequestId = req.RequestId, Success = true };
            }
        }
        catch (OperationCanceledException) when (executionCts.IsCancellationRequested)
        {
            result = new ExecutionResult { RequestId = req.RequestId, Success = false, Cancelled = true, Error = "Cancelled by dashboard" };
        }
        catch (Exception ex)
        {
            result = new ExecutionResult { RequestId = req.RequestId, Success = false, Error = ex.Message };
        }
        finally
        {
            _runningCts.TryRemove(tickerId, out _);
        }

        try
        {
            await SendAsync(new WorkerEvent { ExecutionResult = result }, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send ExecutionResult for {RequestId}", req.RequestId);
        }
    }

    private async Task HandleTriggerResyncAsync(TriggerResync req, CancellationToken ct)
    {
        var ack = new CommandAck { RequestId = req.RequestId };
        try
        {
            await _syncService.SyncAsync(ct).ConfigureAwait(false);
            ack.Success = true;
        }
        catch (Exception ex)
        {
            ack.Success = false;
            ack.Error = ex.Message;
            _logger.LogWarning(ex, "TriggerResync failed for {RequestId}", req.RequestId);
        }
        await SendAsync(new WorkerEvent { Ack = ack }, ct).ConfigureAwait(false);
    }

    private static void HandleRemoveFunction(RemoveFunction req)
    {
        if (string.IsNullOrWhiteSpace(req.FunctionName)) return;
        var dict = TickerFunctionProvider.TickerFunctions.ToDictionary();
        if (dict.Remove(req.FunctionName))
            TickerFunctionProvider.RegisterFunctions(dict);
    }

    /// <summary>
    /// Scheduler asked us to cancel a running ticker. Look up the per-execution
    /// CTS we registered in <see cref="HandleExecuteFunctionAsync"/> and signal
    /// it; the [TickerFunction] body awaiting the token (e.g. Task.Delay(_, ct))
    /// will throw OperationCanceledException and exit. SDKs that don't have
    /// this ticker (it landed on a different replica) silently ignore.
    /// </summary>
    private void HandleCancelExecution(CancelExecution req)
    {
        if (!Guid.TryParse(req.TickerId, out var tickerId)) return;
        if (_runningCts.TryGetValue(tickerId, out var cts))
        {
            try
            {
                cts.Cancel();
                _logger.LogInformation("[CancelExecution] Signalled cancellation for ticker {TickerId}", tickerId);
            }
            catch (ObjectDisposedException)
            {
                // Race with execution finishing — already cleaned up. Safe to ignore.
            }
        }
    }

    private void FailAllPending(Exception ex)
    {
        foreach (var kv in _pendingOps)
            kv.Value.TrySetException(ex);
        _pendingOps.Clear();
        foreach (var kv in _pendingBytes)
            kv.Value.TrySetException(ex);
        _pendingBytes.Clear();
    }

    private static GrpcChannel BuildChannel(string url)
    {
        var handler = new SocketsHttpHandler
        {
            EnableMultipleHttp2Connections = true,
            KeepAlivePingDelay = TimeSpan.FromSeconds(30),
            KeepAlivePingTimeout = TimeSpan.FromSeconds(30),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5)
        };
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.IsLoopback)
            handler.SslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;
        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

        return GrpcChannel.ForAddress(url, new GrpcChannelOptions
        {
            HttpHandler = handler,
            MaxReceiveMessageSize = 16 * 1024 * 1024
        });
    }

    public override void Dispose()
    {
        _channel?.Dispose();
        _writeLock.Dispose();
        base.Dispose();
    }
}
