using NSubstitute;
using TickerQ.Utilities.DashboardDtos;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Infrastructure;
using TickerQ.Utilities.Interfaces;

namespace TickerQ.Tests;

public sealed class DashboardOverviewNavigationTests
{
    [Fact]
    public async Task Upcoming_feed_identifies_cron_parent_without_confusing_occurrence_and_time_ids()
    {
        var sharedId = Guid.NewGuid();
        var cron = new CronTickerEntity { Function = "cron-function", Expression = "0 * * * * *" };
        List<TimeTickerEntity> time = [new() { Id = sharedId, Function = "time-function", ExecutionTime = DateTime.UtcNow.AddHours(2) }];
        List<CronTickerOccurrenceEntity<CronTickerEntity>> occurrences = [new()
        {
            Id = sharedId, CronTickerId = cron.Id, Status = TickerStatus.Queued,
            ExecutionTime = DateTime.UtcNow.AddHours(1)
        }];
        var provider = Substitute.For<ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity>>();
        provider.TimeTickersQuery().Returns(_ => new InMemoryTickerQueryable<TimeTickerEntity>(_ => Task.FromResult(time)));
        provider.CronTickerOccurrencesQuery().Returns(_ => new InMemoryTickerQueryable<CronTickerOccurrenceEntity<CronTickerEntity>>(_ => Task.FromResult(occurrences)));
        provider.GetCronTickerById(cron.Id, Arg.Any<CancellationToken>()).Returns(cron);

        var upcoming = await new TickerDashboardDataService<TimeTickerEntity, CronTickerEntity>(provider).GetUpcomingTickersAsync(10);

        Assert.Equal(2, upcoming.Count);
        Assert.Equal(ExecutionType.CronOccurrence, upcoming[0].Type);
        Assert.Equal(sharedId, upcoming[0].Id);
        Assert.Equal(cron.Id, upcoming[0].CronTickerId);
        Assert.Equal("cron-function", upcoming[0].FunctionName);
        Assert.Equal(ExecutionType.TimeTicker, upcoming[1].Type);
        Assert.Equal(sharedId, upcoming[1].Id);
        Assert.Null(upcoming[1].CronTickerId);
    }
}
