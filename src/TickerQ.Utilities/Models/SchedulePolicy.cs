using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using TickerQ.Utilities.Enums;

namespace TickerQ.Utilities.Models;

[Flags]
public enum ScheduleWeekdays
{
    None = 0,
    Sunday = 1 << 0,
    Monday = 1 << 1,
    Tuesday = 1 << 2,
    Wednesday = 1 << 3,
    Thursday = 1 << 4,
    Friday = 1 << 5,
    Saturday = 1 << 6,
    All = Sunday | Monday | Tuesday | Wednesday | Thursday | Friday | Saturday,
    MondayToFriday = Monday | Tuesday | Wednesday | Thursday | Friday,
    Weekend = Saturday | Sunday,
}

/// <summary>A local-time interval allowed by a schedule policy. End is exclusive.</summary>
public sealed class ScheduleTimeWindow
{
    public TimeSpan Start { get; set; }
    public TimeSpan End { get; set; }

    public ScheduleTimeWindow() { }

    public ScheduleTimeWindow(TimeOnly start, TimeOnly end)
    {
        Start = start.ToTimeSpan();
        End = end.ToTimeSpan();
    }

    internal bool Contains(TimeSpan time)
        => Start == End || (Start < End ? time >= Start && time < End : time >= Start || time < End);
}

/// <summary>A month/day pair that repeats every year.</summary>
public sealed class ScheduleAnnualDate
{
    public int Month { get; set; }
    public int Day { get; set; }

    public ScheduleAnnualDate() { }
    public ScheduleAnnualDate(int month, int day)
    {
        Month = month;
        Day = day;
    }
}

/// <summary>
/// Named, reusable availability policy shared by cron schedules and one-time jobs.
/// Policies are scoped to the configured TickerQ application namespace.
/// </summary>
public sealed class SchedulePolicy
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ApplicationNamespaceKey { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string NameKey { get; set; } = string.Empty;
    public string Description { get; set; }
    public string TimeZoneId { get; set; } = "UTC";
    public ScheduleWeekdays AllowedWeekdays { get; set; } = ScheduleWeekdays.All;
    public List<ScheduleTimeWindow> AllowedTimeWindows { get; set; } = new();
    public List<DateOnly> ExcludedDates { get; set; } = new();
    public List<ScheduleAnnualDate> ExcludedAnnualDates { get; set; } = new();
    public List<string> DateProviderNames { get; set; } = new();
    /// <summary>Every referenced policy must also allow the candidate instant.</summary>
    public List<string> RequiredPolicyNames { get; set; } = new();
    /// <summary>The candidate is blocked whenever any referenced policy allows it.</summary>
    public List<string> ExcludedWhenPolicyAllowsNames { get; set; } = new();
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public long Version { get; set; } = 1;

    public SchedulePolicy Copy() => new()
    {
        Id = Id,
        ApplicationNamespaceKey = ApplicationNamespaceKey,
        Name = Name,
        NameKey = NameKey,
        Description = Description,
        TimeZoneId = TimeZoneId,
        AllowedWeekdays = AllowedWeekdays,
        AllowedTimeWindows = AllowedTimeWindows?.Select(x => new ScheduleTimeWindow
            { Start = x.Start, End = x.End }).ToList() ?? new(),
        ExcludedDates = ExcludedDates?.ToList() ?? new(),
        ExcludedAnnualDates = ExcludedAnnualDates?.Select(x => new ScheduleAnnualDate(x.Month, x.Day)).ToList() ?? new(),
        DateProviderNames = DateProviderNames?.ToList() ?? new(),
        RequiredPolicyNames = RequiredPolicyNames?.ToList() ?? new(),
        ExcludedWhenPolicyAllowsNames = ExcludedWhenPolicyAllowsNames?.ToList() ?? new(),
        CreatedAtUtc = CreatedAtUtc,
        UpdatedAtUtc = UpdatedAtUtc,
        Version = Version,
    };

    public static SchedulePolicyBuilder Create(string name) => new(name);
}

public static class SchedulePolicyNames
{
    public static string Normalize(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;
        var collapsed = string.Join(' ', name.Split((char[])null, StringSplitOptions.RemoveEmptyEntries));
        return collapsed.Normalize(NormalizationForm.FormKC).ToUpperInvariant();
    }

    public static string CreateKey(string name)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Normalize(name))));
}

public sealed class DuplicateSchedulePolicyNameException(string name)
    : InvalidOperationException($"A schedule policy named ‘{name}’ already exists.");

/// <summary>Fluent definition builder used by code-first policy registration and tests.</summary>
public sealed class SchedulePolicyBuilder
{
    private readonly SchedulePolicy _policy;

    internal SchedulePolicyBuilder(string name)
    {
        _policy = new SchedulePolicy { Name = name };
    }

    public SchedulePolicyBuilder WithDescription(string description) { _policy.Description = description; return this; }
    public SchedulePolicyBuilder InTimeZone(string timeZoneId) { _policy.TimeZoneId = timeZoneId; return this; }

    public SchedulePolicyBuilder AllowWeekdays(params DayOfWeek[] days)
    {
        _policy.AllowedWeekdays = ScheduleWeekdays.None;
        foreach (var day in days ?? Array.Empty<DayOfWeek>())
            _policy.AllowedWeekdays |= (ScheduleWeekdays)(1 << (int)day);
        return this;
    }

    public SchedulePolicyBuilder AllowBetween(TimeOnly start, TimeOnly end)
    {
        _policy.AllowedTimeWindows.Add(new ScheduleTimeWindow(start, end));
        return this;
    }

    public SchedulePolicyBuilder ExcludeDate(DateOnly date) { _policy.ExcludedDates.Add(date); return this; }
    public SchedulePolicyBuilder ExcludeAnnualDate(int month, int day)
    {
        _policy.ExcludedAnnualDates.Add(new ScheduleAnnualDate(month, day));
        return this;
    }
    public SchedulePolicyBuilder ExcludeDatesFrom(string providerName) { _policy.DateProviderNames.Add(providerName); return this; }
    public SchedulePolicyBuilder Require(string policyName) { _policy.RequiredPolicyNames.Add(policyName); return this; }
    public SchedulePolicyBuilder ExceptWhen(string policyName) { _policy.ExcludedWhenPolicyAllowsNames.Add(policyName); return this; }
    public SchedulePolicy Build() => _policy.Copy();
    public static implicit operator SchedulePolicy(SchedulePolicyBuilder builder) => builder.Build();
}

public sealed class ScheduleDecision
{
    public bool IsAllowed { get; init; }
    public DateTime RequestedAtUtc { get; init; }
    public DateTime? NextAllowedAtUtc { get; init; }
    public string Rule { get; init; }
    public string Reason { get; init; }
}

public sealed class SchedulePolicyPreviewItem
{
    public DateTime RequestedAtUtc { get; init; }
    public DateTime? EffectiveAtUtc { get; init; }
    public bool IsAllowed { get; init; }
    public string Rule { get; init; }
    public string Reason { get; init; }
}

/// <summary>Strongly typed cron schedule overload; string overloads remain supported.</summary>
public sealed class CronSchedule
{
    public string Expression { get; private init; }
    public string TimeZoneId { get; private set; }
    public string SchedulePolicyName { get; private set; }
    public MissedExecutionPolicy MissedExecution { get; private set; } = MissedExecutionPolicy.RunOnce();
    public CronCatchUpMode CatchUpMode { get; private set; } = CronCatchUpMode.Skip;

    public static CronSchedule From(string expression) => new() { Expression = expression };
    public CronSchedule InTimeZone(string timeZoneId) { TimeZoneId = timeZoneId; return this; }
    public CronSchedule UsingPolicy(string policyName) { SchedulePolicyName = policyName; return this; }
    public CronSchedule WhenMissed(MissedExecutionPolicy policy)
    {
        MissedExecution = policy ?? throw new ArgumentNullException(nameof(policy));
        return this;
    }
    public CronSchedule CatchUp(CronCatchUpMode mode)
    {
        if (!Enum.IsDefined(typeof(CronCatchUpMode), mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        CatchUpMode = mode;
        return this;
    }
}

/// <summary>Strongly typed one-time schedule that preserves the originally requested instant.</summary>
public sealed class TimeSchedule
{
    public DateTimeOffset ExecutionTime { get; private init; }
    public string SchedulePolicyName { get; private set; }
    public BlockedOccurrenceBehavior BlockedBehavior { get; private set; } = BlockedOccurrenceBehavior.Reject;
    public MissedExecutionPolicy MissedExecution { get; private set; } = MissedExecutionPolicy.RunOnce();

    public static TimeSchedule At(DateTimeOffset executionTime) => new() { ExecutionTime = executionTime };
    public TimeSchedule UsingPolicy(string policyName) { SchedulePolicyName = policyName; return this; }
    public TimeSchedule WhenBlocked(BlockedOccurrenceBehavior behavior) { BlockedBehavior = behavior; return this; }
    public TimeSchedule WhenMissed(MissedExecutionPolicy policy)
    {
        MissedExecution = policy ?? throw new ArgumentNullException(nameof(policy));
        return this;
    }
}
