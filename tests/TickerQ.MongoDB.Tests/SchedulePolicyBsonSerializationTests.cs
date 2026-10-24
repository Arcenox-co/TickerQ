using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using TickerQ.MongoDB.Serialization;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Models;

namespace TickerQ.MongoDB.Tests;

public sealed class SchedulePolicyBsonSerializationTests
{
    [Fact]
    public void SchedulePolicy_RoundTrips_DateOnly_TimeSpan_And_Composition()
    {
        TickerClassMaps.RegisterOnce<TestTimeTicker, TestCronTicker>();
        var source = SchedulePolicy.Create("Production calendar")
            .InTimeZone("Europe/Berlin")
            .AllowWeekdays(DayOfWeek.Monday, DayOfWeek.Tuesday)
            .AllowBetween(new TimeOnly(9, 15), new TimeOnly(17, 45))
            .ExcludeDate(new DateOnly(2026, 12, 31))
            .ExcludeAnnualDate(1, 1)
            .ExcludeDatesFrom("Public holidays")
            .Require("Business hours")
            .ExceptWhen("Maintenance")
            .Build();

        var document = source.ToBsonDocument();
        var roundTrip = BsonSerializer.Deserialize<SchedulePolicy>(document);

        Assert.Equal(source.Name, roundTrip.Name);
        Assert.Equal(new DateOnly(2026, 12, 31), Assert.Single(roundTrip.ExcludedDates));
        Assert.Equal(new TimeSpan(9, 15, 0), Assert.Single(roundTrip.AllowedTimeWindows).Start);
        Assert.Equal("Business hours", Assert.Single(roundTrip.RequiredPolicyNames));
        Assert.Equal("Maintenance", Assert.Single(roundTrip.ExcludedWhenPolicyAllowsNames));
    }

    private sealed class TestTimeTicker : TimeTickerEntity<TestTimeTicker>;
    private sealed class TestCronTicker : CronTickerEntity;
}
