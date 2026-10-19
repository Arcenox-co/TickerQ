#nullable disable
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ.Caching.StackExchangeRedis.Infrastructure;

/// <summary>
/// Streams Redis documents instead of keeping the entire dataset alive for a small page.
/// Arbitrary CLR sort expressions use repeated bounded selection windows. Deep sorted pages
/// therefore trade additional scans for bounded payload memory; they never retain skipped documents.
/// Scans retain only unique ID metadata proportional to candidate count for SCAN de-duplication.
/// Like Redis SCAN itself, this query does not promise a snapshot during concurrent writes.
/// </summary>
internal sealed class RedisTickerQueryable<TEntity> : ITickerQueryable<TEntity>, ITickerProjectionQueryable<TEntity>
{
    internal const int BatchSize = 128;
    private readonly Func<IReadOnlyList<Expression<Func<TEntity, bool>>>, CancellationToken, IAsyncEnumerable<TEntity>> _source;
    private readonly IReadOnlyList<Step> _steps;

    internal RedisTickerQueryable(
        Func<IReadOnlyList<Expression<Func<TEntity, bool>>>, CancellationToken, IAsyncEnumerable<TEntity>> source)
        : this(source, []) { }

    private RedisTickerQueryable(
        Func<IReadOnlyList<Expression<Func<TEntity, bool>>>, CancellationToken, IAsyncEnumerable<TEntity>> source,
        IReadOnlyList<Step> steps)
    {
        _source = source;
        _steps = steps;
    }

    private RedisTickerQueryable<TEntity> Append(Step step) => new(_source, [.. _steps, step]);

    public ITickerQueryable<TEntity> Where(Expression<Func<TEntity, bool>> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return Append(new Step { Predicate = predicate, Filter = predicate.Compile() });
    }

    public ITickerQueryable<TEntity> OrderBy<TKey>(Expression<Func<TEntity, TKey>> selector)
        => Order(selector, false);
    public ITickerQueryable<TEntity> OrderByDescending<TKey>(Expression<Func<TEntity, TKey>> selector)
        => Order(selector, true);

    private ITickerQueryable<TEntity> Order<TKey>(Expression<Func<TEntity, TKey>> selector, bool descending)
    {
        ArgumentNullException.ThrowIfNull(selector);
        var compiled = selector.Compile();
        return Append(new Step
        {
            SortKey = row => compiled(row),
            CompareKeys = (left, right) => descending
                ? Comparer<TKey>.Default.Compare((TKey)right, (TKey)left)
                : Comparer<TKey>.Default.Compare((TKey)left, (TKey)right)
        });
    }

    public ITickerQueryable<TEntity> Skip(int count) => Append(new Step { Skip = Math.Max(0, count) });
    public ITickerQueryable<TEntity> Take(int count) => Append(new Step { Take = Math.Max(0, count) });
    public ITickerQueryable<TEntity> WithRelated(params TickerRelation[] relations) => this;
    public ITickerQueryable<TEntity> AsNoTracking() => this;

    public Task<TEntity[]> ToArrayAsync(CancellationToken cancellationToken = default)
        => MaterializeAsync(static row => row, cancellationToken);

    public Task<TResult[]> ToArrayAsync<TResult>(Expression<Func<TEntity, TResult>> selector,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selector);
        return MaterializeAsync(selector.Compile(), cancellationToken);
    }

    private async Task<TResult[]> MaterializeAsync<TResult>(Func<TEntity, TResult> selector, CancellationToken ct)
    {
        var rows = new List<TResult>();
        await foreach (var row in ProjectAsync(selector, ct).ConfigureAwait(false)) rows.Add(row);
        return rows.ToArray();
    }

    public async Task<TEntity> FirstOrDefaultAsync(CancellationToken cancellationToken = default)
    {
        await foreach (var row in Append(new Step { Take = 1 })
                           .ProjectAsync(static row => row, cancellationToken).ConfigureAwait(false))
            return row;
        return default;
    }

    public async Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        // Ordering cannot affect cardinality unless a subsequent filter observes a window
        // selected by that order. Avoid sorting a dataset merely to count it.
        var windowSeen = false;
        var needsOrder = false;
        foreach (var step in _steps)
        {
            if (step.Skip.HasValue || step.Take.HasValue) windowSeen = true;
            if (windowSeen && step.Filter != null) needsOrder = true;
        }
        var steps = needsOrder ? _steps : _steps.Where(step => step.SortKey == null).ToArray();
        var count = 0;
        await foreach (var _ in Execute(steps, cancellationToken).ConfigureAwait(false))
            count = checked(count + 1);
        return count;
    }

    public Task<PaginationResult<TEntity>> ToPaginatedAsync(int pageNumber, int pageSize,
        CancellationToken cancellationToken = default)
        => PaginateAsync(static row => row, pageNumber, pageSize, cancellationToken);

    public Task<PaginationResult<TResult>> ToPaginatedAsync<TResult>(Expression<Func<TEntity, TResult>> selector,
        int pageNumber, int pageSize, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selector);
        return PaginateAsync(selector.Compile(), pageNumber, pageSize, cancellationToken);
    }

    private async Task<PaginationResult<TResult>> PaginateAsync<TResult>(Func<TEntity, TResult> selector,
        int pageNumber, int pageSize, CancellationToken ct)
    {
        pageNumber = Math.Max(1, pageNumber);
        pageSize = Math.Clamp(pageSize, 1, 1000);
        var count = await CountAsync(ct).ConfigureAwait(false);
        var skip = (long)(pageNumber - 1) * pageSize;
        if (skip >= count) return new PaginationResult<TResult>([], count, pageNumber, pageSize);
        var page = await Append(new Step { Skip = skip }).Append(new Step { Take = pageSize })
            .MaterializeAsync(selector, ct).ConfigureAwait(false);
        return new PaginationResult<TResult>(page, count, pageNumber, pageSize);
    }

    private IAsyncEnumerable<TEntity> Source(CancellationToken ct)
    {
        var predicates = new List<Expression<Func<TEntity, bool>>>();
        foreach (var step in _steps)
        {
            if (step.Skip.HasValue || step.Take.HasValue) break;
            if (step.Predicate != null) predicates.Add(step.Predicate);
        }
        return _source(predicates, ct);
    }

    private IAsyncEnumerable<TEntity> Execute(IReadOnlyList<Step> steps, CancellationToken ct)
    {
        Func<IAsyncEnumerable<TEntity>> pipeline = () => Source(ct);
        foreach (var step in steps)
        {
            var previous = pipeline;
            pipeline = step.SortKey != null
                ? () => SortAsync(previous, step, static row => row, ct)
                : () => ApplyAsync(previous(), step, ct);
        }
        return pipeline();
    }

    private IAsyncEnumerable<TResult> ProjectAsync<TResult>(Func<TEntity, TResult> selector, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        // Project candidates before retaining them in the sort window. Dashboard results
        // therefore do not keep request blobs or child aggregates alive while scanning.
        var sortIndex = -1;
        for (var i = 0; i < _steps.Count; i++)
        {
            if (_steps[i].SortKey != null) sortIndex = i;
            else if (_steps[i].Filter != null) sortIndex = -1;
        }
        if (sortIndex < 0) return SelectAsync(Execute(_steps, ct), selector, ct);
        var prefix = _steps.Take(sortIndex).ToArray();
        long needed = long.MaxValue;
        for (var i = _steps.Count - 1; i > sortIndex; i--)
        {
            var step = _steps[i];
            if (step.Take.HasValue) needed = Math.Min(needed, step.Take.Value);
            if (step.Skip.HasValue) needed = needed > long.MaxValue - step.Skip.Value
                ? long.MaxValue : needed + step.Skip.Value;
        }
        var windowSize = (int)Math.Clamp(needed, 1, BatchSize);
        IAsyncEnumerable<TResult> result = SortAsync(() => Execute(prefix, ct), _steps[sortIndex], selector, ct, windowSize);
        foreach (var step in _steps.Skip(sortIndex + 1)) result = ApplyWindowAsync(result, step, ct);
        return result;
    }

    private static async IAsyncEnumerable<TResult> SelectAsync<TResult>(IAsyncEnumerable<TEntity> source,
        Func<TEntity, TResult> selector, [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var row in source.WithCancellation(ct).ConfigureAwait(false))
        {
            ct.ThrowIfCancellationRequested();
            yield return selector(row);
        }
    }

    private static async IAsyncEnumerable<TEntity> ApplyAsync(IAsyncEnumerable<TEntity> source,
        Step step, [EnumeratorCancellation] CancellationToken ct)
    {
        if (step.Filter == null)
        {
            await foreach (var row in ApplyWindowAsync(source, step, ct).ConfigureAwait(false)) yield return row;
            yield break;
        }
        await foreach (var row in source.WithCancellation(ct).ConfigureAwait(false))
        {
            ct.ThrowIfCancellationRequested();
            if (step.Filter(row)) yield return row;
        }
    }

    private static async IAsyncEnumerable<TResult> ApplyWindowAsync<TResult>(IAsyncEnumerable<TResult> source,
        Step step, [EnumeratorCancellation] CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var skip = step.Skip ?? 0;
        var take = step.Take ?? long.MaxValue;
        if (take == 0) yield break;
        await foreach (var row in source.WithCancellation(ct).ConfigureAwait(false))
        {
            ct.ThrowIfCancellationRequested();
            if (skip > 0) { skip--; continue; }
            yield return row;
            if (--take == 0) yield break;
        }
    }

    private static async IAsyncEnumerable<TResult> SortAsync<TResult>(Func<IAsyncEnumerable<TEntity>> source,
        Step step, Func<TEntity, TResult> selector, [EnumeratorCancellation] CancellationToken ct, int windowSize = BatchSize)
    {
        var comparer = Comparer<Candidate<TResult>>.Create((left, right) =>
        {
            var result = step.CompareKeys(left.Key, right.Key);
            return result != 0 ? result : left.Ordinal.CompareTo(right.Ordinal);
        });
        Candidate<TResult> boundary = null;
        while (true)
        {
            var candidates = new SortedSet<Candidate<TResult>>(comparer);
            long ordinal = 0;
            await foreach (var row in source().WithCancellation(ct).ConfigureAwait(false))
            {
                ct.ThrowIfCancellationRequested();
                var candidate = new Candidate<TResult>(step.SortKey(row), ordinal++);
                if (boundary != null && comparer.Compare(candidate, boundary) <= 0) continue;
                if (candidates.Count == windowSize && comparer.Compare(candidate, candidates.Max) >= 0) continue;
                candidate.Value = selector(row);
                candidates.Add(candidate);
                if (candidates.Count > windowSize) candidates.Remove(candidates.Max);
            }
            if (candidates.Count == 0) yield break;
            foreach (var candidate in candidates)
            {
                ct.ThrowIfCancellationRequested();
                yield return candidate.Value;
            }
            if (candidates.Count < windowSize) yield break;
            var last = candidates.Max;
            boundary = new Candidate<TResult>(last.Key, last.Ordinal);
        }
    }

    private sealed class Candidate<TResult>(object key, long ordinal)
    {
        internal object Key { get; } = key;
        internal long Ordinal { get; } = ordinal;
        internal TResult Value { get; set; }
    }

    private sealed class Step
    {
        internal Expression<Func<TEntity, bool>> Predicate { get; init; }
        internal Func<TEntity, bool> Filter { get; init; }
        internal Func<TEntity, object> SortKey { get; init; }
        internal Comparison<object> CompareKeys { get; init; }
        internal long? Skip { get; init; }
        internal long? Take { get; init; }
    }
}
