using System;
using System.Collections.Generic;
using System.Linq;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ.Utilities.Infrastructure;

/// <summary>Pure policy evaluation shared by manager validation, scheduler polling and dashboard preview.</summary>
public sealed class SchedulePolicyEvaluator
{
    private readonly IReadOnlyDictionary<string, IScheduleDateProvider> _dateProviders;

    public SchedulePolicyEvaluator(IEnumerable<IScheduleDateProvider> dateProviders = null)
    {
        var registered = new Dictionary<string, IScheduleDateProvider>(StringComparer.Ordinal);
        foreach (var provider in dateProviders ?? Array.Empty<IScheduleDateProvider>())
        {
            var key = SchedulePolicyNames.Normalize(provider.Name);
            if (key.Length == 0)
                throw new InvalidOperationException("A schedule date provider must have a non-empty name.");
            if (!registered.TryAdd(key, provider))
                throw new InvalidOperationException(
                    $"More than one schedule date provider is registered as '{provider.Name}'.");
        }
        _dateProviders = registered;
    }

    public ScheduleDecision Evaluate(string policyName, DateTime requestedAtUtc,
        IReadOnlyCollection<SchedulePolicy> policies)
    {
        var normalized = SchedulePolicyNames.Normalize(policyName);
        var map = BuildMap(policies);
        if (!map.TryGetValue(normalized, out var policy))
            return Blocked(requestedAtUtc, "MissingPolicy", $"Schedule policy '{policyName}' does not exist.");
        return EvaluateCore(policy, EnsureUtc(requestedAtUtc), map, new HashSet<string>(StringComparer.Ordinal));
    }

    public ScheduleDecision FindNextAllowed(string policyName, DateTime requestedAtUtc,
        IReadOnlyCollection<SchedulePolicy> policies, int maximumSearchDays = 3660)
    {
        var utc = EnsureUtc(requestedAtUtc);
        var first = Evaluate(policyName, utc, policies);
        if (first.IsAllowed) return first;

        var map = BuildMap(policies);
        if (!map.TryGetValue(SchedulePolicyNames.Normalize(policyName), out var root)) return first;
        var participatingPolicies = CollectParticipatingPolicies(root, map);

        for (var offset = 0; offset <= maximumSearchDays; offset++)
        {
            var utcDayStart = utc.Date.AddDays(offset);
            var utcDayEnd = utcDayStart.AddDays(1);
            var candidates = new SortedSet<DateTime>();

            foreach (var policy in participatingPolicies)
            {
                var zone = ResolveTimeZone(policy.TimeZoneId);
                var localMiddle = TimeZoneInfo.ConvertTimeFromUtc(utcDayStart.AddHours(12), zone);
                var localDate = DateOnly.FromDateTime(localMiddle);
                var transitionTimes = new HashSet<TimeSpan> { TimeSpan.Zero };
                foreach (var window in policy.AllowedTimeWindows ?? Enumerable.Empty<ScheduleTimeWindow>())
                {
                    transitionTimes.Add(window.Start);
                    transitionTimes.Add(window.End);
                }

                // The local day overlapping a UTC day can be either side of the day at
                // extreme offsets. Include adjacent local dates, then retain only UTC
                // instants inside the UTC day currently being searched.
                for (var localDateOffset = -1; localDateOffset <= 1; localDateOffset++)
                {
                    var date = localDate.AddDays(localDateOffset);
                    foreach (var time in transitionTimes)
                    {
                        var local = date.ToDateTime(TimeOnly.FromTimeSpan(time), DateTimeKind.Unspecified);
                        foreach (var candidateUtc in ConvertLocalBoundaryToUtc(local, zone))
                            if (candidateUtc > utc && candidateUtc >= utcDayStart && candidateUtc < utcDayEnd)
                                candidates.Add(candidateUtc);
                    }
                }
            }

            foreach (var candidateUtc in candidates)
            {
                var decision = EvaluateCore(root, candidateUtc, map, new HashSet<string>(StringComparer.Ordinal));
                if (decision.IsAllowed)
                    return new ScheduleDecision
                    {
                        IsAllowed = false,
                        RequestedAtUtc = utc,
                        NextAllowedAtUtc = candidateUtc,
                        Rule = first.Rule,
                        Reason = first.Reason,
                    };
            }
        }

        return new ScheduleDecision
        {
            IsAllowed = false,
            RequestedAtUtc = utc,
            Rule = first.Rule,
            Reason = $"{first.Reason} No allowed instant was found within {maximumSearchDays} days.",
        };
    }

    public DateTime? GetNextAllowedCronOccurrence(string expression, DateTime afterUtc,
        string cronTimeZoneId, string policyName, IReadOnlyCollection<SchedulePolicy> policies,
        int maximumCandidates = 100_000)
    {
        var cursor = EnsureUtc(afterUtc);
        for (var i = 0; i < maximumCandidates; i++)
        {
            var next = CronScheduleCache.GetNextOccurrenceOrDefault(expression, cursor, cronTimeZoneId);
            if (!next.HasValue) return null;
            if (string.IsNullOrWhiteSpace(policyName) || Evaluate(policyName, next.Value, policies).IsAllowed)
                return next;
            // Jump to the next policy transition instead of enumerating every blocked
            // cron tick. This matters for second-level expressions over a weekend or
            // holiday: walking each nominal firing can exhaust the safety bound before
            // reaching the next business window.
            var allowed = FindNextAllowed(policyName, next.Value, policies);
            if (!allowed.NextAllowedAtUtc.HasValue) return null;
            cursor = allowed.NextAllowedAtUtc.Value > DateTime.MinValue
                ? allowed.NextAllowedAtUtc.Value.AddTicks(-1)
                : allowed.NextAllowedAtUtc.Value;
        }
        return null;
    }

    internal IReadOnlyCollection<string> DateProviderNames => _dateProviders.Keys.ToArray();

    private ScheduleDecision EvaluateCore(SchedulePolicy policy, DateTime utc,
        IReadOnlyDictionary<string, SchedulePolicy> policies, HashSet<string> path)
    {
        var key = SchedulePolicyNames.Normalize(policy.Name);
        if (!path.Add(key))
            return Blocked(utc, "PolicyCycle", $"Schedule policy composition contains a cycle at '{policy.Name}'.");

        try
        {
            var zone = ResolveTimeZone(policy.TimeZoneId);
            var local = TimeZoneInfo.ConvertTimeFromUtc(utc, zone);
            var date = DateOnly.FromDateTime(local);
            var weekday = (ScheduleWeekdays)(1 << (int)local.DayOfWeek);
            if ((policy.AllowedWeekdays & weekday) == 0)
                return Blocked(utc, "Weekday", $"{local.DayOfWeek} is outside the allowed weekdays.");

            if (policy.AllowedTimeWindows is { Count: > 0 } &&
                !policy.AllowedTimeWindows.Any(x => x.Contains(local.TimeOfDay)))
                return Blocked(utc, "TimeWindow", "The requested time is outside the allowed daily windows.");

            if (policy.ExcludedDates?.Contains(date) == true)
                return Blocked(utc, "ExcludedDate", $"{date:yyyy-MM-dd} is explicitly excluded.");

            if (policy.ExcludedAnnualDates?.Any(x => x.Month == date.Month && x.Day == date.Day) == true)
                return Blocked(utc, "AnnualDate", $"{date:MMMM dd} is excluded every year.");

            foreach (var providerName in policy.DateProviderNames ?? Enumerable.Empty<string>())
            {
                if (!_dateProviders.TryGetValue(SchedulePolicyNames.Normalize(providerName), out var provider))
                    return Blocked(utc, "MissingDateProvider", $"Date provider '{providerName}' is not registered.");
                if (provider.IsExcluded(date))
                    return Blocked(utc, "DateProvider", provider.GetReason(date));
            }

            foreach (var requiredName in policy.RequiredPolicyNames ?? Enumerable.Empty<string>())
            {
                if (!policies.TryGetValue(SchedulePolicyNames.Normalize(requiredName), out var required))
                    return Blocked(utc, "MissingPolicy", $"Required policy '{requiredName}' does not exist.");
                var requiredDecision = EvaluateCore(required, utc, policies, path);
                if (!requiredDecision.IsAllowed)
                    return Blocked(utc, "RequiredPolicy", $"Required policy '{required.Name}' blocked this time: {requiredDecision.Reason}");
            }

            foreach (var excludedName in policy.ExcludedWhenPolicyAllowsNames ?? Enumerable.Empty<string>())
            {
                if (!policies.TryGetValue(SchedulePolicyNames.Normalize(excludedName), out var excluded))
                    return Blocked(utc, "MissingPolicy", $"Exclusion policy '{excludedName}' does not exist.");
                if (EvaluateCore(excluded, utc, policies, path).IsAllowed)
                    return Blocked(utc, "ExclusionPolicy", $"Exclusion policy '{excluded.Name}' applies at this time.");
            }

            return new ScheduleDecision
            {
                IsAllowed = true,
                RequestedAtUtc = utc,
                NextAllowedAtUtc = utc,
                Rule = "Allowed",
                Reason = "Allowed by the schedule policy.",
            };
        }
        finally
        {
            path.Remove(key);
        }
    }

    private static IReadOnlyList<SchedulePolicy> CollectParticipatingPolicies(SchedulePolicy root,
        IReadOnlyDictionary<string, SchedulePolicy> policies)
    {
        var result = new List<SchedulePolicy>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<SchedulePolicy>();
        pending.Push(root);
        while (pending.TryPop(out var policy))
        {
            if (!visited.Add(SchedulePolicyNames.Normalize(policy.Name))) continue;
            result.Add(policy);
            foreach (var name in (policy.RequiredPolicyNames ?? new())
                         .Concat(policy.ExcludedWhenPolicyAllowsNames ?? new()))
                if (policies.TryGetValue(SchedulePolicyNames.Normalize(name), out var referenced))
                    pending.Push(referenced);
        }
        return result;
    }

    private static IEnumerable<DateTime> ConvertLocalBoundaryToUtc(DateTime local, TimeZoneInfo zone)
    {
        if (zone.IsInvalidTime(local))
        {
            // A window can start inside a daylight-saving gap. In that case its
            // effective start is the first representable local instant after the
            // gap, rather than the window being lost for the entire day.
            var firstValid = local;
            for (var minute = 0; minute < 24 * 60 && zone.IsInvalidTime(firstValid); minute++)
                firstValid = firstValid.AddMinutes(1);
            if (zone.IsInvalidTime(firstValid)) yield break;
            local = firstValid;
        }
        if (zone.IsAmbiguousTime(local))
        {
            foreach (var offset in zone.GetAmbiguousTimeOffsets(local))
                yield return new DateTimeOffset(local, offset).UtcDateTime;
            yield break;
        }
        yield return TimeZoneInfo.ConvertTimeToUtc(local, zone);
    }

    private static Dictionary<string, SchedulePolicy> BuildMap(IEnumerable<SchedulePolicy> policies)
        => (policies ?? Array.Empty<SchedulePolicy>()).ToDictionary(
            x => SchedulePolicyNames.Normalize(x.Name), x => x, StringComparer.Ordinal);

    internal static TimeZoneInfo ResolveTimeZone(string id)
        => TimeZoneInfo.FindSystemTimeZoneById(string.IsNullOrWhiteSpace(id) ? "UTC" : id.Trim());

    private static DateTime EnsureUtc(DateTime value)
        => value.Kind == DateTimeKind.Utc ? value : value.Kind == DateTimeKind.Local
            ? value.ToUniversalTime() : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static ScheduleDecision Blocked(DateTime utc, string rule, string reason) => new()
    {
        IsAllowed = false,
        RequestedAtUtc = EnsureUtc(utc),
        Rule = rule,
        Reason = reason,
    };
}
