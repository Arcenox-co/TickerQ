using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Events;
using TickerQ.Utilities.Infrastructure;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Models;
using TickerQ.Utilities.Promises;

namespace TickerQ.Promises;

/// <summary>
/// Checks promises against the application's rows and TickerQ's jobs. Each promise keeps one
/// <see cref="SavedTickerDefinitionKind.Promise"/> row in the definitions store, changed only through optimistic
/// version checks, so every node reads and writes the same state. Scheduled checks run on the node holding the
/// promise's lease; only that node fixes automatically and publishes events. Fix jobs get ids derived from their
/// finding, so a fix run twice, on one node or two, creates one job.
/// </summary>
internal sealed class TickerPromiseService<
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicProperties)] TTimeTicker,
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicProperties)] TCronTicker>
    : ITickerPromiseService
    where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
    where TCronTicker : CronTickerEntity, new()
{
    private const int MaxStoredFindings = 200;
    private const int MaxActivity = 50;
    private const int MaxDismissals = 1000;
    private const int MaxFixAll = 100;
    private const int DefaultAutoFixMax = 50;
    private const int MaxUpdateAttempts = 5;
    private const int MaxListedJobs = 100;
    private const int MaxAnalyticsJobs = 20_000;
    // Fix jobs are told apart by their description; FixedBy is what follows the last " · by ".
    private const string FixPrefix = "Promise fix · ";
    private const string AutoFixPrefix = "Promise auto-fix · ";
    private const string FixedBySeparator = " · by ";
    // A queued job locked this recently is being started by a node.
    private static readonly TimeSpan FreshQueuedLock = TimeSpan.FromMinutes(3);

    private static readonly Expression<Func<TTimeTicker, TTimeTicker>> ErrorColumns = CreateErrorColumns();
    private static readonly Expression<Func<TTimeTicker, TTimeTicker>> ListedJobColumns = CreateListedJobColumns();
    private static readonly Expression<Func<TTimeTicker, TTimeTicker>> AnalyticsColumns = CreateAnalyticsColumns();

    private readonly Promise[] _promises;
    private readonly TickerPromiseOptions _options;
    private readonly JobRetentionOptions _retention;
    private readonly ITickerPersistenceProvider<TTimeTicker, TCronTicker> _persistence;
    private readonly StateStore _state;
    private readonly ITimeTickerManager<TTimeTicker> _jobs;
    private readonly ITickerQDispatcher _dispatcher;
    private readonly IServiceScopeFactory _scopes;
    private readonly ITickerClock _clock;
    private readonly TickerQEventPublisher _events;
    private readonly ILogger<TickerPromiseService<TTimeTicker, TCronTicker>> _logger;
    private readonly string _holder = $"{Environment.MachineName}/{Guid.NewGuid():N}";
    // One job index per function on this node, shared by its promises and by scheduled and dashboard checks.
    private readonly ConcurrentDictionary<string, TickerPromiseJobIndex<TTimeTicker>> _indexes = new(StringComparer.Ordinal);

    public TickerPromiseService(
        IEnumerable<TickerPromiseDefinition> definitions,
        TickerPromiseOptions options,
        JobRetentionOptions retention,
        ITickerPersistenceProvider<TTimeTicker, TCronTicker> persistence,
        ITimeTickerManager<TTimeTicker> jobs,
        ITickerQDispatcher dispatcher,
        IServiceScopeFactory scopes,
        ITickerClock clock,
        TickerExecutionContext executionContext,
        TickerQEventPublisher events,
        ILogger<TickerPromiseService<TTimeTicker, TCronTicker>> logger)
    {
        // Mongo ids are global, so the row id includes the runtime partition.
        var partition = (executionContext.RuntimePartition ?? TickerQRuntimePartition.LegacyGlobal).StorageKey;
        _promises = definitions
            .Select(d => new Promise(d, TickerPromiseIds.StateRow(partition, d.Name)))
            .ToArray();
        _options = options;
        _retention = retention;
        _persistence = persistence;
        _state = StateStore.For(persistence);
        _jobs = jobs;
        _dispatcher = dispatcher;
        _scopes = scopes;
        _clock = clock;
        _events = events;
        _logger = logger;
    }

    public bool HasPromises => _promises.Length > 0;

    private StateStore State => _state
        ?? throw new TickerPromiseException(501, "The operational store keeps no promise state, so it cannot keep promise results.");

    public async Task<IReadOnlyList<TickerPromiseSnapshot>> ListAsync(CancellationToken cancellationToken = default)
    {
        if (_promises.Length == 0)
            return Array.Empty<TickerPromiseSnapshot>();
        var rows = await State.ListAsync(cancellationToken).ConfigureAwait(false);
        return _promises.Select(p => Snapshot(p, rows.TryGetValue(p.RowId, out var row) ? Read(row) : new TickerPromiseState())).ToList();
    }

    public async Task<TickerPromiseSnapshot> CheckAsync(string id, CancellationToken cancellationToken = default)
    {
        var promise = Find(id);
        var check = await EvaluateAsync(promise, await ReadAsync(promise, cancellationToken).ConfigureAwait(false), cancellationToken)
            .ConfigureAwait(false);
        var saved = await UpdateAsync(promise, s =>
        {
            // A check that finished meanwhile is newer; keep it.
            if (s.Result != null && s.Result.CheckedAtUtc > check.At)
                return false;
            Apply(s, check);
            return true;
        }, cancellationToken).ConfigureAwait(false);
        return Snapshot(promise, saved ?? await ReadAsync(promise, cancellationToken).ConfigureAwait(false));
    }

    public async Task<TickerPromiseSnapshot> UpdateSettingsAsync(string id, TickerPromiseSettingsChange change, string user,
        CancellationToken cancellationToken = default)
    {
        if (change.SetWithinSeconds && change.WithinSeconds is { } seconds &&
            (seconds < 1 || seconds > TickerPromiseDefinition.MaxWithin.TotalSeconds))
            throw new TickerPromiseException(400, "The time limit must be between 1 second and 365 days.");

        var promise = Find(id);
        var who = Who(user);
        var saved = await UpdateAsync(promise, s =>
        {
            var changes = new List<string>();
            if (change.Paused is { } paused && paused != s.Settings.Paused)
            {
                s.Settings.Paused = paused;
                s.Settings.EnabledAtUtc = paused ? null : _clock.UtcNow;
                changes.Add(paused ? "disabled the promise" : "enabled the promise");
            }
            if (change.SetWithinSeconds && change.WithinSeconds != s.Settings.WithinSeconds)
            {
                s.Settings.WithinSeconds = change.WithinSeconds;
                changes.Add(change.WithinSeconds is { } within
                    ? $"set the time limit to {TickerPromiseText.Duration(TimeSpan.FromSeconds(within))}"
                    : "restored the time limit from code");
            }
            if (change.SetAutoFix)
            {
                var wasEnabled = AutoFixEnabled(promise, s);
                s.Settings.AutoFix = change.AutoFix;
                var enabled = AutoFixEnabled(promise, s);
                // Turning automatic fixing on, even when it already was, ends a pause.
                var resumed = enabled && s.Result is { AutoFixPaused: true };
                if (resumed)
                {
                    s.Result.AutoFixPaused = false;
                    s.Result.AutoFixPausedReason = null;
                }
                if (resumed || enabled != wasEnabled)
                    changes.Add(!enabled ? "turned automatic fixing off" : resumed ? "resumed automatic fixing" : "turned automatic fixing on");
            }
            if (changes.Count == 0)
                return false;
            AddActivity(s, "settings", $"{who} {string.Join(", ", changes)}.");
            return true;
        }, cancellationToken).ConfigureAwait(false);
        return Snapshot(promise, saved ?? await ReadAsync(promise, cancellationToken).ConfigureAwait(false));
    }

    public async Task<TickerPromiseFixResult> FixAsync(string id, Guid findingId, string user, CancellationToken cancellationToken = default)
    {
        var promise = Find(id);
        var who = Who(user);
        var check = await EvaluateAsync(promise, await ReadAsync(promise, cancellationToken).ConfigureAwait(false), cancellationToken)
            .ConfigureAwait(false);
        var finding = check.Findings.FirstOrDefault(f => f.Id == findingId)
            ?? throw new TickerPromiseException(409, check.Error ?? "This row changed since it was checked. Refresh and try again.");
        if (finding.Fix == TickerPromiseFix.None)
            throw new TickerPromiseException(409, $"This row has no fix to run from here. {finding.Reason}");

        var jobId = await RunFixAsync(promise, finding, check,
            $"{FixPrefix}{promise.Definition.Name} · {finding.Label}{FixedBySeparator}{who}", cancellationToken).ConfigureAwait(false);
        var saved = await UpdateAsync(promise, s =>
        {
            Apply(s, check, new[] { finding.Id });
            AddActivity(s, "fix", finding.Fix == TickerPromiseFix.CancelExtra
                ? $"{who} cancelled the extra job for {finding.Label}."
                : $"{who} ran a fix for {finding.Label}.");
            return true;
        }, cancellationToken).ConfigureAwait(false);
        return new TickerPromiseFixResult { JobId = jobId, Fix = finding.Fix, Promise = Snapshot(promise, saved) };
    }

    public async Task<TickerPromiseFixAllResult> FixAllAsync(string id, string user, CancellationToken cancellationToken = default)
    {
        var promise = Find(id);
        var who = Who(user);
        var state = await ReadAsync(promise, cancellationToken).ConfigureAwait(false);
        var check = await EvaluateAsync(promise, state, cancellationToken).ConfigureAwait(false);
        var dismissed = state.Dismissals.Select(d => d.FindingId).ToHashSet();
        var fixable = check.Findings.Where(f => f.Fix != TickerPromiseFix.None && !dismissed.Contains(f.Id)).ToList();

        var fixedIds = new List<Guid>();
        var failed = 0;
        foreach (var finding in fixable.Take(MaxFixAll))
        {
            try
            {
                await RunFixAsync(promise, finding, check,
                    $"{FixPrefix}{promise.Definition.Name} · {finding.Label}{FixedBySeparator}{who}", cancellationToken).ConfigureAwait(false);
                fixedIds.Add(finding.Id);
            }
            catch (TickerPromiseException ex)
            {
                failed++;
                _logger.LogWarning("TickerQ promise '{Promise}': the fix for {Row} failed: {Reason}",
                    promise.Definition.Name, finding.Label, ex.Message);
            }
        }

        var saved = await UpdateAsync(promise, s =>
        {
            Apply(s, check, fixedIds);
            if (fixable.Count > 0)
                AddActivity(s, "fix_all", $"{who} ran fixes for {Rows(fixedIds.Count)}" +
                                          (failed > 0 ? $"; {Rows(failed)} could not be fixed." : "."));
            return true;
        }, cancellationToken).ConfigureAwait(false);
        return new TickerPromiseFixAllResult
        {
            Fixed = fixedIds.Count,
            Failed = failed,
            Remaining = Math.Max(0, fixable.Count - MaxFixAll),
            Promise = Snapshot(promise, saved),
        };
    }

    public async Task<TickerPromiseJobList> JobsAsync(string id, CancellationToken cancellationToken = default)
    {
        var definition = Find(id).Definition;
        var function = definition.Function;
        var windowStart = _clock.UtcNow - LookBack();
        // The same jobs a check reads: the function's own, not a cron's occurrences, created in the window.
        ITickerQueryable<TTimeTicker> Window() => _persistence.TimeTickersMonitoringQuery()
            .Where(x => x.Function == function && x.OriginCronTickerId == null && x.CreatedAt >= windowStart);
        Task<int> Count(Expression<Func<TTimeTicker, bool>> status) => Window().Where(status).CountAsync(cancellationToken);

        var jobs = await DashboardMetadataQueryable<TTimeTicker>.Create(Window(), ListedJobColumns)
            .OrderByDescending(x => x.CreatedAt).Take(MaxListedJobs)
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
        var list = new TickerPromiseJobList
        {
            WindowStartUtc = windowStart,
            Totals = new TickerPromiseJobTotals
            {
                Done = await Count(x => x.Status == TickerStatus.Done || x.Status == TickerStatus.DueDone).ConfigureAwait(false),
                Failed = await Count(x => x.Status == TickerStatus.Failed).ConfigureAwait(false),
                Running = await Count(x => x.Status == TickerStatus.InProgress).ConfigureAwait(false),
                Waiting = await Count(x => x.Status == TickerStatus.Idle || x.Status == TickerStatus.Queued).ConfigureAwait(false),
                Stopped = await Count(x => x.Status == TickerStatus.Cancelled || x.Status == TickerStatus.Skipped).ConfigureAwait(false),
            },
        };
        foreach (var job in jobs)
        {
            var (source, label, fixedBy) = ReadDescription(definition, job.Description);
            list.Jobs.Add(new TickerPromiseJob
            {
                Id = job.Id,
                Status = job.Status,
                // Keys join their parts with the unit separator; findings show them with " · ".
                Key = definition.TryReadJobKey(job.Request, out var key) ? key?.Replace("\u001f", " · ") : null,
                Label = label,
                Source = source,
                FixedBy = fixedBy,
                CreatedAtUtc = job.CreatedAt,
                ExecutionTimeUtc = job.ExecutionTime,
                ExecutedAtUtc = job.ExecutedAt,
                ElapsedMs = job.ElapsedTime,
                RetryCount = job.RetryCount,
                Retries = job.Retries,
                Error = job.Status == TickerStatus.Skipped ? job.SkippedReason : ErrorMessage(job.ExceptionMessage),
            });
        }
        return list;
    }

    /// <summary>
    /// Who scheduled a job, the row it names and who fixed it, from its description. A fix's description is
    /// "&lt;prefix&gt;&lt;promise&gt; · &lt;row label&gt;[ · by &lt;who&gt;]"; any other job's description is its label.
    /// </summary>
    private static (TickerPromiseJobSource Source, string Label, string FixedBy) ReadDescription(TickerPromiseDefinition definition,
        string description)
    {
        description ??= "";
        var source = description.StartsWith(FixPrefix, StringComparison.Ordinal) ? TickerPromiseJobSource.Fix
            : description.StartsWith(AutoFixPrefix, StringComparison.Ordinal) ? TickerPromiseJobSource.AutoFix
            : TickerPromiseJobSource.App;
        if (source == TickerPromiseJobSource.App)
            return (source, string.IsNullOrWhiteSpace(description) ? null : description, null);
        var by = source == TickerPromiseJobSource.Fix ? description.LastIndexOf(FixedBySeparator, StringComparison.Ordinal) : -1;
        var head = (source == TickerPromiseJobSource.Fix ? FixPrefix : AutoFixPrefix) + definition.Name + " · ";
        var label = description.StartsWith(head, StringComparison.Ordinal)
            ? description[head.Length..(by >= head.Length ? by : description.Length)]
            : null;
        return (source, string.IsNullOrWhiteSpace(label) ? null : label, by < 0 ? null : description[(by + FixedBySeparator.Length)..]);
    }

    public async Task<IReadOnlyList<TickerPromiseAnalytics>> AnalyticsAsync(string id, int days, int offsetMinutes,
        CancellationToken cancellationToken = default)
    {
        if (days is < 1 or > 90)
            throw new TickerPromiseException(400, "days must be between 1 and 90.");
        if (offsetMinutes is < -14 * 60 or > 14 * 60)
            throw new TickerPromiseException(400, "offsetMinutes must be between -840 and 840.");
        var offset = TimeSpan.FromMinutes(offsetMinutes);
        var today = (_clock.UtcNow + offset).Date;
        var from = DateTime.SpecifyKind(today.AddDays(1 - days) - offset, DateTimeKind.Utc);
        var promises = id == null ? _promises : new[] { Find(id) };

        // Promises on the same function share one read.
        var byFunction = new Dictionary<string, TTimeTicker[]>(StringComparer.Ordinal);
        var result = new List<TickerPromiseAnalytics>();
        foreach (var promise in promises)
        {
            var function = promise.Definition.Function;
            if (!byFunction.TryGetValue(function, out var jobs))
            {
                // A job counts on the day it was due; one created the day before may be due inside the range.
                var createdFrom = from.AddDays(-1);
                byFunction[function] = jobs = await DashboardMetadataQueryable<TTimeTicker>.Create(_persistence.TimeTickersMonitoringQuery(), AnalyticsColumns)
                    .Where(x => x.Function == function && x.OriginCronTickerId == null && x.CreatedAt >= createdFrom)
                    .OrderByDescending(x => x.CreatedAt).Take(MaxAnalyticsJobs)
                    .ToArrayAsync(cancellationToken).ConfigureAwait(false);
            }
            result.Add(Analyze(promise, jobs, from, days, offset));
        }
        return result;
    }

    private static TickerPromiseAnalytics Analyze(Promise promise, TTimeTicker[] jobs, DateTime from, int days, TimeSpan offset)
    {
        var analytics = new TickerPromiseAnalytics { PromiseId = promise.Id, FromUtc = from, Truncated = jobs.Length == MaxAnalyticsJobs };
        var buckets = Enumerable.Range(0, days)
            .Select(d => new TickerPromiseDay { Day = (from + offset).AddDays(d).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) })
            .ToArray();
        var durations = new List<long>[days];
        var all = new List<long>();
        foreach (var job in jobs)
        {
            var due = job.ExecutionTime ?? job.CreatedAt;
            if (due < from)
                continue;
            var index = (int)((due - from).Ticks / TimeSpan.TicksPerDay);
            if (index >= days)
                continue;
            var day = buckets[index];
            switch (job.Status)
            {
                case TickerStatus.Done or TickerStatus.DueDone: day.Done++; break;
                case TickerStatus.Failed: day.Failed++; break;
                case TickerStatus.Cancelled or TickerStatus.Skipped: day.Stopped++; break;
                default: day.Pending++; break;
            }
            var description = job.Description ?? "";
            if (description.StartsWith(FixPrefix, StringComparison.Ordinal)) { day.Fixes++; analytics.Sources.Fix++; }
            else if (description.StartsWith(AutoFixPrefix, StringComparison.Ordinal)) { day.AutoFixes++; analytics.Sources.AutoFix++; }
            else analytics.Sources.App++;
            if (job.Status is TickerStatus.Done or TickerStatus.DueDone or TickerStatus.Failed && job.ExecutedAt != null)
            {
                (durations[index] ??= new List<long>()).Add(job.ElapsedTime);
                all.Add(job.ElapsedTime);
            }
        }
        for (var i = 0; i < days; i++)
            (buckets[i].AverageMs, buckets[i].P95Ms) = Durations(durations[i]);
        (analytics.AverageMs, analytics.P95Ms) = Durations(all);
        analytics.Daily.AddRange(buckets);
        return analytics;
    }

    private static (long? Average, long? P95) Durations(List<long> values)
    {
        if (values is not { Count: > 0 })
            return (null, null);
        values.Sort();
        return ((long)values.Average(), values[(int)Math.Ceiling(values.Count * 0.95) - 1]);
    }

    public async Task<TickerPromiseSnapshot> DismissAsync(string id, Guid findingId, string user, string reason,
        CancellationToken cancellationToken = default)
    {
        var promise = Find(id);
        var who = Who(user);
        reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (reason is { Length: > 500 })
            reason = reason[..500];

        var saved = await UpdateAsync(promise, s =>
        {
            var finding = s.Result?.Findings.FirstOrDefault(f => f.Id == findingId)
                ?? throw new TickerPromiseException(409, "This row is no longer listed. Refresh and try again.");
            if (finding.Dismissed != null)
                return false;
            var at = _clock.UtcNow;
            s.Dismissals.Add(new TickerPromiseDismissal { FindingId = findingId, By = who, At = at, Reason = reason });
            if (s.Dismissals.Count > MaxDismissals)
                s.Dismissals.RemoveRange(0, s.Dismissals.Count - MaxDismissals);
            finding.Dismissed = new TickerPromiseDismissed { By = who, At = at, Reason = reason };
            s.Result.Counts.NeedsReview = Math.Max(0, s.Result.Counts.NeedsReview - 1);
            s.Result.Counts.Dismissed++;
            AddActivity(s, "dismiss", $"{who} dismissed {finding.Label}{(reason == null ? "." : $": {reason}")}");
            return true;
        }, cancellationToken).ConfigureAwait(false);
        return Snapshot(promise, saved ?? await ReadAsync(promise, cancellationToken).ConfigureAwait(false));
    }

    public async Task CheckDueAsync(CancellationToken cancellationToken = default)
    {
        if (_state == null)
            return;
        foreach (var promise in _promises)
        {
            try
            {
                await CheckOnScheduleAsync(promise, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "TickerQ promise '{Promise}' could not be checked", promise.Definition.Name);
            }
        }
    }

    private async Task CheckOnScheduleAsync(Promise promise, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var leased = await UpdateAsync(promise, s =>
        {
            if (s.Settings.Paused)
                return false;
            if (s.Lease is { } lease && lease.UntilUtc > now && lease.Holder != _holder)
                return false;
            // Another node, or a person, checked it moments ago.
            if (s.Result != null && now - s.Result.CheckedAtUtc < _options.CheckInterval / 2)
                return false;
            s.Lease = new TickerPromiseLease { Holder = _holder, UntilUtc = now + 2 * _options.CheckInterval };
            return true;
        }, cancellationToken).ConfigureAwait(false);
        if (leased == null)
            return;

        Check check;
        AutoFixOutcome autoFix;
        try
        {
            check = await EvaluateAsync(promise, leased, cancellationToken).ConfigureAwait(false);
            autoFix = await AutoFixAsync(promise, leased, check, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Let another node try before the lease would expire on its own.
            await ReleaseLeaseAsync(promise, cancellationToken).ConfigureAwait(false);
            throw;
        }
        List<TickerQEvent> events = null;
        await UpdateAsync(promise, s =>
        {
            // A node that took over after the lease expired owns the result now, as does a check that finished meanwhile.
            if (s.Lease?.Holder != _holder || (s.Result != null && s.Result.CheckedAtUtc > check.At))
                return false;
            Apply(s, check, autoFix.Fixed);
            if (autoFix.PausedReason != null)
            {
                s.Result.AutoFixPaused = true;
                s.Result.AutoFixPausedReason = autoFix.PausedReason;
                AddActivity(s, "auto_fix_paused", autoFix.PausedReason);
            }
            if (autoFix.Fixed.Count > 0)
                AddActivity(s, "auto_fix", $"Fixed {Rows(autoFix.Fixed.Count)} that {(autoFix.Fixed.Count == 1 ? "was" : "were")} never scheduled.");
            events = Transitions(promise, s, check, autoFix);
            s.Lease = null;
            return true;
        }, cancellationToken).ConfigureAwait(false);

        foreach (var e in events ?? new List<TickerQEvent>())
            await _events.PublishAsync(e).ConfigureAwait(false);
    }

    private async Task ReleaseLeaseAsync(Promise promise, CancellationToken cancellationToken)
    {
        try
        {
            await UpdateAsync(promise, s =>
            {
                if (s.Lease?.Holder != _holder)
                    return false;
                s.Lease = null;
                return true;
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug(ex, "TickerQ promise '{Promise}': the lease could not be released", promise.Definition.Name);
        }
    }

    private async Task<AutoFixOutcome> AutoFixAsync(Promise promise, TickerPromiseState state, Check check, CancellationToken cancellationToken)
    {
        var outcome = new AutoFixOutcome();
        if (!AutoFixEnabled(promise, state) || state.Result is { AutoFixPaused: true })
            return outcome;

        // Only rows that were never scheduled: a job that never ran may still start, and running it twice is worse.
        var dismissed = state.Dismissals.Select(d => d.FindingId).ToHashSet();
        var candidates = check.Findings.Where(f => f.Kind == TickerPromiseFindingKind.NoJob && !dismissed.Contains(f.Id)).ToList();
        if (candidates.Count == 0)
            return outcome;

        var max = promise.Definition.AutoFixMax ?? DefaultAutoFixMax;
        if (candidates.Count > max)
        {
            outcome.PausedReason = $"{candidates.Count} {promise.Definition.RowType.Name} rows were never scheduled — " +
                                   $"more than the limit of {max}, so automatic fixing paused.";
            return outcome;
        }

        foreach (var finding in candidates)
        {
            try
            {
                await RunFixAsync(promise, finding, check,
                    $"{AutoFixPrefix}{promise.Definition.Name} · {finding.Label}", cancellationToken).ConfigureAwait(false);
                outcome.Fixed.Add(finding.Id);
            }
            catch (TickerPromiseException ex)
            {
                _logger.LogWarning("TickerQ promise '{Promise}': the automatic fix for {Row} failed: {Reason}",
                    promise.Definition.Name, finding.Label, ex.Message);
            }
        }
        return outcome;
    }

    private async Task<Guid> RunFixAsync(Promise promise, TickerPromiseFinding finding, Check check, string description,
        CancellationToken cancellationToken)
    {
        if (finding.Fix == TickerPromiseFix.CancelExtra)
        {
            // CancelAsync changes only jobs still pending, so one that started meanwhile is left alone.
            var cancelled = new List<Guid>();
            foreach (var extra in check.Duplicates[finding.Id].Extra)
            {
                var result = await _jobs.CancelAsync(extra, cancellationToken).ConfigureAwait(false);
                if (result.IsSucceeded && result.AffectedRows > 0)
                    cancelled.Add(extra);
            }
            return cancelled.Count > 0
                ? cancelled[0]
                : throw new TickerPromiseException(409, "The extra job already started or finished. Refresh and try again.");
        }
        var row = check.Rows[finding.Id];
        if (finding.Fix == TickerPromiseFix.RunNow)
        {
            // RunNow also accepts finished jobs, so check the job is still waiting and is no chain step.
            var job = await _jobs.GetAsync(finding.LastJob.Id, cancellationToken).ConfigureAwait(false);
            if (job is not { Status: TickerStatus.Idle or TickerStatus.Queued, ParentId: null } || !_dispatcher.IsEnabled)
                throw new TickerPromiseException(409, "The waiting job can't be started from here anymore. Refresh and try again.");
            var run = await _jobs.RunNowAsync(job.Id, cancellationToken).ConfigureAwait(false);
            if (!run.IsSucceeded)
                throw new TickerPromiseException(409, $"The waiting job could not be started: {run.Exception?.Message}");
            return job.Id;
        }

        var id = TickerPromiseIds.Create("fix", finding.Id.ToString("N"));
        Exception failure;
        try
        {
            var added = await _jobs.AddAsync(new TTimeTicker
            {
                Id = id,
                Function = promise.Definition.Function,
                Request = row.CreateRequest(),
                ExecutionTime = _clock.UtcNow,
                Description = description.Length > 500 ? description[..500] : description,
            }, cancellationToken).ConfigureAwait(false);
            if (added.IsSucceeded)
                return id;
            failure = added.Exception;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            failure = ex;
        }
        // Created earlier, by another node or an earlier attempt: the fix already exists.
        if (await _jobs.GetAsync(id, cancellationToken).ConfigureAwait(false) != null)
            return id;
        throw new TickerPromiseException(409, $"The fix job could not be created: {failure?.Message ?? "unknown error"}");
    }

    private async Task<Check> EvaluateAsync(Promise promise, TickerPromiseState state, CancellationToken cancellationToken)
    {
        var definition = promise.Definition;
        var started = Stopwatch.GetTimestamp();
        var now = _clock.UtcNow;
        var check = new Check { At = now, WindowStart = now - LookBack() };
        try
        {
            // Jobs first: a job created while the rows are read is then missing at worst, never a row's only proof.
            var index = IndexFor(definition.Function);
            var view = await index.RefreshAsync(now, check.WindowStart, cancellationToken).ConfigureAwait(false);
            var jobsByKey = GroupByKey(view.Jobs, index.SlotOf(definition));
            var unreadable = view.Unreadable;
            // More jobs than one check keeps: check only the rows the jobs cover.
            var jobsTruncated = view.CoveredFrom > check.WindowStart;
            if (jobsTruncated)
                check.WindowStart = view.CoveredFrom;
            TickerPromiseRowSet rows;
            await using (var scope = _scopes.CreateAsyncScope())
            {
                rows = await definition.ReadRowsAsync(scope.ServiceProvider, check.WindowStart, now - Within(promise, state),
                    _options.MaxRowsPerCheck, cancellationToken).ConfigureAwait(false);
            }

            check.Truncated = rows.Truncated || jobsTruncated;
            check.Counts.Watched = rows.Watched;
            if (rows.NotDone is { } notDone)
                check.Counts.Done = rows.Watched - notDone;
            if (unreadable > 0)
                check.Error = $"{unreadable} {definition.Function} job{(unreadable == 1 ? "" : "s")} could not be read, " +
                              "so 'never scheduled' isn't reported this time.";
            string missingPart = null;
            foreach (var row in rows.Rows)
            {
                if (row.Key == null)
                {
                    check.Counts.Unmatchable++;
                    missingPart ??= row.MissingPart;
                    continue;
                }
                Classify(definition, row, jobsByKey.TryGetValue(row.Key, out var matches) ? matches : null, unreadable > 0, check);
            }
            if (!definition.AllowsRepeats)
                FindDuplicates(definition, jobsByKey, rows, check);
            await FillErrorsAsync(check, cancellationToken).ConfigureAwait(false);
            if (missingPart != null)
                _logger.LogWarning("TickerQ promise '{Promise}': {Count} rows have no {Part}, so no job can be matched to them",
                    definition.Name, check.Counts.Unmatchable, missingPart);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "TickerQ promise '{Promise}' could not be checked", definition.Name);
            check = new Check { At = now, WindowStart = check.WindowStart, Error = $"The check failed: {ex.Message}" };
        }
        check.DurationMs = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        return check;
    }

    private void Classify(TickerPromiseDefinition definition, TickerPromiseRow row, List<TickerPromiseJobIndex<TTimeTicker>.Entry> jobs, bool withholdNoJob, Check check)
    {
        var now = check.At;
        var function = definition.Function;
        var succeeded = jobs?.Where(j => j.Status is TickerStatus.Done or TickerStatus.DueDone).MaxBy(j => j.CreatedAt);

        if (succeeded != null && !definition.HasDoneWhen)
        {
            check.Counts.Done++;
            return;
        }
        if (jobs != null && jobs.Any(j => IsRunning(j, now)))
        {
            check.Counts.Running++;
            return;
        }
        // An app often marks the row done a moment after its job, for example from a message, so give it the grace.
        if (succeeded != null && (succeeded.ExecutedAt ?? succeeded.CreatedAt) > now - _options.RowUpdateGrace)
        {
            check.Counts.Running++;
            return;
        }
        if (succeeded != null)
        {
            Add(TickerPromiseFindingKind.DoneButNotApplied,
                $"The job succeeded {Ago(now - (succeeded.ExecutedAt ?? succeeded.CreatedAt))} ago, but this row still doesn't meet “{definition.DoneCondition}”.",
                succeeded, TickerPromiseFix.None);
            return;
        }
        var waiting = jobs?.Where(j => j.Status is TickerStatus.Idle or TickerStatus.Queued).MaxBy(j => j.CreatedAt);
        if (waiting != null)
        {
            var dueFor = now - (waiting.ExecutionTime ?? waiting.CreatedAt);
            Add(TickerPromiseFindingKind.NeverRan, $"The job was due {Ago(dueFor)} ago and hasn't started.", waiting,
                waiting.ParentId == null && _dispatcher.IsEnabled ? TickerPromiseFix.RunNow : TickerPromiseFix.None);
            return;
        }
        var latest = jobs?.MaxBy(j => j.CreatedAt);
        if (latest == null)
        {
            // Some jobs could not be read, and one of them may be this row's.
            if (!withholdNoJob)
                Add(TickerPromiseFindingKind.NoJob, $"No {function} job was ever scheduled for this row.", null, TickerPromiseFix.NewJob);
        }
        else if (latest.Status == TickerStatus.Failed)
            check.Counts.Failed++; // executions, retries and alerts own failures
        else
            Add(TickerPromiseFindingKind.NeverRan,
                $"The job was {latest.Status.ToString().ToLowerInvariant()} before it ran.", latest, TickerPromiseFix.NewJob);

        void Add(TickerPromiseFindingKind kind, string reason, TickerPromiseJobIndex<TTimeTicker>.Entry job, TickerPromiseFix fix)
        {
            var finding = new TickerPromiseFinding
            {
                Id = TickerPromiseIds.Create(function, definition.RowType.FullName, row.Key, kind.ToString(), job?.Id.ToString("N") ?? "-"),
                Kind = kind,
                Key = row.DisplayKey,
                Label = row.Label ?? row.DisplayKey,
                Reason = reason,
                LastJob = job == null ? null : new TickerPromiseLastJob { Id = job.Id, Status = job.Status },
                Fix = fix,
                DetectedAtUtc = now,
            };
            check.Findings.Add(finding);
            check.Rows[finding.Id] = row;
        }
    }

    /// <summary>
    /// Rows whose job ran twice, or will: two successful jobs, or a waiting job beside one that succeeded, is running
    /// or waits too. Read from the jobs alone, so rows already marked done are covered. Chain steps and retries (the
    /// same job again) don't count, nor do failed or cancelled jobs, which a fix is expected to follow.
    /// </summary>
    private static void FindDuplicates(TickerPromiseDefinition definition, Dictionary<string, List<TickerPromiseJobIndex<TTimeTicker>.Entry>> jobsByKey,
        TickerPromiseRowSet rows, Check check)
    {
        var function = definition.Function;
        var labels = rows.Rows.Where(r => r.Key != null).GroupBy(r => r.Key).ToDictionary(g => g.Key, g => g.First());
        foreach (var (key, all) in jobsByKey)
        {
            var jobs = all.Where(j => j.ParentId == null && j.CreatedAt >= check.WindowStart).ToList();
            var succeeded = jobs.Where(j => j.Status is TickerStatus.Done or TickerStatus.DueDone).OrderBy(j => j.CreatedAt).ToList();
            var running = jobs.Count(j => j.Status == TickerStatus.InProgress);
            var waiting = jobs.Where(j => j.Status is TickerStatus.Idle or TickerStatus.Queued).OrderBy(j => j.CreatedAt).ToList();
            labels.TryGetValue(key, out var row);
            var display = row?.DisplayKey ?? key.Replace("\u001f", " · ");

            if (waiting.Count > 0 && succeeded.Count + running + waiting.Count >= 2)
            {
                // Keep the first waiting job unless another already ran or is running; cancel the rest.
                var extra = succeeded.Count + running > 0 ? waiting : waiting.Skip(1).ToList();
                var already = succeeded.Count > 0 ? "the job already succeeded"
                    : running > 0 ? "a job is already running" : "an earlier job is waiting too";
                var reason = $"{(extra.Count == 1 ? "An extra job is" : $"{extra.Count} extra jobs are")} waiting, though {already}.";
                Add(TickerPromiseFindingKind.WillRunTwice, reason, extra[^1], TickerPromiseFix.CancelExtra).Extra = extra.Select(j => j.Id).ToList();
            }
            else if (succeeded.Count >= 2)
                Add(TickerPromiseFindingKind.RanTwice,
                    $"The job succeeded {succeeded.Count} times, first {Ago(check.At - succeeded[0].CreatedAt)} ago.",
                    succeeded[^1], TickerPromiseFix.None);

            DuplicateFinding Add(TickerPromiseFindingKind kind, string reason, TickerPromiseJobIndex<TTimeTicker>.Entry job, TickerPromiseFix fix)
            {
                var finding = new TickerPromiseFinding
                {
                    Id = TickerPromiseIds.Create(function, definition.RowType.FullName, key, kind.ToString(), job.Id.ToString("N")),
                    Kind = kind,
                    Key = display,
                    // A done row isn't read, so its jobs name it: a fix keeps the row's label, the app its description.
                    Label = row?.Label ?? all.Select(j => ReadDescription(definition, j.Description))
                        .OrderBy(d => d.Source == TickerPromiseJobSource.App).Select(d => d.Label).FirstOrDefault(l => l != null) ?? display,
                    Reason = reason,
                    LastJob = new TickerPromiseLastJob { Id = job.Id, Status = job.Status },
                    Fix = fix,
                    DetectedAtUtc = check.At,
                };
                check.Findings.Add(finding);
                var duplicate = new DuplicateFinding();
                check.Duplicates[finding.Id] = duplicate;
                return duplicate;
            }
        }
    }

    private bool IsRunning(TickerPromiseJobIndex<TTimeTicker>.Entry job, DateTime now) => job.Status switch
    {
        TickerStatus.InProgress => true,
        TickerStatus.Queued when job.LockedAt >= now - FreshQueuedLock => true,
        TickerStatus.Idle or TickerStatus.Queued => (job.ExecutionTime ?? job.CreatedAt) > now - _options.NeverRanAfter,
        _ => false,
    };

    private TickerPromiseJobIndex<TTimeTicker> IndexFor(string function)
        => _indexes.GetOrAdd(function, f => new TickerPromiseJobIndex<TTimeTicker>(f,
            _promises.Select(p => p.Definition).Where(d => d.Function == f).ToArray(),
            () => _persistence.TimeTickersMonitoringQuery(), _persistence.SupportsPromiseDeltaReads,
            _persistence.SupportsPromiseChangeFeed
                ? new TickerPromiseJobIndex<TTimeTicker>.ChangeFeed(_persistence.ReadTimeTickersChangedSinceAsync, _persistence.ReadTimeTickerAggregatesAsync)
                : null,
            _options));

    /// <summary>The window's jobs by this promise's matchBy key; jobs without a readable key are left out.</summary>
    private static Dictionary<string, List<TickerPromiseJobIndex<TTimeTicker>.Entry>> GroupByKey(IReadOnlyList<TickerPromiseJobIndex<TTimeTicker>.Entry> jobs, int slot)
    {
        var byKey = new Dictionary<string, List<TickerPromiseJobIndex<TTimeTicker>.Entry>>(StringComparer.Ordinal);
        foreach (var job in jobs)
        {
            var key = job.Unreadable ? null : job.Keys[slot];
            if (key == null)
                continue;
            if (!byKey.TryGetValue(key, out var list))
                byKey[key] = list = new List<TickerPromiseJobIndex<TTimeTicker>.Entry>();
            list.Add(job);
        }
        return byKey;
    }

    // The index keeps no error text; findings read theirs now, a few rows at most.
    private async Task FillErrorsAsync(Check check, CancellationToken cancellationToken)
    {
        var byJob = check.Findings.Where(f => f.LastJob != null).GroupBy(f => f.LastJob.Id).ToDictionary(g => g.Key, g => g.ToList());
        if (byJob.Count == 0)
            return;
        foreach (var chunk in byJob.Keys.Chunk(500))
        {
            var rows = await DashboardMetadataQueryable<TTimeTicker>.Create(_persistence.TimeTickersMonitoringQuery(), ErrorColumns)
                .Where(TickerPromiseJobIndex<TTimeTicker>.IdIn(chunk)).ToArrayAsync(cancellationToken).ConfigureAwait(false);
            foreach (var row in rows)
                foreach (var finding in byJob[row.Id])
                    finding.LastJob.Error = ErrorMessage(row.ExceptionMessage);
        }
    }

    // Rows older than the shortest retention window may have lost their jobs to retention.
    private TimeSpan LookBack() => _retention.IsEnabled
        ? new[] { _retention.DeleteSucceededAfter, _retention.DeleteFailedAfter, _retention.DeleteCancelledAfter, _retention.DeleteSkippedAfter }
            .Where(w => w.HasValue).Min(w => w.Value)
        : _options.LookBack;

    private static TimeSpan Within(Promise promise, TickerPromiseState state)
        => state.Settings.WithinSeconds is { } seconds ? TimeSpan.FromSeconds(seconds) : promise.Definition.Within ?? TimeSpan.Zero;

    private static bool AutoFixEnabled(Promise promise, TickerPromiseState state)
        => state.Settings.AutoFix ?? promise.Definition.AutoFixMax.HasValue;

    /// <summary>Stores a check as the promise's result. Fixed findings leave the list and count as running.</summary>
    private static void Apply(TickerPromiseState state, Check check, ICollection<Guid> fixedIds = null)
    {
        var dismissals = state.Dismissals.GroupBy(d => d.FindingId).ToDictionary(g => g.Key, g => g.Last());
        var previous = state.Result?.Findings.ToDictionary(f => f.Id) ?? new Dictionary<Guid, TickerPromiseFinding>();
        var open = check.Findings.Where(f => fixedIds?.Contains(f.Id) != true).ToList();
        foreach (var finding in open)
        {
            finding.DetectedAtUtc = previous.TryGetValue(finding.Id, out var seen) ? seen.DetectedAtUtc : check.At;
            finding.Dismissed = dismissals.TryGetValue(finding.Id, out var d)
                ? new TickerPromiseDismissed { By = d.By, At = d.At, Reason = d.Reason }
                : null;
        }

        var counts = check.Counts;
        var dismissed = open.Count(f => f.Dismissed != null);
        var duplicates = open.Count(f => f.Dismissed == null && f.Kind is TickerPromiseFindingKind.RanTwice or TickerPromiseFindingKind.WillRunTwice);
        // A dismissal is kept while its finding recurs; only a complete check proves it did not.
        if (check.Error == null && !check.Truncated)
        {
            var current = check.Findings.Select(f => f.Id).ToHashSet();
            state.Dismissals.RemoveAll(d => d.At < check.At && !current.Contains(d.FindingId));
        }
        state.Result = new TickerPromiseResult
        {
            CheckedAtUtc = check.At,
            DurationMs = check.DurationMs,
            Error = check.Error,
            Truncated = check.Truncated,
            WindowStartUtc = check.WindowStart,
            Counts = new TickerPromiseCounts
            {
                Watched = counts.Watched,
                Done = counts.Done,
                // A fixed row's new or started job is running; a cancelled extra job is not.
                Running = counts.Running + check.Findings.Count(f => fixedIds?.Contains(f.Id) == true && !check.Duplicates.ContainsKey(f.Id)),
                Failed = counts.Failed,
                NeedsReview = open.Count - dismissed,
                Duplicates = duplicates,
                Dismissed = dismissed,
                Unmatchable = counts.Unmatchable,
            },
            Findings = open.Take(MaxStoredFindings).ToList(),
            AutoFixPaused = state.Result?.AutoFixPaused ?? false,
            AutoFixPausedReason = state.Result?.AutoFixPausedReason,
        };
    }

    /// <summary>The lease holder's events: changes since what it last published.</summary>
    private static List<TickerQEvent> Transitions(Promise promise, TickerPromiseState state, Check check, AutoFixOutcome autoFix)
    {
        var events = new List<TickerQEvent>();
        var last = state.Published ?? new TickerPromisePublished();
        // A failed check proves nothing about the rows, so it neither raises nor clears "needs review".
        var needsReview = check.Error == null ? state.Result.Counts.NeedsReview : last.NeedsReview;
        var name = promise.Definition.Name;

        if (needsReview > last.NeedsReview)
            events.Add(Event(promise, check, last, "promise.needs_review", TickerQEventSeverity.Warning,
                $"{name}: {Rows(needsReview)} {(needsReview == 1 ? "needs" : "need")} review", null, needsReview));
        else if (needsReview == 0 && last.NeedsReview > 0)
            events.Add(Event(promise, check, last, "promise.recovered", TickerQEventSeverity.Info,
                $"{name}: every row is on track again", null, needsReview));
        if (check.Error != null && last.Error == null)
            events.Add(Event(promise, check, last, "promise.check_failed", TickerQEventSeverity.Error,
                $"{name}: the check failed", check.Error, needsReview));
        if (autoFix.Fixed.Count > 0)
            events.Add(Event(promise, check, last, "promise.auto_fixed", TickerQEventSeverity.Info,
                $"{name}: fixed {Rows(autoFix.Fixed.Count)} automatically", null, needsReview, autoFix.Fixed.Count));
        if (autoFix.PausedReason != null)
            events.Add(Event(promise, check, last, "promise.auto_fix_paused", TickerQEventSeverity.Warning,
                $"{name}: automatic fixing paused", autoFix.PausedReason, needsReview));

        state.Published = new TickerPromisePublished { NeedsReview = needsReview, Error = check.Error, AtUtc = check.At };
        return events;
    }

    private static TickerQEvent Event(Promise promise, Check check, TickerPromisePublished last, string kind, TickerQEventSeverity severity,
        string title, string detail, int needsReview, int fixedCount = 0)
    {
        // The same transition from the same published state has the same id on every node.
        var id = TickerPromiseIds.Create("event", kind, promise.Id, last.NeedsReview.ToString(CultureInfo.InvariantCulture),
            needsReview.ToString(CultureInfo.InvariantCulture), last.AtUtc.Ticks.ToString(CultureInfo.InvariantCulture),
            fixedCount.ToString(CultureInfo.InvariantCulture));
        var data = new Dictionary<string, string>
        {
            ["promiseId"] = promise.Id,
            ["promise"] = promise.Definition.Name,
            ["function"] = promise.Definition.Function,
            ["needsReview"] = needsReview.ToString(CultureInfo.InvariantCulture),
        };
        if (fixedCount > 0)
            data["fixed"] = fixedCount.ToString(CultureInfo.InvariantCulture);
        return new TickerQEvent(id.ToString("N"), kind, promise.Definition.Name, title, detail, severity,
            $"promises/{promise.Id}", check.At, data);
    }

    private TickerPromiseSnapshot Snapshot(Promise promise, TickerPromiseState state)
    {
        var definition = promise.Definition;
        var result = state.Result;
        return new TickerPromiseSnapshot
        {
            Id = promise.Id,
            Name = definition.Name,
            Function = definition.Function,
            RowType = definition.RowType.Name,
            Summary = definition.Summary(Within(promise, state)),
            ChecksDuplicates = !definition.AllowsRepeats,
            DoneWhen = definition.DoneCondition,
            Rule = new TickerPromiseRule
            {
                DeclaredOn = definition.DeclaredOn,
                RowType = definition.RowType.Name,
                RowsFrom = definition.ContextType?.Name,
                FunctionClass = definition.FunctionType?.Name,
                RequestType = definition.RequestType.Name,
                MatchBy = definition.MatchByParts.ToList(),
                WithinSeconds = (int)(definition.Within ?? TimeSpan.Zero).TotalSeconds,
                After = definition.AfterName,
                DoneWhen = definition.DoneCondition,
                HasLabel = definition.HasLabel,
                NamedInCode = definition.NamedInCode,
                AutoFixMax = definition.AutoFixMax,
                AllowRepeats = definition.AllowsRepeats,
                Checks = new TickerPromiseCheckOptions
                {
                    CheckIntervalSeconds = (int)_options.CheckInterval.TotalSeconds,
                    NeverRanAfterSeconds = (int)_options.NeverRanAfter.TotalSeconds,
                    MaxRowsPerCheck = _options.MaxRowsPerCheck,
                    MaxJobsPerCheck = _options.MaxJobsPerCheck,
                    LookBackDays = LookBack().TotalDays,
                    LookBackFromRetention = _retention.IsEnabled,
                },
            },
            CreatedAtUtc = state.CreatedAtUtc,
            AutoFix = new TickerPromiseAutoFixView
            {
                Enabled = AutoFixEnabled(promise, state),
                FromCode = definition.AutoFixMax.HasValue,
                MaxPerCheck = definition.AutoFixMax ?? DefaultAutoFixMax,
                Paused = result?.AutoFixPaused ?? false,
                PausedReason = result?.AutoFixPausedReason,
            },
            Settings = new TickerPromiseSettingsView
            {
                Paused = state.Settings.Paused,
                WithinSeconds = state.Settings.WithinSeconds,
                WithinSecondsFromCode = (int)(definition.Within ?? TimeSpan.Zero).TotalSeconds,
            },
            CheckedAtUtc = result?.CheckedAtUtc,
            // A never-checked promise is due at once; the background pass picks it up on its next round.
            NextCheckAtUtc = state.Settings.Paused ? null : NextCheck(result?.CheckedAtUtc, state.Settings.EnabledAtUtc),
            CheckIntervalSeconds = (int)_options.CheckInterval.TotalSeconds,
            DurationMs = result?.DurationMs,
            Error = result?.Error,
            Truncated = result?.Truncated ?? false,
            WindowStartUtc = result?.WindowStartUtc,
            Counts = result?.Counts ?? new TickerPromiseCounts(),
            Findings = result?.Findings ?? new List<TickerPromiseFinding>(),
            Activity = Enumerable.Reverse(state.Activity).ToList(),
        };
    }

    // From the last check, or from being enabled again when that came later, since a disabled promise wasn't checked.
    private DateTime NextCheck(DateTime? checkedAt, DateTime? enabledAt)
    {
        var from = checkedAt ?? _clock.UtcNow;
        return (enabledAt > from ? enabledAt.Value : from) + _options.CheckInterval;
    }

    private Promise Find(string id)
        => _promises.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase))
           ?? throw new TickerPromiseException(404, "No promise has this id.");

    private async Task<TickerPromiseState> ReadAsync(Promise promise, CancellationToken cancellationToken)
    {
        var row = await State.GetAsync(promise.RowId, cancellationToken).ConfigureAwait(false);
        return row == null ? new TickerPromiseState() : Read(row);
    }

    /// <summary>
    /// Applies <paramref name="change"/> to the stored state and saves it, retrying on a concurrent write. Returns the
    /// saved state, or null when the change declined. The change may run more than once, each time on a fresh copy.
    /// </summary>
    private async Task<TickerPromiseState> UpdateAsync(Promise promise, Func<TickerPromiseState, bool> change, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < MaxUpdateAttempts; attempt++)
        {
            var row = await State.GetAsync(promise.RowId, cancellationToken).ConfigureAwait(false);
            var state = row == null ? new TickerPromiseState() : Read(row);
            if (!change(state))
                return null;
            state.CreatedAtUtc ??= row == null ? _clock.UtcNow : row.CreatedAtUtc;
            var json = Serialize(state);
            if (State.MaxLength is { } max && json.Length > max)
                json = Shorten(promise, state, max);
            var name = promise.Definition.Name.Length > 200 ? promise.Definition.Name[..200] : promise.Definition.Name;

            if (row != null)
            {
                if (await State.UpdateAsync(row, name, json, cancellationToken).ConfigureAwait(false))
                    return state;
                continue;
            }

            try
            {
                await State.CreateAsync(promise.RowId, name, json, _clock.UtcNow, cancellationToken).ConfigureAwait(false);
                return state;
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                // Another node created the row first; change that one instead.
                if (await State.GetAsync(promise.RowId, cancellationToken).ConfigureAwait(false) == null)
                    throw;
            }
        }
        throw new TickerPromiseException(409, "The promise kept changing while it was being saved. Try again.");
    }

    private static string Serialize(TickerPromiseState state)
        => JsonSerializer.Serialize(state, TickerPromiseJsonContext.Default.TickerPromiseState);

    /// <summary>
    /// Fits the state into a store whose column is bounded, such as Oracle's NVARCHAR2(2000). Findings are found again
    /// by the next check, while a dropped activity entry or dismissal is gone for good, so it gives up the oldest
    /// findings first (down to ten), then the oldest activity (down to five), then the rest of the findings, the rest
    /// of the activity, and dismissals last. Settings, the lease and the counts always stay, so the dashboard still
    /// shows how many rows need review.
    /// </summary>
    private string Shorten(Promise promise, TickerPromiseState state, int max)
    {
        var findings = state.Result?.Findings ?? new List<TickerPromiseFinding>();
        var fits = KeepFitting(state, findings, 10, newestLast: false, max)
            || KeepFitting(state, state.Activity, 5, newestLast: true, max)
            || KeepFitting(state, findings, 0, newestLast: false, max)
            || KeepFitting(state, state.Activity, 0, newestLast: true, max)
            || KeepFitting(state, state.Dismissals, 0, newestLast: true, max);
        if (!fits)
            throw new TickerPromiseException(507,
                $"The promise's state does not fit the {max}-character definitions column, even without findings.");
        _logger.LogDebug("TickerQ promise '{Promise}' state was shortened to fit {Max} characters", promise.Definition.Name, max);
        return Serialize(state);
    }

    /// <summary>
    /// Keeps as many of <paramref name="list"/>'s newest items as fit, but at least <paramref name="floor"/>.
    /// Returns whether the state fits; a list already at its floor is left as it is.
    /// </summary>
    private static bool KeepFitting<T>(TickerPromiseState state, List<T> list, int floor, bool newestLast, int max)
    {
        if (list.Count <= floor)
            return false;
        var all = list.ToArray();
        bool Fits(int count)
        {
            list.Clear();
            list.AddRange(newestLast ? all[^count..] : all[..count]);
            return Serialize(state).Length <= max;
        }

        if (!Fits(floor))
            return false;
        int low = floor, high = all.Length - 1;
        while (low < high)
        {
            var middle = (low + high + 1) / 2;
            if (Fits(middle)) low = middle;
            else high = middle - 1;
        }
        Fits(low);
        return true;
    }

    private TickerPromiseState Read(StateRow row)
    {
        try
        {
            var state = JsonSerializer.Deserialize(row.Json, TickerPromiseJsonContext.Default.TickerPromiseState) ?? new TickerPromiseState();
            state.CreatedAtUtc ??= row.CreatedAtUtc;
            return state;
        }
        catch (JsonException ex)
        {
            // Only TickerQ writes this row, so starting over loses little: dismissals and settings.
            _logger.LogWarning(ex, "TickerQ promise state '{Promise}' could not be read and starts over", row.Name);
            return new TickerPromiseState();
        }
    }

    private void AddActivity(TickerPromiseState state, string kind, string text)
    {
        state.Activity.Add(new TickerPromiseActivity { AtUtc = _clock.UtcNow, Kind = kind, Text = text });
        if (state.Activity.Count > MaxActivity)
            state.Activity.RemoveRange(0, state.Activity.Count - MaxActivity);
    }

    private static string Who(string user) => string.IsNullOrWhiteSpace(user) ? "Someone" : user.Trim();

    private static string Rows(int count) => count == 1 ? "1 row" : $"{count} rows";

    private static string Ago(TimeSpan span) => span.TotalDays >= 1 ? $"{(int)span.TotalDays} d"
        : span.TotalHours >= 1 ? $"{(int)span.TotalHours} h"
        : $"{Math.Max(1, (int)span.TotalMinutes)} min";

    // Failed jobs store their exception as JSON; findings show only its message.
    private static string ErrorMessage(string stored)
    {
        if (string.IsNullOrWhiteSpace(stored) || stored[0] != '{')
            return stored;
        try
        {
            using var document = JsonDocument.Parse(stored);
            return document.RootElement.TryGetProperty("Message", out var message) && message.ValueKind == JsonValueKind.String
                ? message.GetString()
                : stored;
        }
        catch (JsonException)
        {
            return stored;
        }
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "TickerPromiseService preserves public constructors and properties on TTimeTicker; the expression binds only those preserved members.")]
    private static Expression<Func<TTimeTicker, TTimeTicker>> CreateErrorColumns() => e => new TTimeTicker
    {
        Id = e.Id, ExceptionMessage = e.ExceptionMessage,
    };

    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "TickerPromiseService preserves public constructors and properties on TTimeTicker; the expression binds only those preserved members.")]
    private static Expression<Func<TTimeTicker, TTimeTicker>> CreateListedJobColumns() => e => new TTimeTicker
    {
        Id = e.Id, Status = e.Status, Description = e.Description, CreatedAt = e.CreatedAt, ExecutionTime = e.ExecutionTime,
        ExecutedAt = e.ExecutedAt, ElapsedTime = e.ElapsedTime, RetryCount = e.RetryCount, Retries = e.Retries,
        Request = e.Request, ExceptionMessage = e.ExceptionMessage, SkippedReason = e.SkippedReason,
    };

    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "TickerPromiseService preserves public constructors and properties on TTimeTicker; the expression binds only those preserved members.")]
    private static Expression<Func<TTimeTicker, TTimeTicker>> CreateAnalyticsColumns() => e => new TTimeTicker
    {
        Status = e.Status, Description = e.Description, CreatedAt = e.CreatedAt, ExecutionTime = e.ExecutionTime,
        ExecutedAt = e.ExecutedAt, ElapsedTime = e.ElapsedTime,
    };

    /// <summary>A promise's stored row, from whichever store keeps it.</summary>
    private sealed record StateRow(Guid Id, string Name, string Json, long Version, DateTime CreatedAtUtc);

    /// <summary>
    /// Where promise state lives: the promise table when the provider has one, else the definitions store as
    /// <see cref="SavedTickerDefinitionKind.Promise"/> rows, whose column may be bounded.
    /// </summary>
    private sealed class StateStore
    {
        private readonly ITickerPromiseStateStore _table;
        private readonly ITickerDefinitionStore _definitions;

        private StateStore(ITickerPromiseStateStore table, ITickerDefinitionStore definitions)
        {
            _table = table;
            _definitions = definitions;
        }

        public static StateStore For(object persistence) => persistence switch
        {
            ITickerPromiseStateStore { HasPromiseStateTable: true } table => new StateStore(table, null),
            ITickerDefinitionStore definitions => new StateStore(null, definitions),
            _ => null,
        };

        /// <summary>The most characters a row's state may hold, or null when unbounded.</summary>
        public int? MaxLength => _table != null ? null : _definitions.MaxDefinitionJsonLength;

        public async Task<Dictionary<Guid, StateRow>> ListAsync(CancellationToken cancellationToken)
        {
            if (_table != null)
                return (await _table.ListPromiseStatesAsync(cancellationToken).ConfigureAwait(false))
                    .Select(r => new StateRow(r.Id, r.Name, r.StateJson, r.Version, Utc(r.CreatedAtUtc))).ToDictionary(r => r.Id);
            return (await _definitions.ListDefinitionsAsync(SavedTickerDefinitionKind.Promise, null, cancellationToken).ConfigureAwait(false))
                .Select(r => new StateRow(r.Id, r.Name, r.DefinitionJson, r.Version, Utc(r.CreatedAtUtc))).ToDictionary(r => r.Id);
        }

        public async Task<StateRow> GetAsync(Guid id, CancellationToken cancellationToken)
        {
            if (_table != null)
            {
                var row = await _table.GetPromiseStateAsync(id, cancellationToken).ConfigureAwait(false);
                return row == null ? null : new StateRow(row.Id, row.Name, row.StateJson, row.Version, Utc(row.CreatedAtUtc));
            }
            var definition = await _definitions.GetDefinitionAsync(id, cancellationToken).ConfigureAwait(false);
            return definition == null ? null
                : new StateRow(definition.Id, definition.Name, definition.DefinitionJson, definition.Version, Utc(definition.CreatedAtUtc));
        }

        public Task CreateAsync(Guid id, string name, string json, DateTime now, CancellationToken cancellationToken)
            => _table != null
                ? _table.CreatePromiseStateAsync(new TickerPromiseStateRow { Id = id, Name = name, StateJson = json, CreatedAtUtc = now, UpdatedAtUtc = now },
                    cancellationToken)
                : _definitions.CreateDefinitionAsync(new SavedTickerDefinition
                {
                    Id = id,
                    Kind = SavedTickerDefinitionKind.Promise,
                    Target = SavedTickerDefinitionTarget.TimeTicker,
                    Name = name,
                    DefinitionJson = json,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                }, cancellationToken);

        /// <summary>Writes the row at its version; false when another writer got there first.</summary>
        public async Task<bool> UpdateAsync(StateRow row, string name, string json, CancellationToken cancellationToken)
        {
            if (_table != null)
                return await _table.UpdatePromiseStateAsync(new TickerPromiseStateRow { Id = row.Id, Name = name, StateJson = json },
                    row.Version, cancellationToken).ConfigureAwait(false) != null;
            var definition = await _definitions.GetDefinitionAsync(row.Id, cancellationToken).ConfigureAwait(false);
            if (definition == null || definition.Version != row.Version)
                return false;
            var updated = definition.Copy();
            updated.Name = name;
            updated.DefinitionJson = json;
            return await _definitions.UpdateDefinitionAsync(updated, row.Version, cancellationToken).ConfigureAwait(false) != null;
        }

        // Some stores hand dates back without their kind; both stores keep UTC.
        private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
    }

    private sealed record Promise(TickerPromiseDefinition Definition, Guid RowId)
    {
        public string Id { get; } = RowId.ToString("N");
    }

    /// <summary>One check's outcome, before it is stored.</summary>
    private sealed class Check
    {
        public DateTime At { get; init; }
        public DateTime WindowStart { get; set; }
        public long DurationMs { get; set; }
        public string Error { get; set; }
        public bool Truncated { get; set; }
        public TickerPromiseCounts Counts { get; } = new();
        /// <summary>Every finding, newest row first.</summary>
        public List<TickerPromiseFinding> Findings { get; } = new();
        /// <summary>The row behind each missed-work finding, for its fix.</summary>
        public Dictionary<Guid, TickerPromiseRow> Rows { get; } = new();
        /// <summary>The duplicate findings, with the jobs their fix cancels.</summary>
        public Dictionary<Guid, DuplicateFinding> Duplicates { get; } = new();
    }

    private sealed class DuplicateFinding
    {
        public List<Guid> Extra { get; set; } = new();
    }

    private sealed class AutoFixOutcome
    {
        public List<Guid> Fixed { get; } = new();
        public string PausedReason { get; set; }
    }
}
