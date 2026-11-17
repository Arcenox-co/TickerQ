using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Infrastructure;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Promises;

namespace TickerQ.Promises;

/// <summary>
/// One function's jobs as this node last saw them, with each job's matchBy key per promise. A check refreshes it
/// instead of re-reading every job of the window. Where the store serves status and finish-time reads from an index
/// (<see cref="ITickerPersistenceProvider{TTimeTicker,TCronTicker}.SupportsPromiseDeltaReads"/>), only jobs that
/// finished since the last refresh, jobs still waiting or running, and the few the index held as waiting are read.
/// Where the store keeps a change feed (<see cref="ITickerPersistenceProvider{TTimeTicker,TCronTicker}.SupportsPromiseChangeFeed"/>),
/// only the aggregates written since the last refresh are read. Otherwise, and on the first use and every
/// <see cref="TickerPromiseOptions.FullReloadInterval"/>, the whole window is read again. A job's request is parsed
/// once, when the job is first seen or after it was written again.
/// </summary>
internal sealed class TickerPromiseJobIndex<TTimeTicker> where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
{
    // Finished jobs are read back this far past the last refresh, so a job stamped by a node whose clock lags is
    // still seen. Few jobs finish in half an hour next to a month's worth, so the overlap costs little.
    internal static readonly TimeSpan Overlap = TimeSpan.FromMinutes(30);
    // A refresh older than this reads the window again rather than a delta that could be large.
    private static readonly TimeSpan MaxDeltaGap = TimeSpan.FromHours(1);
    private const int IdChunk = 500;

    private static readonly Expression<Func<TTimeTicker, TTimeTicker>> Columns = CreateColumns();
    private static readonly Expression<Func<TTimeTicker, TTimeTicker>> RequestColumns = CreateRequestColumns();

    private readonly string _function;
    private readonly TickerPromiseDefinition[] _definitions;
    private readonly Func<ITickerQueryable<TTimeTicker>> _query;
    private readonly bool _incremental;
    private readonly ChangeFeed _feed;
    private readonly TickerPromiseOptions _options;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<Guid, Entry> _entries = new();
    private DateTime? _loadedAt;
    private DateTime? _refreshedAt;

    /// <param name="definitions">The promises on the function; each entry keeps its key per definition, in this order.</param>
    /// <param name="query">The store's monitoring query, including chain steps; the index projects it itself.</param>
    /// <param name="incremental">Whether status and finish-time reads are indexed, so deltas are cheaper than a reload.</param>
    /// <param name="feed">The store's change feed, when it keeps one; its reads return whole documents.</param>
    public TickerPromiseJobIndex(string function, IReadOnlyList<TickerPromiseDefinition> definitions,
        Func<ITickerQueryable<TTimeTicker>> query, bool incremental, ChangeFeed feed, TickerPromiseOptions options)
    {
        _function = function;
        _definitions = definitions.ToArray();
        _query = query;
        _incremental = incremental;
        _feed = feed;
        _options = options;
    }

    /// <summary>A store's change feed: aggregates written since a time, and whole aggregates by root id.</summary>
    internal sealed record ChangeFeed(
        Func<string, DateTime, int, CancellationToken, Task<TTimeTicker[]>> ChangedSince,
        Func<Guid[], CancellationToken, Task<TTimeTicker[]>> Aggregates);

    /// <summary>A job as the index holds it: no payload, its key per promise, and whether its request was readable.</summary>
    internal sealed record Entry(Guid Id, Guid? ParentId, Guid RootId, TickerStatus Status, DateTime CreatedAt, DateTime UpdatedAt,
        DateTime? ExecutionTime, DateTime? ExecutedAt, DateTime? LockedAt, string Description, string[] Keys, bool Unreadable)
    {
        public bool IsTerminal => Status is TickerStatus.Done or TickerStatus.DueDone or TickerStatus.Failed
            or TickerStatus.Cancelled or TickerStatus.Skipped;
    }

    /// <summary>The jobs a check works with, after a refresh.</summary>
    internal sealed class View
    {
        /// <summary>The window's jobs, newest first, at most <see cref="TickerPromiseOptions.MaxJobsPerCheck"/>.</summary>
        public IReadOnlyList<Entry> Jobs { get; init; }
        /// <summary>Jobs whose request could not be read, so their rows are unknown.</summary>
        public int Unreadable { get; init; }
        /// <summary>The earliest row time the jobs cover: the window start, or later when the window was capped.</summary>
        public DateTime CoveredFrom { get; init; }
        /// <summary>Whether this refresh read the whole window.</summary>
        public bool Reloaded { get; init; }
    }

    /// <summary>Brings the index up to date and returns the window's jobs. Refreshes run one at a time.</summary>
    public async Task<View> RefreshAsync(DateTime now, DateTime windowStart, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var createdFrom = windowStart.AddDays(-1);
            var reload = (!_incremental && _feed == null) || _loadedAt == null || now - _loadedAt.Value >= _options.FullReloadInterval
                         || _refreshedAt == null || now - _refreshedAt.Value > MaxDeltaGap
                         || !await DeltaAsync(now, cancellationToken).ConfigureAwait(false);
            if (reload)
            {
                await ReloadAsync(createdFrom, cancellationToken).ConfigureAwait(false);
                _loadedAt = now;
            }
            _refreshedAt = now;
            return Trim(createdFrom, windowStart, reload);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Where a promise's key sits in each entry's keys.</summary>
    public int SlotOf(TickerPromiseDefinition definition) => Array.IndexOf(_definitions, definition);

    // A store with a feed hands back whole documents, request included, so its scans are not projected either.
    private ITickerQueryable<TTimeTicker> Scan()
        => _feed != null ? _query() : DashboardMetadataQueryable<TTimeTicker>.Create(_query(), Columns);

    // The same jobs a full read always took: the window's, by creation, plus any still waiting or running.
    private async Task ReloadAsync(DateTime createdFrom, CancellationToken cancellationToken)
    {
        var function = _function;
        var jobs = await Scan()
            .Where(x => x.Function == function && x.OriginCronTickerId == null &&
                        (x.CreatedAt >= createdFrom || x.Status == TickerStatus.Idle ||
                         x.Status == TickerStatus.Queued || x.Status == TickerStatus.InProgress))
            .OrderByDescending(x => x.CreatedAt).Take(_options.MaxJobsPerCheck + 1)
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
        // Keys already parsed are kept; everything else about a job is replaced.
        var keys = await HydrateAsync(jobs, cancellationToken).ConfigureAwait(false);
        _entries.Clear();
        foreach (var job in jobs)
            _entries[job.Id] = Create(job, keys[job.Id]);
    }

    /// <summary>Reads what changed since the last refresh. False when the delta was too large to be worth it.</summary>
    private async Task<bool> DeltaAsync(DateTime now, CancellationToken cancellationToken)
    {
        var function = _function;
        var since = _refreshedAt.Value - Overlap;
        var limit = _options.MaxJobsPerCheck + 1;
        var seen = new Dictionary<Guid, TTimeTicker>();

        if (_feed != null)
        {
            // Every write bumps the aggregate in the feed, so one read covers new, finished and re-run jobs.
            var changed = await _feed.ChangedSince(function, since, _options.MaxJobsPerCheck, cancellationToken).ConfigureAwait(false);
            if (changed == null)
                return false;
            foreach (var job in changed)
                seen[job.Id] = job;
            await RefetchUnaccountedAsync(seen, cancellationToken).ConfigureAwait(false);
            await ApplyAsync(seen.Values, cancellationToken).ConfigureAwait(false);
            return true;
        }

        // Waiting or running, whatever their age: the status index serves this.
        var pending = await Scan()
            .Where(x => x.Function == function && x.OriginCronTickerId == null &&
                        (x.Status == TickerStatus.Idle || x.Status == TickerStatus.Queued || x.Status == TickerStatus.InProgress))
            .OrderByDescending(x => x.CreatedAt).Take(limit).ToArrayAsync(cancellationToken).ConfigureAwait(false);
        if (pending.Length >= limit)
            return false;

        // Finished since the last refresh. Roots and chain steps are read apart so a store whose finish-time index
        // starts with the parent can use it for both.
        var finishedRoots = await Scan()
            .Where(x => x.Function == function && x.OriginCronTickerId == null && x.ParentId == null && x.ExecutedAt >= since &&
                        (x.Status == TickerStatus.Done || x.Status == TickerStatus.DueDone || x.Status == TickerStatus.Failed ||
                         x.Status == TickerStatus.Cancelled || x.Status == TickerStatus.Skipped))
            .OrderByDescending(x => x.CreatedAt).Take(limit).ToArrayAsync(cancellationToken).ConfigureAwait(false);
        if (finishedRoots.Length >= limit)
            return false;
        var finishedSteps = await Scan()
            .Where(x => x.Function == function && x.OriginCronTickerId == null && x.ParentId != null && x.ExecutedAt >= since &&
                        (x.Status == TickerStatus.Done || x.Status == TickerStatus.DueDone || x.Status == TickerStatus.Failed ||
                         x.Status == TickerStatus.Cancelled || x.Status == TickerStatus.Skipped))
            .OrderByDescending(x => x.CreatedAt).Take(limit).ToArrayAsync(cancellationToken).ConfigureAwait(false);
        if (finishedSteps.Length >= limit)
            return false;

        foreach (var job in pending.Concat(finishedRoots).Concat(finishedSteps))
            seen[job.Id] = job;
        await RefetchUnaccountedAsync(seen, cancellationToken).ConfigureAwait(false);
        await ApplyAsync(seen.Values, cancellationToken).ConfigureAwait(false);
        return true;
    }

    // Jobs the index held as waiting that no read returned: they finished without a finish time, or were deleted.
    private async Task RefetchUnaccountedAsync(Dictionary<Guid, TTimeTicker> seen, CancellationToken cancellationToken)
    {
        var unaccounted = _entries.Values.Where(e => !e.IsTerminal && !seen.ContainsKey(e.Id)).ToArray();
        if (unaccounted.Length == 0)
            return;
        var found = new HashSet<Guid>();
        if (_feed != null)
        {
            // The feed reads whole aggregates, so a chain step is found through its root.
            var roots = unaccounted.Select(e => e.RootId).Distinct().ToArray();
            foreach (var chunk in roots.Chunk(IdChunk))
                foreach (var job in await _feed.Aggregates(chunk, cancellationToken).ConfigureAwait(false))
                    if (_entries.ContainsKey(job.Id) && !seen.ContainsKey(job.Id))
                    {
                        seen[job.Id] = job;
                        found.Add(job.Id);
                    }
        }
        else
        {
            foreach (var chunk in unaccounted.Select(e => e.Id).Chunk(IdChunk))
            {
                foreach (var job in await Scan().Where(IdIn(chunk)).ToArrayAsync(cancellationToken).ConfigureAwait(false))
                {
                    seen[job.Id] = job;
                    found.Add(job.Id);
                }
            }
        }
        foreach (var entry in unaccounted)
            if (!found.Contains(entry.Id))
                _entries.Remove(entry.Id);
    }

    private async Task ApplyAsync(IEnumerable<TTimeTicker> jobs, CancellationToken cancellationToken)
    {
        var list = jobs.ToList();
        var keys = await HydrateAsync(list, cancellationToken).ConfigureAwait(false);
        foreach (var job in list)
            _entries[job.Id] = Create(job, keys[job.Id]);
    }

    /// <summary>
    /// The keys of each job: kept from the index when it knows the job as it is now, else read from its request. A job
    /// written since the index last saw it is read again, so a request edited while the job waited gets its new key.
    /// </summary>
    private async Task<Dictionary<Guid, (string[] Keys, bool Unreadable)>> HydrateAsync(IEnumerable<TTimeTicker> jobs,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, (string[], bool)>();
        var unknown = new List<Guid>();
        foreach (var job in jobs)
        {
            if (_entries.TryGetValue(job.Id, out var known) && known.UpdatedAt == job.UpdatedAt)
                result[job.Id] = (known.Keys, known.Unreadable);
            else if (job.Request != null)
                result[job.Id] = Keys(job.Request); // the read brought the document, request included
            else
                unknown.Add(job.Id);
        }
        foreach (var chunk in unknown.Chunk(IdChunk))
        {
            var requests = await DashboardMetadataQueryable<TTimeTicker>.Create(_query(), RequestColumns)
                .Where(IdIn(chunk)).ToArrayAsync(cancellationToken).ConfigureAwait(false);
            foreach (var job in requests)
                result[job.Id] = Keys(job.Request);
        }
        // A job deleted between the two reads has no request to read; its keys are unknown, like an unreadable one.
        foreach (var id in unknown)
            result.TryAdd(id, (new string[_definitions.Length], true));
        return result;
    }

    private (string[] Keys, bool Unreadable) Keys(byte[] request)
    {
        var keys = new string[_definitions.Length];
        if (_definitions.Length == 0 || !_definitions[0].TryReadRequest(request, out var value))
            return (keys, true);
        for (var i = 0; i < _definitions.Length; i++)
            keys[i] = _definitions[i].ReadKey(value);
        return (keys, false);
    }

    /// <summary>
    /// "Id is one of these", as a balanced tree of equalities on constants rather than a collection parameter: some
    /// relational providers cannot bind a collection parameter, and every store translates equalities.
    /// </summary>
    internal static Expression<Func<TTimeTicker, bool>> IdIn(IReadOnlyList<Guid> ids)
    {
        Expression<Func<TTimeTicker, Guid>> selector = x => x.Id;
        var job = selector.Parameters[0];
        var id = selector.Body;
        Expression Build(int start, int length)
        {
            if (length == 1)
                return Expression.Equal(id, Expression.Constant(ids[start], typeof(Guid)));
            var left = length / 2;
            return Expression.OrElse(Build(start, left), Build(start + left, length - left));
        }
        return Expression.Lambda<Func<TTimeTicker, bool>>(ids.Count == 0 ? Expression.Constant(false) : Build(0, ids.Count), job);
    }

    private static Entry Create(TTimeTicker job, (string[] Keys, bool Unreadable) keys)
        => new(job.Id, job.ParentId, job.ChainRootId ?? job.Id, job.Status, job.CreatedAt, job.UpdatedAt, job.ExecutionTime,
            job.ExecutedAt, job.LockedAt, job.Description, keys.Keys, keys.Unreadable);

    // Drops jobs that left the window, caps the rest at the newest MaxJobsPerCheck, and says what they cover.
    private View Trim(DateTime createdFrom, DateTime windowStart, bool reloaded)
    {
        foreach (var stale in _entries.Values.Where(e => e.IsTerminal && e.CreatedAt < createdFrom).Select(e => e.Id).ToArray())
            _entries.Remove(stale);

        var jobs = _entries.Values.OrderByDescending(e => e.CreatedAt).ToList();
        var coveredFrom = windowStart;
        if (jobs.Count > _options.MaxJobsPerCheck)
        {
            foreach (var dropped in jobs.Skip(_options.MaxJobsPerCheck))
                if (dropped.IsTerminal)
                    _entries.Remove(dropped.Id);
            jobs.RemoveRange(_options.MaxJobsPerCheck, jobs.Count - _options.MaxJobsPerCheck);
            // Jobs are read a day before their rows' window, so rows from a day after the oldest kept job are covered.
            coveredFrom = jobs[^1].CreatedAt.AddDays(1);
        }
        return new View
        {
            Jobs = jobs,
            Unreadable = jobs.Count(j => j.Unreadable),
            CoveredFrom = coveredFrom,
            Reloaded = reloaded,
        };
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "TickerPromiseService preserves public constructors and properties on TTimeTicker; the expression binds only those preserved members.")]
    private static Expression<Func<TTimeTicker, TTimeTicker>> CreateColumns() => e => new TTimeTicker
    {
        Id = e.Id, ParentId = e.ParentId, ChainRootId = e.ChainRootId, Status = e.Status, CreatedAt = e.CreatedAt,
        UpdatedAt = e.UpdatedAt, ExecutionTime = e.ExecutionTime, ExecutedAt = e.ExecutedAt, LockedAt = e.LockedAt,
        Description = e.Description,
    };

    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "TickerPromiseService preserves public constructors and properties on TTimeTicker; the expression binds only those preserved members.")]
    private static Expression<Func<TTimeTicker, TTimeTicker>> CreateRequestColumns() => e => new TTimeTicker
    {
        Id = e.Id, Request = e.Request,
    };
}
