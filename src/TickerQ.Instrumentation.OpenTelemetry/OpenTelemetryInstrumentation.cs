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

        public override void LogJobEnqueued(string jobType, string functionName, Guid jobId, string enqueuedFrom = null)
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
    }
}
