using System;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using TickerQ.Utilities.Models;

namespace TickerQ.Utilities.Instrumentation;

public abstract class BaseLoggerInstrumentation
{
    private readonly ILogger _logger;

    protected BaseLoggerInstrumentation(ILogger logger)
    {
        _logger = logger;
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

    public virtual void LogJobCancelled(Guid jobId, string functionName, string reason)
    {
        _logger.LogWarning("TickerQ Job cancelled: {Function} ({JobId}) - {Reason}",
            functionName, jobId, reason);
    }

    public virtual void LogJobSkipped(Guid jobId, string functionName, string reason)
    {
        _logger.LogInformation("TickerQ Job skipped: {Function} ({JobId}) - {Reason}", functionName, jobId, reason);
    }
    
    public virtual void LogSeedingDataStarted(string seedingDataType, string environmentName)
    {
        _logger.LogInformation("TickerQ start seeding data: {TickerType} ({EnvironmentName})", seedingDataType, environmentName);
    }

    public virtual void LogSeedingDataCompleted(string seedingDataType, string environmentName)
    {
        _logger.LogInformation("TickerQ completed seeding data: {TickerType} ({EnvironmentName})", seedingDataType, environmentName);
    }
}