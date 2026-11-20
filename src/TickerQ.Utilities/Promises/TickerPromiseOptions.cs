using System;
using System.Collections.Generic;
using System.Linq;
using TickerQ.Utilities.Interfaces;

namespace TickerQ.Utilities.Promises
{
    /// <summary>
    /// How promises are checked, and promises for functions that have no typed registration builder, such as
    /// <c>[TickerFunction]</c> methods. Class-based functions declare theirs with
    /// <c>MapTicker&lt;TFunction, TRequest&gt;().Promise(...)</c>.
    /// </summary>
    public sealed class TickerPromiseOptions
    {
        internal List<TickerPromiseDefinition> Definitions { get; } = new();

        /// <summary>How often each promise is checked. Defaults to one minute; at least five seconds.</summary>
        public TimeSpan CheckInterval { get; set; } = TimeSpan.FromMinutes(1);

        /// <summary>
        /// A job still waiting this long after it was due counts as never ran, for example when no node runs its
        /// function. Defaults to ten minutes.
        /// </summary>
        public TimeSpan NeverRanAfter { get; set; } = TimeSpan.FromMinutes(10);

        /// <summary>
        /// How long after a row's job succeeds the row may still be updated before it is reported as "job ran, row not
        /// updated": many applications mark the row a moment after the job, for example from a message handler.
        /// Defaults to one minute; zero reports at once.
        /// </summary>
        public TimeSpan RowUpdateGrace { get; set; } = TimeSpan.FromMinutes(1);

        /// <summary>The most rows one check reads per promise, newest first. Defaults to 1,000.</summary>
        public int MaxRowsPerCheck { get; set; } = 1000;

        /// <summary>
        /// The most jobs one check reads per function, newest first. When a function has more in the window, only rows
        /// covered by the jobs read are checked, so none is reported as never scheduled for want of its job.
        /// Defaults to 50,000.
        /// </summary>
        public int MaxJobsPerCheck { get; set; } = 50_000;

        /// <summary>
        /// How often a node re-reads a function's whole job window instead of only what changed since its last check,
        /// which catches jobs deleted or changed outside the normal paths. Defaults to one hour; at least one minute.
        /// </summary>
        public TimeSpan FullReloadInterval { get; set; } = TimeSpan.FromHours(1);

        /// <summary>
        /// How far back rows are checked when job retention is off. With retention on, the shortest retention window
        /// is used instead, so a row is never reported because its job was deleted. Defaults to 30 days.
        /// </summary>
        public TimeSpan LookBack { get; set; } = TimeSpan.FromDays(30);

        /// <summary>Declares a promise for a function referenced through the source generator's <c>TickerFunctions</c> class.</summary>
        /// <param name="rows">The rows that each need a successful job, read from <typeparamref name="TContext"/> resolved per check.</param>
        public TickerPromiseOptions For<TRequest, TContext, TRow>(TickerFunctionRef<TRequest> function,
            Func<TContext, IQueryable<TRow>> rows, Action<TickerPromiseBuilder<TRow, TRequest>> configure)
        {
            Definitions.Add(TickerPromiseDefinition.Create(RequireName(function), null, rows, configure));
            return this;
        }

        /// <summary>Declares a promise for a function referenced through the source generator's <c>TickerFunctions</c> class.</summary>
        /// <param name="rows">The rows that each need a successful job, read from a service scope created per check.</param>
        public TickerPromiseOptions For<TRequest, TRow>(TickerFunctionRef<TRequest> function,
            Func<IServiceProvider, IQueryable<TRow>> rows, Action<TickerPromiseBuilder<TRow, TRequest>> configure)
        {
            Definitions.Add(TickerPromiseDefinition.Create(RequireName(function), null, rows, configure));
            return this;
        }

        /// <summary>Starts a promise for the function <typeparamref name="TFunction"/> registered with MapTicker.</summary>
        public TickerPromiseFunction<TRequest> For<TFunction, TRequest>() where TFunction : class, ITickerFunction<TRequest>
            => new(definition => Definitions.Add(definition), typeof(TFunction));

        /// <summary>
        /// Throws an <see cref="ArgumentOutOfRangeException"/> when a setting is out of range. Runs after
        /// <c>ConfigurePromises</c> and again when TickerQ is registered.
        /// </summary>
        public void Validate()
        {
            if (CheckInterval < TimeSpan.FromSeconds(5))
                throw new ArgumentOutOfRangeException(nameof(CheckInterval), CheckInterval,
                    "TickerPromiseOptions.CheckInterval must be at least 5 seconds.");
            if (NeverRanAfter <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(NeverRanAfter), NeverRanAfter,
                    "TickerPromiseOptions.NeverRanAfter must be a positive duration.");
            if (RowUpdateGrace < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(RowUpdateGrace), RowUpdateGrace,
                    "TickerPromiseOptions.RowUpdateGrace cannot be negative.");
            if (MaxRowsPerCheck is < 1 or > 100_000)
                throw new ArgumentOutOfRangeException(nameof(MaxRowsPerCheck), MaxRowsPerCheck,
                    "TickerPromiseOptions.MaxRowsPerCheck must be between 1 and 100,000.");
            if (MaxJobsPerCheck is < 100 or > 1_000_000)
                throw new ArgumentOutOfRangeException(nameof(MaxJobsPerCheck), MaxJobsPerCheck,
                    "TickerPromiseOptions.MaxJobsPerCheck must be between 100 and 1,000,000.");
            if (FullReloadInterval < TimeSpan.FromMinutes(1))
                throw new ArgumentOutOfRangeException(nameof(FullReloadInterval), FullReloadInterval,
                    "TickerPromiseOptions.FullReloadInterval must be at least 1 minute.");
            if (LookBack <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(LookBack), LookBack,
                    "TickerPromiseOptions.LookBack must be a positive duration.");
        }

        private static string RequireName<TRequest>(TickerFunctionRef<TRequest> function)
            => string.IsNullOrWhiteSpace(function.Name)
                ? throw new ArgumentException("The function reference has no name.", nameof(function))
                : function.Name;
    }

    /// <summary>A function that promises are being declared for, from <see cref="TickerPromiseOptions.For{TFunction,TRequest}"/>.</summary>
    public sealed class TickerPromiseFunction<TRequest>
    {
        private readonly Action<TickerPromiseDefinition> _add;
        private readonly Type _functionType;

        internal TickerPromiseFunction(Action<TickerPromiseDefinition> add, Type functionType)
        {
            _add = add;
            _functionType = functionType;
        }

        /// <inheritdoc cref="TickerFunctionBuilder{TRequest}.Promise{TContext,TRow}"/>
        public TickerPromiseFunction<TRequest> Promise<TContext, TRow>(Func<TContext, IQueryable<TRow>> rows,
            Action<TickerPromiseBuilder<TRow, TRequest>> configure)
        {
            _add(TickerPromiseDefinition.Create(null, _functionType, rows, configure));
            return this;
        }

        /// <inheritdoc cref="TickerFunctionBuilder{TRequest}.Promise{TRow}"/>
        public TickerPromiseFunction<TRequest> Promise<TRow>(Func<IServiceProvider, IQueryable<TRow>> rows,
            Action<TickerPromiseBuilder<TRow, TRequest>> configure)
        {
            _add(TickerPromiseDefinition.Create(null, _functionType, rows, configure));
            return this;
        }
    }
}
