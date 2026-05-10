using System;
using System.Diagnostics;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Models;

namespace TickerQ.Utilities.Instrumentation
{
    /// <summary>
    /// Simple placeholder interface for TickerQ instrumentation
    /// </summary>
    internal interface ITickerQInstrumentation
    {
        Activity? StartJobActivity(string activityName, InternalFunctionContext context);
        void LogJobEnqueued(string jobType, string functionName, Guid jobId, string? enqueuedFrom = null);
        void LogJobCompleted(Guid jobId, string functionName, long executionTimeMs, bool success);
        void LogJobFailed(Guid jobId, string functionName, Exception exception, int retryCount);
        /// <summary>Per-attempt failure during retry loop. <paramref name="attempt"/> is 0-indexed (0 = initial). Logged as Error.</summary>
        void LogJobAttemptFailed(Guid jobId, string functionName, int attempt, int maxRetries, long elapsedMs, Exception exception);
        /// <summary>Announces an upcoming retry. <paramref name="nextAttempt"/> is 1-indexed retry number. Logged as Warning.</summary>
        void LogJobRetryScheduled(Guid jobId, string functionName, int nextAttempt, int maxRetries, int intervalSeconds);
        void LogJobCancelled(Guid jobId, string functionName, string reason);
        void LogJobSkipped(Guid jobId, string functionName, string reason);
        void LogSeedingDataStarted(string seedingDataType);
        void LogSeedingDataCompleted(string seedingDataType);
        void LogRequestDeserializationFailure(string requestType, string functionName, Guid tickerId, TickerType type, Exception exception);
    }
}