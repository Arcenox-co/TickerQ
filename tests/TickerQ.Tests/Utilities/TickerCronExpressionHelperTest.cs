using FluentAssertions;
using TickerQ.Utilities;

namespace TickerQ.Tests.Utilities;

public class TickerCronExpressionHelperTest
{
    [Fact]
    public void Test_Every5Minutes()
    {
        string result = TickerCronExpressionHelper.ToHumanReadable("*/5 * * * *");
        result.Should().Be("Every 5 minutes");
    }

    [Fact]
    public void Test_Every2Hours()
    {
        string result = TickerCronExpressionHelper.ToHumanReadable("0 */2 * * *");
        result.Should().Be("Every 2 hours");
    }

    [Fact]
    public void Test_Every2HoursOnWeekdays()
    {
        string result = TickerCronExpressionHelper.ToHumanReadable("0 */2 * * 1,2,3,4,5");
        result.Should().Be("Every 2 hours on Mon, Tue, Wed, Thu and Fri");
    }

    [Fact]
    public void Test_EveryDayAtSpecificTime()
    {
        string result = TickerCronExpressionHelper.ToHumanReadable("30 14 * * *");
        result.Should().Be("Every day at 14:30");
    }

    [Fact]
    public void Test_EveryMondayAtTime()
    {
        string result = TickerCronExpressionHelper.ToHumanReadable("15 10 * * 1");
        result.Should().Be("Every Mon at 10:15");
    }

    [Fact]
    public void Test_Every27thAtTime()
    {
        string result = TickerCronExpressionHelper.ToHumanReadable("0 4 27 * ?");
        result.Should().Be("Every month on the 27th at 04:00");
    }

    [Fact]
    public void Test_Every6MonthsOn27th()
    {
        string result = TickerCronExpressionHelper.ToHumanReadable("0 4 27 */6 ?");
        result.Should().Be("Every 6 months on the 27th at 04:00");
    }

    [Fact]
    public void Test_SpecificDayAndWeek()
    {
        string result = TickerCronExpressionHelper.ToHumanReadable("0 23 15 * 1");
        result.Should().Be("On 15th and Mon at 23:00");
    }

    [Fact]
    public void Test_WithTimeZone()
    {
        TimeZoneInfo timeZone = TimeZoneInfo.FindSystemTimeZoneById("W. Central Africa Standard Time");
        string result = TickerCronExpressionHelper.ToHumanReadable("0 4 * * *", timeZone);
        result.Should().Be("Every day at 05:00");
    }

    [Fact]
    public void Test_Cron6PartExpression()
    {
        string result = TickerCronExpressionHelper.ToHumanReadable("0 15 10 * * ?", null);
        result.Should().Be("Every day at 10:15");
    }

    [Fact]
    public void Test_Cron7PartExpression()
    {
        string result = TickerCronExpressionHelper.ToHumanReadable("0 0 12 1/1 * ? *", null);
        result.Should().Be("Every month on the 1st at 12:00");
    }
}
