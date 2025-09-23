using System;
using System.Diagnostics;
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
        void LogJobCancelled(Guid jobId, string functionName, string reason);
        void LogJobSkipped(Guid jobId, string functionName, string reason);
        void LogSeedingDataStarted(string seedingDataType);
        void LogSeedingDataCompleted(string seedingDataType);
    }
}