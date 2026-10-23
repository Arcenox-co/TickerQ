using TickerQ.Utilities.Infrastructure;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ.Tests;

public sealed class SchedulePolicyEvaluatorTests
{
    private static readonly SchedulePolicyEvaluator Evaluator = new();

    [Fact]
    public void Evaluate_Allows_Only_Configured_Weekdays_And_Local_Window()
    {
        var policy = SchedulePolicy.Create("Business hours")
            .InTimeZone("UTC")
            .AllowWeekdays(DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
                DayOfWeek.Thursday, DayOfWeek.Friday)
            .AllowBetween(new TimeOnly(9, 0), new TimeOnly(17, 0))
            .Build();

        Assert.True(Evaluator.Evaluate(policy.Name,
            new DateTime(2026, 1, 5, 9, 0, 0, DateTimeKind.Utc), [policy]).IsAllowed);
        Assert.Equal("TimeWindow", Evaluator.Evaluate(policy.Name,
            new DateTime(2026, 1, 5, 17, 0, 0, DateTimeKind.Utc), [policy]).Rule);
        Assert.Equal("Weekday", Evaluator.Evaluate(policy.Name,
            new DateTime(2026, 1, 4, 12, 0, 0, DateTimeKind.Utc), [policy]).Rule);
    }

    [Fact]
    public void Evaluate_Supports_Overnight_Windows()
    {
        var policy = SchedulePolicy.Create("Night shift")
            .AllowBetween(new TimeOnly(22, 0), new TimeOnly(6, 0))
            .Build();

        Assert.True(Evaluator.Evaluate(policy.Name,
            new DateTime(2026, 1, 5, 23, 30, 0, DateTimeKind.Utc), [policy]).IsAllowed);
        Assert.True(Evaluator.Evaluate(policy.Name,
            new DateTime(2026, 1, 6, 5, 59, 0, DateTimeKind.Utc), [policy]).IsAllowed);
        Assert.False(Evaluator.Evaluate(policy.Name,
            new DateTime(2026, 1, 6, 6, 0, 0, DateTimeKind.Utc), [policy]).IsAllowed);
    }

    [Fact]
    public void Evaluate_Applies_Exact_Annual_And_Custom_Exclusions()
    {
        var provider = new FixedHolidayProvider(new DateOnly(2026, 4, 3));
        var evaluator = new SchedulePolicyEvaluator([provider]);
        var policy = SchedulePolicy.Create("Office calendar")
            .ExcludeDate(new DateOnly(2026, 2, 10))
            .ExcludeAnnualDate(1, 1)
            .ExcludeDatesFrom(provider.Name)
            .Build();

        Assert.Equal("AnnualDate", evaluator.Evaluate(policy.Name,
            Utc(2026, 1, 1), [policy]).Rule);
        Assert.Equal("ExcludedDate", evaluator.Evaluate(policy.Name,
            Utc(2026, 2, 10), [policy]).Rule);
        Assert.Equal("DateProvider", evaluator.Evaluate(policy.Name,
            Utc(2026, 4, 3), [policy]).Rule);
        Assert.True(evaluator.Evaluate(policy.Name, Utc(2026, 4, 4), [policy]).IsAllowed);
    }

    [Fact]
    public void Evaluate_Composes_Required_And_Exclusion_Policies()
    {
        var business = SchedulePolicy.Create("Business")
            .AllowWeekdays(DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
                DayOfWeek.Thursday, DayOfWeek.Friday)
            .Build();
        var maintenance = SchedulePolicy.Create("Maintenance")
            .AllowWeekdays(DayOfWeek.Wednesday)
            .AllowBetween(new TimeOnly(12, 0), new TimeOnly(13, 0))
            .Build();
        var composed = SchedulePolicy.Create("Production")
            .Require("business")
            .ExceptWhen("MAINTENANCE")
            .Build();

        Assert.True(Evaluator.Evaluate(composed.Name,
            Utc(2026, 1, 5, 12), [business, maintenance, composed]).IsAllowed);
        Assert.Equal("ExclusionPolicy", Evaluator.Evaluate(composed.Name,
            Utc(2026, 1, 7, 12, 30), [business, maintenance, composed]).Rule);
        Assert.Equal("RequiredPolicy", Evaluator.Evaluate(composed.Name,
            Utc(2026, 1, 4, 12), [business, maintenance, composed]).Rule);
    }

    [Fact]
    public void Evaluate_Reports_Missing_Policies_And_Composition_Cycles()
    {
        var missing = SchedulePolicy.Create("Root").Require("Unknown").Build();
        Assert.Equal("MissingPolicy", Evaluator.Evaluate(missing.Name, Utc(2026, 1, 5), [missing]).Rule);

        var first = SchedulePolicy.Create("First").Require("Second").Build();
        var second = SchedulePolicy.Create("Second").Require("First").Build();
        var decision = Evaluator.Evaluate(first.Name, Utc(2026, 1, 5), [first, second]);
        Assert.False(decision.IsAllowed);
        Assert.Contains("cycle", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FindNextAllowed_Moves_After_Weekend_To_Next_Window_Start()
    {
        var policy = SchedulePolicy.Create("Business")
            .AllowWeekdays(DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
                DayOfWeek.Thursday, DayOfWeek.Friday)
            .AllowBetween(new TimeOnly(9, 0), new TimeOnly(17, 0))
            .Build();
        var requested = new DateTime(2026, 1, 2, 18, 0, 0, DateTimeKind.Utc); // Friday

        var decision = Evaluator.FindNextAllowed(policy.Name, requested, [policy]);

        Assert.False(decision.IsAllowed);
        Assert.Equal(new DateTime(2026, 1, 5, 9, 0, 0, DateTimeKind.Utc), decision.NextAllowedAtUtc);
    }

    [Fact]
    public void FindNextAllowed_Uses_Each_Composed_Policy_Time_Zone()
    {
        var newYorkHours = SchedulePolicy.Create("New York hours")
            .InTimeZone("America/New_York")
            .AllowBetween(new TimeOnly(9, 0), new TimeOnly(10, 0))
            .Build();
        var composed = SchedulePolicy.Create("Global operation")
            .InTimeZone("UTC")
            .Require(newYorkHours.Name)
            .Build();

        var decision = Evaluator.FindNextAllowed(composed.Name,
            new DateTime(2026, 1, 5, 12, 0, 0, DateTimeKind.Utc),
            [composed, newYorkHours]);

        Assert.Equal(new DateTime(2026, 1, 5, 14, 0, 0, DateTimeKind.Utc),
            decision.NextAllowedAtUtc);
    }

    [Fact]
    public void FindNextAllowed_Resumes_When_A_Different_Time_Zone_Exclusion_Ends()
    {
        var maintenance = SchedulePolicy.Create("New York maintenance")
            .InTimeZone("America/New_York")
            .AllowBetween(new TimeOnly(9, 0), new TimeOnly(10, 0))
            .Build();
        var composed = SchedulePolicy.Create("Production")
            .ExceptWhen(maintenance.Name)
            .Build();

        var decision = Evaluator.FindNextAllowed(composed.Name,
            new DateTime(2026, 1, 5, 14, 30, 0, DateTimeKind.Utc),
            [composed, maintenance]);

        Assert.Equal(new DateTime(2026, 1, 5, 15, 0, 0, DateTimeKind.Utc),
            decision.NextAllowedAtUtc);
    }

    [Fact]
    public void FindNextAllowed_Uses_The_First_Valid_Instant_When_A_Window_Starts_In_A_Dst_Gap()
    {
        var policy = SchedulePolicy.Create("New York early shift")
            .InTimeZone("America/New_York")
            .AllowBetween(new TimeOnly(2, 0), new TimeOnly(4, 0))
            .Build();

        var decision = Evaluator.FindNextAllowed(policy.Name,
            new DateTime(2026, 3, 8, 6, 0, 0, DateTimeKind.Utc), [policy]);

        // 02:00 does not exist on the spring-forward day. The window begins at
        // 03:00 local, which is 07:00 UTC after the offset changes to UTC-4.
        Assert.Equal(new DateTime(2026, 3, 8, 7, 0, 0, DateTimeKind.Utc),
            decision.NextAllowedAtUtc);
    }

    [Fact]
    public void GetNextAllowedCronOccurrence_Skips_Blocked_Nominal_Firings()
    {
        var policy = SchedulePolicy.Create("Weekdays")
            .AllowWeekdays(DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
                DayOfWeek.Thursday, DayOfWeek.Friday)
            .Build();

        var next = Evaluator.GetNextAllowedCronOccurrence(
            "0 9 * * *", new DateTime(2026, 1, 2, 10, 0, 0, DateTimeKind.Utc),
            "UTC", policy.Name, [policy]);

        Assert.Equal(new DateTime(2026, 1, 5, 9, 0, 0, DateTimeKind.Utc), next);
    }

    [Fact]
    public void GetNextAllowedCronOccurrence_Jumps_A_Second_Level_Schedule_Over_A_Weekend()
    {
        var policy = SchedulePolicy.Create("Business")
            .AllowWeekdays(DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
                DayOfWeek.Thursday, DayOfWeek.Friday)
            .AllowBetween(new TimeOnly(9, 0), new TimeOnly(17, 0))
            .Build();

        var next = Evaluator.GetNextAllowedCronOccurrence(
            "* * * * * *", new DateTime(2026, 1, 2, 18, 0, 0, DateTimeKind.Utc),
            "UTC", policy.Name, [policy], maximumCandidates: 2);

        Assert.Equal(new DateTime(2026, 1, 5, 9, 0, 0, DateTimeKind.Utc), next);
    }

    [Fact]
    public void Evaluate_Uses_The_Policy_Time_Zone()
    {
        var policy = SchedulePolicy.Create("Berlin morning")
            .InTimeZone("Europe/Berlin")
            .AllowBetween(new TimeOnly(9, 0), new TimeOnly(10, 0))
            .Build();

        // January is UTC+1 in Berlin, so 08:30 UTC is 09:30 local.
        Assert.True(Evaluator.Evaluate(policy.Name,
            new DateTime(2026, 1, 5, 8, 30, 0, DateTimeKind.Utc), [policy]).IsAllowed);
        Assert.False(Evaluator.Evaluate(policy.Name,
            new DateTime(2026, 1, 5, 9, 30, 0, DateTimeKind.Utc), [policy]).IsAllowed);
    }

    [Fact]
    public void SourceGenerated_Serializer_RoundTrips_The_Complete_Definition()
    {
        var policy = SchedulePolicy.Create("Operations")
            .WithDescription("All supported rule types")
            .InTimeZone("Europe/Berlin")
            .AllowWeekdays(DayOfWeek.Monday, DayOfWeek.Friday)
            .AllowBetween(new TimeOnly(22, 0), new TimeOnly(6, 0))
            .ExcludeDate(new DateOnly(2026, 4, 3))
            .ExcludeAnnualDate(1, 1)
            .ExcludeDatesFrom("Public holidays")
            .Require("Business days")
            .ExceptWhen("Maintenance")
            .Build();
        policy.ApplicationNamespaceKey = "namespace";
        policy.NameKey = SchedulePolicyNames.CreateKey(policy.Name);
        policy.CreatedAtUtc = new DateTime(2026, 1, 1, 1, 2, 3, DateTimeKind.Utc);
        policy.UpdatedAtUtc = new DateTime(2026, 1, 2, 1, 2, 3, DateTimeKind.Utc);
        policy.Version = 7;

        var roundTrip = SchedulePolicySerializer.Deserialize(SchedulePolicySerializer.Serialize(policy));

        Assert.NotNull(roundTrip);
        Assert.Equal(policy.Id, roundTrip.Id);
        Assert.Equal(policy.ApplicationNamespaceKey, roundTrip.ApplicationNamespaceKey);
        Assert.Equal(policy.NameKey, roundTrip.NameKey);
        Assert.Equal(policy.Description, roundTrip.Description);
        Assert.Equal(policy.TimeZoneId, roundTrip.TimeZoneId);
        Assert.Equal(policy.AllowedWeekdays, roundTrip.AllowedWeekdays);
        Assert.Equal(policy.AllowedTimeWindows[0].Start, roundTrip.AllowedTimeWindows[0].Start);
        Assert.Equal(policy.AllowedTimeWindows[0].End, roundTrip.AllowedTimeWindows[0].End);
        Assert.Equal(policy.ExcludedDates, roundTrip.ExcludedDates);
        Assert.Equal(policy.ExcludedAnnualDates[0].Month, roundTrip.ExcludedAnnualDates[0].Month);
        Assert.Equal(policy.ExcludedAnnualDates[0].Day, roundTrip.ExcludedAnnualDates[0].Day);
        Assert.Equal(policy.DateProviderNames, roundTrip.DateProviderNames);
        Assert.Equal(policy.RequiredPolicyNames, roundTrip.RequiredPolicyNames);
        Assert.Equal(policy.ExcludedWhenPolicyAllowsNames, roundTrip.ExcludedWhenPolicyAllowsNames);
        Assert.Equal(policy.CreatedAtUtc, roundTrip.CreatedAtUtc);
        Assert.Equal(policy.UpdatedAtUtc, roundTrip.UpdatedAtUtc);
        Assert.Equal(policy.Version, roundTrip.Version);
    }

    private static DateTime Utc(int year, int month, int day, int hour = 0, int minute = 0)
        => new(year, month, day, hour, minute, 0, DateTimeKind.Utc);

    private sealed class FixedHolidayProvider(DateOnly holiday) : IScheduleDateProvider
    {
        public string Name => "Public holidays";
        public bool IsExcluded(DateOnly localDate) => localDate == holiday;
        public string GetReason(DateOnly localDate) => $"{localDate:yyyy-MM-dd} is a public holiday.";
    }
}
