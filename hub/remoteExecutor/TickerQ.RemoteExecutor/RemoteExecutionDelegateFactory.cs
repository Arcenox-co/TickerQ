using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.DependencyInjection;
using TickerQ.RemoteExecutor.Logging;
using TickerQ.RemoteExecutor.TunnelClient;
using TickerQ.RemoteExecutor.WorkerStream;
using TickerQ.Utilities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Exceptions;
using TickerQ.Utilities.Interfaces;
using TickerQ.Worker.V1;

namespace TickerQ.RemoteExecutor;

/// <summary>
/// Builds a <see cref="TickerFunctionDelegate"/> that dispatches a fired ticker to the
/// SDK over the persistent worker stream. Replaces the old HTTP/gRPC-to-callback model:
/// the SDK no longer needs an inbound port.
///
/// At dispatch time:
///   1. Resolve <see cref="WorkerStreamRegistry"/> + <see cref="IRemotePayloadLoader"/> via DI.
///   2. Pick a live stream for the function's owning node (round-robin across replicas).
///   3. Load the ticker's stored Request bytes inline so the SDK doesn't need a follow-up
///      GetTimeTickerRequest round-trip.
///   4. Send <c>ExecuteFunction</c> on the picked stream and await the matching
///      <c>ExecutionResult</c> (correlated by request_id inside <c>SchedulerWorkerConnection</c>).
///   5. Surface non-success results as exceptions so the existing retry/failure pipeline
///      treats them like any other dispatch failure.
/// </summary>
internal static class RemoteExecutionDelegateFactory
{
    private static readonly TimeSpan DispatchTimeout = TimeSpan.FromMinutes(5);
    // Transport-retry policy. Distinct from the user's `Retries` budget — these
    // attempts cover infrastructure flakes (SDK reconnecting, stream drained
    // mid-dispatch, replica rolling) where the user's code never ran.
    //
    //   * MaxAttempts = 3 — first try + two retries.
    //   * MaxWindow   = 60s — give up if we can't get a worker inside a minute,
    //     so a long outage doesn't keep one ticker churning forever.
    //   * RetryDelay  = 20s — paces the 3 attempts evenly across the window.
    private const int TransportMaxAttempts = 3;
    private static readonly TimeSpan TransportMaxWindow = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan TransportRetryDelay = TimeSpan.FromSeconds(20);

    public static TickerFunctionDelegate Create(string nodeName)
    {
        if (string.IsNullOrWhiteSpace(nodeName))
            throw new ArgumentException("Node name is required.", nameof(nodeName));

        return async (ct, serviceProvider, context) =>
        {
            var registry = serviceProvider.GetRequiredService<WorkerStreamRegistry>();
            var loader = serviceProvider.GetRequiredService<IRemotePayloadLoader>();
            var sender = serviceProvider.GetService<ITickerQNotificationHubSender>() as TunnelTickerQNotificationHubSender;

            // Pick a live stream — if none, retry-loop until one shows up or the
            // 1-minute transport window expires. Each failed attempt is logged
            // via the dashboard log stream so the user sees the retry cadence
            // ("attempt 2/3, no worker yet…"). Cancellation always wins.
            var transportStart = DateTimeOffset.UtcNow;
            SchedulerWorkerConnection? conn = null;
            string? lastTransportError = null;
            for (var attempt = 1; attempt <= TransportMaxAttempts; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                conn = registry.PickForNode(Guid.Empty, nodeName);
                if (conn != null) break;

                lastTransportError = $"No live worker stream for node '{nodeName}'. SDK is not connected.";
                if (sender != null)
                {
                    _ = sender.PushTickerLogLineAsync(new TickerLogLine
                    {
                        TickerId = context.Id,
                        TickerType = (int)context.Type,
                        UnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                        Level = 3,
                        Source = "scheduler",
                        Message = $"SDK not connected — transport retry {attempt}/{TransportMaxAttempts}",
                        Category = string.Empty,
                        FunctionName = context.FunctionName ?? string.Empty
                    });
                }

                if (attempt == TransportMaxAttempts) break;
                // If the next backoff would push us past the window, stop now —
                // no point sleeping for retries we'll never run.
                var elapsed = DateTimeOffset.UtcNow - transportStart;
                if (elapsed + TransportRetryDelay > TransportMaxWindow) break;
                try { await Task.Delay(TransportRetryDelay, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { throw; }
            }

            if (conn == null)
            {
                // Transport window exhausted. For time tickers, mark the run
                // as `Skipped` with reason "SDK offline" — the SDK will pick
                // it up when it reconnects via DueDone semantics if still
                // applicable, and the user can distinguish "would have run
                // but couldn't" from "ran and broke". For cron occurrences,
                // the auto-pause monitor handles the parent cron, but the
                // already-materialized occurrence still falls through here;
                // Skipped with the same reason keeps the audit trail clean.
                var msg = lastTransportError
                    ?? $"SDK node '{nodeName}' is offline (no worker stream after {TransportMaxAttempts} attempts).";
                throw new SdkOfflineSkipException(msg);
            }

            // Push a scheduler-source dispatch log line so the dashboard panel sees the
            // full lifecycle. (Sender was hoisted above for transport-retry logging.)
            if (sender != null)
            {
                _ = sender.PushTickerLogLineAsync(new TickerLogLine
                {
                    TickerId = context.Id,
                    TickerType = (int)context.Type,
                    UnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    Level = 2,
                    Source = "scheduler",
                    Message = $"Dispatching {context.FunctionName} to {nodeName}",
                    Category = string.Empty,
                    FunctionName = context.FunctionName ?? string.Empty
                });
            }

            var payload = await loader.LoadPayloadAsync(context.Id, context.Type, ct).ConfigureAwait(false);

            Console.WriteLine($"[DBG-DLG] Dispatching {context.FunctionName} ({context.Id}): retries={context.Retries} intervals=[{(context.RetryIntervals == null ? "null" : string.Join(",", context.RetryIntervals))}]");

            var execute = new ExecuteFunction
            {
                TickerId = context.Id.ToString(),
                FunctionName = context.FunctionName ?? string.Empty,
                Type = (int)context.Type,
                RetryCount = context.RetryCount,
                IsDue = false,
                ScheduledFor = Timestamp.FromDateTime(DateTime.SpecifyKind(context.ScheduledFor, DateTimeKind.Utc)),
                RequestPayload = payload != null ? ByteString.CopyFrom(payload) : ByteString.Empty,
                Retries = context.Retries,
            };
            if (context.RetryIntervals is { Length: > 0 })
                execute.RetryIntervalsSeconds.AddRange(context.RetryIntervals);

            var dispatchStart = DateTimeOffset.UtcNow;
            ExecutionResult result;
            try
            {
                result = await conn.ExecuteFunctionAsync(execute, DispatchTimeout, ct).ConfigureAwait(false);
            }
            catch (Exception waitEx) when (ct.IsCancellationRequested)
            {
                // The dashboard's Cancel signalled the scheduler-local CTS, which
                // tripped the timeout-Register callback inside ExecuteFunctionAsync
                // (it surfaces as TimeoutException). The SDK has its own
                // CancelExecution arriving in parallel and will land Cancelled in
                // the DB on its own — we just need to make sure the scheduler's
                // task handler sees this as cancellation, not a generic failure.
                _ = waitEx;
                throw new TaskCanceledException("Cancelled by dashboard");
            }
            var dispatchMs = (long)(DateTimeOffset.UtcNow - dispatchStart).TotalMilliseconds;

            // Worker has accepted the dispatch (this is just the ack — the function is
            // still running on the SDK side). Surfaces the network round-trip latency
            // so a slow tunnel/SDK is visible in the panel. Also emit a synthetic
            // "Queued → InProgress" transition since TickerQ doesn't persist InProgress
            // for top-level tickers, only for children.
            if (sender != null)
            {
                var unixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                _ = sender.PushTickerLogLineAsync(new TickerLogLine
                {
                    TickerId = context.Id,
                    TickerType = (int)context.Type,
                    UnixMs = unixMs,
                    Level = 2,
                    Source = "scheduler",
                    Message = $"Worker acknowledged dispatch ({dispatchMs}ms)",
                    Category = string.Empty,
                    FunctionName = context.FunctionName ?? string.Empty
                });
                _ = sender.PushTickerLogLineAsync(new TickerLogLine
                {
                    TickerId = context.Id,
                    TickerType = (int)context.Type,
                    UnixMs = unixMs + 1,
                    Level = 2,
                    Source = "scheduler",
                    Message = "Status: Queued → InProgress",
                    Category = string.Empty,
                    FunctionName = context.FunctionName ?? string.Empty
                });
            }

            if (!result.Success)
            {
                // Cancellation must surface as TaskCanceledException so the
                // scheduler's local task handler maps it to TickerStatus.Cancelled
                // instead of Failed. Without this branch, every Success=false
                // result lands as Failed even when the user clicked Cancel.
                if (result.Cancelled)
                    throw new TaskCanceledException(
                        string.IsNullOrEmpty(result.Error) ? "Cancelled by dashboard" : result.Error);
                throw new InvalidOperationException(
                    string.IsNullOrEmpty(result.Error) ? "Worker reported execution failure" : result.Error);
            }
        };
    }
}
