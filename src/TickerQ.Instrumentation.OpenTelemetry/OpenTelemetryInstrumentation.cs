using System;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using TickerQ.Utilities;
using TickerQ.Utilities.Instrumentation;
using TickerQ.Utilities.Models;

namespace TickerQ.Instrumentation.OpenTelemetry
{
    internal class OpenTelemetryInstrumentation : BaseLoggerInstrumentation , ITickerQInstrumentation
    {
        private readonly ILogger<OpenTelemetryInstrumentation> _logger;
        private static readonly ActivitySource ActivitySource = new("TickerQ", "1.0.0");

        public OpenTelemetryInstrumentation(ILogger<OpenTelemetryInstrumentation> logger, SchedulerOptionsBuilder optionsBuilder) : base(logger, optionsBuilder.NodeIdentifier)
        {
            _logger = logger;
        }

        public override Activity? StartJobActivity(string activityName, InternalFunctionContext context)
        {
            var activity = ActivitySource.StartActivity(activityName);
            
            if (activity != null)
            {
                activity.SetTag("tickerq.job.id", context.TickerId.ToString());
                activity.SetTag("tickerq.job.type", context.Type.ToString());
                activity.SetTag("tickerq.job.function", context.FunctionName);
                activity.SetTag("tickerq.job.priority", context.CachedPriority.ToString());
                activity.SetTag("tickerq.job.machine", Environment.MachineName);
                
                if (context.ParentId.HasValue)
                {
                    activity.SetTag("tickerq.job.parent_id", context.ParentId.Value.ToString());
                }
            }
            
            return activity;
        }

        public override void LogJobEnqueued(string jobType, string functionName, Guid jobId, string? enqueuedFrom = null)
        {
            using var activity = ActivitySource.StartActivity("tickerq.job.enqueued");
            activity?.SetTag("tickerq.job.id", jobId.ToString());
            activity?.SetTag("tickerq.job.type", jobType);
            activity?.SetTag("tickerq.job.function", functionName);
            
            // Get detailed caller information for OpenTelemetry
            var callerInfo = string.IsNullOrEmpty(enqueuedFrom) ? CallerInfoHelper.GetCallerInfo(6) : enqueuedFrom;
            activity?.SetTag("tickerq.job.enqueued_from", callerInfo);
            
            _logger.LogInformation("TickerQ Job enqueued: {JobType} - {Function} ({JobId}) from {EnqueuedFrom}", 
                jobType, functionName, jobId, callerInfo);
        }

        public override void LogJobCompleted(Guid jobId, string functionName, long executionTimeMs, bool success)
        {
            using var activity = ActivitySource.StartActivity("tickerq.job.completed");
            activity?.SetTag("tickerq.job.id", jobId.ToString());
            activity?.SetTag("tickerq.job.function", functionName);
            activity?.SetTag("tickerq.job.execution_time_ms", executionTimeMs);
            activity?.SetTag("tickerq.job.success", success);
            
            // Set activity status based on success
            if (activity != null)
            {
                activity.SetStatus(success ? ActivityStatusCode.Ok : ActivityStatusCode.Error);
            }
            
            base.LogJobCompleted(jobId, functionName, executionTimeMs, success);
        }

        public override void LogJobFailed(Guid jobId, string functionName, Exception exception, int retryCount)
        {
            using var activity = ActivitySource.StartActivity("tickerq.job.failed");
            activity?.SetTag("tickerq.job.id", jobId.ToString());
            activity?.SetTag("tickerq.job.function", functionName);
            activity?.SetTag("tickerq.job.retry_count", retryCount);
            activity?.SetTag("tickerq.job.error_type", exception.GetType().Name);
            activity?.SetTag("tickerq.job.error_message", exception.Message);
            
            if (activity != null)
            {
                activity.SetStatus(ActivityStatusCode.Error, exception.Message);
                // Record exception information in tags instead of RecordException (not available in all .NET versions)
                if (exception.StackTrace != null)
                {
                    activity.SetTag("tickerq.job.error_stack_trace", exception.StackTrace);
                }
            }
            
            base.LogJobFailed(jobId, functionName, exception, retryCount);
        }

        public override void LogJobCancelled(Guid jobId, string functionName, string reason)
        {
            using var activity = ActivitySource.StartActivity("tickerq.job.cancelled");
            activity?.SetTag("tickerq.job.id", jobId.ToString());
            activity?.SetTag("tickerq.job.function", functionName);
            activity?.SetTag("tickerq.job.cancellation_reason", reason);
            
            if (activity != null)
            {
                activity.SetStatus(ActivityStatusCode.Error, reason);
            }
            
            base.LogJobCancelled(jobId, functionName, reason);
        }

        public override void LogJobSkipped(Guid jobId, string functionName, string reason)
        {
            using var activity = ActivitySource.StartActivity("tickerq.job.skipped");
            activity?.SetTag("tickerq.job.id", jobId.ToString());
            activity?.SetTag("tickerq.job.function", functionName);
            activity?.SetTag("tickerq.job.skip_reason", reason);
            
            base.LogJobSkipped(jobId, functionName, reason);
        }

        public override void LogSeedingDataStarted(string seedingDataType)
        {
            using var activity = ActivitySource.StartActivity("tickerq.seeding.started");
            activity?.SetTag("tickerq.seeding.type", seedingDataType);
            activity?.SetTag("tickerq.seeding.environment", _instanceIdentifier);
            
            base.LogSeedingDataStarted(seedingDataType);
        }

        public override void LogSeedingDataCompleted(string seedingDataType)
        {
            using var activity = ActivitySource.StartActivity("tickerq.seeding.completed");
            activity?.SetTag("tickerq.seeding.type", seedingDataType);
            activity?.SetTag("tickerq.seeding.environment", _instanceIdentifier);
            
            base.LogSeedingDataCompleted(seedingDataType);
        }
    }
}
