using TickerQ.Utilities.Enums;

namespace TickerQ.Utilities
{
    /// <summary>
    /// Fluent builder for configuring a ticker function registered via MapTicker.
    /// Each setter immediately applies the configuration to TickerFunctionProvider.
    /// </summary>
    public sealed class TickerFunctionBuilder
    {
        internal string FunctionName { get; }

        internal TickerFunctionBuilder(string functionName)
        {
            FunctionName = functionName;
        }

        /// <summary>
        /// Sets the cron expression for this function. Validates immediately — throws if invalid.
        /// Accepts string (implicit conversion) or CronExpression.Parse().
        /// </summary>
        public TickerFunctionBuilder WithCron(CronExpression cronExpression)
        {
            TickerFunctionProvider.Configure(FunctionName, cronExpression: cronExpression.Value);
            return this;
        }

        /// <summary>
        /// Sets the maximum concurrent executions for this function.
        /// </summary>
        public TickerFunctionBuilder WithMaxConcurrency(int maxConcurrency)
        {
            TickerFunctionProvider.Configure(FunctionName, maxConcurrency: maxConcurrency);
            return this;
        }

        /// <summary>
        /// Sets the task priority for this function.
        /// </summary>
        public TickerFunctionBuilder WithPriority(TickerTaskPriority priority)
        {
            TickerFunctionProvider.Configure(FunctionName, priority: priority);
            return this;
        }
    }
}
