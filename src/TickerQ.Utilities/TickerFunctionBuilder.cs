using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Promises;

namespace TickerQ.Utilities
{
    /// <summary>
    /// Fluent builder for configuring a ticker function registered via MapTicker.
    /// Each setter immediately applies the configuration to TickerFunctionProvider.
    /// </summary>
    public class TickerFunctionBuilder
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

    /// <summary>Builder for a ticker function with a typed request, which can also declare promises over the application's data.</summary>
    public sealed class TickerFunctionBuilder<TRequest> : TickerFunctionBuilder
    {
        private readonly IServiceCollection _services;
        private readonly Type _functionType;

        internal TickerFunctionBuilder(string functionName, IServiceCollection services, Type functionType = null) : base(functionName)
        {
            _services = services;
            _functionType = functionType;
        }

        private static TickerPromiseDefinition Declared(TickerPromiseDefinition definition)
        {
            definition.DeclaredOn = TickerPromiseDeclaration.MapTicker;
            return definition;
        }

        /// <inheritdoc cref="TickerFunctionBuilder.WithMaxConcurrency"/>
        public new TickerFunctionBuilder<TRequest> WithMaxConcurrency(int maxConcurrency)
        {
            base.WithMaxConcurrency(maxConcurrency);
            return this;
        }

        /// <inheritdoc cref="TickerFunctionBuilder.WithPriority"/>
        public new TickerFunctionBuilder<TRequest> WithPriority(TickerTaskPriority priority)
        {
            base.WithPriority(priority);
            return this;
        }

        /// <summary>
        /// Promises that every row of <paramref name="rows"/> gets a successful job of this function in time.
        /// TickerQ checks it periodically and lists the rows whose work was missed, for a person to fix or dismiss.
        /// </summary>
        /// <param name="rows">The rows to watch, read from <typeparamref name="TContext"/> resolved from a new service scope per check.</param>
        /// <param name="configure">The time limit and job are required: <c>p =&gt; p.Within(...).Job(...)</c>.</param>
        public TickerFunctionBuilder<TRequest> Promise<TContext, TRow>(Func<TContext, IQueryable<TRow>> rows,
            Action<TickerPromiseBuilder<TRow, TRequest>> configure)
        {
            _services.AddSingleton(Declared(TickerPromiseDefinition.Create(FunctionName, _functionType, rows, configure)));
            return this;
        }

        /// <inheritdoc cref="Promise{TContext,TRow}"/>
        /// <param name="rows">The rows to watch, read from a new service scope per check.</param>
        /// <param name="configure">The time limit and job are required: <c>p =&gt; p.Within(...).Job(...)</c>.</param>
        public TickerFunctionBuilder<TRequest> Promise<TRow>(Func<IServiceProvider, IQueryable<TRow>> rows,
            Action<TickerPromiseBuilder<TRow, TRequest>> configure)
        {
            _services.AddSingleton(Declared(TickerPromiseDefinition.Create(FunctionName, _functionType, rows, configure)));
            return this;
        }
    }
}
