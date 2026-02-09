using System;
using FluentAssertions;
using TickerQ.Utilities;
using Xunit;

namespace TickerQ.Tests;

public class CronScheduleCacheDstTests
{
    [Fact]
    public void GetNextOccurrenceOrDefault_Handles_DST_Gap_Without_Throwing()
    {
        // Use US Eastern which has a spring-forward gap (2:00 AM → 3:00 AM)
        var eastern = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        CronScheduleCache.TimeZoneInfo = eastern;

        // Cron: every day at 2:30 AM — this time doesn't exist on spring-forward day
        var expression = "0 30 2 * * *";

        // March 9, 2025 is spring-forward in US Eastern (2:00 AM → 3:00 AM)
        var beforeGap = new DateTime(2025, 3, 9, 6, 0, 0, DateTimeKind.Utc); // 1:00 AM ET

        var result = CronScheduleCache.GetNextOccurrenceOrDefault(expression, beforeGap);

        // Should not crash, and should return a valid UTC time after the gap
        result.Should().NotBeNull();
        result!.Value.Kind.Should().Be(DateTimeKind.Utc);

        // The result should be after the gap (which ends at 3:00 AM ET = 7:00 AM UTC)
        result.Value.Should().BeOnOrAfter(new DateTime(2025, 3, 9, 7, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void GetNextOccurrenceOrDefault_Returns_Correct_Time_Outside_DST_Gap()
    {
        CronScheduleCache.TimeZoneInfo = TimeZoneInfo.Utc;

        // Every minute
        var expression = "0 * * * * *";
        var now = new DateTime(2025, 6, 15, 12, 30, 0, DateTimeKind.Utc);

        var result = CronScheduleCache.GetNextOccurrenceOrDefault(expression, now);

        result.Should().NotBeNull();
        result!.Value.Should().Be(new DateTime(2025, 6, 15, 12, 31, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void GetNextOccurrenceOrDefault_Returns_Null_For_Unparseable_Expression()
    {
        CronScheduleCache.TimeZoneInfo = TimeZoneInfo.Utc;

        var result = CronScheduleCache.GetNextOccurrenceOrDefault("not a cron", DateTime.UtcNow);

        result.Should().BeNull();
    }
}
