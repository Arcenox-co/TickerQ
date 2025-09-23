using System;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using TickerQ.Utilities.Models;

namespace TickerQ.Utilities.Instrumentation
{
    /// <summary>
    /// No-operation implementation of ITickerQInstrumentation
    /// </summary>
    public sealed class LoggerInstrumentation : BaseLoggerInstrumentation, ITickerQInstrumentation
    {
        public LoggerInstrumentation(ILogger<LoggerInstrumentation> logger, SchedulerOptionsBuilder optionsBuilder) : base(logger,  optionsBuilder.NodeIdentifier)
        {
        }

        public override Activity StartJobActivity(string activityName, InternalFunctionContext context)
            => null;
    }
}