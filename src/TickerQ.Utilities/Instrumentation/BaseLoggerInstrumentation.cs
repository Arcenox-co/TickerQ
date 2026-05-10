using System;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Models;

namespace TickerQ.Utilities.Instrumentation;

public abstract class TickerQBaseLoggerInstrumentation
{
    private readonly ILogger _logger;
    protected readonly string InstanceIdentifier;
    protected TickerQBaseLoggerInstrumentation(ILogger logger, string instanceIdentifier)
    {
        _logger = logger;
        InstanceIdentifier = instanceIdentifier;
    }

    public abstract Activity StartJobActivity(string activityName, InternalFunctionContext context);

    public virtual void LogJobEnqueued(string jobType, string functionName, Guid jobId, string enqueuedFrom = null)
    {
        _logger.LogInformation("TickerQ Job enqueued: {JobType} - {Function} ({JobId}) from {EnqueuedFrom}",
            jobType, functionName, jobId, enqueuedFrom ?? "Unknown");
    }

    public virtual void LogJobStarted(Guid jobId, string functionName, string jobType)
    {
        _logger.LogInformation("TickerQ Job started: {JobType} - {Function} ({JobId})",
            jobType, functionName, jobId);
    }

    public virtual void LogJobCompleted(Guid jobId, string functionName, long executionTimeMs, bool success)
    {
        _logger.LogInformation("TickerQ Job completed: {Function} ({JobId}) in {ExecutionTime}ms - Success: {Success}",
            functionName, jobId, executionTimeMs, success);
    }

    public virtual void LogJobFailed(Guid jobId, string functionName, Exception exception, int retryCount)
    {
        _logger.LogError(exception, "TickerQ Job failed: {Function} ({JobId}) - Retry {RetryCount} - {Error}",
            functionName, jobId, retryCount, exception.Message);
    }

    public virtual void LogJobAttemptFailed(Guid jobId, string functionName, int attempt, int maxRetries, long elapsedMs, Exception exception)
    {
        // attempt is 0-indexed in the loop. attempt=0 means the initial run (not a retry);
        // attempt>=1 means it's the Nth retry attempt — matches "N/M" in the dashboard's
        // Retries column ("retries done / max").
        if (attempt == 0)
        {
            _logger.LogError(exception, "Attempt of {Function} failed in {ElapsedMs}ms — {Error}",
                functionName, elapsedMs, exception.Message);
        }
        else
        {
            _logger.LogError(exception, "Retry {Attempt} of {MaxRetries} for {Function} failed in {ElapsedMs}ms — {Error}",
                attempt, maxRetries, functionName, elapsedMs, exception.Message);
        }
    }

    public virtual void LogJobRetryScheduled(Guid jobId, string functionName, int nextAttempt, int maxRetries, int intervalSeconds)
    {
        _logger.LogWarning("Retrying in {IntervalSeconds}s (retry {NextAttempt} of {MaxRetries})…",
            intervalSeconds, nextAttempt, maxRetries);
    }

    public virtual void LogJobCancelled(Guid jobId, string functionName, string reason)
    {
        _logger.LogWarning("TickerQ Job cancelled: {Function} ({JobId}) - {Reason}",
            functionName, jobId, reason);
    }

    public virtual void LogJobSkipped(Guid jobId, string functionName, string reason)
    {
        _logger.LogInformation("TickerQ Job skipped: {Function} ({JobId}) - {Reason}", functionName, jobId, reason);
    }
    
    public virtual void LogSeedingDataStarted(string seedingDataType)
    {
        _logger.LogInformation("TickerQ start seeding data: {TickerType} ({EnvironmentName})", seedingDataType, InstanceIdentifier);
    }

    public virtual void LogSeedingDataCompleted(string seedingDataType)
    {
        _logger.LogInformation("TickerQ completed seeding data: {TickerType} ({EnvironmentName})", seedingDataType, InstanceIdentifier);
    }

    public virtual void LogRequestDeserializationFailure(string requestType, string functionName, Guid tickerId, TickerType type, Exception exception)
    {
        _logger.LogError("Failed to deserialize request to {RequestType} - {TickerId} - {TickerType}: {Exception}", requestType, tickerId, type, exception);
    }
}