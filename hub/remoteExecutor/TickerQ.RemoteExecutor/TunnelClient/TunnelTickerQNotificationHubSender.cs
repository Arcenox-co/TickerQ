using System;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using TickerQ.RemoteExecutor.Logging;
using TickerQ.RemoteExecutor.Tunnel;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ.RemoteExecutor.TunnelClient;

/// <summary>
/// Tunnel-bound implementation of <see cref="ITickerQNotificationHubSender"/>. Replaces
/// the in-process No-Op when the scheduler runs against a Hub: each notification call is
/// translated into a NotificationEvent frame and pushed to the Hub through the existing
/// reverse tunnel. The Hub fans the event out to dashboard browsers via SignalR.
///
/// When the tunnel isn't connected, events are dropped silently (Debug-logged). The Hub
/// dashboard re-fetches on resume so eventual consistency is preserved without us needing
/// a persistent buffer.
/// </summary>
internal sealed class TunnelTickerQNotificationHubSender : ITickerQNotificationHubSender
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly TunnelClientHostedService _tunnel;
    private readonly TickerLogRingBuffer _logBuffer;
    private readonly ILogger<TunnelTickerQNotificationHubSender>? _logger;
    private long _sequence;

    // Track which tickers have already emitted a terminal lifecycle log line.
    // The internal manager fires UpdateTimeTickerFromInternalFunctionContext on
    // multiple status transitions, sometimes more than once at completion — without
    // dedup the panel renders a "Completed in Xms" line per call. We only want one
    // per ticker run (across Done/DueDone/Failed/Cancelled).
    private readonly ConcurrentDictionary<Guid, byte> _lifecycleEmitted = new();

    // Last status seen per ticker — used to emit "Status: X → Y" transition lines.
    // Without this we'd repeat the same line every time UpdateTimeTickerFromInternalFunctionContext
    // fires with the same status (which it does for InProgress during execution).
    private readonly ConcurrentDictionary<Guid, TickerStatus> _lastSeenStatus = new();

    public TunnelTickerQNotificationHubSender(
        TunnelClientHostedService tunnel,
        TickerLogRingBuffer logBuffer,
        ILogger<TunnelTickerQNotificationHubSender>? logger = null)
    {
        _tunnel = tunnel;
        _logBuffer = logBuffer;
        _logger = logger;
    }

    /// <summary>
    /// Pushes a log line both to the in-memory buffer (for late-open hydration on the
    /// dashboard) and out to the Hub over the tunnel as a <c>time_ticker.log</c> event.
    /// Best-effort — if the tunnel is down the line still lands in the buffer so a
    /// dashboard fetch can pick it up later.
    /// </summary>
    public Task PushTickerLogLineAsync(TickerLogLine line)
    {
        if (line == null) return Task.CompletedTask;
        _logBuffer.Append(line);
        return Push("time_ticker.log", "time-tickers", new
        {
            id = line.TickerId,
            unixMs = line.UnixMs,
            level = line.Level,
            source = line.Source,
            message = line.Message,
            category = line.Category,
            functionName = line.FunctionName
        });
    }

    /// <summary>
    /// Push a scheduler-source log line for events that aren't tied to an
    /// <see cref="InternalFunctionContext"/> — e.g. add/cancel/dispatch hooks where
    /// we only have a ticker id and a small bit of context.
    /// </summary>
    private void EnqueueSchedulerLine(Guid tickerId, int tickerType, string functionName, int level, string message)
    {
        if (tickerId == Guid.Empty) return;
        _ = PushTickerLogLineAsync(new TickerLogLine
        {
            TickerId = tickerId,
            TickerType = tickerType,
            UnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Level = level,
            Source = "scheduler",
            Message = message,
            Category = string.Empty,
            FunctionName = functionName ?? string.Empty
        });
    }

    private void EnqueueLifecycleLine(InternalFunctionContext ctx, int level, string message)
    {
        // Fire-and-forget — we don't want a slow tunnel write to block the
        // notification path that already does its own best-effort fan-out.
        _ = PushTickerLogLineAsync(new TickerLogLine
        {
            TickerId = ctx.TickerId,
            TickerType = (int)ctx.Type,
            UnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Level = level,
            Source = "scheduler",
            Message = message,
            Category = string.Empty,
            FunctionName = ctx.FunctionName ?? string.Empty
        });
    }

    // ── Time tickers ──

    public Task AddTimeTickerNotifyAsync(Guid id)
    {
        EnqueueSchedulerLine(id, (int)TickerType.TimeTicker, string.Empty, 2, "Time ticker added to scheduler queue");
        return Push("scope.changed", "time-tickers", new { id });
    }

    public Task AddTimeTickersBatchNotifyAsync()
        => Push("scope.changed", "time-tickers", null);

    public Task UpdateTimeTickerNotifyAsync(object timeTicker)
        => Push("time_ticker.updated", "time-tickers", timeTicker);

    public Task RemoveTimeTickerNotifyAsync(Guid id)
        => Push("time_ticker.removed", "time-tickers", new { id });

    public Task UpdateTimeTickerFromInternalFunctionContext<TTimeTickerEntity>(InternalFunctionContext ctx)
        where TTimeTickerEntity : TimeTickerEntity<TTimeTickerEntity>, new()
    {
        var payload = new
        {
            id = ctx.TickerId,
            status = (int)ctx.Status,
            elapsedMs = ctx.ElapsedTime,
            executedAt = ctx.ExecutedAt == default ? (DateTime?)null : ctx.ExecutedAt,
            exceptionMessage = ctx.ExceptionDetails
        };
        return Push("time_ticker.updated", "time-tickers", payload);
    }

    public Task CanceledTickerNotifyAsync(Guid id)
    {
        EnqueueSchedulerLine(id, (int)TickerType.TimeTicker, string.Empty, 3 /* Warn */, "Cancelled by user");
        return Push("time_ticker.updated", "time-tickers", new { id, status = (int)Utilities.Enums.TickerStatus.Cancelled });
    }

    // ── Cron tickers + occurrences ──

    public Task AddCronTickerNotifyAsync(object cronTicker)
        => Push("scope.changed", "cron-tickers", null);

    public Task UpdateCronTickerNotifyAsync(object cronTicker)
        => Push("scope.changed", "cron-tickers", null);

    public Task RemoveCronTickerNotifyAsync(Guid id)
        => Push("scope.changed", "cron-tickers", new { id });

    public Task AddCronOccurrenceAsync(Guid groupId, object occurrence)
    {
        // Best-effort extract of the occurrence id from the payload object so the
        // log line is keyed to the actual occurrence (not the cron group id).
        var occurrenceId = TryGetOccurrenceId(occurrence);
        if (occurrenceId.HasValue)
            EnqueueSchedulerLine(occurrenceId.Value, (int)TickerType.CronTickerOccurrence, string.Empty, 2, "Cron occurrence enqueued");
        return Push("scope.changed", "cron-occurrences", new { cronTickerId = groupId });
    }

    private static Guid? TryGetOccurrenceId(object occurrence)
    {
        if (occurrence == null) return null;
        var prop = occurrence.GetType().GetProperty("Id");
        if (prop == null) return null;
        var v = prop.GetValue(occurrence);
        return v is Guid g ? g : null;
    }

    public Task UpdateCronOccurrenceAsync(Guid groupId, object occurrence)
        => Push("scope.changed", "cron-occurrences", new { cronTickerId = groupId });

    public Task UpdateCronOccurrenceFromInternalFunctionContext<TCronTickerEntity>(InternalFunctionContext ctx)
        where TCronTickerEntity : CronTickerEntity, new()
    {
        var payload = new
        {
            id = ctx.TickerId,
            cronTickerId = ctx.ParentId,
            status = (int)ctx.Status,
            elapsedMs = ctx.ElapsedTime
        };
        return Push("cron_occurrence.updated", "cron-occurrences", payload);
    }

    /// <summary>
    /// Emits "scheduler" log lines for status transitions seen on the ticker context.
    /// Called by <see cref="WorkerStream.SchedulerWorkerServiceImpl{TT,TC}"/> when the
    /// SDK reports a status update — that's the only path with a truthful elapsed
    /// time. The local handler's dispatch-ack also fires UpdateTickerAsync but with a
    /// misleading 1ms elapsed; routing the synth here keeps that path off the panel.
    ///
    /// Non-terminal transitions render as "Status: prev → next"; terminal transitions
    /// render as "Completed in Xms" / "Failed after Xms — …" / "Cancelled". Each
    /// terminal status emits at most once per ticker — see <see cref="_lifecycleEmitted"/>.
    /// </summary>
    public void EmitLifecycleLine(InternalFunctionContext ctx)
    {
        if (ctx.TickerId == Guid.Empty) return;

        var status = ctx.Status;
        var prev = _lastSeenStatus.TryGetValue(ctx.TickerId, out var p) ? (TickerStatus?)p : null;

        // Skip if status didn't change — multiple SDK reports can carry the same status.
        if (prev == status) return;
        _lastSeenStatus[ctx.TickerId] = status;

        var isTerminal = status is TickerStatus.Done or TickerStatus.DueDone
            or TickerStatus.Failed or TickerStatus.Cancelled;

        if (isTerminal)
        {
            // First-terminal-wins per ticker. Guards against retries that re-enter Done.
            if (!_lifecycleEmitted.TryAdd(ctx.TickerId, 0)) return;

            var (level, msg) = status switch
            {
                TickerStatus.Done or TickerStatus.DueDone =>
                    (2 /* Information */, $"Completed in {ctx.ElapsedTime}ms"),
                TickerStatus.Failed =>
                    (4 /* Error */, string.IsNullOrEmpty(ctx.ExceptionDetails)
                        ? $"Failed after {ctx.ElapsedTime}ms"
                        : $"Failed after {ctx.ElapsedTime}ms — {Truncate(ExtractExceptionMessage(ctx.ExceptionDetails), 500)}"),
                TickerStatus.Cancelled =>
                    (3 /* Warning */, "Cancelled"),
                _ => (0, string.Empty)
            };

            if (string.IsNullOrEmpty(msg)) return;
            EnqueueLifecycleLine(ctx, level, msg);
            return;
        }

        // Non-terminal transition — emit "Status: prev → next" so the panel shows
        // intermediate states (Queued, InProgress). Skip the very first observation
        // when prev is null to avoid noise like "Status: ? → Idle" on creation.
        if (prev is null) return;
        EnqueueLifecycleLine(ctx, 2 /* Information */, $"Status: {prev} → {status}");
    }

    private static string ExtractExceptionMessage(string serialized)
    {
        // ExceptionDetails is JSON ({ "Message": "...", "StackTrace": "..." }) — try to
        // surface just the Message; fall back to the raw string if parsing fails.
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(serialized);
            if (doc.RootElement.TryGetProperty("Message", out var m) && m.ValueKind == System.Text.Json.JsonValueKind.String)
                return m.GetString() ?? serialized;
        }
        catch { }
        return serialized;
    }

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) || s.Length <= max ? s : s.Substring(0, max) + "…";

    // ── Host / nodes ──

    public void UpdateActiveThreads(string activeThreads)
        => _ = Push("host.status", null, new { activeThreads });

    public void UpdateNextOccurrence(DateTime? nextOccurrence)
        => _ = Push("host.status", null, new { nextOccurrence });

    public void UpdateHostStatus(bool active)
        => _ = Push("host.status", null, new { isRunning = active });

    public void UpdateHostException(string exceptionMessage)
        => _ = Push("host.status", null, new { exceptionMessage });

    public Task UpdateNodeHeartBeatAsync(JsonElement nodeHeartBeat)
        => Push("node.heartbeat", null, nodeHeartBeat);

    // ── Internal pump ──

    private async Task Push(string eventType, string? scope, object? payload)
    {
        var writer = _tunnel.ActiveWriter;
        if (writer == null)
        {
            _logger?.LogInformation("[diag] Push DROPPED — no active writer ({EventType})", eventType);
            return;
        }
        _logger?.LogInformation("[diag] Push {EventType} scope={Scope}", eventType, scope ?? "(none)");

        try
        {
            var notification = new NotificationEvent
            {
                EventType = eventType,
                Scope = scope ?? string.Empty,
                Sequence = Interlocked.Increment(ref _sequence),
                EmittedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
            if (payload != null)
                notification.PayloadJson = ByteString.CopyFrom(JsonSerializer.SerializeToUtf8Bytes(payload, Json));

            await _tunnel.WriteLock.WaitAsync().ConfigureAwait(false);
            try
            {
                await writer.WriteAsync(new SchedulerEvent { Notification = notification }).ConfigureAwait(false);
            }
            finally
            {
                _tunnel.WriteLock.Release();
            }
        }
        catch (RpcException) { /* tunnel torn down — drop */ }
        catch (InvalidOperationException) { /* writer completed — drop */ }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Failed to push {EventType} notification through tunnel", eventType);
        }
    }
}
