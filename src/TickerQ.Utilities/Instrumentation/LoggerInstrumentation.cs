using System.Diagnostics;
using Microsoft.Extensions.Logging;
using TickerQ.Utilities.Models;

namespace TickerQ.Utilities.Instrumentation
{
    public class LoggerInstrumentation : BaseLoggerInstrumentation , ITickerQInstrumentation
    {
        public LoggerInstrumentation(ILogger<LoggerInstrumentation> logger) : base(logger)
        {
        }

        public override Activity StartJobActivity(string activityName, InternalFunctionContext context) 
            => null;
    }
}
