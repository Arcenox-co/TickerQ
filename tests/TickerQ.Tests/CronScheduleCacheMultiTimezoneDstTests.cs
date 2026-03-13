using System;
using TickerQ.Utilities;
using Xunit;

namespace TickerQ.Tests;

public class CronScheduleCacheMultiTimezoneDstTests : IDisposable
{
    private readonly TimeZoneInfo _originalTimeZone;

    public CronScheduleCacheMultiTimezoneDstTests()
    {
        _originalTimeZone = CronScheduleCache.TimeZoneInfo;
    }

    public void Dispose()
    {
        CronScheduleCache.TimeZoneInfo = _originalTimeZone;
    }

    private static TimeZoneInfo FindTimeZone(string ianaId, string windowsId)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(ianaId);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById(windowsId);
        }
    }

    [Fact]
    public void EuropeLondon_SpringForward_SkipsGapAndReturnsValidTime()
    {
        // Europe/London springs forward last Sunday of March: 1:00 AM -> 2:00 AM
        var london = FindTimeZone("Europe/London", "GMT Standard Time");
        CronScheduleCache.TimeZoneInfo = london;

        // Cron: every day at 1:30 AM -- this time doesn't exist on spring-forward day
        var expression = "0 30 1 * * *";
        CronScheduleCache.Invalidate(expression);

        // March 30, 2025 is the last Sunday of March (spring-forward in London)
        // At 1:00 AM clocks jump to 2:00 AM, so 1:30 AM doesn't exist
        // Use a UTC time that is before the gap: March 30, 2025 00:30 UTC = 00:30 GMT
        var beforeGap = new DateTime(2025, 3, 30, 0, 30, 0, DateTimeKind.Utc);

        var result = CronScheduleCache.GetNextOccurrenceOrDefault(expression, beforeGap);

        Assert.NotNull(result);
        Assert.Equal(DateTimeKind.Utc, result!.Value.Kind);

        // The gap is 1:00 AM -> 2:00 AM UTC+0 to UTC+1
        // So 1:00 AM GMT = 1:00 UTC, clocks jump to 2:00 BST = 1:00 UTC
        // Result should be a valid UTC time after the gap
        Assert.True(result.Value > beforeGap, "Result should be after the input time");
    }

    [Fact]
    public void EuropeBerlin_SpringForward_HandlesGapAt2AM()
    {
        // Europe/Berlin springs forward last Sunday of March: 2:00 AM -> 3:00 AM
        var berlin = FindTimeZone("Europe/Berlin", "W. Europe Standard Time");
        CronScheduleCache.TimeZoneInfo = berlin;

        // Cron: every day at 2:30 AM -- this time doesn't exist on spring-forward day
        var expression = "0 30 2 * * *";
        CronScheduleCache.Invalidate(expression);

        // March 30, 2025 is spring-forward in Berlin
        // 2:00 AM CET jumps to 3:00 AM CEST
        // 2:00 AM CET = 1:00 AM UTC, so use a UTC time before that
        var beforeGap = new DateTime(2025, 3, 30, 0, 0, 0, DateTimeKind.Utc);

        var result = CronScheduleCache.GetNextOccurrenceOrDefault(expression, beforeGap);

        Assert.NotNull(result);
        Assert.Equal(DateTimeKind.Utc, result!.Value.Kind);

        // The result should be after the gap ends
        // Gap ends at 3:00 AM CEST = 1:00 AM UTC
        Assert.True(result.Value >= new DateTime(2025, 3, 30, 1, 0, 0, DateTimeKind.Utc),
            "Result should be at or after the end of the DST gap");
    }

    [Fact]
    public void AustraliaSydney_FallBack_HandlesOverlapAt2AM()
    {
        // Australia/Sydney falls back first Sunday of April: 3:00 AM -> 2:00 AM
        var sydney = FindTimeZone("Australia/Sydney", "AUS Eastern Standard Time");
        CronScheduleCache.TimeZoneInfo = sydney;

        // Cron: every day at 2:30 AM -- this time occurs twice on fall-back day
        var expression = "0 30 2 * * *";
        CronScheduleCache.Invalidate(expression);

        // April 6, 2025 is the first Sunday of April (fall-back in Sydney)
        // 3:00 AM AEDT -> 2:00 AM AEST
        // 3:00 AM AEDT = 16:00 UTC (Apr 5), so use a time before that
        var beforeOverlap = new DateTime(2025, 4, 5, 15, 0, 0, DateTimeKind.Utc);

        var result = CronScheduleCache.GetNextOccurrenceOrDefault(expression, beforeOverlap);

        Assert.NotNull(result);
        Assert.Equal(DateTimeKind.Utc, result!.Value.Kind);

        // Should return a valid time - the method should not crash during the overlap
        Assert.True(result.Value > beforeOverlap, "Result should be after the input time");
    }

    [Fact]
    public void AsiaTokyo_NoDst_AlwaysReturnsValidTimes()
    {
        // Asia/Tokyo has no DST transitions
        var tokyo = FindTimeZone("Asia/Tokyo", "Tokyo Standard Time");
        CronScheduleCache.TimeZoneInfo = tokyo;

        var expression = "0 30 2 * * *";
        CronScheduleCache.Invalidate(expression);

        // Pick a date that would be DST transition in other zones (March 30, 2025)
        var utcTime = new DateTime(2025, 3, 30, 0, 0, 0, DateTimeKind.Utc);

        var result = CronScheduleCache.GetNextOccurrenceOrDefault(expression, utcTime);

        Assert.NotNull(result);
        Assert.Equal(DateTimeKind.Utc, result!.Value.Kind);

        // 2:30 AM JST = 17:30 UTC (previous day) since JST is UTC+9
        // From March 30 00:00 UTC = March 30 09:00 JST,
        // next occurrence of 2:30 AM JST is March 31 02:30 JST = March 30 17:30 UTC
        var expected = new DateTime(2025, 3, 30, 17, 30, 0, DateTimeKind.Utc);
        Assert.Equal(expected, result.Value);
    }

    [Fact]
    public void Utc_NoDst_AlwaysReturnsValidTimes()
    {
        CronScheduleCache.TimeZoneInfo = TimeZoneInfo.Utc;

        var expression = "0 30 2 * * *";
        CronScheduleCache.Invalidate(expression);

        var utcTime = new DateTime(2025, 3, 30, 0, 0, 0, DateTimeKind.Utc);

        var result = CronScheduleCache.GetNextOccurrenceOrDefault(expression, utcTime);

        Assert.NotNull(result);
        Assert.Equal(DateTimeKind.Utc, result!.Value.Kind);

        // Next 2:30 AM UTC after March 30 00:00 UTC is March 30 02:30 UTC
        var expected = new DateTime(2025, 3, 30, 2, 30, 0, DateTimeKind.Utc);
        Assert.Equal(expected, result.Value);
    }

    [Fact]
    public void CronExpressionAvoidingDstGap_WorksFineRegardlessOfDst()
    {
        // "0 0 5 * * *" = every day at 5:00 AM, which is outside any DST gap
        var expression = "0 0 5 * * *";

        // Test across multiple timezones on their DST transition dates
        var timeZones = new[]
        {
            FindTimeZone("America/New_York", "Eastern Standard Time"),
            FindTimeZone("Europe/London", "GMT Standard Time"),
            FindTimeZone("Europe/Berlin", "W. Europe Standard Time"),
        };

        // March 30, 2025 has DST transitions in Europe
        var utcTime = new DateTime(2025, 3, 30, 0, 0, 0, DateTimeKind.Utc);

        foreach (var tz in timeZones)
        {
            CronScheduleCache.TimeZoneInfo = tz;
            CronScheduleCache.Invalidate(expression);

            var result = CronScheduleCache.GetNextOccurrenceOrDefault(expression, utcTime);

            Assert.NotNull(result);
            Assert.Equal(DateTimeKind.Utc, result!.Value.Kind);
            Assert.True(result.Value > utcTime,
                $"5:00 AM cron should return a future time for timezone {tz.Id}");
        }
    }
}
