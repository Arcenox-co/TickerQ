using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TickerQ.Integrations.Infrastructure;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Integrations;

namespace TickerQ.Integrations.Monitoring;

internal sealed record MonitorDecision(bool? Breached, string Message, Guid TickerId = default)
{
    public long? ObservedDurationSeconds { get; init; }
    public int? FailureCount { get; init; }
    public DateTime? DeadlineUtc { get; init; }
}
internal static class MonitorEvaluation
{
    internal static string Fingerprint(IntegrationMonitor rule) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(rule, IntegrationJsonContext.Default.IntegrationMonitor))));

    internal static void Validate(IntegrationMonitor rule, IntegrationConfiguration config)
    {
        if (rule.Id == Guid.Empty || string.IsNullOrWhiteSpace(rule.Name) || rule.Name.Length > 80)
            throw new ArgumentException("Each monitor needs an ID and a name of at most 80 characters.");
        if (string.IsNullOrWhiteSpace(rule.Function) || rule.Function.Length > 256 || rule.Function.IndexOfAny(['*', '?']) >= 0)
            throw new ArgumentException("Choose one exact function name for each monitor.");
        if (rule.Kind is not ("deadline" or "long_running" or "consecutive_failures")) throw new ArgumentException("Choose a supported monitoring condition.");
        if (rule.DestinationIds == null || rule.DestinationIds.Count is 0 or > 32 || rule.DestinationIds.Distinct().Count() != rule.DestinationIds.Count || rule.DestinationIds.Any(id => !config.Destinations.Any(d => d.Id == id)))
            throw new ArgumentException("Each monitor must reference existing, distinct integrations.");
        if (rule.GraceMinutes is < 0 or > 720 || rule.DurationSeconds is < 5 or > 604800 || rule.FailureThreshold is < 2 or > 20 || rule.LookbackHours is < 1 or > 168)
            throw new ArgumentException("Use a grace period of 0–720 minutes, duration of 5–604800 seconds, failure threshold of 2–20, and lookback of 1–168 hours.");
        if (!TimeOnly.TryParseExact(rule.DeadlineLocalTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            throw new ArgumentException("Use HH:mm for the local deadline.");
        try { TimeZoneInfo.FindSystemTimeZoneById(rule.TimeZoneId); }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException)
        { throw new ArgumentException("Choose a time zone supported by this host."); }
    }

    internal static void Process(IntegrationMonitor rule, MonitorState state, MonitoringObservation observation,
        Action<MonitorDecision, DateTime> emit)
    {
        if (observation.Error != null) { emit(new(null, observation.Error), observation.AtUtc); return; }
        var through = observation.ThroughUtc ?? observation.AtUtc;
        if (state.ProcessedThroughUtc > through) return;
        state.ReplayFromUtc ??= state.View.LastCheckedUtc ?? state.InitializedUtc;
        var previous = state.ProcessedThroughUtc;
        var replayFloor = (previous ?? through) - MonitoringReplayPolicy.Window;
        // Upgrade older persisted monitors without replaying notifications they already
        // sent. Fresh monitors start recording identities on their very first scan.
        if (!state.ReplayTrackingInitialized && previous.HasValue)
        {
            state.SeenCompletions.AddRange(observation.Replayed);
            state.LastStreakBreakUtc = observation.Replayed.Where(x => x.Status != TickerStatus.Failed)
                .Select(x => x.CompletedUtc).Append(state.LastSuccessUtc).Max();
        }
        state.ReplayTrackingInitialized = true;
        var seen = state.SeenCompletions.Select(CompletionKey).ToHashSet();
        var events = observation.Recent.AsEnumerable();
        // Allows sources to supply a known success independently of their terminal rows.
        if (observation.LastSuccessUtc is { } success && !events.Any(x => IsSuccess(x.Status) && x.CompletedUtc == success))
            events = events.Append(new(Guid.Empty, TickerStatus.Done, success));
        var ordered = events.Where(x => x.CompletedUtc.HasValue && x.CompletedUtc <= through
                && (!previous.HasValue || x.CompletedUtc > replayFloor))
            .Concat(observation.Replayed.Where(x => x.CompletedUtc > replayFloor && x.CompletedUtc <= through))
            .Where(x => seen.Add(CompletionKey(x)))
            .OrderBy(x => x.CompletedUtc).ThenBy(x => x.Id).ToArray();
        state.SeenCompletions.RemoveAll(x => x.CompletedUtc <= replayFloor);
        // A full replay window plus one bounded forward page. Refuse progress rather
        // than silently evict identities that are still needed to suppress duplicates.
        if (state.SeenCompletions.Count + ordered.Count(x => x.CompletedUtc > replayFloor) > MonitoringReplayPolicy.MaxIdentities)
        {
            emit(new(null, "Completion replay identity capacity reached; monitoring cannot safely advance."), observation.AtUtc);
            return;
        }
        state.SeenCompletions.AddRange(ordered.Where(x => x.CompletedUtc > replayFloor));

        if (rule.Kind == "consecutive_failures" && previous.HasValue && ordered.Any(x => x.CompletedUtc <= previous))
        {
            // Late rows must be merged with the history already observed at the checkpoint.
            // Adding them to the tail of the existing streak invents an ordering (and can
            // alert on three failures separated by a success discovered in this same batch).
            var oldFailures = state.ConsecutiveFailures.Where(at => at <= replayFloor).ToArray();
            var throughPrevious = state.SeenCompletions.Where(x => x.CompletedUtc <= previous).ToArray();
            state.LastStreakBreakUtc = throughPrevious.Where(x => x.Status != TickerStatus.Failed)
                .Select(x => x.CompletedUtc).Append(state.LastStreakBreakUtc).Max();
            var failures = oldFailures.Concat(throughPrevious.Where(x => x.Status == TickerStatus.Failed)
                    .Select(x => x.CompletedUtc!.Value))
                .Where(at => at >= previous.Value.AddHours(-rule.LookbackHours) &&
                    (state.LastStreakBreakUtc == null || at > state.LastStreakBreakUtc))
                .OrderBy(at => at).TakeLast(rule.FailureThreshold).ToArray();
            state.ConsecutiveFailures.Clear();
            state.ConsecutiveFailures.AddRange(failures);
            if (failures.Length >= rule.FailureThreshold && failures[^1] > state.ReplayFromUtc)
                emit(FailureDecision(rule, state, ordered.Last(x => x.CompletedUtc <= previous).Id), failures[^1]);
        }

        foreach (var group in ordered.GroupBy(x => x.CompletedUtc!.Value))
        {
            var at = group.Key;
            var replay = at > state.ReplayFromUtc;
            if (rule.Kind == "deadline" && replay) CheckDeadlines(rule, state, at, inclusive: false, emit);
            // No portable ordering exists within one completion timestamp. A non-failure
            // conservatively breaks the streak, rather than inventing a failure ordering.
            var breaksStreak = group.Any(x => x.Status != TickerStatus.Failed);
            if (breaksStreak)
            {
                if (state.LastStreakBreakUtc == null || at > state.LastStreakBreakUtc) state.LastStreakBreakUtc = at;
                state.ConsecutiveFailures.RemoveAll(x => x <= at);
            }
            foreach (var execution in group)
            {
                var observedCompletion = state.RunningSince.TryGetValue(execution.Id, out var observed)
                    && (execution.Generation == null || observed.Generation == execution.Generation);
                if (rule.Kind == "long_running" && replay)
                {
                    var duration = Math.Max(0, execution.ElapsedMilliseconds);
                    if (duration == 0 && observedCompletion)
                        duration = Math.Max(0, (long)(at - observed!.SinceUtc).TotalMilliseconds);
                    if (duration >= rule.DurationSeconds * 1000L)
                        emit(new(true, $"An execution finished after {duration / 1000} seconds, exceeding the {rule.DurationSeconds}-second threshold.", execution.Id)
                        { ObservedDurationSeconds = duration / 1000 }, at);
                }
                if (observedCompletion) state.RunningSince.Remove(execution.Id);
                if (IsSuccess(execution.Status))
                {
                    if (state.LastSuccessUtc == null || at > state.LastSuccessUtc) state.LastSuccessUtc = at;
                    if (replay && state.IncidentId != null
                        && (rule.Kind != "deadline" || IsRelevantDeadlineSuccess(rule, state, at))
                        && (rule.Kind != "long_running" || observedCompletion || at >= state.View.OpenedAtUtc)
                        && (rule.Kind != "consecutive_failures" || state.ConsecutiveFailures.Count(x => x > at) < rule.FailureThreshold)
                        && (rule.Kind != "long_running" || !HasOverdueRun(rule, state,
                            previous.HasValue && at <= previous ? observation.AtUtc : at)))
                        emit(new(false, "A successful execution was recorded and the monitored condition is clear.", execution.Id)
                        { FailureCount = rule.Kind == "consecutive_failures" ? state.ConsecutiveFailures.Count : null, DeadlineUtc = state.LastDeadlineUtc },
                            previous.HasValue && at <= previous ? observation.AtUtc : at);
                }
            }
            if (rule.Kind == "consecutive_failures" && (!previous.HasValue || at > previous))
            {
                state.ConsecutiveFailures.RemoveAll(x => x < at.AddHours(-rule.LookbackHours));
                if (!breaksStreak && (state.LastStreakBreakUtc == null || at > state.LastStreakBreakUtc))
                {
                    state.ConsecutiveFailures.AddRange(group.Select(_ => at));
                    state.ConsecutiveFailures.Sort();
                    if (state.ConsecutiveFailures.Count > rule.FailureThreshold)
                        state.ConsecutiveFailures.RemoveRange(0, state.ConsecutiveFailures.Count - rule.FailureThreshold);
                    if (replay && state.ConsecutiveFailures.Count >= rule.FailureThreshold)
                        emit(FailureDecision(rule, state, group.First().Id), at);
                }
            }
            if (rule.Kind == "deadline" && replay) CheckDeadlines(rule, state, at, inclusive: true, emit);
        }

        if (rule.Kind == "deadline") CheckDeadlines(rule, state, through, inclusive: true, emit);
        state.ProcessedThroughUtc = through;
        if (observation.HasMore)
        {
            emit(new(null, "Catching up on retained execution history."), observation.AtUtc);
            return;
        }
        var now = observation.AtUtc;
        if (rule.Kind == "consecutive_failures")
        {
            state.ConsecutiveFailures.RemoveAll(x => x < now.AddHours(-rule.LookbackHours));
            if (state.ConsecutiveFailures.Count >= rule.FailureThreshold)
                emit(FailureDecision(rule, state, ordered.LastOrDefault()?.Id ?? Guid.Empty), now);
            else if (state.IncidentId != null) emit(new(null, "Waiting for a successful execution to confirm recovery."), now);
            else if (state.ConsecutiveFailures.Count == 0 && state.LastSuccessUtc == null && ordered.Length == 0)
                emit(new(null, "No retained terminal executions in the selected lookback."), now);
            else emit(new(false, $"{state.ConsecutiveFailures.Count} consecutive final failures; threshold is {rule.FailureThreshold}.")
                { FailureCount = state.ConsecutiveFailures.Count }, now);
            return;
        }
        if (rule.Kind == "deadline")
        {
            if (state.LastDeadlineUtc == null) emit(new(null, "Waiting for the next daily deadline."), now);
            return;
        }
        // Active timing is conservative; completed rows additionally supply persisted
        // execution duration, including runs that started and finished between scans.
        var active = observation.Running.ToDictionary(x => x.Id);
        foreach (var id in state.RunningSince.Keys.Where(id => !active.ContainsKey(id)).ToArray()) state.RunningSince.Remove(id);
        foreach (var (id, run) in active)
            if (!state.RunningSince.TryGetValue(id, out var observedRun) || observedRun.Generation != run.Generation)
                state.RunningSince[id] = new() { SinceUtc = now, Generation = run.Generation };
        var overdue = state.RunningSince.Where(x => now - x.Value.SinceUtc >= TimeSpan.FromSeconds(rule.DurationSeconds)).OrderBy(x => x.Value.SinceUtc).FirstOrDefault();
        if (overdue.Key != Guid.Empty) emit(new(true, $"An execution has remained InProgress for at least {rule.DurationSeconds} seconds since first observed, including retry waits.", overdue.Key)
            { ObservedDurationSeconds = (long)(now - overdue.Value.SinceUtc).TotalSeconds }, now);
        else if (state.IncidentId != null && !(state.LastSuccessUtc >= state.View.OpenedAtUtc))
            emit(new(null, "Waiting for a successful execution to confirm recovery."), now);
        else emit(new(false, "No execution has exceeded the monitored duration."), now);
    }

    private static bool IsSuccess(TickerStatus status) => status is TickerStatus.Done or TickerStatus.DueDone;

    private static bool IsRelevantDeadlineSuccess(IntegrationMonitor rule, MonitorState state, DateTime at)
    {
        if (state.LastDeadlineUtc is not { } deadline) return true;
        var zone = TimeZoneInfo.FindSystemTimeZoneById(rule.TimeZoneId);
        var dayStart = ToUtc(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(deadline, DateTimeKind.Utc), zone).Date, zone);
        return at >= dayStart;
    }

    private static (Guid, bool, Guid?, DateTime?, TickerStatus) CompletionKey(MonitorExecution execution)
        => (execution.Id, execution.IsCron, execution.Generation, execution.CompletedUtc, execution.Status);

    private static bool HasOverdueRun(IntegrationMonitor rule, MonitorState state, DateTime at)
        => state.RunningSince.Values.Any(x => at - x.SinceUtc >= TimeSpan.FromSeconds(rule.DurationSeconds));

    private static MonitorDecision FailureDecision(IntegrationMonitor rule, MonitorState state, Guid id)
        => new(true, $"At least {state.ConsecutiveFailures.Count} consecutive final failures in the last {rule.LookbackHours} hours.", id)
        { FailureCount = state.ConsecutiveFailures.Count };

    private static void CheckDeadlines(IntegrationMonitor rule, MonitorState state, DateTime until, bool inclusive,
        Action<MonitorDecision, DateTime> emit)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(rule.TimeZoneId);
        var time = TimeOnly.ParseExact(rule.DeadlineLocalTime, "HH:mm", CultureInfo.InvariantCulture);
        var from = state.LastDeadlineUtc ?? state.ReplayFromUtc ?? state.InitializedUtc;
        // Start one local day earlier because a grace period can cross midnight.
        var date = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(from, DateTimeKind.Utc), zone).Date.AddDays(-1);
        for (; ; date = date.AddDays(1))
        {
            var deadline = ToUtc(date + time.ToTimeSpan(), zone);
            var cutoff = deadline.AddMinutes(rule.GraceMinutes);
            if (cutoff > until || !inclusive && cutoff == until) return;
            if (cutoff < state.ReplayFromUtc || deadline <= state.LastDeadlineUtc) continue;
            var start = ToUtc(date, zone);
            var met = state.LastSuccessUtc >= start && state.LastSuccessUtc <= cutoff;
            emit(new(!met, met ? $"A successful execution was recorded before the daily cutoff ({rule.TimeZoneId})."
                : $"No successful execution since local midnight by {rule.DeadlineLocalTime} {rule.TimeZoneId}, including {rule.GraceMinutes} minutes of grace.")
                { DeadlineUtc = deadline }, cutoff);
            state.LastDeadlineUtc = deadline;
        }
    }

    internal static DateTime ToUtc(DateTime local, TimeZoneInfo zone)
    {
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        // Spring gaps use the first valid minute. Fall-back deadlines use the later occurrence.
        while (zone.IsInvalidTime(local)) local = local.AddMinutes(1);
        if (zone.IsAmbiguousTime(local)) return zone.GetAmbiguousTimeOffsets(local).Select(offset => new DateTimeOffset(local, offset).UtcDateTime).Max();
        return TimeZoneInfo.ConvertTimeToUtc(local, zone);
    }
}
