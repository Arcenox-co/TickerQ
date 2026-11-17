using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace TickerQ.Utilities.Promises
{
    /// <summary>One promise as declared in code. The engine works through this untyped view.</summary>
    internal abstract class TickerPromiseDefinition
    {
        internal static readonly TimeSpan MaxWithin = TimeSpan.FromDays(365);

        private readonly string _function;

        protected TickerPromiseDefinition(string function, Type functionType, Type contextType)
        {
            _function = function;
            FunctionType = functionType;
            ContextType = contextType;
        }

        /// <summary>The class whose MapTicker name is the function, when the promise was declared by type.</summary>
        public Type FunctionType { get; }

        /// <summary>The service the rows are read from, or null when they are read from the service provider.</summary>
        public Type ContextType { get; }

        public bool IsFunctionMapped => _function != null || TickerFunctionProvider.TryGetFunctionName(FunctionType, out _);

        public string Function => _function
            ?? (TickerFunctionProvider.TryGetFunctionName(FunctionType, out var name) ? name : FunctionType.Name);

        public string Name => NamedAs ?? $"{Function} · {RowType.Name}";

        public abstract Type RowType { get; }
        public abstract Type RequestType { get; }
        public abstract TimeSpan? Within { get; }
        public abstract bool HasJob { get; }
        public abstract bool HasDoneWhen { get; }
        /// <summary>DoneWhen in words, such as "Status is Sent"; null without DoneWhen.</summary>
        public abstract string DoneCondition { get; }
        /// <summary>The row time Within counts from, such as "PaidAt".</summary>
        public abstract string AfterName { get; }
        /// <summary>The request values matchBy reads, such as "OrderId", or "TenantId" and "Number".</summary>
        public abstract IReadOnlyList<string> MatchByParts { get; }
        public abstract bool HasLabel { get; }
        /// <summary>The name given with Named(), or null when the promise uses its default name.</summary>
        public string NamedInCode => NamedAs;

        /// <summary>Where the promise was declared, for the dashboard to name the call.</summary>
        public TickerPromiseDeclaration DeclaredOn { get; set; } = TickerPromiseDeclaration.ConfigurePromises;
        /// <summary>The automatic-fix limit per check, or null when the code leaves fixing to a person.</summary>
        public abstract int? AutoFixMax { get; }
        /// <summary>Whether a row may have more than one successful job; otherwise duplicates are findings.</summary>
        public abstract bool AllowsRepeats { get; }
        protected abstract string NamedAs { get; }

        /// <summary>The promise in one sentence, for the dashboard.</summary>
        public abstract string Summary(TimeSpan within);

        /// <summary>
        /// Reads the rows whose time falls between <paramref name="windowStart"/> and <paramref name="dueBefore"/> and
        /// that are not yet done, newest first, at most <paramref name="maxRows"/>.
        /// </summary>
        public abstract Task<TickerPromiseRowSet> ReadRowsAsync(IServiceProvider services, DateTime windowStart, DateTime dueBefore,
            int maxRows, CancellationToken cancellationToken);

        /// <summary>
        /// The matchBy key of a job's stored request; null when a part of it is empty. False when the request
        /// cannot be read as the promise's request type.
        /// </summary>
        public abstract bool TryReadJobKey(byte[] request, out string key);

        /// <summary>
        /// Reads a job's stored request as the promise's request type, once per job; <see cref="ReadKey"/> then gives
        /// each promise on the function its key. False when the request cannot be read.
        /// </summary>
        public abstract bool TryReadRequest(byte[] request, out object value);

        /// <summary>The matchBy key of a request from <see cref="TryReadRequest"/>; null when a part of it is empty.</summary>
        public abstract string ReadKey(object value);

        internal static TickerPromiseDefinition Create<TContext, TRow, TRequest>(string function, Type functionType,
            Func<TContext, IQueryable<TRow>> rows, Action<TickerPromiseBuilder<TRow, TRequest>> configure)
        {
            ArgumentNullException.ThrowIfNull(rows);
            return Build(function, functionType, typeof(TContext), sp => rows(sp.GetRequiredService<TContext>()), configure);
        }

        internal static TickerPromiseDefinition Create<TRow, TRequest>(string function, Type functionType,
            Func<IServiceProvider, IQueryable<TRow>> rows, Action<TickerPromiseBuilder<TRow, TRequest>> configure)
        {
            ArgumentNullException.ThrowIfNull(rows);
            return Build(function, functionType, null, rows, configure);
        }

        private static TickerPromiseDefinition Build<TRow, TRequest>(string function, Type functionType, Type contextType,
            Func<IServiceProvider, IQueryable<TRow>> rows, Action<TickerPromiseBuilder<TRow, TRequest>> configure)
        {
            ArgumentNullException.ThrowIfNull(configure);
            var builder = new TickerPromiseBuilder<TRow, TRequest>();
            configure(builder);
            return new TickerPromiseDefinition<TRow, TRequest>(function, functionType, contextType, rows, builder);
        }
    }

    /// <summary>Where a promise was declared in code.</summary>
    internal enum TickerPromiseDeclaration
    {
        /// <summary><c>options.ConfigurePromises(p =&gt; p.For(…))</c>.</summary>
        ConfigurePromises,
        /// <summary><c>services.MapTicker&lt;…&gt;().Promise(…)</c>.</summary>
        MapTicker,
    }

    internal sealed class TickerPromiseDefinition<TRow, TRequest> : TickerPromiseDefinition
    {
        private readonly Func<IServiceProvider, IQueryable<TRow>> _rows;
        private readonly TickerPromiseBuilder<TRow, TRequest> _builder;
        private readonly Expression<Func<TRow, bool>> _notDone;

        public TickerPromiseDefinition(string function, Type functionType, Type contextType,
            Func<IServiceProvider, IQueryable<TRow>> rows, TickerPromiseBuilder<TRow, TRequest> builder)
            : base(function, functionType, contextType)
        {
            _rows = rows;
            _builder = builder;
            if (builder.Done != null)
                _notDone = Expression.Lambda<Func<TRow, bool>>(Expression.Not(builder.Done.Body), builder.Done.Parameters);
        }

        public override Type RowType => typeof(TRow);
        public override Type RequestType => typeof(TRequest);
        public override TimeSpan? Within => _builder.WithinValue;
        public override bool HasJob => _builder.JobFactory != null;
        public override bool HasDoneWhen => _notDone != null;
        public override string AfterName => _builder.After?.Name;
        public override IReadOnlyList<string> MatchByParts => _builder.Key?.PartNames ?? Array.Empty<string>();
        public override bool HasLabel => _builder.LabelFactory != null;
        public override string DoneCondition => _builder.Done == null ? null : TickerPromiseText.Condition(_builder.Done) ?? "its DoneWhen";
        public override int? AutoFixMax => _builder.AutoFixMax;
        public override bool AllowsRepeats => _builder.RepeatsAllowed;
        protected override string NamedAs => _builder.Name;

        public override string Summary(TimeSpan within)
        {
            var summary = $"Each {typeof(TRow).Name} → {Function} within {TickerPromiseText.Duration(within)} after {_builder.After.Name}";
            return _notDone == null ? summary : $"{summary}, done when {DoneCondition}";
        }

        public override async Task<TickerPromiseRowSet> ReadRowsAsync(IServiceProvider services, DateTime windowStart,
            DateTime dueBefore, int maxRows, CancellationToken cancellationToken)
        {
            var watched = _builder.After.Where(_rows(services) ?? throw new InvalidOperationException("The rows query returned null."),
                windowStart, dueBefore);
            var open = _notDone == null ? watched : watched.Where(_notDone);
            var page = await ToListAsync(_builder.After.NewestFirst(open).Take(maxRows + 1), cancellationToken).ConfigureAwait(false);

            // A page that holds every open row already says how many there are; only DoneWhen needs the whole window counted.
            var truncated = page.Count > maxRows;
            var set = new TickerPromiseRowSet
            {
                Watched = _notDone == null && !truncated ? page.Count : watched.Count(),
                NotDone = _notDone == null ? null : truncated ? open.Count() : page.Count,
                Truncated = truncated,
            };
            foreach (var row in page.Take(maxRows))
            {
                var request = _builder.JobFactory(row);
                var key = _builder.Key.Read(request);
                set.Rows.Add(new TickerPromiseRow
                {
                    Key = key.Value,
                    DisplayKey = key.Display,
                    MissingPart = key.Missing,
                    Label = _builder.LabelFactory?.Invoke(row),
                    CreateRequest = () => TickerHelper.CreateTickerRequest(request, TickerFunctionProvider.GetRequestTypeInfo<TRequest>(Function)),
                });
            }
            return set;
        }

        public override bool TryReadJobKey(byte[] request, out string key)
        {
            key = TryReadRequest(request, out var value) ? ReadKey(value) : null;
            return value != null;
        }

        public override bool TryReadRequest(byte[] request, out object value)
        {
            value = null;
            try
            {
                value = TickerHelper.ReadTickerRequest(request, TickerFunctionProvider.GetRequestTypeInfo<TRequest>(Function));
            }
            catch (Exception)
            {
                return false;
            }
            return value != null;
        }

        public override string ReadKey(object value) => value is TRequest request ? _builder.Key.Read(request).Value : null;

        private static async Task<List<TRow>> ToListAsync(IQueryable<TRow> query, CancellationToken cancellationToken)
        {
            if (query is not IAsyncEnumerable<TRow> rows)
                return query.ToList();
            var list = new List<TRow>();
            await foreach (var row in rows.WithCancellation(cancellationToken).ConfigureAwait(false))
                list.Add(row);
            return list;
        }
    }

    /// <summary>Rows one check read for a promise.</summary>
    internal sealed class TickerPromiseRowSet
    {
        /// <summary>Rows in the window whose time limit has passed.</summary>
        public int Watched { get; init; }
        /// <summary>Of those, the rows DoneWhen does not match; null without DoneWhen.</summary>
        public int? NotDone { get; init; }
        public bool Truncated { get; init; }
        public List<TickerPromiseRow> Rows { get; } = new();
    }

    internal sealed class TickerPromiseRow
    {
        /// <summary>The normalized matchBy key, or null when a part is empty.</summary>
        public string Key { get; init; }
        public string DisplayKey { get; init; }
        /// <summary>The first empty matchBy part, when <see cref="Key"/> is null.</summary>
        public string MissingPart { get; init; }
        public string Label { get; init; }
        /// <summary>The row's job request, stored the way a job stores it.</summary>
        public Func<byte[]> CreateRequest { get; init; }
    }

    /// <summary>A promise's time column: filters rows into the check window and orders them newest first.</summary>
    internal sealed class TickerPromiseWindow<TRow>
    {
        private readonly Func<IQueryable<TRow>, DateTime, DateTime, IQueryable<TRow>> _where;
        private readonly Func<IQueryable<TRow>, IQueryable<TRow>> _newestFirst;

        private TickerPromiseWindow(LambdaExpression after, Func<IQueryable<TRow>, DateTime, DateTime, IQueryable<TRow>> where,
            Func<IQueryable<TRow>, IQueryable<TRow>> newestFirst)
        {
            Name = TickerPromiseText.Path(after.Body) ?? "its time";
            _where = where;
            _newestFirst = newestFirst;
        }

        public string Name { get; }

        // The bounds are captured, so query providers send them as parameters rather than inline constants.
        public IQueryable<TRow> Where(IQueryable<TRow> rows, DateTime from, DateTime to) => _where(rows, from, to);

        public IQueryable<TRow> NewestFirst(IQueryable<TRow> rows) => _newestFirst(rows);

        public static TickerPromiseWindow<TRow> Create(Expression<Func<TRow, DateTime?>> after)
            => new(Require(after), (rows, from, to) => rows.Where(Bind(after, t => t != null && t >= from && t <= to)),
                rows => rows.OrderByDescending(after));

        public static TickerPromiseWindow<TRow> Create(Expression<Func<TRow, DateTime>> after)
            => new(Require(after), (rows, from, to) => rows.Where(Bind(after, t => t >= from && t <= to)),
                rows => rows.OrderByDescending(after));

        public static TickerPromiseWindow<TRow> Create(Expression<Func<TRow, DateTimeOffset?>> after)
            => new(Require(after), (rows, from, to) =>
                {
                    DateTimeOffset? lo = Utc(from), hi = Utc(to);
                    return rows.Where(Bind(after, t => t != null && t >= lo && t <= hi));
                },
                rows => rows.OrderByDescending(after));

        public static TickerPromiseWindow<TRow> Create(Expression<Func<TRow, DateTimeOffset>> after)
            => new(Require(after), (rows, from, to) =>
                {
                    DateTimeOffset lo = Utc(from), hi = Utc(to);
                    return rows.Where(Bind(after, t => t >= lo && t <= hi));
                },
                rows => rows.OrderByDescending(after));

        private static DateTimeOffset Utc(DateTime value) => new(value.Ticks, TimeSpan.Zero);

        private static T Require<T>(T after) where T : LambdaExpression
            => after ?? throw new ArgumentNullException(nameof(after));

        private static Expression<Func<TRow, bool>> Bind<TValue>(Expression<Func<TRow, TValue>> value, Expression<Func<TValue, bool>> test)
            => Expression.Lambda<Func<TRow, bool>>(new Substitute(test.Parameters[0], value.Body).Visit(test.Body), value.Parameters);

        private sealed class Substitute(ParameterExpression parameter, Expression replacement) : ExpressionVisitor
        {
            protected override Expression VisitParameter(ParameterExpression node) => node == parameter ? replacement : node;
        }
    }

    /// <summary>A promise's matchBy, read into normalized parts so a row's request and a job's request compare equal.</summary>
    internal sealed class TickerPromiseKey<TRequest>
    {
        private readonly (string Name, Func<TRequest, object> Read)[] _parts;

        private TickerPromiseKey((string, Func<TRequest, object>)[] parts) => _parts = parts;

        public IReadOnlyList<string> PartNames => _parts.Select(p => p.Name).ToArray();

        public static TickerPromiseKey<TRequest> Analyze(Expression<Func<TRequest, object>> matchBy)
        {
            var body = StripConvert(matchBy.Body);
            // An anonymous object, a tuple (ValueTuple.Create or new Tuple), or a single value.
            (IReadOnlyList<Expression> arguments, IReadOnlyList<MemberInfo> members) = body switch
            {
                NewExpression { Arguments.Count: > 0 } created => (created.Arguments, created.Members),
                MethodCallExpression { Method.Name: "Create", Arguments.Count: > 0 } call
                    when call.Method.DeclaringType == typeof(ValueTuple) || call.Method.DeclaringType == typeof(Tuple)
                    => (call.Arguments, null),
                _ => (null, null),
            };
            var parts = arguments == null
                ? new[] { (TickerPromiseText.Path(body) ?? "Key", body) }
                : arguments.Select((argument, i) => (members?[i].Name ?? TickerPromiseText.Path(argument) ?? $"Item{i + 1}", argument));
            return new TickerPromiseKey<TRequest>(parts
                .Select(part => (part.Item1, Expression.Lambda<Func<TRequest, object>>(
                    Expression.Convert(part.Item2, typeof(object)), matchBy.Parameters).Compile()))
                .ToArray());
        }

        public (string Value, string Display, string Missing) Read(TRequest request)
        {
            var values = new string[_parts.Length];
            for (var i = 0; i < _parts.Length; i++)
            {
                object value;
                try
                {
                    value = request == null ? null : _parts[i].Read(request);
                }
                catch (NullReferenceException)
                {
                    value = null; // a nested part whose parent is empty
                }
                if ((values[i] = Normalize(value)) == null)
                    return (null, null, _parts[i].Name);
            }
            return (string.Join('\u001f', values), string.Join(" · ", values), null);
        }

        /// <summary>Text that is equal for equal values of any type: 12 and 12.0 match, and so do Guids in any case.</summary>
        internal static string Normalize(object value) => value switch
        {
            null => null,
            string s => s,
            bool b => b ? "true" : "false",
            Guid g => g.ToString("D"),
            DateTime d => (d.Kind == DateTimeKind.Local ? d.ToUniversalTime() : DateTime.SpecifyKind(d, DateTimeKind.Utc))
                .ToString("O", CultureInfo.InvariantCulture),
            DateTimeOffset d => d.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            Enum e => e.ToString(),
            byte or sbyte or short or ushort or int or uint or long or ulong or decimal => Number(Convert.ToDecimal(value, CultureInfo.InvariantCulture)),
            double or float when Convert.ToDouble(value, CultureInfo.InvariantCulture) is var d && double.IsFinite(d) && Math.Abs(d) < 7.9e28
                => Number((decimal)d),
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString(),
        };

        private static string Number(decimal value) => value.ToString("0.############################", CultureInfo.InvariantCulture);

        private static Expression StripConvert(Expression e)
        {
            while (e is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } convert)
                e = convert.Operand;
            return e;
        }
    }

    /// <summary>Plain-words renderings of promise expressions and durations.</summary>
    internal static class TickerPromiseText
    {
        /// <summary>A member path such as "Customer.Id", or null when the expression is not one.</summary>
        public static string Path(Expression e)
        {
            var names = new List<string>();
            while (true)
            {
                switch (e)
                {
                    case UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } convert:
                        e = convert.Operand;
                        continue;
                    case MemberExpression member:
                        names.Insert(0, member.Member.Name);
                        e = member.Expression;
                        continue;
                    case ParameterExpression when names.Count > 0:
                        return string.Join('.', names);
                    default:
                        return null;
                }
            }
        }

        /// <summary>A condition such as "Status is Processed and SentAt is not empty", or null when it is too complex.</summary>
        public static string Condition(LambdaExpression condition) => Render(condition.Body);

        private static string Render(Expression e)
        {
            switch (e)
            {
                case BinaryExpression { NodeType: ExpressionType.AndAlso or ExpressionType.OrElse } logical:
                    return Join(Render(logical.Left), logical.NodeType == ExpressionType.AndAlso ? " and " : " or ", Render(logical.Right));
                case BinaryExpression comparison when Comparison(comparison.NodeType) is { } verb:
                    return Join(Operand(comparison.Left, comparison.Right), $" {verb} ", Operand(comparison.Right, comparison.Left));
                case UnaryExpression { NodeType: ExpressionType.Not } not when Render(not.Operand) is { } inner:
                    return "not " + inner;
                default:
                    return Path(e);
            }
        }

        private static string Comparison(ExpressionType type) => type switch
        {
            ExpressionType.Equal => "is",
            ExpressionType.NotEqual => "is not",
            ExpressionType.GreaterThan => "is over",
            ExpressionType.GreaterThanOrEqual => "is at least",
            ExpressionType.LessThan => "is under",
            ExpressionType.LessThanOrEqual => "is at most",
            _ => null,
        };

        // An enum compared with a constant is compiled as its number; "other" names the enum so the number reads as its name.
        private static string Operand(Expression e, Expression other)
        {
            if (Path(e) is { } path)
                return path;
            var value = Value(e);
            if (value is not { } known)
                return e is ConstantExpression { Value: null } ? "empty" : null;
            var enumType = (other as UnaryExpression)?.Operand.Type is { } t ? Nullable.GetUnderlyingType(t) ?? t : null;
            return enumType is { IsEnum: true } && known is not Enum
                ? Enum.ToObject(enumType, known).ToString()
                : Convert.ToString(known, CultureInfo.InvariantCulture);
        }

        // Constants, and variables a condition captured.
        private static object Value(Expression e) => e switch
        {
            ConstantExpression constant => constant.Value,
            UnaryExpression { NodeType: ExpressionType.Convert } convert => Value(convert.Operand),
            MemberExpression { Member: FieldInfo field } member when member.Expression is null or ConstantExpression
                => field.GetValue((member.Expression as ConstantExpression)?.Value),
            _ => null,
        };

        private static string Join(string left, string separator, string right)
            => left == null || right == null ? null : left + separator + right;

        /// <summary>"5 minutes", "1 hour 30 minutes", "20 seconds".</summary>
        public static string Duration(TimeSpan span)
        {
            var parts = new List<string>();
            Add(parts, span.Days, "day");
            Add(parts, span.Hours, "hour");
            Add(parts, span.Minutes, "minute");
            Add(parts, span.Seconds, "second");
            return parts.Count == 0 ? "no time" : string.Join(' ', parts.Take(2));
        }

        private static void Add(List<string> parts, int value, string unit)
        {
            if (value > 0)
                parts.Add($"{value} {unit}{(value == 1 ? "" : "s")}");
        }
    }
}
