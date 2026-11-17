using System;
using System.Linq.Expressions;

namespace TickerQ.Utilities.Promises
{
    /// <summary>
    /// Describes a promise: every row of the query gets a successful job of the function within a time limit.
    /// <see cref="Within(TimeSpan, Expression{Func{TRow, DateTime?}})"/> and <see cref="Job"/> are required.
    /// </summary>
    public sealed class TickerPromiseBuilder<TRow, TRequest>
    {
        internal TickerPromiseBuilder() { }

        internal TimeSpan? WithinValue { get; private set; }
        internal TickerPromiseWindow<TRow> After { get; private set; }
        internal Func<TRow, TRequest> JobFactory { get; private set; }
        internal TickerPromiseKey<TRequest> Key { get; private set; }
        internal Expression<Func<TRow, bool>> Done { get; private set; }
        internal Func<TRow, string> LabelFactory { get; private set; }
        internal string Name { get; private set; }
        internal int? AutoFixMax { get; private set; }
        internal bool RepeatsAllowed { get; private set; }

        /// <summary>
        /// Each row's job must succeed within <paramref name="within"/> of the row's <paramref name="after"/> time.
        /// Rows with no time yet are not checked.
        /// </summary>
        public TickerPromiseBuilder<TRow, TRequest> Within(TimeSpan within, Expression<Func<TRow, DateTime?>> after)
            => SetWithin(within, TickerPromiseWindow<TRow>.Create(after));

        /// <inheritdoc cref="Within(TimeSpan, Expression{Func{TRow, DateTime?}})"/>
        public TickerPromiseBuilder<TRow, TRequest> Within(TimeSpan within, Expression<Func<TRow, DateTime>> after)
            => SetWithin(within, TickerPromiseWindow<TRow>.Create(after));

        /// <inheritdoc cref="Within(TimeSpan, Expression{Func{TRow, DateTime?}})"/>
        public TickerPromiseBuilder<TRow, TRequest> Within(TimeSpan within, Expression<Func<TRow, DateTimeOffset?>> after)
            => SetWithin(within, TickerPromiseWindow<TRow>.Create(after));

        /// <inheritdoc cref="Within(TimeSpan, Expression{Func{TRow, DateTime?}})"/>
        public TickerPromiseBuilder<TRow, TRequest> Within(TimeSpan within, Expression<Func<TRow, DateTimeOffset>> after)
            => SetWithin(within, TickerPromiseWindow<TRow>.Create(after));

        /// <summary>
        /// The request a row's job runs with, and the request values that tie a job to its row, for example
        /// <c>r =&gt; r.OrderId</c> or <c>r =&gt; new { r.TenantId, r.OrderId }</c>.
        /// </summary>
        public TickerPromiseBuilder<TRow, TRequest> Job(Func<TRow, TRequest> job, Expression<Func<TRequest, object>> matchBy)
        {
            JobFactory = job ?? throw new ArgumentNullException(nameof(job));
            Key = TickerPromiseKey<TRequest>.Analyze(matchBy ?? throw new ArgumentNullException(nameof(matchBy)));
            return this;
        }

        /// <summary>
        /// When the row itself shows the work done, for example a status. Rows matching it are done whatever their
        /// jobs did. Without it, a row is done once one of its jobs succeeds.
        /// </summary>
        public TickerPromiseBuilder<TRow, TRequest> DoneWhen(Expression<Func<TRow, bool>> done)
        {
            Done = done ?? throw new ArgumentNullException(nameof(done));
            return this;
        }

        /// <summary>How the dashboard names a row, for example <c>o =&gt; $"{o.Number} for {o.Customer}"</c>.</summary>
        public TickerPromiseBuilder<TRow, TRequest> Label(Func<TRow, string> label)
        {
            LabelFactory = label ?? throw new ArgumentNullException(nameof(label));
            return this;
        }

        /// <summary>The promise's name, unique among promises. Defaults to "Function · Row".</summary>
        public TickerPromiseBuilder<TRow, TRequest> Named(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 200)
                throw new ArgumentException("A promise name needs 1 to 200 characters.", nameof(name));
            Name = name.Trim();
            return this;
        }

        /// <summary>
        /// Schedules the job for rows that were never scheduled, without waiting for a person. When more than
        /// <paramref name="maxPerCheck"/> such rows turn up in one check, nothing is fixed and automatic fixing
        /// pauses until it is turned back on in the dashboard. Rows whose job never ran are always left to a person.
        /// </summary>
        public TickerPromiseBuilder<TRow, TRequest> AutoFix(int maxPerCheck = 50)
        {
            if (maxPerCheck is < 1 or > 1000)
                throw new ArgumentOutOfRangeException(nameof(maxPerCheck), maxPerCheck, "maxPerCheck must be between 1 and 1,000.");
            AutoFixMax = maxPerCheck;
            return this;
        }

        /// <summary>
        /// Lets a row have more than one successful job, for functions meant to run again, such as reminders. By default
        /// a row whose job succeeded twice, or that has an extra job waiting, is listed for review.
        /// </summary>
        public TickerPromiseBuilder<TRow, TRequest> AllowRepeats()
        {
            RepeatsAllowed = true;
            return this;
        }

        private TickerPromiseBuilder<TRow, TRequest> SetWithin(TimeSpan within, TickerPromiseWindow<TRow> after)
        {
            if (within <= TimeSpan.Zero || within > TickerPromiseDefinition.MaxWithin)
                throw new ArgumentOutOfRangeException(nameof(within), within, "within must be positive and at most 365 days.");
            WithinValue = within;
            After = after;
            return this;
        }
    }
}
