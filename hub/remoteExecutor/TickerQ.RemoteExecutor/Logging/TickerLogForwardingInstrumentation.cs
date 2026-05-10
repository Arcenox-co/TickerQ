using System.Diagnostics;
using TickerQ.RemoteExecutor.TunnelClient;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Instrumentation;
using TickerQ.Utilities.Models;

namespace TickerQ.RemoteExecutor.Logging;

/// <summary>
/// Decorates the registered <see cref="ITickerQInstrumentation"/> on the scheduler so
/// each lifecycle hook also pushes a scheduler-source log line through the tunnel —
/// the dashboard panel sees "[scheduler] Picked up …", "[scheduler] Cancelled: …",
/// "[scheduler] Skipped: …" alongside the SDK ILogger output.
///
/// We do NOT forward <c>LogJobCompleted</c> / <c>LogJobFailed</c> from this decorator
/// because the scheduler's local task handler fires them with a misleading 1ms elapsed
/// (the dispatch ack returns before the real SDK execution finishes). Those terminal
/// states are emitted by <see cref="WorkerStream.SchedulerWorkerServiceImpl{TT,TC}"/>
/// when the SDK reports back, with the truthful elapsed time.
/// </summary>
internal sealed class TickerLogForwardingInstrumentation : ITickerQInstrumentation
{
    private readonly ITickerQInstrumentation _inner;
    private readonly TunnelTickerQNotificationHubSender? _sender;

    public TickerLogForwardingInstrumentation(
        ITickerQInstrumentation inner,
        TunnelTickerQNotificationHubSender? sender)
    {
        _inner = inner;
        _sender = sender;
    }

    public Activity StartJobActivity(string activityName, InternalFunctionContext context)
        => _inner.StartJobActivity(activityName, context);

    public void LogJobEnqueued(string jobType, string functionName, System.Guid jobId, string? enqueuedFrom = null)
    {
        _inner.LogJobEnqueued(jobType, functionName, jobId, enqueuedFrom);
        var label = string.IsNullOrEmpty(enqueuedFrom) ? "scheduler" : enqueuedFrom;
        Push(jobId, jobType, functionName, 2, $"Picked up {functionName} from {label}");
        // No synthetic "Idle → Queued" line — top-level tickers are persisted in Queued
        // state from the moment they're added, so claiming a transition from Idle here
        // would be fabricating a state change that never happened.
    }

    public void LogJobCompleted(Guid jobId, string functionName, long executionTimeMs, bool success)
    {
        // Don't forward — see class header. Terminal events are emitted from
        // SchedulerWorkerServiceImpl when the SDK reports the real elapsed time.
        _inner.LogJobCompleted(jobId, functionName, executionTimeMs, success);
    }

    public void LogJobFailed(Guid jobId, string functionName, Exception exception, int retryCount)
    {
        // Don't forward — terminal Failed line emitted from SchedulerWorkerServiceImpl
        // with the SDK-reported exception details.
        _inner.LogJobFailed(jobId, functionName, exception, retryCount);
    }

    public void LogJobAttemptFailed(Guid jobId, string functionName, int attempt, int maxRetries, long elapsedMs, Exception exception)
    {
        // Pass through. SDK-side ILogger capture forwards this to the dashboard panel.
        _inner.LogJobAttemptFailed(jobId, functionName, attempt, maxRetries, elapsedMs, exception);
    }

    public void LogJobRetryScheduled(Guid jobId, string functionName, int nextAttempt, int maxRetries, int intervalSeconds)
    {
        _inner.LogJobRetryScheduled(jobId, functionName, nextAttempt, maxRetries, intervalSeconds);
    }

    public void LogJobCancelled(Guid jobId, string functionName, string reason)
    {
        _inner.LogJobCancelled(jobId, functionName, reason);
        Push(jobId, null, functionName, 3 /* Warn */, $"Cancelled: {reason}");
    }

    public void LogJobSkipped(Guid jobId, string functionName, string reason)
    {
        _inner.LogJobSkipped(jobId, functionName, reason);
        Push(jobId, null, functionName, 2, $"Skipped: {reason}");
    }

    public void LogSeedingDataStarted(string seedingDataType)
        => _inner.LogSeedingDataStarted(seedingDataType);

    public void LogSeedingDataCompleted(string seedingDataType)
        => _inner.LogSeedingDataCompleted(seedingDataType);

    public void LogRequestDeserializationFailure(string requestType, string functionName, Guid tickerId, TickerType type, Exception exception)
    {
        _inner.LogRequestDeserializationFailure(requestType, functionName, tickerId, type, exception);
        Push(tickerId, type.ToString().ToLowerInvariant(), functionName, 4 /* Error */,
            $"Request deserialization failed: {exception.Message}");
    }

    private void Push(Guid jobId, string? jobType, string functionName, int level, string message)
    {
        if (_sender == null || jobId == Guid.Empty) return;

        // Map the existing string job-type label to the proto's int TickerType.
        // Defaulting to TimeTicker when unknown — type is informational on the wire.
        var tickerType = jobType switch
        {
            "timeticker" => (int)TickerType.TimeTicker,
            "cronoccurrence" => (int)TickerType.CronTickerOccurrence,
            _ => (int)TickerType.TimeTicker
        };

        _ = _sender.PushTickerLogLineAsync(new Logging.TickerLogLine
        {
            TickerId = jobId,
            TickerType = tickerType,
            UnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Level = level,
            Source = "scheduler",
            Message = message,
            Category = string.Empty,
            FunctionName = functionName ?? string.Empty
        });
    }
}
