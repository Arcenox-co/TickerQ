using System;
using TickerQ.Dashboard.Endpoints;

namespace TickerQ.Tests;

/// <summary>
/// Tests for ToIanaTimeZoneId — ensures the dashboard API always returns
/// IANA timezone identifiers regardless of the host OS.
/// </summary>
public class DashboardTimeZoneTests
{
    [Fact]
    public void ToIanaTimeZoneId_NullTimeZone_ReturnsNull()
    {
        var result = DashboardEndpoints.ToIanaTimeZoneId(null);
        Assert.Null(result);
    }

    [Fact]
    public void ToIanaTimeZoneId_UtcTimeZone_ReturnsUtc()
    {
        var result = DashboardEndpoints.ToIanaTimeZoneId(TimeZoneInfo.Utc);
        Assert.Equal("UTC", result);
    }

    [Fact]
    public void ToIanaTimeZoneId_IanaId_ReturnedUnchanged()
    {
        // On Linux/macOS, FindSystemTimeZoneById with IANA ID returns IANA ID directly.
        // On Windows with .NET 6+, it also supports IANA IDs.
        var tz = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var result = DashboardEndpoints.ToIanaTimeZoneId(tz);

        // Should contain '/' indicating IANA format
        Assert.Contains("/", result);
        Assert.Equal("America/New_York", result);
    }

    [Fact]
    public void ToIanaTimeZoneId_WindowsId_ConvertedToIana()
    {
        // "Eastern Standard Time" is the Windows ID for US Eastern
        var tz = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        var result = DashboardEndpoints.ToIanaTimeZoneId(tz);

        // Should be converted to an IANA ID (America/New_York on most systems)
        Assert.NotNull(result);
        Assert.NotEqual("Eastern Standard Time", result);
        Assert.Contains("/", result);
    }

    [Fact]
    public void ToIanaTimeZoneId_PacificStandardTime_ConvertedToIana()
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");
        var result = DashboardEndpoints.ToIanaTimeZoneId(tz);

        Assert.NotNull(result);
        Assert.NotEqual("Pacific Standard Time", result);
        Assert.Contains("/", result);
    }

    [Fact]
    public void ToIanaTimeZoneId_CentralEuropeanStandardTime_ConvertedToIana()
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById("Central European Standard Time");
        var result = DashboardEndpoints.ToIanaTimeZoneId(tz);

        Assert.NotNull(result);
        Assert.NotEqual("Central European Standard Time", result);
        Assert.Contains("/", result);
    }

    [Fact]
    public void ToIanaTimeZoneId_TokyoStandardTime_ConvertedToIana()
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById("Tokyo Standard Time");
        var result = DashboardEndpoints.ToIanaTimeZoneId(tz);

        Assert.NotNull(result);
        Assert.NotEqual("Tokyo Standard Time", result);
        Assert.Contains("/", result);
    }

    [Fact]
    public void ToIanaTimeZoneId_LocalTimeZone_ReturnsNonNull()
    {
        // Regardless of host OS, local timezone should produce a non-null result
        var result = DashboardEndpoints.ToIanaTimeZoneId(TimeZoneInfo.Local);

        Assert.NotNull(result);
        Assert.NotEmpty(result);
    }
}
