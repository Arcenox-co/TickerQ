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

        /// <summary>No longer supported: declare cron schedules with <c>options.SeedCronTickers(...)</c>.</summary>
        [System.Obsolete(Base.TickerFunctionAttribute.CronMovedMessage, error: true)]
        public TickerFunctionBuilder WithCron(CronExpression cronExpression)
            => throw new System.NotSupportedException(Base.TickerFunctionAttribute.CronMovedMessage);

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
